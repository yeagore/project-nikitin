using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Meshing;

/// <summary>
/// The provisional look of each <see cref="SurfaceMaterial"/>: one flat colour the
/// mesher tints a column's faces with until the biome layer brings textures. The
/// lab's legend and the audit's sheets read this same table, so a swatch there is
/// the colour on the ground. The colours are the alchemical colours of the soil
/// glossary (Notion, "[CLAUDE] The Soil Glossary"), as RGBA hex; ooze is not in it
/// and keeps its own. Five are nudged off the glossary (2026-09-20), each noted
/// beside it: two that blended with a neighbour on the ground, one too dark for
/// the light to shade, and two Maxim asked for.
/// </summary>
public static class SurfacePalette
{
    public static Color Of(SurfaceMaterial m) => m switch
    {
        SurfaceMaterial.Stone => new Color(0x62676Bff),               // dark grey
        SurfaceMaterial.Scree => new Color(0xABA498ff),               // pale warm grey
        SurfaceMaterial.Snow => new Color(0xF4F6F7ff),
        SurfaceMaterial.Sand => new Color(0xF1DD8Bff),
        SurfaceMaterial.Silt => new Color(0x8F866Bff),
        SurfaceMaterial.Frostearth => new Color(0x9FB0B8ff),          // cool grey (glossary #A6A6A6: it read as scree)
        SurfaceMaterial.Bleachearth => new Color(0xC3A2B8ff),         // ash mauve
        SurfaceMaterial.Shadowearth => new Color(0x5A5675ff),         // violet slate (glossary #2F4A6B: it read as water)
        SurfaceMaterial.Murkearth => new Color(0x7E9270ff),           // sage
        SurfaceMaterial.Dryearth => new Color(0xD8A86Eff),            // tan
        SurfaceMaterial.Blackearth => new Color(0x443A31ff),          // very dark umber (glossary #2A2623: too dark to shade)
        SurfaceMaterial.Brownearth => new Color(0x74483Fff),          // brown
        SurfaceMaterial.Muckearth => new Color(0x3FA39Bff),           // teal
        SurfaceMaterial.Dustearth => new Color(0xEADFC8ff),           // bone
        SurfaceMaterial.Yellowearth => new Color(0xC39A22ff),         // yellow ochre (glossary #B3872F, asked yellower)
        SurfaceMaterial.Redearth => new Color(0xA8463Fff),            // muted red (glossary #B5302B, asked less saturated)
        SurfaceMaterial.Floodearth => new Color(0x7A3E78ff),          // plum
        SurfaceMaterial.Ooze => new Color(0.16f, 0.14f, 0.18f),       // near-black plum: a deep floor reads dark through the water
        _ => new Color(1f, 0f, 1f),                                   // an unmapped member: loud on purpose
    };
}
