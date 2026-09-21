using System;
using System.Linq;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// The small parts the inspector dock is built from: captions, wrapping lines, section
/// rules, chips with a × that takes them off, swatches, and a line that answers a click the
/// way a button would if a button could wrap. The colours are <see cref="LabLook"/>'s;
/// nothing here knows the lab, so every part takes the action it is to run.
/// </summary>
internal static class InspectorLook
{
	/// <summary>A small grey label over a field or a list.</summary>
	public static Label Caption(string text) => LabLook.Text(text, 12, LabLook.Dim);

	/// <summary>A line of prose that wraps rather than running out of the dock.</summary>
	public static Label Note(string text, Color? colour = null, int size = 13)
	{
		Label label = LabLook.Text(text, size, colour ?? LabLook.Ink);
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		return label;
	}

	/// <summary>A rule and a caption, to part one block of the dock from the next.</summary>
	public static void Section(VBoxContainer rows, string title)
	{
		rows.AddChild(new Control { CustomMinimumSize = new Vector2(0, 2) });
		rows.AddChild(new HSeparator());
		rows.AddChild(Caption(title));
	}

	/// <summary>A one-line text field, a shade smaller than the theme's, so it sits in a narrow dock.</summary>
	public static LineEdit Field(string text, string placeholder = "")
	{
		var field = new LineEdit { Text = text, PlaceholderText = placeholder };
		field.AddThemeFontSizeOverride("font_size", 14);
		return field;
	}

	/// <summary>A wrapping text field a few lines tall.</summary>
	public static TextEdit Paragraph(string text, int lines)
	{
		var field = new TextEdit
		{
			Text = text,
			WrapMode = TextEdit.LineWrappingMode.Boundary,
			ScrollFitContentHeight = false,
			CustomMinimumSize = new Vector2(0, 18 * lines + 12),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		field.AddThemeFontSizeOverride("font_size", 13);
		return field;
	}

	/// <summary>A row of chips that wraps onto as many lines as it needs.</summary>
	public static HFlowContainer Flow()
	{
		var flow = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		flow.AddThemeConstantOverride("h_separation", 4);
		flow.AddThemeConstantOverride("v_separation", 4);
		return flow;
	}

	/// <summary>A pill: an optional 16 px sprite, a word, and a × that takes the thing off.</summary>
	public static Control Chip(string text, Color fill, Texture2D? icon, string tooltip, Action remove, Color? ink = null)
	{
		var chip = new PanelContainer { TooltipText = tooltip, MouseFilter = Control.MouseFilterEnum.Stop };
		chip.AddThemeStyleboxOverride("panel", LabLook.Box(fill, 9, 9, marginX: 6, marginY: 2));
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 4);
		chip.AddChild(row);
		if (icon != null) row.AddChild(LabLook.Sprite(icon, 16));
		row.AddChild(LabLook.Text(text, 12, ink ?? LabLook.Ink));
		row.AddChild(Cross("Take it off", remove));
		return chip;
	}

	/// <summary>The × inside a chip: quiet until the mouse is on it.</summary>
	public static Button Cross(string tip, Action pressed)
	{
		var cross = new Button { Text = "×", TooltipText = tip, FocusMode = Control.FocusModeEnum.None };
		cross.AddThemeFontSizeOverride("font_size", 14);
		cross.AddThemeColorOverride("font_color", LabLook.Dim);
		cross.AddThemeColorOverride("font_hover_color", LabLook.Ink);
		cross.AddThemeStyleboxOverride("normal", LabLook.Box(new Color(0, 0, 0, 0), 0, 0, marginX: 2, marginY: 0));
		cross.AddThemeStyleboxOverride("hover", LabLook.Box(new Color(1, 1, 1, 0.16f), 4, 4, marginX: 2, marginY: 0));
		cross.AddThemeStyleboxOverride("pressed", LabLook.Box(LabLook.Error.Darkened(0.4f), 4, 4, marginX: 2, marginY: 0));
		cross.Pressed += pressed;
		return cross;
	}

	/// <summary>A small, quiet button: the ones inside a slot's frame or beside a chip row.</summary>
	public static Button Small(string text, string tip, Action pressed)
	{
		var button = new Button { Text = text, TooltipText = tip, FocusMode = Control.FocusModeEnum.None };
		button.AddThemeFontSizeOverride("font_size", 12);
		button.AddThemeColorOverride("font_color", LabLook.Dim);
		button.AddThemeColorOverride("font_hover_color", LabLook.Ink);
		button.AddThemeStyleboxOverride("normal", LabLook.Box(new Color(1, 1, 1, 0.06f), 4, 4, marginX: 6, marginY: 2));
		button.AddThemeStyleboxOverride("hover", LabLook.Box(new Color(1, 1, 1, 0.14f), 4, 4, marginX: 6, marginY: 2));
		button.AddThemeStyleboxOverride("pressed", LabLook.Box(LabLook.Accent.Darkened(0.5f), 4, 4, marginX: 6, marginY: 2));
		button.Pressed += pressed;
		return button;
	}

	/// <summary>
	/// One of a row of choices: a sprite, or a word when there is no sprite to draw, in a pill
	/// that wears the accent while it is the one in force. A button would serve, except that a
	/// button cannot draw a sprite at a whole multiple of its 16 px.
	/// </summary>
	public static Control Toggle(string text, Texture2D? icon, int side, bool on, string tooltip, Action pressed)
	{
		var pill = new PanelContainer
		{
			TooltipText = tooltip,
			MouseFilter = Control.MouseFilterEnum.Stop,
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
		};
		StyleBoxFlat quiet = on
			? LabLook.Box(LabLook.Accent.Darkened(0.66f), 5, 5, LabLook.Accent, 1, 5, 3)
			: LabLook.Box(new Color(1, 1, 1, 0.06f), 5, 5, marginX: 6, marginY: 4);
		StyleBoxFlat lit = on
			? LabLook.Box(LabLook.Accent.Darkened(0.5f), 5, 5, LabLook.Accent, 1, 5, 3)
			: LabLook.Box(new Color(1, 1, 1, 0.14f), 5, 5, marginX: 6, marginY: 4);
		pill.AddThemeStyleboxOverride("panel", quiet);
		if (icon == null) pill.AddChild(LabLook.Text(text, 12, on ? LabLook.Accent : LabLook.Dim));
		else pill.AddChild(LabLook.Sprite(icon, side));
		pill.GuiInput += @event =>
		{
			if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) pressed();
		};
		pill.MouseEntered += () => pill.AddThemeStyleboxOverride("panel", lit);
		pill.MouseExited += () => pill.AddThemeStyleboxOverride("panel", quiet);
		return pill;
	}

	/// <summary>
	/// A sign in a formula: the sprite, with what it stands for in a tooltip, or the word itself
	/// when nothing has been drawn for it yet.
	/// </summary>
	public static Control Glyph(Texture2D? texture, int side, string word, string tooltip)
	{
		if (texture == null) return Mark(word, tooltip, LabLook.Ink);
		TextureRect sprite = LabLook.Sprite(texture, side);
		sprite.MouseFilter = Control.MouseFilterEnum.Stop;
		sprite.TooltipText = tooltip;
		return sprite;
	}

	/// <summary>A word or a mark of punctuation in a formula: the slashes, brackets, pluses and the arrow.</summary>
	public static Label Mark(string text, string tooltip = "", Color? colour = null)
	{
		Label label = LabLook.Text(text, 13, colour ?? LabLook.Faint);
		label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		if (tooltip.Length > 0)
		{
			label.MouseFilter = Control.MouseFilterEnum.Stop;
			label.TooltipText = tooltip;
		}
		return label;
	}

	/// <summary>A colour and what it means, for the legend.</summary>
	public static Control Swatch(string text, Color colour)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);
		var patch = new PanelContainer { CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
		patch.AddThemeStyleboxOverride("panel", LabLook.Box(colour, 3, 3, marginX: 0, marginY: 0));
		row.AddChild(patch);
		row.AddChild(LabLook.Text(text, 12, LabLook.Dim));
		return row;
	}

	/// <summary>
	/// A wrapping line that answers a click: what a button would be, if a button could wrap.
	/// With <paramref name="icon"/> the sprite goes in front of the words.
	/// </summary>
	public static Control Hit(string text, Color colour, string tooltip, Action pressed, Texture2D? icon = null)
	{
		var box = new PanelContainer
		{
			TooltipText = tooltip,
			MouseFilter = Control.MouseFilterEnum.Stop,
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
		};
		StyleBoxFlat quiet = LabLook.Box(new Color(1, 1, 1, 0.03f), 4, 4, marginX: 6, marginY: 3);
		StyleBoxFlat lit = LabLook.Box(new Color(1, 1, 1, 0.11f), 4, 4, marginX: 6, marginY: 3);
		box.AddThemeStyleboxOverride("panel", quiet);
		if (icon == null) box.AddChild(Note(text, colour, 12));
		else
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 6);
			row.AddChild(LabLook.Sprite(icon, 16));
			row.AddChild(Note(text, colour, 12));
			box.AddChild(row);
		}
		box.GuiInput += @event =>
		{
			if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) pressed();
		};
		box.MouseEntered += () => box.AddThemeStyleboxOverride("panel", lit);
		box.MouseExited += () => box.AddThemeStyleboxOverride("panel", quiet);
		return box;
	}

	/// <summary>A tag's chip colour: a stage tag wears its stage's, everything else a neutral grey.</summary>
	public static Color TagColour(string tag) => Stage(tag) ?? Plain;

	/// <summary>The neutral fill of a chip that carries no colour of its own.</summary>
	public static Color Plain => LabLook.Body.Lightened(0.12f);

	/// <summary>The colour a <c>stage:</c> tag names, or null for every other tag.</summary>
	private static Color? Stage(string tag)
	{
		if (!tag.StartsWith("stage:", StringComparison.Ordinal)) return null;
		foreach ((string stage, Color colour) in LabLook.Stages)
			if (tag.AsSpan(6).SequenceEqual(stage)) return colour;
		return null;
	}

	/// <summary>
	/// A tag chip's fill: a stage tag keeps its stage's colour, a tag whose namespace plays a part
	/// in the web gets a dark shade of that part's colour, and the rest the neutral grey.
	/// </summary>
	public static Color TagFill(Palette palette, string tag)
	{
		if (Stage(tag) is { } stage) return stage;
		Color role = LabLook.TagColour(palette, tag);
		return role == LabLook.Ink ? Plain : role.Darkened(0.62f);
	}

	/// <summary>
	/// The words on a tag chip: the colour of the part its namespace plays, or the plain ink
	/// over a stage's fill, which carries the meaning itself.
	/// </summary>
	public static Color TagInk(Palette palette, string tag) => Stage(tag) != null ? LabLook.Ink : LabLook.TagColour(palette, tag);

	/// <summary>The part a tag's namespace plays, in a line for a tooltip; "" for one that only describes.</summary>
	public static string RoleLine(Palette palette, string tag) =>
		palette.IsVariety(tag) ? "a variety tag: it rides from inputs to outputs"
		: palette.IsCore(tag) ? "a core tag: what slots accept"
		: "";

	/// <summary>A tooltip of as many lines as have something to say; the empty ones are left out.</summary>
	public static string Lines(params string[] lines) => string.Join("\n", lines.Where(line => line.Length > 0));
}
