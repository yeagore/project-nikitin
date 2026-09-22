using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ProjectNikitin.Economy;

/// <summary>
/// A web's balance: what flows where in a day, given what the land supplies at the sources, what
/// each recipe takes and gives per run, and what the people want. Two passes over the web, read as
/// a graph with its loops cut. First the wants are <b>pulled</b> back from the consumers to the
/// sources: each consumer asks for its people's due, each recipe asks its slots for enough runs to
/// make what is asked of it, and a slot or a consumer that accepts several goods asks each for an
/// equal share. Then what is there is <b>pushed</b> forward: a good that cannot cover what is asked
/// of it is rationed among its askers in proportion, and a recipe runs as far as its scarcest slot
/// allows and no further than it is asked. What comes out is a rate per good, per recipe and per
/// consumer, and the notes that say where the web starves and where it piles up. Read off a web
/// like the analysis, never stored.
/// </summary>
public sealed class WebBalance
{
	/// <summary>
	/// One good's day: what the land gives, what recipes make, what is asked of it (all told, and
	/// firmly: by required slots and consumers, whose want a shortage actually holds back), and what
	/// is actually taken.
	/// </summary>
	public sealed record GoodFlow(string Id, double Supply, double Made, double Wanted, double Needed, double Taken)
	{
		public double Available => Supply + Made;

		/// <summary>What is there and not taken: what piles up.</summary>
		public double Surplus => Math.Max(0, Available - Taken);

		/// <summary>What is firmly asked for and not there. An optional slot's want does not count: nothing stops for it.</summary>
		public double Shortfall => Math.Max(0, Needed - Available);

		/// <summary>How much of what is firmly asked for is there, 0 to 1; 1 when nothing is asked.</summary>
		public double Coverage => Needed <= Tiny ? 1 : Math.Min(1, Available / Needed);

		public FlowState State =>
			Shortfall > Tiny ? FlowState.Short
			: Available <= Tiny && Wanted <= Tiny ? FlowState.Idle
			: Surplus > Tiny && Surplus >= Available * 0.25 ? FlowState.Surplus
			: FlowState.Even;
	}

	/// <summary>One slot's day: what the recipe would take at the runs asked of it, what it was allotted, and what it uses at the runs it manages.</summary>
	public sealed record SlotFlow(double Wanted, double Got, double Used);

	/// <summary>One recipe's day: the runs asked of it, the runs it manages, and the slot that held it back, if one did.</summary>
	public sealed record RecipeFlow(string Id, double Desired, double Runs, double Days, int? LimitedBy, IReadOnlyList<SlotFlow> Slots)
	{
		/// <summary>How many of the recipe's building are busy: each runs one batch at a time.</summary>
		public double Workshops => Runs * Days;

		public double Coverage => Desired <= Tiny ? 1 : Math.Min(1, Runs / Desired);
	}

	/// <summary>One consumer's day: what its people want, all told, and what reaches them, by good.</summary>
	public sealed record ConsumerFlow(string Id, double Demand, double Got, IReadOnlyList<(string Good, double Got)> ByGood)
	{
		public double Coverage => Demand <= Tiny ? 1 : Math.Min(1, Got / Demand);
	}

	public enum FlowState { Idle, Even, Surplus, Short }

	private const double Tiny = 1e-9;

	public double Heads { get; }

	/// <summary>False until the web has a population with wants, or a supply: then there is nothing to balance.</summary>
	public bool HasNumbers { get; }

	public IReadOnlyList<GoodFlow> Goods => _goods;
	public IReadOnlyList<RecipeFlow> Recipes => _recipes;
	public IReadOnlyList<ConsumerFlow> Consumers => _consumers;

	/// <summary>Where the web starves and where it piles up, in plain sentences, the worst first.</summary>
	public IReadOnlyList<string> Notes => _notes;

	/// <summary>How much of everything the people want reaches them, weighted by amount, 0 to 1.</summary>
	public double Coverage { get; }

	private readonly List<GoodFlow> _goods = new();
	private readonly List<RecipeFlow> _recipes = new();
	private readonly List<ConsumerFlow> _consumers = new();
	private readonly List<string> _notes = new();
	private readonly Dictionary<string, GoodFlow> _goodById = new(StringComparer.Ordinal);
	private readonly Dictionary<string, RecipeFlow> _recipeById = new(StringComparer.Ordinal);
	private readonly Dictionary<string, ConsumerFlow> _consumerById = new(StringComparer.Ordinal);

	public GoodFlow? Good(string id) => _goodById.GetValueOrDefault(id);
	public RecipeFlow? Recipe(string id) => _recipeById.GetValueOrDefault(id);
	public ConsumerFlow? Consumer(string id) => _consumerById.GetValueOrDefault(id);

	public static WebBalance Of(EconomyWeb web, WebAnalysis analysis) => new(web, analysis);

	private WebBalance(EconomyWeb web, WebAnalysis analysis)
	{
		Heads = Math.Max(0, web.Heads ?? 0);
		HasNumbers = (Heads > 0 && web.Consumers.Any(c => c.Rate > 0)) || (web.Supply?.Values.Any(v => v > 0) ?? false);

		List<string> order = Order(web, analysis);
		var recipes = web.Recipes.ToDictionary(r => r.Id, StringComparer.Ordinal);
		var consumers = web.Consumers.ToDictionary(c => c.Id, StringComparer.Ordinal);

		// ---- pull: from the consumers back to the ground ----
		var wanted = new Dictionary<string, double>(StringComparer.Ordinal);
		var needed = new Dictionary<string, double>(StringComparer.Ordinal);
		var desired = new Dictionary<string, double>(StringComparer.Ordinal);
		var pull = new Dictionary<(string User, int Port, string Good), double>();
		var askers = new Dictionary<string, List<(string User, int Port)>>(StringComparer.Ordinal);

		void Ask(string user, int port, IReadOnlyList<string> goods, double units, bool firmly)
		{
			if (units <= Tiny || goods.Count == 0) return;
			double each = units / goods.Count;
			foreach (string good in goods)
			{
				pull[(user, port, good)] = pull.GetValueOrDefault((user, port, good)) + each;
				wanted[good] = wanted.GetValueOrDefault(good) + each;
				if (firmly) needed[good] = needed.GetValueOrDefault(good) + each;
				if (!askers.TryGetValue(good, out List<(string, int)>? list)) askers[good] = list = new List<(string, int)>();
				if (!list.Contains((user, port))) list.Add((user, port));
			}
		}

		for (int at = order.Count - 1; at >= 0; at--)
		{
			string key = order[at];
			if (consumers.TryGetValue(key, out Consumer? consumer))
				Ask(consumer.Id, 0, Eaten(analysis, consumer.Id), Heads * consumer.Rate, firmly: true);
			else if (recipes.TryGetValue(key, out Recipe? recipe))
			{
				double runs = desired.GetValueOrDefault(recipe.Id);
				for (int i = 0; i < recipe.Inputs.Count; i++)
					Ask(recipe.Id, i, analysis.FillersOf(recipe.Id, i), runs * recipe.Inputs[i].Count, firmly: !recipe.Inputs[i].Optional);
			}
			else
			{
				// A good asks its makers for what the land does not give, in equal shares.
				double fromMakers = Math.Max(0, wanted.GetValueOrDefault(key) - web.SupplyOf(key));
				IReadOnlyList<Recipe> makers = analysis.MakersOf(key);
				if (fromMakers <= Tiny || makers.Count == 0) continue;
				foreach (Recipe maker in makers)
				{
					double gives = maker.Outputs.Where(o => o.Good == key).Sum(o => o.Count);
					if (gives <= Tiny) continue;
					double runs = fromMakers / makers.Count / gives;
					desired[maker.Id] = Math.Max(desired.GetValueOrDefault(maker.Id), runs);
				}
			}
		}

		// ---- push: from the ground forward to the consumers ----
		var made = new Dictionary<string, double>(StringComparer.Ordinal);
		var taken = new Dictionary<string, double>(StringComparer.Ordinal);
		var got = new Dictionary<(string User, int Port, string Good), double>();
		var runsOf = new Dictionary<string, RecipeFlow>(StringComparer.Ordinal);
		var gotOf = new Dictionary<string, ConsumerFlow>(StringComparer.Ordinal);

		double GotBy(string user, int port, IReadOnlyList<string> goods) => goods.Sum(g => got.GetValueOrDefault((user, port, g)));

		void Take(string user, int port, IReadOnlyList<string> goods, double units)
		{
			double had = GotBy(user, port, goods);
			if (had <= Tiny || units <= Tiny) return;
			double share = Math.Min(1, units / had);
			foreach (string good in goods) taken[good] = taken.GetValueOrDefault(good) + got.GetValueOrDefault((user, port, good)) * share;
		}

		foreach (string key in order)
		{
			if (recipes.TryGetValue(key, out Recipe? recipe))
			{
				double asked = desired.GetValueOrDefault(recipe.Id);
				double runs = asked;
				int? limit = null;
				for (int i = 0; i < recipe.Inputs.Count && asked > Tiny; i++)
				{
					RecipeInput slot = recipe.Inputs[i];
					if (slot.Optional) continue;
					IReadOnlyList<string> fillers = analysis.FillersOf(recipe.Id, i);
					double possible = fillers.Count == 0 ? 0 : GotBy(recipe.Id, i, fillers) / Math.Max(slot.Count, Tiny);
					if (possible < runs - Tiny)
					{
						runs = possible;
						limit = i;
					}
				}
				var slots = new List<SlotFlow>();
				for (int i = 0; i < recipe.Inputs.Count; i++)
				{
					IReadOnlyList<string> fillers = analysis.FillersOf(recipe.Id, i);
					double want = asked * recipe.Inputs[i].Count, have = GotBy(recipe.Id, i, fillers), use = Math.Min(have, runs * recipe.Inputs[i].Count);
					Take(recipe.Id, i, fillers, use);
					slots.Add(new SlotFlow(want, have, use));
				}
				foreach (RecipeOutput output in recipe.Outputs) made[output.Good] = made.GetValueOrDefault(output.Good) + runs * output.Count;
				runsOf[recipe.Id] = new RecipeFlow(recipe.Id, asked, runs, recipe.Days, limit, slots);
			}
			else if (consumers.TryGetValue(key, out Consumer? consumer))
			{
				IReadOnlyList<string> goods = Eaten(analysis, consumer.Id);
				double demand = Heads * consumer.Rate;
				double have = GotBy(consumer.Id, 0, goods), fed = Math.Min(demand, have);
				Take(consumer.Id, 0, goods, fed);
				double share = have <= Tiny ? 0 : fed / have;
				gotOf[consumer.Id] = new ConsumerFlow(consumer.Id, demand, fed,
					goods.Select(g => (g, got.GetValueOrDefault((consumer.Id, 0, g)) * share)).Where(p => p.Item2 > Tiny).ToList());
			}
			else
			{
				// The good's turn: every maker has run, so what there is is known; hand it out.
				double available = web.SupplyOf(key) + made.GetValueOrDefault(key);
				double asked = wanted.GetValueOrDefault(key);
				double scale = asked <= available || asked <= Tiny ? 1 : available / asked;
				foreach ((string user, int port) in askers.GetValueOrDefault(key) ?? new List<(string, int)>())
					got[(user, port, key)] = pull[(user, port, key)] * scale;
			}
		}

		// ---- the sheet ----
		foreach (string id in web.Goods)
		{
			var flow = new GoodFlow(id, web.SupplyOf(id), made.GetValueOrDefault(id), wanted.GetValueOrDefault(id), needed.GetValueOrDefault(id), taken.GetValueOrDefault(id));
			_goods.Add(flow);
			_goodById[id] = flow;
		}
		foreach (Recipe recipe in web.Recipes)
		{
			RecipeFlow flow = runsOf.GetValueOrDefault(recipe.Id) ?? new RecipeFlow(recipe.Id, 0, 0, recipe.Days, null, recipe.Inputs.Select(_ => new SlotFlow(0, 0, 0)).ToList());
			_recipes.Add(flow);
			_recipeById[recipe.Id] = flow;
		}
		foreach (Consumer consumer in web.Consumers)
		{
			ConsumerFlow flow = gotOf.GetValueOrDefault(consumer.Id) ?? new ConsumerFlow(consumer.Id, Heads * consumer.Rate, 0, Array.Empty<(string, double)>());
			_consumers.Add(flow);
			_consumerById[consumer.Id] = flow;
		}
		double demandAll = _consumers.Sum(c => c.Demand);
		Coverage = demandAll <= Tiny ? 1 : Math.Min(1, _consumers.Sum(c => c.Got) / demandAll);

		WriteNotes(web, analysis);
	}

	/// <summary>The goods a consumer takes, in link order, each once.</summary>
	private static IReadOnlyList<string> Eaten(WebAnalysis analysis, string consumerId) =>
		analysis.Links.Where(l => l.Kind == LinkKind.Consumed && l.To == consumerId).Select(l => l.From).Distinct().ToList();

	/// <summary>
	/// Every node of the web (goods, recipes, consumers) with makers before what they make and
	/// goods before what uses them; a loop's closing edge is left out, so a seed that grows from
	/// its own crop is asked nothing through that edge. The same web gives the same order.
	/// </summary>
	private static List<string> Order(EconomyWeb web, WebAnalysis analysis)
	{
		var next = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		foreach (WebLink link in analysis.Links)
		{
			if (!next.TryGetValue(link.From, out List<string>? list)) next[link.From] = list = new List<string>();
			if (!list.Contains(link.To)) list.Add(link.To);
		}
		var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 on the path, 2 done
		var order = new List<string>();

		void Walk(string node)
		{
			state[node] = 1;
			foreach (string to in next.GetValueOrDefault(node) ?? (IEnumerable<string>)Array.Empty<string>())
				if (state.GetValueOrDefault(to) == 0) Walk(to);
			state[node] = 2;
			order.Add(node);
		}

		foreach (string key in web.Goods.Concat(web.Recipes.Select(r => r.Id)).Concat(web.Consumers.Select(c => c.Id)))
			if (state.GetValueOrDefault(key) == 0) Walk(key);
		order.Reverse();
		return order;
	}

	private void WriteNotes(EconomyWeb web, WebAnalysis analysis)
	{
		if (!HasNumbers)
		{
			_notes.Add("No numbers yet. Give the web a population, tell each consumer what a head wants a day, and give the sources a supply.");
			return;
		}
		string NameOf(string id) => web.Palette.Find(id)?.Name ?? id;

		foreach (ConsumerFlow flow in _consumers.Where(c => c.Demand > Tiny))
		{
			Consumer consumer = web.Consumer(flow.Id)!;
			string title = consumer.Name.Length > 0 ? consumer.Name : consumer.Id;
			_notes.Add(flow.Coverage >= 0.995
				? $"{title}: served in full, {Num(flow.Demand)} a day."
				: $"{title}: {Pct(flow.Coverage)} of what {Num(Heads)} people want ({Num(flow.Got)} of {Num(flow.Demand)} a day).");
		}

		// The root of a shortage is a source that cannot keep up, or a good nothing supplies at all.
		foreach (GoodFlow flow in _goods.Where(g => g.Shortfall > Tiny && analysis.MakersOf(g.Id).Count == 0).OrderByDescending(g => g.Shortfall / Math.Max(g.Needed, Tiny)).ThenBy(g => g.Id, StringComparer.Ordinal).Take(6))
			_notes.Add(flow.Available <= Tiny
				? $"{NameOf(flow.Id)}: {Num(flow.Needed)} a day wanted, and nothing supplies it."
				: $"{NameOf(flow.Id)}: {Num(flow.Needed)} a day wanted, {Num(flow.Available)} supplied.");
		foreach (RecipeFlow flow in _recipes.Where(r => r.Desired > Tiny && r.LimitedBy is { } i && analysis.FillersOf(r.Id, i).Count == 0).Take(3))
			_notes.Add($"{analysis.TitleOf(web.Recipe(flow.Id)!)}: asked for {Num(flow.Desired)} runs a day, and input {flow.LimitedBy + 1} has nothing to fill it.");

		foreach (GoodFlow flow in _goods.Where(g => g.Supply > Tiny && g.Taken < g.Supply * 0.5).OrderByDescending(g => g.Surplus).ThenBy(g => g.Id, StringComparer.Ordinal).Take(4))
			_notes.Add($"{NameOf(flow.Id)}: {Num(flow.Supply)} a day supplied, {Num(flow.Taken)} used.");
	}

	/// <summary>A rate as a person would write it: 1200; 83; 4.5; 0.25. No thousands mark: a comma is the decimal mark on the other machine.</summary>
	public static string Num(double v)
	{
		double a = Math.Abs(v);
		string format = a >= 100 ? "0" : a >= 10 ? "0.#" : "0.##";
		return v.ToString(format, CultureInfo.InvariantCulture);
	}

	public static string Pct(double fraction) => Math.Round(fraction * 100).ToString(CultureInfo.InvariantCulture) + "%";
}
