using System;
using Godot;
using ProjectNikitin.Generation;
using ProjectNikitin.Meshing;
using static ProjectNikitin.Generation.Terrain;

namespace ProjectNikitin.Dev;

/// <summary>
/// The collider check: the last island's renderer stays in the tree, and once
/// physics has stepped, a ray down onto every third land column must hit the top
/// of its highest span and a ray up from below must hit its keel. Headless
/// physics is real physics, so this is the colliders as the game will use them.
/// </summary>
public partial class MeshBench
{
    private IslandRenderer? _kept;
    private IslandData? _keptData;
    private int _physicsFrames;

    /// <summary>Keeps <paramref name="d"/> built in the tree for <see cref="_PhysicsProcess"/> to cast against.</summary>
    private void KeepForColliders(IslandData d)
    {
        _kept = new IslandRenderer();
        AddChild(_kept);
        _kept.Show(d);
        _keptData = d;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_kept == null || _keptData == null) return;
        // The bodies enter the space on the first step; cast on the second.
        if (++_physicsFrames < 2) return;
        CheckColliders(_keptData);
        _kept.QueueFree();
        _kept = null;
        Finish();
    }

    private void CheckColliders(IslandData d)
    {
        PhysicsDirectSpaceState3D space = GetViewport().FindWorld3D().DirectSpaceState;
        int tried = 0, missed = 0, wrongTop = 0, wrongKeel = 0;
        for (int x = 0; x < d.Size; x += 3)
        for (int z = 0; z < d.Size; z += 3)
        {
            Span[] spans = d.Spans[x, z];
            if (spans == null || spans.Length == 0) continue;
            tried++;
            float cx = x * CellSize, cz = z * CellSize;

            Godot.Collections.Dictionary down = space.IntersectRay(
                PhysicsRayQueryParameters3D.Create(new Vector3(cx, 1000f, cz), new Vector3(cx, -1000f, cz)));
            float top = (spans[^1].Top + 1) * SlabHeight;
            if (down.Count == 0) missed++;
            else if (Math.Abs(down["position"].AsVector3().Y - top) > 1e-3f) wrongTop++;

            Godot.Collections.Dictionary up = space.IntersectRay(
                PhysicsRayQueryParameters3D.Create(new Vector3(cx, -1000f, cz), new Vector3(cx, 1000f, cz)));
            float keel = spans[0].Bottom * SlabHeight;
            if (up.Count == 0) missed++;
            else if (Math.Abs(up["position"].AsVector3().Y - keel) > 1e-3f) wrongKeel++;
        }
        bool ok = missed == 0 && wrongTop == 0 && wrongKeel == 0;
        GD.Print($"[MeshBench] colliders: {tried} columns cast at from above and below on the last island; "
            + $"{missed} missed, {wrongTop} wrong top, {wrongKeel} wrong keel: {(ok ? "OK" : "MISMATCH")}");
        if (!ok) _failed = true;
    }

    private void Finish()
    {
        GD.Print(_failed ? "[MeshBench] FAILED" : "[MeshBench] all checks passed");
        GetTree().Quit(_failed ? 1 : 0);
    }
}
