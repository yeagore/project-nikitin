using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// The economy lab: an editor of production webs. Goods are draggable nodes, recipes sit
/// between them, links are drawn by hand or implied by a tag, and a consumer blob marks the
/// consumables. The catalogue of goods is shared; each web is a version of the economy made
/// from it, saved as JSON under <c>resources/economy/</c> so it is versioned with the code.
///
/// This file is the lab's core: what is open, the one door every change goes through
/// (<see cref="Change"/>, which also makes undo and autosave work), loading and saving.
/// The canvas is in <c>EconomyLab.Graph.cs</c>, the bars in <c>EconomyLab.Bar.cs</c>, the
/// docks in <c>EconomyLab.Palette.cs</c> and <c>EconomyLab.Inspector.cs</c>.
/// </summary>
public partial class EconomyLab : Control
{
	/// <summary>What a change touches, which is what undo must put back and what a save must write.</summary>
	[Flags]
	internal enum Touch
	{
		Web = 1,
		Catalogue = 2,
		Both = Web | Catalogue,
	}

	private sealed record Step(string What, string? Merge, Touch Touch, byte[]? Web, byte[]? Catalogue, string? Selected);

	private const int UndoDepth = 100;
	private const double AutosaveAfter = 1.2;
	private const string PrefsPath = "user://economy_lab.cfg";

	internal EconomyStore Store { get; private set; } = null!;
	internal Catalogue Catalogue { get; private set; } = null!;
	internal EconomyWeb Web { get; private set; } = null!;
	internal WebAnalysis Analysis { get; private set; } = null!;
	internal SpriteBank Sprites { get; private set; } = null!;

	/// <summary>The key of the node the inspector shows: a good's, a recipe's or a consumer's id, or null for the web itself.</summary>
	internal string? SelectedKey { get; private set; }

	private readonly Stack<Step> _undo = new(), _redo = new();
	private readonly ConfigFile _prefs = new();
	private bool _webDirty, _catalogueDirty, _autosave = true, _trace = true, _changing;
	private double _saveIn = -1;
	private ulong _lastChangeAt;
	private ulong _shotAt;
	private bool _shooting;
	private string _shotPath = "user://economy_lab.png";
	private string? _shotSelect, _shotShow;
	private float _shotZoom;

	public override void _Ready()
	{
		string[] args = OS.GetCmdlineUserArgs();
		bool selfTest = args.Contains("selftest");
		Store = new EconomyStore(selfTest ? SelfTestRoot() : ProjectSettings.GlobalizePath("res://resources/economy"));
		if (!Store.HasCatalogue)
		{
			AddChild(LabLook.Text($"No catalogue at {Store.CataloguePath}.\nSee docs/economy-lab.md.", 18));
			return;
		}
		Catalogue = Store.LoadCatalogue();
		if (args.Contains("bake"))
		{
			Bake();
			GetTree().Quit();
			return;
		}

		Sprites = new SpriteBank(Store, Catalogue);
		if (!selfTest) _prefs.Load(PrefsPath);
		_autosave = _prefs.GetValue("lab", "autosave", true).AsBool();
		_trace = _prefs.GetValue("lab", "trace", true).AsBool();
		GetTree().AutoAcceptQuit = false;

		BuildUi();
		if (selfTest)
		{
			Callable.From(SelfTest).CallDeferred();
			return;
		}

		string wanted = _prefs.GetValue("lab", "web", "starter").AsString();
		foreach (string arg in args)
		{
			if (arg == "shot")
			{
				// A shot is for looking: it never writes a file, whatever it opens or arranges.
				_shotAt = Engine.GetProcessFrames() + 60;
				_shooting = true;
			}
			else if (arg.StartsWith("web=")) wanted = arg[4..];
			else if (arg.StartsWith("out=")) _shotPath = arg[4..];
			else if (arg.StartsWith("select=")) _shotSelect = arg[7..];
			else if (arg.StartsWith("show=")) _shotShow = arg[5..];
			else if (arg.StartsWith("zoom=")) float.TryParse(arg.AsSpan(5), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _shotZoom);
		}
		List<(string Id, string Name)> webs = Store.ListWebs();
		if (webs.Count == 0)
		{
			Web = new EconomyWeb { Id = "first", Name = "First web" };
			Store.SaveWeb(Web);
			wanted = Web.Id;
		}
		else if (!Store.HasWeb(wanted)) wanted = Store.HasWeb("starter") ? "starter" : webs[0].Id;
		OpenWeb(wanted);

		// The first time on a machine the help sheet opens by itself; after that it is F1.
		if (!_shooting && !_prefs.GetValue("lab", "seen_help", false).AsBool())
		{
			_prefs.SetValue("lab", "seen_help", true);
			SavePrefs();
			ToggleHelp();
		}
	}

	public override void _Process(double delta)
	{
		if (_saveIn >= 0 && (_saveIn -= delta) < 0 && _autosave && !_shooting) Save();
		TickBar();

		// A pop-up asked for by show= opens late: one opened while the window is still settling is closed again by the focus changes.
		if (_shotAt != 0 && _shotShow != null && Engine.GetProcessFrames() + 10 >= _shotAt) ShowForShot();

		if (_shotAt != 0 && Engine.GetProcessFrames() >= _shotAt)
		{
			_shotAt = 0;
			Image shot = GetViewport().GetTexture().GetImage();
			Error err = shot.SavePng(_shotPath);
			GD.Print(err == Error.Ok ? $"Economy lab: saved {ProjectSettings.GlobalizePath(_shotPath)}" : $"Economy lab: could not save {_shotPath}: {err}");
			GetTree().Quit();
		}
	}

	public override void _Notification(int what)
	{
		if (what != NotificationWMCloseRequest) return;
		RememberView();
		SavePrefs();
		if (_webDirty || _catalogueDirty) Save();
		GetTree().Quit();
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true, Echo: false } key || _graph == null) return;
		Control? focus = GetViewport().GuiGetFocusOwner();
		bool typing = focus is LineEdit or TextEdit;
		// The plain keys belong to the canvas: F in the palette is a letter to search by.
		bool onCanvas = focus == null || focus == _graph || _graph.IsAncestorOf(focus);

		if (key.IsCommandOrControlPressed())
		{
			switch (key.Keycode)
			{
				case Key.S: Save(); break;
				case Key.F: Find(); break;
				case Key.Z when !typing && key.ShiftPressed: Redo(); break;
				case Key.Z when !typing: Undo(); break;
				case Key.Y when !typing: Redo(); break;
				default: return;
			}
			GetViewport().SetInputAsHandled();
		}
		else if (key.Keycode == Key.F1)
		{
			ToggleHelp();
			GetViewport().SetInputAsHandled();
		}
		else if (key.Keycode == Key.F && onCanvas)
		{
			FrameAll();
			GetViewport().SetInputAsHandled();
		}
		else if (key.Keycode is Key.Backspace or Key.Delete && onCanvas && SelectedKeys().Count > 0)
		{
			// A Mac's delete key is Backspace, which the canvas does not take for Delete by itself.
			RemoveNodes(SelectedKeys());
			GetViewport().SetInputAsHandled();
		}
	}

	// ---- the door --------------------------------------------------------------

	/// <summary>
	/// Every change to the web or the catalogue goes through here: the state before is kept
	/// for undo, <paramref name="edit"/> runs, the web is read again, and the canvas, the docks
	/// and the bars are brought into line. <paramref name="merge"/> names a run of like changes
	/// (typing in one field) that undo takes back as one. With <paramref name="keepInspector"/>
	/// the inspector is not rebuilt, so the field being typed in keeps its caret.
	/// Look goods and recipes up by id inside <paramref name="edit"/>: objects held from before
	/// an undo are no longer the ones in the web. A change that touches the catalogue anywhere
	/// inside it must say so in <paramref name="touch"/> at the top.
	/// </summary>
	internal void Change(string what, Touch touch, Action edit, string? merge = null, bool keepInspector = false)
	{
		// A change made from inside another is part of it: one step to undo, one refresh at the end.
		if (_changing)
		{
			edit();
			return;
		}

		ulong now = Time.GetTicksMsec();
		bool merged = merge != null && _redo.Count == 0 && _undo.Count > 0 && _undo.Peek().Merge == merge
		              && _undo.Peek().Touch == touch && now - _lastChangeAt < 4000;
		if (!merged)
		{
			_undo.Push(Snap(what, merge, touch));
			if (_undo.Count > UndoDepth) Trim(_undo);
		}
		_redo.Clear();
		_lastChangeAt = now;

		_changing = true;
		try
		{
			edit();
		}
		finally
		{
			_changing = false;
		}
		Touched(touch, keepInspector);
		Say(char.ToUpperInvariant(what[0]) + what[1..] + ".");
	}

	internal void Undo() => Travel(_undo, _redo, "Undid");

	internal void Redo() => Travel(_redo, _undo, "Redid");

	private void Travel(Stack<Step> from, Stack<Step> onto, string verb)
	{
		if (from.Count == 0)
		{
			Say("Nothing to " + (verb == "Undid" ? "undo." : "redo."));
			return;
		}
		Step step = from.Pop();
		onto.Push(Snap(step.What, null, step.Touch));
		if (step.Web != null) Web = EconomyStore.FromJson<EconomyWeb>(Encoding.UTF8.GetString(step.Web));
		if (step.Catalogue != null)
		{
			Catalogue = EconomyStore.FromJson<Catalogue>(Encoding.UTF8.GetString(step.Catalogue));
			Sprites = new SpriteBank(Store, Catalogue);
		}
		SelectedKey = step.Selected != null && Exists(step.Selected) ? step.Selected : null;
		_lastChangeAt = 0;
		Touched(step.Touch, keepInspector: false, rebuild: true);
		Say($"{verb}: {step.What}.");
	}

	private Step Snap(string what, string? merge, Touch touch) => new(what, merge, touch,
		touch.HasFlag(Touch.Web) ? Encoding.UTF8.GetBytes(EconomyStore.ToJson(Web)) : null,
		touch.HasFlag(Touch.Catalogue) ? Encoding.UTF8.GetBytes(EconomyStore.ToJson(Catalogue)) : null,
		SelectedKey);

	private static void Trim(Stack<Step> stack)
	{
		Step[] kept = stack.Take(UndoDepth).Reverse().ToArray();
		stack.Clear();
		foreach (Step step in kept) stack.Push(step);
	}

	private void Touched(Touch touch, bool keepInspector, bool rebuild = false)
	{
		if (touch.HasFlag(Touch.Web)) _webDirty = true;
		if (touch.HasFlag(Touch.Catalogue)) _catalogueDirty = true;
		_saveIn = AutosaveAfter;
		Analysis = WebAnalysis.Of(Catalogue, Web);
		if (rebuild) ResetGraph();
		else SyncGraph();
		if (SelectedKey != null && !Exists(SelectedKey)) SelectedKey = null;
		if (!keepInspector) RefreshInspector();
		RefreshPalette();
		RefreshBar();
	}

	/// <summary>True if the key names a good, a recipe or a consumer of the open web.</summary>
	internal bool Exists(string key) => Web.Holds(key) || Web.Recipe(key) != null || Web.Consumer(key) != null;

	// ---- selection -------------------------------------------------------------

	/// <summary>
	/// Makes a node the selected one: the canvas marks it, the inspector shows it, and with
	/// <paramref name="focus"/> the view travels to it. Null selects the web itself.
	/// </summary>
	internal void Select(string? key, bool focus = false)
	{
		if (key != null && !Exists(key)) key = null;
		SelectedKey = key;
		MarkSelected(key, focus);
		RefreshInspector();
		ApplyTrace();
	}

	/// <summary>The canvas tells the core what the user clicked; the canvas is already marked.</summary>
	private void Selected(string? key)
	{
		if (SelectedKey == key) return;
		SelectedKey = key;
		RefreshInspector();
		ApplyTrace();
	}

	internal Texture2D? IconOf(string goodId) => Sprites.Get(Catalogue.Find(goodId)?.Icon);

	// ---- files -----------------------------------------------------------------

	/// <summary>The web in that file, or null (with the reason in the console) if it does not read: a file edited by hand, say.</summary>
	private EconomyWeb? TryLoad(string id)
	{
		try
		{
			return Store.LoadWeb(id);
		}
		catch (Exception e) when (e is System.Text.Json.JsonException or System.IO.IOException)
		{
			GD.PushError($"Economy lab: {id}.json does not read: {e.Message}");
			return null;
		}
	}

	internal void OpenWeb(string id)
	{
		EconomyWeb? next = TryLoad(id);
		if (next == null && Web == null)
		{
			// Nothing is open yet: any web that reads will do, and failing that a fresh one.
			next = Store.ListWebs().Select(w => TryLoad(w.Id)).FirstOrDefault(w => w != null);
			if (next == null)
			{
				next = new EconomyWeb { Id = EconomyEdit.Free("scratch", Store.HasWeb), Name = "Scratch" };
				Store.SaveWeb(next);
			}
		}
		if (next == null)
		{
			Say($"{id}.json does not read; the console says why. Still on {Web!.Name}.");
			RefreshWebList();
			return;
		}
		if (Web != null)
		{
			RememberView();
			if (_webDirty || _catalogueDirty) Save();
		}
		Web = next;
		id = Web.Id;
		bool arranged = WebArrange.NeedsArranging(Web);
		if (arranged) WebArrange.Arrange(Catalogue, Web);
		_undo.Clear();
		_redo.Clear();
		SelectedKey = null;
		_webDirty = arranged;
		_saveIn = arranged ? AutosaveAfter : -1;
		Analysis = WebAnalysis.Of(Catalogue, Web);
		_prefs.SetValue("lab", "web", id);

		ResetGraph();
		RefreshInspector();
		RefreshPalette();
		RefreshBar();
		RefreshWebList();
		RestoreView();
		Say($"Opened {Web.Name}: {Web.Goods.Count} goods, {Web.Recipes.Count} recipes." + (arranged ? " It had no layout, so it was arranged." : ""));
	}

	internal void Save()
	{
		_saveIn = -1;
		if (_shooting) return;
		if (!_webDirty && !_catalogueDirty)
		{
			Say("Nothing to save.");
			return;
		}
		try
		{
			var wrote = new List<string>();
			if (_catalogueDirty)
			{
				Store.SaveCatalogue(Catalogue);
				wrote.Add("the catalogue");
			}
			if (_webDirty)
			{
				Store.SaveWeb(Web);
				wrote.Add(Web.Name);
			}
			_webDirty = _catalogueDirty = false;
			Say("Saved " + string.Join(" and ", wrote) + ".");
		}
		catch (Exception e)
		{
			Say("Could not save: " + e.Message);
			GD.PushError(e.ToString());
		}
		RefreshBar();
	}

	/// <summary>
	/// A shell chore (<c>-- bake</c>): every web with no layout gets one, and every file is
	/// written back through this build's writer, so an import lands in the lab's own format.
	/// </summary>
	private void Bake()
	{
		Store.SaveCatalogue(Catalogue);
		foreach ((string id, string name) in Store.ListWebs())
		{
			EconomyWeb web = Store.LoadWeb(id);
			bool arranged = WebArrange.NeedsArranging(web);
			if (arranged) WebArrange.Arrange(Catalogue, web);
			Store.SaveWeb(web);
			WebAnalysis analysis = WebAnalysis.Of(Catalogue, web);
			GD.Print($"Economy lab: baked {id} ({name}): {web.Goods.Count} goods, {web.Recipes.Count} recipes, " +
			         $"{web.Consumers.Count} consumers, {analysis.Links.Count} links, {analysis.Issues.Count} issues" + (arranged ? ", arranged" : ""));
			foreach (WebIssue issue in analysis.Issues.Where(i => i.Level != IssueLevel.Note))
				GD.Print($"  {issue.Level}: {issue.Text}");
		}
	}

	private void SavePrefs()
	{
		_prefs.SetValue("lab", "autosave", _autosave);
		_prefs.SetValue("lab", "trace", _trace);
		_prefs.Save(PrefsPath);
	}
}
