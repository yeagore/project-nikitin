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
	private string _name = "", _under = " ";
	private bool _quiet;

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
		VarietySet varieties = analysis.VarietiesOf(good.Id);
		if (!varieties.IsPlain) words.Add(varieties.Capped ? $"~{varieties.Count} varieties" : varieties.Count == 1 ? LabLook.VarietyName(varieties.Sets[0]) : $"{varieties.Count} varieties");
		string line = string.Join(" · ", words);

		Color stage = LabLook.StageColour(good);
		string shown = $"{good.Name}|{line}|{stage.ToHtml()}|{icon?.GetInstanceId()}|{good.Note}";
		if (shown == _shown) return;
		_shown = shown;

		_name = good.Name;
		_under = line.Length > 0 ? line : " ";
		if (!_quiet)
		{
			Title = _name;
			_line.Text = _under;
		}
		_icon.Texture = icon;
		TooltipText = good.Note.Length > 0 ? $"{good.Name}\n{good.Note}" : good.Name;
		LabLook.Dress(this, role == GoodRole.Loose ? stage.Darkened(0.35f) : stage);
		SetSlot(0, true, LabLook.Product, LabLook.ProductPort, true, LabLook.Stuff, stage.Lightened(0.35f));
	}

	/// <summary>
	/// Zoomed far out the writing is a smudge, so the node goes quiet: no icon, no name, no line
	/// under it. The head and the line keep the height they had, so the port and the node's box stay
	/// exactly where they were and no wire moves; zooming back in puts every word back.
	/// </summary>
	internal void Quiet(bool on)
	{
		if (_quiet == on) return;
		// Only a node that has been laid out knows the heights it must hold; one made a moment ago
		// is left alone and quietened at the next turn of the zoom.
		if (on && Size.Y <= 1f) return;
		_quiet = on;
		WebGraph.KeepHeight(GetTitlebarHBox());
		WebGraph.KeepHeight(_line);
		_icon.Visible = !on;
		Title = on ? "" : _name;
		_line.Text = on ? " " : _under;
	}
}
