using Godot;

namespace ProjectNikitin.Meshing;

/// <summary>
/// One 16 × 16-column tile of the island: a ground mesh, a liquid mesh (water and goo
/// as two surfaces) and a trimesh collider over the ground. Rebuilt whole when any
/// column in it changes, and its neighbours' tiles too where the change is on a
/// border, since a side face depends on the column across it.
/// </summary>
public partial class TerrainChunk : StaticBody3D
{
    private MeshInstance3D? _ground;
    private MeshInstance3D? _liquid;
    private CollisionShape3D? _shape;

    public int GroundTriangles { get; private set; }

    public int LiquidTriangles { get; private set; }

    public bool LiquidVisible
    {
        get => _liquid?.Visible ?? true;
        set { if (_liquid != null) _liquid.Visible = value; }
    }

    /// <summary>Replaces the tile's meshes and collider with the buffers' contents.</summary>
    public void Apply(MeshBuffer ground, MeshBuffer water, MeshBuffer goo,
                      TerrainMaterials materials, bool collider, bool liquidVisible)
    {
        Ensure();

        if (ground.IsEmpty)
        {
            _ground!.Mesh = null;
        }
        else
        {
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, ground.ToArrays());
            mesh.SurfaceSetMaterial(0, materials.Ground);
            _ground!.Mesh = mesh;
        }
        GroundTriangles = ground.Triangles;

        if (water.IsEmpty && goo.IsEmpty)
        {
            _liquid!.Mesh = null;
        }
        else
        {
            var mesh = new ArrayMesh();
            int surface = 0;
            if (!water.IsEmpty)
            {
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, water.ToArrays());
                mesh.SurfaceSetMaterial(surface++, materials.Water);
            }
            if (!goo.IsEmpty)
            {
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, goo.ToArrays());
                mesh.SurfaceSetMaterial(surface, materials.Goo);
            }
            _liquid!.Mesh = mesh;
        }
        _liquid!.Visible = liquidVisible;
        LiquidTriangles = water.Triangles + goo.Triangles;

        _shape!.Shape = collider && !ground.IsEmpty
            ? new ConcavePolygonShape3D { Data = ground.ToFaces() }
            : null;
        _shape.Disabled = _shape.Shape == null;
    }

    private void Ensure()
    {
        if (_ground != null) return;
        _ground = new MeshInstance3D { Name = "Ground" };
        _liquid = new MeshInstance3D
        {
            Name = "Liquid",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _shape = new CollisionShape3D { Name = "Collider" };
        AddChild(_ground);
        AddChild(_liquid);
        AddChild(_shape);
    }
}
