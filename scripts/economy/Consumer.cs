using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>
/// A sink: the people who eat, drink, wear or use up what reaches them. A good that
/// leads here is a consumable; one that leads nowhere is a durable or a work. It accepts
/// goods and tags the way a recipe slot does, so a consumer of <c>#need:food</c> takes every food in the web.
/// </summary>
public sealed class Consumer
{
	/// <summary>Unique within its web; starts with <c>c.</c></summary>
	public string Id { get; set; } = "";

	public string Name { get; set; } = "";
	public string Note { get; set; } = "";
	public List<string> Accepts { get; set; } = new();

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }
}
