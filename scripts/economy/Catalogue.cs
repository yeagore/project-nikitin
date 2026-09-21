using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectNikitin.Economy;

/// <summary>
/// Every good there is, with the tags' notes and the sprite atlases: the palette
/// the webs are painted from. One file, <c>catalogue.json</c>, shared by all webs,
/// so a good renamed or redrawn is renamed and redrawn everywhere.
/// </summary>
public sealed class Catalogue
{
	public int Format { get; set; } = 1;
	public string Title { get; set; } = "";
	public string Note { get; set; } = "";
	public List<AtlasDef> Atlases { get; set; } = new();

	/// <summary>What each tag prefix means (<c>kind</c>, <c>need</c>, <c>trait</c>).</summary>
	public List<TagDef> TagNamespaces { get; set; } = new();

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

	/// <summary>Every tag some good carries or the tag list names, sorted; the count is how many goods carry it.</summary>
	public List<(string Tag, int Count)> TagsInUse()
	{
		var counts = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (TagDef def in Tags) counts.TryAdd(def.Id, 0);
		foreach (Good good in Goods)
			foreach (string tag in good.Tags)
				counts[tag] = counts.GetValueOrDefault(tag) + 1;
		return counts.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value)).ToList();
	}

	public IEnumerable<Good> GoodsWith(string tag) => Goods.Where(g => g.Tags.Contains(tag));

	/// <summary>The tag's own note, or failing that the note of its namespace.</summary>
	public string NoteFor(string tag)
	{
		string? own = Tags.FirstOrDefault(t => t.Id == tag)?.Note;
		if (!string.IsNullOrEmpty(own)) return own;
		int colon = tag.IndexOf(':');
		if (colon <= 0) return "";
		string space = tag[..colon];
		return TagNamespaces.FirstOrDefault(n => n.Id == space)?.Note ?? "";
	}
}
