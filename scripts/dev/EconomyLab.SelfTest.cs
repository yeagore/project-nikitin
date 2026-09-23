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
	private static readonly string[] Shipped = { "starter", "tagged-ledger", "full-ledger-reference", "variety-ledger", "hearth" };

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

	private static bool Near(double a, double b, double within = 1e-6) => Math.Abs(a - b) <= within;

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
		Check(Store.LoadWeb("full-ledger-reference").Palette.Goods.All(g => g.Sign?.Extra?.ContainsKey("reading") == true), "the signs keep their readings (unknown fields survive)");

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

		// A slot can grant a tag of its own: the fitting is a fact about the golem, not about bronze.
		Change("the golem's bronze slot grants bronze joints", () =>
		{
			EconomyEdit.EnsureNamespace(Palette, "fit").Role = TagNamespace.Variety;
			Web.Recipe("r.golem")!.Inputs[4].Grants = new List<string> { "fit:bronze-joints" };
		});
		VarietySet fitted = Analysis.VarietiesOf("golem");
		Check(fitted.Count == 2 && fitted.Sets.Any(v => v.Count == 0) && fitted.Sets.Any(v => v.SequenceEqual(new[] { "fit:bronze-joints" })) && Analysis.VarietiesOf("bronze").IsPlain,
			"an optional slot that grants a tag makes two golems, with and without, and bronze itself stays plain");
		Change("renamed the fitting", () => EconomyEdit.RenameTag(Web, "fit:bronze-joints", "fit:bronze"));
		Check(Web.Recipe("r.golem")!.Inputs[4].GrantList.SequenceEqual(new[] { "fit:bronze" }), "a renamed tag follows into the slot that grants it");
		Undo();
		Undo();

		// A recipe's element is part of the web like anything else.
		Change("iron is of fire", () => Web.Recipe("r.ife")!.Element = Element.Fire.Id);
		Check(EconomyStore.ToJson(Web).Contains("\"element\": \"fire\"") && Element.Find(Web.Recipe("r.ife")!.Element) == Element.Fire, "a recipe's element is written to the file");
		Undo();

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
		OpenWeb("full-ledger-reference");
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
		EconomyWeb ledger = Store.LoadWeb("full-ledger-reference");
		Change("enamelware with its chain", () => EconomyEdit.CopyChain(ledger, Web, new[] { "enamelw" }, withOptional: true, new Spot(50, 50)));
		Check(Web.Holds("enamelw") && Web.Recipe("r.enamelw") != null && !Analysis.Issues.Any(i => i.Level == IssueLevel.Error), $"a chain copied into the starter is whole ({Web.Goods.Count} goods now)");
		CheckCanvas("copying a chain in");
		Undo();
		Check(!Web.Holds("enamelw"), "and undo takes the chain out");
		CheckCanvas("the last undo");

		// ---- palettes are the web's own ------------------------------------------
		string ledgerBefore = File.ReadAllText(Store.WebPath("full-ledger-reference"));
		Change("an experiment in the starter", () =>
		{
			Palette.Find("brass")!.Name = "Brass, renamed here only";
			Palette.Add(new Good { Id = "unobtanium", Name = "Unobtanium" });
			EconomyEdit.RenameTag(Web, "kind:metal", "kind:shiny");
		});
		Save();
		Check(File.ReadAllText(Store.WebPath("full-ledger-reference")) == ledgerBefore && Store.LoadWeb("full-ledger-reference").Palette.Find("unobtanium") == null,
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

		// ---- a locked web refuses everything ---------------------------------------
		EconomyWeb reference = EconomyStore.Clone(Web);
		reference.Id = "locked-test";
		reference.Name = "Locked test";
		reference.Locked = true;
		Store.SaveWeb(reference);
		OpenWeb("locked-test");
		string lockedFile = File.ReadAllText(Store.WebPath("locked-test"));
		string lockedWeb = EconomyStore.ToJson(Web);
		Link("grain", 0, "r.golem", 0);
		RemoveNodes(new List<string> { "golem" });
		Change("a note on a locked web", () => Web.Note = "changed");
		Spot stood = Web.Layout["golem"];
		_nodes["golem"].PositionOffset += new Vector2(50, 50);
		NodesMoved();
		Check(EconomyStore.ToJson(Web) == lockedWeb && _undo.Count == 0, "a locked web refuses links, removals, notes and moves, and keeps no undo step");
		Check(SpotOf(_nodes["golem"]) == stood, "a node dragged in a locked web is put back where it stood");
		AskBinWeb();
		BinWeb();
		Save();
		Check(File.Exists(Store.WebPath("locked-test")) && File.ReadAllText(Store.WebPath("locked-test")) == lockedFile && Web.Id == "locked-test", "it cannot be binned, and its file is never written");
		EconomyWeb freed = EconomyStore.Clone(Web);
		Check(freed.Locked, "a clone carries the lock, which New… → a copy takes off on purpose");
		File.Delete(Store.WebPath("locked-test"));

		// ---- what ships: elements on every recipe, in rough balance, and a locked reference ----
		foreach (string id in new[] { "full-ledger-reference", "tagged-ledger", "variety-ledger", "starter" })
		{
			if (!Store.HasWeb(id)) continue;
			EconomyWeb shipped = Store.LoadWeb(id);
			int without = shipped.Recipes.Count(r => Element.Find(r.Element) == null);
			Check(without == 0, $"{id}: every recipe has an element ({without} without)");
			if (id == "starter") continue;
			int four = shipped.Recipes.Count(r => r.Element != Element.Quintessence.Id);
			var shares = Element.All.Where(e => e != Element.Quintessence).Select(e => (e.Name, Share: shipped.Recipes.Count(r => r.Element == e.Id) * 100 / Math.Max(1, four))).ToList();
			Check(shares.All(p => p.Share is >= 15 and <= 35), $"{id}: the four are in rough balance ({string.Join(", ", shares.Select(p => $"{p.Name} {p.Share}%"))}; quintessence {shipped.Recipes.Count - four})");
		}
		if (Store.HasWeb("full-ledger-reference"))
		{
			EconomyWeb kept = Store.LoadWeb("full-ledger-reference");
			Check(kept.Locked && kept.Recipes.Count == 197 && kept.Goods.Count == 286, "the reference copy of the full ledger is locked and whole");
		}

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

		// ---- the variety ledger: sites, folds, properties, stacks, and the icons that follow ----
		if (Store.HasWeb("variety-ledger"))
		{
			OpenWeb("variety-ledger");
			CheckCanvas("opening the variety ledger");
			Check(Web.Locked, "the variety ledger is locked");
			Recipe peat = Web.Recipe("r.peat")!;
			Check(peat.Inputs.Count == 0 && peat.SiteList.SequenceEqual(new[] { "soil:murkearth" }) && !Analysis.Issues.Any(i => i.Node == "r.peat"),
				"peat is cut on murkearth: a site, no slot, and no complaint that it takes nothing");
			Check(!Analysis.Issues.Any(i => i.Text.Contains("a variety tag")), "no slot in it is gated by a variety tag");
			Check(Web.Goods.All(g => Analysis.MakersOf(g).Count > 0) && Web.Recipes.Where(r => r.IsExtraction).All(r => r.SiteList.Count > 0)
			      && Web.Recipes.SelectMany(r => r.SiteList).All(t => Palette.IsSite(t) || Palette.IsVariety(t)),
				$"every good of the ledger comes out of something: {Web.Recipes.Count(r => r.IsExtraction)} extractions, each on a site of site or soil tags");
			Check(Web.Recipes.Any(r => r.Outputs.Any(o => o.ByProduct)) && Web.Consumers.Any(c => c.Accepts.Contains("#need:building")) && Palette.Find("planks")!.Tags.Contains("need:building"),
				"it has by-products, and a consumer for building and upkeep that planks go to");
			Check(Palette.Find("dye") != null && Palette.Find("indigod") == null && Analysis.MakersOf("dye").Count == 6 && Analysis.VarietiesOf("dye").Count == 6,
				"the five dyes are one good in six colours, one recipe each");
			Check(Palette.Find("heart") != null && Analysis.VarietiesOf("heart").Count == 3 && Analysis.Links.Count(l => l.To == "r.golem" && l.From == "heart") == 1,
				"the three hearts are one good in three metals, and the golem takes it by name");
			Check(Analysis.MakersOf("iag").Any(r => r.Id == "r.litharge"), "cupellation yields silver");

			VarietySet golem = Analysis.VarietiesOf("golem");
			Check(golem.Count > 1000 && Analysis.StacksOf("golem").Count == 15, $"the golem: about {golem.Count} varieties, which stack by property into {Analysis.StacksOf("golem").Count} (five kinds of work by three grades of heart)");
			Check(Analysis.VarietiesIn("golem", new[] { "heart" }).Count == 3 && Analysis.VarietiesIn("golem", new[] { "heart", "soil" }).Count == 51,
				"split by heart they are three stacks; by heart and soil, fifty-one");
			Check(Analysis.VarietiesOf("bread").Count == 6 && Analysis.StacksOf("bread").Count == 3, "six breads by grain, three by grade");
			Check(Analysis.VarietiesOf("provis").IsPlain && Analysis.VarietiesOf("brandy").IsPlain, "provisions and brandy stay plain: their slots do not pass variety on");
			Check(Palette.PropertiesOf(new[] { "cloth:silk", "colour:black" }).SequenceEqual(new[] { "grade:common" }), "a scale keeps the lowest: silk in a common dye is common cloth");
			Check(Palette.PropertiesOf(new[] { "gem:jade", "metal:gold" }).SequenceEqual(new[] { "grade:superb", "prized:jadefolk", "prized:lakefolk" }), "and other properties add up: a jade-set gold jewel is superb and prized twice");
			Check(Web.Recipes.SelectMany(r => r.Inputs).Where(i => i.Passes).All(i => i.Accepts.Count > 0), "every passing slot accepts something");

			Good golemGood = Palette.Find("golem")!;
			Texture2D? plain = Sprites.Compose(golemGood, Array.Empty<string>());
			Texture2D? green = Sprites.Compose(golemGood, new[] { "heart:arsenic" });
			Texture2D? again = Sprites.Compose(golemGood, new[] { "heart:arsenic" });
			Check(plain == Sprites.Icon(golemGood.Icon) && green != null && green != plain && again == green, "a plain stack shows the plain icon on its parchment; a variety recolours it, once");
			Check(Sprites.Compose(golemGood, new[] { "stage:assembly" }) == plain, "a tag with no colour changes nothing");

			// The tag functions reach the new places a tag can be named.
			EconomyWeb copy = EconomyStore.Clone(Web);
			copy.Locked = false;
			EconomyEdit.RenameTag(copy, "soil:murkearth", "soil:mire");
			Check(copy.Recipe("r.peat")!.SiteList.SequenceEqual(new[] { "soil:mire" }) && copy.Palette.Tag("soil:mire")?.Implies?.Contains("work:water") == true
			      && copy.Palette.Find("soil")!.VarietyList.Any(v => v.Tags.Contains("soil:mire")), "renaming a tag follows it into sites, implications and varieties");
			EconomyEdit.RenameTag(copy, "work:water", "work:wet");
			Check(copy.Palette.Tag("soil:mire")!.Implies!.Contains("work:wet"), "renaming a property tag follows it into what implies it");
			EconomyEdit.RemoveTag(copy, "soil:mire");
			Check(copy.Recipe("r.peat")!.SiteList.Count == 0 && copy.Palette.Tag("soil:mire") == null, "removing it clears the site");
			EconomyEdit.RenameNamespace(copy, "soil", "ground");
			Check(copy.Palette.Find("golem")!.Layers!.Any(l => l.Match == "ground") && copy.Palette.Tag("ground:sand") != null && copy.Recipe("r.sand")!.SiteList.SequenceEqual(new[] { "ground:sand" }),
				"renaming a namespace follows it into icon layers, tag entries and sites");
		}

		// ---- the balance: Hearth by hand, then the rules on a web made for them ----
		if (Store.HasWeb("hearth"))
		{
			OpenWeb("hearth");
			CheckCanvas("opening Hearth");
			WebBalance sheet = WebBalance.Of(Web, Analysis);
			Check(sheet.HasNumbers && Near(sheet.Heads, 120), "Hearth has numbers: 120 heads");
			Check(Web.Goods.All(g => Analysis.MakersOf(g).Count > 0) && !Analysis.Issues.Any(i => i.Level != IssueLevel.Note),
				"every good in Hearth comes out of something, and the analysis has nothing worse than a note");
			Check(Analysis.RoleOf("grain") == GoodRole.Source && Analysis.Depth("grain") == 0 && Analysis.Depth("flour") == 1 && Web.Recipe("r.fields")!.IsExtraction,
				"grain comes out of an extraction and is still a source at depth 0; flour is one step up");
			// 240 wanted at Food: 120 loaves (20 bakery runs, 100 flour, 11.1 mill runs, 111 grain) and 120 milk (the byre's 20 runs eat 20 grain).
			Check(Near(sheet.Good("grain")!.Needed, 131.11, 0.01) && Near(sheet.Recipe("r.fields")!.Desired, 13.11, 0.01) && sheet.Recipe("r.fields")!.AtLimit && Near(sheet.Recipe("r.fields")!.Runs, 10),
				"the pull reaches the ground: 131 grain needed, and the fields are held at their limit of 10");
			Check(Near(sheet.Good("dung")!.Wanted, 30) && Near(sheet.Good("dung")!.Needed, 0) && Near(sheet.Recipe("r.byre")!.Desired, 20) && Near(sheet.Recipe("r.sheep")!.Desired, 7.2),
				"a by-product asks no one to run: the byre is kept for 120 milk, the flock for 7.2 wool, and the fields' 30 dung is only a wish");
			Check(Near(sheet.Recipe("r.fields")!.Yield, 1.3) && Near(sheet.Good("grain")!.Made, 130) && Analysis.Issues.Any(i => i.Text.StartsWith("A loop")),
				"the loop closes: the byre's and the flock's dung fill the fields' slot and every field gives 30% more, 130 grain");
			double share = 130 / 131.111111;
			Check(Near(sheet.Recipe("r.flour")!.Slots[0].Got / sheet.Recipe("r.flour")!.Slots[0].Wanted, share, 1e-6) && Near(sheet.Recipe("r.byre")!.Slots[0].Got / sheet.Recipe("r.byre")!.Slots[0].Wanted, share, 1e-6),
				"the mill and the byre, short of the same grain, are rationed in the same proportion");
			Check(Near(sheet.Consumer("c.food")!.Got, 240 * share, 0.01) && Near(sheet.Consumer("c.clothing")!.Coverage, 1) && Near(sheet.Consumer("c.building")!.Got, 3),
				$"the people get {WebBalance.Num(sheet.Consumer("c.food")!.Got)} of 240 food, all their clothes, and 3 planks for their roofs");
			Check(sheet.Notes.Any(n => n.StartsWith("Grain: 131 a day needed, 130 made; Fields is at its limit (10 at work)")) && sheet.Notes.Any(n => n.StartsWith("Fields: Dung filling 100% of its slot makes every run give 30% more")),
				"the notes name the root of the shortage and what the boost brought");

			// Without the dung the loop is open: the fields give 100, and the byre's grain comes out of the bread.
			EconomyWeb bare = EconomyStore.Clone(Web);
			bare.Recipe("r.fields")!.Inputs.Clear();
			WebBalance open = WebBalance.Of(bare, WebAnalysis.Of(bare));
			Check(Near(open.Good("grain")!.Made, 100) && open.Consumer("c.food")!.Got < sheet.Consumer("c.food")!.Got - 20 && !WebAnalysis.Of(bare).Issues.Any(i => i.Text.StartsWith("A loop")),
				$"without the dung the loop is open: 100 grain, {WebBalance.Num(open.Consumer("c.food")!.Got)} food instead of {WebBalance.Num(sheet.Consumer("c.food")!.Got)}");

			// The rules, on purpose-built webs.
			EconomyWeb pans = EconomyStore.Clone(Web);
			pans.Recipe("r.slt")!.Limit = null;
			pans.Recipes.Remove(pans.Recipe("r.salt")!);
			pans.Recipes.Add(new Recipe { Id = "r.pans", Name = "Salt pans", Site = new List<string> { "anchor:salt-lake" }, Outputs = { new RecipeOutput { Good = "salt" } } });
			WebBalance lake = WebBalance.Of(pans, WebAnalysis.Of(pans));
			Check(Near(lake.Recipe("r.pans")!.Runs, 4) && lake.Good("salt")!.State == WebBalance.FlowState.Even && lake.Recipe("r.pans")!.Cap == null,
				"an extraction with no limit runs as often as it is asked");

			EconomyWeb spare = EconomyStore.Clone(Web);
			spare.Recipe("r.bread")!.Inputs[1].Optional = true;
			spare.Recipe("r.slt")!.Limit = 0.5;
			WebBalance optional = WebBalance.Of(spare, WebAnalysis.Of(spare));
			Check(Near(optional.Recipe("r.bread")!.Runs, sheet.Recipe("r.bread")!.Runs) && Near(optional.Recipe("r.salt")!.Desired, 0) && Near(optional.Recipe("r.bread")!.Slots[1].Used, 0)
			      && Near(optional.Good("salt")!.Wanted, sheet.Good("salt")!.Wanted, 0.1) && optional.Good("salt")!.State != WebBalance.FlowState.Short,
				"an optional slot does not hold the recipe back, raises no maker (salt boiling stops), takes what is there, and does not call the good short");

			EconomyWeb seeded = EconomyStore.Clone(Web);
			seeded.Recipe("r.fields")!.Inputs.Add(new RecipeInput { Accepts = { "grain" }, Amount = 1 });
			WebBalance seeds = WebBalance.Of(seeded, WebAnalysis.Of(seeded));
			Check(Near(seeds.Recipe("r.fields")!.Runs, 10) && Near(seeds.Recipe("r.fields")!.Slots[1].Used, 10) && Near(seeds.Good("grain")!.Made, 130),
				"seed corn from the crop: the fields keep their seed first, and the loop settles on the most it can keep up, not on nothing");

			// Seed corn two steps round: the bloomery needs a tool to make the iron the toolsmith turns into tools.
			EconomyWeb forge = EconomyStore.Clone(Web);
			forge.Recipe("r.ife")!.Inputs.Add(new RecipeInput { Accepts = { "tools" } });
			forge.Recipe("r.tools")!.Limit = 2;
			WebBalance tooled = WebBalance.Of(forge, WebAnalysis.Of(forge));
			Check(Near(tooled.Recipe("r.ife")!.Runs, 2, 1e-6) && Near(tooled.Good("tools")!.Made, 4, 1e-6) && Near(tooled.Consumer("c.works")!.Got, 2, 1e-6)
			      && tooled.Goods.All(g => g.Taken <= g.Made + 1e-6),
				"a loop two steps round keeps its seed first: the bloomery gets its 2 tools of 4, the people the other 2, and the sheet adds up");

			// A maker at its limit is asked no more than it can make; the rest goes to the other maker.
			EconomyWeb terraced = EconomyStore.Clone(Web);
			terraced.Recipe("r.fields")!.Limit = 5;
			terraced.Recipes.Add(new Recipe { Id = "r.terraces", Name = "Terraces", Site = new List<string> { "anchor:scarp" }, Outputs = { new RecipeOutput { Good = "grain", Amount = 10 } } });
			WebBalance terraces = WebBalance.Of(terraced, WebAnalysis.Of(terraced));
			Check(Near(terraces.Recipe("r.fields")!.Runs, 5) && Near(terraces.Recipe("r.terraces")!.Desired, 8.111, 0.001) && terraces.Good("grain")!.State != WebBalance.FlowState.Short,
				"the fields at their limit give 50 of the 131 grain wanted, and the terraces are asked for the other 81");

			// A good that only comes as a by-product, and is firmly wanted, is named as such when short.
			EconomyWeb mucky = EconomyStore.Clone(Web);
			mucky.Recipe("r.fields")!.Inputs[0].Optional = false;
			mucky.Recipe("r.fields")!.Inputs[0].Amount = 6;
			mucky.Recipe("r.fields")!.Inputs[0].Boost = null;
			WebBalance muck = WebBalance.Of(mucky, WebAnalysis.Of(mucky));
			Check(muck.Good("dung")!.State == WebBalance.FlowState.Short && muck.Notes.Any(n => n.StartsWith("Dung:") && n.Contains("only as a by-product")),
				"dung firmly wanted and short is named as a by-product nothing runs for");

			EconomyWeb greedy = EconomyStore.Clone(Web);
			Recipe waste = EconomyEdit.NewRecipe(greedy, "grain", "grain", new Spot());
			waste.Inputs[0].Amount = 2;
			greedy.Recipe("r.fields")!.Outputs[0].Amount = 0;
			WebBalance runaway = WebBalance.Of(greedy, WebAnalysis.Of(greedy));
			Check(runaway.Notes.Any(n => n.StartsWith("A loop asks more of itself")), "a loop that eats two for one says so rather than hang");

			EconomyWeb blank = EconomyStore.Clone(Web);
			blank.Heads = null;
			foreach (Recipe recipe in blank.Recipes) recipe.Limit = null;
			Check(!WebBalance.Of(blank, WebAnalysis.Of(blank)).HasNumbers && EconomyStore.ToJson(blank).Contains("\"heads\"") == false && !EconomyStore.ToJson(blank).Contains("\"limit\""),
				"a web without numbers says so, and writes no heads or limits");
			Check(EconomyStore.FromJson<EconomyWeb>(EconomyStore.ToJson(Web)).Recipe("r.bread")!.Inputs[1].Count == 0.2 && Web.Recipe("r.salt")!.Inputs[0].Amount == null
			      && EconomyStore.ToJson(Web).Contains("\"byProduct\": true") && EconomyStore.ToJson(Web).Contains("\"boost\": 0.3"),
				"amounts, by-products and boosts read back, and an amount of one is not written");

			// The old supply, from a file saved before extractions, becomes the extraction it stood for.
			EconomyWeb old = EconomyStore.Clone(Web);
			old.Recipes.Remove(old.Recipe("r.fields")!);
			string json = EconomyStore.ToJson(old).Replace("\"heads\": 120,", "\"heads\": 120,\n  \"supply\": { \"grain\": 100, \"nothing\": 5 },");
			EconomyWeb migrated = EconomyStore.FromJson<EconomyWeb>(json);
			EconomyStore.MigrateSupply(migrated);
			Check(migrated.Recipe("r.land.grain") is { Limit: 100 } land && land.IsExtraction && migrated.Recipe("r.land.nothing") == null && !EconomyStore.ToJson(migrated).Contains("\"supply\""),
				"a supply in an old file becomes an extraction with that limit, and is not written again");
			Check(WebAnalysis.Of(migrated).Issues.Any(i => i.Node == "r.land.grain" && i.Text.Contains("stands nowhere")) && WebAnalysis.Of(old).Issues.Any(i => i.Node == "grain" && i.Text.Contains("comes out of nothing")),
				"an extraction with no site is told it needs one, and a good nothing makes is told every good comes out of something");

			Recipe sited = new() { Site = new List<string> { "soil:brownearth", "anchor:river", "soil:blackearth" } };
			Check(sited.SiteGroups().Count == 2 && sited.SiteGroups()[0].Tags.SequenceEqual(new[] { "soil:brownearth", "soil:blackearth" }) && Web.Palette.IsSite("anchor:river") && !Web.Palette.IsSite("soil:brownearth"),
				"a site reads as conditions by namespace: brownearth or blackearth, and a river");
		}

		GD.Print(_failed == 0 ? $"Economy lab self-test: all {_checks} checks passed." : $"Economy lab self-test: {_failed} of {_checks} checks FAILED.");
		GetTree().Quit(_failed == 0 ? 0 : 1);
	}
}
