using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectNikitin.Economy;

/// <summary>
/// Lays a web out left to right: the sources in the first column, each recipe one column
/// past its deepest input, each good one past the recipe that makes it, the consumers in a
/// column of their own at the far right. The sizes are the lab's node sizes near enough,
/// so a web can be arranged with no window open.
/// </summary>
public static class WebArrange
{
	// Measured off the lab's nodes at zoom 1; a node that has been drawn reports its own size instead.
	public const float GoodWidth = 210f, GoodHeight = 66f;
	public const float RecipeWidth = 200f, RecipeHead = 32f, RecipeRow = 22f;
	public const float ConsumerWidth = 190f, ConsumerHeight = 76f;

	/// <summary>
	/// Writes a position for every node of the web. <paramref name="sizeOf"/> may give a node's
	/// real size on screen; without it the constants above stand in.
	/// </summary>
	public static void Arrange(Catalogue catalogue, EconomyWeb web, Func<string, (float W, float H)?>? sizeOf = null)
	{
		WebAnalysis analysis = WebAnalysis.Of(catalogue, web);
		var nodes = new List<LayeredLayout.Node>();

		(float W, float H) Size(string key, float w, float h) => sizeOf?.Invoke(key) ?? (w, h);

		string Hint(string goodId)
		{
			Good? good = catalogue.Find(goodId);
			if (good == null) return goodId;
			string shelf = good.Tags.FirstOrDefault(t => t.StartsWith("group:", StringComparison.Ordinal)) ?? "";
			return shelf + "/" + good.Name;
		}

		foreach (string id in web.Goods)
		{
			(float w, float h) = Size(id, GoodWidth, GoodHeight);
			nodes.Add(new LayeredLayout.Node(id, w, h, Hint(id)));
		}
		foreach (Recipe recipe in web.Recipes)
		{
			int rows = Math.Max(recipe.Inputs.Count, recipe.Outputs.Count) + 1;
			(float w, float h) = Size(recipe.Id, RecipeWidth, RecipeHead + RecipeRow * rows);
			string hint = recipe.Outputs.Count > 0 ? Hint(recipe.Outputs[0].Good) : recipe.Id;
			nodes.Add(new LayeredLayout.Node(recipe.Id, w, h, hint));
		}
		foreach (Consumer consumer in web.Consumers)
		{
			(float w, float h) = Size(consumer.Id, ConsumerWidth, ConsumerHeight);
			nodes.Add(new LayeredLayout.Node(consumer.Id, w, h, consumer.Name, LayeredLayout.PinLast));
		}

		List<LayeredLayout.Edge> edges = analysis.Links.Select(l => new LayeredLayout.Edge(l.From, l.To)).ToList();
		Dictionary<string, (float X, float Y)> placed = LayeredLayout.Arrange(nodes, edges);

		web.Layout.Clear();
		foreach ((string key, (float x, float y)) in placed)
			web.Layout[key] = new Spot((int)MathF.Round(x), (int)MathF.Round(y));
	}

	/// <summary>
	/// True when the web has nodes and not one position, as a web fresh from an import has.
	/// A web with some positions was laid out by hand, and is left alone.
	/// </summary>
	public static bool NeedsArranging(EconomyWeb web) =>
		web.Layout.Count == 0 && web.Goods.Count + web.Recipes.Count + web.Consumers.Count > 0;
}
