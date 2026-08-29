using System.Collections.Generic;
using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

/// <summary>
/// Dev harness for island generation. Open <c>scenes/dev/island_lab.tscn</c> and
/// run it (F6). Edit the <see cref="Params"/> resource in the inspector, then
/// press the <b>Regenerate</b> button (works in the editor) or the <b>R</b> key
/// (while running). Renders one <c>MultiMesh</c> box per land column at its
/// surface level — no mesher, no culling; that comes later.
/// </summary>
[Tool]
public partial class IslandLab : Node3D
{
    [Export] public int Seed { get; set; } = 1337;
    [Export] public IslandParams Params { get; set; } = null!;

    [ExportToolButton("Regenerate")]
    public Callable RegenerateButton => Callable.From(Rebuild);

    private MultiMeshInstance3D _terrain = null!;
    private BoxMesh _blockMesh = null!;

    public override void _Ready()
    {
        _terrain = GetNodeOrNull<MultiMeshInstance3D>("Terrain");
        _blockMesh = new BoxMesh { Size = Vector3.One };
        _blockMesh.Material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 1f,
        };
        Rebuild();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Engine.IsEditorHint()) return;
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.R })
            Rebuild();
    }

    private void Rebuild()
    {
        if (_terrain == null || _blockMesh == null) return;
        Params ??= new IslandParams();

        ulong t0 = Time.GetTicksUsec();
        IslandData data = new IslandGenerator().Generate(Seed, Params);
        int drawn = RenderColumns(data);
        GD.Print($"[IslandLab] seed {Seed}, {Params.Size}² -> {drawn} columns "
            + $"in {(Time.GetTicksUsec() - t0) / 1000f:0.0} ms");
    }

    private int RenderColumns(IslandData d)
    {
        int n = d.Size;
        float half = n * 0.5f;

        var cells = new List<Vector3I>();
        int maxY = 1;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            int y = d.SurfaceLevel(x, z);
            cells.Add(new Vector3I(x, y, z));
            if (y > maxY) maxY = y;
        }

        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = _blockMesh,
            InstanceCount = cells.Count,
        };

        var low = new Color(0.20f, 0.32f, 0.12f);
        var high = new Color(0.62f, 0.70f, 0.42f);
        for (int i = 0; i < cells.Count; i++)
        {
            Vector3I c = cells[i];
            mm.SetInstanceTransform(i, new Transform3D(
                Basis.Identity, new Vector3(c.X - half, c.Y, c.Z - half)));
            float t = Mathf.Clamp(c.Y / (float)maxY, 0f, 1f);
            mm.SetInstanceColor(i, low.Lerp(high, t));
        }

        _terrain.Multimesh = mm;
        return cells.Count;
    }
}
