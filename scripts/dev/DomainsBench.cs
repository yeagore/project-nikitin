using System;
using System.Globalization;
using Godot;
using ProjectNikitin.Generation;
using ProjectNikitin.Meshing;

namespace ProjectNikitin.Dev;

/// <summary>
/// The many-Domains question, measured (<c>scenes/dev/domains_bench.tscn</c>, windowed,
/// since a frame rate needs a screen). Lays out <see cref="Domains"/> Domains on
/// consecutive seeds in a grid a quarter footprint apart, frames them all inside a far
/// plane pushed out to 100 km, and after <see cref="Seconds"/> prints the frame rate,
/// draw calls, primitives, the render thread's CPU time and memory, then quits. Vsync
/// is off; the GPU time reads 0 on Metal. Past 150 Domains it builds no colliders:
/// Jolt's default cap of 10,240 bodies is 160 Domains of 64 chunk bodies (a project
/// setting), and the chunk nodes are bodies with or without a shape, so the engine
/// still logs the cap being hit. <c>-- domains=20 seed=1337 seconds=6</c> override
/// the exports.
/// </summary>
public partial class DomainsBench : Node3D
{
    [Export] public IslandParams Params { get; set; } = null!;
    [Export] public int Domains { get; set; } = 20;
    [Export] public int FirstSeed { get; set; } = 1337;
    [Export] public double Seconds { get; set; } = 6.0;

    private CameraRig _rig = null!;
    private double _clock;
    private bool _done;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("domains=") && int.TryParse(arg.AsSpan(8), out int n) && n > 0) Domains = n;
            else if (arg.StartsWith("seed=") && int.TryParse(arg.AsSpan(5), out int seed)) FirstSeed = seed;
            else if (arg.StartsWith("seconds=") && double.TryParse(arg.AsSpan(8), NumberStyles.Float,
                         CultureInfo.InvariantCulture, out double t) && t > 0) Seconds = t;
        }
        Params ??= new IslandParams();
        _rig = GetNode<CameraRig>("CameraRig");
        var sun = GetNode<DirectionalLight3D>("Sun");
        sun.LookAt(sun.GlobalPosition + new Vector3(0.35f, -0.85f, 0.45f), Vector3.Up);

        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        // Every Domain must sit inside the far plane, or the count is of what the frustum kept.
        _rig.GetNode<Camera3D>("Camera3D").Far = 100000f;
        Build();
    }

    public override void _Process(double delta)
    {
        if (_done) return;
        _clock += delta;
        if (_clock < Seconds) return;
        _done = true;
        Rid viewport = GetViewport().GetViewportRid();
        GD.Print($"[DomainsBench] {Domains} Domains in view, {Engine.GetFramesPerSecond():0} fps"
            + $" ({Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0:0.0} ms a frame),"
            + $" GPU {RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport):0.0} ms,"
            + $" CPU {RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport):0.0} ms,"
            + $" {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame):N0} draw calls,"
            + $" {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame):N0} primitives,"
            + $" {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / 1048576.0:0} MB video,"
            + $" {Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0:0} MB static");
        GetTree().Quit();
    }

    private void Build()
    {
        int across = Mathf.CeilToInt(Mathf.Sqrt(Domains));
        int footprint = Params.Size > 0 ? Params.Size : 128;
        float pitch = footprint * 1.25f * Terrain.CellSize;
        float genMs = 0f, meshMs = 0f;
        int ground = 0, liquid = 0;
        var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        for (int i = 0; i < Domains; i++)
        {
            ulong t0 = Time.GetTicksUsec();
            IslandData data = IslandGenerator.Generate(FirstSeed + i, Params);
            genMs += (Time.GetTicksUsec() - t0) / 1000f;

            var r = new IslandRenderer { Name = $"Domain{i}", Colliders = Domains <= 150 };
            AddChild(r);
            r.Position = new Vector3(i % across * pitch, 0f, i / across * pitch);
            r.Show(data);
            meshMs += r.LastBuildMs;
            ground += r.GroundTriangles;
            liquid += r.LiquidTriangles;

            Vector3 c = r.Position + r.Center;
            lo = lo.Min(c - Vector3.One * r.Radius);
            hi = hi.Max(c + Vector3.One * r.Radius);
        }

        Vector3 center = (lo + hi) * 0.5f;
        float radius = Mathf.Max(1f, (hi - lo).Length() * 0.5f);
        _rig.MaxZoomDistance = Mathf.Max(_rig.MaxZoomDistance, radius * 4f);
        _rig.Frame(center, radius);

        GD.Print($"[DomainsBench] {Domains} Domains at {footprint}²: generated in {genMs:0} ms, "
            + $"meshed in {meshMs:0} ms; {ground:N0} ground and {liquid:N0} liquid triangles"
            + (Domains > 150 ? " (no colliders past 150 Domains)" : ""));
    }
}
