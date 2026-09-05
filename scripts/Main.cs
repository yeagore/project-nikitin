using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin.Generation;
using ProjectNikitin.Meshing;

namespace ProjectNikitin;

/// <summary>
/// The game scene, for now: one Domain generated from the preset and drawn by the
/// terrain renderer, framed by the camera rig, under a sun aimed here (a rotated basis
/// in the .tscn is the transpose gotcha). N rolls another seed, F frames the island.
/// From a shell, <c>-- domains=20</c> lays out that many Domains in a grid on
/// consecutive seeds, and <c>bench</c> prints the frame rate, draw calls and memory
/// after six seconds with vsync off and quits: the many-Domains question, measured.
/// </summary>
public partial class Main : Node3D
{
    [Export] public int Seed { get; set; } = 1337;
    [Export] public IslandParams Params { get; set; } = null!;

    private IslandRenderer _island = null!;
    private CameraRig _rig = null!;
    private readonly List<IslandRenderer> _more = new();
    private int _domains = 1;
    private bool _bench;
    private double _benchClock;
    private Vector3 _center;
    private float _radius = 10f;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("domains=") && int.TryParse(arg.AsSpan(8), out int n) && n > 0) _domains = n;
            else if (arg == "bench") _bench = true;
        }
        _island = GetNode<IslandRenderer>("Island");
        _rig = GetNode<CameraRig>("CameraRig");
        var sun = GetNode<DirectionalLight3D>("Sun");
        sun.LookAt(sun.GlobalPosition + new Vector3(0.35f, -0.85f, 0.45f), Vector3.Up);
        Params ??= new IslandParams();
        if (_bench) DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Build();
    }

    public override void _Process(double delta)
    {
        if (!_bench) return;
        _benchClock += delta;
        if (_benchClock < 6.0) return;
        _bench = false;
        GD.Print($"[Main] bench: {_domains} Domains in view, {Engine.GetFramesPerSecond():0} fps"
            + $" ({Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0:0.0} ms a frame),"
            + $" {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame):N0} draw calls,"
            + $" {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame):N0} primitives,"
            + $" {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / 1048576.0:0} MB video,"
            + $" {Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0:0} MB static");
        GetTree().Quit();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode)
        {
            case Key.N:
                Seed = (int)(GD.Randi() & 0x7FFFFFFF);
                Build();
                break;
            case Key.F:
                Frame();
                break;
        }
    }

    /// <summary>Generates and shows <see cref="_domains"/> Domains on consecutive seeds, in a grid a quarter footprint apart.</summary>
    private void Build()
    {
        foreach (IslandRenderer r in _more)
        {
            RemoveChild(r);
            r.QueueFree();
        }
        _more.Clear();

        int across = Mathf.CeilToInt(Mathf.Sqrt(_domains));
        int footprint = Params.Size > 0 ? Params.Size : 128;
        float pitch = footprint * 1.25f * Terrain.CellSize;
        float genMs = 0f, meshMs = 0f;
        int ground = 0, liquid = 0;
        string first = "";
        var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        for (int i = 0; i < _domains; i++)
        {
            ulong t0 = Time.GetTicksUsec();
            IslandData data = IslandGenerator.Generate(Seed + i, Params);
            genMs += (Time.GetTicksUsec() - t0) / 1000f;
            if (i == 0) first = data.Name;

            IslandRenderer r = _island;
            if (i > 0)
            {
                r = new IslandRenderer { Name = $"Island{i}" };
                AddChild(r);
                _more.Add(r);
            }
            r.Position = new Vector3(i % across * pitch, 0f, i / across * pitch);
            r.Show(data);
            meshMs += r.LastBuildMs;
            ground += r.GroundTriangles;
            liquid += r.LiquidTriangles;

            Vector3 c = r.Position + r.Center;
            lo = lo.Min(c - Vector3.One * r.Radius);
            hi = hi.Max(c + Vector3.One * r.Radius);
        }

        _center = (lo + hi) * 0.5f;
        _radius = Mathf.Max(1f, (hi - lo).Length() * 0.5f);
        if (_domains > 1) _rig.MaxZoomDistance = Mathf.Max(_rig.MaxZoomDistance, _radius * 4f);
        Frame();

        GD.Print($"[Main] {_domains} Domain{(_domains == 1 ? $" ({first}, seed {Seed})" : "s")}, {footprint}²: "
            + $"generated in {genMs:0} ms, meshed in {meshMs:0} ms; {ground:N0} ground and {liquid:N0} liquid triangles");
    }

    private void Frame() => _rig.Frame(_center, _radius);
}
