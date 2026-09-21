using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>What a recipe makes. An object rather than a bare id so an amount, or a by-product flag, can join it later.</summary>
public sealed class RecipeOutput
{
	public string Good { get; set; } = "";

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }
}
