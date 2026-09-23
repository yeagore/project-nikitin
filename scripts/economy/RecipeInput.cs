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

	/// <summary>How many units one run of the recipe takes from this slot; null means one. Read through <see cref="Count"/>.</summary>
	public double? Amount { get; set; }

	/// <summary>The units one run takes, never null.</summary>
	[JsonIgnore]
	public double Count => Amount ?? 1;

	/// <summary>An upgrade or a variant: the recipe runs without it.</summary>
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool Optional { get; set; }

	/// <summary>
	/// What an optional slot adds when it is filled: every output of the run comes out this much
	/// greater, as a fraction (0.3 is three tenths more), in proportion to how full the slot is.
	/// Dung on the fields. Null for none; read through <see cref="Bonus"/>. Meaningless on a
	/// required slot, which is always filled: there the outputs themselves say it.
	/// </summary>
	public double? Boost { get; set; }

	/// <summary>The boost, never null.</summary>
	[JsonIgnore]
	public double Bonus => Boost ?? 0;

	/// <summary>
	/// The variety of what fills this slot passes on to the output: its variety tags are stamped on
	/// what the recipe makes, so the same recipe fed an arsenic heart makes an arsenic-hearted golem.
	/// </summary>
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool Passes { get; set; }

	/// <summary>
	/// Variety tags this slot stamps on the output whenever it is filled, whatever fills it. For when
	/// the effect belongs to the combination and not to the ingredient: clockwork in a golem's hands
	/// slot makes a golem with <c>fit:clockwork-hands</c>, while Clockwork itself stays plain clockwork
	/// for the clockmaker. Null when the slot grants nothing; read through <see cref="GrantList"/>.
	/// </summary>
	public List<string>? Grants { get; set; }

	/// <summary>The granted tags, never null.</summary>
	[JsonIgnore]
	public IReadOnlyList<string> GrantList => Grants ?? (IReadOnlyList<string>)System.Array.Empty<string>();

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }
}
