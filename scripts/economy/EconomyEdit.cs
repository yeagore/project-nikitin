using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ProjectNikitin.Economy;

/// <summary>
/// The changes a designer makes to a web and its palette, as plain functions over the data,
/// so the lab's buttons stay thin and a script or a test can make the same changes. None of
/// them touches the disk, and none reaches outside the web it is given: goods cross from
/// one web to another only by <see cref="ImportGoods"/> and <see cref="CopyChain"/>, which copy.
/// </summary>
public static class EconomyEdit
{
	/// <summary>Lowercase letters, digits and hyphens: what an id or a file name may be.</summary>
	public static string Slug(string name)
	{
		var slug = new StringBuilder();
		foreach (char c in name.Trim().ToLowerInvariant())
		{
			if (c is >= 'a' and <= 'z' or >= '0' and <= '9') slug.Append(c);
			else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
		}
		return slug.ToString().Trim('-');
	}

	/// <summary>The first of <c>stem</c>, <c>stem-2</c>, <c>stem-3</c>… that <paramref name="taken"/> does not claim.</summary>
	public static string Free(string stem, Func<string, bool> taken, string joint = "-")
	{
		if (stem.Length == 0) stem = "new";
		if (!taken(stem)) return stem;
		for (int n = 2; ; n++)
			if (!taken(stem + joint + n)) return stem + joint + n;
	}

	public static string FreeGoodId(Palette palette, string name) =>
		Free(Slug(name), id => palette.Find(id) != null);

	public static string FreeRecipeId(EconomyWeb web, string hint) =>
		Free("r." + (hint.Length > 0 ? hint : "new"), id => web.Recipe(id) != null, ".");

	public static string FreeConsumerId(EconomyWeb web, string name) =>
		Free("c." + Slug(name), id => web.Consumer(id) != null, ".");

	// ---- the web -------------------------------------------------------------

	public static bool AddGood(EconomyWeb web, string goodId, Spot? at = null)
	{
		if (web.Goods.Contains(goodId)) return false;
		web.Goods.Add(goodId);
		if (at.HasValue) web.Layout[goodId] = at.Value;
		return true;
	}

	/// <summary>
	/// Takes a good out of the web. A recipe that made only that good goes with it, since it
	/// has nothing left to make; a slot that accepted only that good stays, empty, for the
	/// issue list to name, because a recipe that silently needs less would be a lie.
	/// </summary>
	public static void RemoveGood(EconomyWeb web, string goodId)
	{
		web.Goods.Remove(goodId);
		web.Layout.Remove(goodId);
		foreach (Recipe recipe in web.Recipes.ToList())
		{
			bool madeIt = recipe.Makes(goodId);
			recipe.Outputs.RemoveAll(o => o.Good == goodId);
			if (madeIt && recipe.Outputs.Count == 0) RemoveRecipe(web, recipe.Id);
			else foreach (RecipeInput slot in recipe.Inputs) slot.Accepts.Remove(goodId);
		}
		foreach (Consumer consumer in web.Consumers) consumer.Accepts.Remove(goodId);
	}

	public static void RemoveRecipe(EconomyWeb web, string recipeId)
	{
		web.Recipes.RemoveAll(r => r.Id == recipeId);
		web.Layout.Remove(recipeId);
	}

	public static void RemoveConsumer(EconomyWeb web, string consumerId)
	{
		web.Consumers.RemoveAll(c => c.Id == consumerId);
		web.Layout.Remove(consumerId);
	}

	public static Recipe NewRecipe(EconomyWeb web, string? makes, string? takes, Spot at)
	{
		var recipe = new Recipe { Id = FreeRecipeId(web, makes ?? "") };
		if (makes != null) recipe.Outputs.Add(new RecipeOutput { Good = makes });
		if (takes != null) recipe.Inputs.Add(new RecipeInput { Accepts = { takes } });
		web.Recipes.Add(recipe);
		web.Layout[recipe.Id] = at;
		return recipe;
	}

	public static Consumer NewConsumer(EconomyWeb web, string name, Spot at)
	{
		var consumer = new Consumer { Id = FreeConsumerId(web, name), Name = name };
		web.Consumers.Add(consumer);
		web.Layout[consumer.Id] = at;
		return consumer;
	}

	/// <summary>Adds an acceptor to a slot's or a consumer's list unless it is there already.</summary>
	public static bool Accept(List<string> accepts, string acceptor)
	{
		if (accepts.Contains(acceptor)) return false;
		accepts.Add(acceptor);
		return true;
	}

	/// <summary>Takes a good off the canvas and out of the palette: it is gone from this web altogether.</summary>
	public static void DeleteGood(EconomyWeb web, string goodId)
	{
		RemoveGood(web, goodId);
		web.Palette.Remove(goodId);
	}

	// ---- between webs ----------------------------------------------------------

	/// <summary>
	/// Copies goods from another web's palette into this one's, with the notes of the tags they
	/// carry, the namespaces (and their roles) of those tags, and the sprite sheets they point at.
	/// A good this palette already has is left as it is, unless <paramref name="overwrite"/>.
	/// They land in the palette only; the canvas is not touched. Returns how many arrived.
	/// </summary>
	public static int ImportGoods(EconomyWeb from, EconomyWeb to, IEnumerable<string> goodIds, bool overwrite = false)
	{
		int arrived = 0;
		foreach (string id in goodIds)
		{
			Good? theirs = from.Palette.Find(id);
			if (theirs == null) continue;
			Good? ours = to.Palette.Find(id);
			if (ours != null && !overwrite) continue;

			Good copy = EconomyStore.Clone(theirs);
			if (ours == null) to.Palette.Add(copy);
			else
			{
				to.Palette.Goods[to.Palette.Goods.IndexOf(ours)] = copy;
				to.Palette.Reindex();
			}
			arrived++;

			foreach (string tag in copy.AllTags().Distinct())
			{
				if (to.Palette.Tags.All(t => t.Id != tag) && from.Palette.Tags.FirstOrDefault(t => t.Id == tag) is { } def)
					to.Palette.Tags.Add(EconomyStore.Clone(def));
				string space = Palette.NamespaceOf(tag);
				if (space.Length > 0 && to.Palette.Namespace(space) == null && from.Palette.Namespace(space) is { } ns)
					to.Palette.TagNamespaces.Add(EconomyStore.Clone(ns));
			}
			foreach (SpriteRef? sprite in new[] { copy.Icon, copy.Sign })
				if (sprite?.Atlas is { } atlas && to.Palette.Atlas(atlas) == null && from.Palette.Atlas(atlas) is { } sheet)
					to.Palette.Atlases.Add(EconomyStore.Clone(sheet));
		}
		return arrived;
	}

	/// <summary>The whole of another web's palette: every good, every tag note, every namespace, every sprite sheet.</summary>
	public static int ImportPalette(EconomyWeb from, EconomyWeb to, bool overwrite = false)
	{
		int arrived = ImportGoods(from, to, from.Palette.Goods.Select(g => g.Id).ToList(), overwrite);
		foreach (TagDef def in from.Palette.Tags.Where(d => to.Palette.Tags.All(t => t.Id != d.Id)))
			to.Palette.Tags.Add(EconomyStore.Clone(def));
		foreach (TagNamespace ns in from.Palette.TagNamespaces.Where(n => to.Palette.Namespace(n.Id) == null))
			to.Palette.TagNamespaces.Add(EconomyStore.Clone(ns));
		foreach (AtlasDef sheet in from.Palette.Atlases.Where(a => to.Palette.Atlas(a.Id) == null))
			to.Palette.Atlases.Add(EconomyStore.Clone(sheet));
		return arrived;
	}

	/// <summary>
	/// Brings goods onto <paramref name="to"/>'s canvas with everything upstream of them as <paramref name="from"/>
	/// has it: the recipes that make them, those recipes' inputs, and so on down to the ground. Goods the
	/// palette lacks are imported on the way. A tag slot brings every good of the source web that carries
	/// the tag. Optional slots come only when asked; a recipe already present (by id) is left alone.
	/// Positions are copied, shifted by <paramref name="shift"/>. Returns the keys of the nodes that arrived.
	/// </summary>
	public static List<string> CopyChain(EconomyWeb from, EconomyWeb to, IEnumerable<string> goodIds, bool withOptional, Spot shift = default)
	{
		var arrived = new List<string>();
		var walked = new HashSet<string>(StringComparer.Ordinal);
		var queue = new Queue<string>(goodIds);

		void Place(string key)
		{
			arrived.Add(key);
			if (from.Layout.TryGetValue(key, out Spot spot)) to.Layout[key] = new Spot(spot.X + shift.X, spot.Y + shift.Y);
		}

		while (queue.Count > 0)
		{
			string goodId = queue.Dequeue();
			if (!walked.Add(goodId)) continue;
			if (to.Palette.Find(goodId) == null) ImportGoods(from, to, new[] { goodId });
			if (to.Palette.Find(goodId) == null) continue;
			if (AddGood(to, goodId)) Place(goodId);

			foreach (Recipe recipe in from.Recipes.Where(r => r.Makes(goodId)))
			{
				if (to.Recipe(recipe.Id) != null) continue;
				Recipe copy = EconomyStore.Clone(recipe);
				if (!withOptional) copy.Inputs.RemoveAll(s => s.Optional);
				to.Recipes.Add(copy);
				Place(copy.Id);

				foreach (RecipeOutput output in copy.Outputs) queue.Enqueue(output.Good);
				foreach (string acceptor in copy.Inputs.SelectMany(s => s.Accepts))
				{
					if (!Acceptor.IsTag(acceptor)) queue.Enqueue(acceptor);
					else
						foreach (string carrier in from.Goods.Where(g => from.Palette.Find(g)?.Has(Acceptor.TagOf(acceptor)) == true))
							queue.Enqueue(carrier);
				}
			}
		}
		return arrived;
	}

	/// <summary>
	/// A new web cut from another: the chosen goods, everything upstream of them, and the consumers
	/// that anything in the cut still reaches. Its palette is the source's whole palette, or with
	/// <paramref name="leanPalette"/> only the goods the cut uses. How a vertical slice is made from the full ledger.
	/// </summary>
	public static EconomyWeb Cut(EconomyWeb from, IEnumerable<string> goodIds, string id, string name, bool withOptional, bool leanPalette)
	{
		var web = new EconomyWeb { Id = id, Name = name };
		if (!leanPalette) ImportPalette(from, web);
		CopyChain(from, web, goodIds, withOptional);

		foreach (Consumer consumer in from.Consumers)
		{
			Consumer copy = EconomyStore.Clone(consumer);
			copy.Accepts.RemoveAll(a => !Acceptor.IsTag(a) && !web.Goods.Contains(a));
			bool reached = copy.Accepts.Any(a => web.Goods.Any(g => web.Palette.Find(g) is { } good && Acceptor.Admits(a, good)));
			if (!reached) continue;
			web.Consumers.Add(copy);
			if (from.Layout.TryGetValue(copy.Id, out Spot spot)) web.Layout[copy.Id] = spot;
		}

		// The source's order, so two cuts of the same goods are the same file.
		var order = new Dictionary<string, int>(StringComparer.Ordinal);
		for (int i = 0; i < from.Palette.Goods.Count; i++) order[from.Palette.Goods[i].Id] = i;
		web.Goods = web.Goods.OrderBy(g => order.GetValueOrDefault(g, int.MaxValue)).ToList();
		web.Palette.Goods = web.Palette.Goods.OrderBy(g => order.GetValueOrDefault(g.Id, int.MaxValue)).ToList();
		web.Palette.Reindex();
		return web;
	}

	// ---- tags ----------------------------------------------------------------

	/// <summary>Renames a tag everywhere in the web: on its goods and their varieties, in the tag list, wherever a slot or a consumer accepts it, and wherever a slot grants it, a recipe's site asks for it, a variety tag implies it or an icon layer answers to it.</summary>
	public static void RenameTag(EconomyWeb web, string from, string to)
	{
		foreach (List<string> tags in TagLists(web.Palette)) Swap(tags, from, to);
		TagDef? def = web.Palette.Tags.FirstOrDefault(t => t.Id == from);
		if (def != null)
		{
			if (web.Palette.Tags.Any(t => t.Id == to)) web.Palette.Tags.Remove(def);
			else def.Id = to;
		}
		foreach (List<string> accepts in AcceptLists(web)) Swap(accepts, Acceptor.ForTag(from), Acceptor.ForTag(to));
		foreach (List<string> named in NamedLists(web)) Swap(named, from, to);
		foreach (IconLayer layer in web.Palette.Goods.Where(g => g.Layers != null).SelectMany(g => g.Layers!))
			if (layer.Match == from) layer.Match = to;
		web.Palette.Reindex();
	}

	/// <summary>Takes a tag off every good and variety, out of the tag list, and out of every slot and consumer that accepted it.</summary>
	public static void RemoveTag(EconomyWeb web, string tag)
	{
		foreach (List<string> tags in TagLists(web.Palette)) tags.Remove(tag);
		web.Palette.Tags.RemoveAll(t => t.Id == tag);
		foreach (List<string> accepts in AcceptLists(web)) accepts.Remove(Acceptor.ForTag(tag));
		foreach (RecipeInput slot in web.Recipes.SelectMany(r => r.Inputs).Where(i => i.Grants != null))
		{
			slot.Grants!.Remove(tag);
			if (slot.Grants.Count == 0) slot.Grants = null;
		}
		foreach (Recipe recipe in web.Recipes.Where(r => r.Site != null))
		{
			recipe.Site!.Remove(tag);
			if (recipe.Site.Count == 0) recipe.Site = null;
		}
		foreach (TagDef def in web.Palette.Tags.Where(t => t.Implies != null))
		{
			def.Implies!.Remove(tag);
			if (def.Implies.Count == 0) def.Implies = null;
		}
		foreach (Good good in web.Palette.Goods.Where(g => g.Layers != null))
		{
			good.Layers!.RemoveAll(l => l.Match == tag);
			if (good.Layers.Count == 0) good.Layers = null;
		}
		web.Palette.Reindex();
	}

	/// <summary>Renames a namespace: its entry, and the prefix of every tag in it, wherever the tag appears.</summary>
	public static void RenameNamespace(EconomyWeb web, string from, string to)
	{
		if (from == to || to.Length == 0) return;
		string prefix = from + ":";
		List<string> tags = web.Palette.TagsInUse().Select(t => t.Tag).Concat(NamedLists(web).SelectMany(g => g)).Distinct()
			.Where(t => t.StartsWith(prefix, StringComparison.Ordinal)).ToList();
		foreach (string acceptor in AcceptLists(web).SelectMany(a => a).Where(Acceptor.IsTag).ToList())
			if (Acceptor.TagOf(acceptor).StartsWith(prefix, StringComparison.Ordinal) && !tags.Contains(Acceptor.TagOf(acceptor)))
				tags.Add(Acceptor.TagOf(acceptor));
		foreach (string tag in tags) RenameTag(web, tag, to + ":" + tag[prefix.Length..]);

		foreach (IconLayer layer in web.Palette.Goods.Where(g => g.Layers != null).SelectMany(g => g.Layers!))
			if (layer.Match == from) layer.Match = to;

		TagNamespace? ns = web.Palette.Namespace(from);
		if (ns == null) return;
		if (web.Palette.Namespace(to) != null) web.Palette.TagNamespaces.Remove(ns);
		else ns.Id = to;
		web.Palette.Reindex();
	}

	/// <summary>The namespace's entry, made if the palette has none yet.</summary>
	public static TagNamespace EnsureNamespace(Palette palette, string id)
	{
		TagNamespace? ns = palette.Namespace(id);
		if (ns != null) return ns;
		ns = new TagNamespace { Id = id };
		palette.TagNamespaces.Add(ns);
		return ns;
	}

	private static void Swap(List<string> list, string from, string to)
	{
		int at = list.IndexOf(from);
		if (at < 0) return;
		if (list.Contains(to)) list.RemoveAt(at);
		else list[at] = to;
	}

	private static IEnumerable<List<string>> GrantLists(EconomyWeb web) =>
		web.Recipes.SelectMany(r => r.Inputs).Where(i => i.Grants != null).Select(i => i.Grants!);

	/// <summary>The lists that name tags plainly, without the acceptor's hash: what slots grant, where recipes must stand, what variety tags imply.</summary>
	private static IEnumerable<List<string>> NamedLists(EconomyWeb web) =>
		GrantLists(web)
			.Concat(web.Recipes.Where(r => r.Site != null).Select(r => r.Site!))
			.Concat(web.Palette.Tags.Where(t => t.Implies != null).Select(t => t.Implies!));

	/// <summary>True if any recipe of the web must stand on the tag.</summary>
	public static bool SitesTag(EconomyWeb web, string tag) => web.Recipes.Any(r => r.SiteList.Contains(tag));

	/// <summary>True if any slot of the web grants the tag.</summary>
	public static bool GrantsTag(EconomyWeb web, string tag) => GrantLists(web).Any(g => g.Contains(tag));

	private static IEnumerable<List<string>> TagLists(Palette palette) =>
		palette.Goods.Select(g => g.Tags).Concat(palette.Goods.SelectMany(g => g.VarietyList).Select(v => v.Tags));

	/// <summary>True if any slot or consumer of the web accepts the tag.</summary>
	public static bool UsesTag(EconomyWeb web, string tag) =>
		AcceptLists(web).Any(a => a.Contains(Acceptor.ForTag(tag)));

	private static IEnumerable<List<string>> AcceptLists(EconomyWeb web) =>
		web.Recipes.SelectMany(r => r.Inputs).Select(s => s.Accepts).Concat(web.Consumers.Select(c => c.Accepts));
}
