using System;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// The economy lab's colours, boxes and port types in one place, so the canvas, the
/// docks and the pop-ups agree. A good's colour is its production stage's.
/// </summary>
internal static class LabLook
{
	/// <summary>Port type of anything a good gives out: into a recipe's slot or a consumer.</summary>
	public const int Stuff = 0;

	/// <summary>Port type of what a recipe gives out: into the good it makes.</summary>
	public const int Product = 1;

	public static readonly Color Ink = new("e9e6df");
	public static readonly Color Dim = new("9b978e");
	public static readonly Color Faint = new("6a675f");
	public static readonly Color Accent = new("ffd27a");
	public static readonly Color Back = new("14161a");
	public static readonly Color Dock = new("1c1e24");
	public static readonly Color Field = new("101216");
	public static readonly Color Body = new("20232a");
	public static readonly Color Edge = new("0b0c0f");
	public static readonly Color RecipeHead = new("3b4150");
	public static readonly Color ConsumerHead = new("2d6e4d");
	public static readonly Color ProductPort = new("9fd0ff");
	public static readonly Color InputPort = new("dcdcdc");
	public static readonly Color OptionalPort = new("7d7d7d");
	public static readonly Color TagPort = new("f2b13f");
	public static readonly Color EatenPort = new("74d495");
	public static readonly Color Error = new("ff7a6b");
	public static readonly Color Warning = new("f2b13f");
	public static readonly Color Note = new("8fb6d9");

	/// <summary>The stage shelves in the order the ledger reads them, each with its colour.</summary>
	public static readonly (string Stage, Color Colour)[] Stages =
	{
		("raw", new Color("7b5b3a")),
		("processed", new Color("3d6a8c")),
		("compound", new Color("2e7d70")),
		("magistery", new Color("6c4b9c")),
		("assembly", new Color("a5662b")),
		("finished", new Color("7e8c2e")),
	};

	public static readonly Color NoStage = new("565a63");

	public static Color StageColour(Good good)
	{
		foreach (string tag in good.Tags)
		{
			if (!tag.StartsWith("stage:", StringComparison.Ordinal)) continue;
			foreach ((string stage, Color colour) in Stages)
				if (tag.AsSpan(6).SequenceEqual(stage)) return colour;
		}
		return NoStage;
	}

	public static Color IssueColour(IssueLevel level) => level switch
	{
		IssueLevel.Error => Error,
		IssueLevel.Warning => Warning,
		_ => Note,
	};

	/// <summary>A flat box: fill, the four corner radii as top and bottom, an optional border, and its margins.</summary>
	public static StyleBoxFlat Box(Color fill, int top = 5, int bottom = 5, Color? border = null, int borderWidth = 0,
	                               int marginX = 8, int marginY = 5)
	{
		var box = new StyleBoxFlat
		{
			BgColor = fill,
			CornerRadiusTopLeft = top,
			CornerRadiusTopRight = top,
			CornerRadiusBottomLeft = bottom,
			CornerRadiusBottomRight = bottom,
			ContentMarginLeft = marginX,
			ContentMarginRight = marginX,
			ContentMarginTop = marginY,
			ContentMarginBottom = marginY,
		};
		if (border.HasValue && borderWidth > 0)
		{
			box.BorderColor = border.Value;
			box.SetBorderWidthAll(borderWidth);
		}
		return box;
	}

	/// <summary>Dresses a graph node: a coloured title bar over a dark body, the accent round it when selected.</summary>
	public static void Dress(GraphNode node, Color head, int radius = 6)
	{
		node.AddThemeStyleboxOverride("titlebar", Box(head, radius, 0, marginX: 8, marginY: 4));
		node.AddThemeStyleboxOverride("titlebar_selected", Box(head.Lightened(0.18f), radius, 0, marginX: 8, marginY: 4));
		node.AddThemeStyleboxOverride("panel", Box(Body, 0, radius, Edge, 1, 10, 5));
		node.AddThemeStyleboxOverride("panel_selected", Box(Body.Lightened(0.06f), 0, radius, Accent, 2, 10, 5));
	}

	public static Label Text(string text, int size = 13, Color? colour = null, bool trim = false)
	{
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", colour ?? Ink);
		if (trim)
		{
			label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
			label.ClipText = true;
		}
		return label;
	}

	/// <summary>A sprite drawn crisp at a whole multiple of its 16 px.</summary>
	public static TextureRect Sprite(Texture2D? texture, int side)
	{
		return new TextureRect
		{
			Texture = texture,
			CustomMinimumSize = new Vector2(side, side),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
		};
	}

	/// <summary>A tag without its namespace, as a node or a chip has room for.</summary>
	public static string Short(string tag)
	{
		int colon = tag.IndexOf(':');
		return colon < 0 ? tag : tag[(colon + 1)..];
	}
}
