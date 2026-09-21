using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectNikitin.Economy;

/// <summary>
/// The elemental association of a recipe, in the spirit of alchemy. Two questions sort a craft:
/// does it put things together or take a thing apart, and does it do so with violence or with
/// patience. Fire is violent synthesis (smelting, burning), wind violent analysis (milling,
/// crushing), water gentle synthesis (mixing, assembling), earth gentle analysis (slow rendering,
/// fermentation, spinning wool into thread). Quintessence is pure magic, like animating a golem.
/// A classification by feel and not a law: nothing reads it yet, and a web does well to keep the
/// main four in rough balance with a little quintessence on top.
/// </summary>
public sealed record Element(string Id, string Name, string Gloss, int Cell)
{
	public static readonly Element Fire = new("fire", "Fire", "violent synthesis: smelting, burning, firing", 0);
	public static readonly Element Wind = new("wind", "Wind", "violent analysis: milling, crushing, distilling", 1);
	public static readonly Element Water = new("water", "Water", "gentle synthesis: mixing, assembling, weaving", 2);
	public static readonly Element Earth = new("earth", "Earth", "gentle analysis: fermenting, curing, spinning", 3);
	public static readonly Element Quintessence = new("qe", "Quintessence", "pure magic: animation, enchantment", 4);

	/// <summary>The five, in the order their icons sit on the sheet (<c>sprites/elements.png</c>, 16 px cells in a row).</summary>
	public static readonly IReadOnlyList<Element> All = new[] { Fire, Wind, Water, Earth, Quintessence };

	/// <summary>The sheet of element icons, relative to the economy folder. The elements belong to the system, not to a palette.</summary>
	public const string Sheet = "sprites/elements.png";

	public static Element? Find(string? id) => id == null ? null : All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));
}
