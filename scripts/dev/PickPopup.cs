using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectNikitin.Dev;

/// <summary>
/// A small search-and-pick pop-up: type to narrow, arrows and Enter or a click to choose,
/// Escape to leave. It picks goods, tags and webs alike; with <c>typed</c> given, text that
/// matches nothing can be taken as a new entry (a new tag).
/// </summary>
public partial class PickPopup : PopupPanel
{
	/// <summary>Rows shown at once; narrowing the search brings the rest.</summary>
	private const int Shown = 250;

	private readonly LineEdit _search = new() { PlaceholderText = "type to search", ClearButtonEnabled = true };
	private readonly ItemList _list = new() { CustomMinimumSize = new Vector2(300, 320), FixedIconSize = new Vector2I(24, 24) };
	private readonly Label _prompt = LabLook.Text("", 12, LabLook.Dim);
	private List<(string Key, string Label, Texture2D? Icon)> _items = new();
	private Action<string>? _picked, _typed;

	public PickPopup()
	{
		var rows = new VBoxContainer();
		rows.AddThemeConstantOverride("separation", 6);
		AddChild(rows);
		rows.AddChild(_prompt);
		rows.AddChild(_search);
		rows.AddChild(_list);
		_list.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_list.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;

		_search.TextChanged += _ => Fill();
		_search.TextSubmitted += _ => Choose(_list.GetSelectedItems().FirstOrDefault(-1));
		_search.GuiInput += Steer;
		_list.ItemActivated += index => Choose((int)index);
		_list.ItemClicked += (index, _, button) => { if (button == (long)MouseButton.Left) Choose((int)index); };
	}

	/// <summary>
	/// Opens at <paramref name="at"/> (viewport pixels). <paramref name="picked"/> gets the key chosen;
	/// <paramref name="typed"/>, if given, gets the text when Enter is pressed on something that matches nothing.
	/// </summary>
	public void Ask(string prompt, IEnumerable<(string Key, string Label, Texture2D? Icon)> items,
	                Vector2 at, Action<string> picked, Action<string>? typed = null)
	{
		_items = items.ToList();
		_picked = picked;
		_typed = typed;
		_prompt.Text = prompt;
		_search.Text = "";
		Fill();
		Popup(new Rect2I((Vector2I)at, new Vector2I(320, 400)));
		_search.GrabFocus();
	}

	private void Fill()
	{
		string want = _search.Text.Trim();
		_list.Clear();
		foreach ((string key, string label, Texture2D? icon) in _items)
		{
			if (want.Length > 0 && !label.Contains(want, StringComparison.OrdinalIgnoreCase) && !key.Contains(want, StringComparison.OrdinalIgnoreCase)) continue;
			int index = _list.AddItem(label, icon);
			_list.SetItemMetadata(index, key);
			if (_list.ItemCount >= Shown) break;
		}
		if (_list.ItemCount > 0) _list.Select(0);
		else if (_typed != null && want.Length > 0)
		{
			int index = _list.AddItem($"new: {want}");
			_list.SetItemMetadata(index, "");
			_list.Select(0);
		}
	}

	/// <summary>Up and down move the list's selection while the caret stays in the search field.</summary>
	private void Steer(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true } key || _list.ItemCount == 0) return;
		int step = key.Keycode == Key.Down ? 1 : key.Keycode == Key.Up ? -1 : 0;
		if (step == 0) return;
		int now = _list.GetSelectedItems().FirstOrDefault(0);
		int next = Mathf.Clamp(now + step, 0, _list.ItemCount - 1);
		_list.Select(next);
		_list.EnsureCurrentIsVisible();
		_search.AcceptEvent();
	}

	private void Choose(int index)
	{
		if (index < 0 || index >= _list.ItemCount) return;
		string key = _list.GetItemMetadata(index).AsString();
		string text = _search.Text.Trim();
		Hide();
		if (key.Length > 0) _picked?.Invoke(key);
		else if (text.Length > 0) _typed?.Invoke(text);
	}
}
