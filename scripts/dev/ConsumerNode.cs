using System.Linq;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// The consumer blob: a rounded green sink with one port. A good that leads here is eaten,
/// drunk, worn out or used up; one that leads nowhere is a durable.
/// </summary>
public partial class ConsumerNode : GraphNode
{
	public string ConsumerId { get; private set; } = "";

	private readonly Label _line;
	private string _shown = "";

	public ConsumerNode()
	{
		CustomMinimumSize = new Vector2(WebArrange.ConsumerWidth, 0);
		LabLook.Dress(this, LabLook.ConsumerHead, 18);
		foreach (Label title in GetTitlebarHBox().GetChildren().OfType<Label>())
		{
			title.AddThemeFontSizeOverride("font_size", 14);
			title.AddThemeColorOverride("font_color", LabLook.Ink);
			title.HorizontalAlignment = HorizontalAlignment.Center;
		}

		_line = LabLook.Text("", 11, LabLook.Dim);
		_line.CustomMinimumSize = new Vector2(0, 34);
		_line.VerticalAlignment = VerticalAlignment.Center;
		_line.HorizontalAlignment = HorizontalAlignment.Center;
		_line.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_line.ClipText = true;
		_line.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(_line);
		SetSlot(0, true, LabLook.Stuff, LabLook.EatenPort, false, LabLook.Stuff, LabLook.EatenPort);
	}

	internal void Show(Consumer consumer, WebAnalysis analysis)
	{
		ConsumerId = consumer.Id;
		int goods = analysis.Links.Count(l => l.Kind == LinkKind.Consumed && l.To == consumer.Id);
		string accepts = string.Join(", ", consumer.Accepts.Where(Acceptor.IsTag).Select(a => "#" + LabLook.Short(Acceptor.TagOf(a))));
		string line = goods == 1 ? "consumes 1 good" : $"consumes {goods} goods";
		if (accepts.Length > 0) line += "\n" + accepts;

		string shown = consumer.Name + "|" + line + "|" + consumer.Note;
		if (shown == _shown) return;
		_shown = shown;

		Title = consumer.Name.Length > 0 ? consumer.Name : "Consumers";
		_line.Text = line;
		TooltipText = consumer.Note.Length > 0 ? consumer.Note : "Goods that lead here are consumables.";
	}
}
