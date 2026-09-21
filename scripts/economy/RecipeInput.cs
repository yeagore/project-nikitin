using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>
/// An input slot of a recipe. Any one of its acceptors fills it (see <see cref="Acceptor"/>):
/// a good by id, or a tag, which admits whatever carries it. A golem's heart slot accepts
/// <c>#kind:golem-heart</c>, so a new heart needs the tag and nothing else.
/// </summary>
public sealed class RecipeInput
{
	public List<string> Accepts { get; set; } = new();

	/// <summary>An upgrade or a variant: the recipe runs without it.</summary>
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool Optional { get; set; }

	/// <summary>
	/// The variety of what fills this slot passes on to the output: its variety tags are stamped on
	/// what the recipe makes, so the same recipe fed an arsenic heart makes an arsenic-hearted golem.
	/// </summary>
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool Passes { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }
}
