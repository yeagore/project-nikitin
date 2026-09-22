using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// The census: a shell run that counts a web's varieties, so the question "does this blow up?"
/// has a number. For every good, how many varieties the web can make of it and how many stacks
/// those fall into when units stack by property; then the same web with every slot passing
/// variety on, which is what restraint is saving us from.
/// <c>godot --path . --headless scenes/dev/economy_lab.tscn -- census web=variety-ledger</c>
/// </summary>
public partial class EconomyLab
{
	private void Census(string[] args)
	{
		string id = args.FirstOrDefault(a => a.StartsWith("web="))?[4..] ?? "variety-ledger";
		if (!Store.HasWeb(id))
		{
			GD.PrintErr($"Economy lab: no web \"{id}\".");
			return;
		}
		EconomyWeb web = Store.LoadWeb(id);
		WebAnalysis analysis = WebAnalysis.Of(web);
		List<Row> rows = Count(web, analysis);

		GD.Print($"CENSUS of {web.Name} ({id}): {web.Goods.Count} goods, {web.Recipes.Count} recipes, " +
		         $"{web.Recipes.Sum(r => r.Inputs.Count(s => s.Passes))} passing slots, {web.Recipes.Sum(r => r.Inputs.Count(s => s.GrantList.Count > 0))} granting slots.");
		Summary("as it is", rows);

		GD.Print("\nThe most varied goods (varieties; stacks by property; what varies):");
		foreach (Row row in rows.OrderByDescending(r => r.Varieties).ThenBy(r => r.Name, StringComparer.Ordinal).Take(24))
			GD.Print($"  {row.Name,-22} {Shown(row.Varieties, row.Capped),8}  {row.Stacks,4}   {row.Spaces}");

		GD.Print("\nVarieties by how many namespaces a good varies in:");
		foreach (IGrouping<int, Row> group in rows.Where(r => r.Varieties > 1).GroupBy(r => r.SpaceCount).OrderBy(g => g.Key))
			GD.Print($"  {group.Key} namespace{(group.Key == 1 ? " " : "s")}: {group.Count(),3} goods, {group.Sum(r => r.Varieties),6} varieties, the most {group.Max(r => r.Varieties)}");

		// The same web with no restraint: every slot passes on whatever its filler carries.
		EconomyWeb loose = EconomyStore.Clone(web);
		foreach (RecipeInput slot in loose.Recipes.SelectMany(r => r.Inputs)) slot.Passes = true;
		List<Row> looseRows = Count(loose, WebAnalysis.Of(loose));
		GD.Print("");
		Summary("if every slot passed variety on", looseRows);
		foreach (Row row in looseRows.OrderByDescending(r => r.Varieties).ThenBy(r => r.Name, StringComparer.Ordinal).Take(8))
			GD.Print($"  {row.Name,-22} {Shown(row.Varieties, row.Capped),22}  {row.Stacks,5}");
	}

	private sealed record Row(string Name, long Varieties, bool Capped, long Stacks, int SpaceCount, string Spaces);

	private static List<Row> Count(EconomyWeb web, WebAnalysis analysis)
	{
		var rows = new List<Row>();
		foreach (string goodId in web.Goods)
		{
			Good? good = web.Palette.Find(goodId);
			if (good == null) continue;
			VarietySet varieties = analysis.VarietiesOf(goodId);
			VarietySet stacks = analysis.StacksOf(goodId);
			List<(string Namespace, List<string> Tags)> spaces = varieties.ByNamespace();
			rows.Add(new Row(good.Name, varieties.Count, varieties.Capped, stacks.Count, spaces.Count,
				string.Join(" × ", spaces.Select(s => $"{s.Namespace} {s.Tags.Count}"))));
		}
		return rows;
	}

	private static void Summary(string title, List<Row> rows)
	{
		List<Row> varied = rows.Where(r => r.Varieties > 1).ToList();
		decimal all = rows.Sum(r => (decimal)r.Varieties), stacks = rows.Sum(r => (decimal)r.Stacks);
		GD.Print($"{char.ToUpperInvariant(title[0])}{title[1..]}: {varied.Count} of {rows.Count} goods come in more than one variety. " +
		         $"Rows in a warehouse that held everything: {rows.Count} by good, {stacks:N0} by property, {all:N0} by variety" +
		         (rows.Any(r => r.Capped) ? " (an estimate: some lists are past the cap)." : "."));
	}

	private static string Shown(long count, bool capped) => capped ? $"~{count:N0}" : count.ToString("N0");
}
