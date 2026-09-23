using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ProjectNikitin.Economy;

/// <summary>
/// A web's balance: what flows where in a day, given what each recipe takes and gives per run, how
/// many of it can be at work (<see cref="Recipe.Limit"/>: the fields, the pits), and what the
/// people want. Every good comes out of a recipe; a raw one out of an extraction, which takes
/// nothing and stands on its site.
/// <para>
/// First the wants are <b>pulled</b> back from the consumers to the ground: each consumer asks for
/// its people's due, each recipe asks its slots for enough runs to make what is firmly asked of it,
/// and a slot or a consumer that accepts several goods asks each for an equal share; a good asks its
/// makers for equal shares too, save that a maker at its limit is asked no more than it can make and
/// the rest goes to the others. Only a firm
/// want raises a maker: a required slot's or a consumer's. An optional slot asks too, and is
/// counted as wanted, but takes what is there (the dung on the fields is the dung of the herd kept
/// for its milk); and a by-product (<see cref="RecipeOutput.ByProduct"/>) is never what a recipe is
/// run for. Where a loop of firm wants closes (seed corn from the crop), the pull goes round again
/// until it settles, or says it cannot.
/// </para>
/// <para>
/// Then what is there is <b>pushed</b> forward: a good that cannot cover what is asked of it serves
/// the seed corn first (a recipe whose own products lead back to the good, through required slots),
/// then its other firm askers, in proportion, and the optional ones from what is left; a recipe runs as
/// far as its scarcest required slot and its limit allow and no further than it is asked; a filled
/// optional slot with a <see cref="RecipeInput.Boost"/> makes every output of the run that much
/// greater. A loop in the push (grain to the byre, dung to the fields, grain again) is settled by
/// going round from the most every recipe could make downwards until nothing moves: each pass can
/// only lower what the last one allowed, so it settles on the most the loop can keep up, and never
/// swings. The plan is made unboosted; a boost that arrives shows as more than was asked.
/// </para>
/// What comes out is a rate per good, per recipe and per consumer, and the notes that say where the
/// web starves and where it piles up. Read off a web like the analysis, never stored.
/// </summary>
public sealed class WebBalance
{
	/// <summary>
	/// One good's day: what recipes make of it, what is asked of it (all told, and firmly: by required
	/// slots and consumers, whose want a shortage actually holds back), and what is actually taken.
	/// </summary>
	public sealed record GoodFlow(string Id, double Made, double Wanted, double Needed, double Taken)
	{
		public double Available => Made;

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
	public sealed record SlotFlow(double Wanted, double Got, double Used)
	{
		/// <summary>How full the slot is at the runs made, 0 to 1: what an optional slot's boost is scaled by.</summary>
		public double Fill(double runsNeed) => runsNeed <= Tiny ? 0 : Math.Min(1, Used / runsNeed);
	}

	/// <summary>
	/// One recipe's day: the runs asked of it, the runs it manages, the most its limit allows (null for
	/// none), the slot that held it back if one did, and how much its boosts added to every output
	/// (1 for nothing).
	/// </summary>
	public sealed record RecipeFlow(string Id, double Desired, double Runs, double Days, double? Cap, int? LimitedBy, double Yield, IReadOnlyList<SlotFlow> Slots)
	{
		/// <summary>How many of the recipe's places are busy: each runs one batch at a time.</summary>
		public double Workshops => Runs * Days;

		public double Coverage => Desired <= Tiny ? 1 : Math.Min(1, Runs / Desired);

		/// <summary>True when the limit, not an input, is what holds it back.</summary>
		public bool AtLimit => Cap is { } cap && Desired > cap + Tiny && Runs >= cap - 1e-6 * Math.Max(1, cap);
	}

	/// <summary>One consumer's day: what its people want, all told, and what reaches them, by good.</summary>
	public sealed record ConsumerFlow(string Id, double Demand, double Got, IReadOnlyList<(string Good, double Got)> ByGood)
	{
		public double Coverage => Demand <= Tiny ? 1 : Math.Min(1, Got / Demand);
	}

	public enum FlowState { Idle, Even, Surplus, Short }

	private const double Tiny = 1e-9;

	/// <summary>The most times the pull or the push goes round a loop before it gives up and says so.</summary>
	public const int MaxRounds = 5000;

	public double Heads { get; }

	/// <summary>False until the web has a population with wants, or a limit on a recipe: then there is nothing to balance.</summary>
	public bool HasNumbers { get; }

	/// <summary>How many passes of the push changed something: 1 for a web with no loop in it, and for a loop that holds at its most.</summary>
	public int Rounds { get; }

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
	private bool _pullRanAway, _pullUnsettled, _pushUnsettled;

	public GoodFlow? Good(string id) => _goodById.GetValueOrDefault(id);
	public RecipeFlow? Recipe(string id) => _recipeById.GetValueOrDefault(id);
	public ConsumerFlow? Consumer(string id) => _consumerById.GetValueOrDefault(id);

	public static WebBalance Of(EconomyWeb web, WebAnalysis analysis) => new(web, analysis);

	/// <summary>The runs a day a recipe's limit allows, or null for none.</summary>
	public static double? CapOf(Recipe recipe) => recipe.Limit is { } limit ? Math.Max(0, limit) / Math.Max(recipe.Days, Tiny) : null;

	private WebBalance(EconomyWeb web, WebAnalysis analysis)
	{
		Heads = Math.Max(0, web.Heads ?? 0);
		HasNumbers = (Heads > 0 && web.Consumers.Any(c => c.Rate > 0)) || web.Recipes.Any(r => r.Limit is > 0);

		List<string> order = Order(web, analysis);
		var recipes = web.Recipes.ToDictionary(r => r.Id, StringComparer.Ordinal);
		var consumers = web.Consumers.ToDictionary(c => c.Id, StringComparer.Ordinal);

		// Who asks each good, in link order: (user, port, firmly). A consumer's port is 0.
		var askers = new Dictionary<string, List<(string User, int Port, bool Firm)>>(StringComparer.Ordinal);
		foreach (WebLink link in analysis.Links)
		{
			bool firm;
			if (link.Kind == LinkKind.Consumed) firm = true;
			else if (link.Kind == LinkKind.Input && recipes.TryGetValue(link.To, out Recipe? user) && link.Port < user.Inputs.Count) firm = !user.Inputs[link.Port].Optional;
			else continue;
			if (!askers.TryGetValue(link.From, out List<(string, int, bool)>? list)) askers[link.From] = list = new List<(string, int, bool)>();
			if (!list.Contains((link.To, link.Port, firm))) list.Add((link.To, link.Port, firm));
		}
		var eaten = web.Consumers.ToDictionary(c => c.Id, c => Eaten(analysis, c.Id), StringComparer.Ordinal);

		// The makers a good's firm want asks: those that make it as more than a by-product.
		var mainMakers = new Dictionary<string, List<Recipe>>(StringComparer.Ordinal);
		foreach (string id in web.Goods)
			mainMakers[id] = analysis.MakersOf(id).Where(r => r.Outputs.Any(o => o.Good == id && !o.ByProduct)).ToList();
		static double Gives(Recipe recipe, string good) => recipe.Outputs.Where(o => o.Good == good && !o.ByProduct).Sum(o => o.Count);

		// The seed corn: a firm asker whose own main products lead back to the good it asks for, through
		// required slots only (the fields asking for seed grain; a forge asking for the tools its parts become).
		var seedCorn = new HashSet<(string Recipe, string Good)>();
		var firmUsers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		foreach ((string good, List<(string User, int Port, bool Firm)> list) in askers)
			foreach ((string user, _, bool firm) in list)
				if (firm && recipes.ContainsKey(user))
				{
					if (!firmUsers.TryGetValue(good, out List<string>? users)) firmUsers[good] = users = new List<string>();
					if (!users.Contains(user)) users.Add(user);
				}
		foreach (Recipe recipe in web.Recipes)
		{
			var asksFor = recipe.Inputs.Select((slot, i) => (slot, i)).Where(p => !p.slot.Optional).SelectMany(p => analysis.FillersOf(recipe.Id, p.i)).ToHashSet(StringComparer.Ordinal);
			if (asksFor.Count == 0) continue;
			var reached = new HashSet<string>(StringComparer.Ordinal);
			var queue = new Queue<Recipe>();
			queue.Enqueue(recipe);
			var walked = new HashSet<string>(StringComparer.Ordinal) { recipe.Id };
			while (queue.Count > 0)
				foreach (RecipeOutput output in queue.Dequeue().Outputs.Where(o => !o.ByProduct))
					if (reached.Add(output.Good))
						foreach (string user in firmUsers.GetValueOrDefault(output.Good) ?? new List<string>())
							if (walked.Add(user)) queue.Enqueue(recipes[user]);
			foreach (string good in asksFor.Where(reached.Contains)) seedCorn.Add((recipe.Id, good));
		}

		// What a good's firm want asks of each main maker, in units: equal shares, except that a maker at its
		// limit takes no more than it can make and the rest is spread over the others; where every maker is
		// at its limit, what is left is asked of all alike, beyond their limits, so the sheet shows the shortage.
		var share = new Dictionary<(string Recipe, string Good), double>();
		void Share(string good, double units)
		{
			List<Recipe> makers = mainMakers.GetValueOrDefault(good) ?? new List<Recipe>();
			if (makers.Count == 0) return;
			double[] room = makers.Select(r => CapOf(r) is { } cap ? cap * Gives(r, good) : double.PositiveInfinity).ToArray();
			var given = new double[makers.Count];
			var open = Enumerable.Range(0, makers.Count).ToList();
			double left = units;
			while (open.Count > 0 && left > Tiny)
			{
				double each = left / open.Count;
				List<int> full = open.Where(i => room[i] < each).ToList();
				if (full.Count == 0)
				{
					foreach (int i in open) given[i] = each;
					left = 0;
					break;
				}
				foreach (int i in full)
				{
					given[i] = room[i];
					left -= room[i];
				}
				open.RemoveAll(full.Contains);
			}
			if (left > Tiny)
				for (int i = 0; i < makers.Count; i++) given[i] += left / makers.Count;
			for (int i = 0; i < makers.Count; i++) share[(makers[i].Id, good)] = given[i];
		}

		// ---- pull: from the consumers back to the ground, round again while a firm loop moves ----
		var ask = new Dictionary<(string User, int Port, string Good), double>();
		var desired = new Dictionary<string, double>(StringComparer.Ordinal);
		var wanted = new Dictionary<string, double>(StringComparer.Ordinal);
		var needed = new Dictionary<string, double>(StringComparer.Ordinal);

		foreach (Consumer consumer in web.Consumers)
		{
			IReadOnlyList<string> goods = eaten[consumer.Id];
			foreach (string good in goods) ask[(consumer.Id, 0, good)] = Heads * consumer.Rate / goods.Count;
		}

		for (int round = 0; ; round++)
		{
			double moved = 0, scale = 1;
			for (int at = order.Count - 1; at >= 0; at--)
			{
				string key = order[at];
				if (recipes.TryGetValue(key, out Recipe? recipe))
				{
					// Asked for the most any of its main outputs needs of it; its slots are asked at the runs it can manage.
					double runs = 0;
					foreach (RecipeOutput output in recipe.Outputs)
					{
						if (output.ByProduct || output.Count <= Tiny) continue;
						runs = Math.Max(runs, share.GetValueOrDefault((recipe.Id, output.Good)) / Gives(recipe, output.Good));
					}
					moved = Math.Max(moved, Math.Abs(runs - desired.GetValueOrDefault(recipe.Id)));
					scale = Math.Max(scale, runs);
					desired[recipe.Id] = runs;
					double asking = CapOf(recipe) is { } cap ? Math.Min(runs, cap) : runs;
					for (int i = 0; i < recipe.Inputs.Count; i++)
					{
						IReadOnlyList<string> fillers = analysis.FillersOf(recipe.Id, i);
						foreach (string good in fillers) ask[(recipe.Id, i, good)] = asking * recipe.Inputs[i].Count / fillers.Count;
					}
				}
				else if (!consumers.ContainsKey(key))
				{
					double all = 0, firm = 0;
					foreach ((string user, int port, bool isFirm) in askers.GetValueOrDefault(key) ?? new List<(string, int, bool)>())
					{
						double units = ask.GetValueOrDefault((user, port, key));
						all += units;
						if (isFirm) firm += units;
					}
					wanted[key] = all;
					needed[key] = firm;
					Share(key, firm);
				}
			}
			if (moved <= 1e-9 * scale) break;
			if (scale > 1e12)
			{
				_pullRanAway = true; // a loop that asks more of itself than it gives grows without end
				break;
			}
			if (round >= MaxRounds)
			{
				_pullUnsettled = true;
				break;
			}
		}

		// ---- push: from the most each recipe could make downwards, until nothing moves ----
		var outRate = new Dictionary<(string Recipe, string Good), double>();
		foreach (Recipe recipe in web.Recipes)
		{
			double top = CapOf(recipe) is { } cap ? Math.Min(desired.GetValueOrDefault(recipe.Id), cap) : desired.GetValueOrDefault(recipe.Id);
			double yield = 1 + recipe.Inputs.Where(i => i.Optional).Sum(i => Math.Max(0, i.Bonus));
			foreach (RecipeOutput output in recipe.Outputs)
				outRate[(recipe.Id, output.Good)] = outRate.GetValueOrDefault((recipe.Id, output.Good)) + top * output.Count * yield;
		}

		// And every asker starts with all it asked: a recipe round a loop from its good (the fields from their seed) reads this before the good's turn.
		var got = new Dictionary<(string User, int Port, string Good), double>(ask);
		var made = new Dictionary<string, double>(StringComparer.Ordinal);
		var runsOf = new Dictionary<string, RecipeFlow>(StringComparer.Ordinal);
		var gotOf = new Dictionary<string, ConsumerFlow>(StringComparer.Ordinal);
		var taken = new Dictionary<string, double>(StringComparer.Ordinal);

		double GotBy(string user, int port, IReadOnlyList<string> goods) => goods.Sum(g => got.GetValueOrDefault((user, port, g)));

		int rounds = 0;
		while (true)
		{
			rounds++;
			double moved = 0, scale = 1;
			made.Clear();
			taken.Clear();
			foreach (string key in order)
			{
				if (recipes.TryGetValue(key, out Recipe? recipe))
				{
					double asked = desired.GetValueOrDefault(recipe.Id);
					double? cap = CapOf(recipe);
					double runs = cap is { } c ? Math.Min(asked, c) : asked;
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
					double yield = 1;
					for (int i = 0; i < recipe.Inputs.Count; i++)
					{
						RecipeInput slot = recipe.Inputs[i];
						IReadOnlyList<string> fillers = analysis.FillersOf(recipe.Id, i);
						double want = (cap is { } k ? Math.Min(asked, k) : asked) * slot.Count, have = GotBy(recipe.Id, i, fillers), use = Math.Min(have, runs * slot.Count);
						Take(taken, got, recipe.Id, i, fillers, use);
						var flow = new SlotFlow(want, have, use);
						slots.Add(flow);
						if (slot.Optional && slot.Bonus > 0) yield += slot.Bonus * flow.Fill(runs * slot.Count);
					}
					foreach (RecipeOutput output in recipe.Outputs)
					{
						double rate = runs * output.Count * yield;
						made[output.Good] = made.GetValueOrDefault(output.Good) + rate;
					}
					foreach (IGrouping<string, RecipeOutput> same in recipe.Outputs.GroupBy(o => o.Good, StringComparer.Ordinal))
					{
						double rate = runs * same.Sum(o => o.Count) * yield;
						moved = Math.Max(moved, Math.Abs(rate - outRate.GetValueOrDefault((recipe.Id, same.Key))));
						scale = Math.Max(scale, rate);
						outRate[(recipe.Id, same.Key)] = rate;
					}
					runsOf[recipe.Id] = new RecipeFlow(recipe.Id, asked, runs, recipe.Days, cap, limit, yield, slots);
				}
				else if (consumers.TryGetValue(key, out Consumer? consumer))
				{
					IReadOnlyList<string> goods = eaten[consumer.Id];
					double demand = Heads * consumer.Rate;
					double have = GotBy(consumer.Id, 0, goods), fed = Math.Min(demand, have);
					Take(taken, got, consumer.Id, 0, goods, fed);
					double eats = have <= Tiny ? 0 : fed / have;
					gotOf[consumer.Id] = new ConsumerFlow(consumer.Id, demand, fed,
						goods.Select(g => (g, got.GetValueOrDefault((consumer.Id, 0, g)) * eats)).Where(p => p.Item2 > Tiny).ToList());
				}
				else
				{
					// The good's turn: what its makers made (a maker later in the order, round a loop, as it last ran), handed out in
					// three tiers, each in proportion from what the one before left: the seed corn (a maker asking for its own good
					// back, firmly), then the other firm askers, then the optional ones.
					double left = analysis.MakersOf(key).Sum(r => outRate.GetValueOrDefault((r.Id, key)));
					List<(string User, int Port, bool Firm)> list = askers.GetValueOrDefault(key) ?? new List<(string, int, bool)>();
					int TierOf((string User, int Port, bool Firm) a) => !a.Firm ? 2 : seedCorn.Contains((a.User, key)) ? 0 : 1;
					for (int tier = 0; tier < 3; tier++)
					{
						double asked = list.Where(a => TierOf(a) == tier).Sum(a => ask.GetValueOrDefault((a.User, a.Port, key)));
						double part = asked <= left || asked <= Tiny ? 1 : left / asked;
						foreach ((string user, int port, bool _) in list.Where(a => TierOf(a) == tier))
						{
							// An asker before its good in the order (round a loop) read last pass's allotment: a change here is a move too.
							double now = ask.GetValueOrDefault((user, port, key)) * part;
							moved = Math.Max(moved, Math.Abs(now - got.GetValueOrDefault((user, port, key))));
							scale = Math.Max(scale, now);
							got[(user, port, key)] = now;
						}
						left = Math.Max(0, left - asked * part);
					}
				}
			}
			if (moved <= 1e-9 * scale) break;
			if (rounds >= MaxRounds)
			{
				_pushUnsettled = true;
				break;
			}
		}
		Rounds = Math.Max(1, rounds - 1); // the last pass only found that nothing moved

		// ---- the sheet ----
		foreach (string id in web.Goods)
		{
			var flow = new GoodFlow(id, made.GetValueOrDefault(id), wanted.GetValueOrDefault(id), needed.GetValueOrDefault(id), taken.GetValueOrDefault(id));
			_goods.Add(flow);
			_goodById[id] = flow;
		}
		foreach (Recipe recipe in web.Recipes)
		{
			RecipeFlow flow = runsOf.GetValueOrDefault(recipe.Id) ?? new RecipeFlow(recipe.Id, 0, 0, recipe.Days, CapOf(recipe), null, 1, recipe.Inputs.Select(_ => new SlotFlow(0, 0, 0)).ToList());
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

	/// <summary>A user takes <paramref name="units"/> of what it was allotted across its fillers, each in the share it got.</summary>
	private static void Take(Dictionary<string, double> taken, Dictionary<(string, int, string), double> got, string user, int port, IReadOnlyList<string> goods, double units)
	{
		double had = goods.Sum(g => got.GetValueOrDefault((user, port, g)));
		if (had <= Tiny || units <= Tiny) return;
		double share = Math.Min(1, units / had);
		foreach (string good in goods) taken[good] = taken.GetValueOrDefault(good) + got.GetValueOrDefault((user, port, good)) * share;
	}

	/// <summary>The goods a consumer takes, in link order, each once.</summary>
	private static IReadOnlyList<string> Eaten(WebAnalysis analysis, string consumerId) =>
		analysis.Links.Where(l => l.Kind == LinkKind.Consumed && l.To == consumerId).Select(l => l.From).Distinct().ToList();

	/// <summary>
	/// Every node of the web (goods, recipes, consumers) with makers before what they make and
	/// goods before what uses them, as far as loops allow; a loop is gone round by the passes, so
	/// where it is cut changes only how many rounds it takes. The same web gives the same order.
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

		// Extractions first, so the ground is where the walk starts and a loop is cut on its way back to it.
		foreach (string key in web.Recipes.Where(r => r.IsExtraction).Select(r => r.Id).Concat(web.Goods).Concat(web.Recipes.Select(r => r.Id)).Concat(web.Consumers.Select(c => c.Id)))
			if (state.GetValueOrDefault(key) == 0) Walk(key);
		order.Reverse();
		return order;
	}

	private void WriteNotes(EconomyWeb web, WebAnalysis analysis)
	{
		if (!HasNumbers)
		{
			_notes.Add("No numbers yet. Give the web a population, tell each consumer what a head wants a day, and give the extractions a limit.");
			return;
		}
		string NameOf(string id) => web.Palette.Find(id)?.Name ?? id;
		string TitleOf(string recipeId) => analysis.TitleOf(web.Recipe(recipeId)!);

		if (_pullRanAway) _notes.Add("A loop asks more of itself than it gives back: the wants round it grow without end. Look for seed or fuel that costs more than it makes.");
		if (_pullUnsettled) _notes.Add($"The wants round a loop had not settled after {MaxRounds} rounds; the numbers are close, not exact.");
		if (_pushUnsettled) _notes.Add($"The flows had not settled after {MaxRounds} rounds; the numbers are close, not exact.");

		foreach (ConsumerFlow flow in _consumers.Where(c => c.Demand > Tiny))
		{
			Consumer consumer = web.Consumer(flow.Id)!;
			string title = consumer.Name.Length > 0 ? consumer.Name : consumer.Id;
			_notes.Add(flow.Coverage >= 0.995
				? $"{title}: served in full, {Num(flow.Demand)} a day."
				: $"{title}: {Pct(flow.Coverage)} of what {Num(Heads)} people want ({Num(flow.Got)} of {Num(flow.Demand)} a day).");
		}

		// The root of a shortage is a good whose makers are all at their limit, or that nothing makes at all.
		foreach (GoodFlow flow in _goods.Where(g => g.Shortfall > Tiny).OrderByDescending(g => g.Shortfall / Math.Max(g.Needed, Tiny)).ThenBy(g => g.Id, StringComparer.Ordinal))
		{
			IReadOnlyList<Recipe> makers = analysis.MakersOf(flow.Id);
			if (makers.Count == 0)
				_notes.Add($"{NameOf(flow.Id)}: {Num(flow.Needed)} a day needed, and nothing makes it.");
			else if (makers.All(r => r.Outputs.Where(o => o.Good == flow.Id).All(o => o.ByProduct)))
				_notes.Add($"{NameOf(flow.Id)}: {Num(flow.Needed)} a day needed, {Num(flow.Available)} made, and only as a by-product: nothing runs for it.");
			else if (makers.All(r => _recipeById[r.Id].AtLimit))
				_notes.Add($"{NameOf(flow.Id)}: {Num(flow.Needed)} a day needed, {Num(flow.Available)} made; " +
					string.Join(" and ", makers.Select(r => $"{TitleOf(r.Id)} is at its limit ({Num(r.Limit ?? 0)} at work)")) + ".");
		}
		foreach (RecipeFlow flow in _recipes.Where(r => r.Desired > Tiny && r.LimitedBy is { } i && analysis.FillersOf(r.Id, i).Count == 0).Take(3))
			_notes.Add($"{TitleOf(flow.Id)}: asked for {Num(flow.Desired)} runs a day, and input {flow.LimitedBy + 1} has nothing to fill it.");

		// What a boost brought: the fields manured.
		foreach (RecipeFlow flow in _recipes.Where(r => r.Yield > 1 + 1e-6 && r.Runs > Tiny))
		{
			Recipe recipe = web.Recipe(flow.Id)!;
			IEnumerable<string> by = recipe.Inputs.Select((slot, i) => (slot, i)).Where(p => p.slot.Optional && p.slot.Bonus > 0 && flow.Slots[p.i].Used > Tiny)
				.Select(p => $"{string.Join("/", analysis.FillersOf(recipe.Id, p.i).Select(NameOf))} filling {Pct(flow.Slots[p.i].Fill(flow.Runs * p.slot.Count))} of its slot");
			_notes.Add($"{TitleOf(flow.Id)}: {string.Join(", ", by)} makes every run give {Pct(flow.Yield - 1)} more.");
		}

		foreach (GoodFlow flow in _goods.Where(g => g.Made > Tiny && g.Taken < g.Made * 0.5).OrderByDescending(g => g.Surplus).ThenBy(g => g.Id, StringComparer.Ordinal).Take(5))
			_notes.Add($"{NameOf(flow.Id)}: {Num(flow.Made)} a day made, {Num(flow.Taken)} used.");
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
