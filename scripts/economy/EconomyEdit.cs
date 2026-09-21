using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ProjectNikitin.Economy;

/// <summary>
/// The changes a designer makes to a web or to the catalogue, as plain functions over the
/// data, so the lab's buttons stay thin and a script or a test can make the same changes.
/// None of them touches the disk.
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

	public static string FreeGoodId(Catalogue catalogue, string name) =>
		Free(Slug(name), id => catalogue.Find(id) != null);

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

	/// <summary>
	/// Brings goods into <paramref name="to"/> with everything upstream of them as <paramref name="from"/>
	/// has it: the recipes that make them, those recipes' inputs, and so on down to the ground. A tag
	/// slot brings every good of the source web that carries the tag. Optional slots come only when
	/// asked; a recipe already present (by id) is left alone. Positions are copied, shifted by
	/// <paramref name="shift"/>. Returns the keys of the nodes that arrived.
	/// </summary>
	public static List<string> CopyChain(Catalogue catalogue, EconomyWeb from, EconomyWeb to,
	                                     IEnumerable<string> goodIds, bool withOptional, Spot shift = default)
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
			if (!walked.Add(goodId) || catalogue.Find(goodId) == null) continue;
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
						foreach (string carrier in from.Goods.Where(g => catalogue.Find(g)?.Has(Acceptor.TagOf(acceptor)) == true))
							queue.Enqueue(carrier);
				}
			}
		}
		return arrived;
	}

	/// <summary>
	/// A new web cut from another: the chosen goods, everything upstream of them, and the
	/// consumers that anything in the cut still reaches. How a vertical slice is made from the full ledger.
	/// </summary>
	public static EconomyWeb Cut(Catalogue catalogue, EconomyWeb from, IEnumerable<string> goodIds, string id, string name, bool withOptional)
	{
		var web = new EconomyWeb { Id = id, Name = name };
		CopyChain(catalogue, from, web, goodIds, withOptional);

		foreach (Consumer consumer in from.Consumers)
		{
			Consumer copy = EconomyStore.Clone(consumer);
			copy.Accepts.RemoveAll(a => !Acceptor.IsTag(a) && !web.Goods.Contains(a));
			bool reached = copy.Accepts.Any(a => web.Goods.Any(g => catalogue.Find(g) is { } good && Acceptor.Admits(a, good)));
			if (!reached) continue;
			web.Consumers.Add(copy);
			if (from.Layout.TryGetValue(copy.Id, out Spot spot)) web.Layout[copy.Id] = spot;
		}

		// Catalogue order, so two cuts of the same goods are the same file.
		var order = new Dictionary<string, int>(StringComparer.Ordinal);
		for (int i = 0; i < catalogue.Goods.Count; i++) order[catalogue.Goods[i].Id] = i;
		web.Goods = web.Goods.OrderBy(g => order.GetValueOrDefault(g, int.MaxValue)).ToList();
		return web;
	}

	// ---- tags ----------------------------------------------------------------

	/// <summary>Renames a tag on every good and in the tag list. The webs need <see cref="RenameTag(EconomyWeb,string,string)"/> too.</summary>
	public static void RenameTag(Catalogue catalogue, string from, string to)
	{
		foreach (Good good in catalogue.Goods)
		{
			int at = good.Tags.IndexOf(from);
			if (at < 0) continue;
			if (good.Tags.Contains(to)) good.Tags.RemoveAt(at);
			else good.Tags[at] = to;
		}
		TagDef? def = catalogue.Tags.FirstOrDefault(t => t.Id == from);
		if (def == null) return;
		if (catalogue.Tags.Any(t => t.Id == to)) catalogue.Tags.Remove(def);
		else def.Id = to;
	}

	/// <summary>Renames a tag wherever a slot or a consumer of the web accepts it; true if anything changed.</summary>
	public static bool RenameTag(EconomyWeb web, string from, string to)
	{
		bool changed = false;
		foreach (List<string> accepts in AcceptLists(web))
		{
			int at = accepts.IndexOf(Acceptor.ForTag(from));
			if (at < 0) continue;
			changed = true;
			if (accepts.Contains(Acceptor.ForTag(to))) accepts.RemoveAt(at);
			else accepts[at] = Acceptor.ForTag(to);
		}
		return changed;
	}

	public static void RemoveTag(Catalogue catalogue, string tag)
	{
		foreach (Good good in catalogue.Goods) good.Tags.Remove(tag);
		catalogue.Tags.RemoveAll(t => t.Id == tag);
	}

	public static bool RemoveTag(EconomyWeb web, string tag)
	{
		bool changed = false;
		foreach (List<string> accepts in AcceptLists(web))
			changed |= accepts.Remove(Acceptor.ForTag(tag));
		return changed;
	}

	/// <summary>True if any slot or consumer of the web accepts the tag.</summary>
	public static bool UsesTag(EconomyWeb web, string tag) =>
		AcceptLists(web).Any(a => a.Contains(Acceptor.ForTag(tag)));

	private static IEnumerable<List<string>> AcceptLists(EconomyWeb web) =>
		web.Recipes.SelectMany(r => r.Inputs).Select(s => s.Accepts).Concat(web.Consumers.Select(c => c.Accepts));
}
