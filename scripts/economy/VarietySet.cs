using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectNikitin.Economy;

/// <summary>
/// The varieties of one good as a web can make them: each a sorted set of variety tags, the
/// plain good being the empty set. Kept whole up to <see cref="Cap"/> varieties; past that the
/// list stops growing and <see cref="Count"/> carries on as a floor, since a golem of seventeen
/// soils, three hearts and four optional fittings is already 816 and nobody reads that list.
/// </summary>
public sealed class VarietySet
{
	/// <summary>Varieties listed in full; beyond this only the count is kept.</summary>
	public const int Cap = 512;

	public static readonly VarietySet Plain = new(new List<IReadOnlyList<string>> { Array.Empty<string>() }, 1, false);

	/// <summary>Each variety's tags, sorted; the varieties themselves sorted too, so the same web reads the same.</summary>
	public IReadOnlyList<IReadOnlyList<string>> Sets { get; }

	/// <summary>How many varieties there are; an estimate (the product of the choices) when <see cref="Capped"/>.</summary>
	public long Count { get; }

	public bool Capped { get; }

	/// <summary>True when there is one variety and it is the plain good.</summary>
	public bool IsPlain => Count == 1 && Sets.Count == 1 && Sets[0].Count == 0;

	private VarietySet(List<IReadOnlyList<string>> sets, long count, bool capped)
	{
		Sets = sets;
		Count = count;
		Capped = capped;
	}

	/// <summary>Every tag that appears on some variety, by namespace, in first-seen order.</summary>
	public List<(string Namespace, List<string> Tags)> ByNamespace()
	{
		var spaces = new List<(string, List<string>)>();
		foreach (string tag in Sets.SelectMany(s => s).Distinct())
		{
			string space = Palette.NamespaceOf(tag);
			int at = spaces.FindIndex(p => p.Item1 == space);
			if (at < 0) spaces.Add((space, new List<string> { tag }));
			else spaces[at].Item2.Add(tag);
		}
		return spaces;
	}

	/// <summary>Collects varieties without repeats, and multiplies two collections together.</summary>
	internal sealed class Builder
	{
		private readonly Dictionary<string, IReadOnlyList<string>> _sets = new(StringComparer.Ordinal);
		private long _estimate;
		private bool _capped;

		public int Count => _sets.Count;

		public void Add(IEnumerable<string> tags)
		{
			List<string> sorted = tags.Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList();
			string key = string.Join("\n", sorted);
			if (_sets.ContainsKey(key)) return;
			if (_sets.Count < Cap) _sets[key] = sorted;
			else _capped = true;
		}

		public void Add(VarietySet other)
		{
			foreach (IReadOnlyList<string> set in other.Sets) Add(set);
			if (!other.Capped) return;
			_capped = true;
			_estimate += other.Count;
		}

		public void Add(Builder other) => Add(other.Build());

		/// <summary>Every variety here joined with every variety there; past the cap, only their number.</summary>
		public Builder Times(Builder other)
		{
			var product = new Builder();
			foreach (IReadOnlyList<string> a in _sets.Values)
			{
				foreach (IReadOnlyList<string> b in other._sets.Values)
				{
					product.Add(a.Concat(b));
					if (product._capped) break;
				}
				if (product._capped) break;
			}
			if (_capped || other._capped || product._capped)
			{
				product._capped = true;
				product._estimate = Multiply(Math.Max(Size, 1), Math.Max(other.Size, 1));
			}
			return product;
		}

		/// <summary>How many varieties this holds, by estimate once the list has been cut short.</summary>
		private long Size => _capped ? Math.Max(_estimate, _sets.Count) : _sets.Count;

		private static long Multiply(long a, long b)
		{
			try
			{
				return checked(a * b);
			}
			catch (OverflowException)
			{
				return long.MaxValue;
			}
		}

		public VarietySet Build()
		{
			List<IReadOnlyList<string>> sets = _sets.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value).ToList();
			if (sets.Count == 0) return Plain;
			return new VarietySet(sets, Size, _capped);
		}
	}
}
