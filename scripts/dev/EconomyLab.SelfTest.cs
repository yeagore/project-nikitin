using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// <c>-- selftest</c>: the lab checks itself from a shell, headless. It copies the economy
/// folder to a scratch place, opens the webs there, and makes the changes a hand would make
/// through the same handlers the mouse calls, checking after each that the web, the canvas
/// and the undo stack agree. It never touches the real files. Prints a line per check and
/// exits 0 if all passed, 1 if not.
/// </summary>
public partial class EconomyLab
{
	private int _checks, _failed;

	/// <summary>The webs the repository ships, which the self-test holds to having no errors.</summary>
	private static readonly string[] Shipped = { "starter", "full-ledger", "tagged-ledger" };

	/// <summary>The scratch copy of the economy folder the self-test works in.</summary>
	private static string SelfTestRoot()
	{
		string from = ProjectSettings.GlobalizePath("res://resources/economy");
		string to = Path.Combine(ProjectSettings.GlobalizePath("user://"), "economy_selftest");
		if (Directory.Exists(to)) Directory.Delete(to, recursive: true);
		foreach (string dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories).Prepend(from))
			Directory.CreateDirectory(dir.Replace(from, to));
		foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
			if (!file.EndsWith(".import")) File.Copy(file, file.Replace(from, to), overwrite: true);
		return to;
	}

	private void Check(bool ok, string what)
	{
		_checks++;
		if (!ok) _failed++;
		GD.Print((ok ? "  ok    " : "  FAIL  ") + what);
	}

	/// <summary>The web, the canvas and the engine's own wire list must tell the same story.</summary>
	private void CheckCanvas(string after)
	{
		int nodes = Web.Goods.Count(id => Palette.Find(id) != null) + Web.Recipes.Count + Web.Consumers.Count;
		int wires = Analysis.Links.Select(l => (l.From, l.To, l.Kind, l.Port)).Distinct().Count();
		bool ok = _nodes.Count == nodes && _wired.Count == wires && _graph.GetConnectionList().Count == wires;
		Check(ok, $"canvas agrees with the web after {after} ({_nodes.Count}/{nodes} nodes, {_wired.Count}/{wires} wires, engine {_graph.GetConnectionList().Count})");
	}

	private void SelfTest()
	{
		GD.Print("Economy lab self-test, in " + Store.Root);
		_autosave = false;

		// ---- the files ---------------------------------------------------------
		foreach ((string id, _) in Store.ListWebs())
		{
			EconomyWeb web = Store.LoadWeb(id);
			string once = EconomyStore.ToJson(web);
			Check(EconomyStore.ToJson(EconomyStore.FromJson<EconomyWeb>(once)) == once, $"{id}: a load and a save change nothing");
			// The webs that ship must be whole; a web of the user's may be in any state, and only has to read and write.
			if (!Shipped.Contains(id)) continue;
			WebAnalysis read = WebAnalysis.Of(web);
			Check(!read.Issues.Any(i => i.Level == IssueLevel.Error), $"{id}: no errors ({read.Issues.Count} issues, {read.Links.Count} links)");
		}
		Check(Store.LoadWeb("full-ledger").Palette.Goods.All(g => g.Sign?.Extra?.ContainsKey("reading") == true), "the signs keep their readings (unknown fields survive)");

		// ---- the starter -------------------------------------------------------
		OpenWeb("starter");
		CheckCanvas("opening the starter");
		Check(Analysis.RoleOf("golem") == GoodRole.Final && Analysis.RoleOf("cu") == GoodRole.Source && Analysis.RoleOf("icu") == GoodRole.Intermediate, "roles: golem final, native copper a source, copper an intermediate");
		Check(Analysis.Depth("cu") == 0 && Analysis.Depth("icu") == 1 && Analysis.Depth("golem") >= 4, $"depth: copper 1, golem {Analysis.Depth("golem")}");
		Check(Analysis.Links.Count(l => l.To == "r.golem" && l.ByTag) == 3, "the golem's heart slot admits the three hearts by tag");
		Check(Analysis.IsConsumed("bread") && Analysis.IsConsumed("beer") && !Analysis.IsConsumed("golem"), "bread and beer are consumed, the golem is not");

		// A link onto the open port makes a slot; undo and redo walk it back and forth.
		int slots = Web.Recipe("r.golem")!.Inputs.Count;
		Link("grain", 0, "r.golem", slots);
		Check(Web.Recipe("r.golem")!.Inputs.Count == slots + 1 && Web.Recipe("r.golem")!.Inputs[slots].Accepts.SequenceEqual(new[] { "grain" }), "a link dropped on + input makes a slot");
		CheckCanvas("the new slot");
		Undo();
		Check(Web.Recipe("r.golem")!.Inputs.Count == slots, "undo takes the slot away");
		CheckCanvas("undo");
		Redo();
		Check(Web.Recipe("r.golem")!.Inputs.Count == slots + 1, "redo brings it back");
		Undo();

		// A link onto a slot that has something makes it "either"; cutting it leaves the first.
		Link("coal", 0, "r.golem", 0);
		Check(Web.Recipe("r.golem")!.Inputs[0].Accepts.SequenceEqual(new[] { "blood", "coal" }), "a link dropped on a filled slot makes it either-or");
		Unlink("coal", 0, "r.golem", 0);
		Check(Web.Recipe("r.golem")!.Inputs[0].Accepts.SequenceEqual(new[] { "blood" }), "cutting it leaves the first");
		CheckCanvas("link and cut");

		// A link a tag brings cannot be cut by hand.
		string before = EconomyStore.ToJson(Web);
		Unlink("h1", 0, "r.golem", 2);
		Check(EconomyStore.ToJson(Web) == before, "a link a tag brings refuses to be cut");

		// The tag is the rule: tag bread a heart and it fits the golem.
		Change("tagged bread a heart", () => Palette.Find("bread")!.Tags.Add("kind:golem-heart"));
		Check(Analysis.Links.Any(l => l.From == "bread" && l.To == "r.golem" && l.ByTag), "a good given the tag links itself into the slot");
		CheckCanvas("the tag");
		Undo();
		Check(!Palette.Find("bread")!.Has("kind:golem-heart") && !Analysis.Links.Any(l => l.From == "bread" && l.To == "r.golem"), "undo takes the tag and the link away");

		// An output re-aimed, then cut.
		Link("r.bread", 0, "beer", 0);
		Check(Web.Recipe("r.bread")!.Outputs[0].Good == "beer", "a recipe's output dropped on another good re-aims it");
		Undo();
		Link("r.bread", 1, "beer", 0);
		Check(Web.Recipe("r.bread")!.Outputs.Count == 2, "dropped from output + it adds a second output");
		Unlink("r.bread", 1, "beer", 0);
		Check(Web.Recipe("r.bread")!.Outputs.Count == 1, "and cutting that removes it");
		CheckCanvas("outputs");

		// Removing a good takes the recipe that only made it, and leaves the palette alone.
		RemoveNodes(new List<string> { "golem" });
		Check(!Web.Holds("golem") && Web.Recipe("r.golem") == null && Palette.Find("golem") != null, "removing the golem removes its recipe, not its palette entry");
		CheckCanvas("removing the golem");
		Undo();
		Check(Web.Holds("golem") && Web.Recipe("r.golem") != null, "undo brings both back");
		CheckCanvas("undoing that");

		// Removing an input good empties the slot and the issue list says so.
		RemoveNodes(new List<string> { "blood" });
		Check(Web.Recipe("r.golem")!.Inputs[0].Accepts.Count == 0 && Analysis.Issues.Any(i => i.Level == IssueLevel.Error && i.Node == "r.golem"), "removing an input leaves an empty slot and an error");
		Undo();

		// A new good goes into the palette and onto the canvas in one step.
		int goods = Palette.Goods.Count;
		_goodAt = new Spot(100, 100);
		_goodThen = id => Link(id, 0, "r.golem", 2);
		_goodName.Text = "Lead heart";
		MakeGood();
		Check(Palette.Find("lead-heart") != null && Web.Holds("lead-heart") && Web.Recipe("r.golem")!.Inputs[2].Accepts.Contains("lead-heart"), "a new good lands in the palette, on the canvas and in the slot, together");
		CheckCanvas("the new good");
		Undo();
		Check(Palette.Goods.Count == goods && !Web.Holds("lead-heart") && !Web.Recipe("r.golem")!.Inputs[2].Accepts.Contains("lead-heart"), "and one undo takes all three back");

		// Consumers.
		Link("golem", 0, "c.food", 0);
		Check(Analysis.IsConsumed("golem"), "a good linked to a consumer is consumed");
		Unlink("golem", 0, "c.food", 0);
		Check(!Analysis.IsConsumed("golem"), "and cut, it is not");

		// A tag renamed follows into the slots.
		Change("renamed the heart tag", () => EconomyEdit.RenameTag(Web, "kind:golem-heart", "kind:heart"));
		Check(Web.Recipe("r.golem")!.Inputs[2].Accepts.Contains("#kind:heart") && Analysis.Links.Count(l => l.To == "r.golem" && l.ByTag) == 3, "a renamed tag keeps its slot and its three links");
		Undo();

		// A namespace renamed takes its tags, and the slots that accept them, along.
		Change("renamed the kind namespace", () => EconomyEdit.RenameNamespace(Web, "kind", "sort"));
		Check(Palette.Find("h1")!.Has("sort:golem-heart") && Web.Recipe("r.golem")!.Inputs[2].Accepts.Contains("#sort:golem-heart")
		      && Palette.Namespace("sort")?.Role == TagNamespace.Core && Palette.Namespace("kind") == null, "a renamed namespace takes its tags, its slots and its role along");
		Undo();

		// ---- varieties -------------------------------------------------------------
		// Nothing passes variety on yet, so every good is plain.
		Check(Analysis.VarietiesOf("golem").IsPlain, "with no variety tags and no passing slot the golem is one plain good");
		Change("hearts get variety tags and the golem's slots pass them on", () =>
		{
			EconomyEdit.EnsureNamespace(Palette, "heart").Role = TagNamespace.Variety;
			EconomyEdit.EnsureNamespace(Palette, "fit").Role = TagNamespace.Variety;
			Palette.Find("h1")!.Tags.Add("heart:arsenic");
			Palette.Find("h2")!.Tags.Add("heart:antimony");
			Palette.Find("h3")!.Tags.Add("heart:bismuth");
			Palette.Find("bronze")!.Tags.Add("fit:bronze-joints");
			Recipe golem = Web.Recipe("r.golem")!;
			golem.Inputs[2].Passes = true; // the heart
			golem.Inputs[4].Passes = true; // the optional bronze
		});
		VarietySet golems = Analysis.VarietiesOf("golem");
		Check(golems.Count == 6 && !golems.Capped, $"three hearts and an optional fitting make six golems from one recipe ({golems.Count})");
		Check(golems.Sets.Any(v => v.SequenceEqual(new[] { "fit:bronze-joints", "heart:arsenic" })) && golems.Sets.Any(v => v.SequenceEqual(new[] { "heart:bismuth" })),
			"among them the arsenic golem with bronze joints and the plain bismuth one");
		Check(Analysis.VarietiesOf("h1").Count == 1 && Analysis.VarietiesOf("bread").IsPlain, "a heart is one variety; bread, which nothing passes into, stays plain");

		// An authored variety at the source travels as far as the slots pass it.
		Change("grain gets varieties and they are passed down to the loaf", () =>
		{
			EconomyEdit.EnsureNamespace(Palette, "grain").Role = TagNamespace.Variety;
			Palette.Find("grain")!.Varieties = new List<Variety>
			{
				new() { Id = "wheat", Name = "Wheat", Tags = { "grain:wheat" } },
				new() { Id = "rye", Name = "Rye", Tags = { "grain:rye" } },
			};
			foreach (string recipe in new[] { "r.flour", "r.bread" }) Web.Recipe(recipe)!.Inputs[0].Passes = true;
		});
		Check(Analysis.VarietiesOf("grain").Count == 2 && Analysis.VarietiesOf("flour").Count == 2 && Analysis.VarietiesOf("bread").Count == 2
		      && Analysis.VarietiesOf("bread").Sets.Any(v => v.SequenceEqual(new[] { "grain:rye" })), "rye grain makes rye flour makes rye bread, through one flour and one bread node");
		Check(Analysis.VarietiesOf("beer").IsPlain, "beer's malt slot does not pass variety on, so beer stays plain");
		Undo();
		Undo();
		Check(Analysis.VarietiesOf("golem").IsPlain && Palette.Find("grain")!.VarietyList.Count == 0, "two undos take the varieties away");

		// Moving is a change too.
		Spot was = Web.Layout["golem"];
		_nodes["golem"].PositionOffset += new Vector2(40, 30);
		NodesMoved();
		Check(Web.Layout["golem"] == new Spot(was.X + 40, was.Y + 30), "a moved node is written to the layout");
		Undo();
		Check(Web.Layout["golem"] == was && SpotOf(_nodes["golem"]) == was, "and undo moves it back, on the canvas too");

		// Saving and opening again gives the same web.
		Change("a note", () => Web.Note += " (self-test)");
		string saved = EconomyStore.ToJson(Web);
		Save();
		Check(EconomyStore.ToJson(Store.LoadWeb("starter")) == saved, "a saved web reads back the same");

		// ---- cutting and copying chains ----------------------------------------
		OpenWeb("full-ledger");
		CheckCanvas("opening the full ledger");
		EconomyWeb cut = EconomyEdit.Cut(Web, new[] { "enamelw", "bread" }, "cut", "Cut", withOptional: false, leanPalette: true);
		WebAnalysis cutRead = WebAnalysis.Of(cut);
		Check(cut.Palette.Goods.Count == cut.Goods.Count && cut.Palette.Namespace("kind")?.Role == TagNamespace.Core && cut.Palette.Atlases.Count == 2,
			$"a lean cut's palette is just its own {cut.Goods.Count} goods, with their namespaces and sprite sheets");
		Check(EconomyEdit.Cut(Web, new[] { "bread" }, "cut2", "Cut", withOptional: false, leanPalette: false).Palette.Goods.Count == Palette.Goods.Count, "a cut with the whole palette has all of it");
		Check(cut.Goods.Count > 10 && cut.Goods.Count < 60 && !cutRead.Issues.Any(i => i.Level == IssueLevel.Error), $"a web cut from enamelware and bread is whole: {cut.Goods.Count} goods, {cut.Recipes.Count} recipes, {cut.Consumers.Count} consumers");
		Check(cut.Goods.Where(g => cutRead.RoleOf(g) == GoodRole.Source).All(g => WebAnalysis.Of(Web).RoleOf(g) == GoodRole.Source || g == "nit"), "its sources are the ledger's sources");
		Check(cut.Consumers.Any(c => c.Id == "c.food") && cut.Consumers.Any(c => c.Id == "c.wares") && cut.Consumers.All(c => c.Id != "c.clothing"), "it keeps the consumers it still reaches and no others");

		OpenWeb("starter");
		EconomyWeb ledger = Store.LoadWeb("full-ledger");
		Change("enamelware with its chain", () => EconomyEdit.CopyChain(ledger, Web, new[] { "enamelw" }, withOptional: true, new Spot(50, 50)));
		Check(Web.Holds("enamelw") && Web.Recipe("r.enamelw") != null && !Analysis.Issues.Any(i => i.Level == IssueLevel.Error), $"a chain copied into the starter is whole ({Web.Goods.Count} goods now)");
		CheckCanvas("copying a chain in");
		Undo();
		Check(!Web.Holds("enamelw"), "and undo takes the chain out");
		CheckCanvas("the last undo");

		// ---- palettes are the web's own ------------------------------------------
		string ledgerBefore = File.ReadAllText(Store.WebPath("full-ledger"));
		Change("an experiment in the starter", () =>
		{
			Palette.Find("brass")!.Name = "Brass, renamed here only";
			Palette.Add(new Good { Id = "unobtanium", Name = "Unobtanium" });
			EconomyEdit.RenameTag(Web, "kind:metal", "kind:shiny");
		});
		Save();
		Check(File.ReadAllText(Store.WebPath("full-ledger")) == ledgerBefore && Store.LoadWeb("full-ledger").Palette.Find("unobtanium") == null,
			"renaming, tagging and inventing goods in one web leaves another web's file untouched");
		Undo();
		Save();

		// A clean web takes goods from another by import, which copies.
		var clean = new EconomyWeb { Id = "clean", Name = "Clean" };
		int came = EconomyEdit.ImportGoods(ledger, clean, new[] { "h1", "brass" });
		Check(came == 2 && clean.Palette.Goods.Count == 2 && clean.Goods.Count == 0 && clean.Palette.Namespace("kind")?.Role == TagNamespace.Core
		      && clean.Palette.Tags.Any(t => t.Id == "kind:golem-heart") && clean.Palette.Atlas("icons") != null,
			"imported goods land in the palette only, with their tag notes, namespaces and sprite sheets");
		clean.Palette.Find("brass")!.Name = "Changed";
		Check(ledger.Palette.Find("brass")!.Name == "Brass" && EconomyEdit.ImportGoods(ledger, clean, new[] { "brass" }) == 0 && clean.Palette.Find("brass")!.Name == "Changed",
			"the copy is its own, and importing again does not overwrite it");
		List<string> arrived = EconomyEdit.CopyChain(ledger, clean, new[] { "golem" }, withOptional: false);
		Check(clean.Holds("golem") && clean.Palette.Find("cu") != null && !WebAnalysis.Of(clean).Issues.Any(i => i.Level == IssueLevel.Error),
			$"a chain copied into a clean web brings the goods its palette lacked ({arrived.Count} nodes, {clean.Palette.Goods.Count} goods in the palette)");

		// Deleting from the palette takes the good out of the web altogether.
		Change("deleted the golem from the palette", () => EconomyEdit.DeleteGood(Web, "golem"));
		Check(Palette.Find("golem") == null && !Web.Holds("golem") && Web.Recipe("r.golem") == null, "a good deleted from the palette is gone from the canvas and so is its recipe");
		CheckCanvas("deleting from the palette");
		Undo();
		Check(Palette.Find("golem") != null && Web.Holds("golem"), "and undo brings it back");

		// ---- the tagged ledger: the worked example of tag slots and varieties ---------
		if (Store.HasWeb("tagged-ledger"))
		{
			OpenWeb("tagged-ledger");
			CheckCanvas("opening the tagged ledger");
			int lists = Web.Recipes.SelectMany(r => r.Inputs).Count(slot => slot.Accepts.Count(a => !Acceptor.IsTag(a)) > 1);
			Check(lists == 1, $"one either-or list is left in it, the spectacle frames ({lists})");
			Check(Web.Recipes.All(r => r.Inputs.All(slot => !slot.Accepts.Where(Acceptor.IsTag)
				.Any(a => r.Outputs.Any(o => Palette.Find(o.Good)?.Has(Acceptor.TagOf(a)) == true)))), "no tag slot admits its own recipe's output");
			VarietySet bread = Analysis.VarietiesOf("bread"), golem = Analysis.VarietiesOf("golem");
			Check(bread.Count == 3 && Analysis.VarietiesOf("beer").Count == 3 && Analysis.VarietiesOf("soap").Count == 3 && Analysis.VarietiesOf("jewel").Count == 5,
				"three breads, three beers, three soaps, five jewels, each from one recipe");
			Check(golem.Capped && golem.Count == 17 * 3 * 16, $"the golem: seventeen soils, three hearts, four optional fittings, about {golem.Count} varieties from one recipe");
			Check(Analysis.VarietiesOf("icu").IsPlain, "and copper, which nothing marks, is plain");
		}

		GD.Print(_failed == 0 ? $"Economy lab self-test: all {_checks} checks passed." : $"Economy lab self-test: {_failed} of {_checks} checks FAILED.");
		GetTree().Quit(_failed == 0 ? 0 : 1);
	}
}
