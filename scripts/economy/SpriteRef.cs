using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>
/// Where a sprite lives: a cell of an atlas (<see cref="Atlas"/> and <see cref="Index"/>),
/// or a PNG of its own (<see cref="File"/>, relative to the economy folder).
/// </summary>
public sealed class SpriteRef
{
	/// <summary>An <see cref="AtlasDef.Id"/>, or null when the sprite is a file.</summary>
	public string? Atlas { get; set; }

	/// <summary>The cell, counted along the rows from the top left.</summary>
	public int? Index { get; set; }

	public string? File { get; set; }

	/// <summary>The sign's reading, source and parts ride here untouched.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }

	public static SpriteRef Cell(string atlas, int index) => new() { Atlas = atlas, Index = index };

	public static SpriteRef Png(string file) => new() { File = file };
}
