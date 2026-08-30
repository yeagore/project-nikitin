using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

/// <summary>
/// Dev harness for island generation. Open <c>scenes/dev/island_lab.tscn</c> and
/// run it (F6). Edit the <see cref="Params"/> resource (or <see cref="Seed"/>)
/// in the running scene's remote inspector — the island rebuilds automatically
/// when a value changes.
///
/// Camera: WASD move, Q/E or middle-mouse-drag rotate, middle-drag or up/down
/// arrows tilt (far enough to look at the keel from below), wheel zoom, Shift
/// for faster pan (see <see cref="CameraRig"/>). <b>N</b> rolls a new seed,
/// <b>F</b> re-frames the island, <b>R</b> forces a rebuild of the same one, and
/// <b>F1</b> shows the full key list.
///
/// Renders one scaled <c>MultiMesh</c> box per span (keel → surface), in slab
/// units — no mesher, no per-face culling; that comes later. NOT a <c>[Tool]</c>
/// script: generating in-editor bakes the MultiMesh buffer into the scene file.
/// </summary>
public partial class IslandLab : Node3D
{
	[Export] public int Seed { get; set; } = 1337;
	[Export] public IslandParams Params { get; set; } = null!;

	private MultiMeshInstance3D _terrain = null!;
	private MultiMeshInstance3D _water = null!;
	private MultiMeshInstance3D _falls = null!;
	private MultiMeshInstance3D _gates = null!;
	private MultiMeshInstance3D _marks = null!;
	private BoxMesh _gateBox = null!;
	private BoxMesh _markBox = null!;
	private CameraRig _rig = null!;
	private BoxMesh _unitBox = null!;
	private PlaneMesh _waterQuad = null!;
	private PlaneMesh _fallQuad = null!;
	private readonly List<Label3D> _compass = new();
	private int _lastSignature;

	private Vector3 _islandCenter = Vector3.Zero;
	private float _islandRadius = 10f;
	private bool _framedOnce;

	private enum View
	{
		/// <summary>Height-tinted, the default look.</summary>
		Height,
		/// <summary>Flat colour per landform.</summary>
		Landform,
		/// <summary>The patchwork itself.</summary>
		Region,
		/// <summary>What connects to what on foot.</summary>
		Walk,
		/// <summary>What connects to what once stairs and bridges are built.</summary>
		Reach,
		/// <summary>Ground level enough to build a settlement on.</summary>
		Shelves,
	}

	private static readonly int ViewCount = Enum.GetValues<View>().Length;

	/// <summary>What each view is answering, in one line, next to the picture.</summary>
	private static string ViewLegend(View view) => view switch
	{
		View.Height => "height    low ground dark, high ground pale",
		View.Landform => "landform  green plain / dark hills / grey mountain / "
					   + "brown mesa / blue basin; yellow = pass",
		View.Region => "region    one hue per patch, borders darkened",
		View.Walk => "walk      what you can cross on foot: green mainland, "
				   + "a hue per other district, grey = broken ground",
		View.Reach => "reach     what you can cross once built: green heartland, "
					+ "red = out of reach whatever you build",
		_ => "shelves   ground you could settle on; dim brown = level but too "
		   + "small or too narrow",
	};

	private View _view = View.Height;

	// Overlays. Each answers a question about the island that the terrain itself
	// does not show: where you could build across, where a vessel could set down,
	// and which way is north.
	private bool _showBridges;
	private bool _showAirstrips;
	private bool _showCompass = true;
	private bool _showHelp;

	private Label _status = null!;
	private Label _legend = null!;
	private Label _help = null!;
	private Label _overlays = null!;
	private Label _hint = null!;

	/// <summary>Flat colours for the landform view, so landforms read as landforms.</summary>
	private static Color LandformColor(LandformType type) => type switch
	{
		LandformType.Plain => new Color(0.45f, 0.60f, 0.28f),
		LandformType.Hills => new Color(0.30f, 0.44f, 0.20f),
		LandformType.Mountain => new Color(0.52f, 0.50f, 0.55f),
		LandformType.Mesa => new Color(0.68f, 0.45f, 0.26f),
		LandformType.Basin => new Color(0.28f, 0.40f, 0.52f),
		_ => new Color(0.5f, 0.5f, 0.5f),
	};

	/// <summary>
	/// A distinct hue per region, so the patchwork itself is visible. Adjacent
	/// ids get widely separated hues via the golden-ratio step.
	/// </summary>
	private static Color RegionColor(int id)
	{
		if (id < 0) return new Color(0.5f, 0.5f, 0.5f);
		float hue = id * 0.61803399f % 1f;
		float sat = 0.45f + (id * 7 % 3) * 0.12f;
		float val = 0.62f + (id * 5 % 4) * 0.09f;
		return Color.FromHsv(hue, sat, val);
	}

	/// <summary>Broken ground, and anything with no classification: one flat grey.</summary>
	private static readonly Color Unremarkable = new(0.34f, 0.34f, 0.36f);

	private static readonly Color WaterTint = new(0.16f, 0.34f, 0.52f);

	/// <summary>A pass, overlaid on the landform view so the saddle is findable.</summary>
	private static readonly Color PassTint = new(0.92f, 0.85f, 0.42f);

	/// <summary>Overlay colours: a bridge deck, its two banks, and a landing strip.</summary>
	private static readonly Color DeckTint = new(0.95f, 0.72f, 0.30f);
	private static readonly Color BankTint = new(0.99f, 0.94f, 0.55f);
	private static readonly Color StripTint = new(0.35f, 0.85f, 0.95f, 0.85f);
	private static readonly Color StripUsedTint = new(1f, 0.55f, 0.85f);

	/// <summary>
	/// Walkable areas, by area rank. Rank 0 is the mainland and always reads as
	/// ground; the rest get widely separated hues.
	///
	/// <b>Only districts get a colour.</b> A mountain flank of four-slab risers is
	/// a stack of contour benches, each of which is technically its own connected
	/// area — colouring every one would paint the massif in fifty stripes and say
	/// nothing. Everything under <see cref="Traversal.MinDistrictArea"/> is broken
	/// ground: one grey, so the eye reads it as the single impassable mass it is.
	/// </summary>
	private static Color WalkColor(IslandData d, int id)
	{
		if (id == Traversal.Water) return WaterTint;
		if (id < 0 || id >= d.Areas.Count) return Unremarkable;
		if (!d.Areas[id].IsDistrict) return Unremarkable;
		if (id == d.Mainland) return new Color(0.42f, 0.62f, 0.28f);

		float hue = (0.08f + id * 0.61803399f) % 1f;
		return Color.FromHsv(hue, 0.62f, 0.88f);
	}

	/// <summary>
	/// The same question asked of a player who can build. Green is the heartland —
	/// everything a stair, a hoist or a bridge could join into one place. Red is
	/// what stays out of reach whatever you build, which should be mountain and
	/// almost nothing else.
	/// </summary>
	private static Color ReachColor(IslandData d, int id)
	{
		if (id == Traversal.Water) return WaterTint;
		if (id < 0 || id >= d.Reaches.Count) return Unremarkable;
		if (id == d.Heartland) return new Color(0.42f, 0.62f, 0.28f);

		// Warm, and warmer the smaller the scrap, so a stranded plateau reads
		// differently from the shrapnel of a summit.
		float t = Mathf.Clamp(d.Reaches[id].Area / 120f, 0f, 1f);
		return new Color(0.86f, 0.22f + 0.26f * t, 0.18f);
	}

	/// <summary>
	/// Where a settlement could go. A buildable shelf — big enough and at least
	/// <see cref="Traversal.MinShelfWidth"/> cells wide — gets a colour; ground
	/// that is level but too small or too narrow is dimmed, so what the settlement
	/// layer could actually use stands out from what is merely flat.
	/// </summary>
	private static Color ShelfColor(IslandData d, int x, int z)
	{
		if (d.WaterLevel[x, z] != IslandData.NoLand) return new Color(0.16f, 0.34f, 0.52f);

		int id = d.ShelfId[x, z];
		if (id < 0 || id >= d.Shelves.Count) return Unremarkable;

		Shelf shelf = d.Shelves[id];
		if (!shelf.Buildable) return new Color(0.40f, 0.36f, 0.30f);

		float hue = (0.30f + id * 0.61803399f) % 1f;
		return Color.FromHsv(hue, 0.55f, 0.95f);
	}

	public override void _Ready()
	{
		_terrain = GetNode<MultiMeshInstance3D>("Terrain");
		_rig = GetNode<CameraRig>("CameraRig");
		_unitBox = new BoxMesh { Size = Vector3.One };
		_unitBox.Material = new StandardMaterial3D
		{
			VertexColorUseAsAlbedo = true,
			Roughness = 1f,
		};

		// Water is a single flat quad per cell at the surface, NOT a box. Boxes
		// share interior faces with their neighbours, and under alpha blending
		// every one of those faces still gets drawn — the doubled alpha is what
		// draws a dark grid line along each cell edge. Coplanar quads do not
		// overlap, so the surface reads as one sheet.
		_waterQuad = new PlaneMesh
		{
			Size = new Vector2(Terrain.CellSize, Terrain.CellSize),
			Orientation = PlaneMesh.OrientationEnum.Y,
		};
		_waterQuad.Material = WaterMaterial(0.66f);
		_water = new MultiMeshInstance3D
		{
			Name = "Water",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_water);

		// A fall is the same water stood on end: one quad per fall, as wide as the
		// channel and as tall as the drop. At the rim it runs on past the keel,
		// because there is nothing under a Domain to catch it.
		_fallQuad = new PlaneMesh
		{
			Size = Vector2.One,
			Orientation = PlaneMesh.OrientationEnum.Z,
		};
		_fallQuad.Material = WaterMaterial(0.75f);
		_falls = new MultiMeshInstance3D
		{
			Name = "Falls",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_falls);

		// A Gate is three cells across, one deep and twelve slabs tall, which at
		// four slabs to the cell is a square portal. Drawn unshaded so it reads as
		// a structure rather than as terrain, and visible through the island so a
		// Gate on the far side can still be found.
		_gateBox = new BoxMesh { Size = Vector3.One };
		_gateBox.Material = new StandardMaterial3D
		{
			VertexColorUseAsAlbedo = true,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			NoDepthTest = true,
		};
		_gates = new MultiMeshInstance3D
		{
			Name = "Gates",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_gates);

		// The overlays share one mesh: flat markers laid on the ground, and the
		// arrows that show which way a Gate opens. Unshaded, so an overlay never
		// reads as terrain, but depth-tested, so a marker on the far side of a
		// mountain is hidden by it.
		_markBox = new BoxMesh { Size = Vector3.One };
		_markBox.Material = new StandardMaterial3D
		{
			VertexColorUseAsAlbedo = true,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};
		_marks = new MultiMeshInstance3D
		{
			Name = "Overlays",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_marks);

		BuildCompass();
		BuildOverlayUi();
		Rebuild();
	}

	private static StandardMaterial3D WaterMaterial(float alpha) => new()
	{
		AlbedoColor = new Color(0.16f, 0.42f, 0.62f, alpha),
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		// Visible from underneath too, since the lab can tilt below the island.
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		Roughness = 0.12f,
		Metallic = 0.1f,
	};

	public override void _Process(double delta)
	{
		if (Signature() != _lastSignature)
			Rebuild();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
		switch (key.Keycode)
		{
			// Generation is a pure function of (seed, params), so R alone always
			// rebuilds the same island. N rolls a new seed; _Process picks it up.
			case Key.N: Seed = (int)(GD.Randi() & 0x7FFFFFFF); break;
			case Key.R: Rebuild(); break;
			case Key.F: _rig.Frame(_islandCenter, _islandRadius); break;
			case Key.C: _view = (View)(((int)_view + 1) % ViewCount); Rebuild(); break;
			// Force a character, so one kind can be inspected across many seeds
			// instead of waiting for Auto to roll it.
			case Key.V: CycleCharacter(); break;
			case Key.G: CycleArrangement(); break;
			// The two knobs worth eyeballing on the same island rather than by
			// editing a resource and losing your place.
			case Key.H: Cycle(v => Params.Hilliness = v, Params?.Hilliness ?? 0.5f, "Hilliness"); break;
			case Key.M: Cycle(v => Params.LandformMix = v, Params?.LandformMix ?? 0.5f, "LandformMix"); break;
			// The Entry's kind is an input from the Domain that sent you, so it is
			// something to try both ways rather than a preference.
			case Key.T: CycleEntryGate(); break;
			case Key.Y: CycleCrossings(); break;
			case Key.B: _showBridges = !_showBridges; Redraw(); break;
			case Key.J: _showAirstrips = !_showAirstrips; Redraw(); break;
			case Key.X: _showCompass = !_showCompass; Redraw(); break;
			case Key.F1: _showHelp = !_showHelp; Redraw(); break;
		}
	}

	/// <summary>Steps a 0-1 knob through quarters, so its whole range is four keypresses.</summary>
	private void Cycle(Action<float> set, float current, string label)
	{
		Params ??= new IslandParams();
		float next = Mathf.Round(current * 4f + 1f) / 4f;
		if (next > 1.001f) next = 0f;
		set(next);
		GD.Print($"[IslandLab] {label} = {next:0.00}");
	}

	private void CycleArrangement()
	{
		Params ??= new IslandParams();
		int count = Enum.GetValues<IslandArrangement>().Length;
		Params.Arrangement = (IslandArrangement)(((int)Params.Arrangement + 1) % count);
		GD.Print($"[IslandLab] Arrangement = {Params.Arrangement}");
	}

	private void CycleCharacter()
	{
		Params ??= new IslandParams();
		int count = Enum.GetValues<TerrainCharacter>().Length;
		Params.Character = (TerrainCharacter)(((int)Params.Character + 1) % count);
		GD.Print($"[IslandLab] Character = {Params.Character}");
	}

	private void CycleEntryGate()
	{
		Params ??= new IslandParams();
		Params.EntryGate = Params.EntryGate switch
		{
			GateKind.Auto => GateKind.Hanging,
			GateKind.Hanging => GateKind.Land,
			_ => GateKind.Auto,
		};
		GD.Print($"[IslandLab] EntryGate = {Params.EntryGate}");
	}

	private void CycleCrossings()
	{
		Params ??= new IslandParams();
		Params.Crossings = Params.Crossings switch
		{
			BridgeEase.Easy => BridgeEase.Medium,
			BridgeEase.Medium => BridgeEase.Hard,
			_ => BridgeEase.Easy,
		};
		GD.Print($"[IslandLab] Crossings = {Params.Crossings} ({(int)Params.Crossings} cells)");
	}

	private int Signature()
	{
		var h = new HashCode();
		h.Add(Seed);
		if (Params != null)
		{
			h.Add(Params.Size);
			h.Add(Params.Radius);
			h.Add(Params.Coverage);
			h.Add(Params.Arrangement);
			h.Add(Params.Irregularity);
			h.Add(Params.Character);
			h.Add(Params.LandformMix);
			h.Add(Params.Relief);
			h.Add(Params.Hilliness);
			h.Add(Params.RegionScale);
			h.Add(Params.CliffHeight);
			h.Add(Params.PlateauLevels);
			h.Add(Params.MountainHeight);
			h.Add(Params.MesaHeight);
			h.Add(Params.BasinDepth);
			// The Gate and water knobs belong here too: leaving them out meant
			// editing EntryGate in the remote inspector did nothing at all, which
			// reads as the setting being ignored rather than as a stale island.
			h.Add(Params.Rivers);
			h.Add(Params.Crossings);
			h.Add(Params.EntryGate);
			h.Add(Params.ExitGates);
			h.Add(Params.EdgeThickness);
			h.Add(Params.KeelDepth);
			h.Add(Params.KeelRoughness);
		}
		return h.ToHashCode();
	}

	private IslandData? _data;

	private void Rebuild()
	{
		if (_terrain == null || _unitBox == null) return;
		Params ??= new IslandParams();
		_lastSignature = Signature();

		ulong t0 = Time.GetTicksUsec();
		_data = new IslandGenerator().Generate(Seed, Params);
		int spans = RenderSpans(_data);
		float ms = (Time.GetTicksUsec() - t0) / 1000f;
		int lakes = Redraw();
		GD.Print($"[IslandLab] seed {Seed}, {Params.Size}², {_data.Character} ({_data.Style})"
			+ $" -> {spans} spans, {lakes} lakes in {ms:0.0} ms");

		if (!_framedOnce)
		{
			_rig.Frame(_islandCenter, _islandRadius);
			_framedOnce = true;
		}
	}

	/// <summary>
	/// Everything that is not the terrain itself: water, falls, Gates, overlays
	/// and the text. Separate from <see cref="Rebuild"/> so toggling an overlay
	/// does not regenerate the island.
	/// </summary>
	private int Redraw()
	{
		if (_data == null) return 0;
		int lakes = RenderWater(_data);
		RenderFalls(_data);
		RenderGates(_data);
		RenderOverlays(_data);
		UpdateText(_data, lakes);
		return lakes;
	}

	private void UpdateText(IslandData d, int lakes)
	{
		if (_status == null) return;

		_status.Text =
			$"{d.Character}   {d.Arrangement}   high ground: {d.Style}   seed {Seed}\n"
			+ $"hilliness {Params.Hilliness:0.00}   mix {Params.LandformMix:0.00}   "
			+ $"entry gate {Params.EntryGate}   crossings {Params.Crossings} "
			+ $"({d.BridgeSpan} cells)   lakes {lakes}\n"
			+ WalkSummary(d) + "\n"
			+ GateSummary(d);

		_legend.Text = ViewLegend(_view);
		_overlays.Text = $"[B] bridges {(_showBridges ? "on" : "off")}    "
			+ $"[J] airstrips {(_showAirstrips ? "on" : "off")}    "
			+ $"[X] compass {(_showCompass ? "on" : "off")}";

		// The plate, not the label: hiding the text alone leaves an empty panel.
		if (_help.GetParent() is Control plate) plate.Visible = _showHelp;
		_hint.Visible = !_showHelp;
		_help.Text = _showHelp
			? "WASD move   Q/E rotate   MMB-drag rotate + tilt   arrows tilt   "
			+ "wheel zoom   Shift faster\n"
			+ "N new seed   R rebuild   F frame   C view\n"
			+ "V character   G arrangement   H hilliness   M landform mix   "
			+ "T entry gate   Y crossing ease\n"
			+ "B bridges   J airstrips   X compass and gate vectors   F1 hide this"
			: "";
	}

	private static int RiverCells(IslandData d)
	{
		int found = 0;
		for (int x = 0; x < d.Size; x++)
		for (int z = 0; z < d.Size; z++) if (d.River[x, z]) found++;
		return found;
	}

	/// <summary>
	/// What the traversal analysis found, in one line: how much of the island is
	/// one walkable piece, how much is broken ground, and how much of the level
	/// ground is big and wide enough to settle.
	/// </summary>
	private static string WalkSummary(IslandData d)
	{
		int land = 0;
		for (int x = 0; x < d.Size; x++)
		for (int z = 0; z < d.Size; z++) if (d.HasLand(x, z)) land++;
		if (land == 0) return "no land";

		int districts = 0, broken = 0;
		foreach (WalkArea a in d.Areas)
		{
			if (a.IsDistrict) districts++;
			else broken += a.Area;
		}

		int mainland = d.Mainland >= 0 ? d.Areas[d.Mainland].Area : 0;
		int buildable = 0;
		foreach (Shelf shelf in d.Shelves) if (shelf.Buildable) buildable++;

		int heart = d.Heartland >= 0 ? d.Reaches[d.Heartland].Area : 0;
		int rim = 0;
		foreach (Fall f in d.Falls) if (f.OffRim) rim++;

		return $"walk {100f * mainland / land:0}% mainland in {districts} districts   "
			+ $"reach {100f * heart / land:0}%   "
			+ $"shelves {buildable} buildable of {d.Shelves.Count}   "
			+ $"passes {d.Passes.Count}   bridges {d.Bridges.Count}   "
			+ $"rivers {RiverCells(d)} cells, {d.Falls.Count} falls ({rim} off the rim)";
	}

	/// <summary>
	/// One quad per flooded column, at the water surface. Returns the number of
	/// distinct lakes, for the status line.
	/// </summary>
	private int RenderWater(IslandData d)
	{
		int n = d.Size;
		float half = n * 0.5f;
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;

		var xf = new List<Transform3D>();
		var lakes = new HashSet<int>();

		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
		{
			short level = d.WaterLevel[x, z];
			if (level == IslandData.NoLand) continue;

			short top = d.SurfaceLevel(x, z);
			if (top == IslandData.NoLand || level <= top) continue;

			// Slab index L fills world Y from L*sh to (L+1)*sh, so the surface of
			// the topmost water slab sits at (level + 1) * sh.
			xf.Add(new Transform3D(
				Basis.Identity,
				new Vector3((x - half) * cs, (level + 1) * sh, (z - half) * cs)));
			// Rivers share the water plane but are not lakes; counting them would
			// make the tally meaningless.
			if (!d.River[x, z]) lakes.Add(d.Region[x, z]);
		}

		var mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = _waterQuad,
			InstanceCount = xf.Count,
		};
		for (int i = 0; i < xf.Count; i++) mm.SetInstanceTransform(i, xf[i]);
		_water.Multimesh = mm;
		return lakes.Count;
	}

	/// <summary>
	/// One vertical sheet per fall, standing across the flow at the lip.
	///
	/// The rim falls are the point of the whole exercise: a Domain seen from
	/// below should have water spilling off its underside into the aether, and
	/// that image does more for "these are floating islands" than anything else
	/// in the renderer. So a rim fall is drawn running on past the keel rather
	/// than stopping at the ground.
	/// </summary>
	private void RenderFalls(IslandData d)
	{
		int n = d.Size;
		float half = n * 0.5f;
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;

		var xf = new List<Transform3D>();

		foreach (Fall f in d.Falls)
		{
			float top = (f.Top + 1) * sh;
			float bottom = f.Bottom * sh;
			float height = Mathf.Max(sh, top - bottom);

			// The sheet stands across the flow: rotate the quad's +Z normal onto
			// the direction the water is going, and push it half a cell that way
			// so it hangs at the lip rather than through the column behind it.
			float angle = Mathf.Atan2(f.Flow.X, f.Flow.Y);
			var basis = new Basis(Vector3.Up, angle)
				.Scaled(new Vector3(f.Width * cs, height, 1f));

			var origin = new Vector3(
				(f.Cell.X - half + f.Flow.X * 0.5f) * cs,
				bottom + height * 0.5f,
				(f.Cell.Y - half + f.Flow.Y * 0.5f) * cs);

			xf.Add(new Transform3D(basis, origin));
		}

		var mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = _fallQuad,
			InstanceCount = xf.Count,
		};
		for (int i = 0; i < xf.Count; i++) mm.SetInstanceTransform(i, xf[i]);
		_falls.Multimesh = mm;
	}

	/// <summary>
	/// One box per Gate, in the portal's real proportions. Entry is gold, exits
	/// are cyan; a hanging Gate is drawn paler, since the thing that distinguishes
	/// it is that there is nothing under it.
	/// </summary>
	private void RenderGates(IslandData d)
	{
		int n = d.Size;
		float half = n * 0.5f;
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;

		var xf = new List<Transform3D>();
		var col = new List<Color>();

		foreach (Gate g in d.Gates)
		{
			// Center.Y is the slab the sill rests on, so the portal starts at the
			// top of that slab and rises Gate.Height from there.
			float baseY = (g.Center.Y + 1) * sh;
			var origin = new Vector3((g.Center.X - half) * cs,
									 baseY + Gate.Height * sh * 0.5f,
									 (g.Center.Z - half) * cs);

			// Three cells across its face, one deep through it.
			var size = new Vector3(
				Mathf.Abs(g.Across.X) * Gate.Width * cs + Mathf.Abs(g.Outward.X) * cs,
				Gate.Height * sh,
				Mathf.Abs(g.Across.Y) * Gate.Width * cs + Mathf.Abs(g.Outward.Y) * cs);

			xf.Add(new Transform3D(Basis.Identity.Scaled(size), origin));

			Color tint = g.Role == GateRole.Entry
				? new Color(1f, 0.82f, 0.25f, 0.85f)
				: new Color(0.35f, 0.85f, 0.95f, 0.75f);
			col.Add(g.Kind == GateKind.Hanging ? tint.Lightened(0.35f) : tint);
		}

		var mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseColors = true,
			Mesh = _gateBox,
			InstanceCount = xf.Count,
		};
		for (int i = 0; i < xf.Count; i++)
		{
			mm.SetInstanceTransform(i, xf[i]);
			mm.SetInstanceColor(i, col[i]);
		}
		_gates.Multimesh = mm;
	}

	/// <summary>
	/// The overlays: the crossings that hold the arrangement together, the ground
	/// a vessel could land on, and the way each Gate opens.
	///
	/// All three are answers the generator already has and the terrain does not
	/// show — where you would have to build to make the island one place, where a
	/// Link could come out, and which way is north.
	/// </summary>
	private void RenderOverlays(IslandData d)
	{
		int n = d.Size;
		float half = n * 0.5f;
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;

		var xf = new List<Transform3D>();
		var col = new List<Color>();

		void Mark(float x, float y, float z, Vector3 size, Color tint)
		{
			xf.Add(new Transform3D(Basis.Identity.Scaled(size),
								   new Vector3((x - half) * cs, y, (z - half) * cs)));
			col.Add(tint);
		}

		if (_showBridges)
		{
			foreach (Crossing c in d.Bridges)
			{
				// The deck itself: a run of slabs at one level from bank to bank,
				// which is all a bridge is.
				for (int i = 0; i < c.Span; i++)
				{
					Vector2I cell = c.Cell(i);
					Mark(cell.X, (c.Deck + 1) * sh, cell.Y,
						 new Vector3(cs * 0.8f, sh * 0.7f, cs * 0.8f), DeckTint);
				}
				foreach (Vector2I bank in new[] { c.A, c.B })
					Mark(bank.X, (Traversal.CrossLevel(d, bank.X, bank.Y) + 1) * sh + sh * 0.2f,
						 bank.Y, new Vector3(cs * 0.9f, sh * 0.5f, cs * 0.9f), BankTint);
			}
		}

		if (_showAirstrips)
		{
			for (int x = 0; x < n; x++)
			for (int z = 0; z < n; z++)
			{
				if (!d.Airstrip[x, z]) continue;
				Mark(x, (d.SurfaceLevel(x, z) + 1) * sh + sh * 0.1f, z,
					 new Vector3(cs * 0.85f, sh * 0.25f, cs * 0.85f), StripTint);
			}

			// And the strips the Gates actually took, in a colour of their own.
			foreach (Gate g in d.Gates)
			{
				if (g.Kind != GateKind.Hanging) continue;
				Vector2I head = new Vector2I(g.Center.X, g.Center.Z)
								- g.Outward * GatePlacement.HangingOffset;
				for (int along = 0; along < Mathf.Max(1, g.Landing); along++)
				{
					Vector2I cell = head - g.Outward * along;
					if (cell.X < 0 || cell.Y < 0 || cell.X >= n || cell.Y >= n) break;
					if (!d.HasLand(cell.X, cell.Y)) break;
					Mark(cell.X, (d.SurfaceLevel(cell.X, cell.Y) + 1) * sh + sh * 0.3f, cell.Y,
						 new Vector3(cs * 0.6f, sh * 0.3f, cs * 0.6f), StripUsedTint);
				}
			}
		}

		if (_showCompass)
		{
			// Which way each Gate opens onto the Domain. A Gate faces outward, so
			// the way *in* is the opposite — and that is the direction that
			// matters, because it is where the player is walking when they arrive.
			foreach (Gate g in d.Gates)
			{
				Vector2I inward = -g.Outward;
				Color tint = g.Role == GateRole.Entry
					? new Color(1f, 0.82f, 0.25f)
					: new Color(0.35f, 0.85f, 0.95f);

				for (int step = 1; step <= 6; step++)
				{
					int cx = g.Center.X + inward.X * step;
					int cz = g.Center.Z + inward.Y * step;
					if (cx < 0 || cz < 0 || cx >= n || cz >= n) continue;

					short ground = d.SurfaceLevel(cx, cz);
					float y = ground == IslandData.NoLand
						? (g.Center.Y + 1) * sh
						: (ground + 1) * sh + sh * 0.6f;
					// Tapering, so the arrow has a direction rather than being a
					// row of identical pips.
					float w = Mathf.Lerp(0.7f, 0.2f, (step - 1) / 5f);
					Mark(cx, y, cz, new Vector3(cs * w, sh * 0.5f, cs * w), tint);
				}
			}
		}

		var mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseColors = true,
			Mesh = _markBox,
			InstanceCount = xf.Count,
		};
		for (int i = 0; i < xf.Count; i++)
		{
			mm.SetInstanceTransform(i, xf[i]);
			mm.SetInstanceColor(i, col[i]);
		}
		_marks.Multimesh = mm;

		PlaceCompass(d);
	}

	/// <summary>
	/// N / E / S / W, standing off the four edges of the footprint. Domains are
	/// laid out on a plane by their world-tree position, so which way a Gate faces
	/// is a fact about the world and not a detail of the view.
	/// </summary>
	private void BuildCompass()
	{
		var letters = new[] { "N", "E", "S", "W" };
		foreach (string text in letters)
		{
			var label = new Label3D
			{
				Text = text,
				FontSize = 128,
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				NoDepthTest = true,
				Modulate = new Color(1f, 0.95f, 0.8f, 0.85f),
				PixelSize = 0.006f,
			};
			AddChild(label);
			_compass.Add(label);
		}
	}

	private void PlaceCompass(IslandData d)
	{
		float half = d.Size * 0.5f * Terrain.CellSize;
		float out_ = half + 6f;
		var at = new[]
		{
			new Vector3(0f, 0f, -out_),   // N is -Z
			new Vector3(out_, 0f, 0f),    // E is +X
			new Vector3(0f, 0f, out_),    // S is +Z
			new Vector3(-out_, 0f, 0f),   // W is -X
		};

		for (int i = 0; i < _compass.Count; i++)
		{
			_compass[i].Position = at[i] + Vector3.Up * 3f;
			_compass[i].Visible = _showCompass;
		}
	}

	private static string GateSummary(IslandData d)
	{
		if (d.Gates.Count == 0) return "gates: none";

		var bits = new List<string>();
		foreach (Gate g in d.Gates)
			bits.Add($"{g.Facing} {g.Kind}{(g.Role == GateRole.Entry ? "*" : "")}");
		return "gates: " + string.Join(", ", bits) + "   (* = entry)";
	}

	private static bool OnRegionBorder(IslandData d, int x, int z)
	{
		int n = d.Size;
		int r = d.Region[x, z];
		if (x == 0 || z == 0 || x == n - 1 || z == n - 1) return true;
		return d.Region[x - 1, z] != r || d.Region[x + 1, z] != r
			|| d.Region[x, z - 1] != r || d.Region[x, z + 1] != r;
	}

	private int RenderSpans(IslandData d)
	{
		int n = d.Size;
		float half = n * 0.5f;
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;

		var xf = new List<Transform3D>();
		var col = new List<Color>();

		int topMax = 1, topMin = 0;
		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
		{
			Span[] spans = d.Spans[x, z];
			if (spans == null) continue;
			foreach (Span s in spans)
			{
				topMax = Math.Max(topMax, s.Top);
				topMin = Math.Min(topMin, s.Top);
			}
		}
		float tintSpan = Math.Max(1, topMax - topMin);

		var low = new Color(0.24f, 0.20f, 0.13f);   // deep / dirt
		var mid = new Color(0.30f, 0.42f, 0.18f);   // grass
		var high = new Color(0.66f, 0.72f, 0.52f);  // highlands

		var bbMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
		var bbMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
		{
			Span[] spans = d.Spans[x, z];
			if (spans == null) continue;

			foreach (Span s in spans)
			{
				float hWorld = s.Height * sh;
				float yCenter = (s.Bottom + s.Top + 1) * 0.5f * sh;
				var origin = new Vector3((x - half) * cs, yCenter, (z - half) * cs);

				xf.Add(new Transform3D(
					Basis.Identity.Scaled(new Vector3(cs, hWorld, cs)), origin));

				switch (_view)
				{
					case View.Landform:
						col.Add(d.Pass[x, z]
							? LandformColor((LandformType)d.Landform[x, z]).Lerp(PassTint, 0.55f)
							: LandformColor((LandformType)d.Landform[x, z]));
						break;
					case View.Region:
						// Darken the border ring so each patch is outlined.
						col.Add(OnRegionBorder(d, x, z)
							? RegionColor(d.Region[x, z]).Darkened(0.55f)
							: RegionColor(d.Region[x, z]));
						break;
					case View.Walk:
						col.Add(WalkColor(d, d.Walk[x, z]));
						break;
					case View.Reach:
						col.Add(ReachColor(d, d.Reach[x, z]));
						break;
					case View.Shelves:
						col.Add(ShelfColor(d, x, z));
						break;
					default:
						float t = Mathf.Clamp((s.Top - topMin) / tintSpan, 0f, 1f);
						col.Add(t < 0.5f ? low.Lerp(mid, t * 2f) : mid.Lerp(high, (t - 0.5f) * 2f));
						break;
				}

				var ext = new Vector3(cs * 0.5f, hWorld * 0.5f, cs * 0.5f);
				bbMin = bbMin.Min(origin - ext);
				bbMax = bbMax.Max(origin + ext);
			}
		}

		if (xf.Count > 0)
		{
			_islandCenter = (bbMin + bbMax) * 0.5f;
			_islandRadius = Mathf.Max(1f, (bbMax - bbMin).Length() * 0.5f);
		}

		var mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseColors = true,
			Mesh = _unitBox,
			InstanceCount = xf.Count,
		};
		for (int i = 0; i < xf.Count; i++)
		{
			mm.SetInstanceTransform(i, xf[i]);
			mm.SetInstanceColor(i, col[i]);
		}
		_terrain.Multimesh = mm;
		return xf.Count;
	}

	/// <summary>
	/// The overlay text, in panels that cannot land on top of each other: what the
	/// island is (top left), what the current view means (top right), which
	/// overlays are on (bottom right) and the key list (bottom left, on F1).
	///
	/// Laid out with containers rather than fixed positions — the previous version
	/// put two multi-line labels at hard-coded offsets, and every line the status
	/// grew ran straight through the one above it.
	/// </summary>
	private void BuildOverlayUi()
	{
		var layer = new CanvasLayer();
		AddChild(layer);

		var frame = new MarginContainer();
		frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		foreach (string side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
			frame.AddThemeConstantOverride(side, 12);
		frame.MouseFilter = Control.MouseFilterEnum.Ignore;
		layer.AddChild(frame);

		var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		rows.AddThemeConstantOverride("separation", 8);
		frame.AddChild(rows);

		var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		rows.AddChild(top);
		_status = Panelled(top, Control.SizeFlags.ShrinkBegin, new Color(1f, 0.93f, 0.72f));
		top.AddChild(new Control
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});
		_legend = Panelled(top, Control.SizeFlags.ShrinkEnd, new Color(0.82f, 0.92f, 1f));

		// The stretcher: everything above it sits at the top, everything below at
		// the bottom, whatever the window size.
		rows.AddChild(new Control
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});

		var bottom = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		rows.AddChild(bottom);
		_help = Panelled(bottom, Control.SizeFlags.ShrinkBegin, new Color(0.88f, 0.88f, 0.9f));
		bottom.AddChild(new Control
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});
		_overlays = Panelled(bottom, Control.SizeFlags.ShrinkEnd, new Color(0.95f, 0.86f, 0.7f));

		if (_help.GetParent() is Control helpPlate) helpPlate.Visible = false;
		_legend.Text = ViewLegend(_view);
		_overlays.Text = "";
		_status.Text = "";

		_hint = new Label
		{
			Text = "  F1 keys",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_hint.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.45f));
		bottom.AddChild(_hint);

		// The legend can run long on the landform view; let it wrap rather than
		// push the status panel off its own side of the screen.
		_legend.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_legend.CustomMinimumSize = new Vector2(360, 0);
	}

	/// <summary>One label on a dark plate, so text stays readable over pale terrain.</summary>
	private static Label Panelled(Container into, Control.SizeFlags flags, Color tint)
	{
		var plate = new StyleBoxFlat
		{
			BgColor = new Color(0f, 0f, 0f, 0.55f),
			ContentMarginLeft = 10,
			ContentMarginRight = 10,
			ContentMarginTop = 6,
			ContentMarginBottom = 6,
			CornerRadiusTopLeft = 5,
			CornerRadiusTopRight = 5,
			CornerRadiusBottomLeft = 5,
			CornerRadiusBottomRight = 5,
		};

		var panel = new PanelContainer
		{
			SizeFlagsHorizontal = flags,
			SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		panel.AddThemeStyleboxOverride("panel", plate);
		into.AddChild(panel);

		var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
		label.AddThemeColorOverride("font_color", tint);
		panel.AddChild(label);
		return label;
	}
}
