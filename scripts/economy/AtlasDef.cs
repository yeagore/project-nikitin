using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>A sheet of square sprites, cells counted along the rows from the top left.</summary>
public sealed class AtlasDef
{
	public string Id { get; set; } = "";

	/// <summary>The PNG, relative to the economy folder.</summary>
	public string File { get; set; } = "";

	/// <summary>Pixels to a cell's side.</summary>
	public int Cell { get; set; } = 16;

	public int Columns { get; set; } = 16;

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }
}
