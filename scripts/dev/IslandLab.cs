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
/// <b>Tab</b> hides the control panel. Every key is also a control on that
/// panel; the panel is the interface and the keys are the shortcut.
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
	private MultiMeshInstance3D _goo = null!;
	private MultiMeshInstance3D _falls = null!;
	private MultiMeshInstance3D _gates = null!;
	private MultiMeshInstance3D _marks = null!;
	private BoxMesh _gateBox = null!;
	private BoxMesh _markBox = null!;
	private CameraRig _rig = null!;
	private BoxMesh _unitBox = null!;
	private PlaneMesh _waterQuad = null!;
	private PlaneMesh _gooQuad = null!;
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
		/// <summary>What the ground is made of.</summary>
		Surface,
		/// <summary>The feature anchors the content layer attaches things to.</summary>
		Anchors,
	}

	private static readonly int ViewCount = Enum.GetValues<View>().Length;

	/// <summary>What each view is answering, in one line, next to the picture.</summary>
	private static string ViewLegend(View view) => view switch
	{
		View.Height => "height    low ground dark, high ground pale",
		View.Landform => "landform  green plain / dark hills / grey mountain / "
					   + "brown mesa / blue basin / tan badlands / pale karst / "
					   + "mauve massif / sand dunes / olive sinkholes; yellow = pass",
		View.Region => "region    one hue per patch, borders darkened",
		View.Walk => "walk      what you can cross on foot: green mainland, "
				   + "a hue per other district, grey = broken ground",
		View.Reach => "reach     what you can cross once built: green heartland, "
					+ "red = out of reach whatever you build",
		View.Shelves => "shelves   ground you could settle on; dim brown = level but "
					  + "too small or too narrow",
		View.Surface => "surface   what the ground is made of: grey stone / pale scree / "
					  + "white snow / sand / brown silt / dark green grass (by water) / "
					  + "light green meadow / olive heath / tan dust",
		_ => "anchors   what the content layer attaches to: cyan coast / red cliff / "
		   + "magenta overhang / sand beach / yellow gate landing / green ford / "
		   + "blue ferry quay. Unmarked ground is dimmed",
	};

	private View _view = View.Height;

	// Overlays. Each answers a question about the island that the terrain itself
	// does not show: where you could build across, where a vessel could set down,
	// and which way is north.
	private bool _showBridges;
	private bool _showLandings;
	private bool _showFerries;
	private bool _showRoutes;
	private bool _showFords;
	private bool _showCompass = true;
	private bool _showPanel = true;

	private Label _status = null!;
	private Label _legend = null!;

	/// <summary>Flat colours for the landform view, so landforms read as landforms.</summary>
	private static Color LandformColor(LandformType type) => type switch
	{
		LandformType.Plain => new Color(0.45f, 0.60f, 0.28f),
		LandformType.Hills => new Color(0.30f, 0.44f, 0.20f),
		LandformType.Mountain => new Color(0.52f, 0.50f, 0.55f),
		LandformType.Mesa => new Color(0.68f, 0.45f, 0.26f),
		LandformType.Basin => new Color(0.28f, 0.40f, 0.52f),
		LandformType.Badlands => new Color(0.72f, 0.56f, 0.34f),
		LandformType.Karst => new Color(0.58f, 0.66f, 0.62f),
		LandformType.Massif => new Color(0.62f, 0.42f, 0.48f),
		LandformType.Dunes => new Color(0.80f, 0.74f, 0.46f),
		LandformType.Sinkholes => new Color(0.50f, 0.58f, 0.44f),
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
	private static readonly Color StripUsedTint = new(1f, 0.55f, 0.85f);

	/// <summary>A ferry berth: the quay, and the water it puts a hull on.</summary>
	private static readonly Color QuayTint = new(0.98f, 0.45f, 0.30f);
	private static readonly Color HullTint = new(0.55f, 0.85f, 0.98f, 0.9f);

	/// <summary>
	/// The road from the entry Gate to an exit: the walk, and the works on it.
	/// One colour per kind of crossing, because "what does this road cost?" is a
	/// question about which of the three you have to build.
	/// </summary>
	private static readonly Color RoadTint = new(0.98f, 0.95f, 0.62f, 0.8f);
	private static readonly Color StairTint = new(1f, 0.45f, 0.25f);
	private static readonly Color SpanTint = new(1f, 0.80f, 0.20f);
	private static readonly Color CrossingTint = new(0.30f, 0.95f, 0.85f);

	/// <summary>A ford: the one place a stream can be crossed on foot.</summary>
	private static readonly Color FordTint = new(0.85f, 0.95f, 0.60f);

	/// <summary>
	/// What the ground is made of and what the content layer can hang off it, in
	/// one line — the counterpart to the `surface` and `anchors` views for someone
	/// reading the numbers rather than the picture. The wind only appears where
	/// there are dunes for it to have made.
	/// </summary>
	private static string GroundSummary(IslandData d)
	{
		int n = d.Size;
		var made = new int[Enum.GetValues<SurfaceMaterial>().Length];
		int land = 0, dunes = 0;

		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
		{
			if (!d.HasLand(x, z)) continue;
			land++;
			made[d.Material[x, z]]++;
			if ((LandformType)d.Landform[x, z] == LandformType.Dunes) dunes++;
		}
		if (land == 0) return "ground: none";

		var bits = new List<(string Name, int Cells)>();
		foreach (SurfaceMaterial m in Enum.GetValues<SurfaceMaterial>())
			if (made[(int)m] > 0) bits.Add((m.ToString().ToLowerInvariant(), made[(int)m]));
		bits.Sort((a, b) => b.Cells.CompareTo(a.Cells));

		var parts = new List<string>();
		foreach (var (name, cells) in bits) parts.Add($"{name} {100 * cells / land}%");

		string wind = dunes > 0 ? $"   wind from {d.WindFrom}, dunes run {d.DuneRun}" : "";
		return $"ground: {string.Join(", ", parts)}{wind}"
			+ $"\nanchors: {d.CoastCells.Count} coast, {d.CliffCells.Count} cliff, "
			+ $"{d.Overhangs.Count} overhang, {Count(d.Beach)} beach, {Count(d.Ford)} ford, "
			+ $"{Count(d.Landings)} gate landing, {d.Berths.Count} quay";
	}

	private static int Count(bool[,] flags)
	{
		int total = 0;
		foreach (bool set in flags) if (set) total++;
		return total;
	}

	/// <summary>
	/// The feature anchors, flattened onto the footprint for the anchors view.
	///
	/// <para><b>These are the lists the content layer is meant to read.</b> A
	/// forest does not go "at (43, 71)", it goes "on flat well-watered ground away
	/// from the coast"; coral goes on a rim; vines hang under an overhang. So
	/// generation answers the geometric questions once and content reads the
	/// answers — which only works if the answers are right, and nothing showed
	/// them before this view. A coast list that has quietly stopped including half
	/// the coast is exactly the sort of thing that would go unnoticed until the
	/// biome layer was built on top of it.</para>
	///
	/// <para>Later kinds win where a cell is several things at once, so a gate
	/// landing on a beach reads as a landing: the built ground is the rarer fact
	/// and the one worth seeing.</para>
	/// </summary>
	private static byte[,] AnchorGrid(IslandData d)
	{
		int n = d.Size;
		var grid = new byte[n, n];

		foreach (Vector2I c in d.CoastCells) grid[c.X, c.Y] = 1;
		foreach (Vector2I c in d.CliffCells) grid[c.X, c.Y] = 2;
		foreach (Vector2I c in d.Overhangs) grid[c.X, c.Y] = 3;

		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
		{
			if (d.Beach[x, z]) grid[x, z] = 4;
			if (d.Ford[x, z]) grid[x, z] = 5;
			if (d.Landings[x, z]) grid[x, z] = 6;
			if (d.Ferry[x, z]) grid[x, z] = 7;
		}
		return grid;
	}

	/// <summary>One colour per anchor kind; everything else is dimmed ground.</summary>
	private static Color AnchorColor(IslandData d, int x, int z, byte[,]? grid)
	{
		if (grid == null) return Unremarkable;
		if (d.WaterLevel[x, z] != IslandData.NoLand) return new Color(0.16f, 0.24f, 0.38f);

		return grid[x, z] switch
		{
			1 => new Color(0.30f, 0.82f, 0.88f),      // coast
			2 => new Color(0.88f, 0.28f, 0.24f),      // cliff
			3 => new Color(0.88f, 0.35f, 0.85f),      // overhang / arch
			4 => new Color(0.90f, 0.82f, 0.55f),      // beach
			5 => new Color(0.55f, 0.92f, 0.45f),      // ford
			6 => new Color(0.98f, 0.86f, 0.25f),      // gate landing
			7 => new Color(0.35f, 0.55f, 0.95f),      // ferry quay
			_ => new Color(0.26f, 0.26f, 0.27f),      // unremarkable ground
		};
	}

	/// <summary>What the ground is made of, for the surface view.</summary>
	private static Color MaterialColor(SurfaceMaterial m) => m switch
	{
		SurfaceMaterial.Stone => new Color(0.46f, 0.46f, 0.48f),
		SurfaceMaterial.Scree => new Color(0.62f, 0.60f, 0.55f),
		SurfaceMaterial.Snow => new Color(0.92f, 0.94f, 0.96f),
		SurfaceMaterial.Sand => new Color(0.85f, 0.78f, 0.55f),
		SurfaceMaterial.Silt => new Color(0.52f, 0.44f, 0.32f),
		SurfaceMaterial.Grass => new Color(0.36f, 0.56f, 0.26f),
		SurfaceMaterial.Meadow => new Color(0.50f, 0.64f, 0.30f),
		SurfaceMaterial.Heath => new Color(0.52f, 0.52f, 0.32f),
		_ => new Color(0.68f, 0.58f, 0.42f),          // Dust
	};

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
		// Per-cell colour: a lake, a stream, a ford and a navigable reach are four
		// different things and used to be one blue. See WaterColor.
		if (_waterQuad.Material is StandardMaterial3D lit) lit.VertexColorUseAsAlbedo = true;
		_water = new MultiMeshInstance3D
		{
			Name = "Water",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_water);

		// Goo: the same flat sheet, its own material. Violet, glossier and more
		// opaque than water — a thing you notice and do not drink. It cannot ride
		// the water multimesh because vertex colours multiply into that material's
		// blue albedo, which crushes any warm channel to nothing.
		_gooQuad = new PlaneMesh
		{
			Size = new Vector2(Terrain.CellSize, Terrain.CellSize),
			Orientation = PlaneMesh.OrientationEnum.Y,
		};
		_gooQuad.Material = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.52f, 0.14f, 0.72f, 0.9f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			Roughness = 0.05f,
			Metallic = 0.2f,
		};
		_goo = new MultiMeshInstance3D
		{
			Name = "Goo",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_goo);

		// A fall is the same water stood on end: one quad per fall, as wide as the
		// channel and as tall as the drop. At the rim it runs on past the keel,
		// because there is nothing under a Domain to catch it.
		_fallQuad = new PlaneMesh
		{
			Size = Vector2.One,
			Orientation = PlaneMesh.OrientationEnum.Z,
		};
		_fallQuad.Material = WaterMaterial(0.75f);
		// Drawn after the water sheet, always. Two transparent objects are
		// otherwise ordered by distance from the camera to their origins — and
		// both multimeshes sit at the world origin, so the tie broke differently
		// as the camera moved and the falls popped in and out where the two
		// overlapped.
		if (_fallQuad.Material is StandardMaterial3D fallLit) fallLit.RenderPriority = 1;
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
		// Every key here is also a control on the panel, so whatever a key moves
		// has to move the widget with it — see Sync.
		switch (key.Keycode)
		{
			// The panel out of the way, for looking at the island rather than at
			// the numbers.
			case Key.Tab:
			case Key.F1:
				_showPanel = !_showPanel;
				_panel.Visible = _showPanel;
				return;
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
			// The plateau ladder, which is the least obvious knob on the island:
			// stepping it here is the only way to see what a rung is worth.
			case Key.L: CyclePlateaus(); break;
			// Everything added after the first audit, in or out in one keypress.
			case Key.U: CycleNewShapes(); break;
			case Key.B: _showBridges = !_showBridges; Redraw(); break;
			case Key.J: _showLandings = !_showLandings; Redraw(); break;
			case Key.K: _showFerries = !_showFerries; Redraw(); break;
			case Key.P: _showRoutes = !_showRoutes; Redraw(); break;
			// <b>O, not D.</b> D is the camera's strafe — the rig reads it every
			// frame through Input.IsKeyPressed, so binding an overlay to it meant
			// walking right also flickered the fords on and off.
			case Key.O: _showFords = !_showFords; Redraw(); break;
			case Key.X: _showCompass = !_showCompass; Redraw(); break;
			// Headless verifies numbers and never appearance, which is the standing
			// limit on any change to how terrain looks. A PNG is the cheapest way
			// to review a look without a live session.
			case Key.F2: Capture(); break;
		}
		Sync();
	}

	/// <summary>
	/// Writes the current view to a PNG next to the project, and says where.
	///
	/// Headless generation can verify every number on the island and none of its
	/// appearance, which is the standing limit on any change to how terrain looks —
	/// "run it and look" needs a live session and a human. A screenshot is not a
	/// substitute for looking, but it makes a look reviewable afterwards, and two
	/// of them make a change comparable.
	/// </summary>
	private void Capture()
	{
		Image shot = GetViewport().GetTexture().GetImage();
		string path = $"user://island-{Seed}-{_view.ToString().ToLowerInvariant()}.png";
		Error err = shot.SavePng(path);
		GD.Print(err == Error.Ok
			? $"[IslandLab] wrote {ProjectSettings.GlobalizePath(path)}"
			: $"[IslandLab] could not write {path}: {err}");
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

	/// <summary>
	/// Steps the plateau ladder through 1..4 rungs. It is the hardest parameter on
	/// the island to picture from its name — a rung is a level regions sit on, and
	/// a difference of one rung between two neighbours is a cliff — so stepping it
	/// on one seed and watching the terraces appear is the explanation.
	/// </summary>
	private void CyclePlateaus()
	{
		Params ??= new IslandParams();
		Params.PlateauLevels = Params.PlateauLevels >= 4 ? 1 : Params.PlateauLevels + 1;
		GD.Print($"[IslandLab] PlateauLevels = {Params.PlateauLevels} rungs "
			+ $"of {Params.CliffHeight} slabs");
	}

	/// <summary>
	/// Takes the newer arrangements and landforms in or out of <c>Auto</c>'s pool,
	/// both at once. They are two flags on the resource; one key is enough here,
	/// because what you want in the lab is to see the island with and without
	/// everything that was added after the first audit.
	/// </summary>
	private void CycleNewShapes()
	{
		Params ??= new IslandParams();
		bool on = !(Params.NewArrangements && Params.NewLandforms);
		Params.NewArrangements = on;
		Params.NewLandforms = on;
		GD.Print($"[IslandLab] new arrangements and landforms {(on ? "on" : "off")}");
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
			h.Add(Params.NewArrangements);
			h.Add(Params.NewLandforms);
			h.Add(Params.Lakes);
			h.Add(Params.Valleys);
			h.Add(Params.ExitGate);
			h.Add(Params.EntryEdge);
			h.Add(Params.OverhangDensity);
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

		// A shape only Auto-with-the-flag-on could have rolled is marked, so the
		// checkbox has a visible consequence on the island as well as on the pool.
		string newer = IslandGenerator.IsNewerShape(d.Arrangement)
					|| IslandGenerator.IsNewerShape(d.Character) ? " (newer shape)" : "";

		_status.Text =
			$"{d.Name}   seed {Seed}   {d.Arrangement}   {d.Character}{newer}: {Made(d)}"
			+ $"   high ground {d.Style}"
			+ (d.Rough ? "   ROUGH GOING" : "")
			+ (d.Unmet.Length > 0 ? $"   UNMET: {d.Unmet}" : "")
			+ $"\nladder {Params.PlateauLevels} rungs x {Params.CliffHeight} slabs   "
			+ $"crossings {Params.Crossings} ({d.BridgeSpan} cells)   lakes {lakes}   "
			+ $"built in {d.Attempts} attempt{(d.Attempts == 1 ? "" : "s")}\n"
			+ WalkSummary(d) + "\n"
			+ GroundSummary(d) + "\n"
			+ GateSummary(d) + "\n"
			+ RoadSummary(d);

		_legend.Text = ViewLegend(_view);
		Sync();
	}

	/// <summary>
	/// Which landforms this island actually ended up with, in size order.
	///
	/// A <c>TerrainCharacter</c> is a recipe, not a list of what came out — the
	/// <c>Ziggurat</c> character carries plains, hills, a mountain and the stepped
	/// massifs it is named for, and nothing on screen said which of those you were
	/// looking at. This does.
	/// </summary>
	private static string Made(IslandData d)
	{
		var cells = new Dictionary<LandformType, int>();
		for (int x = 0; x < d.Size; x++)
		for (int z = 0; z < d.Size; z++)
		{
			if (!d.HasLand(x, z)) continue;
			var form = (LandformType)d.Landform[x, z];
			cells.TryGetValue(form, out int had);
			cells[form] = had + 1;
		}
		if (cells.Count == 0) return "no land";

		var order = new List<LandformType>(cells.Keys);
		order.Sort((a, b) => cells[b].CompareTo(cells[a]));
		var bits = new List<string>();
		foreach (LandformType form in order) bits.Add(form.ToString().ToLowerInvariant());
		return string.Join(", ", bits);
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
		int gooCells = 0;
		for (int x = 0; x < d.Size; x++)
		for (int z = 0; z < d.Size; z++)
			if (d.WaterLevel[x, z] != IslandData.NoLand
				&& d.Fluid[x, z] == (byte)FluidKind.Goo) gooCells++;

		return $"walk {100f * mainland / land:0}% mainland in {districts} districts   "
			+ $"reach {100f * heart / land:0}%   "
			+ $"shelves {buildable} buildable of {d.Shelves.Count}   "
			+ $"passes {d.Passes.Count}   bridges {d.Bridges.Count}   "
			+ $"ferry berths {d.Berths.Count} on {d.WaterBodies} bodies   "
			+ $"rivers {RiverCells(d)} cells, {d.Falls.Count} falls ({rim} off the rim)"
			+ (gooCells > 0 ? $"   goo {gooCells} cells (violet)" : "")
			+ (d.Geysers.Count > 0 ? $"   geysers {d.Geysers.Count}" : "");
	}

	/// <summary>
	/// What each Link out costs to reach from the one the player arrives by, in
	/// works: stairs, bridges and ferries. Zero means you can walk it on the day
	/// you land.
	/// </summary>
	/// <summary>
	/// What kind of water this is, in colour.
	///
	/// Every body of water used to be drawn in one blue, which is why a navigable
	/// river was invisible: two cells of the same blue as the lake it came out of
	/// and the stream it became. They are four different things to a player — one
	/// you wade at a ford, one you ship goods on, one you ferry across, one you
	/// keep out of — so they are four colours.
	/// </summary>
	private static Color WaterColor(IslandData d, int x, int z)
	{
		if (d.Ford[x, z]) return new Color(0.55f, 0.80f, 0.72f, 0.55f);      // pale, shallow
		if (d.Navigable[x, z]) return new Color(0.10f, 0.45f, 0.60f, 0.85f); // deep, workable
		if (d.River[x, z]) return new Color(0.35f, 0.66f, 0.80f, 0.70f);     // a stream
		return new Color(0.13f, 0.30f, 0.55f, 0.80f);                        // standing water
	}

	private static string RoadSummary(IslandData d)
	{
		if (d.Passages.Count == 0) return "roads: none";

		var bits = new List<string>();
		foreach (Passage road in d.Passages)
		{
			int stairs = 0, spans = 0, ferries = 0;
			foreach (Works w in road.Built)
			{
				if (w.Kind == WorksKind.Stair) stairs++;
				else if (w.Kind == WorksKind.Bridge) spans++;
				else ferries++;
			}
			Gate exit = d.Gates[road.Exit];
			bits.Add($"{exit.Facing} cost {road.Cost}"
				+ (road.Cost > 0 ? $" ({stairs}s {spans}b {ferries}f)" : ""));
		}
		return "roads from the entry: " + string.Join(",   ", bits);
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
		var col = new List<Color>();
		var goo = new List<Transform3D>();
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
			var at = new Transform3D(
				Basis.Identity,
				new Vector3((x - half) * cs, (level + 1) * sh, (z - half) * cs));

			// The other fluid has its own sheet and its own material: goo is not
			// a colour of water, and the water material's blue albedo would eat
			// any warm tint a vertex colour tried to give it.
			if (d.Fluid[x, z] == (byte)FluidKind.Goo) { goo.Add(at); continue; }

			xf.Add(at);
			col.Add(WaterColor(d, x, z));
			// Rivers share the water plane but are not lakes; counting them would
			// make the tally meaningless.
			if (!d.River[x, z]) lakes.Add(d.Region[x, z]);
		}

		var gm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = _gooQuad,
			InstanceCount = goo.Count,
		};
		for (int i = 0; i < goo.Count; i++) gm.SetInstanceTransform(i, goo[i]);
		_goo.Multimesh = gm;

		// <b>UseColors, and the colour actually set.</b> The material has asked for
		// vertex colour as albedo since the four kinds of water were named, but
		// nothing ever wrote a per-instance colour — so a ford, a stream, a
		// navigable reach and a lake all came out the one blue the legend says they
		// are not.
		var mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseColors = true,
			Mesh = _waterQuad,
			InstanceCount = xf.Count,
		};
		for (int i = 0; i < xf.Count; i++)
		{
			mm.SetInstanceTransform(i, xf[i]);
			mm.SetInstanceColor(i, col[i]);
		}
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
		float floor = 0f, ceiling = 0f;

		foreach (Fall f in d.Falls)
		{
			float top = (f.Top + 1) * sh;
			float bottom = f.Bottom * sh;
			float height = Mathf.Max(sh, top - bottom);

			// The sheet stands across the flow: rotate the quad's +Z normal onto
			// the direction the water is going, and push it a whisker past half a
			// cell that way — half a cell is exactly the plane of the cliff face
			// under the lip, and a sheet in that plane z-fights the terrain,
			// which is the shimmer the falls used to have.
			float angle = Mathf.Atan2(f.Flow.X, f.Flow.Y);
			var basis = new Basis(Vector3.Up, angle)
				.Scaled(new Vector3(f.Width * cs, height, 1f));

			var origin = new Vector3(
				(f.Cell.X - half + f.Flow.X * 0.53f) * cs,
				bottom + height * 0.5f,
				(f.Cell.Y - half + f.Flow.Y * 0.53f) * cs);

			xf.Add(new Transform3D(basis, origin));
			floor = Mathf.Min(floor, bottom);
			ceiling = Mathf.Max(ceiling, top);
		}

		// <b>Cataracts.</b> A one- or two-slab step between adjacent flooded
		// cells is a rapid, not a fall — too small for the generator to name,
		// still a hole in the picture if nothing is drawn there: two sheets of
		// surface and a gap. Every such step gets a connecting sheet, purely in
		// the renderer; the generator's falls list stays the falls.
		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
		{
			short a = d.WaterLevel[x, z];
			if (a == IslandData.NoLand) continue;
			if (d.Fluid[x, z] != (byte)FluidKind.Water) continue;   // goo does not pour

			for (int k = 0; k < 2; k++)
			{
				int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
				if (nx >= n || nz >= n) continue;
				short b = d.WaterLevel[nx, nz];
				if (b == IslandData.NoLand || a == b) continue;
				if (d.Fluid[nx, nz] != (byte)FluidKind.Water) continue;
				if (Math.Abs(a - b) >= Rivers.FallDepth) continue;  // a fall, drawn above

				// The sheet hangs from the higher surface, facing the lower cell,
				// pushed the same whisker past the face the falls use.
				bool aHigh = a > b;
				int hx = aHigh ? x : nx, hz = aHigh ? z : nz;
				int fx = aHigh ? nx - x : x - nx, fz = aHigh ? nz - z : z - nz;
				short hi = aHigh ? a : b;
				short lo = aHigh ? b : a;

				float top = (hi + 1) * sh;
				float bottom = lo * sh;
				float height = top - bottom;
				var basis = new Basis(Vector3.Up, Mathf.Atan2(fx, fz))
					.Scaled(new Vector3(cs, height, 1f));
				xf.Add(new Transform3D(basis, new Vector3(
					(hx - half + fx * 0.53f) * cs,
					bottom + height * 0.5f,
					(hz - half + fz * 0.53f) * cs)));
				floor = Mathf.Min(floor, bottom);
				ceiling = Mathf.Max(ceiling, top);
			}
		}

		// A geyser is the same water stood on end once more, upwards: two narrow
		// sheets crossed at right angles, so the jet reads from every side.
		foreach (Geyser g in d.Geysers)
		{
			float bottom = (g.Base + 1) * sh;
			float top = (g.Top + 1) * sh;
			float height = Mathf.Max(sh, top - bottom);
			var at = new Vector3((g.Cell.X - half) * cs, bottom + height * 0.5f,
								 (g.Cell.Y - half) * cs);
			for (int arm = 0; arm < 2; arm++)
			{
				var basis = new Basis(Vector3.Up, arm * Mathf.Pi / 2f)
					.Scaled(new Vector3(0.34f * cs, height, 1f));
				xf.Add(new Transform3D(basis, at));
			}
			floor = Mathf.Min(floor, bottom);
			ceiling = Mathf.Max(ceiling, top);
		}

		var mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = _fallQuad,
			InstanceCount = xf.Count,
		};
		for (int i = 0; i < xf.Count; i++) mm.SetInstanceTransform(i, xf[i]);
		_falls.Multimesh = mm;
		// One box round every sheet, set by hand. The quad is flat, so the
		// automatic bounds are paper-thin on one axis — and a bound the culler
		// half-believes is a fall that blinks out whenever the camera swings.
		_falls.CustomAabb = new Aabb(
			new Vector3(-half * cs - cs, floor - cs, -half * cs - cs),
			new Vector3(n * cs + 2 * cs, ceiling - floor + 2 * cs, n * cs + 2 * cs));
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

			// One cell across its face, one deep through it, four slabs tall — a
			// single block, which is what a Gate is now.
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

		// The ground the Gates are served by: 3 by 5 where a vessel sets down at a
		// hanging Gate, 3 by 3 of forecourt where a land Gate stands. Nothing else
		// is marked — with a Gate guaranteed on every Domain, every *other* coast
		// that would take a strip is an answer to a question nobody is asking.
		if (_showLandings)
		{
			for (int x = 0; x < n; x++)
			for (int z = 0; z < n; z++)
			{
				if (!d.Landings[x, z]) continue;
				Mark(x, (d.SurfaceLevel(x, z) + 1) * sh + sh * 0.2f, z,
					 new Vector3(cs * 0.8f, sh * 0.3f, cs * 0.8f), StripUsedTint);
			}
		}

		// Fords: the one place a stream can be crossed on foot. Everywhere else a
		// stream is an obstacle, which is what makes the crossing a place.
		if (_showFords)
		{
			for (int x = 0; x < n; x++)
			for (int z = 0; z < n; z++)
			{
				if (!d.Ford[x, z]) continue;
				Mark(x, (d.WaterLevel[x, z] + 1) * sh + sh * 0.2f, z,
					 new Vector3(cs * 0.7f, sh * 0.4f, cs * 0.7f), FordTint);
			}
		}

		// Ferry berths: the quay and the water in front of it, drawn as the domino
		// they are. Where a lake or a navigable river divides the Domain, these are
		// the only places a crossing can be built at all.
		if (_showFerries)
		{
			foreach (FerryBerth berth in d.Berths)
			{
				Mark(berth.Land.X,
					 (Traversal.CrossLevel(d, berth.Land.X, berth.Land.Y) + 1) * sh + sh * 0.25f,
					 berth.Land.Y, new Vector3(cs * 0.55f, sh * 0.5f, cs * 0.55f), QuayTint);
				Mark(berth.Water.X, (berth.Level + 1) * sh + sh * 0.1f, berth.Water.Y,
					 new Vector3(cs * 0.4f, sh * 0.3f, cs * 0.4f), HullTint);
			}
		}

		// The roads between the Gates: the walk in pale yellow, and every crossing
		// that has to be built before anyone can use it in its own colour. The
		// count of those is the road's whole cost — see Passage.
		if (_showRoutes)
		{
			foreach (Passage road in d.Passages)
			{
				foreach (Vector2I cell in road.Path)
					Mark(cell.X, (Traversal.CrossLevel(d, cell.X, cell.Y) + 1) * sh + sh * 0.15f,
						 cell.Y, new Vector3(cs * 0.35f, sh * 0.3f, cs * 0.35f), RoadTint);

				foreach (Works works in road.Built)
				{
					Color tint = works.Kind switch
					{
						WorksKind.Stair => StairTint,
						WorksKind.Bridge => SpanTint,
						_ => CrossingTint,
					};
					foreach (Vector2I cell in new[] { works.From, works.To })
						Mark(cell.X,
							 (Traversal.CrossLevel(d, cell.X, cell.Y) + 1) * sh + sh * 0.55f,
							 cell.Y, new Vector3(cs * 0.7f, sh * 0.8f, cs * 0.7f), tint);
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

			// And which way the wind blows, drawn over the dune fields it made.
			// A dune field's grain is a fact about the Domain — one direction for
			// the whole island, snapped to a compass point — and until now it was
			// a local variable inside the surface pass that nothing could see.
			DrawDuneGrain(d, Mark);

			// <b>Two bounding boxes.</b> The faint one is the Domain's cube —
			// the maximal possible extent, whose walls are the grid and whose
			// law the audit holds the Gates to: nothing built may poke through.
			// The gold one is what the landmass actually takes: the tight box
			// round the land itself, keel to peak, waterfalls and Gates left
			// out — so the gap between the two is the room an arrangement is
			// not using.
			float yLo = 0f, yHi = 0f;
			float xLo = n, xHi = -1f, zLo = n, zHi = -1f;
			bool anyCol = false;
			for (int x = 0; x < n; x++)
			for (int z = 0; z < n; z++)
			{
				if (!d.HasLand(x, z)) continue;
				float keel = d.KeelLevel(x, z) * sh;
				float top = d.Spans[x, z][^1].Top * sh;
				yLo = anyCol ? Mathf.Min(yLo, keel) : keel;
				yHi = anyCol ? Mathf.Max(yHi, top) : top;
				anyCol = true;
				xLo = Mathf.Min(xLo, x);
				xHi = Mathf.Max(xHi, x);
				zLo = Mathf.Min(zLo, z);
				zHi = Mathf.Max(zHi, z);
			}

			void Box(float ax, float bx, float ay, float by, float az, float bz,
					 float girth, Color tint)
			{
				float mx = (ax + bx) * 0.5f, mz = (az + bz) * 0.5f;
				float my = (ay + by) * 0.5f;
				float lx = (bx - ax) * cs + girth, lz = (bz - az) * cs + girth;
				foreach (float y in new[] { ay, by })
				{
					Mark(mx, y, az, new Vector3(lx, girth, girth), tint);
					Mark(mx, y, bz, new Vector3(lx, girth, girth), tint);
					Mark(ax, y, mz, new Vector3(girth, girth, lz), tint);
					Mark(bx, y, mz, new Vector3(girth, girth, lz), tint);
				}
				foreach (float px in new[] { ax, bx })
				foreach (float pz in new[] { az, bz })
					Mark(px, my, pz, new Vector3(girth, by - ay, girth), tint);
			}

			if (anyCol)
			{
				// The land's own box, snug: half a cell past the outermost
				// columns, a slab past keel and peak.
				Box(xLo - 0.5f, xHi + 0.5f, yLo - sh, yHi + sh, zLo - 0.5f,
					zHi + 0.5f, cs * 0.14f, new Color(1f, 0.78f, 0.25f, 0.6f));

				// And the Domain's cube, faint, fitted vertically round
				// everything the island has — portals counted among the peaks.
				float cubeHi = yHi;
				foreach (Gate g in d.Gates)
					cubeHi = Mathf.Max(cubeHi, (g.Center.Y + Gate.Height) * sh);
				Box(-0.5f, n - 0.5f, yLo - 2f * cs, cubeHi + 2f * cs, -0.5f,
					n - 0.5f, cs * 0.1f, new Color(0.92f, 0.96f, 1f, 0.3f));
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
	/// The prevailing wind, drawn as a run of arrows across each dune field.
	///
	/// The grain is one direction for the whole Domain and it is snapped to a
	/// compass point, so an arrow is an honest picture of it rather than a
	/// decoration: the ridges lie <i>across</i> these arrows, and the readout
	/// names the same direction in letters.
	///
	/// Drawn from the centre of each dune patch, so a Domain with three dune
	/// fields gets three arrows rather than one legend in a corner nobody
	/// connects to the ground.
	/// </summary>
	private static void DrawDuneGrain(IslandData d, Action<float, float, float, Vector3, Color> mark)
	{
		int n = d.Size;
		const float sh = Terrain.SlabHeight;
		const float cs = Terrain.CellSize;
		var wind = new Color(0.98f, 0.62f, 0.30f);

		// The centre of each dune patch: the mean of its cells, per region, so one
		// arrow lands on each field rather than one per cell.
		var sumX = new Dictionary<int, long>();
		var sumZ = new Dictionary<int, long>();
		var count = new Dictionary<int, int>();

		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
		{
			if (!d.HasLand(x, z)) continue;
			if ((LandformType)d.Landform[x, z] != LandformType.Dunes) continue;
			int r = d.Region[x, z];
			sumX[r] = sumX.GetValueOrDefault(r) + x;
			sumZ[r] = sumZ.GetValueOrDefault(r) + z;
			count[r] = count.GetValueOrDefault(r) + 1;
		}

		Vector2 dir = d.DuneVector;
		foreach ((int r, int cells) in count)
		{
			if (cells < 12) continue;                     // a sliver is not a field
			float cx = sumX[r] / (float)cells;
			float cz = sumZ[r] / (float)cells;

			// Long enough to read as a direction, short enough to stay on the
			// patch: a shaft either side of the centre, tapering downwind.
			int run = Math.Clamp((int)MathF.Sqrt(cells) - 1, 4, 14);
			for (int step = -run; step <= run; step++)
			{
				float px = cx + dir.X * step;
				float pz = cz + dir.Y * step;
				int ix = Mathf.RoundToInt(px), iz = Mathf.RoundToInt(pz);
				if (ix < 0 || iz < 0 || ix >= n || iz >= n || !d.HasLand(ix, iz)) continue;

				short ground = d.SurfaceLevel(ix, iz);
				if (ground == IslandData.NoLand) continue;

				float t = (step + run) / (float)(2 * run);
				float w = Mathf.Lerp(0.75f, 0.18f, t);     // widest upwind, a point downwind
				mark(px, (ground + 1) * sh + sh * 0.9f,
					 pz, new Vector3(cs * w, sh * 0.6f, cs * w), wind);
			}
		}
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

	/// <summary>
	/// The Gates, and — where a Gate was <i>asked</i> for rather than rolled — what
	/// was asked for beside what came out.
	///
	/// The Gate parameters are the only ones set from outside the Domain, so they
	/// are the only ones where the panel saying one thing and the island another is
	/// a bug rather than a seed. The island cannot always oblige (three hanging
	/// Exits want three coasts that will take one), and when it cannot, saying so
	/// here is the difference between a limit and a broken control.
	/// </summary>
	private string GateSummary(IslandData d)
	{
		if (d.Gates.Count == 0) return "gates: none";

		var bits = new List<string>();
		int exits = 0;
		foreach (Gate g in d.Gates)
		{
			if (g.Role == GateRole.Exit) exits++;
			bits.Add($"{g.Facing} {g.Kind}{(g.Role == GateRole.Entry ? "*" : "")}");
		}

		var asked = new List<string>();
		if (Params.EntryEdge != GateEdge.Auto || Params.EntryGate != GateKind.Auto)
		{
			Gate? entry = null;
			foreach (Gate g in d.Gates) if (g.Role == GateRole.Entry) entry = g;

			bool edgeOk = Params.EntryEdge == GateEdge.Auto
				|| (entry != null && (int)entry.Value.Facing == (int)Params.EntryEdge - 1);
			bool kindOk = Params.EntryGate == GateKind.Auto
				|| (entry != null && entry.Value.Kind == Params.EntryGate);
			if (!edgeOk || !kindOk)
				asked.Add($"entry asked {Params.EntryEdge} {Params.EntryGate} — COAST WOULD NOT");
		}
		if (Params.ExitGates > 0 && exits < Params.ExitGates)
			asked.Add($"asked {Params.ExitGates} exits, got {exits} — COAST WOULD NOT");

		return "gates: " + string.Join(", ", bits) + "   (* = entry)"
			+ (asked.Count > 0 ? "\n   " + string.Join(";   ", asked) : "");
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

		// The anchor grid, built once rather than searched per column: three of the
		// anchors are Lists of cells and one is a list of berths, so asking "is
		// this cell an anchor?" from inside the draw loop would be a linear scan
		// tens of thousands of times over.
		byte[,]? anchor = _view == View.Anchors ? AnchorGrid(d) : null;

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
					case View.Surface:
						col.Add(MaterialColor((SurfaceMaterial)d.Material[x, z]));
						break;
					case View.Anchors:
						col.Add(AnchorColor(d, x, z, anchor));
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
	/// <summary>
	/// The control panel, and the two text plates that go with it.
	///
	/// <para>Every knob in here is also a key, and the keys came first — but a key
	/// list is a poor interface for twenty-odd settings, and cycling an enum of
	/// twenty-three arrangements one keypress at a time is worse. So the panel is
	/// the interface and the keys are the shortcut: both write the same
	/// <see cref="Params"/>, and <see cref="Sync"/> pulls the widgets back into
	/// line whenever a key moves something behind their back.</para>
	///
	/// <para>It lives in a <see cref="ScrollContainer"/> pinned to the left edge at
	/// a fixed width, so it cannot grow into the view however long the lists get,
	/// and it is collapsed by <b>Tab</b> when you want to look at the island.</para>
	/// </summary>
	private void BuildOverlayUi()
	{
		var layer = new CanvasLayer();
		AddChild(layer);

		var frame = new MarginContainer();
		frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		foreach (string side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
			frame.AddThemeConstantOverride(side, 10);
		frame.MouseFilter = Control.MouseFilterEnum.Ignore;
		layer.AddChild(frame);

		var columns = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		columns.AddThemeConstantOverride("separation", 10);
		frame.AddChild(columns);

		// ---- left: the controls ------------------------------------------------
		_panel = new PanelContainer
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(PanelWidth, 0),
		};
		_panel.AddThemeStyleboxOverride("panel", Plate());
		columns.AddChild(_panel);

		var scroll = new ScrollContainer
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		_panel.AddChild(scroll);

		var rows = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		rows.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(rows);

		var doing = new HBoxContainer();
		rows.AddChild(doing);
		Button(doing, "New seed  (N)", () => { Seed = (int)(GD.Randi() & 0x7FFFFFFF); Sync(); });
		Button(doing, "Frame  (F)", () => _rig.Frame(_islandCenter, _islandRadius));
		Button(doing, "Rebuild  (R)", Rebuild);

		Heading(rows, "what the island is");
		_viewPick = Choice<View>(rows, "View  (C)", () => _view,
			v => { _view = v; Rebuild(); });
		// The supported footprints and nothing between: every constant in the
		// pipeline is audited at exactly these sizes (the audit's Sizes sweep).
		// A dropdown, because the roster is picked from, not typed.
		rows.AddChild(Caption("Size, cells"));
		_size = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		foreach (int s in IslandParams.SupportedSizes)
			_size.AddItem($"{s} × {s}  ({s} slabs tall)", s);
		_size.Selected = _size.GetItemIndex(Params.Size);
		_size.ItemSelected += _ => { if (!_syncing) Params.Size = _size.GetSelectedId(); };
		rows.AddChild(_size);
		_arrangePick = Choice<IslandArrangement>(rows, "Arrangement  (G)",
			() => Params.Arrangement, v => Params.Arrangement = v);
		_characterPick = Choice<TerrainCharacter>(rows, "Character  (V)",
			() => Params.Character, v => Params.Character = v);

		_newShapes = Check(rows, "Auto may roll the newer shapes  (U)",
			() => Params.NewArrangements && Params.NewLandforms,
			on => { Params.NewArrangements = on; Params.NewLandforms = on; });
		_newShapes.TooltipText =
			"Gates the dice, not the code. It widens the pool Auto draws from; "
			+ "naming an arrangement or a character by hand still builds it, and "
			+ "with both named this does nothing at all.";

		// What the flag is worth on the settings as they stand. It is the one
		// control whose effect is invisible — it changes a pool nothing on screen
		// names — so the pool is what it says.
		_poolNote = Caption("");
		_poolNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_poolNote.AddThemeColorOverride("font_color", new Color(0.62f, 0.78f, 0.95f));
		_poolNote.AddThemeFontSizeOverride("font_size", 12);
		rows.AddChild(_poolNote);

		Heading(rows, "relief");
		_hilliness = Slide(rows, "Hilliness  (H)", 0f, 1f, 0.05f,
			() => Params.Hilliness, v => Params.Hilliness = v);
		_mix = Slide(rows, "Landform mix  (M)", 0f, 1f, 0.05f,
			() => Params.LandformMix, v => Params.LandformMix = v);
		_relief = Slide(rows, "Relief", 0f, 1f, 0.05f,
			() => Params.Relief, v => Params.Relief = v);
		_wet = Slide(rows, "Rivers", 0f, 1f, 0.05f,
			() => Params.Rivers, v => Params.Rivers = v);
		_lakes = Slide(rows, "Lakes", 0f, 1f, 0.05f,
			() => Params.Lakes, v => Params.Lakes = v);
		_valleys = Slide(rows, "Valleys", 0f, 1f, 0.05f,
			() => Params.Valleys, v => Params.Valleys = v);
		_rungs = Count(rows, "Plateau rungs  (L)", 1, 8,
			() => Params.PlateauLevels, v => Params.PlateauLevels = v);
		_cliff = Count(rows, "Cliff height, slabs", 3, 16,
			() => Params.CliffHeight, v => Params.CliffHeight = v);
		_patch = Count(rows, "Region scale, cells", 6, 40,
			() => Params.RegionScale, v => Params.RegionScale = v);

		Heading(rows, "gates and crossings");
		_entryKind = Choice<GateKind>(rows, "Entry gate  (T)",
			() => Params.EntryGate, v => Params.EntryGate = v);
		_entryEdge = Choice<GateEdge>(rows, "Entry edge",
			() => Params.EntryEdge, v => Params.EntryEdge = v);
		_exits = Count(rows, "Exit gates  (0 = per seed)", 0, 3,
			() => Params.ExitGates, v => Params.ExitGates = v);
		_exitKind = Choice<GateKind>(rows, "Exit gates are",
			() => Params.ExitGate, v => Params.ExitGate = v);
		_crossings = Choice<BridgeEase>(rows, "Crossings  (Y)",
			() => Params.Crossings, v => Params.Crossings = v);

		Heading(rows, "overlays");
		_bridgeBox = Check(rows, "Bridge sites  (B)",
			() => _showBridges, on => { _showBridges = on; Redraw(); });
		_stripBox = Check(rows, "Gate landings  (J)",
			() => _showLandings, on => { _showLandings = on; Redraw(); });
		_ferryBox = Check(rows, "Ferry berths  (K)",
			() => _showFerries, on => { _showFerries = on; Redraw(); });
		_roadBox = Check(rows, "Roads between gates  (P)",
			() => _showRoutes, on => { _showRoutes = on; Redraw(); });
		_fordBox = Check(rows, "Fords  (O)",
			() => _showFords, on => { _showFords = on; Redraw(); });
		_compassBox = Check(rows, "Compass and gate vectors  (X)",
			() => _showCompass, on => { _showCompass = on; Redraw(); });

		Heading(rows, "camera");
		var keys = new Label
		{
			Text = "WASD move   Q/E rotate   MMB-drag rotate and tilt\n"
				 + "arrows tilt   wheel zoom   Shift faster\n"
				 + "Tab hides this panel",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		keys.AddThemeColorOverride("font_color", new Color(0.72f, 0.74f, 0.78f));
		rows.AddChild(keys);

		// ---- right: what the island turned out to be ---------------------------
		var right = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		right.AddThemeConstantOverride("separation", 8);
		columns.AddChild(right);

		// Both plates at the **top**. The status used to sit at the bottom, where
		// the editor's own chrome above the running game pushes it off the screen —
		// the last line of the readout was the one you most wanted to read.
		_legend = Panelled(right, Control.SizeFlags.ShrinkEnd, new Color(0.82f, 0.92f, 1f));
		_status = Panelled(right, Control.SizeFlags.ExpandFill, new Color(1f, 0.93f, 0.72f));
		right.AddChild(new Control
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});

		_legend.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_legend.CustomMinimumSize = new Vector2(420, 0);
		_legend.HorizontalAlignment = HorizontalAlignment.Right;
		_status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_legend.Text = ViewLegend(_view);
		_status.Text = "";
		Sync();
	}

	/// <summary>Cells of screen the control column takes, whatever the window is.</summary>
	private const int PanelWidth = 330;

	private PanelContainer _panel = null!;
	private OptionButton _viewPick = null!, _arrangePick = null!, _characterPick = null!;
	private OptionButton _entryKind = null!, _entryEdge = null!, _crossings = null!;
	private OptionButton _exitKind = null!;
	private HSlider _hilliness = null!, _mix = null!, _relief = null!, _wet = null!;
	private HSlider _lakes = null!, _valleys = null!;
	private SpinBox _rungs = null!, _cliff = null!, _patch = null!, _exits = null!;
	private OptionButton _size = null!;
	private Label _poolNote = null!;
	private CheckBox _newShapes = null!, _bridgeBox = null!, _stripBox = null!;

	/// <summary>
	/// What the newer-shapes flag is currently worth, in the numbers it changes.
	///
	/// The flag gates <c>Auto</c>'s pool and nothing else, so with an arrangement
	/// and a character both named by hand it is genuinely inert — and a checkbox
	/// that is inert without saying so reads as a checkbox that is broken.
	/// </summary>
	private string PoolNote()
	{
		bool newer = Params.NewArrangements && Params.NewLandforms;
		bool rollsShape = Params.Arrangement == IslandArrangement.Auto;
		bool rollsMade = Params.Character == TerrainCharacter.Auto;

		if (!rollsShape && !rollsMade)
			return "no effect here: arrangement and character are both named, so "
				+ "there is no dice roll left to gate.";

		var bits = new List<string>();
		if (rollsShape)
			bits.Add($"{IslandGenerator.AutoArrangements(newer)} of "
				+ $"{IslandGenerator.AutoArrangements(true)} arrangements");
		if (rollsMade)
			bits.Add($"{IslandGenerator.AutoCharacters(newer)} of "
				+ $"{IslandGenerator.AutoCharacters(true)} characters");
		return "Auto draws from " + string.Join(" and ", bits) + ".";
	}
	private CheckBox _ferryBox = null!, _roadBox = null!, _compassBox = null!, _fordBox = null!;
	private bool _syncing;

	/// <summary>
	/// Pulls every widget back into line with what it displays.
	///
	/// The keys and the panel are two ways to the same settings, so pressing a key
	/// has to move the widget as well — otherwise the dropdown says one thing and
	/// the island is another, which is worse than having no dropdown. The
	/// <see cref="_syncing"/> guard is what stops that write coming back round as
	/// a change signal and undoing the key.
	/// </summary>
	private void Sync()
	{
		if (_viewPick == null || Params == null) return;
		_syncing = true;

		_viewPick.Selected = (int)_view;
		_arrangePick.Selected = _arrangePick.GetItemIndex((int)Params.Arrangement);
		_characterPick.Selected = _characterPick.GetItemIndex((int)Params.Character);
		_entryKind.Selected = _entryKind.GetItemIndex((int)Params.EntryGate);
		_entryEdge.Selected = _entryEdge.GetItemIndex((int)Params.EntryEdge);
		_crossings.Selected = _crossings.GetItemIndex((int)Params.Crossings);
		_exitKind.Selected = _exitKind.GetItemIndex((int)Params.ExitGate);

		_hilliness.Value = Params.Hilliness;
		_mix.Value = Params.LandformMix;
		_relief.Value = Params.Relief;
		_wet.Value = Params.Rivers;
		_lakes.Value = Params.Lakes;
		_valleys.Value = Params.Valleys;
		_size.Selected = _size.GetItemIndex(Params.Size);
		_rungs.Value = Params.PlateauLevels;
		_cliff.Value = Params.CliffHeight;
		_patch.Value = Params.RegionScale;
		_exits.Value = Params.ExitGates;

		_newShapes.ButtonPressed = Params.NewArrangements && Params.NewLandforms;
		_poolNote.Text = PoolNote();
		_bridgeBox.ButtonPressed = _showBridges;
		_stripBox.ButtonPressed = _showLandings;
		_ferryBox.ButtonPressed = _showFerries;
		_roadBox.ButtonPressed = _showRoutes;
		_compassBox.ButtonPressed = _showCompass;
		_fordBox.ButtonPressed = _showFords;

		_syncing = false;
	}

	private static void Heading(Container into, string text)
	{
		var label = new Label { Text = text.ToUpperInvariant() };
		label.AddThemeColorOverride("font_color", new Color(0.62f, 0.78f, 0.95f));
		label.AddThemeFontSizeOverride("font_size", 12);
		into.AddChild(new HSeparator());
		into.AddChild(label);
	}

	private static void Button(Container into, string text, Action pressed)
	{
		var button = new Godot.Button { Text = text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		button.Pressed += pressed;
		into.AddChild(button);
	}

	/// <summary>A labelled dropdown over an enum, with <c>Auto</c> first where there is one.</summary>
	private OptionButton Choice<T>(Container into, string text, Func<T> read, Action<T> write)
		where T : struct, Enum
	{
		into.AddChild(Caption(text));
		var pick = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		foreach (T value in Enum.GetValues<T>())
			pick.AddItem(Spaced(value.ToString()!), Convert.ToInt32(value));
		pick.Selected = pick.GetItemIndex(Convert.ToInt32(read()));
		pick.ItemSelected += _ =>
		{
			if (_syncing) return;
			write((T)Enum.ToObject(typeof(T), pick.GetSelectedId()));
		};
		// Capped, so a long list scrolls instead of running off the window —
		// thirty arrangements put the bottom of the popup past the bottom of
		// the screen and the newest shapes out of reach.
		pick.GetPopup().MaxSize = new Vector2I(480, 440);
		into.AddChild(pick);
		return pick;
	}

	private HSlider Slide(Container into, string text, float min, float max, float step,
						  Func<float> read, Action<float> write)
	{
		Label caption = Caption($"{text}   {read():0.00}");
		into.AddChild(caption);

		var slider = new HSlider
		{
			MinValue = min,
			MaxValue = max,
			Step = step,
			Value = read(),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 18),
		};
		slider.ValueChanged += v =>
		{
			caption.Text = $"{text}   {v:0.00}";
			if (!_syncing) write((float)v);
		};
		into.AddChild(slider);
		return slider;
	}

	private SpinBox Count(Container into, string text, int min, int max,
						  Func<int> read, Action<int> write)
	{
		var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		Label caption = Caption(text);
		caption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddChild(caption);

		var spin = new SpinBox { MinValue = min, MaxValue = max, Step = 1, Value = read() };
		spin.ValueChanged += v => { if (!_syncing) write((int)v); };
		row.AddChild(spin);
		into.AddChild(row);
		return spin;
	}

	private CheckBox Check(Container into, string text, Func<bool> read, Action<bool> write)
	{
		var box = new CheckBox { Text = text, ButtonPressed = read() };
		box.Toggled += on => { if (!_syncing) write(on); };
		into.AddChild(box);
		return box;
	}

	private static Label Caption(string text)
	{
		var label = new Label { Text = text };
		label.AddThemeColorOverride("font_color", new Color(0.85f, 0.87f, 0.9f));
		label.AddThemeFontSizeOverride("font_size", 13);
		return label;
	}

	/// <summary>"BrokenRing" reads as "Broken Ring" in a list a human has to scan.</summary>
	private static string Spaced(string name)
	{
		var text = new System.Text.StringBuilder(name.Length + 4);
		for (int i = 0; i < name.Length; i++)
		{
			if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) text.Append(' ');
			text.Append(name[i]);
		}
		return text.ToString();
	}

	private static StyleBoxFlat Plate() => new()
	{
		BgColor = new Color(0f, 0f, 0f, 0.62f),
		ContentMarginLeft = 10,
		ContentMarginRight = 10,
		ContentMarginTop = 8,
		ContentMarginBottom = 8,
		CornerRadiusTopLeft = 5,
		CornerRadiusTopRight = 5,
		CornerRadiusBottomLeft = 5,
		CornerRadiusBottomRight = 5,
	};

	/// <summary>One label on a dark plate, so text stays readable over pale terrain.</summary>
	private static Label Panelled(Container into, Control.SizeFlags flags, Color tint)
	{
		var panel = new PanelContainer
		{
			SizeFlagsHorizontal = flags,
			SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		panel.AddThemeStyleboxOverride("panel", Plate());
		into.AddChild(panel);

		var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
		label.AddThemeColorOverride("font_color", tint);
		panel.AddChild(label);
		return label;
	}
}
