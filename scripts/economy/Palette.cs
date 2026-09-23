using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>
/// A web's own goods, tags and sprite sheets: what its canvas is painted from. Every web
/// carries its palette inside its file, so an experiment in one web cannot touch another;
/// goods travel between webs only by being imported, which copies them.
/// </summary>
public sealed class Palette
{
	public List<AtlasDef> Atlases { get; set; } = new();

	/// <summary>The tag prefixes (<c>kind</c>, <c>need</c>, <c>heart</c>), what each means, and its role.</summary>
	public List<TagNamespace> TagNamespaces { get; set; } = new();

	/// <summary>The tags with a note of their own. A tag can be in use without being listed here.</summary>
	public List<TagDef> Tags { get; set; } = new();

	public List<Good> Goods { get; set; } = new();

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? Extra { get; set; }

	private Dictionary<string, Good>? _byId;
	private Dictionary<string, TagDef>? _tagById;
	private Dictionary<string, int>? _tagRank;
	private Dictionary<string, TagNamespace>? _nsById;
	private int _tagsIndexed = -1, _spacesIndexed = -1;

	/// <summary>The good with that id, or null. The index rebuilds itself when the list has changed length.</summary>
	public Good? Find(string id)
	{
		if (_byId == null || _byId.Count != Goods.Count) Reindex();
		return _byId!.TryGetValue(id, out Good? good) ? good : null;
	}

	/// <summary>Call after replacing a good in place; adding and removing are noticed without it.</summary>
	public void Reindex()
	{
		_byId = new Dictionary<string, Good>(Goods.Count, StringComparer.Ordinal);
		foreach (Good good in Goods) _byId[good.Id] = good;
		_tagById = null;
		_nsById = null;
	}

	public void Add(Good good)
	{
		Goods.Add(good);
		_byId = null;
	}

	public bool Remove(string id)
	{
		int removed = Goods.RemoveAll(g => g.Id == id);
		_byId = null;
		return removed > 0;
	}

	public AtlasDef? Atlas(string id) => Atlases.FirstOrDefault(a => a.Id == id);

	/// <summary>
	/// Every tag some good or variety carries or the tag list names, sorted; the count is how many goods carry it
	/// (a good counts once, whether the tag is its own or one of its varieties').
	/// </summary>
	public List<(string Tag, int Count)> TagsInUse()
	{
		var counts = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (TagDef def in Tags)
		{
			counts.TryAdd(def.Id, 0);
			foreach (string implied in def.Implies ?? (IEnumerable<string>)Array.Empty<string>()) counts.TryAdd(implied, 0);
		}
		foreach (Good good in Goods)
			foreach (string tag in good.AllTags().Distinct())
				counts[tag] = counts.GetValueOrDefault(tag) + 1;
		return counts.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value)).ToList();
	}

	/// <summary>The goods that carry the tag, on themselves or on one of their authored varieties.</summary>
	public IEnumerable<Good> GoodsWith(string tag) => Goods.Where(g => g.Tags.Contains(tag) || g.VarietyList.Any(v => v.Tags.Contains(tag)));

	/// <summary>The part of a tag before its colon, or "" for a tag with none.</summary>
	public static string NamespaceOf(string tag)
	{
		int colon = tag.IndexOf(':');
		return colon <= 0 ? "" : tag[..colon];
	}

	/// <summary>The namespace's entry, or null. Indexed, since the variety walk asks per tag; <see cref="Reindex"/> after renaming one in place.</summary>
	public TagNamespace? Namespace(string id)
	{
		if (_nsById == null || _spacesIndexed != TagNamespaces.Count)
		{
			_nsById = new Dictionary<string, TagNamespace>(TagNamespaces.Count, StringComparer.Ordinal);
			foreach (TagNamespace ns in TagNamespaces) _nsById.TryAdd(ns.Id, ns);
			_spacesIndexed = TagNamespaces.Count;
		}
		return _nsById.TryGetValue(id, out TagNamespace? found) ? found : null;
	}

	/// <summary>True for a tag whose namespace is marked as a variety namespace: it rides along from inputs to outputs.</summary>
	public bool IsVariety(string tag) => Namespace(NamespaceOf(tag))?.Role == TagNamespace.Variety;

	/// <summary>True for a tag whose namespace is marked core: the kind of tag slots and consumers are meant to accept.</summary>
	public bool IsCore(string tag) => Namespace(NamespaceOf(tag))?.Role == TagNamespace.Core;

	/// <summary>True for a tag whose namespace is marked property: what variety tags imply, and what units stack by.</summary>
	public bool IsProperty(string tag) => Namespace(NamespaceOf(tag))?.Role == TagNamespace.Property;

	/// <summary>True when the tag's namespace names ground or a place a recipe stands on (<c>anchor:river</c>).</summary>
	public bool IsSite(string tag) => Namespace(NamespaceOf(tag))?.Role == TagNamespace.Site;

	/// <summary>The tag's entry in the tag list, or null for a tag that is in use without one. The index rebuilds itself when the list has changed length; <see cref="Reindex"/> after a rename.</summary>
	public TagDef? Tag(string id)
	{
		// Counted against the list's length as seen when indexed, not the index's own size: two entries with one id (a hand-edited file) must not rebuild it on every call.
		if (_tagById == null || _tagsIndexed != Tags.Count)
		{
			_tagById = new Dictionary<string, TagDef>(Tags.Count, StringComparer.Ordinal);
			_tagRank = new Dictionary<string, int>(Tags.Count, StringComparer.Ordinal);
			for (int i = 0; i < Tags.Count; i++)
			{
				if (_tagById.TryAdd(Tags[i].Id, Tags[i])) _tagRank[Tags[i].Id] = i;
			}
			_tagsIndexed = Tags.Count;
		}
		return _tagById.TryGetValue(id, out TagDef? def) ? def : null;
	}

	/// <summary>Where the tag stands in the tag list: the order of a scale (<see cref="TagNamespace.Lowest"/>). Unlisted tags come last, in ordinal order among themselves.</summary>
	public (int Rank, string Tag) RankOf(string tag) => (Tag(tag) != null ? _tagRank![tag] : int.MaxValue, tag);

	/// <summary>The tag's colour as <c>#RRGGBB</c>, or null.</summary>
	public string? ColourOf(string tag) => Tag(tag)?.Colour;

	/// <summary>The tag's symbol: its own, or failing that its namespace's.</summary>
	public SpriteRef? SignOf(string tag) => Tag(tag)?.Sign ?? Namespace(NamespaceOf(tag))?.Sign;

	/// <summary>The property tags a variety tag implies; none for any other tag.</summary>
	public IReadOnlyList<string> ImpliedBy(string tag) => Tag(tag)?.Implies ?? (IReadOnlyList<string>)Array.Empty<string>();

	/// <summary>
	/// What a unit carrying these variety tags stacks as: the property tags they imply, and of a
	/// namespace that is a scale (<see cref="TagNamespace.Lowest"/>) only the lowest. Sorted.
	/// </summary>
	public List<string> PropertiesOf(IEnumerable<string> varietyTags)
	{
		var kept = new SortedSet<string>(StringComparer.Ordinal);
		var lowest = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (string tag in varietyTags)
			foreach (string property in IsProperty(tag) ? new[] { tag } : ImpliedBy(tag))
			{
				string space = NamespaceOf(property);
				if (Namespace(space)?.Combine != TagNamespace.Lowest) kept.Add(property);
				else if (!lowest.TryGetValue(space, out string? held) || RankOf(property).CompareTo(RankOf(held)) < 0) lowest[space] = property;
			}
		kept.UnionWith(lowest.Values);
		return kept.ToList();
	}

	/// <summary>The tag's own note, or failing that the note of its namespace.</summary>
	public string NoteFor(string tag)
	{
		string? own = Tag(tag)?.Note;
		if (!string.IsNullOrEmpty(own)) return own;
		return Namespace(NamespaceOf(tag))?.Note ?? "";
	}
}
