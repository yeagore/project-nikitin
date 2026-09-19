using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin.Generation;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Dev;

/// <summary>
/// The cursor's column, read off the mesh's colliders: the first thing to use them.
/// The readout is two parts: what the column is in any view, and under it what the
/// current view says about it — the anchors it is on in the anchors view, the body it
/// belongs to in the navigable one — so the cell answers the question the map is asking.
/// </summary>
public partial class IslandLab
{
	private string _pickText = "";
	private string _fpsText = "";

	/// <summary>The column and view the readout was last written for; it is rewritten only when one of them moves.</summary>
	private (int X, int Z, int Slab, View View, IslandData? Data) _picked = (-1, -1, 0, View.Height, null);

	/// <summary>
	/// Casts a ray from the camera through the cursor; a hit on a chunk's collider
	/// names the column and what the pipeline said about it. Physics-side, since
	/// that is where the space state is current. Nothing is cast while the cursor is
	/// over a plate: the island under a plate is not what is being pointed at.
	/// </summary>
	public override void _PhysicsProcess(double delta)
	{
		if (!_showMesh || _data == null || _mesh == null) { ClearPick(); return; }
		if (_shotPick.X >= 0)
		{
			// A shell run has no cursor over its window: the readout is pinned to the cell it named.
			if (InBounds(_data.Size, _shotPick.X, _shotPick.Y) && _data.HasLand(_shotPick.X, _shotPick.Y))
				Point(_data, _shotPick.X, _shotPick.Y, _data.SurfaceLevel(_shotPick.X, _shotPick.Y));
			return;
		}
		if (GetViewport().GuiGetHoveredControl() != null) return;
		Camera3D? cam = GetViewport().GetCamera3D();
		if (cam == null) { ClearPick(); return; }

		Vector2 mouse = GetViewport().GetMousePosition();
		Vector3 from = cam.ProjectRayOrigin(mouse);
		Vector3 to = from + cam.ProjectRayNormal(mouse) * 4000f;
		Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(
			PhysicsRayQueryParameters3D.Create(from, to));
		if (hit.Count == 0) { ClearPick(); return; }

		// A touch inside the solid along the normal, so a side face resolves to its own
		// column and a top to the slab under it rather than the air above.
		Vector3 p = hit["position"].AsVector3() - hit["normal"].AsVector3() * 0.02f;
		Vector3 local = _mesh.ToLocal(p);
		int x = Mathf.RoundToInt(local.X / Terrain.CellSize);
		int z = Mathf.RoundToInt(local.Z / Terrain.CellSize);
		int slab = Mathf.FloorToInt(local.Y / Terrain.SlabHeight);
		if (x < 0 || z < 0 || x >= _data.Size || z >= _data.Size || !_data.HasLand(x, z)) { ClearPick(); return; }

		Point(_data, x, z, slab);
	}

	/// <summary>Writes the readout for a column, unless it is the one already written.</summary>
	private void Point(IslandData d, int x, int z, int slab)
	{
		var now = (x, z, slab, _view, d);
		if (now == _picked && _pickText.Length > 0) return;
		_picked = now;
		_pickText = CellSummary(d, x, z, slab) + "\n\n" + ViewFacts(d, x, z, slab);
	}

	private void ClearPick()
	{
		_pickText = "";
		_picked = (-1, -1, 0, _view, null);
	}

	/// <summary>What a column is whatever the view: where, what landform and ground, its water.</summary>
	private static string CellSummary(IslandData d, int x, int z, int slab)
	{
		short surface = d.SurfaceLevel(x, z);
		return $"cell {x},{z}   slab {slab}\n"
			+ $"{Lower((LandformType)d.Landform[x, z])}, patch {d.Region[x, z]}\n"
			+ $"ground {surface} ({surface * Terrain.SlabHeight:0.##} m), {Lower((SurfaceMaterial)d.Material[x, z])}\n"
			+ Standing(d, x, z)
			+ (d.Spans[x, z].Length > 1 ? "\nunder an overhang's lip" : "");
	}

	/// <summary>The water standing in a column, by kind, with its level and depth; or dry.</summary>
	private static string Standing(IslandData d, int x, int z)
	{
		short water = d.WaterLevel[x, z];
		if (water == IslandData.NoLand || water <= d.SurfaceLevel(x, z)) return "dry";
		string kind = d.Fluid[x, z] == (byte)FluidKind.Goo ? "goo"
			: d.River[x, z] ? (d.Navigable[x, z] ? "navigable river" : "stream") : "lake";
		int deep = d.WaterDepth(x, z);
		return $"{kind} to {water}, {deep} slab{(deep == 1 ? "" : "s")} deep";
	}

	/// <summary>What the current view says about a column, under the view's name.</summary>
	private string ViewFacts(IslandData d, int x, int z, int slab)
	{
		string body = _view switch
		{
			View.Height => HeightFacts(d, x, z, slab),
			View.Landform => LandformFacts(d, x, z),
			View.Region => RegionFacts(d, x, z),
			View.Walk => WalkFacts(d, x, z),
			View.Reach => ReachFacts(d, x, z),
			View.Navigable => NavigableFacts(d, x, z),
			View.Surface => SurfaceFacts(d, x, z),
			View.Anchors => AnchorFacts(d, x, z),
			View.Moisture => $"moisture {d.Moisture[x, z]} of 255: {MoistureBand(d.Moisture[x, z])}\n"
				+ $"walk to fresh water {WaterWalk(d, x, z)}\nexposure {d.Exposure[x, z]} (the rain shadow reads it)\n"
				+ $"ruggedness {d.Ruggedness[x, z]} (sheltered broken ground keeps its damp)\n"
				+ $"background {d.Settings.Moisture:0.00}, wind {d.Settings.Wind:0.00} from {d.WindFrom}",
			View.Warmth => $"warmth {d.Warmth[x, z]} of 255: {WarmthBand(d.Warmth[x, z])}\n"
				+ (d.Hot[x, z] ? "hot water\n" : "")
				+ $"exposure {d.Exposure[x, z]} (the lee is milder)\nrim distance {d.RimDistance[x, z]} (the rim is colder)\n"
				+ $"moisture {d.Moisture[x, z]} (wet ground is tempered)\n"
				+ $"background {d.Settings.Warmth:0.00}, sun from {d.SunFrom}",
			View.Rugged => $"ruggedness {d.Ruggedness[x, z]} of 255\n"
				+ $"relief within two cells {Habitat.LocalRelief(d, x, z)} slabs",
			View.Exposure => $"exposure {d.Exposure[x, z]} of 255\nwind from {d.WindFrom}, strength {d.Settings.Wind:0.00}",
			View.Rim => $"{d.RimDistance[x, z]} cell{(d.RimDistance[x, z] == 1 ? "" : "s")} of land to the aether",
			View.Water => $"walk to fresh water {WaterWalk(d, x, z)}\n(a cell per cell along or down, two more per slab up)",
			_ => $"magick {d.Magick[x, z]} of 255\npattern {Lower(d.Settings.MagickPattern)}, density {d.Settings.MagickDensity:0.00}",
		};
		return $"{_view.ToString().ToUpperInvariant()}\n{body}";
	}

	private static string Lower<T>(T value) where T : struct, Enum => Spaced(value.ToString()).ToLowerInvariant();

	/// <summary>A band line's byte off the surface stage's own chart lines, so the readout cannot drift from the rule.</summary>
	private static int Line((string Name, int At)[] lines, string name)
	{
		foreach ((string n, int at) in lines) if (n == name) return at;
		throw new ArgumentException($"no band line called {name}");
	}

	/// <summary>The warmth band the surface stage reads a byte as.</summary>
	private static string WarmthBand(byte v)
	{
		(string, int)[] l = Surfaces.WarmthLines;
		if (v < Line(l, "SNOW")) return $"snow (under {Line(l, "SNOW")})";
		if (v < Line(l, "FRIGID")) return $"frigid (under {Line(l, "FRIGID")})";
		if (v < Line(l, "COLD")) return $"cold (under {Line(l, "COLD")})";
		if (v < Line(l, "HOT")) return $"temperate (under {Line(l, "HOT")}), {(v < Line(l, "BOG/MARSH") ? "the bog side" : "the marsh side")}";
		return v < Line(l, "SAND") ? $"hot (from {Line(l, "HOT")})" : $"hot, sand where dry (from {Line(l, "SAND")})";
	}

	/// <summary>The moisture band the surface stage reads a byte as.</summary>
	private static string MoistureBand(byte v)
	{
		(string, int)[] l = Surfaces.MoistureLines;
		if (v < Line(l, "DRY")) return $"dry (under {Line(l, "DRY")})";
		if (v < Line(l, "WET")) return $"balanced (under {Line(l, "WET")})";
		return $"wet (from {Line(l, "WET")})";
	}

	private static string WaterWalk(IslandData d, int x, int z)
		=> d.WaterDistance[x, z] == byte.MaxValue ? "out of reach (255)" : d.WaterDistance[x, z].ToString();

	private static string HeightFacts(IslandData d, int x, int z, int slab)
	{
		Span[] spans = d.Spans[x, z];
		var lines = new List<string>
		{
			$"top {spans[^1].Top}, keel {spans[0].Bottom}: {spans[0].Height} slabs of ground",
		};
		if (spans.Length > 1)
			for (int i = 1; i < spans.Length; i++)
				lines.Add($"lip {spans[i].Bottom}..{spans[i].Top}, {spans[i].Bottom - spans[i - 1].Top - 1} slabs of air under it"
					+ (slab >= spans[i].Bottom ? "  (pointed at)" : ""));
		lines.Add("steps  " + Steps(d, x, z));
		return string.Join("\n", lines);
	}

	/// <summary>The step to each cardinal neighbour, as the walk reads it: free, a scarp, a cliff, water or aether.</summary>
	private static string Steps(IslandData d, int x, int z)
	{
		string[] names = { "E", "W", "S", "N" };   // Grid.Dx/Dz order; north is -Z
		var bits = new List<string>();
		int here = Traversal.CrossLevel(d, x, z);
		for (int k = 0; k < 4; k++)
		{
			int nx = x + Dx[k], nz = z + Dz[k];
			if (!InBounds(d.Size, nx, nz) || !d.HasLand(nx, nz)) { bits.Add($"{names[k]} aether"); continue; }
			int step = Traversal.CrossLevel(d, nx, nz) - here;
			string kind = Math.Abs(step) <= Traversal.FreeStep ? ""
				: Math.Abs(step) < Traversal.CliffFace ? " scarp" : " cliff";
			string wet = d.Walk[nx, nz] == Traversal.Water && d.Walk[x, z] != Traversal.Water ? " water" : "";
			bits.Add($"{names[k]} {step:+0;-0;0}{kind}{wet}");
		}
		return string.Join("   ", bits);
	}

	private static string LandformFacts(IslandData d, int x, int z)
	{
		var flags = new List<string>();
		if (d.Pass[x, z]) flags.Add("a pass");
		if (d.Canyon[x, z]) flags.Add("a canyon");
		if (d.Fjord[x, z]) flags.Add("fjord shore");
		return $"{Lower((LandformType)d.Landform[x, z])} of a {Lower(d.Character)} Domain, high ground {Lower(d.Style)}\n"
			+ $"patch {d.Region[x, z]}, {PatchArea(d, d.Region[x, z])} cells"
			+ (flags.Count > 0 ? "\n" + string.Join(", ", flags) : "");
	}

	private static string RegionFacts(IslandData d, int x, int z)
		=> $"patch {d.Region[x, z]}, {PatchArea(d, d.Region[x, z])} cells, {Lower((LandformType)d.Landform[x, z])}\n"
			+ (OnRegionBorder(d, x, z) ? "on its border" : "inside it");

	private static int PatchArea(IslandData d, int region)
	{
		int cells = 0;
		for (int x = 0; x < d.Size; x++)
		for (int z = 0; z < d.Size; z++)
			if (d.HasLand(x, z) && d.Region[x, z] == region) cells++;
		return cells;
	}

	private static string WalkFacts(IslandData d, int x, int z)
	{
		int walk = d.Walk[x, z];
		string area;
		if (walk == Traversal.Water) area = d.Ford[x, z] ? "water, forded here" : "water: not ground";
		else if (walk < 0 || walk >= d.Areas.Count) area = "no walk area";
		else
		{
			WalkArea a = d.Areas[walk];
			string name = walk < d.Districts.Count && d.Districts[walk].Length > 0 ? $" \"{d.Districts[walk]}\"" : "";
			area = (walk == d.Mainland ? $"the mainland{name}" : a.IsDistrict ? $"district{name}" : "broken ground")
				+ $", area {walk}: {a.Area} cells, levels {a.Low}..{a.High}"
				+ (a.IsDistrict ? "\nsomewhere to build" : $"\nunder {Traversal.MinDistrictArea} cells: not a place");
		}
		return area + "\nsteps  " + Steps(d, x, z);
	}

	private static string ReachFacts(IslandData d, int x, int z)
	{
		int reach = d.Reach[x, z];
		if (reach == Traversal.Water) return "water: not ground";
		if (reach < 0 || reach >= d.Reaches.Count) return "no reach area";
		WalkArea a = d.Reaches[reach];
		return (reach == d.Heartland ? "the heartland" : "out of reach whatever is built")
			+ $", area {reach}: {a.Area} cells, levels {a.Low}..{a.High}\n"
			+ $"crossings {d.BridgeSpan} cells; works to {Traversal.InfrastructureStep} slabs";
	}

	private string NavigableFacts(IslandData d, int x, int z)
	{
		int id = d.WaterBody[x, z];
		if (id < 0)
		{
			short water = d.WaterLevel[x, z];
			bool wet = water != IslandData.NoLand && water > d.SurfaceLevel(x, z);
			return !wet ? "land: no body"
				: d.Fluid[x, z] == (byte)FluidKind.Goo ? "goo: nothing sails it"
				: "a stream: forded, not sailed";
		}
		int cells = 0, still = 0;
		for (int bx = 0; bx < d.Size; bx++)
		for (int bz = 0; bz < d.Size; bz++)
		{
			if (d.WaterBody[bx, bz] != id) continue;
			cells++;
			if (!d.River[bx, bz]) still++;
		}
		var lines = new List<string>
		{
			$"{BodyName(d, id)}, body {id}: {cells} cells ({still} still, {cells - still} reach)",
		};
		foreach (Fall f in d.Falls)
			if (f.Cell.X == x && f.Cell.Y == z)
				lines.Add(f.OffRim ? "a lip: the body ends here, off the rim" : $"a lip: the body ends here, a fall of {f.Drop} slabs");
		for (int k = 0; k < 4; k++)
		{
			int nx = x + Dx[k], nz = z + Dz[k];
			if (!Traversal.Sailable(d, nx, nz) || d.WaterLevel[nx, nz] == d.WaterLevel[x, z]) continue;
			int step = d.WaterLevel[nx, nz] - d.WaterLevel[x, z];
			lines.Add($"water beside it stands {step:+0;-0} slab{(Math.Abs(step) == 1 ? "" : "s")}"
				+ (d.WaterBody[nx, nz] == id ? ", in this body" : $", in {BodyName(d, d.WaterBody[nx, nz])}"));
		}
		return string.Join("\n", lines);
	}

	private static string SurfaceFacts(IslandData d, int x, int z)
	{
		var flags = new List<string>();
		if (d.Beach[x, z]) flags.Add("a beach (a shape: it wears the climate's ground)");
		if (d.Delta[x, z]) flags.Add("a delta's fan");
		if (d.Hot[x, z]) flags.Add("hot water");
		return $"{Lower((SurfaceMaterial)d.Material[x, z])}"
			+ (d.Spans[x, z].Length > 1 ? "; the lip over it is stone" : "") + "\n"
			+ $"warmth {d.Warmth[x, z]}: {WarmthBand(d.Warmth[x, z])}\n"
			+ $"moisture {d.Moisture[x, z]}: {MoistureBand(d.Moisture[x, z])}\n"
			+ $"walk to fresh water {WaterWalk(d, x, z)}, ruggedness {d.Ruggedness[x, z]}"
			+ (flags.Count > 0 ? "\n" + string.Join(", ", flags) : "");
	}

	/// <summary>
	/// Every anchor list a column is on. The lists overlap and the map shows one colour,
	/// so the readout names them all and then the one the colour is.
	/// </summary>
	private static string AnchorFacts(IslandData d, int x, int z)
	{
		var c = new Vector2I(x, z);
		var on = new List<string>();
		void Listed(List<Vector2I> list, string name) { if (list.Contains(c)) on.Add(name); }

		Listed(d.CoastCells, "coast");
		if (d.Beach[x, z]) on.Add("beach");
		Listed(d.BankCells, "bank");
		bool brink = d.CliffCells.Contains(c), foot = d.CliffFootCells.Contains(c);
		if (brink) on.Add("cliff brink");
		if (foot) on.Add("cliff foot");
		if (brink && foot) on.Add("(so a cliff ledge)");
		bool scarpBrink = d.ScarpCells.Contains(c), scarpFoot = d.ScarpFootCells.Contains(c);
		if (scarpBrink) on.Add("scarp brink");
		if (scarpFoot) on.Add("scarp foot");
		if (scarpBrink && scarpFoot) on.Add("(so a scarp ledge)");
		Listed(d.RiverBedCells, "river bed");
		Listed(d.LakeBedCells, "lake bed");
		Listed(d.ShallowBedCells, "shallow bed");
		Listed(d.MidBedCells, "mid bed");
		Listed(d.DeepBedCells, "deep bed (ooze)");
		Listed(d.Deeps, "a deep");
		Listed(d.Springs, "spring");
		Listed(d.HotWater, "hot water");
		foreach (Fall f in d.Falls)
			if (f.Cell == c) on.Add(f.OffRim ? "fall off the rim" : $"fall of {f.Drop} slabs");
		if (d.Ford[x, z]) on.Add("ford");
		if (d.Landings[x, z]) on.Add("gate landing");
		Listed(d.Summits, "summit");
		Listed(d.Overhangs, "overhang lip");
		Listed(d.TerminalLakes, "a lake that swallows a river");
		Listed(d.GreatLakes, "a great lake");
		Listed(d.Deltas, "a delta's mouth");
		Listed(d.Estuaries, "an estuary's mouth");
		Listed(d.Fjords, "a fjord's mouth");
		if (d.WaterLevel[x, z] != IslandData.NoLand && d.Fluid[x, z] == (byte)FluidKind.Goo) on.Add("goo bed");

		if (on.Count == 0) return "on no anchor list: unremarkable ground";
		byte shown = AnchorGrid(d)[x, z];
		return "on: " + string.Join(", ", on)
			+ $"\ncoloured as: {(d.Spans[x, z].Length > 1 ? "overhang lip above, " : "")}{ShortAnchor(shown)}";
	}

	/// <summary>An anchor kind's legend name without its parenthesis.</summary>
	private static string ShortAnchor(int kind)
	{
		string name = DevPalette.AnchorName(kind);
		int cut = name.IndexOf(" (", StringComparison.Ordinal);
		return cut < 0 ? name : name[..cut];
	}
}
