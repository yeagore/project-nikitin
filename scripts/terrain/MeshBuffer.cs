using System.Collections.Generic;
using Godot;

namespace ProjectNikitin.Meshing;

/// <summary>
/// The vertices of one mesh surface in the making: flat-shaded quads, four vertices
/// each, with a normal, a texture coordinate in metres, a second one carrying what
/// the face is (material or fluid, and its <see cref="FaceKind"/>) for a shader to
/// read, and a colour. Reused across chunks: <see cref="Clear"/> keeps the capacity.
/// </summary>
public sealed class MeshBuffer
{
    /// <summary>
    /// Godot draws a triangle's front where its vertices run clockwise on screen.
    /// <see cref="AddQuad"/> orients every quad to this whatever order its corners
    /// arrive in; the mesh bench's winding probe checks the constant against a BoxMesh.
    /// </summary>
    public const bool FrontIsClockwise = true;

    private readonly List<Vector3> _vertices = new();
    private readonly List<Vector3> _normals = new();
    private readonly List<Vector2> _uv = new();
    private readonly List<Vector2> _uv2 = new();
    private readonly List<Color> _colors = new();
    private readonly List<int> _indices = new();

    public int Triangles => _indices.Count / 3;

    public int Vertices => _vertices.Count;

    public bool IsEmpty => _indices.Count == 0;

    /// <summary>Bytes the surface takes on the GPU, near enough: five attributes and the indices.</summary>
    public long Bytes => _vertices.Count * (12L + 12 + 8 + 8 + 16) + _indices.Count * 4L;

    public void Clear()
    {
        _vertices.Clear();
        _normals.Clear();
        _uv.Clear();
        _uv2.Clear();
        _colors.Clear();
        _indices.Clear();
    }

    /// <summary>
    /// One quad, corners in ring order round the face, each with its texture coordinate,
    /// wound so that <paramref name="normal"/> is the front.
    /// </summary>
    public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                        Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud,
                        Vector3 normal, Vector2 tag, Color color)
    {
        // (b − a) × (c − a) points toward whoever sees the ring run anticlockwise;
        // reverse the ring when that is the front and the front must run clockwise.
        bool anticlockwiseFromFront = (b - a).Cross(c - a).Dot(normal) > 0f;
        if (anticlockwiseFromFront == FrontIsClockwise)
        {
            (b, d) = (d, b);
            (ub, ud) = (ud, ub);
        }

        int i0 = _vertices.Count;
        _vertices.Add(a); _vertices.Add(b); _vertices.Add(c); _vertices.Add(d);
        _uv.Add(ua); _uv.Add(ub); _uv.Add(uc); _uv.Add(ud);
        for (int i = 0; i < 4; i++)
        {
            _normals.Add(normal);
            _uv2.Add(tag);
            _colors.Add(color);
        }
        _indices.Add(i0); _indices.Add(i0 + 1); _indices.Add(i0 + 2);
        _indices.Add(i0); _indices.Add(i0 + 2); _indices.Add(i0 + 3);
    }

    /// <summary>The surface as <see cref="ArrayMesh.AddSurfaceFromArrays"/> takes it.</summary>
    public Godot.Collections.Array ToArrays()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = _uv.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV2] = _uv2.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _colors.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = _indices.ToArray();
        return arrays;
    }

    /// <summary>Every triangle as three points: the form <see cref="ConcavePolygonShape3D.Data"/> takes.</summary>
    public Vector3[] ToFaces()
    {
        var faces = new Vector3[_indices.Count];
        for (int i = 0; i < _indices.Count; i++) faces[i] = _vertices[_indices[i]];
        return faces;
    }

    /// <summary>The surface's area in square metres, summed over its triangles. The bench checks it against a slab-by-slab count.</summary>
    public double Area()
    {
        double area = 0;
        for (int i = 0; i + 2 < _indices.Count; i += 3)
        {
            Vector3 a = _vertices[_indices[i]], b = _vertices[_indices[i + 1]], c = _vertices[_indices[i + 2]];
            area += 0.5 * (b - a).Cross(c - a).Length();
        }
        return area;
    }
}
