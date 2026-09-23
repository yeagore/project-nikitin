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

	/// <summary>
	/// What comes out anyway while the recipe makes something else: the mill's bran, the smelter's
	/// slag. Nobody runs a mill for bran, so a want of it never asks the recipe to run; it takes
	/// what comes.
	/// </summary>
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool ByProduct { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }
}
