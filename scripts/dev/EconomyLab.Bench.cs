using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using ProjectNikitin.Economy;

namespace ProjectNikitin.Dev;

/// <summary>
/// The bench (<c>-- bench</c>): the lab measured from a shell, so the lag a big web brings can be
/// read off any machine rather than guessed at. It runs scripted phases of a fixed number of
/// frames — idle, panning, zooming, the mouse crossing the canvas, thirty selections — then times
/// the pieces a change is made of, prints one table and quits. Like a shot it never writes web
/// data. Every knob it can turn (the minimap, the wires' antialiasing, the grid, low-processor
/// mode, the zoomed-out level of detail) is an argument, so a suspect can be weighed by running
/// the bench twice.
/// </summary>
public partial class EconomyLab
{
	/// <summary>One scripted phase: what to set up once, and what to do on each of its frames.</summary>
	private sealed record BenchPhase(string Name, Action? Setup, Action<int>? Each);

	/// <summary>Frames let by before the first phase: the view is restored two frames after the web opens, and the first frames are not like the rest.</summary>
	private const int BenchSettle = 45;

	/// <summary>No phase runs longer than this, whatever its frame count, so a slow machine still finishes inside a minute.</summary>
	private const double BenchCapSeconds = 6.0;

	private static readonly (Performance.Monitor Monitor, string Head, double Scale)[] BenchMonitors =
	{
		// TimeProcess is the worst process step of the last second, not this frame's: a spike gauge.
		(Performance.Monitor.TimeProcess, "spike ms", 1000.0),
		(Performance.Monitor.RenderTotalDrawCallsInFrame, "draws", 1.0),
		(Performance.Monitor.RenderTotalObjectsInFrame, "objects", 1.0),
		(Performance.Monitor.RenderTotalPrimitivesInFrame, "prims", 1.0),
		(Performance.Monitor.ObjectNodeCount, "nodes", 1.0),
	};

	private bool _benching;
	private string _benchTag = "";
	private int _benchWant = 240;
	private int _benchSettling = BenchSettle;
	private int _benchAt = -1;
	private int _benchFrame;
	private double _benchSince;
	private BenchPhase[] _benchPhases = Array.Empty<BenchPhase>();
	private readonly List<double> _benchMs = new();
	private readonly double[] _benchSum = new double[BenchMonitorCount];
	private readonly List<string> _benchRows = new();
	private readonly List<(string What, double Avg, double Max, int N)> _benchOnce = new();

	private const int BenchMonitorCount = 5;

	/// <summary>The zoom and scroll that frame the whole web, found once so the wide phases all sit the same way.</summary>
	private float _benchWide;

	private Vector2 _benchWideAt, _benchHome, _benchMouse, _benchMiddle;
	private Vector2I _benchWindow = new(1920, 1080);
	private int _benchMouseAt, _benchWires = -1;
	private List<string> _benchPicks = new();

	/// <summary>Asks again for a plain unmaximised window of the bench's size: a window manager may take a frame or two to agree.</summary>
	private void BenchPlainWindow()
	{
		Window window = GetWindow();
		if (window.Mode != Window.ModeEnum.Windowed) window.Mode = Window.ModeEnum.Windowed;
		if (window.Size != _benchWindow) window.Size = _benchWindow;
		if (!Mathf.IsEqualApprox(window.ContentScaleFactor, 1f)) window.ContentScaleFactor = 1f;
		// A resize builds a new swap chain, which brings the screen's own sync back with it.
		if (DisplayServer.WindowGetVsyncMode() != DisplayServer.VSyncMode.Disabled)
			DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		Engine.MaxFps = 0;
		// A window behind the shell it was started from is throttled by the compositor, which would
		// hold every phase at the screen's own rate and hide what the work really costs.
		DisplayServer.WindowMoveToForeground();
	}

	partial void BenchStart(string[] args)
	{
		string? Arg(string key) => args.FirstOrDefault(a => a.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..];
		bool Flag(string key, bool fallback) => Arg(key) is { } v ? v is "1" or "on" or "yes" : fallback;
		float Number(string key, float fallback) =>
			Arg(key) is { } v && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? n : fallback;

		// A shot may turn the level of detail too, to photograph the far view with and without it.
		LodZoom = Number("lod", LodZoom);
		if (!args.Contains("bench")) return;
		_benching = true;
		// A bench is for measuring, not for editing: nothing it does may reach a file. _shotAt stays 0,
		// so the shot's screenshot-and-quit path never fires.
		_shooting = true;

		_benchTag = Arg("tag") ?? "";
		_benchWant = Math.Max(10, (int)Number("frames", 240));

		// A plain window at scale 1, so two machines' numbers are of the same picture. The window manager
		// has the last word on a maximised window's size, so the ask is repeated while the bench settles.
		_benchWindow = new Vector2I((int)Number("window", 1920), (int)Number("windowh", 1080));
		_uiScale = 1f;
		BenchPlainWindow();
		DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		Engine.MaxFps = 0;

		_graph.MinimapEnabled = Flag("minimap", _graph.MinimapEnabled);
		_graph.ConnectionLinesAntialiased = Flag("aa", _graph.ConnectionLinesAntialiased);
		_graph.ShowGrid = Flag("grid", _graph.ShowGrid);
		_graph.ConnectionLinesCurvature = Number("curve", _graph.ConnectionLinesCurvature);
		_graph.ConnectionLinesThickness = Number("thick", _graph.ConnectionLinesThickness);
		_trace = Flag("trace", _trace);
		OS.LowProcessorUsageMode = Flag("lowcpu", OS.LowProcessorUsageMode);

		_benchPhases = BenchScript();
	}

	partial void BenchTick(double delta)
	{
		if (!_benching) return;
		if (_benchSettling > 0)
		{
			if (_benchSettling % 8 == 0) BenchPlainWindow();
			if (--_benchSettling == 0) BenchEnter(0);
			return;
		}
		if (_benchAt >= _benchPhases.Length) return;

		// The frame that has just been drawn is the one being measured; the phase's work is done after it is recorded.
		_benchMs.Add(delta * 1000.0);
		for (int i = 0; i < BenchMonitorCount; i++) _benchSum[i] += Performance.GetMonitor(BenchMonitors[i].Monitor) * BenchMonitors[i].Scale;
		_benchSince += delta;

		BenchPhase phase = _benchPhases[_benchAt];
		phase.Each?.Invoke(_benchFrame);
		if (++_benchFrame < _benchWant && _benchSince < BenchCapSeconds) return;

		BenchClose(phase.Name);
		if (_benchAt + 1 < _benchPhases.Length) BenchEnter(_benchAt + 1);
		else
		{
			_benchAt = _benchPhases.Length;
			BenchOneOffs();
			BenchReport();
			GetTree().Quit();
		}
	}

	private void BenchEnter(int index)
	{
		BenchPlainWindow();
		_benchAt = index;
		_benchFrame = 0;
		_benchSince = 0;
		_benchMs.Clear();
		Array.Clear(_benchSum);
		_benchPhases[index].Setup?.Invoke();
	}

	/// <summary>The phase just run, written as one row: its frame times and the mean of each monitor over them.</summary>
	private void BenchClose(string name)
	{
		_benchMs.Sort();
		int n = Math.Max(1, _benchMs.Count);
		double avg = _benchMs.Count > 0 ? _benchMs.Average() : 0;
		double p95 = _benchMs.Count > 0 ? _benchMs[Math.Min(_benchMs.Count - 1, (int)(_benchMs.Count * 0.95))] : 0;
		var row = new StringBuilder();
		row.Append(name.PadRight(14)).Append(n.ToString(CultureInfo.InvariantCulture).PadLeft(7));
		row.Append(Num(avg).PadLeft(9)).Append(Num(p95).PadLeft(9)).Append(Num(avg > 0 ? 1000.0 / avg : 0, 1).PadLeft(8));
		for (int i = 0; i < BenchMonitorCount; i++)
			row.Append(Num(_benchSum[i] / n, i == 0 ? 2 : 0).PadLeft(BenchMonitors[i].Head.Length + 3));
		_benchRows.Add(row.ToString());
	}

	private static string Num(double value, int places = 2) => value.ToString("F" + places, CultureInfo.InvariantCulture);

	// ---- the script -------------------------------------------------------------

	private BenchPhase[] BenchScript()
	{
		return new[]
		{
			new BenchPhase("idle-z1", () => BenchView(1f), null),
			new BenchPhase("idle-wide", BenchWideView, null),
			new BenchPhase("pan-z1", () => BenchView(1f), f => _graph.ScrollOffset = _benchHome + new Vector2(BenchSweep(f) * 1400f, BenchSweep(f * 3) * 300f)),
			new BenchPhase("pan-wide", BenchWideView, f => _graph.ScrollOffset = _benchWideAt + new Vector2(BenchSweep(f) * 600f, BenchSweep(f * 3) * 200f)),
			new BenchPhase("zoom", BenchWideView, f =>
			{
				float t = (BenchSweep(f) + 1f) / 2f;
				Vector2 middle = _graph.CanvasCentre;
				_graph.Zoom = Mathf.Lerp(_benchWide, 1f, t);
				_graph.ScrollOffset = middle * _graph.Zoom - _graph.Size / 2f;
			}),
			new BenchPhase("mouse-z1", () => BenchView(1f), _ => BenchMouseStep()),
			new BenchPhase("mouse-wide", BenchWideView, _ => BenchMouseStep()),
			new BenchPhase("select", BenchPickNodes, f =>
			{
				if (_benchPicks.Count == 0) return;
				string key = _benchPicks[f % _benchPicks.Count];
				long from = Stopwatch.GetTimestamp();
				Select(key);
				BenchNote("Select(key)", Stopwatch.GetElapsedTime(from).TotalMilliseconds);
			}),
		};
	}

	/// <summary>A triangle wave in -1..1, so a phase sweeps back and forth over the same ground.</summary>
	private float BenchSweep(int frame)
	{
		float t = frame / (float)Math.Max(1, _benchWant) * 4f % 4f;
		return t < 1f ? t : t < 3f ? 2f - t : t - 4f;
	}

	/// <summary>The middle of the web at any zoom, so a close view looks at nodes rather than at empty canvas.</summary>
	private void BenchView(float zoom)
	{
		BenchMeasureWide();
		_graph.Zoom = zoom;
		_graph.ScrollOffset = _benchHome = _benchMiddle * zoom - _graph.Size / 2f;
	}

	private void BenchWideView()
	{
		BenchMeasureWide();
		_graph.Zoom = _benchWide;
		_graph.ScrollOffset = _benchWideAt;
	}

	/// <summary>The zoom and scroll that frame the whole web, taken once from the lab's own F, and the web's middle.</summary>
	private void BenchMeasureWide()
	{
		if (_benchWide > 0f && _benchWideAt != Vector2.Zero) return;
		FrameAll();
		_benchWide = _graph.Zoom;
		_benchWideAt = _graph.ScrollOffset;
		_benchMiddle = (_benchWideAt + _graph.Size / 2f) / _benchWide;
	}

	/// <summary>
	/// One synthetic mouse move across the canvas. GraphEdit hunts for the wire under the pointer on
	/// every motion event, so this is the phase that says what that hunt costs with a thousand wires.
	/// </summary>
	private void BenchMouseStep()
	{
		Rect2 rect = new(_graph.GlobalPosition, _graph.Size);
		float t = (BenchSweep(_benchMouseAt++) + 1f) / 2f;
		var at = new Vector2(
			rect.Position.X + 20f + (rect.Size.X - 40f) * t,
			rect.Position.Y + 20f + (rect.Size.Y - 40f) * (0.5f + 0.4f * Mathf.Sin(t * 9f)));
		var motion = new InputEventMouseMotion
		{
			Position = at,
			GlobalPosition = at,
			Relative = at - _benchMouse,
			Velocity = (at - _benchMouse) * 60f,
		};
		_benchMouse = at;
		Input.ParseInputEvent(motion);
	}

	/// <summary>One mouse move delivered there and then, so a stopwatch round it catches what the canvas does with it.</summary>
	private void BenchMouseNow()
	{
		BenchMouseStep();
		Input.FlushBufferedEvents();
	}

	/// <summary>Thirty nodes spread through the web: goods, recipes and consumers, so a selection is a fair average one.</summary>
	private void BenchPickNodes()
	{
		BenchView(1f);
		var keys = Web.Goods.Concat(Web.Recipes.Select(r => r.Id)).Concat(Web.Consumers.Select(c => c.Id)).ToList();
		int step = Math.Max(1, keys.Count / 30);
		_benchPicks = Enumerable.Range(0, Math.Min(30, keys.Count)).Select(i => keys[i * step % keys.Count]).ToList();
	}

	// ---- the pieces of a change --------------------------------------------------

	/// <summary>
	/// What a change costs, piece by piece. <c>full-ledger-reference</c> is locked, so the door itself
	/// refuses; these are the parts <see cref="Change"/> would run, called straight, none of which
	/// writes anything: the undo snapshot, the re-read of the web, the canvas and the three docks.
	/// </summary>
	private void BenchOneOffs()
	{
		const int rounds = 10;

		// The frame time above is quantised by the screen (a 30 Hz panel hides everything faster than
		// 33 ms), so the canvas is also drawn by hand, without a swap and so without the wait, and the
		// mouse is moved with the input queue flushed on the spot. Those two numbers are the honest ones.
		for (int i = 0; i < rounds; i++)
		{
			BenchView(1f);
			BenchTime("draw frame z1", () => RenderingServer.ForceDraw(false));
			BenchTime("mouse motion z1", BenchMouseNow);
			BenchTime("closest wire z1", () => _graph.GetClosestConnectionAtPoint(_graph.Size / 2f, 10f));
			BenchWideView();
			BenchTime("draw frame wide", () => RenderingServer.ForceDraw(false));
			BenchTime("mouse motion wide", BenchMouseNow);
			BenchTime("closest wire wide", () => _graph.GetClosestConnectionAtPoint(_graph.Size / 2f, 10f));
			// The same move, but over the left dock: what a motion event costs before the canvas sees it.
			BenchTime("mouse motion off", () =>
			{
				var at = new Vector2(_graph.GlobalPosition.X / 2f, 200f + _benchMouseAt++ % 200);
				Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = Vector2.One });
				Input.FlushBufferedEvents();
			});
		}

		for (int i = 0; i < rounds; i++)
		{
			BenchTime("EconomyStore.ToJson", () => EconomyStore.ToJson(Web));
			BenchTime("WebAnalysis.Of", () => WebAnalysis.Of(Web));
			BenchTime("SyncGraph", SyncGraph);
			BenchTime("RefreshInspector", RefreshInspector);
			BenchTime("RefreshPalette", RefreshPalette);
			BenchTime("RefreshBar", RefreshBar);
		}
		BenchTime("ApplyTrace", ApplyTrace);
		BenchTime("OpenWeb (re-open)", () => OpenWeb(Web.Id));

		// Last, because they break the canvas: where a mouse move over the canvas actually goes.
		// Without the wires, and then without the canvas at all, against "mouse motion z1" above.
		BenchView(1f);
		_benchWires = _wired.Count;
		_graph.ClearConnections();
		_wired.Clear();
		for (int i = 0; i < rounds; i++) BenchTime("motion, no wires", BenchMouseNow);
		_graph.Visible = false;
		for (int i = 0; i < rounds; i++) BenchTime("motion, no canvas", BenchMouseNow);
		_graph.Visible = true;
	}

	private void BenchTime(string what, Action run)
	{
		long from = Stopwatch.GetTimestamp();
		run();
		BenchNote(what, Stopwatch.GetElapsedTime(from).TotalMilliseconds);
	}

	private void BenchNote(string what, double ms)
	{
		for (int i = 0; i < _benchOnce.Count; i++)
			if (_benchOnce[i].What == what)
			{
				(string _, double avg, double max, int n) = _benchOnce[i];
				_benchOnce[i] = (what, (avg * n + ms) / (n + 1), Math.Max(max, ms), n + 1);
				return;
			}
		_benchOnce.Add((what, ms, ms, 1));
	}

	// ---- the table ---------------------------------------------------------------

	private void BenchReport()
	{
		var sheet = new StringBuilder();
		sheet.Append("Economy lab bench").Append(_benchTag.Length > 0 ? " — " + _benchTag : "").Append('\n');
		sheet.Append($"machine   {OS.GetName()} · {OS.GetProcessorName()} · {RenderingServer.GetVideoAdapterName()}\n");
		sheet.Append($"window    {GetWindow().Size.X}x{GetWindow().Size.Y} · canvas {(int)_graph.Size.X}x{(int)_graph.Size.Y} · " +
		             $"content scale {Num(GetWindow().ContentScaleFactor)} · vsync {DisplayServer.WindowGetVsyncMode()} · max fps {Engine.MaxFps}\n");
		sheet.Append($"web       {Web.Name}: {Web.Goods.Count} goods, {Web.Recipes.Count} recipes, {Web.Consumers.Count} consumers, " +
		             $"{Analysis.Links.Count} links, {_nodes.Count} nodes, {(_benchWires < 0 ? _wired.Count : _benchWires)} wires\n");
		sheet.Append($"canvas    minimap {On(_graph.MinimapEnabled)}, wire aa {On(_graph.ConnectionLinesAntialiased)}, grid {On(_graph.ShowGrid)}, " +
		             $"curvature {Num(_graph.ConnectionLinesCurvature)}, thickness {Num(_graph.ConnectionLinesThickness)}, " +
		             $"trace {On(_trace)}, lod zoom {Num(LodZoom)}, low cpu {On(OS.LowProcessorUsageMode)}\n");
		sheet.Append($"zoom      wide {Num(_benchWide, 3)} frames the whole web; frames asked {_benchWant}, capped at {Num(BenchCapSeconds, 0)}s a phase\n");
		sheet.Append("note      frame time is quantised by the screen's own rate; below that floor read the counters and the\n" +
		             "          one-offs instead. \"spike ms\" is the worst process step of the last second, not this frame's.\n\n");

		var head = new StringBuilder();
		head.Append("phase".PadRight(14)).Append("frames".PadLeft(7)).Append("avg ms".PadLeft(9)).Append("p95 ms".PadLeft(9)).Append("fps".PadLeft(8));
		foreach ((Performance.Monitor _, string name, double _) in BenchMonitors) head.Append(name.PadLeft(name.Length + 3));
		sheet.Append(head).Append('\n');
		sheet.Append(new string('-', head.Length)).Append('\n');
		foreach (string row in _benchRows) sheet.Append(row).Append('\n');

		sheet.Append('\n').Append("one-off".PadRight(22)).Append("avg ms".PadLeft(9)).Append("max ms".PadLeft(9)).Append("n".PadLeft(5)).Append('\n');
		sheet.Append(new string('-', 45)).Append('\n');
		foreach ((string what, double avg, double max, int n) in _benchOnce)
			sheet.Append(what.PadRight(22)).Append(Num(avg).PadLeft(9)).Append(Num(max).PadLeft(9)).Append(n.ToString(CultureInfo.InvariantCulture).PadLeft(5)).Append('\n');

		GD.Print("\n" + sheet);
		const string path = "user://economy_bench.txt";
		using (FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write))
		{
			if (file != null) file.StoreString(sheet.ToString());
		}
		GD.Print($"Economy lab bench: saved {ProjectSettings.GlobalizePath(path)}");
	}

	private static string On(bool flag) => flag ? "on" : "off";
}
