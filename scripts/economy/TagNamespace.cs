using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>
/// A tag prefix (the <c>kind</c> of <c>kind:metal</c>), what it means, and the part its tags play.
/// Core tags say what a good is, and are what slots and consumers accept: they decide a good's
/// place in the web. Variety tags say what is particular about it (<c>heart:arsenic</c>,
/// <c>grain:rye</c>): they ride from an input to the output through any slot that passes
/// variety on, so one recipe yields as many varieties as its inputs allow. Property tags are what
/// variety tags imply (<c>grade:fine</c>): units stack by them. The rest describe.
/// </summary>
public sealed class TagNamespace
{
	public const string Core = "core", Variety = "variety", Property = "property";

	/// <summary>The value of <see cref="Combine"/> for a namespace whose tags are a scale: a unit keeps only the lowest it was given.</summary>
	public const string Lowest = "lowest";

	public string Id { get; set; } = "";
	public string Note { get; set; } = "";

	/// <summary><see cref="Core"/>, <see cref="Variety"/>, <see cref="Property"/>, or null for a namespace that only describes.</summary>
	public string? Role { get; set; }

	/// <summary>
	/// For a property namespace: <see cref="Lowest"/> makes its tags a scale, in the order the palette lists
	/// them, of which a unit keeps the lowest (cloth of fine wool in a common dye is common cloth). Null keeps them all.
	/// </summary>
	public string? Combine { get; set; }

	/// <summary>The namespace's symbol: what its tags show in a formula unless they have their own.</summary>
	public SpriteRef? Sign { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }
}
