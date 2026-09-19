using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin;
using ProjectNikitin.Generation;
using ProjectNikitin.Meshing;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Dev;

/// <summary>
/// Dev harness for island generation (<c>scenes/dev/island_lab.tscn</c>, run with F6).
/// Rebuilds whenever <see cref="Seed"/> or a <see cref="Params"/> field changes, so
/// remote-inspector edits take effect live. NOT a <c>[Tool]</c> script: generating
/// in-editor bakes the MultiMesh buffer into the scene file. The ground is drawn by
/// the game's <see cref="IslandRenderer"/>, or (Z) as the old box per span.
/// </summary>
public partial class IslandLab : Node3D
{
	[Export] public int Seed { get; set; } = 1337;
	[Export] public IslandParams Params { get; set; } = null!;

	private IslandRenderer _mesh = null!;
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

	// Water twice over: the blue every view but the navigable one draws, and the
	// white-albedo pair that lets a body's own hue through (ApplyWaterMaterial).
	private StandardMaterial3D _blueWater = null!;
	private StandardMaterial3D _flatWater = null!;
	private StandardMaterial3D _blueFall = null!;
	private StandardMaterial3D _flatFall = null!;
	private TerrainMaterials _blueMaterials = null!;
	private TerrainMaterials _flatMaterials = null!;

	private readonly List<Label3D> _compass = new();
	private Label3D _windLabel = null!;
	private Label3D _sunLabel = null!;
	private int _lastSignature;
	private IslandData? _data;

	/// <summary>The lip of every fall that ends a body of sailable water; the navigable view marks them.</summary>
	private HashSet<Vector2I> _fallLips = new();

	private Vector3 _islandCenter = Vector3.Zero;
	private float _islandRadius = 10f;
	private bool _framedOnce;

	private View _view = View.Height;

	private bool _showBridges = true;
	private bool _showLandings = true;
	private bool _showRoutes = true;
	private bool _showFords = true;
	private bool _showCompass = true;
	private bool _showLiquid = true;
	private bool _showMesh = true;
	private bool _showPanel = true;

	/// <summary>Frame at which a shell run (<c>-- shot</c>) saves its screenshot and quits; 0 for never.</summary>
	private ulong _shotAt;

	/// <summary>How much closer than the whole island the first framing sits (<c>zoom=4</c> frames a quarter of it), and where.</summary>
	private float _shotZoom = 1f;
	private Vector3 _shotOffset = Vector3.Zero;

	/// <summary>A cell a shell run pins the cell readout to (<c>pick=X,Z</c>), since no cursor is over the window then; X is -1 for none.</summary>
	private Vector2I _shotPick = new(-1, -1);

	/// <summary>Degrees above the horizon a shell run's first framing looks from (<c>tilt=N</c>; <c>under</c> is -40, up at the keel); NaN leaves the rig's own.</summary>
	private float _shotTilt = float.NaN;

	/// <summary>Degrees a shell run's first framing is turned about the island (<c>yaw=N</c>; 180 looks from the north).</summary>
	private float _shotYaw;

	public override void _Ready()
	{
		ReadShellArgs();
		_terrain = GetNode<MultiMeshInstance3D>("Terrain");
		_rig = GetNode<CameraRig>("CameraRig");
		// The game's renderer, and the same materials on the boxes so the two modes read alike.
		_mesh = new IslandRenderer { Name = "Mesh" };
		AddChild(_mesh);
		_unitBox = new BoxMesh { Size = Vector3.One };
		_unitBox.Material = TerrainMaterials.GroundMaterial();

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
		_blueWater = TerrainMaterials.WaterMaterial(0.66f);
		_flatWater = TerrainMaterials.WaterMaterial(0.72f, Colors.White);
		_waterQuad.Material = _blueWater;
		_water = Sheet("Water");

		// Goo gets its own material: the water material's blue albedo multiplies any
		// warm vertex tint down to nothing.
		_gooQuad = new PlaneMesh
		{
			Size = new Vector2(Terrain.CellSize, Terrain.CellSize),
			Orientation = PlaneMesh.OrientationEnum.Y,
		};
		_gooQuad.Material = TerrainMaterials.GooMaterial();
		_goo = Sheet("Goo");

		_fallQuad = new PlaneMesh
		{
			Size = Vector2.One,
			Orientation = PlaneMesh.OrientationEnum.Z,
		};
		_blueFall = TerrainMaterials.WaterMaterial(0.75f);
		_flatFall = TerrainMaterials.WaterMaterial(0.8f, Colors.White);
		// RenderPriority 1: both sheets sit at the world origin, so without it the
		// falls and the water sort against each other by camera distance and pop.
		_blueFall.RenderPriority = _flatFall.RenderPriority = 1;
		_fallQuad.Material = _blueFall;
		_blueMaterials = new TerrainMaterials { Water = _blueWater, Falls = _blueFall };
		_flatMaterials = new TerrainMaterials { Water = _flatWater, Falls = _flatFall };
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
		ShowPlates(_showPanel);
		Rebuild();
	}

	/// <summary>Every plate at once, for a clear look at the island: the controls, the readout, the legend and the cell.</summary>
	private void ShowPlates(bool on)
	{
		_showPanel = on;
		_panel.Visible = _statusPlate.Visible = _legendPlate.Visible = on;
		_cellPlate.Visible = on && _showMesh;
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
		if (_fps != null)
		{
			if ((Engine.GetProcessFrames() & 31) == 0) _fpsText = $"{Engine.GetFramesPerSecond():0} fps";
			_fps.Text = _fpsText;
			FitPlates();
		}
		if (_shotAt != 0 && Engine.GetProcessFrames() >= _shotAt)
		{
			_shotAt = 0;
			Capture();
			GetTree().Quit();
		}
	}

	/// <summary>
	/// A windowed run from a shell, for a look without a hand on the keys:
	/// <c>godot --path . scenes/dev/island_lab.tscn -- shot [boxes] [nopanel] [seed=N]
	/// [view=NAME] [zoom=N] [at=X,Z] [pick=X,Z] [tilt=DEG] [yaw=DEG] [under]</c> builds the island, frames it
	/// (a quarter of it at <c>zoom=4</c>, centred on cell X,Z if given; from <c>tilt</c> degrees
	/// above the horizon, or from below with <c>under</c>), pins the cell readout to a cell, saves the screenshot Capture writes
	/// a few frames in, and quits. Not headless: a screenshot needs a viewport.
	/// </summary>
	private void ReadShellArgs()
	{
		var inv = System.Globalization.CultureInfo.InvariantCulture;
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (arg == "shot") _shotAt = 8;
			else if (arg == "boxes") _showMesh = false;
			else if (arg == "noliquid") _showLiquid = false;   // the beds, as I does in the lab
			else if (arg == "nopanel") _showPanel = false;
			else if (arg == "under") _shotTilt = -40f;
			else if (arg.StartsWith("yaw=") && float.TryParse(arg.AsSpan(4), System.Globalization.NumberStyles.Float, inv, out float yaw)) _shotYaw = yaw;
			else if (arg.StartsWith("tilt=") && float.TryParse(arg.AsSpan(5), System.Globalization.NumberStyles.Float, inv, out float tilt)) _shotTilt = tilt;
			else if (arg.StartsWith("pick=") && arg[5..].Split(',') is { Length: 2 } pick
					 && int.TryParse(pick[0], out int px) && int.TryParse(pick[1], out int pz))
				_shotPick = new Vector2I(px, pz);
			else if (arg.StartsWith("seed=") && int.TryParse(arg.AsSpan(5), out int seed)) Seed = seed;
			else if (arg.StartsWith("view=") && Enum.TryParse(arg[5..], true, out View view)) _view = view;
			else if (arg.StartsWith("zoom=") && float.TryParse(arg.AsSpan(5), System.Globalization.NumberStyles.Float, inv, out float zoom) && zoom > 0f) _shotZoom = zoom;
			else if (arg.StartsWith("at=") && arg[3..].Split(',') is { Length: 2 } xz
					 && int.TryParse(xz[0], out int ax) && int.TryParse(xz[1], out int az))
				_shotOffset = new Vector3(ax, 0f, az);
		}
	}

	/// <summary>The first framing: the whole island, or the part a shell run asked for.</summary>
	private void FrameFirst()
	{
		if (_data == null) return;
		if (!float.IsNaN(_shotTilt)) _rig.Tilt(_shotTilt);
		if (_shotYaw != 0f) _rig.RotateY(Mathf.DegToRad(_shotYaw));
		if (_shotZoom == 1f) { _rig.Frame(_islandCenter, _islandRadius); return; }
		const float cs = Terrain.CellSize;
		float half = _data.Size * 0.5f;
		Vector3 at = _shotOffset == Vector3.Zero
			? _islandCenter
			: new Vector3((_shotOffset.X - half) * cs, _islandCenter.Y, (_shotOffset.Z - half) * cs);
		_rig.Frame(at, _islandRadius / _shotZoom);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
		switch (key.Keycode)
		{
			case Key.Tab:
			case Key.F1:
				ShowPlates(!_showPanel);
				return;
			case Key.F3: OpenStatus(!_statusOpen); return;
			case Key.F4: _legendPlate.Visible = !_legendPlate.Visible; return;
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
			case Key.P: _showRoutes = !_showRoutes; Redraw(); break;
			// O, not D: the rig polls D every frame for strafe.
			case Key.O: _showFords = !_showFords; Redraw(); break;
			case Key.X: _showCompass = !_showCompass; Redraw(); break;
			// I, not W: the rig polls W every frame for forward.
			case Key.I: _showLiquid = !_showLiquid; Redraw(); break;
			case Key.Z: ToggleMesh(!_showMesh); break;
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
		// Where the tallest inner falls, the fjords and the estuaries are, so a second shot can be aimed with at=X,Z.
		if (_data is { } w)
		{
			var inner = w.Falls.FindAll(f => !f.OffRim);
			inner.Sort((a, b) => b.Drop != a.Drop ? b.Drop.CompareTo(a.Drop) : a.Cell.X != b.Cell.X ? a.Cell.X.CompareTo(b.Cell.X) : a.Cell.Y.CompareTo(b.Cell.Y));
			if (inner.Count > 0)
				GD.Print("[IslandLab] inner falls (cell: drop) "
					+ string.Join("  ", inner.GetRange(0, Math.Min(8, inner.Count)).ConvertAll(f => $"{f.Cell.X},{f.Cell.Y}: {f.Drop}")));
		}
		if (_data is { } d && d.Fjords.Count > 0)
			GD.Print($"[IslandLab] fjord mouths {Cells(d.Fjords)}");
		if (_data is { } e && e.Estuaries.Count > 0)
			GD.Print($"[IslandLab] estuary mouths {Cells(e.Estuaries)}");
	}

	/// <summary>Steps a 0-1 knob through quarters, so its whole range is four keypresses.</summary>
	/// <summary>Steps a 0–1 knob through auto, 0, 0.25 … 1 and round.</summary>
	private void Cycle(Action<float> set, float current, string label)
	{
		Params ??= new IslandParams();
		float next = current < 0f ? 0f : Mathf.Round(current * 4f + 1f) / 4f;
		if (next > 1.001f) next = IslandParams.Auto;
		set(next);
		GD.Print($"[IslandLab] {label} = {(next < 0f ? "auto" : next.ToString("0.00"))}");
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
			h.Add(Params.Wind);
			h.Add(Params.Fjords);
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
		_fallLips = FallLips(_data);
		int drawn = RenderTerrain(_data);
		float ms = (Time.GetTicksUsec() - t0) / 1000f;
		int lakes = Redraw();
		GD.Print($"[IslandLab] seed {Seed}, {_data.Size}², {_data.Character} ({_data.Style})"
			+ $" -> {drawn} {(_showMesh ? "triangles" : "spans")}, {lakes} lakes in {ms:0.0} ms");

		if (!_framedOnce)
		{
			FrameFirst();
			_framedOnce = true;
		}
	}

	/// <summary>
	/// The cells a body of sailable water ends at: the lip of every fall standing on
	/// sailable water, whether it pours into the next body or off the rim. A fall on a
	/// stream is not one — no hull was going up it either way.
	/// </summary>
	private static HashSet<Vector2I> FallLips(IslandData d)
	{
		var lips = new HashSet<Vector2I>();
		foreach (Fall f in d.Falls)
			if (d.WaterBody[f.Cell.X, f.Cell.Y] >= 0) lips.Add(f.Cell);
		return lips;
	}

	/// <summary>Mesh or boxes: redraws the ground the other way without regenerating the island.</summary>
	private void ToggleMesh(bool mesh)
	{
		_showMesh = mesh;
		if (_data == null) return;
		RenderTerrain(_data);
		Redraw();
	}

	/// <summary>Everything but the terrain, so toggling an overlay does not regenerate the island.</summary>
	private int Redraw()
	{
		if (_data == null) return 0;
		int lakes = RenderWater(_data);
		RenderFalls(_data);
		RenderGates(_data);
		RenderOverlays(_data);
		// Liquid off shows the beds; the columns are drawn already. With the mesh on,
		// its own water stands in for the sheets, and its own falls for the fall sheets.
		_water.Visible = _goo.Visible = _falls.Visible = _showLiquid && !_showMesh;
		_mesh.LiquidVisible = _showLiquid;
		UpdateText(_data, lakes);
		return lakes;
	}

	private void UpdateText(IslandData d, int lakes)
	{
		if (_status == null) return;

		string newer = Roster.IsNewerShape(d.Arrangement)
					|| Roster.IsNewerShape(d.Character) ? " (newer shape)" : "";

		_title.Text = $"{d.Name}   seed {Seed}   {d.Size}²   {d.Arrangement}   {d.Character}{newer}"
			+ (d.Rough ? "   ROUGH GOING" : "")
			+ (d.Unmet.Length > 0 ? $"   UNMET: {d.Unmet}" : "");

		_status.Text =
			$"made of {Made(d)}   high ground {d.Style}"
			+ $"\nladder {Params.PlateauLevels} rungs x {Params.CliffHeight} slabs   "
			+ $"crossings {Params.Crossings} ({d.BridgeSpan} cells)   lakes {lakes}   "
			+ $"built in {d.Attempts} attempt{(d.Attempts == 1 ? "" : "s")}\n"
			+ SettingsSummary(d) + "\n"
			+ WalkSummary(d) + "\n"
			+ (_view == View.Navigable ? WaterSummary(d) + "\n" : "")
			+ GroundSummary(d) + "\n"
			+ GateSummary(d) + "\n"
			+ RoadSummary(d) + "\n"
			+ DrawSummary();

		ShowLegend(ViewLegend(_view));
		Sync();
		ShowRolled(d);
	}

	/// <summary>The knobs the island was built with; a star marks one the seed rolled because the panel said Auto.</summary>
	private string SettingsSummary(IslandData d)
	{
		IslandParams s = d.Settings;
		if (s == null) return "settings: none";
		string Knob(string name, float asked, float used)
			=> $"{name} {used:0.00}{(asked < 0f ? "*" : "")}";
		return "settings: "
			+ Knob("mix", Params.LandformMix, s.LandformMix) + "  "
			+ Knob("relief", Params.Relief, s.Relief) + "  "
			+ Knob("hills", Params.Hilliness, s.Hilliness) + "  "
			+ Knob("rivers", Params.Rivers, s.Rivers) + "  "
			+ Knob("lakes", Params.Lakes, s.Lakes) + "  "
			+ Knob("valleys", Params.Valleys, s.Valleys) + "  "
			+ Knob("moisture", Params.Moisture, s.Moisture) + "  "
			+ Knob("warmth", Params.Warmth, s.Warmth) + "  "
			+ Knob("wind", Params.Wind, s.Wind) + "  "
			+ Knob("fjords", Params.Fjords, s.Fjords) + "  "
			+ Knob("overhangs", Params.OverhangDensity, s.OverhangDensity) + "\n"
			+ "  magicks: "
			+ s.MagickPattern.ToString().ToLowerInvariant()
			+ (Params.MagickPattern == MagickPattern.Auto ? "*" : "") + "  "
			+ Knob("density", Params.MagickDensity, s.MagickDensity)
			+ "   (* rolled from the seed)";
	}

	/// <summary>How the ground is drawn and what it cost: the mesh's triangles and chunks, or the boxes' count.</summary>
	private string DrawSummary()
	{
		if (!_showMesh)
			return $"drawn: boxes, {_terrain.Multimesh?.InstanceCount ?? 0:N0} spans (Z for the mesh)";
		int across = _data == null ? 0 : ChunkMesher.ChunksAcross(_data.Size);
		return $"drawn: mesh, {_mesh.GroundTriangles:N0} ground + {_mesh.LiquidTriangles:N0} liquid triangles "
			+ $"in {across * across} chunks with colliders, built in {_mesh.LastBuildMs:0} ms (Z for boxes)";
	}
}
