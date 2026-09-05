using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin.Generation;
using ProjectNikitin.Meshing;
using static ProjectNikitin.Generation.Terrain;

namespace ProjectNikitin.Dev;

/// <summary>
/// Headless measure of the mesher (<c>scenes/dev/mesh_bench.tscn</c>): for each
/// footprint, a few seeds — how many triangles the island is against the boxes the
/// lab draws, how long meshing and the node work take — and two checks: a winding
/// probe (a BoxMesh's front faces wind the way <see cref="MeshBuffer"/> assumes) and
/// a voxel oracle (the mesh's area equals a slab-by-slab count of faces touching air,
/// for the ground and for the water). Prints, then quits with 1 if a check failed.
/// </summary>
public partial class MeshBench : Node
{
    [Export] public IslandParams Params { get; set; } = null!;
    [Export] public int Seeds { get; set; } = 3;
    [Export] public int FirstSeed { get; set; } = 5000;

    private bool _failed;

    private sealed record Row(int Size, int Seed, string Name, int Columns, int Spans, int Flooded,
        int GroundTris, int LiquidTris, int BoxTris, float GenMs, float MeshMs, float NodeMs, long Bytes,
        double GroundOff, double LiquidOff);

    public override void _Ready()
    {
        Params ??= new IslandParams();
        Probe();
        foreach (int size in IslandParams.SupportedSizes)
        {
            var rows = new List<Row>();
            for (int i = 0; i < Seeds; i++) rows.Add(Case(size, FirstSeed + i));
            Summary(size, rows);
        }
        GD.Print(_failed ? "[MeshBench] FAILED" : "[MeshBench] all checks passed");
        GetTree().Quit(_failed ? 1 : 0);
    }

    /// <summary>Which way a BoxMesh winds its front faces, against what the buffer assumes.</summary>
    private void Probe()
    {
        var box = new BoxMesh { Size = Vector3.One };
        Godot.Collections.Array arrays = box.GetMeshArrays();
        Vector3[] v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Vector3[] nrm = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        int[] idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        int clockwise = 0, anticlockwise = 0;
        for (int i = 0; i + 2 < idx.Length; i += 3)
        {
            Vector3 a = v[idx[i]], b = v[idx[i + 1]], c = v[idx[i + 2]];
            // The cross of the edges points toward whoever sees the ring run anticlockwise.
            if ((b - a).Cross(c - a).Dot(nrm[idx[i]]) < 0f) clockwise++;
            else anticlockwise++;
        }
        if (idx.Length == 0)
        {
            GD.Print("[MeshBench] winding probe: the BoxMesh gave no arrays here; not checked");
            return;
        }
        bool frontIsClockwise = clockwise > anticlockwise;
        bool ok = frontIsClockwise == MeshBuffer.FrontIsClockwise;
        GD.Print($"[MeshBench] winding probe: a BoxMesh's front faces run "
            + $"{(frontIsClockwise ? "clockwise" : "anticlockwise")} ({clockwise} vs {anticlockwise} triangles); "
            + $"MeshBuffer assumes {(MeshBuffer.FrontIsClockwise ? "clockwise" : "anticlockwise")}: {(ok ? "OK" : "MISMATCH")}");
        if (!ok) _failed = true;
    }

    private Row Case(int size, int seed)
    {
        var p = (IslandParams)Params.Duplicate();
        p.Size = size;

        ulong t0 = Time.GetTicksUsec();
        IslandData d = IslandGenerator.Generate(seed, p);
        float genMs = (Time.GetTicksUsec() - t0) / 1000f;

        // The pure part, timed alone; the areas measured after, outside the clock.
        var g = new MeshBuffer();
        var w = new MeshBuffer();
        var o = new MeshBuffer();
        int across = ChunkMesher.ChunksAcross(d.Size);
        int groundTris = 0, liquidTris = 0;
        long bytes = 0;
        double groundArea = 0, liquidArea = 0;
        ulong meshUs = 0;
        for (int cx = 0; cx < across; cx++)
        for (int cz = 0; cz < across; cz++)
        {
            ulong tb = Time.GetTicksUsec();
            ChunkMesher.Build(d, cx, cz, IslandTint.Default, g, w, o);
            meshUs += Time.GetTicksUsec() - tb;
            groundTris += g.Triangles;
            liquidTris += w.Triangles + o.Triangles;
            bytes += g.Bytes + w.Bytes + o.Bytes;
            groundArea += g.Area();
            liquidArea += w.Area() + o.Area();
        }
        float meshMs = meshUs / 1000f;

        // Meshes, colliders and nodes, the way the renderer does it; meshing again inside.
        var renderer = new IslandRenderer();
        AddChild(renderer);
        renderer.Show(d);
        float nodeMs = Math.Max(0f, renderer.LastBuildMs - meshMs);
        RemoveChild(renderer);
        renderer.Free();

        int columns = 0, spans = 0, flooded = 0;
        for (int x = 0; x < d.Size; x++)
        for (int z = 0; z < d.Size; z++)
        {
            if (!d.HasLand(x, z)) continue;
            columns++;
            spans += d.Spans[x, z].Length;
            if (d.WaterLevel[x, z] != IslandData.NoLand && d.WaterLevel[x, z] > d.SurfaceLevel(x, z)) flooded++;
        }
        // The lab: a twelve-triangle box per span and a two-triangle quad per flooded column.
        int boxTris = spans * 12 + flooded * 2;

        (double oracleGround, double oracleLiquid) = Oracle(d);
        double groundOff = groundArea - oracleGround;
        double liquidOff = liquidArea - oracleLiquid;
        if (Math.Abs(groundOff) > 1e-3 || Math.Abs(liquidOff) > 1e-3) _failed = true;

        var row = new Row(size, seed, d.Name, columns, spans, flooded, groundTris, liquidTris, boxTris,
                          genMs, meshMs, nodeMs, bytes, groundOff, liquidOff);
        GD.Print(
            Inv($"  {size}² seed {seed} \"{d.Name}\": {columns:N0} columns, {spans:N0} spans, {flooded:N0} flooded")
            + Inv($" -> ground {groundTris:N0} tris, liquid {liquidTris:N0} tris (the boxes draw {boxTris:N0});")
            + Inv($" generated {genMs:0} ms, meshed {meshMs:0.0} ms, nodes and colliders {nodeMs:0.0} ms, {bytes / 1048576.0:0.00} MB;")
            + Inv($" oracle off by {groundOff:0.000} m² ground, {liquidOff:0.000} m² liquid"));
        return row;
    }

    /// <summary>A decimal point whatever the machine's locale: the Windows box prints commas.</summary>
    private static string Inv(FormattableString f) => FormattableString.Invariant(f);

    private static void Summary(int size, List<Row> rows)
    {
        if (rows.Count == 0) return;
        double Mean(Func<Row, double> f)
        {
            double sum = 0;
            foreach (Row r in rows) sum += f(r);
            return sum / rows.Count;
        }
        double ground = Mean(r => r.GroundTris), liquid = Mean(r => r.LiquidTris), boxes = Mean(r => r.BoxTris);
        GD.Print(
            Inv($"[MeshBench] {size}² over {rows.Count} seeds: {Mean(r => r.Columns):N0} columns, {Mean(r => r.Spans):N0} spans;")
            + Inv($" {ground:N0} ground + {liquid:N0} liquid triangles, {(ground + liquid) / Math.Max(1, boxes):P0} of the boxes' {boxes:N0};")
            + Inv($" generate {Mean(r => r.GenMs):0} ms, mesh {Mean(r => r.MeshMs):0.0} ms, nodes and colliders {Mean(r => r.NodeMs):0.0} ms,")
            + Inv($" {Mean(r => r.Bytes) / 1048576.0:0.00} MB"));
    }

    /// <summary>
    /// Faces touching air, slab by slab, as area: a top or an underside is CellSize²,
    /// a side CellSize × SlabHeight. Water's sides count against anything that is
    /// neither solid nor the same fluid; its bed is the ground's top and counts once,
    /// for the ground.
    /// </summary>
    private static (double Ground, double Liquid) Oracle(IslandData d)
    {
        const double horizontal = CellSize * CellSize;
        const double vertical = CellSize * SlabHeight;
        double ground = 0, liquid = 0;
        for (int x = 0; x < d.Size; x++)
        for (int z = 0; z < d.Size; z++)
        {
            Span[] spans = d.Spans[x, z];
            if (spans == null || spans.Length == 0) continue;
            foreach (Span s in spans)
            {
                ground += 2 * horizontal;
                for (int y = s.Bottom; y <= s.Top; y++)
                {
                    if (!Solid(d, x + 1, z, y)) ground += vertical;
                    if (!Solid(d, x - 1, z, y)) ground += vertical;
                    if (!Solid(d, x, z + 1, y)) ground += vertical;
                    if (!Solid(d, x, z - 1, y)) ground += vertical;
                }
            }

            short level = d.WaterLevel[x, z];
            if (level == IslandData.NoLand || level <= spans[0].Top) continue;
            byte fluid = d.Fluid[x, z];
            liquid += horizontal;
            for (int y = spans[0].Top + 1; y <= level; y++)
            {
                if (!Solid(d, x + 1, z, y) && !SameFluid(d, x + 1, z, y, fluid)) liquid += vertical;
                if (!Solid(d, x - 1, z, y) && !SameFluid(d, x - 1, z, y, fluid)) liquid += vertical;
                if (!Solid(d, x, z + 1, y) && !SameFluid(d, x, z + 1, y, fluid)) liquid += vertical;
                if (!Solid(d, x, z - 1, y) && !SameFluid(d, x, z - 1, y, fluid)) liquid += vertical;
            }
        }
        return (ground, liquid);
    }

    private static bool Solid(IslandData d, int x, int z, int y)
    {
        if (x < 0 || z < 0 || x >= d.Size || z >= d.Size) return false;
        Span[] spans = d.Spans[x, z];
        if (spans == null) return false;
        foreach (Span s in spans)
            if (y >= s.Bottom && y <= s.Top) return true;
        return false;
    }

    private static bool SameFluid(IslandData d, int x, int z, int y, byte fluid)
    {
        if (x < 0 || z < 0 || x >= d.Size || z >= d.Size || !d.HasLand(x, z)) return false;
        short level = d.WaterLevel[x, z];
        return level != IslandData.NoLand && d.Fluid[x, z] == fluid
            && y > d.Spans[x, z][0].Top && y <= level;
    }
}
