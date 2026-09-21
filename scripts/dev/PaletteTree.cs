using System.Collections.Generic;
using Godot;

namespace ProjectNikitin.Dev;

/// <summary>
/// The palette's list of goods: a <see cref="Tree"/> whose selected rows can be dragged onto
/// the canvas as goods. A row's metadata is the good's id, set only on good rows; a section
/// header carries none, so it is never part of a drag.
/// </summary>
public partial class PaletteTree : Tree
{
	/// <summary>Looks a good up for the drag preview: its name and icon. Set once by the palette.</summary>
	public System.Func<string, (string Name, Texture2D? Icon)>? Lookup;

	/// <summary>
	/// The dragged goods are the selection, unless the row under the cursor is not part of it,
	/// in which case only that one row is dragged. A section header drags nothing.
	/// </summary>
	public override Variant _GetDragData(Vector2 atPosition)
	{
		string underId = GoodIdOf(GetItemAtPosition(atPosition));
		if (underId.Length == 0) return default;

		List<string> ids = SelectedGoodIds();
		if (!ids.Contains(underId)) ids = new List<string> { underId };

		SetDragPreview(Preview(ids));
		return WebGraph.DragData(ids.ToArray());
	}

	private static string GoodIdOf(TreeItem? item) => item?.GetMetadata(0).AsString() ?? "";

	private List<string> SelectedGoodIds()
	{
		var ids = new List<string>();
		for (TreeItem? item = GetNextSelected(null); item != null; item = GetNextSelected(item))
		{
			string id = GoodIdOf(item);
			if (id.Length > 0) ids.Add(id);
		}
		return ids;
	}

	/// <summary>A small dark plate for the drag cursor: the icon and name of one good, or a count for several.</summary>
	private Control Preview(List<string> ids)
	{
		var plate = new PanelContainer();
		plate.AddThemeStyleboxOverride("panel", LabLook.Box(LabLook.Dock.Lightened(0.08f), 6, 6, LabLook.Accent, 1));
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);
		plate.AddChild(row);
		if (ids.Count == 1 && Lookup != null)
		{
			(string name, Texture2D? icon) = Lookup(ids[0]);
			row.AddChild(LabLook.Sprite(icon, 20));
			row.AddChild(LabLook.Text(name, 13));
		}
		else row.AddChild(LabLook.Text($"{ids.Count} goods", 13));
		return plate;
	}
}
