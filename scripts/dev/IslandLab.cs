using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin;
using ProjectNikitin.Generation;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Dev;

/// <summary>
/// Dev harness for island generation (<c>scenes/dev/island_lab.tscn</c>, run with F6).
/// Rebuilds whenever <see cref="Seed"/> or a <see cref="Params"/> field changes, so
/// remote-inspector edits take effect live. NOT a <c>[Tool]</c> script: generating
/// in-editor bakes the MultiMesh buffer into the scene file.
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
	private Label3D _windLabel = null!;
	private int _lastSignature;
	private IslandData? _data;

	private Vector3 _islandCenter = Vector3.Zero;
	private float _islandRadius = 10f;
	private bool _framedOnce;

	private View _view = View.Height;

	private bool _showBridges;
	private bool _showLandings;
	private bool _showFerries;
	private bool _showRoutes;
	private bool _showFords;
	private bool _showCompass = true;
	private bool _showLiquid = true;
	private bool _showPanel = true;

	public override void _Ready()
	{
		_terrain = GetNode<MultiMeshInstance3D>("Terrain");
		_rig = GetNode<CameraRig>("CameraRig");
		_unitBox = new BoxMesh { Size = Vector3.One };
		// Matte and unspecular, so a face's colour is its vertex colour times the light.
		_unitBox.Material = new StandardMaterial3D
		{
			VertexColorUseAsAlbedo = true,
			Roughness = 1f,
			Metallic = 0f,
			SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
		};

		// A steep white sun over a neutral 0.3 ambient (set on the scene's Environment,
		// with linear tonemapping): a top face reads at about the legend's colour and
		// the shaded sides still separate. Oriented here: a rotated basis in the .tscn
		// is the transpose gotcha.
		var sun = GetNode<DirectionalLight3D>("Sun");
		sun.LookAt(sun.GlobalPosition + new Vector3(0.35f, -0.85f, 0.45f), Vector3.Up);

		// Water is one flat quad per cell, not a box: alpha-blended boxes draw their
		// shared faces twice and that doubled alpha is a dark grid line on every edge.
		_waterQuad = new PlaneMesh
		{
			Size = new Vector2(Terrain.CellSize, Terrain.CellSize),
			Orientation = PlaneMesh.OrientationEnum.Y,
		};
		_waterQuad.Material = WaterMaterial(0.66f);
		if (_waterQuad.Material is StandardMaterial3D lit) lit.VertexColorUseAsAlbedo = true;
		_water = Sheet("Water");

		// Goo gets its own material: the water material's blue albedo multiplies any
		// warm vertex tint down to nothing.
		_gooQuad = new PlaneMesh
		{
			Size = new Vector2(Terrain.CellSize, Terrain.CellSize),
			Orientation = PlaneMesh.OrientationEnum.Y,
		};
		_gooQuad.Material = GooMaterial();
		_goo = Sheet("Goo");

		_fallQuad = new PlaneMesh
		{
			Size = Vector2.One,
			Orientation = PlaneMesh.OrientationEnum.Z,
		};
		_fallQuad.Material = WaterMaterial(0.75f);
		// RenderPriority 1: both sheets sit at the world origin, so without it the
		// falls and the water sort against each other by camera distance and pop.
		if (_fallQuad.Material is StandardMaterial3D fallLit) fallLit.RenderPriority = 1;
		_falls = Sheet("Falls");

		// A Gate is one cell by four slabs; NoDepthTest so a Gate on the far side is findable.
		_gateBox = new BoxMesh { Size = Vector3.One };
		_gateBox.Material = GateMaterial();
		_gates = Sheet("Gates");

		// Overlay markers: unshaded so they never read as terrain, depth-tested so a
		// mountain hides what is behind it.
		_markBox = new BoxMesh { Size = Vector3.One };
		_markBox.Material = MarkMaterial();
		_marks = Sheet("Overlays");

		BuildCompass();
		BuildOverlayUi();
		Rebuild();
	}

	/// <summary>A shadowless MultiMesh node, added as a child.</summary>
	private MultiMeshInstance3D Sheet(string name)
	{
		var node = new MultiMeshInstance3D
		{
			Name = name,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(node);
		return node;
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
			case Key.Tab:
			case Key.F1:
				_showPanel = !_showPanel;
				_panel.Visible = _showPanel;
				return;
			case Key.N: Seed = (int)(GD.Randi() & 0x7FFFFFFF); break;
			case Key.R: Rebuild(); break;
			case Key.F: _rig.Frame(_islandCenter, _islandRadius); break;
			case Key.C: _view = (View)(((int)_view + 1) % ViewCount); Rebuild(); break;
			case Key.V: CycleCharacter(); break;
			case Key.G: CycleArrangement(); break;
			case Key.H: Cycle(v => Params.Hilliness = v, Params?.Hilliness ?? 0.5f, "Hilliness"); break;
			case Key.M: Cycle(v => Params.LandformMix = v, Params?.LandformMix ?? 0.5f, "LandformMix"); break;
			case Key.T: CycleEntryGate(); break;
			case Key.Y: CycleCrossings(); break;
			case Key.L: CyclePlateaus(); break;
			case Key.U: CycleNewShapes(); break;
			case Key.B: _showBridges = !_showBridges; Redraw(); break;
			case Key.J: _showLandings = !_showLandings; Redraw(); break;
			case Key.K: _showFerries = !_showFerries; Redraw(); break;
			case Key.P: _showRoutes = !_showRoutes; Redraw(); break;
			// O, not D: the rig polls D every frame for strafe.
			case Key.O: _showFords = !_showFords; Redraw(); break;
			case Key.X: _showCompass = !_showCompass; Redraw(); break;
			// I, not W: the rig polls W every frame for forward.
			case Key.I: _showLiquid = !_showLiquid; Redraw(); break;
			case Key.F2: Capture(); break;
		}
		Sync();
	}

	/// <summary>Writes the viewport to <c>user://island-{seed}-{view}.png</c> — the only headless-reviewable look.</summary>
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
		// By the values, not the ints: the enum has gaps where shapes were removed.
		IslandArrangement[] all = Enum.GetValues<IslandArrangement>();
		int at = Array.IndexOf(all, Params.Arrangement);
		Params.Arrangement = all[(at + 1) % all.Length];
		GD.Print($"[IslandLab] Arrangement = {Params.Arrangement}");
	}

	private void CycleCharacter()
	{
		Params ??= new IslandParams();
		int count = Enum.GetValues<TerrainCharacter>().Length;
		Params.Character = (TerrainCharacter)(((int)Params.Character + 1) % count);
		GD.Print($"[IslandLab] Character = {Params.Character}");
	}

	// Auto -> Hanging -> Land: deliberately not GateKind's declared order.
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

	/// <summary>Steps the plateau ladder through 1..4 rungs.</summary>
	private void CyclePlateaus()
	{
		Params ??= new IslandParams();
		Params.PlateauLevels = Params.PlateauLevels >= 4 ? 1 : Params.PlateauLevels + 1;
		GD.Print($"[IslandLab] PlateauLevels = {Params.PlateauLevels} rungs "
			+ $"of {Params.CliffHeight} slabs");
	}

	/// <summary>Takes the newer arrangements and landforms in or out of <c>Auto</c>'s pool, both at once.</summary>
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

	/// <summary>
	/// The change detector <see cref="_Process"/> polls. Every field Generate reads
	/// must be hashed here, or an inspector edit silently does nothing.
	/// </summary>
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
			h.Add(Params.Moisture);
			h.Add(Params.Warmth);
		}
		return h.ToHashCode();
	}

	private void Rebuild()
	{
		if (_terrain == null || _unitBox == null) return;
		Params ??= new IslandParams();
		_lastSignature = Signature();

		ulong t0 = Time.GetTicksUsec();
		_data = IslandGenerator.Generate(Seed, Params);
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

	/// <summary>Everything but the terrain, so toggling an overlay does not regenerate the island.</summary>
	private int Redraw()
	{
		if (_data == null) return 0;
		int lakes = RenderWater(_data);
		RenderFalls(_data);
		RenderGates(_data);
		RenderOverlays(_data);
		// Liquid off shows the beds; the columns are drawn already.
		_water.Visible = _goo.Visible = _falls.Visible = _showLiquid;
		UpdateText(_data, lakes);
		return lakes;
	}

	private void UpdateText(IslandData d, int lakes)
	{
		if (_status == null) return;

		string newer = Roster.IsNewerShape(d.Arrangement)
					|| Roster.IsNewerShape(d.Character) ? " (newer shape)" : "";

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

		ShowLegend(ViewLegend(_view));
		Sync();
	}
}
