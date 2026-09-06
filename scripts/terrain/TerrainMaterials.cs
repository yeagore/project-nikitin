using Godot;

namespace ProjectNikitin.Meshing;

/// <summary>
/// The three materials the mesh is drawn with, and the factories the lab shares so a
/// box there and a face here read the same. Ground is matte and unspecular: a face's
/// colour is its vertex colour times the light. Water and goo are alpha-blended and
/// double-sided, so a tilt under the island still shows them.
/// </summary>
public sealed class TerrainMaterials
{
    public Material Ground { get; init; } = GroundMaterial();

    public Material Water { get; init; } = WaterMaterial(0.66f);

    public Material Goo { get; init; } = GooMaterial();

    public static StandardMaterial3D GroundMaterial() => new()
    {
        VertexColorUseAsAlbedo = true,
        Roughness = 1f,
        Metallic = 0f,
        SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
    };

    /// <summary>Blue, tinted by the vertex colour where one is set (the lab colours water by kind).</summary>
    public static StandardMaterial3D WaterMaterial(float alpha) => new()
    {
        AlbedoColor = new Color(0.16f, 0.42f, 0.62f, alpha),
        VertexColorUseAsAlbedo = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        Roughness = 0.12f,
        Metallic = 0.1f,
    };

    /// <summary>Violet, and deaf to vertex colour: the water blue would multiply any warm tint down to nothing, and goo has one look.</summary>
    public static StandardMaterial3D GooMaterial() => new()
    {
        AlbedoColor = new Color(0.52f, 0.14f, 0.72f, 0.9f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        Roughness = 0.05f,
        Metallic = 0.2f,
    };
}
