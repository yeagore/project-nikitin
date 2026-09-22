using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// The right-hand dock: whatever is selected, laid out for editing. A good shows its name, its
/// description, its tags, the varieties of it this web can make and the ones written by hand, the
/// stacks those varieties come to when units stack by property and the same split by a namespace
/// or two, each stack wearing the icon a unit of it would; then its two sprites and what makes,
/// uses and eats it. A recipe shows its element, where the work has to stand, itself written out
/// in signs as a formula, then its input slots — what each admits, whether it passes variety on
/// and what it grants of its own — and its outputs; a consumer what reaches it; several nodes what
/// can be done to them all; and with nothing selected, the web itself — its numbers, how its
/// recipes fall among the elements, its hubs, what the analysis found wrong, and the legend of the
/// canvas's colours.
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

	/// <summary>Which of a good's two sprites the PNG dialog was opened for.</summary>
	private bool _inspPngSign;

	/// <summary>How many tag values one namespace's line of the varieties block names before it counts the rest.</summary>
	private const int VarietyValuesShown = 20;

	/// <summary>How many whole varieties the block lists before it counts the rest.</summary>
	private const int VarietiesListed = 8;

	/// <summary>How many stacks the split-by block lists before it counts the rest.</summary>
	private const int StacksListed = 24;

	/// <summary>
	/// Which namespaces the selected good's varieties are being split by, and the good they were
	/// chosen for. The dock is rebuilt on every change, so the choice lives here rather than on a
	/// button; another good clears it.
	/// </summary>
	private readonly HashSet<string> _inspSplit = new(StringComparer.Ordinal);

	private string? _inspSplitGood;

	/// <summary>The namespace a <c>site:</c> tag belongs to: a place that is not a soil.</summary>
	private const string SiteSpace = "site";

	/// <summary>
	/// A tag of a property namespace: what units stack by. <see cref="LabLook"/> holds the violet
	/// of a variety tag and the amber of a core one; this third part of the tag system is newer
	/// than that file, so its colour waits here until the look is next gathered up.
	/// </summary>
	private static readonly Color PropertyTag = new("6fc9d4");

	/// <summary>
	/// A shell run can open the dock already split: <c>split=heart</c>, <c>split=heart,soil</c>.
	/// The shot arguments proper are read in <c>EconomyLab.cs</c>; this one belongs to the dock alone.
	/// </summary>
	private static string[]? _shotSplit;

	private static string[] ShotSplit => _shotSplit ??= OS.GetCmdlineUserArgs()
		.FirstOrDefault(arg => arg.StartsWith("split=", StringComparison.Ordinal))?["split=".Length..]
		.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
		?? Array.Empty<string>();

	/// <summary>
	/// And <c>scroll=900</c> holds the dock that far down, so a picture can be taken of a block
	/// that sits past the fold. Nothing but a shell run passes it.
	/// </summary>
	private static int _shotScroll = -1;

	private static int ShotScroll => _shotScroll >= 0 ? _shotScroll : _shotScroll =
		int.TryParse(OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("scroll=", StringComparison.Ordinal))?["scroll=".Length..],
			out int at) ? at : 0;

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

		_inspBin = new ConfirmationDialog { Title = "Delete from this web's palette", OkButtonText = "Delete" };
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
		_inspPng.FileSelected += ImportSprite;
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
		if (keep == 0) keep = ShotScroll;
		_inspShown = key;
		_inspShownMany = many;

		foreach (Node old in _inspRows.GetChildren())
		{
			_inspRows.RemoveChild(old);
			old.QueueFree();
		}

		// A locked web says so at the head of every view: the core refuses the edits, and this is why.
		if (Web.Locked)
			_inspRows.AddChild(InspectorLook.Note(
				"Locked: a reference copy. Look, cut and import from it; New… → \"A copy of the open web\" to change it.",
				LabLook.Accent, 12));

		if (many) ShowSeveralInspector(selected);
		else if (key == null) ShowWebInspector();
		else if (Web.Holds(key) && Palette.Find(key) is { } good) ShowGoodInspector(good);
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
			Change("renamed " + (text.Trim().Length > 0 ? text.Trim() : id), () => { if (Palette.Find(id) is { } live) live.Name = text; },
				merge: "good.name:" + id, keepInspector: true);
		};
		rows.AddChild(name);

		rows.AddChild(InspectorLook.Caption("Description"));
		TextEdit note = InspectorLook.Paragraph(good.Note, 5);
		note.TextChanged += () => Change("described " + NameOf(id), () => { if (Palette.Find(id) is { } live) live.Note = note.Text; },
			merge: "good.note:" + id, keepInspector: true);
		rows.AddChild(note);

		InspectorLook.Section(rows, "Tags");
		HFlowContainer tags = InspectorLook.Flow();
		rows.AddChild(tags);
		foreach (string tag in good.Tags)
		{
			string held = tag;
			tags.AddChild(InspectorLook.Chip(held, InspectorLook.TagFill(Palette, held), null,
				InspectorLook.Lines(held, Palette.NoteFor(held), InspectorLook.RoleLine(Palette, held)),
				() => Change($"took {held} off {NameOf(id)}", () => Palette.Find(id)?.Tags.Remove(held)),
				InspectorLook.TagInk(Palette, held)));
		}
		tags.AddChild(InspectorLook.Small("+ tag", "Give it another property. A slot that accepts the tag then takes this good.",
			() => AskTag($"Tag {NameOf(id)}…", tag => InspectorTagGood(id, tag))));

		GoodVarieties(rows, id);

		InspectorLook.Section(rows, "Sprites");
		rows.AddChild(SpriteRow(id, sign: false));
		rows.AddChild(SpriteRow(id, sign: true));

		InspectorLook.Section(rows, "Made by");
		IReadOnlyList<Recipe> makers = Analysis.MakersOf(id);
		if (makers.Count == 0)
			rows.AddChild(InspectorLook.Note("Nothing here makes it: it comes out of the ground, or in from another Domain.", LabLook.Faint, 12));
		foreach (Recipe maker in makers) rows.AddChild(InspectorJump(Analysis.TitleOf(maker), maker.Id));
		rows.AddChild(InspectorLook.Small("+ recipe that makes it", "A new recipe, empty, to the left of the good.", () =>
			Change($"new recipe making {NameOf(id)}", () => SelectNew(EconomyEdit.NewRecipe(Web, id, null, LeftOf(id)).Id))));

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
		rows.AddChild(InspectorAct("Remove from this web", "It stays in this web's palette, so it can come back on the canvas.",
			() => RemoveNodes(new List<string> { id })));
		Button bin = InspectorAct("Delete from this web's palette…", "Out of this web altogether. Every other web keeps its own.",
			() => AskBinGood(id));
		bin.AddThemeColorOverride("font_color", LabLook.Error);
		rows.AddChild(bin);
	}

	// ---- a good's varieties ----------------------------------------------------

	/// <summary>
	/// What this web can make of a good, and the kinds of it written by hand. The first part is
	/// read off the analysis: every variety the passing slots upstream allow, which can run into
	/// the hundreds, so it is counted and sampled rather than listed. The second is the good's
	/// own list, one framed block each.
	/// </summary>
	private void GoodVarieties(VBoxContainer rows, string id)
	{
		Good? good = Palette.Find(id);
		if (_inspSplitGood != id)
		{
			// Another good: the split starts over, seeded only by what a shell run asked for.
			_inspSplitGood = id;
			_inspSplit.Clear();
			foreach (string space in ShotSplit) _inspSplit.Add(space);
		}

		InspectorLook.Section(rows, "Varieties");
		VarietySet made = Analysis.VarietiesOf(id);
		if (made.IsPlain)
			rows.AddChild(InspectorLook.Note("One plain good: nothing it is made of passes variety on, and it carries no variety tag.", LabLook.Faint, 12));
		else
		{
			string headline = made.Count == 1 ? "1 variety" : $"{(made.Capped ? "about " : "")}{made.Count} varieties";
			rows.AddChild(LabLook.Text(headline, 13, LabLook.VarietyTag));
			foreach ((string space, List<string> values) in made.ByNamespace()) rows.AddChild(VarietyValues(space, values));
			if (made.Sets.Count > 1)
			{
				// One to a line, shortest first, each wearing the icon a unit of it would: a variety of
				// five tags reads as a sentence, a dozen of them as a wall.
				foreach (IReadOnlyList<string> variety in made.Sets.OrderBy(v => v.Count).Take(VarietiesListed))
					rows.AddChild(VarietyLine(good, variety, 20, LabLook.Dim, "· "));
				long more = made.Count - Math.Min(made.Sets.Count, VarietiesListed);
				if (more > 0) rows.AddChild(InspectorLook.Note($"  and {more} more", LabLook.Faint, 12));
			}
			GoodStacks(rows, id, good, made);
			GoodSplit(rows, id, good);
		}
		if (LayerLine(good) is { } layers) rows.AddChild(layers);

		rows.AddChild(InspectorLook.Caption("Authored varieties"));
		rows.AddChild(InspectorLook.Note("Kinds of this good made by hand, like rye and wheat of grain. One node, the same slots; their variety tags travel downstream.", LabLook.Dim, 12));
		if (good != null)
			foreach (Variety variety in good.VarietyList) rows.AddChild(VarietyBlock(id, variety));

		var adding = new HBoxContainer();
		adding.AddThemeConstantOverride("separation", 4);
		rows.AddChild(adding);
		LineEdit named = InspectorLook.Field("", "rye");
		named.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		named.TextSubmitted += _ => AddVariety(id, named);
		adding.AddChild(named);
		adding.AddChild(InspectorLook.Small("+ variety", "A kind of this good made by hand: the same node in the web, with its own tags.",
			() => AddVariety(id, named)));
	}

	/// <summary>One namespace of a good's varieties: its name dim, then the values that turn up in it.</summary>
	private static Control VarietyValues(string space, List<string> tags)
	{
		var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		row.AddThemeConstantOverride("separation", 4);
		row.AddChild(LabLook.Text((space.Length > 0 ? space : "no namespace") + ":", 12, LabLook.Dim));
		List<string> shown = tags.Take(VarietyValuesShown).Select(LabLook.Short).ToList();
		int more = tags.Count - shown.Count;
		row.AddChild(InspectorLook.Note(string.Join(", ", shown) + (more > 0 ? $", and {more} more" : ""), LabLook.VarietyTag, 12));
		return row;
	}

	/// <summary>
	/// One variety, or one stack, in a line: the good's icon as a unit carrying those tags shows
	/// it — the shape the good's, the hue the tags' — and the tags in words. Composing is cached,
	/// so a list of two dozen of these costs one pass over a 16 px picture each.
	/// </summary>
	private Control VarietyLine(Good? good, IReadOnlyList<string> tags, int side, Color ink, string lead = "")
	{
		var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		row.AddThemeConstantOverride("separation", 6);
		row.AddChild(LabLook.Sprite(good == null ? null : Sprites.Compose(good, tags), side));
		row.AddChild(InspectorLook.Note(lead + LabLook.VarietyName(tags), ink, 12));
		return row;
	}

	/// <summary>
	/// What the market tells apart. A unit stacks by the property tags its variety tags imply, so
	/// seventeen soils that mean five kinds of work are five stacks and not seventeen piles. Worked
	/// out the way the varieties are, so it is exact even where their list has been cut short.
	/// </summary>
	private void GoodStacks(VBoxContainer rows, string id, Good? good, VarietySet made)
	{
		VarietySet stacks = Analysis.StacksOf(id);
		string count = stacks.Count == 1 ? "1 stack" : $"{(stacks.Capped ? "about " : "")}{stacks.Count} stacks";
		rows.AddChild(LabLook.Text("Stack by property: " + count, 13, PropertyTag));
		if (stacks.Count < made.Count)
			rows.AddChild(InspectorLook.Note("what the market tells apart: the varieties above come to this many kinds of unit.", LabLook.Faint, 12));
		foreach (IReadOnlyList<string> stack in stacks.Sets.OrderBy(s => s.Count).Take(VarietiesListed))
			rows.AddChild(VarietyLine(good, stack, 24, LabLook.Dim));
		long more = stacks.Count - Math.Min(stacks.Sets.Count, VarietiesListed);
		if (more > 0) rows.AddChild(InspectorLook.Note($"  and {more} more", LabLook.Faint, 12));
	}

	/// <summary>
	/// The same varieties seen through one namespace or two: a row of namespaces to switch on, and
	/// under it what a unit would be told apart by if only those were read. A variety namespace
	/// keeps its own tags, a property one what the varieties imply, and everything else is let go —
	/// so golems split by heart are three, whatever their soils. Nothing chosen is one stack.
	/// </summary>
	private void GoodSplit(VBoxContainer rows, string id, Good? good)
	{
		var spaces = new List<string>();
		foreach ((string space, _) in Analysis.VarietiesOf(id).ByNamespace())
			if (space.Length > 0 && Palette.Namespace(space)?.Role == TagNamespace.Variety && !spaces.Contains(space)) spaces.Add(space);
		foreach ((string space, _) in Analysis.StacksOf(id).ByNamespace())
			if (space.Length > 0 && !spaces.Contains(space)) spaces.Add(space);
		if (spaces.Count == 0) return;

		rows.AddChild(InspectorLook.Caption("Split by"));
		// A flow, not a row: the dock is a fixed width and a golem has five namespaces to offer.
		HFlowContainer picks = InspectorLook.Flow();
		rows.AddChild(picks);
		foreach (string space in spaces)
		{
			string held = space;
			bool property = Palette.Namespace(held)?.Role == TagNamespace.Property;
			var pick = new Button
			{
				Text = held,
				ToggleMode = true,
				ButtonPressed = _inspSplit.Contains(held),
				FocusMode = FocusModeEnum.None,
				TooltipText = property
					? $"{held}: a property namespace. Units stack by it."
					: $"{held}: a variety namespace. It rides from inputs to outputs.",
			};
			// Quiet until it is on, and then in its own colour: the look of the lab's other small buttons.
			Color ink = property ? PropertyTag : LabLook.VarietyTag;
			pick.AddThemeFontSizeOverride("font_size", 12);
			foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" })
				pick.AddThemeColorOverride(state, ink);
			pick.AddThemeStyleboxOverride("normal", LabLook.Box(new Color(1, 1, 1, 0.06f), 4, 4, marginX: 6, marginY: 2));
			pick.AddThemeStyleboxOverride("hover", LabLook.Box(new Color(1, 1, 1, 0.14f), 4, 4, marginX: 6, marginY: 2));
			pick.AddThemeStyleboxOverride("pressed", LabLook.Box(ink.Darkened(0.68f), 4, 4, ink, 1, 6, 2));
			pick.AddThemeStyleboxOverride("hover_pressed", LabLook.Box(ink.Darkened(0.55f), 4, 4, ink, 1, 6, 2));
			pick.Toggled += on =>
			{
				if (on) _inspSplit.Add(held);
				else _inspSplit.Remove(held);
				RefreshInspector();
			};
			picks.AddChild(pick);
		}

		List<string> chosen = spaces.Where(_inspSplit.Contains).ToList();
		if (chosen.Count == 0)
		{
			rows.AddChild(VarietyLine(good, Array.Empty<string>(), 24, LabLook.Dim));
			rows.AddChild(InspectorLook.Note("One stack: everything together. Switch a namespace on to see what it tells apart.", LabLook.Faint, 12));
			return;
		}

		VarietySet split = Analysis.VarietiesIn(id, chosen);
		string count = split.Count == 1 ? "1 stack" : $"{(split.Capped ? "about " : "")}{split.Count} stacks";
		rows.AddChild(LabLook.Text($"Split by {Listed(chosen)}: {count}", 13, PropertyTag));
		foreach (IReadOnlyList<string> set in split.Sets.OrderBy(s => s.Count).Take(StacksListed))
			rows.AddChild(VarietyLine(good, set, 24, LabLook.Dim));
		long more = split.Count - Math.Min(split.Sets.Count, StacksListed);
		if (more > 0) rows.AddChild(InspectorLook.Note($"  and {more} more", LabLook.Faint, 12));
	}

	/// <summary>Words in a list, the last joined with "and": heart, soil and fit.</summary>
	private static string Listed(IReadOnlyList<string> words) => words.Count switch
	{
		0 => "",
		1 => words[0],
		_ => string.Join(", ", words.Take(words.Count - 1)) + " and " + words[^1],
	};

	/// <summary>
	/// How the good's icon answers to a variety, in one line: which namespace or tag tints which
	/// part of the picture. A layer with no mask has the whole icon. Read from the data and not
	/// editable here; the sprites are authored beside the sheets.
	/// </summary>
	private static Control? LayerLine(Good? good)
	{
		if (good?.Layers is not { Count: > 0 } layers) return null;
		var said = new List<string>();
		for (int i = 0; i < layers.Count; i++)
			said.Add(layers[i].Match + (i == 0 ? " tints " : " ") + MaskName(layers[i].Mask));
		return InspectorLook.Note("Icon: " + string.Join(", ", said) + ".", LabLook.Faint, 12);
	}

	/// <summary>What part of an icon a mask covers, in words: its file's name, or the whole picture for none.</summary>
	private static string MaskName(SpriteRef? mask)
	{
		if (mask == null) return "the whole icon";
		if (!string.IsNullOrEmpty(mask.File)) return "the " + Path.GetFileNameWithoutExtension(mask.File);
		return $"a part ({mask.Atlas} #{mask.Index})";
	}

	/// <summary>
	/// One authored variety in a frame: its name, its id, the tags that set it apart, and a way
	/// to take it off again. Nothing here holds the variety; every change finds it by its id.
	/// </summary>
	private Control VarietyBlock(string goodId, Variety variety)
	{
		string vid = variety.Id, vname = variety.Name;
		var frame = new PanelContainer();
		frame.AddThemeStyleboxOverride("panel", LabLook.Box(LabLook.Field, 6, 6, marginX: 8, marginY: 6));
		var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		rows.AddThemeConstantOverride("separation", 4);
		frame.AddChild(rows);

		var head = new HBoxContainer();
		head.AddThemeConstantOverride("separation", 6);
		rows.AddChild(head);
		LineEdit name = InspectorLook.Field(vname, "rye");
		name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		name.TextChanged += text => Change($"renamed a variety of {NameOf(goodId)} " + (text.Trim().Length > 0 ? text.Trim() : vid),
			() => { if (VarietyOf(goodId, vid) is { } live) live.Name = text; },
			merge: "variety.name:" + goodId + ":" + vid, keepInspector: true);
		head.AddChild(name);
		head.AddChild(InspectorLook.Small("remove", "Take this variety off the good.", () =>
			Change($"took the {VarietyOf(goodId, vid)?.Name ?? vid} variety off {NameOf(goodId)}", () =>
			{
				if (Palette.Find(goodId) is not { } live) return;
				live.Varieties?.RemoveAll(v => v.Id == vid);
				if (live.Varieties is { Count: 0 }) live.Varieties = null;
			})));
		rows.AddChild(LabLook.Text(vid, 12, LabLook.Faint));

		HFlowContainer tags = InspectorLook.Flow();
		rows.AddChild(tags);
		foreach (string tag in variety.Tags)
		{
			string held = tag;
			tags.AddChild(InspectorLook.Chip(held, InspectorLook.TagFill(Palette, held), null,
				InspectorLook.Lines(held, Palette.NoteFor(held), InspectorLook.RoleLine(Palette, held)),
				() => Change($"took {held} off the {VarietyOf(goodId, vid)?.Name ?? vid} variety", () => VarietyOf(goodId, vid)?.Tags.Remove(held)),
				InspectorLook.TagInk(Palette, held)));
		}
		tags.AddChild(InspectorLook.Small("+ tag", "What sets this variety apart. A variety tag rides from here downstream.",
			() => AskTag($"Tag the {(vname.Length > 0 ? vname : vid)} of {NameOf(goodId)}…", tag => VarietyTagged(goodId, vid, tag))));
		return frame;
	}

	/// <summary>One authored variety of a good, looked up afresh, or null if either has gone.</summary>
	private Variety? VarietyOf(string goodId, string varietyId) =>
		Palette.Find(goodId)?.VarietyList.FirstOrDefault(v => v.Id == varietyId);

	/// <summary>
	/// A new authored variety of a good, named as typed and with an id slugged from that name,
	/// made unique among that good's own varieties. Its tags are for the designer to add.
	/// </summary>
	private void AddVariety(string goodId, LineEdit field)
	{
		string name = field.Text.Trim();
		if (name.Length == 0)
		{
			Say("A variety wants a name: rye, wheat, arsenic.");
			return;
		}
		if (Palette.Find(goodId) is not { } good) return;
		string vid = EconomyEdit.Free(EconomyEdit.Slug(name), taken => good.VarietyList.Any(v => v.Id == taken));
		Change($"{NameOf(goodId)} has a variety, {name}", () =>
		{
			if (Palette.Find(goodId) is not { } live) return;
			live.Varieties ??= new List<Variety>();
			live.Varieties.Add(new Variety { Id = vid, Name = name });
		});
	}

	/// <summary>
	/// Gives a good, or one of its authored varieties, a tag meant to travel. A tag only travels
	/// if its namespace is a variety namespace, so one that plays no part yet is made one in the
	/// same step, and the status line says so: it is a decision about every tag in that namespace.
	/// </summary>
	private void VarietyTagged(string goodId, string? varietyId, string tag)
	{
		if (tag.Length == 0) return;
		string what = varietyId == null ? NameOf(goodId) : "the " + (VarietyOf(goodId, varietyId)?.Name ?? varietyId) + " variety";
		List<string>? tags = varietyId == null ? Palette.Find(goodId)?.Tags : VarietyOf(goodId, varietyId)?.Tags;
		if (tags == null) return;
		if (tags.Contains(tag))
		{
			Say($"{what} carries {tag} already.");
			return;
		}

		string space = Palette.NamespaceOf(tag);
		bool teach = space.Length > 0 && !Palette.IsVariety(tag);
		Change($"tagged {what} {tag}", () =>
		{
			if (teach) EconomyEdit.EnsureNamespace(Palette, space).Role = TagNamespace.Variety;
			List<string>? live = varietyId == null ? Palette.Find(goodId)?.Tags : VarietyOf(goodId, varietyId)?.Tags;
			live?.Add(tag);
		});
		if (teach) Say($"Tagged {what} {tag}. {space}: is a variety namespace now, so every tag in it rides from inputs to outputs.");
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

	/// <summary>
	/// One row of the sprites block: the sprite as it stands, which of the two it is, and the two
	/// ways to change it — a cell of this web's sheet, or a PNG of the good's own.
	/// </summary>
	private Control SpriteRow(string id, bool sign)
	{
		Good? good = Palette.Find(id);
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		row.AddChild(LabLook.Sprite(Sprites.Get(sign ? good?.Sign : good?.Icon), 32));
		Label what = LabLook.Text(sign ? "Sign" : "Icon", 13, LabLook.Dim);
		what.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		row.AddChild(what);
		Button sheet = InspectorLook.Small("from the sheet…", sign
			? "Pick a cell of sprites/signs.png. The sign's reading and its parts are kept."
			: "Pick a cell of sprites/icons.png.", () => PickSprite(id, sign));
		sheet.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(sheet);
		Button png = InspectorLook.Small("from a PNG…",
			$"Copy a 16 px PNG into sprites/custom/ and use it as the {(sign ? "sign" : "icon")}.", () => AskPng(id, sign));
		png.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(png);
		return row;
	}

	private void InspectorTagGood(string id, string tag)
	{
		if (tag.Length == 0) return;
		if (Palette.Find(id) is not { } good) return;
		if (good.Tags.Contains(tag))
		{
			Say($"{good.Name} carries {tag} already.");
			return;
		}
		Change($"tagged {good.Name} {tag}", () => Palette.Find(id)?.Tags.Add(tag));
	}

	private void PickSprite(string id, bool sign)
	{
		if (Palette.Find(id) is not { } good) return;
		string atlas = sign ? "signs" : "icons";
		SpriteRef? now = sign ? good.Sign : good.Icon;
		int current = now != null && now.Atlas == atlas ? now.Index ?? -1 : -1;
		_inspSheet.Ask($"{(sign ? "The sign" : "The icon")} for {good.Name}", Sprites.CellCount(atlas),
			cell => Sprites.Cell(atlas, cell), current, GetViewport().GetMousePosition(), cell => SetSprite(id, sign, cell));
	}

	private void SetSprite(string id, bool sign, int cell) =>
		Change($"gave {NameOf(id)} a new {(sign ? "sign" : "icon")}", () =>
		{
			if (Palette.Find(id) is not { } good) return;
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

	private void AskPng(string id, bool sign)
	{
		_inspPngGood = id;
		_inspPngSign = sign;
		_inspPng.Title = sign ? "A PNG for the sign" : "A PNG for the icon";
		_inspPng.PopupCentered(new Vector2I(900, 620));
	}

	/// <summary>
	/// The chosen PNG is copied into <c>sprites/custom/</c> under the good's id — with
	/// <c>.sign</c> before the extension when it is the sign — and becomes that sprite. The copy
	/// is a file and no undo takes it back; the good pointing at it is a change like any other,
	/// so an undo leaves an unused PNG behind and nothing worse.
	/// </summary>
	private void ImportSprite(string path)
	{
		string id = _inspPngGood;
		bool sign = _inspPngSign;
		if (Palette.Find(id) == null) return;
		string relative = "sprites/custom/" + id + (sign ? ".sign" : "") + ".png";
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
		Change($"gave {NameOf(id)} {(sign ? "a sign" : "an icon")} of its own", () =>
		{
			if (Palette.Find(id) is not { } good) return;
			if (!sign)
			{
				good.Icon = SpriteRef.Png(relative);
				return;
			}
			// A sign's reading, its source and its parts ride in Extra; only where the picture comes from changes.
			if (good.Sign == null) good.Sign = SpriteRef.Png(relative);
			else
			{
				good.Sign.File = relative;
				good.Sign.Atlas = null;
				good.Sign.Index = null;
			}
		});
	}

	/// <summary>
	/// Offers to delete a good from this web's palette. Every web keeps its own palette inside
	/// its own file, so there is nothing to read elsewhere and nothing elsewhere to break: what
	/// goes is this web's copy, and whatever here referred to it.
	/// </summary>
	private void AskBinGood(string id)
	{
		_inspBinGood = id;
		_inspBin.DialogText = $"Delete {NameOf(id)} from {Web.Name}?\n\nIt leaves this web altogether: the canvas, this web's palette, any recipe that made nothing else, and its name in every slot that asked for it. Other webs have their own palettes and are untouched. Undo brings it back.";
		_inspBin.PopupCentered();
	}

	private void BinGood()
	{
		string id = _inspBinGood;
		if (Palette.Find(id) == null) return;
		Change($"deleted {NameOf(id)} from the palette", () => EconomyEdit.DeleteGood(Web, id));
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
			Change("labelled the recipe " + (text.Trim().Length > 0 ? text.Trim() : rid), () => { if (Web.Recipe(rid) is { } live) live.Name = text; },
				merge: "recipe.name:" + rid, keepInspector: true);
			if (Web.Recipe(rid) is { } named) title.Text = Analysis.TitleOf(named);
		};
		rows.AddChild(label);

		rows.AddChild(InspectorLook.Caption("Note"));
		TextEdit note = InspectorLook.Paragraph(recipe.Note, 3);
		note.TextChanged += () => Change("noted " + NameOf(rid), () => { if (Web.Recipe(rid) is { } live) live.Note = note.Text; },
			merge: "recipe.note:" + rid, keepInspector: true);
		rows.AddChild(note);

		Label element = InspectorLook.Caption("Element");
		element.MouseFilter = MouseFilterEnum.Stop;
		element.TooltipText = "What kind of craft this is: violence or patience, putting together or taking apart. A classification by feel; nothing reads it yet.";
		rows.AddChild(element);
		rows.AddChild(ElementRow(rid, recipe.Element));

		Label site = InspectorLook.Caption("Site");
		site.MouseFilter = MouseFilterEnum.Stop;
		site.TooltipText = "The ground or the place the work stands on. Not a slot: nothing is hauled and nothing is used up.";
		rows.AddChild(site);
		rows.AddChild(SiteRow(rid, recipe.SiteList));
		rows.AddChild(InspectorLook.Note(
			"Where the work has to stand: tags of the ground or the place, any one of which will do. Nothing is hauled.",
			LabLook.Faint, 12));

		rows.AddChild(InspectorLook.Caption("Formula"));
		rows.AddChild(Formula(recipe));

		InspectorLook.Section(rows, "Inputs");
		if (recipe.Inputs.Count == 0)
			rows.AddChild(recipe.SiteList.Count > 0
				? InspectorLook.Note("No inputs: an extraction, dug where it stands.", LabLook.Dim, 12)
				: InspectorLook.Note("It takes nothing.", LabLook.Warning, 12));
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
				Change($"{NameOf(rid)} no longer makes {NameOf(made)}", () =>
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

	// ---- a recipe's site -------------------------------------------------------

	/// <summary>
	/// Where the work stands: the tags of the ground or the place, any one of which will do. Not a
	/// slot — nothing is hauled and nothing is used up — so it is written on the recipe, and peat
	/// cut on murkearth takes no soil in at all.
	/// </summary>
	private Control SiteRow(string rid, IReadOnlyList<string> sites)
	{
		HFlowContainer flow = InspectorLook.Flow();
		if (sites.Count == 0) flow.AddChild(LabLook.Text("anywhere", 12, LabLook.Faint));
		foreach (string tag in sites)
		{
			string held = tag;
			flow.AddChild(TagChip(held, "the work stands on it; nothing is hauled",
				() => Change($"took the site off {NameOf(rid)}: {held}", () =>
				{
					if (Web.Recipe(rid) is not { } live) return;
					live.Site?.Remove(held);
					if (live.Site is { Count: 0 }) live.Site = null;
				})));
		}
		flow.AddChild(InspectorLook.Small("+ site…", "A tag of the ground or the place this work has to stand on.",
			() => AskSiteTag("Where does the work have to stand?", tag => Sited(rid, tag))));
		return flow;
	}

	/// <summary>A tag added to where a recipe must stand; the list is made on the first one.</summary>
	private void Sited(string rid, string tag)
	{
		if (tag.Length == 0) return;
		if (Web.Recipe(rid) is not { } recipe) return;
		if (recipe.SiteList.Contains(tag))
		{
			Say($"{NameOf(rid)} stands on {tag} already.");
			return;
		}
		Change($"{NameOf(rid)} stands on {tag}", () =>
		{
			if (Web.Recipe(rid) is not { } live) return;
			live.Site ??= new List<string>();
			if (!live.Site.Contains(tag)) live.Site.Add(tag);
		});
	}

	/// <summary>
	/// Asks for a tag of the ground or the place: the variety namespaces, which is where the soils
	/// are, and <c>site:</c>, which is where everything else is. A tag typed in that matches nothing
	/// is taken as a new one, as the other pickers do.
	/// </summary>
	private void AskSiteTag(string prompt, Action<string> then)
	{
		IEnumerable<(string, string, Texture2D?)> items = Palette.TagsInUse()
			.Where(t => Palette.IsVariety(t.Tag) || Palette.NamespaceOf(t.Tag) == SiteSpace)
			.Select(t => (t.Tag, $"#{t.Tag}   ({t.Count})", Sprites.Get(Palette.SignOf(t.Tag))));
		_picker.Ask(prompt, items, GetViewport().GetMousePosition(), then, typed => then(TidyTag(typed)));
	}

	/// <summary>
	/// A tag as a chip that wears its own colour: its symbol tinted where the palette gives it one,
	/// then the tag itself, then a × that takes it off. A tag with no colour falls back to the fill
	/// its namespace's part in the web earns it.
	/// </summary>
	private Control TagChip(string tag, string what, Action remove)
	{
		bool tinted = SpriteBank.TryColour(Palette.ColourOf(tag), out Color hue);
		Texture2D? symbol = Sprites.Get(Palette.SignOf(tag));
		string tip = InspectorLook.Lines("#" + tag, Palette.NoteFor(tag), what);

		var chip = new PanelContainer { TooltipText = tip, MouseFilter = MouseFilterEnum.Stop };
		chip.AddThemeStyleboxOverride("panel", LabLook.Box(
			tinted ? hue.Darkened(0.68f) : InspectorLook.TagFill(Palette, tag), 9, 9, marginX: 6, marginY: 2));
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 4);
		chip.AddChild(row);
		if (symbol != null)
		{
			TextureRect sprite = LabLook.Sprite(symbol, 16);
			if (tinted) sprite.Modulate = hue;
			row.AddChild(sprite);
		}
		row.AddChild(LabLook.Text(tag, 12, tinted ? hue : InspectorLook.TagInk(Palette, tag)));
		row.AddChild(InspectorLook.Cross("Take it off", remove));
		return chip;
	}

	// ---- a recipe's element and its formula ------------------------------------

	/// <summary>
	/// The recipe's elemental association: the five, and none. The one in force wears the accent,
	/// and a click on it is a change like any other. The icons sit on a sheet of the system's own,
	/// so each element stands as its name until that sheet is drawn.
	/// </summary>
	private Control ElementRow(string rid, string? now)
	{
		HFlowContainer flow = InspectorLook.Flow();
		foreach (Element element in Element.All)
		{
			string eid = element.Id;
			bool on = string.Equals(now, eid, StringComparison.Ordinal);
			flow.AddChild(InspectorLook.Toggle(element.Name, Sprites.Element(eid), 24, on, $"{element.Name}: {element.Gloss}",
				() => { if (!on) SetElement(rid, eid); }));
		}
		flow.AddChild(InspectorLook.Toggle("none", null, 24, now == null, "No elemental association.",
			() => { if (now != null) SetElement(rid, null); }));
		return flow;
	}

	private void SetElement(string rid, string? id)
	{
		if (Web.Recipe(rid) is not { } recipe) return;
		string title = Analysis.TitleOf(recipe);
		Element? element = Element.Find(id);
		Change(element == null ? $"{title} has no element now" : $"{title} is now of {element.Name.ToLowerInvariant()}",
			() => { if (Web.Recipe(rid) is { } live) live.Element = element?.Id; });
	}

	/// <summary>
	/// The recipe written out in signs: its element, then what fills each slot, then what comes
	/// out. The alternatives of one slot are parted by a slash, an optional slot stands in
	/// brackets, and a slot that takes a tag is written as the tag, there being no one sign for
	/// it. Nothing here is editable: it is a reading, and the far goal is a chain of recipes that
	/// reads as a formula.
	/// </summary>
	private Control Formula(Recipe recipe)
	{
		HFlowContainer flow = InspectorLook.Flow();
		if (Element.Find(recipe.Element) is { } element)
			flow.AddChild(InspectorLook.Glyph(Sprites.Element(element.Id), 24, element.Name, $"{element.Name}: {element.Gloss}"));

		// The site leads, straight after the element: the work stands somewhere before it takes anything.
		if (recipe.SiteList.Count > 0)
		{
			flow.AddChild(InspectorLook.Mark("on", "where the work has to stand; nothing is hauled", LabLook.Dim));
			for (int s = 0; s < recipe.SiteList.Count; s++)
			{
				if (s > 0) flow.AddChild(InspectorLook.Mark("/", "any one of them"));
				flow.AddChild(AcceptorGlyph(Acceptor.ForTag(recipe.SiteList[s]), site: true));
			}
		}

		for (int i = 0; i < recipe.Inputs.Count; i++)
		{
			if (i > 0) flow.AddChild(InspectorLook.Mark("+"));
			RecipeInput slot = recipe.Inputs[i];
			if (slot.Optional) flow.AddChild(InspectorLook.Mark("(", "the recipe runs without it"));
			if (slot.Accepts.Count == 0) flow.AddChild(InspectorLook.Mark("?", "the slot accepts nothing", LabLook.Error));
			for (int a = 0; a < slot.Accepts.Count; a++)
			{
				if (a > 0) flow.AddChild(InspectorLook.Mark("/", "any one of them"));
				flow.AddChild(AcceptorGlyph(slot.Accepts[a]));
			}
			if (slot.Optional) flow.AddChild(InspectorLook.Mark(")", "the recipe runs without it"));
		}

		flow.AddChild(InspectorLook.Mark("→", "makes", LabLook.Dim));
		if (recipe.Outputs.Count == 0) flow.AddChild(InspectorLook.Mark("?", "it makes nothing", LabLook.Error));
		for (int o = 0; o < recipe.Outputs.Count; o++)
		{
			if (o > 0) flow.AddChild(InspectorLook.Mark("+"));
			flow.AddChild(AcceptorGlyph(recipe.Outputs[o].Good));
		}
		return flow;
	}

	/// <summary>
	/// One term of a formula: a good's sign, or its icon where it has no sign yet, or its name
	/// where it has neither. A tag shows the symbol its palette gives it — its own, or failing that
	/// its namespace's — tinted with its colour; a tag with no symbol drawn yet stands as the tag
	/// itself, in its colour where it has one and dim where it has none.
	/// </summary>
	private Control AcceptorGlyph(string acceptor, bool site = false)
	{
		if (Acceptor.IsTag(acceptor))
		{
			string tag = Acceptor.TagOf(acceptor);
			string tip = InspectorLook.Lines("#" + tag, Palette.NoteFor(tag),
				site ? "the work stands on it; nothing is hauled" : "anything in this web carrying it");
			bool tinted = SpriteBank.TryColour(Palette.ColourOf(tag), out Color hue);
			if (Sprites.Get(Palette.SignOf(tag)) is { } symbol)
			{
				Control glyph = InspectorLook.Glyph(symbol, 24, "#" + LabLook.Short(tag), tip);
				if (tinted) glyph.Modulate = hue;
				return glyph;
			}
			return InspectorLook.Mark("#" + LabLook.Short(tag), tip, tinted ? hue : LabLook.Dim);
		}
		Good? good = Palette.Find(acceptor);
		if (good == null) return InspectorLook.Mark(acceptor, $"{acceptor} is not in this web's palette.", LabLook.Error);
		return InspectorLook.Glyph(Sprites.Get(good.Sign) ?? Sprites.Get(good.Icon), 24, good.Name, good.Name);
	}

	/// <summary>
	/// One input slot in a frame: what fills it, whether the recipe runs without it, whether the
	/// variety of what fills it passes on to the output, what it grants the output of its own,
	/// what each tag of it admits from this web as things stand, and — when it passes — what each
	/// filler would pass.
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
		head.AddChild(InspectorLook.Small("remove", "Take this slot off the recipe.", () =>
			Change($"took input {port + 1} off {NameOf(rid)}", () =>
			{
				if (Web.Recipe(rid) is { } live && port < live.Inputs.Count) live.Inputs.RemoveAt(port);
			})));

		// Two switches of their own row: the dock is too narrow to carry them beside the caption.
		string named = SlotName(rid, port);
		var switches = new HBoxContainer();
		switches.AddThemeConstantOverride("separation", 10);
		rows.AddChild(switches);
		switches.AddChild(SlotSwitch("optional", slot.Optional, LabLook.Dim,
			"An upgrade or a variant: the recipe runs without it, and it does not count towards depth.",
			on => Change($"{named} of {NameOf(rid)} is {(on ? "optional" : "required")}", () =>
			{
				if (Web.Recipe(rid) is { } live && port < live.Inputs.Count) live.Inputs[port].Optional = on;
			})));
		switches.AddChild(SlotSwitch("passes variety", slot.Passes, slot.Passes ? LabLook.VarietyTag : LabLook.Dim,
			"What fills this slot marks what comes out: its variety tags are stamped on the output, and on whatever that goes into through other passing slots.",
			on => Change($"{named} of {NameOf(rid)} {(on ? "now passes variety on" : "no longer passes variety on")}", () =>
			{
				if (Web.Recipe(rid) is { } live && port < live.Inputs.Count) live.Inputs[port].Passes = on;
			})));

		rows.AddChild(AcceptorChips(slot.Accepts, rid, port));
		if (slot.Accepts.Count == 0) rows.AddChild(InspectorLook.Note("accepts nothing", LabLook.Error, 12));
		else if (slot.Accepts.Count > 1) rows.AddChild(InspectorLook.Note("any one of these", LabLook.Faint, 12));

		// A variety is meant to ride through a slot, not to stand at its door; the analysis says so too.
		if (slot.Accepts.Where(Acceptor.IsTag).Select(Acceptor.TagOf).Any(Palette.IsVariety))
			rows.AddChild(InspectorLook.Note(
				"A variety tag gating a slot: varieties are meant to ride, not to gate. Ask for a site, a core tag or a good.",
				LabLook.Warning, 12));

		foreach (string acceptor in slot.Accepts.Where(Acceptor.IsTag)) rows.AddChild(AdmitsLine(rid, port, acceptor));
		if (slot.Passes)
			foreach (string filler in Analysis.FillersOf(rid, port).Distinct())
				rows.AddChild(FillerLine(filler));
		rows.AddChild(SlotGrants(rid, port, slot.GrantList));
		rows.AddChild(AcceptorButtons(rid, port));
		return frame;
	}

	/// <summary>
	/// What a slot stamps on the output of its own accord, whatever fills it: the tags as chips
	/// with a × each, and a way to add another. Unlike a passed variety this belongs to the
	/// combination rather than to the ingredient, so it is written on the slot and not on a good.
	/// </summary>
	private Control SlotGrants(string rid, int port, IReadOnlyList<string> grants)
	{
		HFlowContainer flow = InspectorLook.Flow();
		Label caption = LabLook.Text("grants the output", 12, grants.Count > 0 ? LabLook.VarietyTag : LabLook.Dim);
		caption.MouseFilter = MouseFilterEnum.Stop;
		caption.TooltipText = "For when the effect belongs to the combination and not to the ingredient: the slot stamps these on what the recipe makes, whatever fills it.";
		caption.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		flow.AddChild(caption);
		foreach (string tag in grants)
		{
			string held = tag;
			flow.AddChild(InspectorLook.Chip(held, InspectorLook.TagFill(Palette, held), null,
				InspectorLook.Lines(held, Palette.NoteFor(held), InspectorLook.RoleLine(Palette, held)),
				() => Ungrant(rid, port, held), InspectorLook.TagInk(Palette, held)));
		}
		flow.AddChild(InspectorLook.Small("+ grant…", "A tag this slot stamps on the output whenever it is filled, whatever fills it.",
			() => AskTag("Whenever this slot is filled the output gets…", tag => Granted(rid, port, tag))));
		return flow;
	}

	/// <summary>
	/// A tag a slot grants. Granted tags travel like any other variety tag, and only a tag of a
	/// variety namespace travels at all, so a namespace that plays no part yet is made a variety
	/// one in the same step and the status line says so: it decides for every tag in it.
	/// </summary>
	private void Granted(string rid, int port, string tag)
	{
		if (tag.Length == 0) return;
		if (Web.Recipe(rid) is not { } recipe || port >= recipe.Inputs.Count) return;
		string named = SlotName(rid, port);
		if (recipe.Inputs[port].GrantList.Contains(tag))
		{
			Say($"{named} of {NameOf(rid)} grants {tag} already.");
			return;
		}

		string space = Palette.NamespaceOf(tag);
		bool teach = space.Length > 0 && !Palette.IsVariety(tag);
		Change($"{named} of {NameOf(rid)} grants {tag}", () =>
		{
			if (teach) EconomyEdit.EnsureNamespace(Palette, space).Role = TagNamespace.Variety;
			if (Web.Recipe(rid) is not { } live || port >= live.Inputs.Count) return;
			RecipeInput filled = live.Inputs[port];
			filled.Grants ??= new List<string>();
			if (!filled.Grants.Contains(tag)) filled.Grants.Add(tag);
		});
		if (teach) Say($"{named} of {NameOf(rid)} grants {tag}. {space}: is a variety namespace now, so every tag in it rides from inputs to outputs.");
	}

	/// <summary>A granted tag off a slot again; the list goes back to nothing when the last one leaves.</summary>
	private void Ungrant(string rid, int port, string tag) =>
		Change($"{SlotName(rid, port)} of {NameOf(rid)} no longer grants {tag}", () =>
		{
			if (Web.Recipe(rid) is not { } live || port >= live.Inputs.Count) return;
			RecipeInput slot = live.Inputs[port];
			slot.Grants?.Remove(tag);
			if (slot.Grants is { Count: 0 }) slot.Grants = null;
		});

	/// <summary>One of a slot's two switches: small, quiet, and one change to undo when it is toggled.</summary>
	private static CheckBox SlotSwitch(string text, bool on, Color ink, string tip, Action<bool> toggled)
	{
		var box = new CheckBox { Text = text, ButtonPressed = on, FocusMode = FocusModeEnum.None, TooltipText = tip };
		box.AddThemeFontSizeOverride("font_size", 12);
		box.AddThemeColorOverride("font_color", ink);
		box.Toggled += pressed => toggled(pressed);
		return box;
	}

	/// <summary>A slot in a sentence: "the heart slot" from the first thing it takes, or its number.</summary>
	private string SlotName(string rid, int port)
	{
		if (Web.Recipe(rid) is not { } recipe || port >= recipe.Inputs.Count) return $"input {port + 1}";
		List<string> accepts = recipe.Inputs[port].Accepts;
		if (accepts.Count == 0) return $"input {port + 1}";
		string first = accepts[0];
		return "the " + (Acceptor.IsTag(first) ? LabLook.Short(Acceptor.TagOf(first)) : NameOf(first).ToLowerInvariant()) + " slot";
	}

	/// <summary>
	/// What one good would pass into a slot that passes variety on: the variety tags it carries,
	/// or how many varieties of it this web can make. A good with none passes nothing, which is
	/// worth saying and worth mending, so that line comes with a way to give it a variety tag.
	/// </summary>
	private Control FillerLine(string goodId)
	{
		VarietySet varieties = Analysis.VarietiesOf(goodId);
		if (!varieties.IsPlain)
		{
			string passed = varieties.Sets.Count == 1 && !varieties.Capped
				? string.Join(", ", varieties.Sets[0])
				: $"any of {(varieties.Capped ? "about " : "")}{varieties.Count} varieties";
			return InspectorLook.Note($"{NameOf(goodId)} passes {passed}", LabLook.Dim, 12);
		}

		var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		row.AddThemeConstantOverride("separation", 4);
		row.AddChild(InspectorLook.Note($"{NameOf(goodId)} carries no variety tag, so it passes nothing", LabLook.Warning, 12));
		row.AddChild(InspectorLook.Small("+ variety tag…",
			"A tag that travels: what fills this slot then marks what comes out. A namespace that is not a variety one yet becomes one.",
			() => AskTag($"A variety tag for {NameOf(goodId)}…", tag => VarietyTagged(goodId, null, tag))));
		return row;
	}

	/// <summary>
	/// The chips of one accept list, a good's with its icon and a tag's in the colour of the part
	/// its namespace plays — amber for a core tag, violet for a variety one — each with a ×.
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
				string meaning = Palette.NoteFor(tag);
				flow.AddChild(InspectorLook.Chip("#" + tag, InspectorLook.TagFill(Palette, tag), null,
					InspectorLook.Lines(held, meaning.Length > 0 ? meaning : "Anything in this web carrying it fits.",
						InspectorLook.RoleLine(Palette, tag)),
					() => DropAcceptor(nodeKey, port, held), InspectorLook.TagInk(Palette, tag)));
				continue;
			}
			Good? good = Palette.Find(held);
			flow.AddChild(InspectorLook.Chip(good?.Name ?? held, LabLook.Body.Lightened(0.12f), IconOf(held),
				good == null ? $"{held} is not in the palette." : good.Note.Length > 0 ? $"{good.Name}\n{good.Note}" : good.Name,
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
		Change($"{NameOf(nodeKey)} takes {NameOf(goodId)}", () =>
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
		Change($"{NameOf(nodeKey)} no longer takes {what}", () =>
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
		Change($"{NameOf(rid)} makes {NameOf(goodId)}", () =>
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
			Change("renamed the consumer " + (text.Trim().Length > 0 ? text.Trim() : cid), () => { if (Web.Consumer(cid) is { } live) live.Name = text; },
				merge: "consumer.name:" + cid, keepInspector: true);
		};
		rows.AddChild(name);

		rows.AddChild(InspectorLook.Caption("Note"));
		TextEdit note = InspectorLook.Paragraph(consumer.Note, 3);
		note.TextChanged += () => Change("noted the consumer " + NameOf(cid), () => { if (Web.Consumer(cid) is { } live) live.Note = note.Text; },
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
		name.TextChanged += text => Change("renamed the web " + (text.Trim().Length > 0 ? text.Trim() : Web.Id), () => Web.Name = text, merge: "web.name:" + Web.Id, keepInspector: true);
		rows.AddChild(name);

		rows.AddChild(InspectorLook.Caption("Note"));
		TextEdit note = InspectorLook.Paragraph(Web.Note, 4);
		note.TextChanged += () => Change("noted the web", () => Web.Note = note.Text, merge: "web.note:" + Web.Id, keepInspector: true);
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
		int varied = Web.Goods.Count(goodId => !Analysis.VarietiesOf(goodId).IsPlain);
		rows.AddChild(InspectorLook.Note(varied == 1 ? "1 good comes in varieties." : $"{varied} goods come in varieties.", LabLook.Dim, 12));

		string? deepest = Web.Goods.Where(g => Palette.Find(g) != null)
			.OrderByDescending(Analysis.Depth).ThenBy(g => g, StringComparer.Ordinal).FirstOrDefault();
		if (deepest != null)
			rows.AddChild(InspectorJump($"deepest: {NameOf(deepest)}, {Analysis.Depth(deepest)} steps from the ground",
				deepest, LabLook.Dim, IconOf(deepest)));

		InspectorLook.Section(rows, "Elements");
		var elements = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (Recipe recipe in Web.Recipes)
			if (Element.Find(recipe.Element) is { } of) elements[of.Id] = elements.GetValueOrDefault(of.Id) + 1;
		foreach (Element element in Element.All)
		{
			int count = elements.GetValueOrDefault(element.Id);
			rows.AddChild(ElementLine(element,
				$"{element.Name}: {(count == 0 ? "none" : count == 1 ? "1 recipe" : count + " recipes")}",
				count > 0 ? LabLook.Ink : LabLook.Faint));
		}
		int elementless = Web.Recipes.Count - elements.Values.Sum();
		if (elementless > 0)
			rows.AddChild(InspectorLook.Note(elementless == 1 ? "1 recipe has none" : $"{elementless} recipes have none", LabLook.Faint, 12));
		rows.AddChild(InspectorLook.Note(ElementBalance(elements), LabLook.Dim, 12));

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
		rows.AddChild(InspectorLook.Note("» on a slot: what fills it passes its variety on", LabLook.Dim, 12));
		rows.AddChild(InspectorLook.Note("+ on a slot: it grants the output a tag of its own", LabLook.Dim, 12));
		rows.AddChild(InspectorLook.Swatch("a variety tag", LabLook.VarietyTag));
		rows.AddChild(InspectorLook.Swatch("a core tag", LabLook.CoreTag));
		foreach (Element element in Element.All) rows.AddChild(ElementLine(element, $"{element.Name}: {element.Gloss}", LabLook.Dim));
	}

	/// <summary>An element's icon and a line about it; the icon is a blank square until the sheet is drawn.</summary>
	private Control ElementLine(Element element, string text, Color colour)
	{
		var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		row.AddThemeConstantOverride("separation", 6);
		row.AddChild(LabLook.Sprite(Sprites.Element(element.Id), 16));
		row.AddChild(InspectorLook.Note(text, colour, 12));
		return row;
	}

	/// <summary>
	/// Whether the four main elements share the recipes that have one of them evenly enough. A
	/// quarter each would be 25%; under 15 or over 35 is worth saying, the classification being
	/// by feel and a web that is all fire probably not having been thought about.
	/// </summary>
	private static string ElementBalance(IReadOnlyDictionary<string, int> counts)
	{
		Element[] four = { Element.Fire, Element.Wind, Element.Water, Element.Earth };
		int total = four.Sum(element => counts.GetValueOrDefault(element.Id));
		if (total == 0) return "No recipe is of fire, wind, water or earth yet.";
		var said = new List<string>();
		foreach (Element element in four)
		{
			int share = (int)Math.Round(counts.GetValueOrDefault(element.Id) * 100.0 / total);
			if (share < 15) said.Add($"{element.Name.ToLowerInvariant()} is thin at {share}%");
			else if (share > 35) said.Add($"{element.Name.ToLowerInvariant()} is heavy at {share}%");
		}
		return said.Count == 0 ? "The four are in rough balance." : string.Join(", ", said) + ".";
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
		rows.AddChild(InspectorAct("Remove them from this web", "Goods stay in this web's palette; recipes and consumers are deleted.",
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
