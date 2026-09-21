using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// Import: the one way goods cross from one web to another. Pick another web, pick some of
/// its palette's goods (or take the whole palette), and they are copied into the open web's
/// palette, with their tag notes, their namespaces and roles, and their sprite sheets; with
/// the chain box ticked their recipes and everything upstream land on the canvas too. The
/// other web is only read: nothing here can change it.
/// </summary>
public partial class EconomyLab
{
	private AcceptDialog _impDialog = null!;
	private OptionButton _impFrom = null!;
	private LineEdit _impFilter = null!;
	private Tree _impTree = null!;
	private Label _impCount = null!;
	private CheckBox _impChain = null!, _impOverwrite = null!;
	private Button _impSelected = null!, _impWhole = null!;

	/// <summary>The web being imported from, loaded when it is picked and never written.</summary>
	private EconomyWeb? _impSource;

	private void BuildImport()
	{
		_impDialog = new AcceptDialog { Title = "Import from another web", OkButtonText = "Close", MinSize = new Vector2I(560, 640) };
		var rows = new VBoxContainer();
		rows.AddThemeConstantOverride("separation", 6);
		_impDialog.AddChild(rows);

		var fromRow = new HBoxContainer();
		fromRow.AddChild(LabLook.Text("From", 13, LabLook.Dim));
		_impFrom = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_impFrom.ItemSelected += _ => LoadImportSource();
		fromRow.AddChild(_impFrom);
		rows.AddChild(fromRow);

		_impFilter = new LineEdit { PlaceholderText = "filter: a name, or #tag", ClearButtonEnabled = true };
		_impFilter.TextChanged += _ => FillImportTree();
		rows.AddChild(_impFilter);

		_impTree = new Tree
		{
			HideRoot = true,
			Columns = 1,
			SelectMode = Tree.SelectModeEnum.Multi,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 360),
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
		};
		_impTree.MultiSelected += (_, _, _) => Callable.From(CountImport).CallDeferred();
		rows.AddChild(_impTree);

		_impCount = LabLook.Text("", 12, LabLook.Dim);
		rows.AddChild(_impCount);

		_impChain = new CheckBox
		{
			Text = "with their recipes and everything upstream, onto the canvas",
			TooltipText = "Copies the recipes that make them and all their inputs, down to the ground, as that web has them. They land on the canvas.",
		};
		rows.AddChild(_impChain);
		_impOverwrite = new CheckBox
		{
			Text = "overwrite goods this palette already has",
			TooltipText = "Off: a good that is here already (the same id) is left as it is. On: its name, note, tags, varieties and sprites are replaced by the other web's.",
		};
		rows.AddChild(_impOverwrite);

		var buttons = new HBoxContainer();
		buttons.AddThemeConstantOverride("separation", 8);
		_impSelected = Press("Import selected", "Copy the selected goods into this web's palette.", ImportSelected);
		_impWhole = Press("Import the whole palette", "Copy every good, tag note, namespace and sprite sheet of that web into this one's palette.", ImportWhole);
		_impSelected.SizeFlagsHorizontal = _impWhole.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		buttons.AddChild(_impSelected);
		buttons.AddChild(_impWhole);
		rows.AddChild(buttons);

		AddChild(_impDialog);
	}

	private void AskImport()
	{
		List<(string Id, string Name)> others = Store.ListWebs().Where(w => w.Id != Web.Id).ToList();
		if (others.Count == 0)
		{
			Say("There is no other web to import from.");
			return;
		}
		string keep = _impFrom.ItemCount > 0 && _impFrom.Selected >= 0 ? _impFrom.GetItemMetadata(_impFrom.Selected).AsString() : "full-ledger";
		_impFrom.Clear();
		foreach ((string id, string name) in others)
		{
			_impFrom.AddItem(name);
			_impFrom.SetItemMetadata(_impFrom.ItemCount - 1, id);
			if (id == keep) _impFrom.Select(_impFrom.ItemCount - 1);
		}
		if (_impFrom.Selected < 0) _impFrom.Select(0);
		_impFilter.Text = "";
		LoadImportSource();
		_impDialog.PopupCentered(new Vector2I(560, 640));
	}

	private void LoadImportSource()
	{
		_impSource = _impFrom.Selected < 0 ? null : TryLoad(_impFrom.GetItemMetadata(_impFrom.Selected).AsString());
		if (_impSource == null) Say("That web does not read; the console says why.");
		FillImportTree();
	}

	/// <summary>The other web's palette, shelved by stage like the Goods tab; what this palette already has is marked and faint.</summary>
	private void FillImportTree()
	{
		_impTree.Clear();
		TreeItem root = _impTree.CreateItem();
		if (_impSource == null)
		{
			CountImport();
			return;
		}

		string raw = _impFilter.Text.Trim();
		bool byTag = raw.StartsWith(Acceptor.TagMark);
		string needle = byTag ? raw[1..] : raw;
		List<Good> shown = _impSource.Palette.Goods.Where(g => needle.Length == 0 || (byTag
			? g.AllTags().Any(t => t.Contains(needle, StringComparison.OrdinalIgnoreCase))
			: g.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) || g.Id.Contains(needle, StringComparison.OrdinalIgnoreCase))).ToList();

		void Shelf(string title, Color colour, List<Good> goods)
		{
			if (goods.Count == 0) return;
			TreeItem section = _impTree.CreateItem(root);
			section.SetText(0, $"{title} ({goods.Count})");
			section.SetSelectable(0, false);
			section.SetCustomColor(0, colour.Lightened(0.4f));
			foreach (Good good in goods)
			{
				bool here = Palette.Find(good.Id) != null;
				TreeItem item = _impTree.CreateItem(section);
				item.SetText(0, good.Name + (here ? "  ✓ here already" : ""));
				if (Sprites.Get(good.Icon) is { } icon) item.SetIcon(0, icon);
				item.SetIconMaxWidth(0, 24);
				item.SetMetadata(0, good.Id);
				if (here) item.SetCustomColor(0, LabLook.Faint);
				item.SetTooltipText(0, good.Note + "\n\n" + string.Join("  ", good.AllTags().Distinct()));
			}
		}

		foreach ((string stage, Color colour) in LabLook.Stages)
			Shelf(Capitalised(stage), colour, shown.Where(g => g.Tags.Contains("stage:" + stage)).ToList());
		Shelf("No stage", LabLook.NoStage, shown.Where(g => !g.Tags.Any(t => t.StartsWith("stage:", StringComparison.Ordinal))).ToList());
		CountImport();
	}

	private List<string> SelectedImportIds()
	{
		var ids = new List<string>();
		for (TreeItem? item = _impTree.GetNextSelected(null); item != null; item = _impTree.GetNextSelected(item))
		{
			string id = item.GetMetadata(0).AsString();
			if (id.Length > 0) ids.Add(id);
		}
		return ids;
	}

	private void CountImport()
	{
		int all = _impSource?.Palette.Goods.Count ?? 0;
		int here = _impSource?.Palette.Goods.Count(g => Palette.Find(g.Id) != null) ?? 0;
		int selected = SelectedImportIds().Count;
		_impCount.Text = $"{all} goods · {here} here already · {selected} selected";
		_impSelected.Disabled = selected == 0;
		_impWhole.Disabled = _impSource == null;
	}

	private void ImportSelected()
	{
		if (_impSource is not { } source) return;
		List<string> ids = SelectedImportIds();
		if (ids.Count == 0) return;
		bool chain = _impChain.ButtonPressed, overwrite = _impOverwrite.ButtonPressed;
		int arrived = 0, landed = 0;
		Change($"imported {ids.Count} good{(ids.Count == 1 ? "" : "s")} from {source.Name}", () =>
		{
			arrived = EconomyEdit.ImportGoods(source, Web, ids, overwrite);
			if (!chain) return;
			// The first good lands mid-screen and the rest keep their places round it, as the other web laid them out.
			Spot origin = source.Layout.GetValueOrDefault(ids[0]), centre = CentreSpot();
			int before = Palette.Goods.Count;
			landed = EconomyEdit.CopyChain(source, Web, ids, withOptional: true, new Spot(centre.X - origin.X, centre.Y - origin.Y)).Count;
			arrived += Palette.Goods.Count - before;
		});
		Say($"{arrived} good{(arrived == 1 ? "" : "s")} arrived in the palette from {source.Name}" + (chain ? $", and {landed} node{(landed == 1 ? "" : "s")} landed on the canvas." : "."));
		FillImportTree();
	}

	private void ImportWhole()
	{
		if (_impSource is not { } source) return;
		bool overwrite = _impOverwrite.ButtonPressed;
		int arrived = 0;
		Change($"imported the palette of {source.Name}", () => arrived = EconomyEdit.ImportPalette(source, Web, overwrite));
		Say($"{arrived} good{(arrived == 1 ? "" : "s")} arrived in the palette from {source.Name}, with its tags and namespaces.");
		FillImportTree();
	}
}
