using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>
/// One good of the catalogue: what it is called, what it is, what it looks like.
/// How it is made is not here; that is a <see cref="Recipe"/>, and recipes belong
/// to a web, so the same good can be made differently in two versions of the economy.
/// </summary>
public sealed class Good
{
	/// <summary>Short, lowercase, unique and stable: the key everything else refers to. The name may change; this does not.</summary>
	public string Id { get; set; } = "";

	public string Name { get; set; } = "";

	/// <summary>The description, in the designer's words.</summary>
	public string Note { get; set; } = "";

	/// <summary>
	/// Namespaced properties (<c>kind:metal</c>, <c>need:food</c>). A recipe slot or a
	/// consumer can accept a tag instead of a good, and then anything carrying it fits.
	/// </summary>
	public List<string> Tags { get; set; } = new();

	public SpriteRef? Icon { get; set; }

	/// <summary>The alchemical sign, a second 16 px sprite beside the icon.</summary>
	public SpriteRef? Sign { get; set; }

	/// <summary>Fields this build does not know, kept so a load and a save lose nothing.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }

	public bool Has(string tag) => Tags.Contains(tag);
}
