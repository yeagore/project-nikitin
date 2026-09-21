using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// The canvas. The web is the truth and the canvas follows it: after every change
/// <see cref="SyncGraph"/> drops the nodes and wires that are gone, adds the new ones and
/// lets each node redraw itself if what it shows has changed. The user's gestures (a link
/// dragged, a node deleted, a good dropped from the palette) are turned into changes to the
/// web and never edit the canvas directly.
/// </summary>
public partial class EconomyLab
{
	private WebGraph _graph = null!;
	private PopupMenu _menu = null!;
	private PickPopup _picker = null!;
	private readonly List<Action> _menuActions = new();
	private readonly Dictionary<string, GraphNode> _nodes = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _keyOfName = new(StringComparer.Ordinal);
	private readonly HashSet<(string From, int FromPort, string To, int ToPort)> _wired = new();
	private int _serial, _cascade;

	/// <summary>Set while the lab itself marks the selection, so the canvas's signals are not taken for clicks.</summary>
	private bool _quiet;

	private WebGraph BuildGraph()
	{
		_graph = new WebGraph
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			RightDisconnects = true,
			MinimapEnabled = true,
			ShowArrangeButton = false,
			ZoomMin = 0.08f,
			ZoomMax = 2f,
			SnappingDistance = 10,
			ConnectionLinesThickness = 3f,
			ConnectionLinesCurvature = 0.5f,
			ConnectionLinesAntialiased = true,
			MinimapSize = new Vector2(260, 170),
		};
		_graph.AddThemeStyleboxOverride("panel", LabLook.Box(LabLook.Back, 0, 0));
		// "Activity" is the one per-wire colour the canvas offers. The trace spends it on the wires
		// that are NOT traced, fading them, so the traced ones keep the colours that say what they are.
		_graph.AddThemeColorOverride("activity", new Color(0.55f, 0.58f, 0.65f, 0.07f));
		_graph.AddThemeColorOverride("grid_major", new Color(1, 1, 1, 0.06f));
		_graph.AddThemeColorOverride("grid_minor", new Color(1, 1, 1, 0.025f));

		_graph.ConnectionRequest += (from, fromPort, to, toPort) => Link(KeyOf(from), (int)fromPort, KeyOf(to), (int)toPort);
		_graph.DisconnectionRequest += (from, fromPort, to, toPort) => Unlink(KeyOf(from), (int)fromPort, KeyOf(to), (int)toPort);
		_graph.ConnectionToEmpty += (from, fromPort, at) => LinkToNowhere(KeyOf(from), (int)fromPort, at);
		_graph.ConnectionFromEmpty += (to, toPort, at) => LinkFromNowhere(KeyOf(to), (int)toPort, at);
		_graph.DeleteNodesRequest += names => RemoveNodes(names.Select(n => KeyOf(n)).Where(k => k != null).Select(k => k!).ToList());
		_graph.PopupRequest += CanvasMenu;
		_graph.EndNodeMove += NodesMoved;
		_graph.GoodsDropped += DropGoods;
		_graph.NodeSelected += node =>
		{
			if (!_quiet) Selected(KeyOf(node.Name));
		};
		_graph.NodeDeselected += _ =>
		{
			if (_quiet) return;
			string? still = _nodes.FirstOrDefault(p => p.Value.Selected).Key;
			Selected(still);
		};

		_menu = new PopupMenu();
		_menu.IdPressed += id => _menuActions[(int)id]();
		AddChild(_menu);
		_picker = new PickPopup();
		AddChild(_picker);
		return _graph;
	}

	private string? KeyOf(StringName name) => _keyOfName.GetValueOrDefault(name.ToString());

	// ---- the web onto the canvas -----------------------------------------------

	/// <summary>Empties the canvas and draws the web afresh: a web opened, or an undo taken.</summary>
	private void ResetGraph()
	{
		_graph.ClearConnections();
		_wired.Clear();
		foreach (GraphNode node in _nodes.Values)
		{
			_graph.RemoveChild(node);
			node.QueueFree();
		}
		_nodes.Clear();
		_keyOfName.Clear();
		_cascade = 0;
		SyncGraph();
		MarkSelected(SelectedKey, focus: false);
	}

	private void SyncGraph()
	{
		var wanted = new List<string>();
		wanted.AddRange(Web.Goods.Where(id => Catalogue.Find(id) != null));
		wanted.AddRange(Web.Recipes.Select(r => r.Id));
		wanted.AddRange(Web.Consumers.Select(c => c.Id));
		var wantedSet = new HashSet<string>(wanted, StringComparer.Ordinal);

		var wires = new HashSet<(string, int, string, int)>();
		foreach (WebLink link in Analysis.Links)
		{
			if (!wantedSet.Contains(link.From) || !wantedSet.Contains(link.To)) continue;
			wires.Add(link.Kind switch
			{
				LinkKind.Input => (link.From, 0, link.To, link.Port),
				LinkKind.Output => (link.From, link.Port, link.To, 0),
				_ => (link.From, 0, link.To, 0),
			});
		}

		// Stale wires go first: a recipe about to lose a row must not be left wired to it.
		foreach ((string from, int fromPort, string to, int toPort) in _wired.Where(w => !wires.Contains(w)).ToList())
		{
			_graph.DisconnectNode(_nodes[from].Name, fromPort, _nodes[to].Name, toPort);
			_wired.Remove((from, fromPort, to, toPort));
		}
		foreach (string key in _nodes.Keys.Where(k => !wantedSet.Contains(k)).ToList())
		{
			GraphNode gone = _nodes[key];
			_keyOfName.Remove(gone.Name.ToString());
			_nodes.Remove(key);
			_graph.RemoveChild(gone);
			gone.QueueFree();
		}

		foreach (string key in wanted)
		{
			if (!_nodes.TryGetValue(key, out GraphNode? node)) node = AddNode(key);
			else if (Web.Layout.TryGetValue(key, out Spot spot) && SpotOf(node) != spot) node.PositionOffset = new Vector2(spot.X, spot.Y);

			switch (node)
			{
				case GoodNode good: good.Show(Catalogue.Find(key)!, IconOf(key), Analysis); break;
				case RecipeNode recipe: recipe.Show(Web.Recipe(key)!, Catalogue, Analysis, Sprites); break;
				case ConsumerNode consumer: consumer.Show(Web.Consumer(key)!, Analysis); break;
			}
		}

		foreach ((string from, int fromPort, string to, int toPort) in wires)
			if (_wired.Add((from, fromPort, to, toPort)))
				_graph.ConnectNode(_nodes[from].Name, fromPort, _nodes[to].Name, toPort);

		ApplyTrace();
	}

	private GraphNode AddNode(string key)
	{
		GraphNode node = Web.Holds(key) ? new GoodNode() : Web.Recipe(key) != null ? new RecipeNode() : new ConsumerNode();
		node.Name = "n" + ++_serial;
		if (!Web.Layout.TryGetValue(key, out Spot spot))
		{
			// A node with no place yet (a web edited by hand) lands mid-screen, each a little off the last.
			Vector2 free = _graph.CanvasCentre + new Vector2(26, 26) * (_cascade++ % 10);
			Web.Layout[key] = spot = new Spot((int)free.X, (int)free.Y);
		}
		node.PositionOffset = new Vector2(spot.X, spot.Y);
		node.GuiInput += @event => NodeInput(key, @event);
		_graph.AddChild(node);
		_nodes[key] = node;
		_keyOfName[node.Name.ToString()] = key;
		return node;
	}

	private static Spot SpotOf(GraphNode node) => new((int)MathF.Round(node.PositionOffset.X), (int)MathF.Round(node.PositionOffset.Y));

	private void NodesMoved()
	{
		List<KeyValuePair<string, GraphNode>> moved = _nodes.Where(p => Web.Layout.GetValueOrDefault(p.Key) != SpotOf(p.Value)).ToList();
		if (moved.Count == 0) return;
		Change(moved.Count == 1 ? "moved a node" : $"moved {moved.Count} nodes", Touch.Web, () =>
		{
			foreach ((string key, GraphNode node) in moved) Web.Layout[key] = SpotOf(node);
		}, keepInspector: true);
	}

	// ---- selection, trace and travel ------------------------------------------

	private void MarkSelected(string? key, bool focus)
	{
		_quiet = true;
		foreach ((string k, GraphNode node) in _nodes) node.Selected = k == key;
		_quiet = false;
		if (focus && key != null) Centre(key);
	}

	/// <summary>Selects several nodes at once on the canvas (every good carrying a tag, say) and frames nothing; the first becomes the inspector's.</summary>
	internal void SelectMany(IReadOnlyCollection<string> keys)
	{
		_quiet = true;
		foreach ((string k, GraphNode node) in _nodes) node.Selected = keys.Contains(k);
		_quiet = false;
		SelectedKey = keys.FirstOrDefault(Exists);
		RefreshInspector();
		ApplyTrace();
	}

	/// <summary>The middle of what the canvas shows, as a place to put a new node.</summary>
	internal Spot CentreSpot() => new((int)_graph.CanvasCentre.X, (int)_graph.CanvasCentre.Y);

	/// <summary>The keys of every node selected on the canvas, the rubber band's catch included.</summary>
	internal List<string> SelectedKeys() => _nodes.Where(p => p.Value.Selected).Select(p => p.Key).ToList();

	/// <summary>
	/// With Trace on and one node selected, everything that node is made of and everything
	/// made with it stays lit with its links, and the rest of the web fades, wires and all.
	/// </summary>
	private void ApplyTrace()
	{
		HashSet<string>? lit = null;
		if (_trace && SelectedKey != null && _nodes.ContainsKey(SelectedKey) && SelectedKeys().Count <= 1)
		{
			lit = Analysis.Upstream(SelectedKey);
			lit.UnionWith(Analysis.Downstream(SelectedKey));
		}
		var faded = new Color(1, 1, 1, 0.22f);
		foreach ((string key, GraphNode node) in _nodes)
			node.Modulate = lit == null || lit.Contains(key) ? Colors.White : faded;
		foreach ((string from, int fromPort, string to, int toPort) in _wired)
			_graph.SetConnectionActivity(_nodes[from].Name, fromPort, _nodes[to].Name, toPort,
				lit == null || (lit.Contains(from) && lit.Contains(to)) ? 0f : 1f);
	}

	private void Centre(string key)
	{
		if (!_nodes.TryGetValue(key, out GraphNode? node)) return;
		if (_graph.Zoom < 0.5f) _graph.Zoom = 0.8f;
		_graph.ScrollOffset = (node.PositionOffset + SizeOf(node) / 2f) * _graph.Zoom - _graph.Size / 2f;
	}

	/// <summary>Fits the whole web in the canvas (F).</summary>
	internal void FrameAll()
	{
		if (_nodes.Count == 0) return;
		Rect2 box = default;
		bool first = true;
		foreach (GraphNode node in _nodes.Values)
		{
			var rect = new Rect2(node.PositionOffset, SizeOf(node));
			box = first ? rect : box.Merge(rect);
			first = false;
		}
		Vector2 room = _graph.Size - new Vector2(120, 120);
		float zoom = Mathf.Clamp(Mathf.Min(room.X / box.Size.X, room.Y / box.Size.Y), _graph.ZoomMin, 1f);
		_graph.Zoom = zoom;
		_graph.ScrollOffset = box.GetCenter() * zoom - _graph.Size / 2f;
	}

	/// <summary>A node's size on the canvas; before its first layout pass, the size the arranger assumes.</summary>
	private static Vector2 SizeOf(GraphNode node)
	{
		if (node.Size.X > 1 && node.Size.Y > 1) return node.Size;
		return node switch
		{
			GoodNode => new Vector2(WebArrange.GoodWidth, WebArrange.GoodHeight),
			ConsumerNode => new Vector2(WebArrange.ConsumerWidth, WebArrange.ConsumerHeight),
			_ => new Vector2(WebArrange.RecipeWidth, WebArrange.RecipeHead + WebArrange.RecipeRow * 3),
		};
	}

	internal void ArrangeAll() => Change("arranged the web", Touch.Web, () =>
		WebArrange.Arrange(Catalogue, Web, key => _nodes.TryGetValue(key, out GraphNode? node) && node.Size.Y > 1 ? (node.Size.X, node.Size.Y) : null),
		keepInspector: true);

	private void RememberView()
	{
		if (Web == null || _graph == null) return;
		_prefs.SetValue("view", Web.Id, new Vector3(_graph.ScrollOffset.X, _graph.ScrollOffset.Y, _graph.Zoom));
	}

	/// <summary>Back to where this web was last looked at on this machine, or the whole of it; a frame late, once the canvas has a size.</summary>
	private async void RestoreView()
	{
		string id = Web.Id;
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (Web.Id != id) return;

		if (_shotAt == 0 && _prefs.HasSectionKey("view", id) && _prefs.GetValue("view", id).VariantType == Variant.Type.Vector3)
		{
			Vector3 view = _prefs.GetValue("view", id).AsVector3();
			_graph.Zoom = view.Z;
			_graph.ScrollOffset = new Vector2(view.X, view.Y);
		}
		else FrameAll();

		if (_shotAt == 0) return;
		if (_shotZoom > 0)
		{
			Vector2 middle = _graph.CanvasCentre;
			_graph.Zoom = _shotZoom;
			_graph.ScrollOffset = middle * _shotZoom - _graph.Size / 2f;
		}
		if (_shotSelect != null) Select(_shotSelect, focus: true);
	}

	/// <summary><c>show=</c> opens one of the lab's pop-ups for the picture: help, find, newweb, newgood, issues.</summary>
	private void ShowForShot()
	{
		switch (_shotShow)
		{
			case "help": ToggleHelp(); break;
			case "find": Find(); break;
			case "newweb": AskNewWeb(); break;
			case "newgood": AskNewGood(CentreSpot(), _ => { }); break;
			case "issues": ShowIssues(); break;
		}
		_shotShow = null;
	}

	// ---- gestures into changes -------------------------------------------------

	private enum Kind
	{
		None,
		Good,
		Recipe,
		Consumer,
	}

	/// <summary>What a key names, asked of the web rather than the canvas, so it holds for a node made a moment ago.</summary>
	private Kind KindOf(string key) =>
		Web.Holds(key) ? Kind.Good : Web.Recipe(key) != null ? Kind.Recipe : Web.Consumer(key) != null ? Kind.Consumer : Kind.None;

	/// <summary>A link dropped on a port: a good into a slot or a consumer, or a recipe's output into a good.</summary>
	internal void Link(string? from, int fromPort, string? to, int toPort)
	{
		if (from == null || to == null) return;
		switch (KindOf(from), KindOf(to))
		{
			case (Kind.Good, Kind.Recipe):
				Change($"linked {NameOf(from)} into {NameOf(to)}", Touch.Web, () =>
				{
					Recipe recipe = Web.Recipe(to)!;
					if (toPort < recipe.Inputs.Count) EconomyEdit.Accept(recipe.Inputs[toPort].Accepts, from);
					else recipe.Inputs.Add(new RecipeInput { Accepts = { from } });
				});
				break;
			case (Kind.Good, Kind.Consumer):
				Change($"{NameOf(from)} is consumed by {NameOf(to)}", Touch.Web, () => EconomyEdit.Accept(Web.Consumer(to)!.Accepts, from));
				break;
			case (Kind.Recipe, Kind.Good):
				Change($"{NameOf(from)} makes {NameOf(to)}", Touch.Web, () =>
				{
					Recipe recipe = Web.Recipe(from)!;
					if (fromPort < recipe.Outputs.Count) recipe.Outputs[fromPort].Good = to;
					else if (!recipe.Makes(to)) recipe.Outputs.Add(new RecipeOutput { Good = to });
				});
				break;
		}
	}

	/// <summary>A link pulled off its port. One a tag implies cannot be pulled off: the tag or the slot has to change.</summary>
	private void Unlink(string? from, int fromPort, string? to, int toPort)
	{
		if (from == null || to == null) return;
		if (KindOf(from) == Kind.Recipe)
		{
			Change($"{NameOf(from)} no longer makes {NameOf(to)}", Touch.Web, () =>
			{
				Recipe recipe = Web.Recipe(from)!;
				if (fromPort < recipe.Outputs.Count) recipe.Outputs.RemoveAt(fromPort);
			});
			return;
		}

		WebLink link = Analysis.Links.FirstOrDefault(l => l.From == from && l.To == to && l.Kind != LinkKind.Output && l.Port == toPort);
		if (link.From == null) return;
		if (link.ByTag)
		{
			Say($"That link comes from the tag {link.Via}: {NameOf(from)} carries it and the slot accepts it. Take the tag off the good, or change what the slot accepts.");
			return;
		}
		Change($"unlinked {NameOf(from)} from {NameOf(to)}", Touch.Web, () =>
		{
			if (Web.Consumer(to) is { } consumer) consumer.Accepts.Remove(from);
			else if (Web.Recipe(to) is { } recipe && toPort < recipe.Inputs.Count)
			{
				recipe.Inputs[toPort].Accepts.Remove(from);
				if (recipe.Inputs[toPort].Accepts.Count == 0) recipe.Inputs.RemoveAt(toPort);
			}
		});
	}

	/// <summary>A link dragged from a node's right side and let go over nothing: offer what could be there.</summary>
	private void LinkToNowhere(string? from, int fromPort, Vector2 at)
	{
		if (from == null) return;
		Spot spot = SpotAt(at);
		if (_nodes[from] is GoodNode)
		{
			ShowMenu(
				($"New recipe that uses {NameOf(from)}", () => Change($"new recipe using {NameOf(from)}", Touch.Web, () => SelectNew(EconomyEdit.NewRecipe(Web, null, from, spot).Id))),
				("New consumer of it", () => Change($"new consumer of {NameOf(from)}", Touch.Web, () =>
				{
					Consumer consumer = EconomyEdit.NewConsumer(Web, "Consumers", spot);
					consumer.Accepts.Add(from);
					SelectNew(consumer.Id);
				})));
		}
		else if (_nodes[from] is RecipeNode)
		{
			ShowMenu(
				("It makes a good from the catalogue…", () => AskGood("What does it make?", id => PlaceAndLink(id, spot, () => Link(from, fromPort, id, 0)))),
				("It makes a new good…", () => AskNewGood(spot, id => Link(from, fromPort, id, 0))));
		}
	}

	/// <summary>A link dragged from a node's left side and let go over nothing.</summary>
	private void LinkFromNowhere(string? to, int toPort, Vector2 at)
	{
		if (to == null) return;
		Spot spot = SpotAt(at);
		if (_nodes[to] is GoodNode)
		{
			ShowMenu(($"New recipe that makes {NameOf(to)}", () => Change($"new recipe making {NameOf(to)}", Touch.Web,
				() => SelectNew(EconomyEdit.NewRecipe(Web, to, null, new Spot(spot.X - (int)WebArrange.RecipeWidth, spot.Y)).Id))));
			return;
		}
		ShowMenu(
			("Takes a good from the catalogue…", () => AskGood("What goes in?", id => PlaceAndLink(id, new Spot(spot.X - (int)WebArrange.GoodWidth, spot.Y), () => Link(id, 0, to, toPort)))),
			("Takes anything with a tag…", () => AskTag("Any good tagged…", tag => AcceptTag(to, toPort, tag))),
			("Takes a new good…", () => AskNewGood(new Spot(spot.X - (int)WebArrange.GoodWidth, spot.Y), id => Link(id, 0, to, toPort))));
	}

	/// <summary>Makes a slot (or the open port's new slot, or a consumer) accept a tag.</summary>
	internal void AcceptTag(string nodeKey, int port, string tag)
	{
		Change($"{NameOf(nodeKey)} accepts #{tag}", Touch.Web, () =>
		{
			if (Web.Consumer(nodeKey) is { } consumer) EconomyEdit.Accept(consumer.Accepts, Acceptor.ForTag(tag));
			else if (Web.Recipe(nodeKey) is { } recipe)
			{
				if (port < recipe.Inputs.Count) EconomyEdit.Accept(recipe.Inputs[port].Accepts, Acceptor.ForTag(tag));
				else recipe.Inputs.Add(new RecipeInput { Accepts = { Acceptor.ForTag(tag) } });
			}
		});
	}

	/// <summary>Brings a catalogue good into the web if it is not there, then runs the link, as one step to undo.</summary>
	private void PlaceAndLink(string goodId, Spot spot, Action link)
	{
		if (Web.Holds(goodId))
		{
			link();
			return;
		}
		Change($"added {NameOf(goodId)} and linked it", Touch.Web, () =>
		{
			EconomyEdit.AddGood(Web, goodId, spot);
			link();
		});
	}

	private void DropGoods(string[] ids, Vector2 at)
	{
		List<string> fresh = ids.Where(id => !Web.Holds(id) && Catalogue.Find(id) != null).ToList();
		if (fresh.Count == 0)
		{
			if (ids.Length > 0) Select(ids[0], focus: true);
			Say(ids.Length == 1 ? $"{NameOf(ids[0])} is already in this web." : "Those are already in this web.");
			return;
		}
		AddGoods(fresh, new Spot((int)at.X, (int)at.Y));
	}

	/// <summary>
	/// Puts catalogue goods into the web at a spot, stacked downwards. With the palette's
	/// "with its chain" on, each comes with its recipes and everything upstream as the chosen web has it.
	/// </summary>
	internal void AddGoods(List<string> ids, Spot? at = null)
	{
		Spot spot = at ?? new Spot((int)_graph.CanvasCentre.X, (int)_graph.CanvasCentre.Y);
		EconomyWeb? source = ChainSource();
		string what = ids.Count == 1 ? $"added {NameOf(ids[0])}" : $"added {ids.Count} goods";
		Change(source == null ? what : what + $" with the chain from {source.Name}", Touch.Web, () =>
		{
			if (source != null)
			{
				Spot origin = source.Layout.GetValueOrDefault(ids[0]);
				EconomyEdit.CopyChain(Catalogue, source, Web, ids, withOptional: true, new Spot(spot.X - origin.X, spot.Y - origin.Y));
			}
			for (int i = 0; i < ids.Count; i++)
				EconomyEdit.AddGood(Web, ids[i], new Spot(spot.X, spot.Y + i * (int)(WebArrange.GoodHeight + 16)));
			SelectNew(ids[0]);
		});
	}

	/// <summary>Removes nodes from the web: goods leave the web and stay in the catalogue; recipes and consumers are deleted.</summary>
	internal void RemoveNodes(List<string> keys)
	{
		if (keys.Count == 0) keys = SelectedKeys();
		if (keys.Count == 0) return;
		string what = keys.Count == 1 ? $"removed {NameOf(keys[0])} from the web" : $"removed {keys.Count} nodes from the web";
		Change(what, Touch.Web, () =>
		{
			foreach (string key in keys)
			{
				if (Web.Holds(key)) EconomyEdit.RemoveGood(Web, key);
				else if (Web.Recipe(key) != null) EconomyEdit.RemoveRecipe(Web, key);
				else EconomyEdit.RemoveConsumer(Web, key);
			}
		});
	}

	/// <summary>Inside a change: the node about to exist becomes the selection once the canvas has it.</summary>
	private void SelectNew(string key) => Callable.From(() => Select(key)).CallDeferred();

	private Spot SpotAt(Vector2 local)
	{
		Vector2 canvas = _graph.ToCanvas(local);
		return new Spot((int)canvas.X, (int)canvas.Y);
	}

	/// <summary>What to call a node in a sentence: the good's or consumer's name, a recipe's title.</summary>
	internal string NameOf(string key)
	{
		if (Catalogue.Find(key) is { } good) return good.Name;
		if (Web.Recipe(key) is { } recipe) return "the recipe " + Analysis.TitleOf(recipe);
		if (Web.Consumer(key) is { } consumer) return consumer.Name.Length > 0 ? consumer.Name : "the consumer";
		return key;
	}

	// ---- menus -----------------------------------------------------------------

	/// <summary>Right-click on the canvas: on a link, offer to cut it; on empty canvas, offer what can be made there.</summary>
	private void CanvasMenu(Vector2 at)
	{
		Godot.Collections.Dictionary near = _graph.GetClosestConnectionAtPoint(at, 10f);
		if (near.Count > 0)
		{
			string? from = KeyOf(near["from_node"].AsStringName()), to = KeyOf(near["to_node"].AsStringName());
			int fromPort = near["from_port"].AsInt32(), toPort = near["to_port"].AsInt32();
			if (from != null && to != null)
			{
				ShowMenu(($"Cut the link from {NameOf(from)} to {NameOf(to)}", () => Unlink(from, fromPort, to, toPort)));
				return;
			}
		}

		Spot spot = SpotAt(at);
		var items = new List<(string, Action)>
		{
			("Add a good from the catalogue…", () => AskGood("Add which good?", id => AddGoods(new List<string> { id }, spot), notInWeb: true)),
			("New good…", () => AskNewGood(spot, _ => { })),
			("New recipe", () => Change("new recipe", Touch.Web, () => SelectNew(EconomyEdit.NewRecipe(Web, null, null, spot).Id))),
			("New consumer", () => Change("new consumer", Touch.Web, () => SelectNew(EconomyEdit.NewConsumer(Web, "Consumers", spot).Id))),
		};
		int selected = SelectedKeys().Count;
		if (selected > 0) items.Add((selected == 1 ? "Remove the selected node from the web" : $"Remove the {selected} selected nodes from the web", () => RemoveNodes(SelectedKeys())));
		ShowMenu(items.ToArray());
	}

	private void NodeInput(string key, InputEvent @event)
	{
		if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) return;
		if (!_nodes.TryGetValue(key, out GraphNode? node)) return;
		if (!node.Selected) Select(key);
		node.AcceptEvent();

		Vector2 beside = node.PositionOffset + new Vector2(SizeOf(node).X + 90, 0);
		Vector2 before = node.PositionOffset - new Vector2(WebArrange.RecipeWidth + 90, 0);
		var items = new List<(string, Action)>();
		if (node is GoodNode)
		{
			items.Add(("New recipe that makes it", () => Change($"new recipe making {NameOf(key)}", Touch.Web,
				() => SelectNew(EconomyEdit.NewRecipe(Web, key, null, new Spot((int)before.X, (int)before.Y)).Id))));
			items.Add(("New recipe that uses it", () => Change($"new recipe using {NameOf(key)}", Touch.Web,
				() => SelectNew(EconomyEdit.NewRecipe(Web, null, key, new Spot((int)beside.X, (int)beside.Y)).Id))));
			items.Add(("Remove from this web", () => RemoveNodes(new List<string> { key })));
		}
		else items.Add((node is RecipeNode ? "Delete this recipe" : "Delete this consumer", () => RemoveNodes(new List<string> { key })));

		int selected = SelectedKeys().Count;
		if (selected > 1) items.Add(($"Remove all {selected} selected nodes", () => RemoveNodes(SelectedKeys())));
		ShowMenu(items.ToArray());
	}

	/// <summary>A menu under the mouse; each row runs its action.</summary>
	internal void ShowMenu(params (string Text, Action Do)[] items)
	{
		_menu.Clear();
		_menuActions.Clear();
		foreach ((string text, Action action) in items)
		{
			_menu.AddItem(text, _menuActions.Count);
			_menuActions.Add(action);
		}
		_menu.ResetSize();
		_menu.Popup(new Rect2I((Vector2I)GetViewport().GetMousePosition(), Vector2I.Zero));
	}

	// ---- pickers ---------------------------------------------------------------

	/// <summary>Asks for a good of the catalogue. Goods not in this web say so; with <paramref name="notInWeb"/> only those are offered.</summary>
	internal void AskGood(string prompt, Action<string> then, bool notInWeb = false)
	{
		IEnumerable<(string, string, Texture2D?)> items = Catalogue.Goods
			.Where(g => !notInWeb || !Web.Holds(g.Id))
			.OrderBy(g => Web.Holds(g.Id) ? 0 : 1).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
			.Select(g => (g.Id, Web.Holds(g.Id) || notInWeb ? g.Name : g.Name + "   (not in this web)", Sprites.Get(g.Icon)));
		_picker.Ask(prompt, items, GetViewport().GetMousePosition(), then);
	}

	/// <summary>Asks for a tag: one in use, or a new one typed in.</summary>
	internal void AskTag(string prompt, Action<string> then)
	{
		IEnumerable<(string, string, Texture2D?)> items = Catalogue.TagsInUse().Select(t => (t.Tag, $"#{t.Tag}   ({t.Count})", (Texture2D?)null));
		_picker.Ask(prompt, items, GetViewport().GetMousePosition(), then, typed => then(TidyTag(typed)));
	}

	/// <summary>A typed tag made fit to store: lowercase, hyphens for spaces, no hash.</summary>
	internal static string TidyTag(string typed)
	{
		string tag = typed.Trim().TrimStart(Acceptor.TagMark).ToLowerInvariant();
		int colon = tag.IndexOf(':');
		return colon < 0 ? EconomyEdit.Slug(tag) : EconomyEdit.Slug(tag[..colon]) + ":" + EconomyEdit.Slug(tag[(colon + 1)..]);
	}

	/// <summary>Jump to a node of this web by name (⌘F).</summary>
	internal void Find()
	{
		IEnumerable<(string, string, Texture2D?)> items = Web.Goods.Select(id => Catalogue.Find(id)).Where(g => g != null)
			.Select(g => (g!.Id, g.Name, Sprites.Get(g.Icon)))
			.Concat(Web.Recipes.Select(r => (r.Id, "recipe " + Analysis.TitleOf(r), (Texture2D?)null)))
			.Concat(Web.Consumers.Select(c => (c.Id, "consumer " + c.Name, (Texture2D?)null)));
		_picker.Ask("Go to…", items, _graph.GlobalPosition + new Vector2(_graph.Size.X / 2f - 160, 40), key => Select(key, focus: true));
	}
}
