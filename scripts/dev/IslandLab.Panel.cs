using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

/// <summary>
/// The control panel and the two text plates. Every widget and every key write the
/// same <see cref="Params"/>; <see cref="Sync"/> pulls the widgets back into line.
/// </summary>
public partial class IslandLab
{
	/// <summary>Pixels of screen the control column takes, whatever the window is.</summary>
	private const int PanelWidth = 330;

	private Label _status = null!;
	private RichTextLabel _legend = null!;
	private PanelContainer _panel = null!;
	private OptionButton _viewPick = null!, _arrangePick = null!, _characterPick = null!;
	private OptionButton _entryKind = null!, _entryEdge = null!, _crossings = null!;
	private OptionButton _exitKind = null!;
	private HSlider _hilliness = null!, _mix = null!, _relief = null!, _wet = null!;
	private HSlider _lakes = null!, _valleys = null!, _moisture = null!, _warmth = null!;
	private SpinBox _rungs = null!, _cliff = null!, _patch = null!, _exits = null!;
	private OptionButton _size = null!;
	private Label _sizeCaption = null!, _poolNote = null!;
	private CheckBox _gooBox = null!;
	private CheckBox _newShapes = null!, _bridgeBox = null!, _stripBox = null!;
	private CheckBox _ferryBox = null!, _roadBox = null!, _compassBox = null!, _fordBox = null!;
	private CheckBox _liquidBox = null!;
	private LineEdit _seedField = null!;
	private bool _syncing;

	/// <summary>
	/// The 0–1 knobs: each a slider, its caption, its Auto box, and how to read
	/// what the seed rolled for it off a built island's settings.
	/// </summary>
	private readonly List<(HSlider Slider, Label Caption, CheckBox Auto, string Text,
	                       Func<float> Read, Action<float> Write, Func<IslandParams, float> Rolled)> _knobs = new();

	/// <summary>The island last built, whose settings say what Auto rolled.</summary>
	private IslandData? _rolledFrom;

	private void BuildOverlayUi()
	{
		var layer = new CanvasLayer();
		AddChild(layer);

		var frame = new MarginContainer();
		frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		foreach (string side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
			frame.AddThemeConstantOverride(side, 10);
		frame.MouseFilter = Control.MouseFilterEnum.Ignore;
		layer.AddChild(frame);

		var columns = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		columns.AddThemeConstantOverride("separation", 10);
		frame.AddChild(columns);

		// ---- left: the controls ------------------------------------------------
		_panel = new PanelContainer
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(PanelWidth, 0),
		};
		_panel.AddThemeStyleboxOverride("panel", Plate());
		columns.AddChild(_panel);

		var scroll = new ScrollContainer
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		_panel.AddChild(scroll);

		var rows = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		rows.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(rows);

		var doing = new HBoxContainer();
		rows.AddChild(doing);
		AddButton(doing, "New seed  (N)", () => { Seed = (int)(GD.Randi() & 0x7FFFFFFF); Sync(); });
		AddButton(doing, "Frame  (F)", () => _rig.Frame(_islandCenter, _islandRadius));
		AddButton(doing, "Rebuild  (R)", Rebuild);

		// The seed as a field, so a seed named in the audit, a commit or a screenshot can
		// be typed back in and built against whatever the parameters below happen to say.
		rows.AddChild(Caption("Seed  (Enter builds it)"));
		var seedRow = new HBoxContainer();
		seedRow.AddThemeConstantOverride("separation", 4);
		rows.AddChild(seedRow);
		_seedField = new LineEdit
		{
			Text = SeedText,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsStretchRatio = 2f,
			TooltipText = "A whole number. Generation is a pure function of this and the "
				+ "parameters below, so the same pair always gives the same Domain; "
				+ "anything that is not a whole number is put back.",
		};
		_seedField.TextSubmitted += _ => UseTypedSeed();
		seedRow.AddChild(_seedField);
		AddButton(seedRow, "Build", UseTypedSeed);

		Heading(rows, "what the island is");
		_viewPick = Choice<View>(rows, "View  (C)", () => _view,
			v => { _view = v; Rebuild(); },
			"Which field the island is coloured by. The legend on the right names the colours.");
		// A dropdown over the supported footprints only: the pipeline is audited at exactly these.
		_sizeCaption = Caption("Size, cells",
			"Footprint edge, in cells; altitude is bounded by the same number of slabs. "
			+ "These three are the audited footprints. Auto rolls one of them per seed, "
			+ "and the caption says which once the island is built.");
		rows.AddChild(_sizeCaption);
		_size = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_size.AddItem("Auto", IslandParams.SizeAuto);
		foreach (int s in IslandParams.SupportedSizes)
			_size.AddItem($"{s} × {s}  ({s} slabs tall)", s);
		_size.Selected = _size.GetItemIndex(Params.Size);
		_size.ItemSelected += _ => { if (!_syncing) Params.Size = _size.GetSelectedId(); };
		rows.AddChild(_size);
		_arrangePick = Choice<IslandArrangement>(rows, "Arrangement  (G)",
			() => Params.Arrangement, v => Params.Arrangement = v,
			"How the land is laid out. Auto picks one per seed; every arrangement is "
			+ "linkable by bridge.");
		_characterPick = Choice<TerrainCharacter>(rows, "Character  (V)",
			() => Params.Character, v => Params.Character = v,
			"Which landforms the island is built from. Auto picks one per seed, and the "
			+ "relief style follows from it.");

		_newShapes = Check(rows, "Auto may roll the newer shapes  (U)",
			() => Params.NewArrangements && Params.NewLandforms,
			on => { Params.NewArrangements = on; Params.NewLandforms = on; },
			"Gates the dice, not the code. It widens the pool Auto draws from; "
			+ "naming an arrangement or a character by hand still builds it, and "
			+ "with both named this does nothing at all.");

		_poolNote = Caption("");
		_poolNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_poolNote.AddThemeColorOverride("font_color", new Color(0.62f, 0.78f, 0.95f));
		_poolNote.AddThemeFontSizeOverride("font_size", 12);
		rows.AddChild(_poolNote);

		Heading(rows, "relief");
		_hilliness = Slide(rows, "Hilliness  (H)", 0f, 1f, 0.05f,
			() => Params.Hilliness, v => Params.Hilliness = v, q => q.Hilliness,
			"What hills do: 0 low swells, 1 steep mounds, in one-slab steps either way. "
			+ "Also sets how jagged the surface noise is.");
		_mix = Slide(rows, "Landform mix  (M)", 0f, 1f, 0.05f,
			() => Params.LandformMix, v => Params.LandformMix = v, q => q.LandformMix,
			"The quota of high ground: 0 mostly plains, 1 as much as the character allows. "
			+ "Every landform the character names appears at any setting.");
		_relief = Slide(rows, "Relief", 0f, 1f, 0.05f,
			() => Params.Relief, v => Params.Relief = v, q => q.Relief,
			"Vertical exaggeration of every landform's relief.");
		_wet = Slide(rows, "Rivers", 0f, 1f, 0.05f,
			() => Params.Rivers, v => Params.Rivers = v, q => q.Rivers,
			"How wet the Domain is: the catchment a channel needs before it counts as a river.");
		_lakes = Slide(rows, "Lakes", 0f, 1f, 0.05f,
			() => Params.Lakes, v => Params.Lakes = v, q => q.Lakes,
			"How readily standing water collects: 0 no lakes, 1 one in every flat patch "
			+ "that could hold one.");
		_valleys = Slide(rows, "Valleys", 0f, 1f, 0.05f,
			() => Params.Valleys, v => Params.Valleys = v, q => q.Valleys,
			"How far the ground falls toward a watercourse: 0 a bare incision, 1 five cells "
			+ "of valley either side.");
		_gooBox = Check(rows, "Goo may roll",
			() => Params.Goo, on => Params.Goo = on,
			"About three islands in ten roll goo puddles instead of more lakes. Unticked, "
			+ "no Domain gets goo whatever the seed says; the same seed with the box on "
			+ "is the same island with its puddles back.");
		_rungs = Spin(rows, "Plateau rungs  (L)", 1, 8,
			() => Params.PlateauLevels, v => Params.PlateauLevels = v,
			"Rungs on the plateau ladder above the coastal level: how terraced the island is.");
		_cliff = Spin(rows, "Cliff height, slabs", 3, 16,
			() => Params.CliffHeight, v => Params.CliffHeight = v,
			"One rung of the plateau ladder, in slabs. Three or more, so every ladder "
			+ "border is an unambiguous cliff.");
		_patch = Spin(rows, "Region scale, cells", 6, 40,
			() => Params.RegionScale, v => Params.RegionScale = v,
			"Typical width of one landform region. The smallest region allowed follows "
			+ "from it: max(12, 0.215 × scale²) cells.");

		Heading(rows, "climate");
		_moisture = Slide(rows, "Background moisture", 0f, 1f, 0.05f,
			() => Params.Moisture, v => Params.Moisture = v, q => q.Moisture,
			"What the ground has before its water adds any: 0.15 is dry country, 0.45 "
			+ "balanced, 0.75 wet. The water always adds its own strip, so even a dry "
			+ "Domain has fertile banks.");
		_warmth = Slide(rows, "Background warmth", 0f, 1f, 0.05f,
			() => Params.Warmth, v => Params.Warmth = v, q => q.Warmth,
			"The open lowland: under about 0.3 is cold country, 0.5 temperate, over about "
			+ "0.7 hot, the last twentieth sand. Even 0 keeps its lowland above the snow — "
			+ "the snow on the map is a mountain's upper part, at every footprint.");
		AddButton(rows, "All knobs to auto", AllKnobsAuto);

		Heading(rows, "gates and crossings");
		_entryKind = Choice<GateKind>(rows, "Entry gate  (T)",
			() => Params.EntryGate, v => Params.EntryGate = v,
			"The Gate you arrive through. An input, set by the Domain that sent you; "
			+ "Auto is for a Home Domain.");
		_entryEdge = Choice<GateEdge>(rows, "Entry edge",
			() => Params.EntryEdge, v => Params.EntryEdge = v,
			"The edge you arrive on, an input for the same reason. Auto tries every edge; "
			+ "a named edge falls back to the others if its coast cannot host a Gate.");
		_exits = Spin(rows, "Exit gates  (0 = per seed)", 0, 3,
			() => Params.ExitGates, v => Params.ExitGates = v,
			"Links onward, one per edge. 0 takes the count from the seed.");
		_exitKind = Choice<GateKind>(rows, "Exit gates are",
			() => Params.ExitGate, v => Params.ExitGate = v,
			"What kind the Exits are. Auto hangs them unless a coast will not have it; "
			+ "a named kind applies where the coast allows.");
		_crossings = Choice<BridgeEase>(rows, "Crossings  (Y)",
			() => Params.Crossings, v => Params.Crossings = v,
			"Cells one bridge may span: Easy 1, Medium 3, Hard 6. Also how far apart the "
			+ "linker leaves the landmasses and what counts as reachable, so an Easy Domain "
			+ "has no navigable rivers.");

		Heading(rows, "overlays");
		_bridgeBox = Check(rows, "Bridge sites  (B)",
			() => _showBridges, on => { _showBridges = on; Redraw(); },
			"Every crossing site the analysis found: the deck bank to bank, and a marker "
			+ "on each bank.");
		_stripBox = Check(rows, "Gate landings  (J)",
			() => _showLandings, on => { _showLandings = on; Redraw(); },
			"The 1 × 3 strip running inland from each Gate, levelled for it.");
		_ferryBox = Check(rows, "Ferry berths  (K)",
			() => _showFerries, on => { _showFerries = on; Redraw(); },
			"Each berth as a pair: the quay on land, the hull on the water in front of it.");
		_roadBox = Check(rows, "Roads between gates  (P)",
			() => _showRoutes, on => { _showRoutes = on; Redraw(); },
			"The least-works road from the Entry to each Exit: pale yellow walk, red stair, "
			+ "gold bridge, cyan ferry.");
		_fordBox = Check(rows, "Fords  (O)",
			() => _showFords, on => { _showFords = on; Redraw(); },
			"Stream cells crossable on foot: one at the head of each course and one every "
			+ "11 cells along it. A stream is an obstacle everywhere else.");
		_compassBox = Check(rows, "Compass, wind and gate vectors  (X)",
			() => _showCompass, on => { _showCompass = on; Redraw(); },
			"N/E/S/W, each Gate's landward vector, the wind and the grain of any dune field, "
			+ "and two boxes: the Domain's cube and one tight round the landmass.");
		_liquidBox = Check(rows, "Liquid: water, goo, falls  (I)",
			() => _showLiquid, on => { _showLiquid = on; Redraw(); },
			"Off shows the beds under the water: the columns are always drawn.");

		Heading(rows, "camera");
		var keys = new Label
		{
			Text = "WASD move   Q/E rotate   MMB-drag rotate and tilt\n"
				 + "arrows tilt   wheel zoom   Shift faster\n"
				 + "Tab or F1 hides this panel   F2 saves a screenshot",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		keys.AddThemeColorOverride("font_color", new Color(0.72f, 0.74f, 0.78f));
		rows.AddChild(keys);

		// ---- right: what the island turned out to be ---------------------------
		var right = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		right.AddThemeConstantOverride("separation", 8);
		columns.AddChild(right);

		// Both plates at the top: the editor's chrome hides the bottom of the embedded game.
		_legend = PanelledRich(right, new Color(0.82f, 0.92f, 1f));
		_status = Panelled(right, Control.SizeFlags.ExpandFill, new Color(1f, 0.93f, 0.72f));
		right.AddChild(new Control
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});

		_status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		ShowLegend(ViewLegend(_view));
		_status.Text = "";
		Sync();
	}

	/// <summary>
	/// Lays the legend out: BBCode text, and an inline image tinted to the colour
	/// wherever the markup says <c>{#rrggbb}</c>. An image, unlike a run of
	/// background-coloured spaces, survives being at a line wrap.
	/// </summary>
	private void ShowLegend(string markup)
	{
		if (_swatch == null)
		{
			var white = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
			white.Fill(Colors.White);
			_swatch = ImageTexture.CreateFromImage(white);
		}

		_legend.Clear();
		int at = 0;
		while (at < markup.Length)
		{
			int open = markup.IndexOf("{#", at, StringComparison.Ordinal);
			int close = open < 0 ? -1 : markup.IndexOf('}', open);
			if (open < 0 || close < 0)
			{
				_legend.AppendText(markup[at..]);
				break;
			}
			if (open > at) _legend.AppendText(markup[at..open]);
			_legend.AddImage(_swatch, 14, 14, Color.FromHtml(markup[(open + 1)..close]));
			at = close + 1;
		}
	}

	private ImageTexture? _swatch;

	/// <summary>The seed as the field shows it; culture-invariant, since this machine's is not.</summary>
	private string SeedText => Seed.ToString(System.Globalization.CultureInfo.InvariantCulture);

	/// <summary>
	/// Takes the seed typed into the panel. The rebuild is not done here: setting
	/// <see cref="Seed"/> moves the signature and <c>_Process</c> notices, the same
	/// path a remote-inspector edit takes. Anything that is not a whole number is put
	/// back, because a half-typed seed is not a Domain.
	/// </summary>
	private void UseTypedSeed()
	{
		if (int.TryParse(_seedField.Text.Trim(), System.Globalization.NumberStyles.Integer,
				System.Globalization.CultureInfo.InvariantCulture, out int typed))
		{
			Seed = typed;
			GD.Print($"[IslandLab] seed {Seed} from the panel");
		}
		else
		{
			GD.Print($"[IslandLab] '{_seedField.Text}' is not a whole number; kept seed {Seed}");
			_seedField.Text = SeedText;
		}
		Sync();
	}

	/// <summary>
	/// Pulls every widget back into line with what it displays. The <see cref="_syncing"/>
	/// guard stops the write coming back round as a change signal.
	/// </summary>
	private void Sync()
	{
		if (_viewPick == null || Params == null) return;
		_syncing = true;

		_viewPick.Selected = (int)_view;
		_arrangePick.Selected = _arrangePick.GetItemIndex((int)Params.Arrangement);
		_characterPick.Selected = _characterPick.GetItemIndex((int)Params.Character);
		_entryKind.Selected = _entryKind.GetItemIndex((int)Params.EntryGate);
		_entryEdge.Selected = _entryEdge.GetItemIndex((int)Params.EntryEdge);
		_crossings.Selected = _crossings.GetItemIndex((int)Params.Crossings);
		_exitKind.Selected = _exitKind.GetItemIndex((int)Params.ExitGate);

		SyncKnobs();
		_size.Selected = _size.GetItemIndex(Params.Size);
		_gooBox.ButtonPressed = Params.Goo;
		_rungs.Value = Params.PlateauLevels;
		_cliff.Value = Params.CliffHeight;
		_patch.Value = Params.RegionScale;
		_exits.Value = Params.ExitGates;

		// Not while it is being typed into: N and the keys write the seed too.
		if (!_seedField.HasFocus()) _seedField.Text = SeedText;

		_newShapes.ButtonPressed = Params.NewArrangements && Params.NewLandforms;
		_poolNote.Text = PoolNote();
		_bridgeBox.ButtonPressed = _showBridges;
		_stripBox.ButtonPressed = _showLandings;
		_ferryBox.ButtonPressed = _showFerries;
		_roadBox.ButtonPressed = _showRoutes;
		_compassBox.ButtonPressed = _showCompass;
		_fordBox.ButtonPressed = _showFords;
		_liquidBox.ButtonPressed = _showLiquid;

		_syncing = false;
	}

	private static void Heading(Container into, string text)
	{
		var label = new Label { Text = text.ToUpperInvariant() };
		label.AddThemeColorOverride("font_color", new Color(0.62f, 0.78f, 0.95f));
		label.AddThemeFontSizeOverride("font_size", 12);
		into.AddChild(new HSeparator());
		into.AddChild(label);
	}

	private static void AddButton(Container into, string text, Action pressed)
	{
		var button = new Button { Text = text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		button.Pressed += pressed;
		into.AddChild(button);
	}

	/// <summary>A labelled dropdown over an enum, with <c>Auto</c> first where there is one.</summary>
	private OptionButton Choice<T>(Container into, string text, Func<T> read, Action<T> write,
								   string? tip = null)
		where T : struct, Enum
	{
		into.AddChild(Caption(text, tip));
		var pick = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		foreach (T value in Enum.GetValues<T>())
			pick.AddItem(Spaced(value.ToString()!), Convert.ToInt32(value));
		pick.Selected = pick.GetItemIndex(Convert.ToInt32(read()));
		pick.ItemSelected += _ =>
		{
			if (_syncing) return;
			write((T)Enum.ToObject(typeof(T), pick.GetSelectedId()));
		};
		// Capped so thirty arrangements scroll instead of running off the screen.
		pick.GetPopup().MaxSize = new Vector2I(480, 440);
		pick.TooltipText = tip ?? "";
		into.AddChild(pick);
		return pick;
	}

	/// <summary>
	/// A 0–1 knob with an Auto box beside it. Ticked, the seed rolls the knob, the
	/// slider is greyed and, once the island is built, sits at what the seed rolled
	/// (<paramref name="rolled"/> reads that off the island's settings). Unticking it
	/// freezes the knob at the value the slider shows, so a rolled setting can be
	/// kept and nudged. Dragging a greyed slider does nothing; untick first.
	/// </summary>
	private HSlider Slide(Container into, string text, float min, float max, float step,
						  Func<float> read, Action<float> write, Func<IslandParams, float> rolled,
						  string? tip = null)
	{
		tip = (tip ?? "") + " Ticked auto, the seed rolls it and the slider shows the roll "
			+ "once the island is built; untick to keep that value and set it yourself.";
		var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		Label caption = Caption(KnobCaption(text, read()), tip);
		caption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddChild(caption);
		var auto = new CheckBox
		{
			Text = "auto",
			ButtonPressed = read() < 0f,
			TooltipText = tip,
			FocusMode = Control.FocusModeEnum.None,
		};
		row.AddChild(auto);
		into.AddChild(row);

		float shown = read() < 0f ? 0.5f : read();
		var slider = new HSlider
		{
			MinValue = min,
			MaxValue = max,
			Step = step,
			Value = shown,
			Editable = read() >= 0f,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 18),
			TooltipText = tip,
		};
		slider.ValueChanged += v =>
		{
			if (!auto.ButtonPressed) caption.Text = KnobCaption(text, (float)v);
			if (!_syncing && !auto.ButtonPressed) write((float)v);
		};
		auto.Toggled += on =>
		{
			slider.Editable = !on;
			if (_syncing) return;
			write(on ? IslandParams.Auto : (float)slider.Value);
			caption.Text = on ? KnobCaption(text, IslandParams.Auto) : KnobCaption(text, (float)slider.Value);
		};
		into.AddChild(slider);
		_knobs.Add((slider, caption, auto, text, read, write, rolled));
		return slider;
	}

	private static string KnobCaption(string text, float v)
		=> v < 0f ? $"{text}   auto" : $"{text}   {v:0.00}";

	/// <summary>
	/// Puts every knob's controls where its parameter is: the Auto box from the sign,
	/// the slider at the value, or at what the seed rolled for an Auto knob once an
	/// island has been built, so the slider's position is true to the island shown.
	/// </summary>
	private void SyncKnobs()
	{
		IslandParams? rolled = _rolledFrom?.Settings;
		foreach (var (slider, caption, auto, text, read, _, roll) in _knobs)
		{
			float v = read();
			bool isAuto = v < 0f;
			auto.ButtonPressed = isAuto;
			slider.Editable = !isAuto;
			if (!isAuto) slider.Value = v;
			else if (rolled != null) slider.Value = roll(rolled);
			caption.Text = isAuto && rolled != null
				? $"{text}   auto -> {roll(rolled):0.00}"
				: KnobCaption(text, v);
		}

		// The footprint is a knob too, on a dropdown: Auto's caption says what it rolled.
		if (_sizeCaption != null)
			_sizeCaption.Text = Params.Size > 0 ? "Size, cells"
				: rolled != null ? $"Size, cells   auto -> {rolled.Size}"
				: "Size, cells   auto";
	}

	/// <summary>After a build: the Auto knobs' sliders and captions take what the seed rolled.</summary>
	private void ShowRolled(IslandData d)
	{
		_rolledFrom = d;
		_syncing = true;
		SyncKnobs();
		_syncing = false;
	}

	/// <summary>Every 0–1 knob back to Auto, as the preset has them.</summary>
	private void AllKnobsAuto()
	{
		foreach (var knob in _knobs) knob.Write(IslandParams.Auto);
		Sync();
	}

	private SpinBox Spin(Container into, string text, int min, int max,
						 Func<int> read, Action<int> write, string? tip = null)
	{
		var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		Label caption = Caption(text, tip);
		caption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddChild(caption);

		var spin = new SpinBox
		{
			MinValue = min,
			MaxValue = max,
			Step = 1,
			Value = read(),
			TooltipText = tip ?? "",
		};
		spin.ValueChanged += v => { if (!_syncing) write((int)v); };
		row.AddChild(spin);
		into.AddChild(row);
		return spin;
	}

	private CheckBox Check(Container into, string text, Func<bool> read, Action<bool> write,
						   string? tip = null)
	{
		var box = new CheckBox { Text = text, ButtonPressed = read(), TooltipText = tip ?? "" };
		box.Toggled += on => { if (!_syncing) write(on); };
		into.AddChild(box);
		return box;
	}

	/// <summary>
	/// A knob's name. A caption with a tooltip is set to <c>Pass</c>, not the Label
	/// default of <c>Ignore</c>: a tooltip needs the mouse, and <c>Pass</c> still lets
	/// the wheel through to the scroll container behind it.
	/// </summary>
	private static Label Caption(string text, string? tip = null)
	{
		var label = new Label { Text = text };
		label.AddThemeColorOverride("font_color", new Color(0.85f, 0.87f, 0.9f));
		label.AddThemeFontSizeOverride("font_size", 13);
		if (tip != null)
		{
			label.TooltipText = tip;
			label.MouseFilter = Control.MouseFilterEnum.Pass;
		}
		return label;
	}

	/// <summary>"BrokenRing" reads as "Broken Ring" in a list a human has to scan.</summary>
	private static string Spaced(string name)
	{
		var text = new System.Text.StringBuilder(name.Length + 4);
		for (int i = 0; i < name.Length; i++)
		{
			if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) text.Append(' ');
			text.Append(name[i]);
		}
		return text.ToString();
	}

	private static StyleBoxFlat Plate() => new()
	{
		BgColor = new Color(0f, 0f, 0f, 0.62f),
		ContentMarginLeft = 10,
		ContentMarginRight = 10,
		ContentMarginTop = 8,
		ContentMarginBottom = 8,
		CornerRadiusTopLeft = 5,
		CornerRadiusTopRight = 5,
		CornerRadiusBottomLeft = 5,
		CornerRadiusBottomRight = 5,
	};

	/// <summary>The legend: BBCode on a dark plate, so its colour swatches are the view's own colours.</summary>
	private static RichTextLabel PanelledRich(Container into, Color tint)
	{
		var panel = new PanelContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		panel.AddThemeStyleboxOverride("panel", Plate());
		into.AddChild(panel);

		var label = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			ScrollActive = false,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(420, 0),
		};
		label.AddThemeColorOverride("default_color", tint);
		panel.AddChild(label);
		return label;
	}

	/// <summary>One label on a dark plate, so text stays readable over pale terrain.</summary>
	private static Label Panelled(Container into, Control.SizeFlags flags, Color tint)
	{
		var panel = new PanelContainer
		{
			SizeFlagsHorizontal = flags,
			SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		panel.AddThemeStyleboxOverride("panel", Plate());
		into.AddChild(panel);

		var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
		label.AddThemeColorOverride("font_color", tint);
		panel.AddChild(label);
		return label;
	}
}
