using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>
/// One way of making something: every input slot filled, the outputs come out.
/// A good made two ways has two recipes. Each slot and output has an amount and the
/// recipe a time, so a web can be balanced; the building and the labour are not modelled
/// yet and will be fields here.
/// </summary>
public sealed class Recipe
{
	/// <summary>Unique within its web, and never the same as a good's id: it starts with <c>r.</c></summary>
	public string Id { get; set; } = "";

	/// <summary>An optional label ("Cementation"). Blank reads as "→ output".</summary>
	public string Name { get; set; } = "";

	public string Note { get; set; } = "";

	/// <summary>The recipe's elemental association, an <see cref="Economy.Element"/> id (<c>fire</c>, <c>wind</c>, <c>water</c>, <c>earth</c>, <c>qe</c>), or null.</summary>
	public string? Element { get; set; }

	/// <summary>
	/// Where the work has to stand, as tags of the ground or the place (<c>soil:murkearth</c>,
	/// <c>site:coast</c>), any one of which will do; null for anywhere. Not a slot: nothing is
	/// hauled or used up. Peat is cut where the ground is murkearth, so its recipe takes no soil.
	/// </summary>
	public List<string>? Site { get; set; }

	/// <summary>How long one run takes, in days; null means one. Read through <see cref="Days"/>. A workshop runs one batch at a time, so runs a day times days is workshops busy.</summary>
	public double? Time { get; set; }

	[JsonIgnore]
	public double Days => Time ?? 1;

	public List<RecipeInput> Inputs { get; set; } = new();
	public List<RecipeOutput> Outputs { get; set; } = new();

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }

	public bool Makes(string goodId) => Outputs.Exists(o => o.Good == goodId);

	/// <summary>The site tags, never null.</summary>
	[JsonIgnore]
	public IReadOnlyList<string> SiteList => Site ?? (IReadOnlyList<string>)System.Array.Empty<string>();
}
