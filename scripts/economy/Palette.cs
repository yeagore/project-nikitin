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
		foreach (TagDef def in Tags) counts.TryAdd(def.Id, 0);
		foreach (Good good in Goods)
			foreach (string tag in good.AllTags().Distinct())
				counts[tag] = counts.GetValueOrDefault(tag) + 1;
		return counts.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value)).ToList();
	}

	public IEnumerable<Good> GoodsWith(string tag) => Goods.Where(g => g.Tags.Contains(tag));

	/// <summary>The part of a tag before its colon, or "" for a tag with none.</summary>
	public static string NamespaceOf(string tag)
	{
		int colon = tag.IndexOf(':');
		return colon <= 0 ? "" : tag[..colon];
	}

	public TagNamespace? Namespace(string id) => TagNamespaces.FirstOrDefault(n => n.Id == id);

	/// <summary>True for a tag whose namespace is marked as a variety namespace: it rides along from inputs to outputs.</summary>
	public bool IsVariety(string tag) => Namespace(NamespaceOf(tag))?.Role == TagNamespace.Variety;

	/// <summary>True for a tag whose namespace is marked core: the kind of tag slots and consumers are meant to accept.</summary>
	public bool IsCore(string tag) => Namespace(NamespaceOf(tag))?.Role == TagNamespace.Core;

	/// <summary>The tag's own note, or failing that the note of its namespace.</summary>
	public string NoteFor(string tag)
	{
		string? own = Tags.FirstOrDefault(t => t.Id == tag)?.Note;
		if (!string.IsNullOrEmpty(own)) return own;
		return Namespace(NamespaceOf(tag))?.Note ?? "";
	}
}
