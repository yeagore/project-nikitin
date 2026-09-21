using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// The right-hand dock: whatever is selected, laid out for editing. A good shows its name,
/// its description, its tags, its two sprites and what makes, uses and eats it; a recipe its
/// input slots and its outputs; a consumer what reaches it; several nodes what can be done to
/// them all; and with nothing selected, the web itself — its numbers, its hubs, what the
/// analysis found wrong, and the legend of the canvas's colours.
///
/// Every field and button goes through <see cref="Change"/>, so everything here is a step to
/// undo and a moment later a save. Typing merges into one step and leaves the dock standing;
/// anything structural rebuilds it, which is why nothing below holds a Good or a Recipe from
/// one refresh to the next: the closures keep ids and look the object up when they run.
/// </summary>
public partial class EconomyLab
{
	private ScrollContainer _inspScroll = null!;
	private VBoxContainer _inspRows = null!;
	private IconPickPopup _inspSheet = null!;
	private ConfirmationDialog _inspBin = null!;
	private FileDialog _inspPng = null!;

	/// <summary>What the dock showed last, so the scroll is kept when the same thing is shown again.</summary>
	private string? _inspShown;

	private bool _inspShownMany;
	private string _inspBinGood = "", _inspPngGood = "";

	private Control BuildInspector()
	{
		_inspScroll = new ScrollContainer
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		_inspRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_inspRows.AddThemeConstantOverride("separation", 6);
		_inspScroll.AddChild(_inspRows);

		_inspSheet = new IconPickPopup();
		AddChild(_inspSheet);

		_inspBin = new ConfirmationDialog { Title = "Delete from the catalogue", OkButtonText = "Delete" };
		_inspBin.Confirmed += BinGood;
		AddChild(_inspBin);

		_inspPng = new FileDialog
		{
			Title = "A PNG of its own",
			Access = FileDialog.AccessEnum.Filesystem,
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Filters = new[] { "*.png ; PNG images" },
			UseNativeDialog = true,
		};
		_inspPng.FileSelected += ImportIcon;
		AddChild(_inspPng);
		return _inspScroll;
	}

	/// <summary>
	/// Draws the dock afresh for what is selected now. Several nodes get the multi-selection
	/// panel; one node its own; nothing, the web. Showing the same thing again keeps the scroll
	/// where it was, so a chip taken off a long good does not throw the page back to the top.
	/// </summary>
	private void RefreshInspector()
	{
		if (_inspRows == null) return;
		List<string> selected = SelectedKeys();
		bool many = selected.Count > 1;
		string? key = many ? null : SelectedKey;
		int keep = !many && !_inspShownMany && key == _inspShown ? _inspScroll.ScrollVertical : 0;
		_inspShown = key;
		_inspShownMany = many;

		foreach (Node old in _inspRows.GetChildren())
		{
			_inspRows.RemoveChild(old);
			old.QueueFree();
		}

		if (many) ShowSeveralInspector(selected);
		else if (key == null) ShowWebInspector();
		else if (Web.Holds(key) && Catalogue.Find(key) is { } good) ShowGoodInspector(good);
		else if (Web.Recipe(key) is { } recipe) ShowRecipeInspector(recipe);
		else if (Web.Consumer(key) is { } consumer) ShowConsumerInspector(consumer);
		else ShowWebInspector();

		KeepInspectorScroll(keep);
	}

	/// <summary>Puts the scroll back, a frame on, once the rebuilt dock knows how tall it is.</summary>
	private async void KeepInspectorScroll(int at)
	{
		_inspScroll.ScrollVertical = at;
		if (at <= 0) return;
		string? shown = _inspShown;
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (!IsInstanceValid(_inspScroll) || _inspShown != shown) return;
		_inspScroll.ScrollVertical = at;
	}

	// ---- a good ----------------------------------------------------------------

	private void ShowGoodInspector(Good good)
	{
		string id = good.Id;
		VBoxContainer rows = _inspRows;

		var head = new HBoxContainer();
		head.AddThemeConstantOverride("separation", 8);
		rows.AddChild(head);
		head.AddChild(LabLook.Sprite(Sprites.Get(good.Icon), 64));
		head.AddChild(LabLook.Sprite(Sprites.Get(good.Sign), 48));
		var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
		titles.AddThemeConstantOverride("separation", 2);
		head.AddChild(titles);
		Label title = LabLook.Text(good.Name, 18, LabLook.Ink, trim: true);
		titles.AddChild(title);
		titles.AddChild(LabLook.Text(id, 12, LabLook.Faint));
		rows.AddChild(InspectorLook.Note(GoodLine(id), LabLook.Dim, 12));

		rows.AddChild(InspectorLook.Caption("Name"));
		LineEdit name = InspectorLook.Field(good.Name);
		name.TextChanged += text =>
		{
			title.Text = text;
			Change("renamed " + (text.Trim().Length > 0 ? text.Trim() : id), Touch.Catalogue,
				() => { if (Catalogue.Find(id) is { } live) live.Name = text; },
				merge: "good.name:" + id, keepInspector: true);
		};
		rows.AddChild(name);

		rows.AddChild(InspectorLook.Caption("Description"));
		TextEdit note = InspectorLook.Paragraph(good.Note, 5);
		note.TextChanged += () => Change("described " + NameOf(id), Touch.Catalogue,
			() => { if (Catalogue.Find(id) is { } live) live.Note = note.Text; },
			merge: "good.note:" + id, keepInspector: true);
		rows.AddChild(note);

		InspectorLook.Section(rows, "Tags");
		HFlowContainer tags = InspectorLook.Flow();
		rows.AddChild(tags);
		foreach (string tag in good.Tags)
		{
			string held = tag;
			string meaning = Catalogue.NoteFor(held);
			tags.AddChild(InspectorLook.Chip(held, InspectorLook.TagColour(held), null,
				meaning.Length > 0 ? $"{held}\n{meaning}" : held,
				() => Change($"took {held} off {NameOf(id)}", Touch.Catalogue, () => Catalogue.Find(id)?.Tags.Remove(held))));
		}
		tags.AddChild(InspectorLook.Small("+ tag", "Give it another property. A slot that accepts the tag then takes this good.",
			() => AskTag($"Tag {NameOf(id)}…", tag => InspectorTagGood(id, tag))));

		InspectorLook.Section(rows, "Sprites");
		rows.AddChild(SpriteRow(Sprites.Get(good.Icon), "Icon from the sheet…",
			"Pick a cell of sprites/icons.png.", () => PickSprite(id, sign: false)));
		rows.AddChild(SpriteRow(Sprites.Get(good.Sign), "Sign from the sheet…",
			"Pick a cell of sprites/signs.png. The sign's reading and its parts are kept.", () => PickSprite(id, sign: true)));
		rows.AddChild(InspectorAct("Import PNG…", "Copy a 16 px PNG into sprites/custom/ and use it as the icon.", () => AskPng(id)));

		InspectorLook.Section(rows, "Made by");
		IReadOnlyList<Recipe> makers = Analysis.MakersOf(id);
		if (makers.Count == 0)
			rows.AddChild(InspectorLook.Note("Nothing here makes it: it comes out of the ground, or in from another Domain.", LabLook.Faint, 12));
		foreach (Recipe maker in makers) rows.AddChild(InspectorJump(Analysis.TitleOf(maker), maker.Id));
		rows.AddChild(InspectorLook.Small("+ recipe that makes it", "A new recipe, empty, to the left of the good.", () =>
			Change($"new recipe making {NameOf(id)}", Touch.Web,
				() => SelectNew(EconomyEdit.NewRecipe(Web, id, null, LeftOf(id)).Id))));

		InspectorLook.Section(rows, "Used in");
		IReadOnlyList<Recipe> users = Analysis.UsersOf(id);
		if (users.Count == 0) rows.AddChild(InspectorLook.Note("No recipe here takes it.", LabLook.Faint, 12));
		foreach (Recipe user in users) rows.AddChild(InspectorJump(Analysis.TitleOf(user), user.Id));

		IReadOnlyList<Consumer> eaters = Analysis.ConsumersOf(id);
		if (eaters.Count > 0)
		{
			InspectorLook.Section(rows, "Consumed by");
			foreach (Consumer eater in eaters) rows.AddChild(InspectorJump(NameOf(eater.Id), eater.Id, LabLook.EatenPort));
		}

		InspectorLook.Section(rows, "This good");
		rows.AddChild(InspectorAct("Remove from this web", "It stays in the catalogue and in every other web.",
			() => RemoveNodes(new List<string> { id })));
		Label refusal = InspectorLook.Note("", LabLook.Error, 12);
		refusal.Visible = false;
		Button bin = InspectorAct("Delete from the catalogue…", "Out of the catalogue file altogether, if no other web holds it.",
			() => AskBinGood(id, refusal));
		bin.AddThemeColorOverride("font_color", LabLook.Error);
		rows.AddChild(bin);
		rows.AddChild(refusal);
	}

	/// <summary>What this web makes of a good, in one line.</summary>
	private string GoodLine(string id)
	{
		var words = new List<string> { Analysis.RoleOf(id).ToString().ToLowerInvariant(), "depth " + Analysis.Depth(id) };
		int uses = Analysis.UsersOf(id).Count;
		if (uses > 0) words.Add(uses == 1 ? "used by 1 recipe" : $"used by {uses} recipes");
		if (Analysis.IsConsumed(id)) words.Add("consumed");
		if (Analysis.IsHub(id)) words.Add("hub");
		return string.Join(" · ", words);
	}

	/// <summary>A row of the sprites block: the sprite as it stands, and the button that changes it.</summary>
	private static Control SpriteRow(Texture2D? now, string text, string tip, Action pressed)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		row.AddChild(LabLook.Sprite(now, 32));
		Button button = InspectorAct(text, tip, pressed);
		button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(button);
		return row;
	}

	private void InspectorTagGood(string id, string tag)
	{
		if (tag.Length == 0) return;
		if (Catalogue.Find(id) is not { } good) return;
		if (good.Tags.Contains(tag))
		{
			Say($"{good.Name} carries {tag} already.");
			return;
		}
		Change($"tagged {good.Name} {tag}", Touch.Catalogue, () => Catalogue.Find(id)?.Tags.Add(tag));
	}

	private void PickSprite(string id, bool sign)
	{
		if (Catalogue.Find(id) is not { } good) return;
		string atlas = sign ? "signs" : "icons";
		SpriteRef? now = sign ? good.Sign : good.Icon;
		int current = now != null && now.Atlas == atlas ? now.Index ?? -1 : -1;
		_inspSheet.Ask($"{(sign ? "The sign" : "The icon")} for {good.Name}", Sprites.CellCount(atlas),
			cell => Sprites.Cell(atlas, cell), current, GetViewport().GetMousePosition(), cell => SetSprite(id, sign, cell));
	}

	private void SetSprite(string id, bool sign, int cell) =>
		Change($"gave {NameOf(id)} a new {(sign ? "sign" : "icon")}", Touch.Catalogue, () =>
		{
			if (Catalogue.Find(id) is not { } good) return;
			if (!sign)
			{
				good.Icon = SpriteRef.Cell("icons", cell);
				return;
			}
			// A sign's reading, its source and its parts ride in Extra; only the cell changes.
			if (good.Sign == null) good.Sign = SpriteRef.Cell("signs", cell);
			else
			{
				good.Sign.Atlas = "signs";
				good.Sign.Index = cell;
				good.Sign.File = null;
			}
		});

	private void AskPng(string id)
	{
		_inspPngGood = id;
		_inspPng.PopupCentered(new Vector2I(900, 620));
	}

	/// <summary>
	/// The chosen PNG is copied into <c>sprites/custom/</c> under the good's id and becomes its
	/// icon. The copy is a file and no undo takes it back; the good pointing at it is a change
	/// like any other, so an undo leaves an unused PNG behind and nothing worse.
	/// </summary>
	private void ImportIcon(string path)
	{
		string id = _inspPngGood;
		if (Catalogue.Find(id) == null) return;
		string relative = "sprites/custom/" + id + ".png";
		try
		{
			string into = Store.Resolve(relative);
			Directory.CreateDirectory(Path.GetDirectoryName(into)!);
			File.Copy(path, into, overwrite: true);
		}
		catch (Exception e)
		{
			Say($"Could not copy that PNG: {e.Message}");
			GD.PushError(e.ToString());
			return;
		}
		Sprites.Forget(relative);
		Change($"gave {NameOf(id)} an icon of its own", Touch.Catalogue,
			() => { if (Catalogue.Find(id) is { } good) good.Icon = SpriteRef.Png(relative); });
	}

	/// <summary>
	/// Offers to delete a good from the catalogue, but reads every other web on disk first: one
	/// that another web holds cannot go, and the dock says in red which webs hold it.
	/// </summary>
	private void AskBinGood(string id, Label refusal)
	{
		var holders = new List<string>();
		foreach ((string webId, string webName) in Store.ListWebs())
		{
			if (webId == Web.Id) continue;
			try
			{
				if (Store.LoadWeb(webId).Holds(id)) holders.Add(webName);
			}
			catch (Exception e)
			{
				GD.PushWarning($"Economy lab: could not read {webId}.json: {e.Message}");
			}
		}

		if (holders.Count > 0)
		{
			string held = string.Join(", ", holders);
			refusal.Text = $"{NameOf(id)} is in {held}. Take it out of {(holders.Count == 1 ? "that web" : "those webs")} first.";
			refusal.Visible = true;
			Say($"{NameOf(id)} cannot leave the catalogue: {held} still holds it.");
			return;
		}
		refusal.Visible = false;
		_inspBinGood = id;
		_inspBin.DialogText = $"Delete {NameOf(id)} from the catalogue?\nIt goes from this web and out of catalogue.json. Undo brings it back.";
		_inspBin.PopupCentered();
	}

	private void BinGood()
	{
		string id = _inspBinGood;
		if (Catalogue.Find(id) == null) return;
		Change($"deleted {NameOf(id)} from the catalogue", Touch.Both, () =>
		{
			EconomyEdit.RemoveGood(Web, id);
			Catalogue.Remove(id);
		});
	}

	// ---- a recipe --------------------------------------------------------------

	private void ShowRecipeInspector(Recipe recipe)
	{
		string rid = recipe.Id;
		VBoxContainer rows = _inspRows;

		Label title = LabLook.Text(Analysis.TitleOf(recipe), 17, LabLook.Ink, trim: true);
		rows.AddChild(title);
		rows.AddChild(LabLook.Text(rid, 12, LabLook.Faint));

		rows.AddChild(InspectorLook.Caption("Label"));
		LineEdit label = InspectorLook.Field(recipe.Name, "blank reads as → what it makes");
		label.TextChanged += text =>
		{
			Change("labelled the recipe " + (text.Trim().Length > 0 ? text.Trim() : rid), Touch.Web,
				() => { if (Web.Recipe(rid) is { } live) live.Name = text; },
				merge: "recipe.name:" + rid, keepInspector: true);
			if (Web.Recipe(rid) is { } named) title.Text = Analysis.TitleOf(named);
		};
		rows.AddChild(label);

		rows.AddChild(InspectorLook.Caption("Note"));
		TextEdit note = InspectorLook.Paragraph(recipe.Note, 3);
		note.TextChanged += () => Change("noted " + NameOf(rid), Touch.Web,
			() => { if (Web.Recipe(rid) is { } live) live.Note = note.Text; },
			merge: "recipe.note:" + rid, keepInspector: true);
		rows.AddChild(note);

		InspectorLook.Section(rows, "Inputs");
		if (recipe.Inputs.Count == 0) rows.AddChild(InspectorLook.Note("It takes nothing.", LabLook.Warning, 12));
		for (int i = 0; i < recipe.Inputs.Count; i++) rows.AddChild(SlotBlock(rid, recipe.Inputs[i], i));

		var adding = new HBoxContainer();
		adding.AddThemeConstantOverride("separation", 4);
		rows.AddChild(adding);
		Button addGood = InspectorLook.Small("+ input: a good…", "A new slot that takes one named good.",
			() => AskGood("What else goes in?", gid => TakeGood(rid, int.MaxValue, gid)));
		addGood.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		adding.AddChild(addGood);
		Button addTag = InspectorLook.Small("+ input: a tag…", "A new slot that takes anything carrying the tag.",
			() => AskTag("A slot for anything tagged…", tag => AcceptTag(rid, Web.Recipe(rid)?.Inputs.Count ?? 0, tag)));
		addTag.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		adding.AddChild(addTag);

		InspectorLook.Section(rows, "Outputs");
		if (recipe.Outputs.Count == 0) rows.AddChild(InspectorLook.Note("It makes nothing.", LabLook.Error, 12));
		for (int o = 0; o < recipe.Outputs.Count; o++)
		{
			int port = o;
			string made = recipe.Outputs[o].Good;
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 4);
			row.AddChild(LabLook.Sprite(IconOf(made), 16));
			if (Web.Holds(made))
			{
				Control jump = InspectorJump(NameOf(made), made, LabLook.ProductPort);
				jump.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				row.AddChild(jump);
			}
			else
			{
				Label missing = InspectorLook.Note($"{made} is not in this web", LabLook.Error, 12);
				row.AddChild(missing);
			}
			row.AddChild(InspectorLook.Small("change…", "Make it something else instead.",
				() => AskGood("What does it make?", gid => SetOutput(rid, port, gid))));
			row.AddChild(InspectorLook.Cross("Stop making this.", () =>
				Change($"{NameOf(rid)} no longer makes {NameOf(made)}", Touch.Web, () =>
				{
					if (Web.Recipe(rid) is { } live && port < live.Outputs.Count) live.Outputs.RemoveAt(port);
				})));
			rows.AddChild(row);
		}
		rows.AddChild(InspectorLook.Small("+ output…", "Something else this recipe makes as well.",
			() => AskGood("What else does it make?", gid => SetOutput(rid, int.MaxValue, gid))));

		InspectorLook.Section(rows, "This recipe");
		Button bin = InspectorAct("Delete this recipe", "The goods stay; only this way of making them goes.",
			() => RemoveNodes(new List<string> { rid }));
		bin.AddThemeColorOverride("font_color", LabLook.Error);
		rows.AddChild(bin);
	}

	/// <summary>
	/// One input slot in a frame: what fills it, whether the recipe runs without it, and what
	/// each tag of it admits from this web as things stand.
	/// </summary>
	private Control SlotBlock(string rid, RecipeInput slot, int port)
	{
		var frame = new PanelContainer();
		frame.AddThemeStyleboxOverride("panel", LabLook.Box(LabLook.Field, 6, 6, marginX: 8, marginY: 6));
		var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		rows.AddThemeConstantOverride("separation", 4);
		frame.AddChild(rows);

		var head = new HBoxContainer();
		head.AddThemeConstantOverride("separation", 6);
		rows.AddChild(head);
		Label caption = InspectorLook.Caption($"Input {port + 1}");
		caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		head.AddChild(caption);
		var optional = new CheckBox
		{
			Text = "optional",
			ButtonPressed = slot.Optional,
			FocusMode = FocusModeEnum.None,
			TooltipText = "An upgrade or a variant: the recipe runs without it, and it does not count towards depth.",
		};
		optional.AddThemeFontSizeOverride("font_size", 12);
		optional.AddThemeColorOverride("font_color", LabLook.Dim);
		optional.Toggled += on => Change($"input {port + 1} of {NameOf(rid)} is {(on ? "optional" : "required")}", Touch.Web, () =>
		{
			if (Web.Recipe(rid) is { } live && port < live.Inputs.Count) live.Inputs[port].Optional = on;
		});
		head.AddChild(optional);
		head.AddChild(InspectorLook.Small("remove", "Take this slot off the recipe.", () =>
			Change($"took input {port + 1} off {NameOf(rid)}", Touch.Web, () =>
			{
				if (Web.Recipe(rid) is { } live && port < live.Inputs.Count) live.Inputs.RemoveAt(port);
			})));

		rows.AddChild(AcceptorChips(slot.Accepts, rid, port));
		if (slot.Accepts.Count == 0) rows.AddChild(InspectorLook.Note("accepts nothing", LabLook.Error, 12));
		else if (slot.Accepts.Count > 1) rows.AddChild(InspectorLook.Note("any one of these", LabLook.Faint, 12));
		foreach (string acceptor in slot.Accepts.Where(Acceptor.IsTag)) rows.AddChild(AdmitsLine(rid, port, acceptor));
		rows.AddChild(AcceptorButtons(rid, port));
		return frame;
	}

	/// <summary>
	/// The chips of one accept list, a good's with its icon and a tag's in amber, each with a ×.
	/// The same list serves a recipe's slot and a consumer, which accept things the same way.
	/// </summary>
	private Control AcceptorChips(List<string> accepts, string nodeKey, int port)
	{
		HFlowContainer flow = InspectorLook.Flow();
		foreach (string acceptor in accepts)
		{
			string held = acceptor;
			if (Acceptor.IsTag(held))
			{
				string tag = Acceptor.TagOf(held);
				string meaning = Catalogue.NoteFor(tag);
				flow.AddChild(InspectorLook.Chip("#" + tag, LabLook.TagPort.Darkened(0.62f), null,
					meaning.Length > 0 ? $"{held}\n{meaning}" : $"{held}\nAnything in this web carrying it fits.",
					() => DropAcceptor(nodeKey, port, held), LabLook.TagPort));
				continue;
			}
			Good? good = Catalogue.Find(held);
			flow.AddChild(InspectorLook.Chip(good?.Name ?? held, LabLook.Body.Lightened(0.12f), IconOf(held),
				good == null ? $"{held} is not in the catalogue." : good.Note.Length > 0 ? $"{good.Name}\n{good.Note}" : good.Name,
				() => DropAcceptor(nodeKey, port, held)));
		}
		return flow;
	}

	/// <summary>The two ways to fill a slot or a consumer: a named good, or a tag.</summary>
	private Control AcceptorButtons(string nodeKey, int port)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 4);
		Button good = InspectorLook.Small("+ good…", "Another good this takes. One not in the web comes in with it.",
			() => AskGood("Which good?", gid => TakeGood(nodeKey, port, gid)));
		good.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(good);
		Button tag = InspectorLook.Small("+ tag…", "Anything in the web carrying the tag fits, now and later.",
			() => AskTag("Anything tagged…", tag => AcceptTag(nodeKey, port, tag)));
		tag.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(tag);
		return row;
	}

	/// <summary>What one tag of a slot or a consumer lets in from this web as it stands.</summary>
	private Control AdmitsLine(string nodeKey, int port, string acceptor)
	{
		bool consumer = Web.Consumer(nodeKey) != null;
		List<string> admitted = Analysis.Links
			.Where(link => link.To == nodeKey && link.ByTag && link.Via == acceptor && (consumer || link.Port == port))
			.Select(link => NameOf(link.From)).ToList();
		return admitted.Count == 0
			? InspectorLook.Note($"Nothing in this web carries {acceptor}.", LabLook.Warning, 12)
			: InspectorLook.Note($"{acceptor} admits {string.Join(", ", admitted)}.", LabLook.Dim, 12);
	}

	/// <summary>
	/// A good into a slot or a consumer, brought into the web a little to the left of the node
	/// first if it is not here yet. A port past the last slot makes a new slot of it.
	/// </summary>
	private void TakeGood(string nodeKey, int port, string goodId)
	{
		Change($"{NameOf(nodeKey)} takes {NameOf(goodId)}", Touch.Web, () =>
		{
			if (!Web.Holds(goodId)) EconomyEdit.AddGood(Web, goodId, LeftOf(nodeKey));
			if (Web.Consumer(nodeKey) is { } consumer) EconomyEdit.Accept(consumer.Accepts, goodId);
			else if (Web.Recipe(nodeKey) is { } recipe)
			{
				if (port < recipe.Inputs.Count) EconomyEdit.Accept(recipe.Inputs[port].Accepts, goodId);
				else recipe.Inputs.Add(new RecipeInput { Accepts = { goodId } });
			}
		});
	}

	private void DropAcceptor(string nodeKey, int port, string acceptor)
	{
		string what = Acceptor.IsTag(acceptor) ? "#" + Acceptor.TagOf(acceptor) : NameOf(acceptor);
		Change($"{NameOf(nodeKey)} no longer takes {what}", Touch.Web, () =>
		{
			if (Web.Consumer(nodeKey) is { } consumer) consumer.Accepts.Remove(acceptor);
			else if (Web.Recipe(nodeKey) is { } recipe && port < recipe.Inputs.Count) recipe.Inputs[port].Accepts.Remove(acceptor);
		});
	}

	/// <summary>A recipe's output set or added; a good it makes already is refused rather than doubled.</summary>
	private void SetOutput(string rid, int port, string goodId)
	{
		if (Web.Recipe(rid) is not { } recipe) return;
		if (recipe.Makes(goodId) && (port >= recipe.Outputs.Count || recipe.Outputs[port].Good != goodId))
		{
			Say($"{NameOf(rid)} makes {NameOf(goodId)} already.");
			return;
		}
		Change($"{NameOf(rid)} makes {NameOf(goodId)}", Touch.Web, () =>
		{
			if (!Web.Holds(goodId)) EconomyEdit.AddGood(Web, goodId, RightOf(rid));
			if (Web.Recipe(rid) is not { } live) return;
			if (port < live.Outputs.Count) live.Outputs[port].Good = goodId;
			else live.Outputs.Add(new RecipeOutput { Good = goodId });
		});
	}

	// ---- a consumer ------------------------------------------------------------

	private void ShowConsumerInspector(Consumer consumer)
	{
		string cid = consumer.Id;
		VBoxContainer rows = _inspRows;

		Label title = LabLook.Text(consumer.Name.Length > 0 ? consumer.Name : "The consumer", 17, LabLook.Ink, trim: true);
		rows.AddChild(title);
		rows.AddChild(LabLook.Text(cid, 12, LabLook.Faint));
		rows.AddChild(InspectorLook.Note("Goods that reach a consumer are eaten, drunk, worn out or used up.", LabLook.Dim, 12));

		rows.AddChild(InspectorLook.Caption("Name"));
		LineEdit name = InspectorLook.Field(consumer.Name, "Food");
		name.TextChanged += text =>
		{
			title.Text = text.Trim().Length > 0 ? text : "The consumer";
			Change("renamed the consumer " + (text.Trim().Length > 0 ? text.Trim() : cid), Touch.Web,
				() => { if (Web.Consumer(cid) is { } live) live.Name = text; },
				merge: "consumer.name:" + cid, keepInspector: true);
		};
		rows.AddChild(name);

		rows.AddChild(InspectorLook.Caption("Note"));
		TextEdit note = InspectorLook.Paragraph(consumer.Note, 3);
		note.TextChanged += () => Change("noted the consumer " + NameOf(cid), Touch.Web,
			() => { if (Web.Consumer(cid) is { } live) live.Note = note.Text; },
			merge: "consumer.note:" + cid, keepInspector: true);
		rows.AddChild(note);

		InspectorLook.Section(rows, "Accepts");
		rows.AddChild(AcceptorChips(consumer.Accepts, cid, 0));
		if (consumer.Accepts.Count == 0) rows.AddChild(InspectorLook.Note("accepts nothing", LabLook.Warning, 12));
		foreach (string acceptor in consumer.Accepts.Where(Acceptor.IsTag)) rows.AddChild(AdmitsLine(cid, 0, acceptor));
		rows.AddChild(AcceptorButtons(cid, 0));

		InspectorLook.Section(rows, "What reaches it now");
		List<string> eaten = Analysis.Links.Where(link => link.Kind == LinkKind.Consumed && link.To == cid)
			.Select(link => link.From).Distinct().ToList();
		if (eaten.Count == 0) rows.AddChild(InspectorLook.Note("Nothing, yet.", LabLook.Faint, 12));
		foreach (string goodId in eaten) rows.AddChild(InspectorJump(NameOf(goodId), goodId, LabLook.EatenPort, IconOf(goodId)));

		InspectorLook.Section(rows, "This consumer");
		Button bin = InspectorAct("Delete this consumer", "The goods stay in the web; they simply lead nowhere.",
			() => RemoveNodes(new List<string> { cid }));
		bin.AddThemeColorOverride("font_color", LabLook.Error);
		rows.AddChild(bin);
	}

	// ---- the web itself --------------------------------------------------------

	private void ShowWebInspector()
	{
		VBoxContainer rows = _inspRows;
		rows.AddChild(LabLook.Text("The web", 17, LabLook.Ink, trim: true));
		rows.AddChild(InspectorLook.Note("Nothing is selected, so this is the web: one version of the economy.", LabLook.Dim, 12));

		rows.AddChild(InspectorLook.Caption("Name"));
		LineEdit name = InspectorLook.Field(Web.Name);
		name.TextChanged += text => Change("renamed the web " + (text.Trim().Length > 0 ? text.Trim() : Web.Id), Touch.Web,
			() => Web.Name = text, merge: "web.name:" + Web.Id, keepInspector: true);
		rows.AddChild(name);

		rows.AddChild(InspectorLook.Caption("Note"));
		TextEdit note = InspectorLook.Paragraph(Web.Note, 4);
		note.TextChanged += () => Change("noted the web", Touch.Web,
			() => Web.Note = note.Text, merge: "web.note:" + Web.Id, keepInspector: true);
		rows.AddChild(note);
		rows.AddChild(InspectorLook.Note($"resources/economy/webs/{Web.Id}.json", LabLook.Faint, 12));

		InspectorLook.Section(rows, "Numbers");
		int sources = 0, middles = 0, finals = 0, loose = 0;
		foreach (string goodId in Web.Goods)
			switch (Analysis.RoleOf(goodId))
			{
				case GoodRole.Source: sources++; break;
				case GoodRole.Intermediate: middles++; break;
				case GoodRole.Final: finals++; break;
				default: loose++; break;
			}
		rows.AddChild(InspectorLook.Note($"{Web.Goods.Count} goods: {sources} sources, {middles} intermediate, {finals} final, {loose} loose.", LabLook.Dim, 12));
		rows.AddChild(InspectorLook.Note($"{Web.Recipes.Count} recipes · {Web.Consumers.Count} consumers · {Analysis.Links.Count} links", LabLook.Dim, 12));

		string? deepest = Web.Goods.Where(g => Catalogue.Find(g) != null)
			.OrderByDescending(Analysis.Depth).ThenBy(g => g, StringComparer.Ordinal).FirstOrDefault();
		if (deepest != null)
			rows.AddChild(InspectorJump($"deepest: {NameOf(deepest)}, {Analysis.Depth(deepest)} steps from the ground",
				deepest, LabLook.Dim, IconOf(deepest)));

		List<string> hubs = Web.Goods.Where(Analysis.IsHub)
			.OrderByDescending(g => Analysis.UsersOf(g).Count).ThenBy(NameOf, StringComparer.OrdinalIgnoreCase).ToList();
		if (hubs.Count > 0)
		{
			InspectorLook.Section(rows, $"Hubs: used by {WebAnalysis.HubUses} recipes or more");
			foreach (string hub in hubs)
				rows.AddChild(InspectorJump($"{NameOf(hub)} ×{Analysis.UsersOf(hub).Count}", hub, LabLook.Ink, IconOf(hub)));
		}

		InspectorLook.Section(rows, "Issues");
		if (Analysis.Issues.Count == 0) rows.AddChild(InspectorLook.Note("No issues.", LabLook.Dim, 12));
		foreach (WebIssue issue in Analysis.Issues.OrderByDescending(i => i.Level))
			rows.AddChild(InspectorJump(issue.Text, issue.Node, LabLook.IssueColour(issue.Level)));

		InspectorLook.Section(rows, "The colours");
		HFlowContainer stages = InspectorLook.Flow();
		rows.AddChild(stages);
		foreach ((string stage, Color colour) in LabLook.Stages) stages.AddChild(InspectorLook.Swatch(stage, colour));
		rows.AddChild(InspectorLook.Swatch("a required input", LabLook.InputPort));
		rows.AddChild(InspectorLook.Swatch("optional", LabLook.OptionalPort));
		rows.AddChild(InspectorLook.Swatch("admitted by a tag", LabLook.TagPort));
		rows.AddChild(InspectorLook.Swatch("what a recipe makes", LabLook.ProductPort));
		rows.AddChild(InspectorLook.Swatch("consumed", LabLook.EatenPort));
	}

	// ---- several nodes ---------------------------------------------------------

	private void ShowSeveralInspector(List<string> keys)
	{
		VBoxContainer rows = _inspRows;
		int goods = keys.Count(Web.Holds), recipes = keys.Count(k => Web.Recipe(k) != null), consumers = keys.Count(k => Web.Consumer(k) != null);
		rows.AddChild(LabLook.Text($"{keys.Count} nodes selected", 17, LabLook.Ink, trim: true));
		rows.AddChild(InspectorLook.Note($"{goods} goods · {recipes} recipes · {consumers} consumers", LabLook.Dim, 12));
		rows.AddChild(InspectorLook.Note(string.Join(", ", keys.Take(14).Select(NameOf)) + (keys.Count > 14 ? ", …" : ""), LabLook.Faint, 12));

		InspectorLook.Section(rows, "All of them");
		rows.AddChild(InspectorAct("Remove them from this web", "Goods stay in the catalogue; recipes and consumers are deleted.",
			() => RemoveNodes(SelectedKeys())));
		Button cut = InspectorAct("New web from the selected goods…", "A web of these goods and everything upstream of them, down to the ground.", AskNewWeb);
		cut.Disabled = goods == 0;
		rows.AddChild(cut);
	}

	// ---- the small shared parts ------------------------------------------------

	/// <summary>A full-width action button of the dock, a shade smaller than the top bar's.</summary>
	private static Button InspectorAct(string text, string tip, Action pressed)
	{
		Button button = Press(text, tip, pressed);
		button.AddThemeFontSizeOverride("font_size", 13);
		return button;
	}

	/// <summary>A line of the dock that goes to another node of the web when clicked.</summary>
	private Control InspectorJump(string text, string key, Color? colour = null, Texture2D? icon = null) =>
		InspectorLook.Hit(text, colour ?? LabLook.Ink, Exists(key) ? "Go to it" : $"{key} is not in this web", () => Select(key, focus: true), icon);

	/// <summary>A place a little to the left of a node, for what feeds it.</summary>
	private Spot LeftOf(string key, int by = 260)
	{
		Spot at = Web.Layout.GetValueOrDefault(key, CentreSpot());
		return new Spot(at.X - by, at.Y);
	}

	/// <summary>A place a little to the right of a node, for what it makes.</summary>
	private Spot RightOf(string key, int by = 260)
	{
		Spot at = Web.Layout.GetValueOrDefault(key, CentreSpot());
		return new Spot(at.X + by, at.Y);
	}
}
