using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>
/// A tag prefix (the <c>kind</c> of <c>kind:metal</c>), what it means, and the part its tags play.
/// Core tags say what a good is, and are what slots and consumers accept: they decide a good's
/// place in the web. Variety tags say what is particular about it (<c>heart:arsenic</c>,
/// <c>grain:rye</c>): they ride from an input to the output through any slot that passes
/// variety on, so one recipe yields as many varieties as its inputs allow. The rest describe.
/// </summary>
public sealed class TagNamespace
{
	public const string Core = "core", Variety = "variety";

	public string Id { get; set; } = "";
	public string Note { get; set; } = "";

	/// <summary><see cref="Core"/>, <see cref="Variety"/>, or null for a namespace that only describes.</summary>
	public string? Role { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }
}
