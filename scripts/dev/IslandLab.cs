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
/// <b>F</b> re-frames the island, <b>R</b> forces a rebuild of the same one.
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
	private MultiMeshInstance3D _gates = null!;
	private BoxMesh _gateBox = null!;
	private CameraRig _rig = null!;
	private BoxMesh _unitBox = null!;
	private PlaneMesh _waterBox = null!;
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
		/// <summary>Flat ground, and whether it is big and wide enough to build on.</summary>
		Shelves,
	}

	private static readonly int ViewCount = Enum.GetValues<View>().Length;

	private View _view = View.Height;
	private Label _status = null!;

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
	/// Flat ground. A buildable shelf — big enough and at least
	/// <see cref="Traversal.MinShelfWidth"/> cells wide — gets a colour; a shelf
	/// that is merely flat is dimmed, so what the settlement layer could actually
	/// use stands out from what is only level.
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
		_waterBox = new PlaneMesh
		{
			Size = new Vector2(Terrain.CellSize, Terrain.CellSize),
			Orientation = PlaneMesh.OrientationEnum.Y,
		};
		_waterBox.Material = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.16f, 0.42f, 0.62f, 0.66f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			// Visible from underneath too, since the lab can tilt below the island.
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			Roughness = 0.12f,
			Metallic = 0.1f,
		};
		_water = new MultiMeshInstance3D
		{
			Name = "Water",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_water);

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

		AddControlsHint();
		Rebuild();
	}

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
			h.Add(Params.EdgeThickness);
			h.Add(Params.KeelDepth);
			h.Add(Params.KeelRoughness);
		}
		return h.ToHashCode();
	}

	private void Rebuild()
	{
		if (_terrain == null || _unitBox == null) return;
		Params ??= new IslandParams();
		_lastSignature = Signature();

		ulong t0 = Time.GetTicksUsec();
		IslandData data = new IslandGenerator().Generate(Seed, Params);
		int spans = RenderSpans(data);
		int lakes = RenderWater(data);
		RenderGates(data);
		float ms = (Time.GetTicksUsec() - t0) / 1000f;
		GD.Print($"[IslandLab] seed {Seed}, {Params.Size}², {data.Character} ({data.Style})"
			+ $" -> {spans} spans, {lakes} lakes in {ms:0.0} ms");

		if (_status != null)
			_status.Text = $"{data.Character}   {data.Arrangement}   high ground: {data.Style}   "
				+ $"seed {Seed}   view: {_view}   lakes: {lakes}"
				+ (Params.Character == TerrainCharacter.Auto ? "" : "   [character pinned]")
				+ $"\nhilliness {Params.Hilliness:0.00}   mix {Params.LandformMix:0.00}   "
				+ WalkSummary(data);

		if (!_framedOnce)
		{
			_rig.Frame(_islandCenter, _islandRadius);
			_framedOnce = true;
		}
	}

	/// <summary>
	/// What the traversal analysis found, in one line: how much of the island is
	/// one walkable piece, how much is broken ground, and how much of the flat
	/// ground is big and wide enough to settle.
	/// </summary>
	private static int RiverCells(IslandData d)
	{
		int found = 0;
		for (int x = 0; x < d.Size; x++)
		for (int z = 0; z < d.Size; z++) if (d.River[x, z]) found++;
		return found;
	}

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

		return $"walk: {districts} districts, mainland {100f * mainland / land:0}% of land, "
			+ $"broken {100f * broken / land:0}% in {d.Areas.Count - districts} scraps"
			+ $"   reach: heartland {100f * heart / land:0}%"
			+ $"   shelves: {buildable} buildable of {d.Shelves.Count}"
			+ $"   passes: {d.Passes.Count}"
			+ $"   rivers: {RiverCells(d)} cells, {d.Falls.Count} falls"
			+ "\n" + GateSummary(d);
	}

	/// <summary>
	/// One box per flooded column, spanning surface+1 … water level. Returns the
	/// number of distinct lakes, for the status line.
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
			Mesh = _waterBox,
			InstanceCount = xf.Count,
		};
		for (int i = 0; i < xf.Count; i++) mm.SetInstanceTransform(i, xf[i]);
		_water.Multimesh = mm;
		return lakes.Count;
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

	private void AddControlsHint()
	{
		var layer = new CanvasLayer();
		AddChild(layer);
		var label = new Label
		{
			Text = "WASD move   Q/E rotate   MMB-drag rotate + tilt   arrows tilt   wheel zoom   Shift faster"
				 + "\nN new seed   V character   G arrangement   H hilliness   M landform mix"
				 + "   C view: height/landform/region/walk/reach/shelves   F frame   R rebuild"
				 + "\ngold portal = entry gate   cyan = exits   pale = hanging in the aether",
			Position = new Vector2(12, 8),
		};
		label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.85f));
		label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.6f));
		label.AddThemeConstantOverride("shadow_offset_x", 1);
		label.AddThemeConstantOverride("shadow_offset_y", 1);
		layer.AddChild(label);

		// What the island actually is, on screen: reading it off the console or
		// inferring it by cycling variants is guesswork.
		_status = new Label { Position = new Vector2(12, 54) };
		_status.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.72f));
		_status.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.7f));
		_status.AddThemeConstantOverride("shadow_offset_x", 1);
		_status.AddThemeConstantOverride("shadow_offset_y", 1);
		layer.AddChild(_status);
	}
}
