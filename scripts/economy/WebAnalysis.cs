using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectNikitin.Economy;

/// <summary>
/// A web read back: every link it implies (the drawn ones and the ones a tag brings),
/// what each good is here (a source, an intermediate, a final good, or loose), how many
/// steps it is from the ground, what uses it, and what is wrong. Nothing here is stored;
/// build a new one after any change. It is what the lab draws and what a future
/// simulation would walk.
/// </summary>
public sealed class WebAnalysis
{
	/// <summary>A good used by this many recipes or more is a hub.</summary>
	public const int HubUses = 6;

	public IReadOnlyList<WebLink> Links => _links;
	public IReadOnlyList<WebIssue> Issues => _issues;

	private readonly List<WebLink> _links = new();
	private readonly List<WebIssue> _issues = new();
	private readonly Dictionary<string, List<Recipe>> _makers = new(StringComparer.Ordinal);
	private readonly Dictionary<string, List<Recipe>> _users = new(StringComparer.Ordinal);
	private readonly Dictionary<string, List<Consumer>> _eaters = new(StringComparer.Ordinal);
	private readonly Dictionary<string, int> _depth = new(StringComparer.Ordinal);
	private readonly Dictionary<string, List<string>> _next = new(StringComparer.Ordinal);
	private readonly Dictionary<string, List<string>> _prev = new(StringComparer.Ordinal);
	private readonly Catalogue _catalogue;
	private readonly EconomyWeb _web;

	private static readonly List<Recipe> NoRecipes = new();
	private static readonly List<Consumer> NoConsumers = new();

	public static WebAnalysis Of(Catalogue catalogue, EconomyWeb web) => new(catalogue, web);

	private WebAnalysis(Catalogue catalogue, EconomyWeb web)
	{
		_catalogue = catalogue;
		_web = web;
		ReadLinks();
		foreach (string id in web.Goods) DepthOf(id, new HashSet<string>(StringComparer.Ordinal));
		FindLoops();
	}

	/// <summary>The recipes of this web that make the good.</summary>
	public IReadOnlyList<Recipe> MakersOf(string goodId) => _makers.GetValueOrDefault(goodId, NoRecipes);

	/// <summary>The recipes of this web with a slot the good fits, by name or by tag.</summary>
	public IReadOnlyList<Recipe> UsersOf(string goodId) => _users.GetValueOrDefault(goodId, NoRecipes);

	public IReadOnlyList<Consumer> ConsumersOf(string goodId) => _eaters.GetValueOrDefault(goodId, NoConsumers);

	public bool IsConsumed(string goodId) => _eaters.ContainsKey(goodId);

	public bool IsHub(string goodId) => UsersOf(goodId).Count >= HubUses;

	public GoodRole RoleOf(string goodId)
	{
		bool made = MakersOf(goodId).Count > 0, used = UsersOf(goodId).Count > 0;
		if (made) return used ? GoodRole.Intermediate : GoodRole.Final;
		return used || IsConsumed(goodId) ? GoodRole.Source : GoodRole.Loose;
	}

	/// <summary>
	/// Steps from the ground by the shortest way: a source is 0; a made good is one more than
	/// the deepest required slot of its shallowest recipe, each slot counted by its shallowest acceptor.
	/// </summary>
	public int Depth(string goodId) => _depth.GetValueOrDefault(goodId);

	/// <summary>The node and everything it is made of, recipes included, back to the ground.</summary>
	public HashSet<string> Upstream(string nodeKey) => Reach(nodeKey, _prev);

	/// <summary>The node and everything that is made with it, consumers included.</summary>
	public HashSet<string> Downstream(string nodeKey) => Reach(nodeKey, _next);

	private static HashSet<string> Reach(string start, Dictionary<string, List<string>> edges)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal) { start };
		var queue = new Queue<string>();
		queue.Enqueue(start);
		while (queue.Count > 0)
			foreach (string next in edges.GetValueOrDefault(queue.Dequeue()) ?? (IEnumerable<string>)Array.Empty<string>())
				if (seen.Add(next)) queue.Enqueue(next);
		return seen;
	}

	private void ReadLinks()
	{
		var inWeb = new HashSet<string>(_web.Goods, StringComparer.Ordinal);
		var goods = new List<Good>();
		foreach (string id in _web.Goods)
		{
			Good? good = _catalogue.Find(id);
			if (good != null) goods.Add(good);
			else _issues.Add(new WebIssue(IssueLevel.Error, id, $"\"{id}\" is in this web but not in the catalogue."));
		}

		foreach (Recipe recipe in _web.Recipes)
		{
			string title = TitleOf(recipe);
			if (recipe.Outputs.Count == 0) _issues.Add(new WebIssue(IssueLevel.Error, recipe.Id, $"{title} makes nothing."));
			if (recipe.Inputs.Count == 0) _issues.Add(new WebIssue(IssueLevel.Warning, recipe.Id, $"{title} takes nothing."));

			for (int o = 0; o < recipe.Outputs.Count; o++)
			{
				string made = recipe.Outputs[o].Good;
				if (!inWeb.Contains(made))
				{
					_issues.Add(new WebIssue(IssueLevel.Error, recipe.Id, $"{title} makes \"{made}\", which is not in this web."));
					continue;
				}
				Add(_makers, made, recipe);
				AddLink(new WebLink(recipe.Id, made, LinkKind.Output, o, made));
			}

			for (int i = 0; i < recipe.Inputs.Count; i++)
			{
				RecipeInput slot = recipe.Inputs[i];
				if (slot.Accepts.Count == 0)
					_issues.Add(new WebIssue(IssueLevel.Error, recipe.Id, $"{title}: input {i + 1} accepts nothing."));
				foreach ((Good good, string via) in Admitted(slot.Accepts, goods, inWeb, recipe.Id, title))
				{
					Add(_users, good.Id, recipe);
					AddLink(new WebLink(good.Id, recipe.Id, LinkKind.Input, i, via));
				}
			}
		}

		foreach (Consumer consumer in _web.Consumers)
		{
			string title = consumer.Name.Length > 0 ? consumer.Name : consumer.Id;
			if (consumer.Accepts.Count == 0)
				_issues.Add(new WebIssue(IssueLevel.Warning, consumer.Id, $"The consumer {title} accepts nothing."));
			foreach ((Good good, string via) in Admitted(consumer.Accepts, goods, inWeb, consumer.Id, title))
			{
				Add(_eaters, good.Id, consumer);
				AddLink(new WebLink(good.Id, consumer.Id, LinkKind.Consumed, 0, via));
			}
		}

		foreach (Good good in goods)
			if (RoleOf(good.Id) == GoodRole.Loose)
				_issues.Add(new WebIssue(IssueLevel.Note, good.Id, $"{good.Name} is not linked to anything."));
	}

	/// <summary>
	/// The goods of the web a list of acceptors lets in, each once, with the acceptor that let it in.
	/// A good named outright wins over a tag that would also admit it, so its link reads as drawn.
	/// </summary>
	private IEnumerable<(Good Good, string Via)> Admitted(List<string> accepts, List<Good> goods, HashSet<string> inWeb, string node, string title)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var found = new List<(Good, string)>();
		foreach (string acceptor in accepts.Where(a => !Acceptor.IsTag(a)))
		{
			Good? good = inWeb.Contains(acceptor) ? _catalogue.Find(acceptor) : null;
			if (good == null) _issues.Add(new WebIssue(IssueLevel.Error, node, $"{title} accepts \"{acceptor}\", which is not in this web."));
			else if (seen.Add(good.Id)) found.Add((good, acceptor));
		}
		foreach (string acceptor in accepts.Where(Acceptor.IsTag))
		{
			string tag = Acceptor.TagOf(acceptor);
			int before = found.Count;
			bool any = false;
			foreach (Good good in goods)
			{
				if (!good.Tags.Contains(tag)) continue;
				any = true;
				if (seen.Add(good.Id)) found.Add((good, acceptor));
			}
			if (!any && found.Count == before)
				_issues.Add(new WebIssue(IssueLevel.Warning, node, $"{title} accepts {acceptor}, and nothing in this web carries that tag."));
		}
		return found;
	}

	private void AddLink(WebLink link)
	{
		_links.Add(link);
		Add(_next, link.From, link.To);
		Add(_prev, link.To, link.From);
	}

	private static void Add<T>(Dictionary<string, List<T>> map, string key, T value)
	{
		if (!map.TryGetValue(key, out List<T>? list)) map[key] = list = new List<T>();
		if (!list.Contains(value)) list.Add(value);
	}

	private const int Unreachable = int.MaxValue / 2;

	private int DepthOf(string goodId, HashSet<string> walking)
	{
		if (_depth.TryGetValue(goodId, out int known)) return known;
		IReadOnlyList<Recipe> makers = MakersOf(goodId);
		if (makers.Count == 0) return _depth[goodId] = 0;
		if (!walking.Add(goodId)) return Unreachable; // a loop: this way round does not reach the ground

		int best = Unreachable;
		foreach (Recipe recipe in makers)
		{
			int deepest = 0;
			for (int i = 0; i < recipe.Inputs.Count && deepest < Unreachable; i++)
			{
				if (recipe.Inputs[i].Optional) continue;
				int shallowest = Unreachable;
				foreach (WebLink link in _links)
					if (link.Kind == LinkKind.Input && link.To == recipe.Id && link.Port == i)
						shallowest = Math.Min(shallowest, DepthOf(link.From, walking));
				// A slot nothing fills says nothing about depth; the issue list already names it.
				if (shallowest < Unreachable || HasLink(recipe.Id, i)) deepest = Math.Max(deepest, shallowest);
			}
			if (deepest < Unreachable) best = Math.Min(best, deepest + 1);
		}
		walking.Remove(goodId);
		if (best >= Unreachable) return Unreachable; // not cached: another way in may still reach the ground
		return _depth[goodId] = best;
	}

	private bool HasLink(string recipeId, int port) =>
		_links.Exists(l => l.Kind == LinkKind.Input && l.To == recipeId && l.Port == port);

	/// <summary>A note for each loop found (seeds from the crop that grew from seeds), five at most.</summary>
	private void FindLoops()
	{
		var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 on the path, 2 done
		var path = new List<string>();
		int notes = 0;

		void Walk(string node)
		{
			state[node] = 1;
			path.Add(node);
			foreach (string next in _next.GetValueOrDefault(node) ?? (IEnumerable<string>)Array.Empty<string>())
			{
				int seen = state.GetValueOrDefault(next);
				if (seen == 0) Walk(next);
				else if (seen == 1 && notes < 5)
				{
					notes++;
					IEnumerable<string> loop = path.Skip(path.IndexOf(next)).Where(k => _catalogue.Find(k) != null).Select(k => _catalogue.Find(k)!.Name);
					_issues.Add(new WebIssue(IssueLevel.Note, next, "A loop: " + string.Join(" → ", loop) + " → and round again."));
				}
			}
			path.RemoveAt(path.Count - 1);
			state[node] = 2;
		}

		foreach (string id in _web.Goods)
			if (state.GetValueOrDefault(id) == 0) Walk(id);
	}

	/// <summary>A recipe's label, or "→ what it makes" when it has none.</summary>
	public string TitleOf(Recipe recipe)
	{
		if (recipe.Name.Length > 0) return recipe.Name;
		if (recipe.Outputs.Count == 0) return "→ ?";
		return "→ " + string.Join(", ", recipe.Outputs.Select(o => _catalogue.Find(o.Good)?.Name ?? o.Good));
	}
}
