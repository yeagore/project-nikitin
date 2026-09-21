using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// A recipe on the canvas. Each row is an input slot on the left and an output on the right;
/// the last row is a pair of open ports, so a link dropped there makes a new slot or a new
/// output. A slot says what it accepts: a good, several ("or"), or a tag in amber, which
/// links itself to whatever in the web carries it; a chevron marks a slot whose filling
/// passes its variety on to the output.
/// </summary>
public partial class RecipeNode : GraphNode
{
	public string RecipeId { get; private set; } = "";

	/// <summary>Input ports below this index are slots; the port at it is the open one.</summary>
	public int Inputs { get; private set; }

	/// <summary>Output ports below this index are outputs; the port at it is the open one.</summary>
	public int Outputs { get; private set; }

	private string _shown = "";

	public RecipeNode()
	{
		CustomMinimumSize = new Vector2(WebArrange.RecipeWidth, 0);
		LabLook.Dress(this, LabLook.RecipeHead);
		foreach (Label title in GetTitlebarHBox().GetChildren().OfType<Label>())
		{
			title.AddThemeFontSizeOverride("font_size", 12);
			title.AddThemeColorOverride("font_color", LabLook.Ink);
			title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
			title.ClipText = true;
		}
	}

	internal void Show(Recipe recipe, Palette palette, WebAnalysis analysis, SpriteBank sprites)
	{
		RecipeId = recipe.Id;
		string title = analysis.TitleOf(recipe);

		var shown = new StringBuilder(title).Append('|').Append(recipe.Note);
		foreach (RecipeInput slot in recipe.Inputs)
		{
			shown.Append("|i").Append(slot.Optional ? '?' : '!').Append(slot.Passes ? '>' : '.');
			foreach (string acceptor in slot.Accepts) shown.Append(acceptor).Append('=').Append(palette.Find(acceptor)?.Name).Append(',');
		}
		foreach (RecipeOutput output in recipe.Outputs)
			shown.Append("|o").Append(output.Good).Append('=').Append(palette.Find(output.Good)?.Name);
		if (shown.ToString() == _shown) return;
		_shown = shown.ToString();

		Title = title;
		TooltipText = recipe.Note.Length > 0 ? $"{title}\n{recipe.Note}" : title;
		Inputs = recipe.Inputs.Count;
		Outputs = recipe.Outputs.Count;

		foreach (Node row in GetChildren())
		{
			RemoveChild(row);
			row.QueueFree();
		}
		ClearAllSlots();

		int rows = Math.Max(Inputs, Outputs) + 1;
		for (int i = 0; i < rows; i++)
		{
			var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 20), MouseFilter = MouseFilterEnum.Ignore };
			row.AddThemeConstantOverride("separation", 4);
			AddChild(row);

			Color left = LabLook.Faint;
			if (i < Inputs)
			{
				RecipeInput slot = recipe.Inputs[i];
				left = SlotColour(slot);
				FillInput(row, slot, palette, sprites);
			}
			else if (i == Inputs) row.AddChild(LabLook.Text("+ input", 11, LabLook.Faint));

			row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });

			if (i < Outputs)
			{
				Good? made = palette.Find(recipe.Outputs[i].Good);
				row.AddChild(LabLook.Text(made?.Name ?? recipe.Outputs[i].Good, 11, LabLook.ProductPort, trim: false));
				row.AddChild(LabLook.Sprite(sprites.Get(made?.Icon), 16));
			}
			else if (i == Outputs) row.AddChild(LabLook.Text("output +", 11, LabLook.Faint));

			SetSlot(i, i <= Inputs, LabLook.Stuff, left, i <= Outputs, LabLook.Product, i < Outputs ? LabLook.ProductPort : LabLook.Faint);
		}
	}

	private static Color SlotColour(RecipeInput slot)
	{
		if (slot.Optional) return LabLook.OptionalPort;
		return slot.Accepts.Any(Acceptor.IsTag) ? LabLook.TagPort : LabLook.InputPort;
	}

	/// <summary>The slot in words: "Copper", "Coal or Charcoal", "#golem-heart", with "(opt)" in front of an optional one.</summary>
	private static void FillInput(HBoxContainer row, RecipeInput slot, Palette palette, SpriteBank sprites)
	{
		if (slot.Accepts.Count == 0)
		{
			row.AddChild(LabLook.Text("accepts nothing", 11, LabLook.Error));
			return;
		}

		string? single = slot.Accepts.Count == 1 && !Acceptor.IsTag(slot.Accepts[0]) ? slot.Accepts[0] : null;
		if (single != null) row.AddChild(LabLook.Sprite(sprites.Get(palette.Find(single)?.Icon), 16));

		var words = new List<string>();
		foreach (string acceptor in slot.Accepts)
			words.Add(Acceptor.IsTag(acceptor) ? "#" + LabLook.Short(Acceptor.TagOf(acceptor)) : palette.Find(acceptor)?.Name ?? acceptor);
		// A slot that passes variety on wears a chevron: what goes in here shows in what comes out.
		string text = (slot.Optional ? "(opt) " : "") + string.Join(" or ", words) + (slot.Passes ? "  »" : "");

		Label label = LabLook.Text(text, 11, SlotColour(slot) == LabLook.InputPort ? LabLook.Ink : SlotColour(slot), trim: true);
		label.CustomMinimumSize = new Vector2(Math.Min(120, 20 + text.Length * 6), 0);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		label.SizeFlagsStretchRatio = 4;
		label.MouseFilter = MouseFilterEnum.Ignore;
		row.AddChild(label);
	}
}
