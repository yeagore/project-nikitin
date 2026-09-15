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

    /// <summary>The water's own blue, which a vertex colour then tints.</summary>
    public static readonly Color WaterBlue = new(0.16f, 0.42f, 0.62f);

    /// <summary>Blue, tinted by the vertex colour where one is set (the lab colours water by kind).</summary>
    public static StandardMaterial3D WaterMaterial(float alpha) => WaterMaterial(alpha, WaterBlue);

    /// <summary>
    /// Water of a chosen albedo, since the vertex colour multiplies it: white lets a
    /// colour through as it was asked for, which the blue would otherwise crush to
    /// nothing on a warm hue. The lab uses it for a view that colours the water by
    /// what it means rather than by what kind of water it is.
    /// </summary>
    public static StandardMaterial3D WaterMaterial(float alpha, Color albedo) => new()
    {
        AlbedoColor = new Color(albedo.R, albedo.G, albedo.B, alpha),
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
