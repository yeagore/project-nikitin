using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>
/// One variety of a good, authored by hand: rye and wheat are varieties of grain, the seventeen
/// soils varieties of soil. It is the same node in the web and fits the same slots; what differs
/// is its tags, which should be variety tags so that they travel downstream (rye flour, rye bread).
/// A variety that comes from a recipe's inputs (an arsenic-hearted golem) is not written down
/// anywhere: <see cref="WebAnalysis.VarietiesOf"/> derives it.
/// </summary>
public sealed class Variety
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public string Note { get; set; } = "";

	/// <summary>On top of the good's own tags.</summary>
	public List<string> Tags { get; set; } = new();

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }
}
