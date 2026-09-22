using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>
/// How one kind of variety shows on a good's icon. The shape belongs to the good and the hue to
/// the variety: a layer takes the colour of the unit's tag in <see cref="Match"/> and tints the
/// pixels of <see cref="Mask"/> with it (the golem's heart, its eyes), or the whole icon when
/// there is no mask (rye and wheat). A unit that has no such tag, or a stack not split by it,
/// keeps the plain icon there.
/// </summary>
public sealed class IconLayer
{
	/// <summary>A variety namespace (<c>soil</c>: whichever soil tag the unit has), or one whole tag (<c>fit:smalt-eyes</c>).</summary>
	public string Match { get; set; } = "";

	/// <summary>The pixels to tint, as a sprite whose opaque pixels mark them; null for the whole icon.</summary>
	public SpriteRef? Mask { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }

	/// <summary>True when this layer answers to the tag: its namespace, or the tag itself.</summary>
	public bool Answers(string tag) => Match.Contains(':') ? Match == tag : Palette.NamespaceOf(tag) == Match;
}
