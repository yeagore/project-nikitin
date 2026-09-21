using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// The left dock: a catalogue of goods to drag onto the canvas, and a tab of tags. Both tabs
/// read the model afresh on every <see cref="RefreshPalette"/>, but a tab only rebuilds its
/// tree when a signature of what it shows has actually changed, so a keystroke anywhere else
/// in the lab (the inspector included) costs this dock almost nothing.
/// </summary>
public partial class EconomyLab
{
	// ---- goods tab ---------------------------------------------------------------

	private LineEdit _palFilter = null!;
	private OptionButton _palShow = null!, _palGroup = null!;
	private PaletteTree _palTree = null!;
	private CheckBox _palChainCheck = null!;
	private OptionButton _palChainPick = null!;
	private Label _palCount = null!;
	private string _palGoodsSig = "";
	private readonly HashSet<string> _palGoodFold = new(StringComparer.Ordinal);
	private readonly Dictionary<TreeItem, string> _palGoodSections = new();

	// ---- tags tab ------------------------------------------------------------------

	private LineEdit _palTagFilter = null!;
	private Tree _palTagTree = null!;
	private Control _palTagDetail = null!;
	private Label _palTagCaption = null!, _palTagCarriers = null!;
	private TextEdit _palTagNote = null!;
	private string? _palSelectedTag, _palPendingTagSelect;
	private string _palTagsSig = "";
	private readonly HashSet<string> _palTagFold = new(StringComparer.Ordinal);
	private readonly Dictionary<TreeItem, string> _palTagSections = new();
	private readonly Dictionary<string, TreeItem> _palTagItems = new(StringComparer.Ordinal);

	private ConfirmationDialog _palNewTagDialog = null!, _palRenameTagDialog = null!, _palDeleteTagDialog = null!;
	private LineEdit _palNewTagEdit = null!, _palRenameTagEdit = null!;

	// ---- build ---------------------------------------------------------------------

	private Control BuildPalette()
	{
		// One font size for the whole dock, the inspector's, so the two sides of the canvas read alike.
		var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill, Theme = new Theme { DefaultFontSize = 13 } };
		tabs.AddChild(BuildGoodsTab());
		tabs.SetTabTitle(0, "Goods");
		tabs.AddChild(BuildTagsTab());
		tabs.SetTabTitle(1, "Tags");
		BuildPaletteDialogs();

		// A standing hook for looking at the Tags tab headless: `-- tags` opens it on a
		// chosen tag, which is also handy for checking a specific tag by hand.
		if (OS.GetCmdlineUserArgs().Contains("tags"))
		{
			tabs.CurrentTab = 1;
			_palPendingTagSelect = "kind:golem-heart";
		}
		return tabs;
	}

	private Control BuildGoodsTab()
	{
		var col = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		col.AddThemeConstantOverride("separation", 6);

		_palFilter = new LineEdit { PlaceholderText = "filter: a name, or #tag", ClearButtonEnabled = true };
		_palFilter.TextChanged += _ => RebuildGoodsTree();
		col.AddChild(_palFilter);

		var modes = new HBoxContainer();
		modes.AddThemeConstantOverride("separation", 6);
		_palShow = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "Which goods of the catalogue to list." };
		_palShow.AddItem("All");
		_palShow.AddItem("Not in this web");
		_palShow.AddItem("In this web");
		_palShow.ItemSelected += _ => RebuildGoodsTree();
		modes.AddChild(_palShow);
		_palGroup = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "How to shelve the list." };
		_palGroup.AddItem("By stage");
		_palGroup.AddItem("By shelf");
		_palGroup.AddItem("A–Z");
		_palGroup.ItemSelected += _ => RebuildGoodsTree();
		modes.AddChild(_palGroup);
		col.AddChild(modes);

		_palTree = new PaletteTree
		{
			HideRoot = true,
			Columns = 1,
			SelectMode = Tree.SelectModeEnum.Multi,
			AllowRmbSelect = true,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
		};
		_palTree.Lookup = id =>
		{
			Good? good = Catalogue.Find(id);
			return (good?.Name ?? id, good == null ? null : Sprites.Get(good.Icon));
		};
		_palTree.ItemActivated += ActivateGood;
		_palTree.ItemMouseSelected += (position, mouseButton) =>
		{
			if (mouseButton == (long)MouseButton.Right) RightClickGood(position);
		};
		col.AddChild(_palTree);

		var chainRow = new HBoxContainer();
		chainRow.AddThemeConstantOverride("separation", 6);
		_palChainCheck = new CheckBox
		{
			Text = "with its chain from",
			TooltipText = "Bring each good with its recipes and everything upstream of them, as that web has them.",
		};
		_palChainCheck.Toggled += on => _palChainPick.Disabled = !on;
		chainRow.AddChild(_palChainCheck);
		_palChainPick = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, Disabled = true };
		chainRow.AddChild(_palChainPick);
		col.AddChild(chainRow);

		col.AddChild(Press("New good…", "Make a new good in the catalogue and put it in this web.", () => AskNewGood(CentreSpot(), _ => { })));

		_palCount = LabLook.Text("", 12, LabLook.Dim);
		col.AddChild(_palCount);

		return col;
	}

	private Control BuildTagsTab()
	{
		var col = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		col.AddThemeConstantOverride("separation", 6);

		_palTagFilter = new LineEdit { PlaceholderText = "filter tags", ClearButtonEnabled = true };
		_palTagFilter.TextChanged += _ => RebuildTagsTree();
		col.AddChild(_palTagFilter);

		col.AddChild(Press("New tag…", "Add a tag to the catalogue, carried by no good yet.", AskNewTag));

		_palTagTree = new Tree { HideRoot = true, Columns = 1, SizeFlagsVertical = SizeFlags.ExpandFill };
		_palTagTree.ItemSelected += () =>
		{
			string tag = _palTagTree.GetSelected()?.GetMetadata(0).AsString() ?? "";
			_palSelectedTag = tag.Length > 0 ? tag : null;
			RefreshTagDetail();
		};
		_palTagTree.ItemCollapsed += item =>
		{
			if (!_palTagSections.TryGetValue(item, out string? key)) return;
			if (item.Collapsed) _palTagFold.Add(key); else _palTagFold.Remove(key);
		};
		col.AddChild(_palTagTree);

		_palTagDetail = BuildTagDetail();
		col.AddChild(_palTagDetail);
		return col;
	}

	private Control BuildTagDetail()
	{
		var box = new VBoxContainer { Visible = false };
		box.AddThemeConstantOverride("separation", 4);

		_palTagCaption = LabLook.Text("", 14, LabLook.Ink);
		box.AddChild(_palTagCaption);

		_palTagNote = new TextEdit
		{
			PlaceholderText = "what this tag means",
			WrapMode = TextEdit.LineWrappingMode.Boundary,
			CustomMinimumSize = new Vector2(0, 54),
		};
		_palTagNote.TextChanged += () =>
		{
			if (_palSelectedTag == null) return;
			string tag = _palSelectedTag, note = _palTagNote.Text;
			Change("noted the tag " + tag, Touch.Catalogue, () =>
			{
				TagDef? def = Catalogue.Tags.FirstOrDefault(t => t.Id == tag);
				if (def == null) Catalogue.Tags.Add(def = new TagDef { Id = tag });
				def.Note = note;
			}, merge: "tag.note:" + tag, keepInspector: true);
		};
		box.AddChild(_palTagNote);

		_palTagCarriers = LabLook.Text("", 12, LabLook.Dim);
		_palTagCarriers.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		box.AddChild(_palTagCarriers);

		// A flow rather than a row: three buttons abreast made this tab wider than the other, and the dock jumped.
		var buttons = new HFlowContainer();
		buttons.AddThemeConstantOverride("h_separation", 4);
		buttons.AddChild(Press("Select in the web", "Select every good of this web that carries the tag.", SelectTagCarriers));
		buttons.AddChild(Press("Rename…", "Rename this tag on every good, and in every web's slots.", AskRenameTag));
		buttons.AddChild(Press("Delete…", "Remove this tag from every good, and from every web's slots.", AskDeleteTag));
		box.AddChild(buttons);

		return box;
	}

	private void BuildPaletteDialogs()
	{
		_palNewTagDialog = new ConfirmationDialog { Title = "New tag", OkButtonText = "Create", MinSize = new Vector2I(360, 0) };
		var newRows = new VBoxContainer();
		_palNewTagDialog.AddChild(newRows);
		newRows.AddChild(LabLook.Text("A namespace and a name, like kind:golem-heart", 12, LabLook.Dim));
		_palNewTagEdit = new LineEdit { PlaceholderText = "kind:golem-heart" };
		newRows.AddChild(_palNewTagEdit);
		_palNewTagEdit.TextSubmitted += _ =>
		{
			_palNewTagDialog.Hide();
			MakeNewTag();
		};
		_palNewTagDialog.Confirmed += MakeNewTag;
		AddChild(_palNewTagDialog);

		_palRenameTagDialog = new ConfirmationDialog { Title = "Rename tag", OkButtonText = "Rename", MinSize = new Vector2I(420, 0) };
		var renameRows = new VBoxContainer();
		_palRenameTagDialog.AddChild(renameRows);
		Label renameWarn = LabLook.Text(
			"Renames it on every good, and in every web's slots. The other webs' files are rewritten at once and that part cannot be undone.",
			12, LabLook.Dim);
		renameWarn.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		renameWarn.CustomMinimumSize = new Vector2(400, 46);
		renameRows.AddChild(renameWarn);
		_palRenameTagEdit = new LineEdit();
		renameRows.AddChild(_palRenameTagEdit);
		_palRenameTagEdit.TextSubmitted += _ =>
		{
			_palRenameTagDialog.Hide();
			MakeRenameTag();
		};
		_palRenameTagDialog.Confirmed += MakeRenameTag;
		AddChild(_palRenameTagDialog);

		_palDeleteTagDialog = new ConfirmationDialog { Title = "Delete tag", OkButtonText = "Delete" };
		_palDeleteTagDialog.Confirmed += MakeDeleteTag;
		AddChild(_palDeleteTagDialog);
	}

	// ---- refresh ---------------------------------------------------------------------

	/// <summary>
	/// Called after every change. Cheap to call and do nothing: a signature of the catalogue
	/// and the web is compared to what each tab last showed, all from objects already in
	/// memory, and only a tab whose signature actually moved rebuilds its tree.
	/// </summary>
	private void RefreshPalette()
	{
		string goodsSig = GoodsSignature();
		if (goodsSig != _palGoodsSig)
		{
			_palGoodsSig = goodsSig;
			RebuildGoodsTree();
		}
		string tagsSig = TagsSignature();
		if (tagsSig != _palTagsSig)
		{
			_palTagsSig = tagsSig;
			RebuildTagsTree();
		}
	}

	private string GoodsSignature()
	{
		var sb = new StringBuilder();
		sb.Append(Web.Id).Append('#');
		foreach (string id in Web.Goods) sb.Append(id).Append(',');
		sb.Append('|');
		foreach (Good g in Catalogue.Goods)
			sb.Append(g.Id).Append('=').Append(g.Name).Append(':').Append(string.Join(" ", g.Tags)).Append(':').Append(IconKey(g.Icon)).Append(';');
		return sb.ToString();
	}

	private static string IconKey(SpriteRef? icon) => icon == null ? "" : icon.File ?? $"{icon.Atlas}#{icon.Index}";

	// ---- goods tree --------------------------------------------------------------------

	/// <summary>Rebuilds the goods tree from the filter, the show/group choices and the model.</summary>
	private void RebuildGoodsTree()
	{
		string raw = _palFilter.Text.Trim();
		bool byTag = raw.StartsWith(Acceptor.TagMark);
		string needle = byTag ? raw[1..] : raw;

		bool PassesFilter(Good g) => needle.Length == 0 || (byTag
			? g.Tags.Any(t => t.Contains(needle, StringComparison.OrdinalIgnoreCase))
			: g.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) || g.Id.Contains(needle, StringComparison.OrdinalIgnoreCase));

		bool PassesShow(Good g) => _palShow.Selected switch
		{
			1 => !Web.Holds(g.Id),
			2 => Web.Holds(g.Id),
			_ => true,
		};

		List<Good> shown = Catalogue.Goods.Where(g => PassesShow(g) && PassesFilter(g)).ToList();

		// The row nearest the top before the rebuild, so the list does not jump under the user.
		string topKey = _palTree.GetItemAtPosition(new Vector2(6, 6))?.GetMetadata(0).AsString() ?? "";

		_palTree.Clear();
		_palGoodSections.Clear();
		TreeItem root = _palTree.CreateItem();
		var goodItems = new Dictionary<string, TreeItem>(StringComparer.Ordinal);

		void MakeGoodItem(TreeItem parent, Good good)
		{
			bool inWeb = Web.Holds(good.Id);
			TreeItem item = _palTree.CreateItem(parent);
			item.SetText(0, good.Name + (inWeb ? " ✓" : ""));
			Texture2D? icon = Sprites.Get(good.Icon);
			if (icon != null) item.SetIcon(0, icon);
			item.SetIconMaxWidth(0, 24);
			item.SetMetadata(0, good.Id);
			if (inWeb) item.SetCustomColor(0, LabLook.Faint);
			item.SetTooltipText(0, good.Note + "\n\n" + string.Join("  ", good.Tags));
			goodItems[good.Id] = item;
		}

		TreeItem MakeSection(string key, string title, Color colour)
		{
			TreeItem item = _palTree.CreateItem(root);
			item.SetText(0, title);
			item.SetSelectable(0, false);
			item.SetCustomColor(0, colour.Lightened(0.4f));
			item.Collapsed = _palGoodFold.Contains(key);
			_palGoodSections[item] = key;
			return item;
		}

		switch (_palGroup.Selected)
		{
			case 1: // by shelf
			{
				var order = new List<string>();
				var seen = new HashSet<string>(StringComparer.Ordinal);
				foreach (Good g in Catalogue.Goods)
				{
					string shelf = g.Tags.FirstOrDefault(t => t.StartsWith("group:", StringComparison.Ordinal)) ?? "";
					if (shelf.Length > 0 && seen.Add(shelf)) order.Add(shelf);
				}
				ILookup<string, Good> byShelf = shown.ToLookup(g => g.Tags.FirstOrDefault(t => t.StartsWith("group:", StringComparison.Ordinal)) ?? "");
				foreach (string shelf in order)
				{
					List<Good> goods = byShelf[shelf].ToList();
					if (goods.Count == 0) continue;
					TreeItem section = MakeSection("shelf:" + shelf, $"{LabLook.Short(shelf).Replace('-', ' ')} ({goods.Count})", LabLook.StageColour(goods[0]));
					foreach (Good g in goods) MakeGoodItem(section, g);
				}
				break;
			}
			case 2: // A-Z
				foreach (Good g in shown.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)) MakeGoodItem(root, g);
				break;
			default: // by stage
			{
				foreach ((string stage, Color colour) in LabLook.Stages)
				{
					List<Good> goods = shown.Where(g => g.Tags.Contains("stage:" + stage)).ToList();
					if (goods.Count == 0) continue;
					TreeItem section = MakeSection("stage:" + stage, $"{Capitalised(stage)} ({goods.Count})", colour);
					foreach (Good g in goods) MakeGoodItem(section, g);
				}
				List<Good> noStage = shown.Where(g => !g.Tags.Any(t => t.StartsWith("stage:", StringComparison.Ordinal))).ToList();
				if (noStage.Count > 0)
				{
					TreeItem section = MakeSection("stage:none", $"No stage ({noStage.Count})", LabLook.NoStage);
					foreach (Good g in noStage) MakeGoodItem(section, g);
				}
				break;
			}
		}

		if (topKey.Length > 0 && goodItems.TryGetValue(topKey, out TreeItem? topAfter)) _palTree.ScrollToItem(topAfter, false);

		RebuildChainPicker();
		_palCount.Text = $"{Catalogue.Goods.Count} goods · {Web.Goods.Count} in this web · {shown.Count} shown";
	}

	private static string Capitalised(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

	/// <summary>Refills the chain-source picker from the webs on disk, keeping the choice made if it still exists.</summary>
	private void RebuildChainPicker()
	{
		string keepId = _palChainPick.ItemCount > 0 && _palChainPick.Selected >= 0 ? _palChainPick.GetItemMetadata(_palChainPick.Selected).AsString() : "";
		_palChainPick.Clear();
		int keep = -1, fallback = -1;
		foreach ((string id, string name) in Store.ListWebs())
		{
			if (id == Web.Id) continue;
			_palChainPick.AddItem(name);
			int index = _palChainPick.ItemCount - 1;
			_palChainPick.SetItemMetadata(index, id);
			if (id == keepId) keep = index;
			if (id == "full-ledger") fallback = index;
		}
		if (_palChainPick.ItemCount > 0) _palChainPick.Select(keep >= 0 ? keep : fallback >= 0 ? fallback : 0);
	}

	/// <summary>The web to copy a chain from when the palette's checkbox asks for one, else null.</summary>
	private EconomyWeb? ChainSource()
	{
		if (_palChainCheck is not { ButtonPressed: true } || _palChainPick.Selected < 0 || _palChainPick.ItemCount == 0) return null;
		string id = _palChainPick.GetItemMetadata(_palChainPick.Selected).AsString();
		return Store.HasWeb(id) ? Store.LoadWeb(id) : null;
	}

	private void ActivateGood()
	{
		string id = _palTree.GetSelected()?.GetMetadata(0).AsString() ?? "";
		if (id.Length == 0 || Catalogue.Find(id) == null) return;
		if (Web.Holds(id)) Select(id, focus: true);
		else AddGoods(new List<string> { id });
	}

	private void RightClickGood(Vector2 position)
	{
		string id = _palTree.GetItemAtPosition(position)?.GetMetadata(0).AsString() ?? "";
		if (id.Length == 0 || Catalogue.Find(id) == null) return;
		bool inWeb = Web.Holds(id);
		ShowMenu(
			(inWeb ? "Go to it on the canvas" : "Add to this web", () =>
			{
				if (inWeb) Select(id, focus: true);
				else AddGoods(new List<string> { id });
			}),
			("New good…", () => AskNewGood(CentreSpot(), _ => { })));
	}

	// ---- tags tree -----------------------------------------------------------------

	private string TagsSignature()
	{
		var sb = new StringBuilder();
		sb.Append(Web.Id).Append('|');
		foreach (TagDef def in Catalogue.Tags) sb.Append(def.Id).Append('=').Append(def.Note).Append(';');
		sb.Append('|');
		foreach (TagDef ns in Catalogue.TagNamespaces) sb.Append(ns.Id).Append('=').Append(ns.Note).Append(';');
		sb.Append('|');
		foreach (Good g in Catalogue.Goods) sb.Append(g.Id).Append('=').Append(g.Name).Append(':').Append(string.Join(" ", g.Tags)).Append(';');
		sb.Append('|');
		foreach (Recipe r in Web.Recipes)
			foreach (RecipeInput slot in r.Inputs)
				sb.Append(string.Join(",", slot.Accepts)).Append(';');
		foreach (Consumer c in Web.Consumers) sb.Append(string.Join(",", c.Accepts)).Append(';');
		return sb.ToString();
	}

	/// <summary>Rebuilds the tag tree, grouped by namespace, and restores the selection and the scroll.</summary>
	private void RebuildTagsTree()
	{
		string raw = _palTagFilter.Text.Trim();
		List<(string Tag, int Count)> all = Catalogue.TagsInUse();
		List<(string Tag, int Count)> shown = raw.Length == 0 ? all : all.Where(t => t.Tag.Contains(raw, StringComparison.OrdinalIgnoreCase)).ToList();

		// Declared namespaces keep their catalogue order; an undeclared one falls in by first use.
		var nsOrder = new List<string>();
		var seenNs = new HashSet<string>(StringComparer.Ordinal);
		foreach (TagDef def in Catalogue.TagNamespaces) if (seenNs.Add(def.Id)) nsOrder.Add(def.Id);
		foreach ((string tag, _) in all)
		{
			int colon = tag.IndexOf(':');
			if (colon > 0 && seenNs.Add(tag[..colon])) nsOrder.Add(tag[..colon]);
		}

		string topKey = _palTagTree.GetItemAtPosition(new Vector2(6, 6))?.GetMetadata(0).AsString() ?? "";

		_palTagTree.Clear();
		_palTagSections.Clear();
		_palTagItems.Clear();
		TreeItem root = _palTagTree.CreateItem();

		void MakeTagItem(TreeItem parent, string tag, int count)
		{
			TreeItem item = _palTagTree.CreateItem(parent);
			item.SetText(0, $"{LabLook.Short(tag)} ({count})");
			item.SetMetadata(0, tag);
			if (EconomyEdit.UsesTag(Web, tag))
			{
				item.SetCustomColor(0, LabLook.TagPort);
				item.SetTooltipText(0, "← a slot takes it");
			}
			_palTagItems[tag] = item;
		}

		foreach (string ns in nsOrder)
		{
			List<(string Tag, int Count)> members = shown.Where(t =>
			{
				int c = t.Tag.IndexOf(':');
				return c > 0 && string.CompareOrdinal(t.Tag[..c], ns) == 0;
			}).ToList();
			if (members.Count == 0) continue;
			TreeItem section = _palTagTree.CreateItem(root);
			section.SetText(0, ns);
			section.SetSelectable(0, false);
			section.SetTooltipText(0, Catalogue.TagNamespaces.FirstOrDefault(n => n.Id == ns)?.Note ?? "");
			section.Collapsed = _palTagFold.Contains(ns);
			_palTagSections[section] = ns;
			foreach ((string tag, int count) in members) MakeTagItem(section, tag, count);
		}
		List<(string Tag, int Count)> plain = shown.Where(t => !t.Tag.Contains(':')).ToList();
		if (plain.Count > 0)
		{
			TreeItem section = _palTagTree.CreateItem(root);
			section.SetText(0, "no namespace");
			section.SetSelectable(0, false);
			section.Collapsed = _palTagFold.Contains("");
			_palTagSections[section] = "";
			foreach ((string tag, int count) in plain) MakeTagItem(section, tag, count);
		}

		if (topKey.Length > 0 && _palTagItems.TryGetValue(topKey, out TreeItem? topAfter)) _palTagTree.ScrollToItem(topAfter, false);

		string? want = _palPendingTagSelect ?? _palSelectedTag;
		_palPendingTagSelect = null;
		if (want != null && _palTagItems.TryGetValue(want, out TreeItem? wanted))
		{
			_palSelectedTag = want;
			wanted.Select(0);
			for (TreeItem? p = wanted.GetParent(); p != null && p != root; p = p.GetParent())
			{
				p.Collapsed = false;
				if (_palTagSections.TryGetValue(p, out string? key)) _palTagFold.Remove(key);
			}
			_palTagTree.ScrollToItem(wanted, true);
		}
		else _palSelectedTag = null;

		RefreshTagDetail();
	}

	/// <summary>
	/// Fills the detail pane for the selected tag, or hides it. Left alone entirely while the
	/// note is being typed in, so a signature change mid-keystroke never disturbs the caret.
	/// </summary>
	private void RefreshTagDetail()
	{
		if (_palTagNote.HasFocus()) return;
		if (_palSelectedTag == null)
		{
			_palTagDetail.Visible = false;
			return;
		}
		string tag = _palSelectedTag;
		_palTagDetail.Visible = true;
		_palTagCaption.Text = tag;
		_palTagNote.Text = Catalogue.Tags.FirstOrDefault(t => t.Id == tag)?.Note ?? "";

		List<string> names = Catalogue.GoodsWith(tag).Select(g => g.Name).ToList();
		if (names.Count == 0) _palTagCarriers.Text = "0 goods carry it.";
		else
		{
			string head = names.Count == 1 ? "1 good carries it: " : $"{names.Count} goods carry it: ";
			string more = names.Count > 12 ? $", and {names.Count - 12} more" : "";
			_palTagCarriers.Text = head + string.Join(", ", names.Take(12)) + more + ".";
		}
	}

	private void SelectTagCarriers()
	{
		if (_palSelectedTag == null) return;
		string tag = _palSelectedTag;
		List<string> ids = Web.Goods.Where(id => Catalogue.Find(id)?.Has(tag) == true).ToList();
		if (ids.Count == 0)
		{
			Say($"No good in this web carries #{tag}.");
			return;
		}
		SelectMany(ids);
		Say($"Selected {ids.Count} good{(ids.Count == 1 ? "" : "s")} carrying #{tag}.");
	}

	// ---- tag dialogs -----------------------------------------------------------------

	private void AskNewTag()
	{
		_palNewTagEdit.Text = "";
		_palNewTagDialog.PopupCentered();
		_palNewTagEdit.GrabFocus();
	}

	private void MakeNewTag()
	{
		string tag = TidyTag(_palNewTagEdit.Text);
		if (tag.Length == 0)
		{
			Say("That is not a usable tag.");
			return;
		}
		if (Catalogue.TagsInUse().Any(t => t.Tag == tag))
		{
			Say($"#{tag} already exists.");
			return;
		}
		_palPendingTagSelect = tag;
		Change($"new tag #{tag}", Touch.Catalogue, () => Catalogue.Tags.Add(new TagDef { Id = tag }));
	}

	private void AskRenameTag()
	{
		if (_palSelectedTag == null) return;
		_palRenameTagEdit.Text = _palSelectedTag;
		_palRenameTagDialog.PopupCentered();
		_palRenameTagEdit.GrabFocus();
		_palRenameTagEdit.SelectAll();
	}

	private void MakeRenameTag()
	{
		if (_palSelectedTag == null) return;
		string from = _palSelectedTag;
		string to = TidyTag(_palRenameTagEdit.Text);
		if (to.Length == 0)
		{
			Say("That is not a usable tag.");
			return;
		}
		if (to == from)
		{
			Say("That is already its name.");
			return;
		}
		if (Catalogue.TagsInUse().Any(t => t.Tag == to))
		{
			Say($"#{to} already exists.");
			return;
		}

		_palPendingTagSelect = to;
		Change($"renamed the tag {from}", Touch.Both, () =>
		{
			EconomyEdit.RenameTag(Catalogue, from, to);
			EconomyEdit.RenameTag(Web, from, to);
		});

		int rewritten = 0;
		foreach ((string id, string _) in Store.ListWebs())
		{
			if (id == Web.Id) continue;
			EconomyWeb other = Store.LoadWeb(id);
			if (EconomyEdit.RenameTag(other, from, to))
			{
				Store.SaveWeb(other);
				rewritten++;
			}
		}
		Say(rewritten == 0 ? $"Renamed #{from} to #{to}." : $"Renamed #{from} to #{to}, and rewrote {rewritten} other web{(rewritten == 1 ? "" : "s")}.");
	}

	private void AskDeleteTag()
	{
		if (_palSelectedTag == null) return;
		string tag = _palSelectedTag;
		int carriers = Catalogue.GoodsWith(tag).Count();
		bool accepted = EconomyEdit.UsesTag(Web, tag);
		_palDeleteTagDialog.DialogText =
			$"Delete #{tag}?\n\n" +
			(carriers == 1 ? "1 good carries it." : $"{carriers} goods carry it.") + "\n" +
			(accepted ? "A slot or a consumer of this web accepts it." : "Nothing in this web's slots accepts it.") + "\n\n" +
			"The other webs' files are rewritten at once and that part cannot be undone.";
		_palDeleteTagDialog.PopupCentered();
	}

	private void MakeDeleteTag()
	{
		if (_palSelectedTag == null) return;
		string tag = _palSelectedTag;
		_palSelectedTag = null;
		Change($"deleted the tag {tag}", Touch.Both, () =>
		{
			EconomyEdit.RemoveTag(Catalogue, tag);
			EconomyEdit.RemoveTag(Web, tag);
		});

		int rewritten = 0;
		foreach ((string id, string _) in Store.ListWebs())
		{
			if (id == Web.Id) continue;
			EconomyWeb other = Store.LoadWeb(id);
			if (EconomyEdit.RemoveTag(other, tag))
			{
				Store.SaveWeb(other);
				rewritten++;
			}
		}
		Say(rewritten == 0 ? $"Deleted #{tag}." : $"Deleted #{tag}, and rewrote {rewritten} other web{(rewritten == 1 ? "" : "s")}.");
	}
}
