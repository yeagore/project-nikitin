using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

/// <summary>The status text: what the island turned out to be.</summary>
public partial class IslandLab
{
	/// <summary>Which landforms this island actually got, in size order.</summary>
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

	/// <summary>The traversal analysis in one line: walk, reach, districts, works, water.</summary>
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
		int onHeart = 0;
		foreach (WalkArea a in d.Areas)
			if (a.IsDistrict && d.Heartland >= 0 && d.Reach[a.Seat.X, a.Seat.Y] == d.Heartland) onHeart++;

		int heart = d.Heartland >= 0 ? d.Reaches[d.Heartland].Area : 0;
		int rim = 0;
		foreach (Fall f in d.Falls) if (f.OffRim) rim++;
		int gooCells = 0;
		for (int x = 0; x < d.Size; x++)
		for (int z = 0; z < d.Size; z++)
			if (d.WaterLevel[x, z] != IslandData.NoLand
				&& d.Fluid[x, z] == (byte)FluidKind.Goo) gooCells++;

		return $"walk {100f * mainland / land:0}% mainland in {districts} districts "
			+ $"({onHeart} on the heartland: somewhere to build)   "
			+ $"reach {100f * heart / land:0}%   "
			+ $"passes {d.Passes.Count}   bridges {d.Bridges.Count}   "
			+ $"bodies of water {d.WaterBodies}   "
			+ $"rivers {RiverCells(d)} cells, {d.Falls.Count} falls ({rim} off the rim), "
			+ $"{d.Springs.Count} springs"
			+ (d.HotWater.Count > 0 ? $"   hot water {d.HotWater.Count} cells" : "")
			+ (d.TerminalLakes.Count > 0 ? $"   {d.TerminalLakes.Count} lake swallows a river" : "")
			+ (d.GreatLakes.Count > 0 ? $"   great lakes {d.GreatLakes.Count}" : "")
			+ (d.Deeps.Count > 0 ? $"   deeps {d.Deeps.Count}" : "")
			+ (d.Deltas.Count > 0 ? $"   deltas {d.Deltas.Count}" : "")
			+ (d.Estuaries.Count > 0 ? $"   estuaries {d.Estuaries.Count} (mouth at {Cells(d.Estuaries)})" : "")
			+ (d.Fjords.Count > 0 ? $"   fjords {d.Fjords.Count} (mouth at {Cells(d.Fjords)})" : "")
			+ (gooCells > 0 ? $"   goo {gooCells} cells (violet)" : "")
			+ (d.Geysers.Count > 0 ? $"   geysers {d.Geysers.Count}" : "");
	}

	/// <summary>
	/// The bodies of sailable water, largest first: what a hull could actually get
	/// around in, what each is made of (standing water, or a navigable reach, or a
	/// lake with its river) and how many falls end one. The navigable view's line.
	/// </summary>
	private static string WaterSummary(IslandData d)
	{
		if (d.WaterBodies == 0) return "navigable: nothing a hull could sit on";

		int n = d.Size;
		var cells = new int[d.WaterBodies];
		var still = new int[d.WaterBodies];
		for (int x = 0; x < n; x++)
		for (int z = 0; z < n; z++)
		{
			int id = d.WaterBody[x, z];
			if (id < 0) continue;
			cells[id]++;
			if (!d.River[x, z]) still[id]++;
		}

		var order = new List<int>();
		for (int i = 0; i < d.WaterBodies; i++) order.Add(i);
		order.Sort((a, b) => cells[b] != cells[a] ? cells[b].CompareTo(cells[a]) : a.CompareTo(b));

		int specks = 0;
		foreach (int id in order) if (cells[id] < 2) specks++;

		var bits = new List<string>();
		foreach (int id in order)
		{
			if (bits.Count == 6) break;
			if (cells[id] < 2) break;
			int reach = cells[id] - still[id];
			string made = still[id] == 0 ? "reach" : reach == 0 ? "still" : $"{still[id]} still, {reach} reach";
			bits.Add($"{BodyName(d, id)} {cells[id]} ({made})");
		}

		int lips = 0;
		foreach (Fall f in d.Falls) if (d.WaterBody[f.Cell.X, f.Cell.Y] >= 0) lips++;
		int listed = bits.Count;
		int rest = d.WaterBodies - listed - specks;

		return $"navigable: {d.WaterBodies} bod{(d.WaterBodies == 1 ? "y" : "ies")}"
			+ (specks > 0 ? $", {d.WaterBodies - specks} of them 2+ cells" : "")
			+ "   " + string.Join(",   ", bits)
			+ (rest > 0 ? $",   and {rest} smaller" : "")
			+ (specks > 0 ? $",   {specks} of one cell" : "")
			+ $"   {lips} fall{(lips == 1 ? " ends" : "s end")} a body";
	}

	/// <summary>A body's name (<c>Names</c> keeps them apart), or its id where the naming stage has not run. Not <c>Name</c>: that is the node's own.</summary>
	private static string BodyName(IslandData d, int id)
		=> id < d.WaterNames.Count ? d.WaterNames[id] : $"body {id}";

	/// <summary>Material shares and anchor counts; the wind always, since exposure reads it whether or not there are dunes.</summary>
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

		string wind = $"   wind from {d.WindFrom}" + (dunes > 0 ? $", dunes run {d.DuneRun}" : "")
			+ $"   sun from {d.SunFrom}";
		return $"ground: {string.Join(", ", parts)}{wind}"
			+ $"\nanchors: {d.CoastCells.Count} coast, {d.CliffCells.Count} cliff brink, "
			+ $"{d.CliffFootCells.Count} cliff foot, {d.ScarpCells.Count} scarp brink, "
			+ $"{d.ScarpFootCells.Count} scarp foot, {d.BankCells.Count} bank, "
			+ $"{d.RiverBedCells.Count} river bed, {d.LakeBedCells.Count} lake bed "
			+ $"({d.ShallowBedCells.Count} shallow, {d.MidBedCells.Count} mid, {d.DeepBedCells.Count} deep), {d.Deeps.Count} deeps, "
			+ $"{d.Summits.Count} summit, {d.Overhangs.Count} overhang, "
			+ $"{CellCount(d.Beach)} beach, {CellCount(d.Ford)} ford, "
			+ $"{d.Springs.Count} spring, {d.Falls.Count} fall, "
			+ $"{CellCount(d.Landings)} gate landing, "
			+ $"{d.SeaStacks.Count} sea stack cells";
	}

	private static int CellCount(bool[,] flags)
	{
		int total = 0;
		foreach (bool set in flags) if (set) total++;
		return total;
	}

	/// <summary>
	/// The Gates, and "COAST WOULD NOT" where a Gate asked for is not the Gate you got.
	/// Relies on (int)Cardinal == (int)GateEdge - 1.
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

	/// <summary>What each Exit costs from the Entry, in works; zero means you can walk it.</summary>
	private static string RoadSummary(IslandData d)
	{
		if (d.Passages.Count == 0) return "roads: none";

		var bits = new List<string>();
		foreach (Passage road in d.Passages)
		{
			int ladders = 0, stairs = 0, spans = 0;
			foreach (Works w in road.Built)
			{
				if (w.Kind == WorksKind.Ladder) ladders++;
				else if (w.Kind == WorksKind.Stair) stairs++;
				else spans++;
			}
			Gate exit = d.Gates[road.Exit];
			bits.Add($"{exit.Facing} cost {road.Cost}"
				+ (road.Cost > 0 ? $" ({ladders}l {stairs}s {spans}b)" : ""));
		}
		return "roads from the entry: " + string.Join(",   ", bits);
	}

	/// <summary>What the newer-shapes flag is worth: it gates Auto's pool and nothing else.</summary>
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
			bits.Add($"{Roster.AutoArrangements(newer)} of "
				+ $"{Roster.AutoArrangements(true)} arrangements");
		if (rollsMade)
			bits.Add($"{Roster.AutoCharacters(newer)} of "
				+ $"{Roster.AutoCharacters(true)} characters");
		return "Auto draws from " + string.Join(" and ", bits) + ".";
	}

	/// <summary>Cells as "x,z" pairs, so a crack can be found with the lab's at=X,Z framing.</summary>
	private static string Cells(List<Vector2I> cells)
		=> string.Join(" ", cells.ConvertAll(c => $"{c.X},{c.Y}"));
}
