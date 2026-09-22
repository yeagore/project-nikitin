using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>A tag, or a tag namespace, and what it means.</summary>
public sealed class TagDef
{
	public string Id { get; set; } = "";
	public string Note { get; set; } = "";

	/// <summary>The tag's colour as <c>#RRGGBB</c>, or null. A variety tag's colour is the hue its variety takes on an icon, and the tint of its symbol.</summary>
	public string? Colour { get; set; }

	/// <summary>The tag's own symbol, or null: then its namespace's symbol stands for it, tinted with <see cref="Colour"/>.</summary>
	public SpriteRef? Sign { get; set; }

	/// <summary>
	/// For a variety tag: the property tags every unit of this variety has (<c>soil:murkearth</c> implies
	/// <c>work:water</c>). Units stack by property, so seventeen soils can come to five stacks. Null for none.
	/// </summary>
	public List<string>? Implies { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }
}
