using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin.Generation;
using static ProjectNikitin.Generation.Terrain;

namespace ProjectNikitin.Meshing;

/// <summary>
/// Turns one chunk of an <see cref="IslandData"/> into the faces a camera can see. A
/// pure function of the data: no node, no randomness. Per column per span: the top at
/// <c>Top + 1</c>, the underside at <c>Bottom</c> (under a lip that is an overhang's
/// roof; under the lowest span it is the keel), and a side wherever the neighbouring
/// column's spans do not fill that slab range, merged over the range so a cliff of
/// four slabs is one quad. Water is its own surface: the top at <c>WaterLevel + 1</c>
/// and sides against anything that is neither solid nor the same water — the face of
/// a river spilling over the rim, the step of a cataract. Goo likewise, on a third
/// surface. Nothing buried is ever emitted.
/// </summary>
public static class ChunkMesher
{
    /// <summary>Columns along each side of a chunk: 128² is 8 × 8 chunks, 64² is 4 × 4.</summary>
    public const int ChunkSize = 16;

    public static int ChunksAcross(int size) => (size + ChunkSize - 1) / ChunkSize;

    private static readonly (int Dx, int Dz)[] Sides = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>Meshes chunk (<paramref name="cx"/>, <paramref name="cz"/>) into the three buffers, clearing them first.</summary>
    public static void Build(IslandData d, int cx, int cz, IslandTint tint,
                             MeshBuffer ground, MeshBuffer water, MeshBuffer goo)
    {
        ground.Clear();
        water.Clear();
        goo.Clear();
        var cover = new List<Span>(4);
        int n = d.Size;
        int x0 = cx * ChunkSize, z0 = cz * ChunkSize;
        int x1 = Math.Min(x0 + ChunkSize, n), z1 = Math.Min(z0 + ChunkSize, n);

        for (int x = x0; x < x1; x++)
        for (int z = z0; z < z1; z++)
        {
            Span[] spans = d.Spans[x, z];
            if (spans == null || spans.Length == 0) continue;

            for (int i = 0; i < spans.Length; i++)
            {
                Span s = spans[i];
                // A lip is a rock roof, whatever the ground under it is made of.
                byte material = i > 0 ? (byte)SurfaceMaterial.Stone : d.Material[x, z];

                Horizontal(ground, x, z, s.Top + 1, Vector3.Up,
                           tint.Ground(d, x, z, i, FaceKind.Top), new Vector2(material, (int)FaceKind.Top));
                Horizontal(ground, x, z, s.Bottom, Vector3.Down,
                           tint.Ground(d, x, z, i, FaceKind.Bottom), new Vector2(material, (int)FaceKind.Bottom));

                Color side = tint.Ground(d, x, z, i, FaceKind.Side);
                var sideTag = new Vector2(material, (int)FaceKind.Side);
                foreach ((int dx, int dz) in Sides)
                {
                    SolidCover(d, x + dx, z + dz, cover);
                    Exposed(ground, x, z, dx, dz, s.Bottom, s.Top, cover, side, sideTag);
                }
            }

            short level = d.WaterLevel[x, z];
            short bed = spans[0].Top;
            if (level == IslandData.NoLand || level <= bed) continue;

            byte fluid = d.Fluid[x, z];
            bool isGoo = fluid == (byte)FluidKind.Goo;
            MeshBuffer into = isGoo ? goo : water;
            Color colour = isGoo ? Colors.White : tint.Liquid(d, x, z);
            Horizontal(into, x, z, level + 1, Vector3.Up, colour, new Vector2(fluid, (int)FaceKind.Top));
            var wallTag = new Vector2(fluid, (int)FaceKind.Side);
            foreach ((int dx, int dz) in Sides)
            {
                FluidCover(d, x + dx, z + dz, fluid, cover);
                Exposed(into, x, z, dx, dz, bed + 1, level, cover, colour, wallTag);
            }
        }
    }

    /// <summary>The neighbour's solid spans, or nothing off the grid or in the aether. Bottom-up, as stored.</summary>
    private static void SolidCover(IslandData d, int x, int z, List<Span> into)
    {
        into.Clear();
        if (x < 0 || z < 0 || x >= d.Size || z >= d.Size) return;
        Span[] spans = d.Spans[x, z];
        if (spans == null) return;
        into.AddRange(spans);
    }

    /// <summary>
    /// The neighbour's solid spans with its standing water of the same kind folded into
    /// the lowest, so water meets water without a face. Water of another kind, or none,
    /// leaves the range open and the face is drawn.
    /// </summary>
    private static void FluidCover(IslandData d, int x, int z, byte fluid, List<Span> into)
    {
        SolidCover(d, x, z, into);
        if (into.Count == 0) return;
        short level = d.WaterLevel[x, z];
        if (level == IslandData.NoLand || level <= into[0].Top || d.Fluid[x, z] != fluid) return;
        // The water sits on the ground span; extend it up, swallowing any lip the water reaches.
        into[0] = new Span(into[0].Bottom, level);
        while (into.Count > 1 && into[1].Bottom <= level + 1)
        {
            into[0] = new Span(into[0].Bottom, Math.Max(level, into[1].Top));
            into.RemoveAt(1);
        }
    }

    /// <summary>The parts of slabs <paramref name="lo"/>..<paramref name="hi"/> that no span in <paramref name="cover"/> fills, as side faces.</summary>
    private static void Exposed(MeshBuffer into, int x, int z, int dx, int dz, int lo, int hi,
                                List<Span> cover, Color colour, Vector2 tag)
    {
        int a = lo;
        foreach (Span c in cover)
        {
            if (c.Top < a) continue;
            if (c.Bottom > hi) break;
            if (c.Bottom > a) Vertical(into, x, z, dx, dz, a, c.Bottom - 1, colour, tag);
            a = Math.Max(a, c.Top + 1);
            if (a > hi) return;
        }
        if (a <= hi) Vertical(into, x, z, dx, dz, a, hi, colour, tag);
    }

    /// <summary>A horizontal face over cell (x, z) at slab boundary <paramref name="level"/>, facing up or down.</summary>
    private static void Horizontal(MeshBuffer into, int x, int z, int level, Vector3 normal, Color colour, Vector2 tag)
    {
        float y = level * SlabHeight;
        float xa = (x - 0.5f) * CellSize, xb = (x + 0.5f) * CellSize;
        float za = (z - 0.5f) * CellSize, zb = (z + 0.5f) * CellSize;
        into.AddQuad(
            new Vector3(xa, y, za), new Vector3(xb, y, za), new Vector3(xb, y, zb), new Vector3(xa, y, zb),
            new Vector2(xa, za), new Vector2(xb, za), new Vector2(xb, zb), new Vector2(xa, zb),
            normal, tag, colour);
    }

    /// <summary>A vertical face on the (dx, dz) side of cell (x, z), slabs <paramref name="lo"/>..<paramref name="hi"/> inclusive.</summary>
    private static void Vertical(MeshBuffer into, int x, int z, int dx, int dz, int lo, int hi, Color colour, Vector2 tag)
    {
        float ya = lo * SlabHeight, yb = (hi + 1) * SlabHeight;
        var normal = new Vector3(dx, 0f, dz);
        if (dx != 0)
        {
            float fx = (x + 0.5f * dx) * CellSize;
            float za = (z - 0.5f) * CellSize, zb = (z + 0.5f) * CellSize;
            into.AddQuad(
                new Vector3(fx, ya, za), new Vector3(fx, ya, zb), new Vector3(fx, yb, zb), new Vector3(fx, yb, za),
                new Vector2(za, ya), new Vector2(zb, ya), new Vector2(zb, yb), new Vector2(za, yb),
                normal, tag, colour);
        }
        else
        {
            float fz = (z + 0.5f * dz) * CellSize;
            float xa = (x - 0.5f) * CellSize, xb = (x + 0.5f) * CellSize;
            into.AddQuad(
                new Vector3(xa, ya, fz), new Vector3(xb, ya, fz), new Vector3(xb, yb, fz), new Vector3(xa, yb, fz),
                new Vector2(xa, ya), new Vector2(xb, ya), new Vector2(xb, yb), new Vector2(xa, yb),
                normal, tag, colour);
        }
    }
}
