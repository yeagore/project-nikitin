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
		int nodes = Web.Goods.Count(id => Catalogue.Find(id) != null) + Web.Recipes.Count + Web.Consumers.Count;
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
			WebAnalysis read = WebAnalysis.Of(Catalogue, web);
			Check(!read.Issues.Any(i => i.Level == IssueLevel.Error), $"{id}: no errors ({read.Issues.Count} issues, {read.Links.Count} links)");
		}
		string catalogueOnce = EconomyStore.ToJson(Catalogue);
		Check(EconomyStore.ToJson(EconomyStore.FromJson<Catalogue>(catalogueOnce)) == catalogueOnce, "catalogue: a load and a save change nothing");
		Check(Catalogue.Goods.All(g => g.Sign?.Extra?.ContainsKey("reading") == true), "catalogue: the signs keep their readings (unknown fields survive)");

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
		Change("tagged bread a heart", Touch.Catalogue, () => Catalogue.Find("bread")!.Tags.Add("kind:golem-heart"));
		Check(Analysis.Links.Any(l => l.From == "bread" && l.To == "r.golem" && l.ByTag), "a good given the tag links itself into the slot");
		CheckCanvas("the tag");
		Undo();
		Check(!Catalogue.Find("bread")!.Has("kind:golem-heart") && !Analysis.Links.Any(l => l.From == "bread" && l.To == "r.golem"), "undo takes the tag and the link away");

		// An output re-aimed, then cut.
		Link("r.bread", 0, "beer", 0);
		Check(Web.Recipe("r.bread")!.Outputs[0].Good == "beer", "a recipe's output dropped on another good re-aims it");
		Undo();
		Link("r.bread", 1, "beer", 0);
		Check(Web.Recipe("r.bread")!.Outputs.Count == 2, "dropped from output + it adds a second output");
		Unlink("r.bread", 1, "beer", 0);
		Check(Web.Recipe("r.bread")!.Outputs.Count == 1, "and cutting that removes it");
		CheckCanvas("outputs");

		// Removing a good takes the recipe that only made it, and leaves the catalogue alone.
		RemoveNodes(new List<string> { "golem" });
		Check(!Web.Holds("golem") && Web.Recipe("r.golem") == null && Catalogue.Find("golem") != null, "removing the golem removes its recipe, not its catalogue entry");
		CheckCanvas("removing the golem");
		Undo();
		Check(Web.Holds("golem") && Web.Recipe("r.golem") != null, "undo brings both back");
		CheckCanvas("undoing that");

		// Removing an input good empties the slot and the issue list says so.
		RemoveNodes(new List<string> { "blood" });
		Check(Web.Recipe("r.golem")!.Inputs[0].Accepts.Count == 0 && Analysis.Issues.Any(i => i.Level == IssueLevel.Error && i.Node == "r.golem"), "removing an input leaves an empty slot and an error");
		Undo();

		// A new good goes into the catalogue and the web in one step.
		int goods = Catalogue.Goods.Count;
		_goodAt = new Spot(100, 100);
		_goodThen = id => Link(id, 0, "r.golem", 2);
		_goodName.Text = "Lead heart";
		MakeGood();
		Check(Catalogue.Find("lead-heart") != null && Web.Holds("lead-heart") && Web.Recipe("r.golem")!.Inputs[2].Accepts.Contains("lead-heart"), "a new good lands in the catalogue, in the web and in the slot, together");
		CheckCanvas("the new good");
		Undo();
		Check(Catalogue.Goods.Count == goods && !Web.Holds("lead-heart") && !Web.Recipe("r.golem")!.Inputs[2].Accepts.Contains("lead-heart"), "and one undo takes all three back");

		// Consumers.
		Link("golem", 0, "c.food", 0);
		Check(Analysis.IsConsumed("golem"), "a good linked to a consumer is consumed");
		Unlink("golem", 0, "c.food", 0);
		Check(!Analysis.IsConsumed("golem"), "and cut, it is not");

		// A tag renamed follows into the slots.
		Change("renamed the heart tag", Touch.Both, () =>
		{
			EconomyEdit.RenameTag(Catalogue, "kind:golem-heart", "kind:heart");
			EconomyEdit.RenameTag(Web, "kind:golem-heart", "kind:heart");
		});
		Check(Web.Recipe("r.golem")!.Inputs[2].Accepts.Contains("#kind:heart") && Analysis.Links.Count(l => l.To == "r.golem" && l.ByTag) == 3, "a renamed tag keeps its slot and its three links");
		Undo();

		// Moving is a change too.
		Spot was = Web.Layout["golem"];
		_nodes["golem"].PositionOffset += new Vector2(40, 30);
		NodesMoved();
		Check(Web.Layout["golem"] == new Spot(was.X + 40, was.Y + 30), "a moved node is written to the layout");
		Undo();
		Check(Web.Layout["golem"] == was && SpotOf(_nodes["golem"]) == was, "and undo moves it back, on the canvas too");

		// Saving and opening again gives the same web.
		Change("a note", Touch.Web, () => Web.Note += " (self-test)");
		string saved = EconomyStore.ToJson(Web);
		Save();
		Check(EconomyStore.ToJson(Store.LoadWeb("starter")) == saved, "a saved web reads back the same");

		// ---- cutting and copying chains ----------------------------------------
		OpenWeb("full-ledger");
		CheckCanvas("opening the full ledger");
		EconomyWeb cut = EconomyEdit.Cut(Catalogue, Web, new[] { "enamelw", "bread" }, "cut", "Cut", withOptional: false);
		WebAnalysis cutRead = WebAnalysis.Of(Catalogue, cut);
		Check(cut.Goods.Count > 10 && cut.Goods.Count < 60 && !cutRead.Issues.Any(i => i.Level == IssueLevel.Error), $"a web cut from enamelware and bread is whole: {cut.Goods.Count} goods, {cut.Recipes.Count} recipes, {cut.Consumers.Count} consumers");
		Check(cut.Goods.Where(g => cutRead.RoleOf(g) == GoodRole.Source).All(g => WebAnalysis.Of(Catalogue, Web).RoleOf(g) == GoodRole.Source || g == "nit"), "its sources are the ledger's sources");
		Check(cut.Consumers.Any(c => c.Id == "c.food") && cut.Consumers.Any(c => c.Id == "c.wares") && cut.Consumers.All(c => c.Id != "c.clothing"), "it keeps the consumers it still reaches and no others");

		OpenWeb("starter");
		EconomyWeb ledger = Store.LoadWeb("full-ledger");
		Change("enamelware with its chain", Touch.Web, () => EconomyEdit.CopyChain(Catalogue, ledger, Web, new[] { "enamelw" }, withOptional: true, new Spot(50, 50)));
		Check(Web.Holds("enamelw") && Web.Recipe("r.enamelw") != null && !Analysis.Issues.Any(i => i.Level == IssueLevel.Error), $"a chain copied into the starter is whole ({Web.Goods.Count} goods now)");
		CheckCanvas("copying a chain in");
		Undo();
		Check(!Web.Holds("enamelw"), "and undo takes the chain out");
		CheckCanvas("the last undo");

		GD.Print(_failed == 0 ? $"Economy lab self-test: all {_checks} checks passed." : $"Economy lab self-test: {_failed} of {_checks} checks FAILED.");
		GetTree().Quit(_failed == 0 ? 0 : 1);
	}
}
