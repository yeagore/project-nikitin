using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>A tag, or a tag namespace, and what it means.</summary>
public sealed class TagDef
{
	public string Id { get; set; } = "";
	public string Note { get; set; } = "";

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }
}
