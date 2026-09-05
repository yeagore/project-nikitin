using System;
using Godot;
using ProjectNikitin.Generation;
using static ProjectNikitin.Generation.Terrain;

namespace ProjectNikitin.Meshing;

/// <summary>
/// Draws an <see cref="IslandData"/> as chunked meshes with colliders: the terrain
/// renderer. In this node's local space cell (x, z) is centred on
/// <c>(x · CellSize, ·, z · CellSize)</c> and slab y fills
/// <c>[y · SlabHeight, (y + 1) · SlabHeight)</c>, so the island's corner column stands
/// at the origin and placing the node places the island. <see cref="Show"/> builds
/// every chunk; <see cref="RebuildAround"/> remeshes the chunk holding one column and
/// the neighbours its border faces depend on.
/// </summary>
public partial class IslandRenderer : Node3D
{
    /// <summary>How faces are coloured; the lab swaps in one per view. Applied at the next build or <see cref="Retint"/>.</summary>
    public IslandTint Tint { get; set; } = IslandTint.Default;

    public TerrainMaterials Materials { get; set; } = new();

    /// <summary>Whether each chunk gets a trimesh collider over its ground.</summary>
    [Export] public bool Colliders { get; set; } = true;

    public IslandData? Data { get; private set; }

    public int GroundTriangles { get; private set; }

    public int LiquidTriangles { get; private set; }

    /// <summary>Milliseconds the last <see cref="Show"/> or <see cref="Retint"/> took, meshing and node work together.</summary>
    public float LastBuildMs { get; private set; }

    /// <summary>Centre of the built terrain in local space, and the radius of a sphere round it: what a camera frames.</summary>
    public Vector3 Center { get; private set; }

    public float Radius { get; private set; } = 10f;

    private TerrainChunk[,]? _chunks;
    private bool _liquidVisible = true;
    private readonly MeshBuffer _ground = new(), _water = new(), _goo = new();

    public bool LiquidVisible
    {
        get => _liquidVisible;
        set
        {
            _liquidVisible = value;
            if (_chunks == null) return;
            foreach (TerrainChunk chunk in _chunks) chunk.LiquidVisible = value;
        }
    }

    /// <summary>Drops whatever is shown and builds every chunk of <paramref name="data"/>.</summary>
    public void Show(IslandData data)
    {
        Clear();
        Data = data;
        ulong t0 = Time.GetTicksUsec();
        int across = ChunkMesher.ChunksAcross(data.Size);
        _chunks = new TerrainChunk[across, across];
        for (int cx = 0; cx < across; cx++)
        for (int cz = 0; cz < across; cz++)
        {
            var chunk = new TerrainChunk { Name = $"Chunk_{cx}_{cz}" };
            AddChild(chunk);
            _chunks[cx, cz] = chunk;
            Build(cx, cz);
        }
        Tally();
        Measure(data);
        LastBuildMs = (Time.GetTicksUsec() - t0) / 1000f;
    }

    /// <summary>Rebuilds every chunk from the same data, for a changed <see cref="Tint"/>.</summary>
    public void Retint()
    {
        if (Data == null || _chunks == null) return;
        ulong t0 = Time.GetTicksUsec();
        int across = _chunks.GetLength(0);
        for (int cx = 0; cx < across; cx++)
        for (int cz = 0; cz < across; cz++)
            Build(cx, cz);
        Tally();
        LastBuildMs = (Time.GetTicksUsec() - t0) / 1000f;
    }

    /// <summary>Remeshes one chunk.</summary>
    public void Rebuild(int cx, int cz)
    {
        if (_chunks == null) return;
        Build(cx, cz);
        Tally();
    }

    /// <summary>Remeshes the chunk holding column (x, z), and the neighbouring chunk on each side the column borders.</summary>
    public void RebuildAround(int x, int z)
    {
        if (_chunks == null) return;
        int across = _chunks.GetLength(0);
        int cx = x / ChunkMesher.ChunkSize, cz = z / ChunkMesher.ChunkSize;
        int cx0 = x % ChunkMesher.ChunkSize == 0 ? Math.Max(0, cx - 1) : cx;
        int cx1 = x % ChunkMesher.ChunkSize == ChunkMesher.ChunkSize - 1 ? Math.Min(across - 1, cx + 1) : cx;
        int cz0 = z % ChunkMesher.ChunkSize == 0 ? Math.Max(0, cz - 1) : cz;
        int cz1 = z % ChunkMesher.ChunkSize == ChunkMesher.ChunkSize - 1 ? Math.Min(across - 1, cz + 1) : cz;
        for (int i = cx0; i <= cx1; i++)
        for (int j = cz0; j <= cz1; j++)
            Build(i, j);
        Tally();
    }

    public void Clear()
    {
        if (_chunks != null)
            foreach (TerrainChunk chunk in _chunks)
            {
                RemoveChild(chunk);
                chunk.QueueFree();
            }
        _chunks = null;
        Data = null;
        GroundTriangles = 0;
        LiquidTriangles = 0;
    }

    private void Build(int cx, int cz)
    {
        ChunkMesher.Build(Data!, cx, cz, Tint, _ground, _water, _goo);
        _chunks![cx, cz].Apply(_ground, _water, _goo, Materials, Colliders, _liquidVisible);
    }

    private void Tally()
    {
        int ground = 0, liquid = 0;
        if (_chunks != null)
            foreach (TerrainChunk chunk in _chunks)
            {
                ground += chunk.GroundTriangles;
                liquid += chunk.LiquidTriangles;
            }
        GroundTriangles = ground;
        LiquidTriangles = liquid;
    }

    /// <summary>The land's bounding box, water included, as a centre and a radius.</summary>
    private void Measure(IslandData d)
    {
        var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int x = 0; x < d.Size; x++)
        for (int z = 0; z < d.Size; z++)
        {
            Span[] spans = d.Spans[x, z];
            if (spans == null || spans.Length == 0) continue;
            int top = Math.Max(spans[^1].Top, d.WaterLevel[x, z]) + 1;
            lo = lo.Min(new Vector3((x - 0.5f) * CellSize, spans[0].Bottom * SlabHeight, (z - 0.5f) * CellSize));
            hi = hi.Max(new Vector3((x + 0.5f) * CellSize, top * SlabHeight, (z + 0.5f) * CellSize));
        }
        if (lo.X > hi.X) { Center = Vector3.Zero; Radius = 10f; return; }
        Center = (lo + hi) * 0.5f;
        Radius = Mathf.Max(1f, (hi - lo).Length() * 0.5f);
    }
}
