using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>
/// One way of making something: every input slot filled, the outputs come out.
/// A good made two ways has two recipes. Amounts, time, the building and the labour
/// are not modelled yet; they will be fields here and on the slots.
/// </summary>
public sealed class Recipe
{
	/// <summary>Unique within its web, and never the same as a good's id: it starts with <c>r.</c></summary>
	public string Id { get; set; } = "";

	/// <summary>An optional label ("Cementation"). Blank reads as "→ output".</summary>
	public string Name { get; set; } = "";

	public string Note { get; set; } = "";
	public List<RecipeInput> Inputs { get; set; } = new();
	public List<RecipeOutput> Outputs { get; set; } = new();

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }

	public bool Makes(string goodId) => Outputs.Exists(o => o.Good == goodId);
}
