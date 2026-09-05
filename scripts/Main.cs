using Godot;
using ProjectNikitin.Generation;
using ProjectNikitin.Meshing;

namespace ProjectNikitin;

/// <summary>
/// The game scene, for now: one Domain generated from the preset and drawn by the
/// terrain renderer, framed by the camera rig, under a sun aimed here (a rotated basis
/// in the .tscn is the transpose gotcha). N rolls another seed, F frames the island.
/// </summary>
public partial class Main : Node3D
{
    [Export] public int Seed { get; set; } = 1337;
    [Export] public IslandParams Params { get; set; } = null!;

    private IslandRenderer _island = null!;
    private CameraRig _rig = null!;

    public override void _Ready()
    {
        _island = GetNode<IslandRenderer>("Island");
        _rig = GetNode<CameraRig>("CameraRig");
        var sun = GetNode<DirectionalLight3D>("Sun");
        sun.LookAt(sun.GlobalPosition + new Vector3(0.35f, -0.85f, 0.45f), Vector3.Up);
        Params ??= new IslandParams();
        Build();
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

    private void Build()
    {
        ulong t0 = Time.GetTicksUsec();
        IslandData data = IslandGenerator.Generate(Seed, Params);
        float genMs = (Time.GetTicksUsec() - t0) / 1000f;
        _island.Show(data);
        Frame();
        GD.Print($"[Main] {data.Name}, seed {Seed}, {data.Size}²: generated in {genMs:0} ms, "
            + $"meshed in {_island.LastBuildMs:0} ms; {_island.GroundTriangles:N0} ground and "
            + $"{_island.LiquidTriangles:N0} liquid triangles");
    }

    private void Frame() => _rig.Frame(_island.ToGlobal(_island.Center), _island.Radius);
}
