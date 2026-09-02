using System;
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
	private Label _poolNote = null!;
	private CheckBox _newShapes = null!, _bridgeBox = null!, _stripBox = null!;
	private CheckBox _ferryBox = null!, _roadBox = null!, _compassBox = null!, _fordBox = null!;
	private CheckBox _liquidBox = null!;
	private bool _syncing;

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

		Heading(rows, "what the island is");
		_viewPick = Choice<View>(rows, "View  (C)", () => _view,
			v => { _view = v; Rebuild(); });
		// A dropdown over the supported footprints only: the pipeline is audited at exactly these.
		rows.AddChild(Caption("Size, cells"));
		_size = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		foreach (int s in IslandParams.SupportedSizes)
			_size.AddItem($"{s} × {s}  ({s} slabs tall)", s);
		_size.Selected = _size.GetItemIndex(Params.Size);
		_size.ItemSelected += _ => { if (!_syncing) Params.Size = _size.GetSelectedId(); };
		rows.AddChild(_size);
		_arrangePick = Choice<IslandArrangement>(rows, "Arrangement  (G)",
			() => Params.Arrangement, v => Params.Arrangement = v);
		_characterPick = Choice<TerrainCharacter>(rows, "Character  (V)",
			() => Params.Character, v => Params.Character = v);

		_newShapes = Check(rows, "Auto may roll the newer shapes  (U)",
			() => Params.NewArrangements && Params.NewLandforms,
			on => { Params.NewArrangements = on; Params.NewLandforms = on; });
		_newShapes.TooltipText =
			"Gates the dice, not the code. It widens the pool Auto draws from; "
			+ "naming an arrangement or a character by hand still builds it, and "
			+ "with both named this does nothing at all.";

		_poolNote = Caption("");
		_poolNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_poolNote.AddThemeColorOverride("font_color", new Color(0.62f, 0.78f, 0.95f));
		_poolNote.AddThemeFontSizeOverride("font_size", 12);
		rows.AddChild(_poolNote);

		Heading(rows, "relief");
		_hilliness = Slide(rows, "Hilliness  (H)", 0f, 1f, 0.05f,
			() => Params.Hilliness, v => Params.Hilliness = v);
		_mix = Slide(rows, "Landform mix  (M)", 0f, 1f, 0.05f,
			() => Params.LandformMix, v => Params.LandformMix = v);
		_relief = Slide(rows, "Relief", 0f, 1f, 0.05f,
			() => Params.Relief, v => Params.Relief = v);
		_wet = Slide(rows, "Rivers", 0f, 1f, 0.05f,
			() => Params.Rivers, v => Params.Rivers = v);
		_lakes = Slide(rows, "Lakes", 0f, 1f, 0.05f,
			() => Params.Lakes, v => Params.Lakes = v);
		_valleys = Slide(rows, "Valleys", 0f, 1f, 0.05f,
			() => Params.Valleys, v => Params.Valleys = v);
		_rungs = Spin(rows, "Plateau rungs  (L)", 1, 8,
			() => Params.PlateauLevels, v => Params.PlateauLevels = v);
		_cliff = Spin(rows, "Cliff height, slabs", 3, 16,
			() => Params.CliffHeight, v => Params.CliffHeight = v);
		_patch = Spin(rows, "Region scale, cells", 6, 40,
			() => Params.RegionScale, v => Params.RegionScale = v);

		Heading(rows, "climate");
		_moisture = Slide(rows, "Background moisture", 0f, 1f, 0.05f,
			() => Params.Moisture, v => Params.Moisture = v);
		_moisture.TooltipText = "What the ground has before its water adds any: 0.15 is dry country, 0.65 wet.";
		_warmth = Slide(rows, "Background warmth", 0f, 1f, 0.05f,
			() => Params.Warmth, v => Params.Warmth = v);
		_warmth.TooltipText = "The lowland before the lapse and the chills: 1 is the warmest, 0.7 a cold country.";

		Heading(rows, "gates and crossings");
		_entryKind = Choice<GateKind>(rows, "Entry gate  (T)",
			() => Params.EntryGate, v => Params.EntryGate = v);
		_entryEdge = Choice<GateEdge>(rows, "Entry edge",
			() => Params.EntryEdge, v => Params.EntryEdge = v);
		_exits = Spin(rows, "Exit gates  (0 = per seed)", 0, 3,
			() => Params.ExitGates, v => Params.ExitGates = v);
		_exitKind = Choice<GateKind>(rows, "Exit gates are",
			() => Params.ExitGate, v => Params.ExitGate = v);
		_crossings = Choice<BridgeEase>(rows, "Crossings  (Y)",
			() => Params.Crossings, v => Params.Crossings = v);

		Heading(rows, "overlays");
		_bridgeBox = Check(rows, "Bridge sites  (B)",
			() => _showBridges, on => { _showBridges = on; Redraw(); });
		_stripBox = Check(rows, "Gate landings  (J)",
			() => _showLandings, on => { _showLandings = on; Redraw(); });
		_ferryBox = Check(rows, "Ferry berths  (K)",
			() => _showFerries, on => { _showFerries = on; Redraw(); });
		_roadBox = Check(rows, "Roads between gates  (P)",
			() => _showRoutes, on => { _showRoutes = on; Redraw(); });
		_fordBox = Check(rows, "Fords  (O)",
			() => _showFords, on => { _showFords = on; Redraw(); });
		_compassBox = Check(rows, "Compass, wind and gate vectors  (X)",
			() => _showCompass, on => { _showCompass = on; Redraw(); });
		_liquidBox = Check(rows, "Liquid: water, goo, falls  (I)",
			() => _showLiquid, on => { _showLiquid = on; Redraw(); });
		_liquidBox.TooltipText = "Off shows the beds under the water: the columns are always drawn.";

		Heading(rows, "camera");
		var keys = new Label
		{
			Text = "WASD move   Q/E rotate   MMB-drag rotate and tilt\n"
				 + "arrows tilt   wheel zoom   Shift faster\n"
				 + "Tab hides this panel",
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

		_hilliness.Value = Params.Hilliness;
		_mix.Value = Params.LandformMix;
		_relief.Value = Params.Relief;
		_wet.Value = Params.Rivers;
		_lakes.Value = Params.Lakes;
		_valleys.Value = Params.Valleys;
		_moisture.Value = Params.Moisture;
		_warmth.Value = Params.Warmth;
		_size.Selected = _size.GetItemIndex(Params.Size);
		_rungs.Value = Params.PlateauLevels;
		_cliff.Value = Params.CliffHeight;
		_patch.Value = Params.RegionScale;
		_exits.Value = Params.ExitGates;

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
	private OptionButton Choice<T>(Container into, string text, Func<T> read, Action<T> write)
		where T : struct, Enum
	{
		into.AddChild(Caption(text));
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
		into.AddChild(pick);
		return pick;
	}

	private HSlider Slide(Container into, string text, float min, float max, float step,
						  Func<float> read, Action<float> write)
	{
		Label caption = Caption($"{text}   {read():0.00}");
		into.AddChild(caption);

		var slider = new HSlider
		{
			MinValue = min,
			MaxValue = max,
			Step = step,
			Value = read(),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 18),
		};
		slider.ValueChanged += v =>
		{
			caption.Text = $"{text}   {v:0.00}";
			if (!_syncing) write((float)v);
		};
		into.AddChild(slider);
		return slider;
	}

	private SpinBox Spin(Container into, string text, int min, int max,
						 Func<int> read, Action<int> write)
	{
		var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		Label caption = Caption(text);
		caption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddChild(caption);

		var spin = new SpinBox { MinValue = min, MaxValue = max, Step = 1, Value = read() };
		spin.ValueChanged += v => { if (!_syncing) write((int)v); };
		row.AddChild(spin);
		into.AddChild(row);
		return spin;
	}

	private CheckBox Check(Container into, string text, Func<bool> read, Action<bool> write)
	{
		var box = new CheckBox { Text = text, ButtonPressed = read() };
		box.Toggled += on => { if (!_syncing) write(on); };
		into.AddChild(box);
		return box;
	}

	private static Label Caption(string text)
	{
		var label = new Label { Text = text };
		label.AddThemeColorOverride("font_color", new Color(0.85f, 0.87f, 0.9f));
		label.AddThemeFontSizeOverride("font_size", 13);
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
