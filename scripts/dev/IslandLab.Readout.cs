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
			+ $"ferry berths {d.Berths.Count} on {d.WaterBodies} bodies   "
			+ $"rivers {RiverCells(d)} cells, {d.Falls.Count} falls ({rim} off the rim), "
			+ $"{d.Springs.Count} springs"
			+ (d.TerminalLakes.Count > 0 ? $"   {d.TerminalLakes.Count} lake swallows a river" : "")
			+ (d.Deltas.Count > 0 ? $"   deltas {d.Deltas.Count}" : "")
			+ (gooCells > 0 ? $"   goo {gooCells} cells (violet)" : "")
			+ (d.Geysers.Count > 0 ? $"   geysers {d.Geysers.Count}" : "");
	}

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
			+ $"\nanchors: {d.CoastCells.Count} coast, {d.CliffCells.Count} brink, "
			+ $"{d.CliffFootCells.Count} foot, {d.BankCells.Count} bank, "
			+ $"{d.RiverBedCells.Count} river bed, {d.LakeBedCells.Count} lake bed, "
			+ $"{d.Summits.Count} summit, {d.Overhangs.Count} overhang, "
			+ $"{CellCount(d.Beach)} beach, {CellCount(d.Ford)} ford, "
			+ $"{d.Springs.Count} spring, {d.Falls.Count} fall, "
			+ $"{CellCount(d.Landings)} gate landing, {d.Berths.Count} quay, "
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
}
