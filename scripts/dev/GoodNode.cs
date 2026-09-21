using System.Collections.Generic;
using System.Linq;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// A good on the canvas: its icon and name on a bar in its stage's colour, and a line
/// saying what it is in this web. The left port takes the recipes that make it, the
/// right one gives it to the recipes that use it and to the consumers.
/// </summary>
public partial class GoodNode : GraphNode
{
	public string GoodId { get; private set; } = "";

	private readonly TextureRect _icon;
	private readonly Label _line;
	private string _shown = "";

	public GoodNode()
	{
		CustomMinimumSize = new Vector2(WebArrange.GoodWidth, 0);
		HBoxContainer bar = GetTitlebarHBox();
		_icon = LabLook.Sprite(null, 32);
		bar.AddChild(_icon);
		bar.MoveChild(_icon, 0);
		foreach (Label title in bar.GetChildren().OfType<Label>())
		{
			title.AddThemeFontSizeOverride("font_size", 14);
			title.AddThemeColorOverride("font_color", LabLook.Ink);
			title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
			title.ClipText = true;
		}

		_line = LabLook.Text("", 11, LabLook.Dim, trim: true);
		_line.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_line.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(_line);
	}

	/// <summary>Brings the node into line with the good and the web's reading of it; does nothing if nothing it shows has changed.</summary>
	internal void Show(Good good, Texture2D? icon, WebAnalysis analysis)
	{
		GoodId = good.Id;
		GoodRole role = analysis.RoleOf(good.Id);
		int uses = analysis.UsersOf(good.Id).Count;

		var words = new List<string>();
		if (role != GoodRole.Intermediate) words.Add(role.ToString().ToLowerInvariant());
		words.AddRange(good.Tags.Where(t => t.StartsWith("kind:")).Take(2).Select(LabLook.Short));
		if (analysis.IsConsumed(good.Id)) words.Add("consumed");
		if (analysis.IsHub(good.Id)) words.Add($"hub ×{uses}");
		string line = string.Join(" · ", words);

		Color stage = LabLook.StageColour(good);
		string shown = $"{good.Name}|{line}|{stage.ToHtml()}|{icon?.GetInstanceId()}|{good.Note}";
		if (shown == _shown) return;
		_shown = shown;

		Title = good.Name;
		_icon.Texture = icon;
		_line.Text = line.Length > 0 ? line : " ";
		TooltipText = good.Note.Length > 0 ? $"{good.Name}\n{good.Note}" : good.Name;
		LabLook.Dress(this, role == GoodRole.Loose ? stage.Darkened(0.35f) : stage);
		SetSlot(0, true, LabLook.Product, LabLook.ProductPort, true, LabLook.Stuff, stage.Lightened(0.35f));
	}
}
