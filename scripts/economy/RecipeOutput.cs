using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>What a recipe makes, and how much of it one run gives.</summary>
public sealed class RecipeOutput
{
	public string Good { get; set; } = "";

	/// <summary>How many units one run gives; null means one. Read through <see cref="Count"/>.</summary>
	public double? Amount { get; set; }

	[JsonIgnore]
	public double Count => Amount ?? 1;

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }
}
