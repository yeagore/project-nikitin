using System;
using Godot;

namespace ProjectNikitin.Dev;

/// <summary>
/// A sprite sheet to choose from: every cell of an atlas at 32 px in a grid, the cell in use
/// ringed in the accent, a click picks one. The inspector opens it for a good's icon and for
/// its alchemical sign; both sheets are 16 px pixel art, so the cells draw unfiltered.
/// </summary>
public partial class IconPickPopup : PopupPanel
{
	/// <summary>A cell's side on screen: a whole multiple of the sprite's 16 px.</summary>
	private const int Side = 32;

	private const int Pad = 4, Columns = 8, Rows = 9;

	private readonly Label _prompt = LabLook.Text("", 12, LabLook.Dim);
	private readonly ScrollContainer _scroll = new();
	private readonly GridContainer _grid = new() { Columns = Columns };
	private Action<int>? _picked;

	public IconPickPopup()
	{
		var rows = new VBoxContainer();
		rows.AddThemeConstantOverride("separation", 6);
		AddChild(rows);
		rows.AddChild(_prompt);

		_scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		_scroll.CustomMinimumSize = new Vector2(Columns * (Side + Pad * 2 + 4) + 16, Rows * (Side + Pad * 2 + 4));
		_scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		rows.AddChild(_scroll);
		_grid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_scroll.AddChild(_grid);
	}

	/// <summary>
	/// Opens over the cells of one sheet at <paramref name="at"/> (viewport pixels).
	/// <paramref name="cell"/> gives each cell its texture, <paramref name="current"/> is the
	/// cell in use (−1 for none) and is scrolled to, and <paramref name="picked"/> gets the choice.
	/// </summary>
	public void Ask(string prompt, int count, Func<int, Texture2D?> cell, int current, Vector2 at, Action<int> picked)
	{
		_picked = picked;
		_prompt.Text = prompt;
		foreach (Node old in _grid.GetChildren())
		{
			_grid.RemoveChild(old);
			old.QueueFree();
		}

		for (int i = 0; i < count; i++)
		{
			int index = i;
			var button = new Button
			{
				Icon = cell(i),
				ExpandIcon = true,
				FocusMode = Control.FocusModeEnum.None,
				CustomMinimumSize = new Vector2(Side + Pad * 2, Side + Pad * 2),
				TooltipText = "cell " + i,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			};
			bool now = i == current;
			button.AddThemeStyleboxOverride("normal", now
				? LabLook.Box(LabLook.Field, 4, 4, LabLook.Accent, 2, Pad, Pad)
				: LabLook.Box(new Color(1, 1, 1, 0.04f), 4, 4, marginX: Pad, marginY: Pad));
			button.AddThemeStyleboxOverride("hover", LabLook.Box(new Color(1, 1, 1, 0.16f), 4, 4, LabLook.Accent, 1, Pad, Pad));
			button.AddThemeStyleboxOverride("pressed", LabLook.Box(LabLook.Accent.Darkened(0.4f), 4, 4, marginX: Pad, marginY: Pad));
			button.Pressed += () =>
			{
				Hide();
				_picked?.Invoke(index);
			};
			_grid.AddChild(button);
		}

		Popup(new Rect2I((Vector2I)at, new Vector2I((int)_scroll.CustomMinimumSize.X + 28, (int)_scroll.CustomMinimumSize.Y + 52)));
		ScrollTo(current);
	}

	/// <summary>Brings the cell in use into view, a frame on, once the grid has been laid out.</summary>
	private async void ScrollTo(int index)
	{
		if (index < 0) return;
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (!IsInstanceValid(_scroll)) return;
		_scroll.ScrollVertical = Mathf.Max(0, index / Columns * (Side + Pad * 2 + 4) - (Side + Pad * 2 + 4) * 2);
	}
}
