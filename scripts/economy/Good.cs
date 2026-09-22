using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>
/// One good of a web's palette: what it is called, what it is, what it looks like.
/// How it is made is not here; that is a <see cref="Recipe"/>.
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

	/// <summary>Varieties authored by hand (rye and wheat of grain), or null for a good that has none. Read through <see cref="VarietyList"/>.</summary>
	public List<Variety>? Varieties { get; set; }

	/// <summary>How varieties show on the icon, first to last; null leaves it to the lab (the first variety namespace tints the whole icon).</summary>
	public List<IconLayer>? Layers { get; set; }

	/// <summary>Fields this build does not know, kept so a load and a save lose nothing.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }

	public bool Has(string tag) => Tags.Contains(tag);

	/// <summary>The authored varieties, never null.</summary>
	[JsonIgnore]
	public IReadOnlyList<Variety> VarietyList => Varieties ?? (IReadOnlyList<Variety>)System.Array.Empty<Variety>();

	/// <summary>The good's own tags and those of its authored varieties.</summary>
	public IEnumerable<string> AllTags()
	{
		foreach (string tag in Tags) yield return tag;
		if (Varieties == null) yield break;
		foreach (Variety variety in Varieties)
			foreach (string tag in variety.Tags) yield return tag;
	}
}
