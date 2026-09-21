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
/// consumables. Each web is a version of the economy with its own palette of goods and tags,
/// saved whole as one JSON file under <c>resources/economy/webs/</c>, versioned with the code.
///
/// This file is the lab's core: what is open, the one door every change goes through
/// (<see cref="Change"/>, which also makes undo and autosave work), loading and saving.
/// The canvas is in <c>EconomyLab.Graph.cs</c>, the bars in <c>EconomyLab.Bar.cs</c>, the
/// docks in <c>EconomyLab.Palette.cs</c> and <c>EconomyLab.Inspector.cs</c>.
/// </summary>
public partial class EconomyLab : Control
{
	private sealed record Step(string What, string? Merge, byte[] Web, string? Selected);

	private const int UndoDepth = 100;
	private const double AutosaveAfter = 1.2;
	private const string PrefsPath = "user://economy_lab.cfg";

	internal EconomyStore Store { get; private set; } = null!;
	internal EconomyWeb Web { get; private set; } = null!;

	/// <summary>The open web's own goods and tags. Every web has its own, so nothing done here reaches another web.</summary>
	internal Palette Palette => Web.Palette;

	internal WebAnalysis Analysis { get; private set; } = null!;
	internal SpriteBank Sprites { get; private set; } = null!;

	/// <summary>The key of the node the inspector shows: a good's, a recipe's or a consumer's id, or null for the web itself.</summary>
	internal string? SelectedKey { get; private set; }

	private readonly Stack<Step> _undo = new(), _redo = new();
	private readonly ConfigFile _prefs = new();
	private bool _webDirty, _autosave = true, _trace = true, _changing, _fullscreen;

	/// <summary>The interface's scale; 0 asks the screen. Per machine, since a 4K panel and a laptop want different answers.</summary>
	private float _uiScale;

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
		if (args.Contains("bake"))
		{
			Bake();
			GetTree().Quit();
			return;
		}

		Sprites = new SpriteBank(Store, () => Web.Palette);
		if (!selfTest) _prefs.Load(PrefsPath);
		_autosave = _prefs.GetValue("lab", "autosave", true).AsBool();
		_trace = _prefs.GetValue("lab", "trace", true).AsBool();
		_uiScale = _prefs.GetValue("lab", "ui_scale", 0f).AsSingle();
		_fullscreen = _prefs.GetValue("lab", "fullscreen", false).AsBool();
		GetTree().AutoAcceptQuit = false;
		ApplyWindow(args.Contains("shot") || selfTest);

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
		if (_webDirty) Save();
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
		else if (key.Keycode == Key.F11)
		{
			ToggleFullscreen();
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
	/// Every change to the web or its palette goes through here: the state before is kept
	/// for undo, <paramref name="edit"/> runs, the web is read again, and the canvas, the docks
	/// and the bars are brought into line. <paramref name="merge"/> names a run of like changes
	/// (typing in one field) that undo takes back as one. With <paramref name="keepInspector"/>
	/// the inspector is not rebuilt, so the field being typed in keeps its caret.
	/// Look goods and recipes up by id inside <paramref name="edit"/>: objects held from before
	/// an undo are no longer the ones in the web.
	/// </summary>
	internal void Change(string what, Action edit, string? merge = null, bool keepInspector = false)
	{
		// A change made from inside another is part of it: one step to undo, one refresh at the end.
		if (_changing)
		{
			edit();
			return;
		}

		ulong now = Time.GetTicksMsec();
		bool merged = merge != null && _redo.Count == 0 && _undo.Count > 0 && _undo.Peek().Merge == merge
		              && now - _lastChangeAt < 4000;
		if (!merged)
		{
			_undo.Push(Snap(what, merge));
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
		Touched(keepInspector);
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
		onto.Push(Snap(step.What, null));
		string id = Web.Id;
		Web = EconomyStore.FromJson<EconomyWeb>(Encoding.UTF8.GetString(step.Web));
		Web.Id = id;
		SelectedKey = step.Selected != null && Exists(step.Selected) ? step.Selected : null;
		_lastChangeAt = 0;
		Touched(keepInspector: false, rebuild: true);
		Say($"{verb}: {step.What}.");
	}

	private Step Snap(string what, string? merge) => new(what, merge, Encoding.UTF8.GetBytes(EconomyStore.ToJson(Web)), SelectedKey);

	private static void Trim(Stack<Step> stack)
	{
		Step[] kept = stack.Take(UndoDepth).Reverse().ToArray();
		stack.Clear();
		foreach (Step step in kept) stack.Push(step);
	}

	private void Touched(bool keepInspector, bool rebuild = false)
	{
		_webDirty = true;
		_saveIn = AutosaveAfter;
		Analysis = WebAnalysis.Of(Web);
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

	internal Texture2D? IconOf(string goodId) => Sprites.Get(Palette.Find(goodId)?.Icon);

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
			if (_webDirty) Save();
		}
		Web = next;
		id = Web.Id;
		bool arranged = WebArrange.NeedsArranging(Web);
		if (arranged) WebArrange.Arrange(Web);
		_undo.Clear();
		_redo.Clear();
		SelectedKey = null;
		_webDirty = arranged;
		_saveIn = arranged ? AutosaveAfter : -1;
		Analysis = WebAnalysis.Of(Web);
		_prefs.SetValue("lab", "web", id);

		ResetGraph();
		RefreshInspector();
		RefreshPalette();
		RefreshBar();
		RefreshWebList();
		RestoreView();
		Say($"Opened {Web.Name}: {Web.Goods.Count} goods on the canvas of {Palette.Goods.Count} in its palette, {Web.Recipes.Count} recipes." + (arranged ? " It had no layout, so it was arranged." : ""));
	}

	internal void Save()
	{
		_saveIn = -1;
		if (_shooting) return;
		if (!_webDirty)
		{
			Say("Nothing to save.");
			return;
		}
		try
		{
			Store.SaveWeb(Web);
			_webDirty = false;
			Say($"Saved {Web.Name} to webs/{Web.Id}.json.");
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
		foreach ((string id, string name) in Store.ListWebs())
		{
			EconomyWeb web = Store.LoadWeb(id);
			bool arranged = WebArrange.NeedsArranging(web);
			if (arranged) WebArrange.Arrange(web);
			Store.SaveWeb(web);
			WebAnalysis analysis = WebAnalysis.Of(web);
			GD.Print($"Economy lab: baked {id} ({name}): {web.Goods.Count} goods of {web.Palette.Goods.Count} in its palette, {web.Recipes.Count} recipes, " +
			         $"{web.Consumers.Count} consumers, {analysis.Links.Count} links, {analysis.Issues.Count} issues" + (arranged ? ", arranged" : ""));
			foreach (WebIssue issue in analysis.Issues.Where(i => i.Level != IssueLevel.Note))
				GD.Print($"  {issue.Level}: {issue.Text}");
		}
	}

	// ---- the window -------------------------------------------------------------

	/// <summary>
	/// The project stretches its 1920 by 1080 canvas to whatever the window is, which suits a game
	/// and not an editor: a bigger window should mean more room, not bigger buttons. So the lab
	/// turns the stretch off, opens maximised (or full screen), and scales its interface by a
	/// factor of its own: the screen's, unless one was chosen. A shell run keeps the plain window,
	/// so a shot is the same picture on every machine.
	/// </summary>
	private void ApplyWindow(bool plain = false)
	{
		Window window = GetWindow();
		window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
		window.ContentScaleFactor = plain ? 1f : UiScale;
		if (plain) return;
		window.MinSize = new Vector2I(1100, 640);
		window.Mode = _fullscreen ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Maximized;
	}

	/// <summary>The scale in force: the chosen one, or the screen's own (a Retina panel says 2, a Windows screen says its DPI).</summary>
	private float UiScale
	{
		get
		{
			if (_uiScale > 0) return _uiScale;
			int screen = GetWindow().CurrentScreen;
			float auto = OS.GetName() == "macOS" ? DisplayServer.ScreenGetScale(screen) : DisplayServer.ScreenGetDpi(screen) / 96f;
			return Mathf.Clamp(Mathf.Snapped(auto, 0.25f), 1f, 3f);
		}
	}

	internal void SetUiScale(float scale)
	{
		_uiScale = scale;
		GetWindow().ContentScaleFactor = UiScale;
		SavePrefs();
		Say(scale > 0 ? $"Interface at {scale * 100:0}%." : $"Interface at the screen's own scale, {UiScale * 100:0}%.");
	}

	internal void ToggleFullscreen()
	{
		_fullscreen = !_fullscreen;
		GetWindow().Mode = _fullscreen ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Maximized;
		SavePrefs();
	}

	private void SavePrefs()
	{
		_prefs.SetValue("lab", "autosave", _autosave);
		_prefs.SetValue("lab", "trace", _trace);
		_prefs.SetValue("lab", "ui_scale", _uiScale);
		_prefs.SetValue("lab", "fullscreen", _fullscreen);
		_prefs.Save(PrefsPath);
	}
}
