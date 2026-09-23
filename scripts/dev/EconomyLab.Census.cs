using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// Two shell reports. The census counts a web's varieties, so the question "does this blow up?"
/// has a number: for every good, how many varieties the web can make of it and how many stacks
/// those fall into when units stack by property; then the same web with every slot passing
/// variety on, which is what restraint is saving us from. The balance prints a web's balance
/// sheet (<see cref="WebBalance"/>): what flows where in a day, and where it starves or piles up.
/// <c>godot --path . --headless scenes/dev/economy_lab.tscn -- census web=variety-ledger</c>
/// <c>godot --path . --headless scenes/dev/economy_lab.tscn -- balance web=hearth</c>
/// </summary>
public partial class EconomyLab
{
	private void BalanceSheet(string[] args)
	{
		string id = args.FirstOrDefault(a => a.StartsWith("web="))?[4..] ?? "hearth";
		if (!Store.HasWeb(id))
		{
			GD.PrintErr($"Economy lab: no web \"{id}\".");
			return;
		}
		EconomyWeb web = Store.LoadWeb(id);
		WebAnalysis analysis = WebAnalysis.Of(web);
		WebBalance balance = WebBalance.Of(web, analysis);
		string N(double v) => WebBalance.Num(v);

		GD.Print($"BALANCE of {web.Name} ({id}): {N(balance.Heads)} heads; {WebBalance.Pct(balance.Coverage)} of what they want reaches them" +
			(balance.Rounds > 1 ? $"; a loop settled in {balance.Rounds} rounds." : "."));
		foreach (string note in balance.Notes) GD.Print("  " + note);

		GD.Print("\nConsumers (want a day; get; by good):");
		foreach (WebBalance.ConsumerFlow flow in balance.Consumers)
		{
			Consumer consumer = web.Consumer(flow.Id)!;
			string by = string.Join(", ", flow.ByGood.Select(p => $"{web.Palette.Find(p.Good)?.Name ?? p.Good} {N(p.Got)}"));
			GD.Print($"  {(consumer.Name.Length > 0 ? consumer.Name : consumer.Id),-24} {N(flow.Demand),8} {N(flow.Got),8}  {WebBalance.Pct(flow.Coverage),4}   {by}");
		}

		GD.Print("\nRecipes (runs asked a day; runs managed; days a run; at work; of a limit; yield; held back by):");
		foreach (WebBalance.RecipeFlow flow in balance.Recipes)
		{
			Recipe recipe = web.Recipe(flow.Id)!;
			string held = flow.LimitedBy is { } i ? "input " + (i + 1) + " (" + string.Join("/", recipe.Inputs[i].Accepts) + ")" : flow.AtLimit ? "its limit" : "";
			string limit = recipe.Limit is { } l ? N(l) : "-";
			string yield = flow.Yield > 1 + 1e-6 ? "+" + WebBalance.Pct(flow.Yield - 1) : "";
			GD.Print($"  {analysis.TitleOf(recipe),-24} {N(flow.Desired),8} {N(flow.Runs),8} {N(flow.Days),6} {N(flow.Workshops),8} {limit,6} {yield,6}   {held}");
		}

		GD.Print("\nGoods (made a day; wanted; needed firmly; taken; state):");
		foreach (WebBalance.GoodFlow flow in balance.Goods)
			GD.Print($"  {web.Palette.Find(flow.Id)?.Name ?? flow.Id,-24} {N(flow.Made),8} {N(flow.Wanted),8} {N(flow.Needed),8} {N(flow.Taken),8}   {flow.State.ToString().ToLowerInvariant()}");
	}

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
