using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// The frame round the canvas: the top bar (which web, save, undo, arrange, trace, find,
/// issues), the status line, the help sheet, and the dialogs that make a good and make or
/// bin a web. The docks either side are built by their own files.
/// </summary>
public partial class EconomyLab
{
	private OptionButton _webPick = null!;
	private Button _saveButton = null!, _undoButton = null!, _redoButton = null!, _issuesButton = null!;
	private Label _status = null!, _counts = null!;
	private Control _help = null!;
	private ConfirmationDialog _goodDialog = null!, _webDialog = null!, _binDialog = null!;
	private LineEdit _goodName = null!, _webName = null!;
	private Label _goodId = null!, _webHint = null!;
	private OptionButton _webFrom = null!;
	private CheckBox _webOptional = null!;
	private Spot _goodAt;
	private Action<string>? _goodThen;
	private ulong _saidAt;

	private void BuildUi()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);
		var back = new ColorRect { Color = LabLook.Back, MouseFilter = MouseFilterEnum.Ignore };
		back.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(back);

		var rows = new VBoxContainer();
		rows.SetAnchorsPreset(LayoutPreset.FullRect);
		rows.AddThemeConstantOverride("separation", 0);
		AddChild(rows);
		rows.AddChild(BuildTopBar());

		var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		rows.AddChild(split);
		split.AddChild(Docked(BuildPalette(), 300));
		var right = new HSplitContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		split.AddChild(right);
		right.AddChild(BuildGraph());
		right.AddChild(Docked(BuildInspector(), 370));

		rows.AddChild(BuildStatusBar());
		BuildHelp();
		BuildDialogs();
	}

	private static PanelContainer Docked(Control content, int width)
	{
		var dock = new PanelContainer { CustomMinimumSize = new Vector2(width, 0) };
		dock.AddThemeStyleboxOverride("panel", LabLook.Box(LabLook.Dock, 0, 0, marginX: 10, marginY: 8));
		dock.AddChild(content);
		return dock;
	}

	private Control BuildTopBar()
	{
		var bar = new PanelContainer();
		bar.AddThemeStyleboxOverride("panel", LabLook.Box(LabLook.Dock.Darkened(0.2f), 0, 0, marginX: 10, marginY: 6));
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		bar.AddChild(row);

		row.AddChild(LabLook.Text("Web", 13, LabLook.Dim));
		_webPick = new OptionButton { CustomMinimumSize = new Vector2(260, 0), FocusMode = FocusModeEnum.None, TooltipText = "Which version of the economy is open. Each is a file under resources/economy/webs." };
		_webPick.ItemSelected += index => OpenWeb(_webPick.GetItemMetadata((int)index).AsString());
		row.AddChild(_webPick);
		row.AddChild(Press("New…", "A new web: empty, a copy of this one, or the selected goods with everything upstream of them.", AskNewWeb));
		row.AddChild(Press("Bin…", "Move this web's file to the system trash.", AskBinWeb));
		row.AddChild(new VSeparator());

		_saveButton = Press("Save", "Write the web and the catalogue to disk now (Cmd/Ctrl+S).", Save);
		row.AddChild(_saveButton);
		var auto = new CheckBox { Text = "Autosave", ButtonPressed = _autosave, TooltipText = "Save a moment after every change. The files are in the repository, so git is the safety net." };
		auto.Toggled += on =>
		{
			_autosave = on;
			SavePrefs();
		};
		row.AddChild(auto);
		row.AddChild(new VSeparator());

		_undoButton = Press("Undo", "Cmd/Ctrl+Z", Undo);
		_redoButton = Press("Redo", "Shift+Cmd/Ctrl+Z", Redo);
		row.AddChild(_undoButton);
		row.AddChild(_redoButton);
		row.AddChild(new VSeparator());

		row.AddChild(Press("Arrange", "Lay the whole web out afresh, sources on the left, consumers on the right. Undo brings your layout back.", ArrangeAll));
		row.AddChild(Press("Frame", "Fit the whole web on screen (F).", FrameAll));
		var trace = new CheckBox { Text = "Trace", ButtonPressed = _trace, TooltipText = "With one node selected, keep lit what it is made of and what is made with it, and fade the rest." };
		trace.Toggled += on =>
		{
			_trace = on;
			SavePrefs();
			ApplyTrace();
		};
		row.AddChild(trace);
		row.AddChild(Press("Find…", "Go to a good, a recipe or a consumer of this web by name (Cmd/Ctrl+F).", Find));

		row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
		_issuesButton = Press("No issues", "What the web's analysis found wrong or unfinished. Pick one to go to it.", ShowIssues);
		row.AddChild(_issuesButton);
		row.AddChild(Press("Help", "What the mouse and the keys do (F1).", ToggleHelp));
		return bar;
	}

	private static Button Press(string text, string tip, Action pressed)
	{
		var button = new Button { Text = text, TooltipText = tip, FocusMode = FocusModeEnum.None };
		button.Pressed += pressed;
		return button;
	}

	private Control BuildStatusBar()
	{
		var bar = new PanelContainer();
		bar.AddThemeStyleboxOverride("panel", LabLook.Box(LabLook.Dock.Darkened(0.2f), 0, 0, marginX: 12, marginY: 4));
		var row = new HBoxContainer();
		bar.AddChild(row);
		_status = LabLook.Text("", 13, LabLook.Ink, trim: true);
		_status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(_status);
		_counts = LabLook.Text("", 13, LabLook.Dim);
		row.AddChild(_counts);
		return bar;
	}

	/// <summary>One line at the bottom: what just happened, or why something did not.</summary>
	internal void Say(string message)
	{
		if (_status == null) return;
		_status.Text = message;
		_status.AddThemeColorOverride("font_color", LabLook.Ink);
		_saidAt = Time.GetTicksMsec();
	}

	private void TickBar()
	{
		if (_saidAt != 0 && Time.GetTicksMsec() - _saidAt > 9000)
		{
			_saidAt = 0;
			_status.AddThemeColorOverride("font_color", LabLook.Faint);
		}
	}

	private void RefreshBar()
	{
		if (_webPick == null) return;
		if (_webPick.Selected >= 0 && _webPick.GetItemText(_webPick.Selected) != Web.Name) _webPick.SetItemText(_webPick.Selected, Web.Name);
		bool dirty = _webDirty || _catalogueDirty;
		_saveButton.Text = dirty ? "Save •" : "Saved";
		_saveButton.Disabled = !dirty;
		_undoButton.Disabled = _undo.Count == 0;
		_redoButton.Disabled = _redo.Count == 0;
		_undoButton.TooltipText = _undo.Count > 0 ? $"Undo: {_undo.Peek().What} (Cmd/Ctrl+Z)" : "Nothing to undo";
		_redoButton.TooltipText = _redo.Count > 0 ? $"Redo: {_redo.Peek().What} (Shift+Cmd/Ctrl+Z)" : "Nothing to redo";

		int errors = Analysis.Issues.Count(i => i.Level == IssueLevel.Error), warnings = Analysis.Issues.Count(i => i.Level == IssueLevel.Warning);
		int notes = Analysis.Issues.Count - errors - warnings;
		_issuesButton.Text = Analysis.Issues.Count == 0 ? "No issues"
			: string.Join(", ", new[] { (errors, "error"), (warnings, "warning"), (notes, "note") }.Where(p => p.Item1 > 0).Select(p => $"{p.Item1} {p.Item2}{(p.Item1 == 1 ? "" : "s")}"));
		_issuesButton.AddThemeColorOverride("font_color", errors > 0 ? LabLook.Error : warnings > 0 ? LabLook.Warning : LabLook.Dim);
		_issuesButton.Disabled = Analysis.Issues.Count == 0;

		int sources = Web.Goods.Count(g => Analysis.RoleOf(g) == GoodRole.Source), finals = Web.Goods.Count(g => Analysis.RoleOf(g) == GoodRole.Final);
		_counts.Text = $"{Web.Goods.Count} goods ({sources} sources, {finals} final) · {Web.Recipes.Count} recipes · {Web.Consumers.Count} consumers · {Analysis.Links.Count} links · catalogue {Catalogue.Goods.Count}";
	}

	private void RefreshWebList()
	{
		_webPick.Clear();
		foreach ((string id, string onDisk) in Store.ListWebs())
		{
			string name = id == Web.Id ? Web.Name : onDisk; // the open web may have been renamed and not saved yet
			_webPick.AddItem(name);
			_webPick.SetItemMetadata(_webPick.ItemCount - 1, id);
			if (id == Web.Id) _webPick.Select(_webPick.ItemCount - 1);
		}
	}

	private void ShowIssues()
	{
		IEnumerable<(string, string, Texture2D?)> items = Analysis.Issues
			.OrderByDescending(i => i.Level)
			.Select(i => (i.Node, $"{i.Level}: {i.Text}", Exists(i.Node) && Web.Holds(i.Node) ? IconOf(i.Node) : null));
		_picker.Ask("Issues: pick one to go to it", items, _issuesButton.GlobalPosition + new Vector2(-220, 40), key => Select(key, focus: true));
	}

	// ---- help ------------------------------------------------------------------

	private const string HelpText =
		"""
		THE ECONOMY LAB

		A web is one version of the economy: some goods of the catalogue, the recipes between
		them, and the consumers they lead to. Goods are shared by all webs; recipes belong to one.

		CANVAS
		  Drag a node to move it; drag on empty canvas for a rubber band; wheel to zoom;
		  middle-drag (or Space-drag) to pan. F frames the whole web.
		  Drag from a good's right port to a recipe's left port: the good goes into that slot.
		    Drop it on a slot that has something already and the slot takes either ("or").
		    Drop it on "+ input" to make a new slot.
		  Drag from a recipe's right port to a good: the recipe makes that good.
		  Drag from a good to a consumer: the good is a consumable.
		  Drag a link off a left port to cut it, or right-click the link.
		  Let a link go over empty canvas for a menu: a new recipe, a new good, a tag.
		  Right-click the canvas or a node for more. Delete removes the selected nodes
		  from the web (goods stay in the catalogue).

		TAGS
		  A slot can accept a tag instead of a good (amber). Every good in the web that carries
		  the tag links itself in. Give a new heart the tag kind:golem-heart and it fits the golem.

		DOCKS
		  Left: the catalogue. Drag goods onto the canvas, or double-click. "With its chain"
		  brings the recipes and everything upstream from another web. The Tags tab renames
		  and deletes tags.
		  Right: whatever is selected, for editing. Nothing selected shows the web itself.

		KEYS
		  Cmd/Ctrl+S save · Cmd/Ctrl+Z undo · Shift+Cmd/Ctrl+Z redo · Cmd/Ctrl+F find · F frame · F1 this sheet

		FILES
		  resources/economy/catalogue.json, webs/*.json, sprites/. Commit them like code.
		""";

	private void BuildHelp()
	{
		var shade = new ColorRect { Color = new Color(0, 0, 0, 0.55f), Visible = false };
		shade.SetAnchorsPreset(LayoutPreset.FullRect);
		shade.GuiInput += @event =>
		{
			if (@event is InputEventMouseButton { Pressed: true }) ToggleHelp();
		};
		AddChild(shade);
		var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
		centre.SetAnchorsPreset(LayoutPreset.FullRect);
		shade.AddChild(centre);
		var sheet = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
		sheet.AddThemeStyleboxOverride("panel", LabLook.Box(LabLook.Dock, 8, 8, LabLook.Accent, 1, 26, 20));
		centre.AddChild(sheet);
		Label text = LabLook.Text(HelpText.Replace("\t", ""), 14);
		text.MouseFilter = MouseFilterEnum.Ignore;
		sheet.AddChild(text);
		_help = shade;
	}

	private void ToggleHelp() => _help.Visible = !_help.Visible;

	// ---- dialogs ---------------------------------------------------------------

	private void BuildDialogs()
	{
		_goodDialog = new ConfirmationDialog { Title = "New good", OkButtonText = "Create", MinSize = new Vector2I(380, 0) };
		var goodRows = new VBoxContainer();
		_goodDialog.AddChild(goodRows);
		goodRows.AddChild(LabLook.Text("Name", 13, LabLook.Dim));
		_goodName = new LineEdit { PlaceholderText = "Lead heart" };
		goodRows.AddChild(_goodName);
		_goodId = LabLook.Text(" ", 12, LabLook.Faint);
		goodRows.AddChild(_goodId);
		_goodName.TextChanged += text => _goodId.Text = text.Trim().Length == 0 ? " " : "id: " + EconomyEdit.FreeGoodId(Catalogue, text) + "   (it goes into the catalogue and into this web)";
		_goodName.TextSubmitted += _ =>
		{
			_goodDialog.Hide();
			MakeGood();
		};
		_goodDialog.Confirmed += MakeGood;
		AddChild(_goodDialog);

		_webDialog = new ConfirmationDialog { Title = "New web", OkButtonText = "Create", MinSize = new Vector2I(460, 0) };
		var webRows = new VBoxContainer();
		_webDialog.AddChild(webRows);
		webRows.AddChild(LabLook.Text("Name", 13, LabLook.Dim));
		_webName = new LineEdit { PlaceholderText = "Vertical slice" };
		webRows.AddChild(_webName);
		webRows.AddChild(LabLook.Text("Start from", 13, LabLook.Dim));
		_webFrom = new OptionButton();
		_webFrom.AddItem("Nothing: an empty web");
		_webFrom.AddItem("A copy of the open web");
		_webFrom.AddItem("The selected goods and everything upstream of them");
		webRows.AddChild(_webFrom);
		_webOptional = new CheckBox { Text = "bring optional inputs and their chains too", ButtonPressed = false };
		webRows.AddChild(_webOptional);
		_webHint = LabLook.Text(" ", 12, LabLook.Faint);
		_webHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_webHint.CustomMinimumSize = new Vector2(430, 40);
		webRows.AddChild(_webHint);
		_webFrom.ItemSelected += _ => HintNewWeb();
		_webName.TextChanged += _ => HintNewWeb();
		_webDialog.Confirmed += MakeWeb;
		AddChild(_webDialog);

		_binDialog = new ConfirmationDialog { Title = "Bin this web", OkButtonText = "Move to trash" };
		_binDialog.Confirmed += BinWeb;
		AddChild(_binDialog);
	}

	/// <summary>
	/// Asks for a name, then makes a good in the catalogue and puts it in the web at <paramref name="at"/>.
	/// <paramref name="then"/> runs inside the same change with the new id, so a link to it is part of one undo step.
	/// </summary>
	internal void AskNewGood(Spot at, Action<string> then)
	{
		_goodAt = at;
		_goodThen = then;
		_goodName.Text = "";
		_goodId.Text = " ";
		_goodDialog.PopupCentered();
		_goodName.GrabFocus();
	}

	private void MakeGood()
	{
		string name = _goodName.Text.Trim();
		if (name.Length == 0) return;
		string id = EconomyEdit.FreeGoodId(Catalogue, name);
		Change($"new good {name}", Touch.Both, () =>
		{
			Catalogue.Add(new Good { Id = id, Name = name });
			EconomyEdit.AddGood(Web, id, _goodAt);
			_goodThen?.Invoke(id);
			SelectNew(id);
		});
	}

	private void AskNewWeb()
	{
		_webName.Text = "";
		_webFrom.Select(SelectedKeys().Any(Web.Holds) ? 2 : 0);
		HintNewWeb();
		_webDialog.PopupCentered();
		_webName.GrabFocus();
	}

	private void HintNewWeb()
	{
		int goods = SelectedKeys().Count(Web.Holds);
		string file = "webs/" + EconomyEdit.Free(EconomyEdit.Slug(_webName.Text), Store.HasWeb) + ".json";
		_webOptional.Visible = _webFrom.Selected == 2;
		_webHint.Text = _webFrom.Selected switch
		{
			2 when goods == 0 => "Nothing is selected. Select the final goods you want on the canvas first (a rubber band, or Shift-click), then come back.",
			2 => $"{goods} selected good{(goods == 1 ? "" : "s")}, their recipes, and everything those need, down to the ground. Saved as {file}.",
			1 => $"Every good, recipe and consumer of {Web.Name}, to change freely. Saved as {file}.",
			_ => $"An empty canvas; drag goods in from the catalogue. Saved as {file}.",
		};
	}

	private void MakeWeb()
	{
		string name = _webName.Text.Trim();
		if (name.Length == 0) name = "New web";
		string id = EconomyEdit.Free(EconomyEdit.Slug(name), Store.HasWeb);
		EconomyWeb made;
		switch (_webFrom.Selected)
		{
			case 1:
				made = EconomyStore.Clone(Web);
				made.Id = id;
				made.Name = name;
				break;
			case 2:
				List<string> goods = SelectedKeys().Where(Web.Holds).ToList();
				if (goods.Count == 0)
				{
					Say("No goods are selected, so there was nothing to cut a web from.");
					return;
				}
				made = EconomyEdit.Cut(Catalogue, Web, goods, id, name, _webOptional.ButtonPressed);
				made.Note = $"Cut from {Web.Name}: " + string.Join(", ", goods.Select(g => Catalogue.Find(g)?.Name ?? g)) + ".";
				break;
			default:
				made = new EconomyWeb { Id = id, Name = name };
				break;
		}
		Store.SaveWeb(made);
		OpenWeb(id);
		RefreshWebList();
	}

	private void AskBinWeb()
	{
		_binDialog.DialogText = $"Move \"{Web.Name}\" ({Web.Id}.json) to the system trash?\nThe catalogue and the other webs are not touched.";
		_binDialog.PopupCentered();
	}

	private void BinWeb()
	{
		string gone = Web.Name;
		_webDirty = false;
		_saveIn = -1;
		Error err = OS.MoveToTrash(Store.WebPath(Web.Id));
		if (err != Error.Ok)
		{
			Say($"Could not move {Web.Id}.json to the trash: {err}.");
			return;
		}
		List<(string Id, string Name)> left = Store.ListWebs();
		if (left.Count == 0)
		{
			var first = new EconomyWeb { Id = "first", Name = "First web" };
			Store.SaveWeb(first);
			left.Add((first.Id, first.Name));
		}
		Web = null!;
		OpenWeb(left[0].Id);
		RefreshWebList();
		Say($"Moved {gone} to the trash, and opened {Web.Name}.");
	}
}
