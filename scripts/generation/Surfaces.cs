using System;
using System.Collections.Generic;

using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

/// <summary>
/// Collects the feature anchors (coast, cliff and scarp brinks and feet, banks, beds, summits)
/// and the provisional <see cref="SurfaceMaterial"/>. Everything is measured against
/// <see cref="IslandData.EffectiveLevel"/> — the water surface where a column is
/// flooded — otherwise every river bank reads as a cliff over its own bed. The
/// springs, the falls, the sea stacks and the deltas are anchors the water and
/// footprint stages wrote already.
/// </summary>
internal static class Surfaces
{
    /// <summary>
    /// Slabs of face that bare the rock on a rocky landform. This was the cliff threshold
    /// until cliffs became four (<see cref="Traversal.CliffFace"/>); a rocky scarp of
    /// three still shows stone, so the look did not move with the name.
    /// </summary>
    private const int RockFace = 3;

    /// <summary>Slabs of face that bare the rock whatever the landform: a plateau rung or a mesa wall is not one, a mountain flank or a canyon is.</summary>
    private const int TallFace = 6;

    /// <summary>Ruggedness (32 per slab) at which a rock landform shows stone off its cliffs.</summary>
    private const int RockyStoneAt = 144;

    /// <summary>Ruggedness at which a rock landform shows scree.</summary>
    private const int RockyScreeAt = 96;

    /// <summary>Ruggedness at which soft ground turns to scree: seven slabs in five cells, which only stacked rungs manage.</summary>
    private const int BrokenAt = 224;

    // ---- the climate grid --------------------------------------------------
    // Warmth in four bands (frigid, cold, temperate, hot) and moisture in three,
    // with two cells for water in excess — murkearth on the cold-to-cool half of the
    // warmth range, muckearth on the warm-to-hot half — and sand and snow past the ends.

    /// <summary>Warmth below which ground is frozen: the extreme cold, and a mountain above its frostearth.</summary>
    internal const int SnowBelow = 35;

    // The bands are placed on the knob: warmth is 60 + 180 × the knob on open
    // lowland, so frigid is a knob under about 0.14, cold under about 0.3, hot one
    // over about 0.7, and sand the last twentieth.

    /// <summary>Warmth below which the ground is frigid: frostearth whatever the moisture, but for murkearth.</summary>
    private const int FrigidBelow = 85;

    /// <summary>Warmth below which the ground is the cold band: frostearth, bleachearth, shadowearth.</summary>
    private const int ColdBelow = 115;

    /// <summary>
    /// Warmth from which the excess cell is muckearth rather than murkearth: the warm part of
    /// the temperate band and everything hotter. Just under the knob's middle (150)
    /// less what the water's tempering takes off a wet bank, so a temperate Domain's
    /// riversides are warm-side and a cool one's (a knob of 0.4 and under) murkearth-side.
    /// </summary>
    private const int WarmFrom = 140;

    /// <summary>Warmth from which the ground is the hot band: dustearth, yellowearth, floodearth.</summary>
    private const int HotFrom = 185;

    /// <summary>Warmth from which hot ground is sand: the extreme heat. Floodearth still beats it.</summary>
    private const int SandFrom = 220;

    /// <summary>Moisture below which the ground is dry: dustearth, dryearth, frostearth.</summary>
    private const int DryBelow = 90;

    /// <summary>Moisture from which the ground is wet: floodearth, brownearth, murkearth.</summary>
    private const int WetFrom = 170;

    /// <summary>Cells from fresh water a hot floodearth reaches; wet hot ground further off is yellowearth.</summary>
    private const int FloodearthReach = 3;

    /// <summary>
    /// Warmth from which a wet riverside is floodearth: the hot line less what the
    /// water's tempering takes off a bank (135 + 0.7 × (185 − 135)), so the bank and
    /// the strip behind it read the same and floodearth never starts a cell away
    /// from its river.
    /// </summary>
    private const int FloodearthFrom = 170;

    /// <summary>Moisture from which hot ground is redearth rather than yellowearth: a higher bar than brownearth, since heat is the less forgiving side.</summary>
    private const int HotWetFrom = 200;

    /// <summary>Moisture from which cold-to-cool ground may be murkearth: past wet, water in excess.</summary>
    private const int MurkearthFrom = 190;

    /// <summary>The noise bar such ground must clear to be murkearth: in patches, and more of them than there are of muckearth, but a tenth of a wet cool Domain and not a fifth.</summary>
    private const float MurkearthBar = 0.66f;

    /// <summary>Moisture from which warm-to-hot ground beside the water may be muckearth: extreme, so a high background and the water's own strip both.</summary>
    private const int MuckearthFrom = 230;

    /// <summary>Cells from fresh water muckearth reaches.</summary>
    private const int MuckearthReach = 2;

    /// <summary>Ruggedness (32 per slab) muckearth tolerates: flat, give or take a slab, so the ground is low as well as near.</summary>
    private const int MuckearthFlat = 40;

    /// <summary>The noise bar such ground must clear to be muckearth: occasional.</summary>
    private const float MuckearthBar = 0.62f;

    /// <summary>The noise bar a plain or a hillside must clear to show a tor: a small outcrop, rare.</summary>
    private const float TorBar = 0.87f;

    /// <summary>Slabs of water at most over a shallow bed: wading depth, where reeds and shoals go. A mid bed lies between this and <see cref="DeepBed"/>.</summary>
    public const int ShallowBed = 2;

    /// <summary>Slabs of water at least over a deep bed: from here down the bed is ooze, and what lives there lives in the dark.</summary>
    public const int DeepBed = 9;

    /// <summary>Rebuilds the anchor lists in scan order and picks every column's material.</summary>
    public static void Classify(int seed, IslandData d)
    {
        int n = d.Size;
        var murkearth = new Noise(seed + 71_019, 0.07f, octaves: 2);
        var muckearth = new Noise(seed + 71_029, 0.09f, octaves: 2);
        var tor = new Noise(seed + 71_027, 0.16f, octaves: 1);
        // Cells from fresh water (goo waters nothing), as far as floodearth reaches; -1 beyond.
        int[,] toWater = Flood.Distance(n,
            (x, z) => d.HasLand(x, z) && d.WaterLevel[x, z] != IslandData.NoLand
                      && d.Fluid[x, z] != (byte)FluidKind.Goo,
            (_, _, nx, nz) => d.HasLand(nx, nz),
            cap: FloodearthReach);

        d.CoastCells.Clear();
        d.CliffCells.Clear();
        d.CliffFootCells.Clear();
        d.ScarpCells.Clear();
        d.ScarpFootCells.Clear();
        d.BankCells.Clear();
        d.RiverBedCells.Clear();
        d.LakeBedCells.Clear();
        d.ShallowBedCells.Clear();
        d.MidBedCells.Clear();
        d.DeepBedCells.Clear();
        d.Summits.Clear();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            short eff = d.EffectiveLevel(x, z);
            bool dry = d.WaterLevel[x, z] == IslandData.NoLand;

            if (!dry)
            {
                if (d.River[x, z]) d.RiverBedCells.Add(new Vector2I(x, z));
                else if (d.Fluid[x, z] == (byte)FluidKind.Water)
                {
                    d.LakeBedCells.Add(new Vector2I(x, z));
                    int depth = d.WaterDepth(x, z);
                    if (depth <= ShallowBed) d.ShallowBedCells.Add(new Vector2I(x, z));
                    else if (depth >= DeepBed) d.DeepBedCells.Add(new Vector2I(x, z));
                    else d.MidBedCells.Add(new Vector2I(x, z));
                }
            }

            bool coast = false, bank = false, gooSide = false;
            bool scarpDown = false, scarpUp = false;
            int drop = 0, face = 0;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz))
                {
                    coast = true;
                    continue;
                }
                short ne = d.EffectiveLevel(nx, nz);
                drop = Math.Max(drop, eff - ne);
                face = Math.Max(face, ne - eff);
                // Each face on its own: over a cliff one way and a scarp another is both
                // kinds of brink, where drop and face keep only the tallest.
                int down = eff - ne;
                if (down > Traversal.FreeStep && down < Traversal.CliffFace) scarpDown = true;
                if (-down > Traversal.FreeStep && -down < Traversal.CliffFace) scarpUp = true;

                if (!dry || d.WaterLevel[nx, nz] == IslandData.NoLand) continue;
                if (d.Fluid[nx, nz] == (byte)FluidKind.Goo) gooSide = true;
                else if (eff - d.WaterLevel[nx, nz] is >= 0 and <= 1) bank = true;
            }

            if (coast) d.CoastCells.Add(new Vector2I(x, z));
            if (dry && drop >= Traversal.CliffFace) d.CliffCells.Add(new Vector2I(x, z));
            if (dry && face >= Traversal.CliffFace) d.CliffFootCells.Add(new Vector2I(x, z));
            if (dry && scarpDown) d.ScarpCells.Add(new Vector2I(x, z));
            if (dry && scarpUp) d.ScarpFootCells.Add(new Vector2I(x, z));
            if (bank && !d.Beach[x, z] && !d.Landings[x, z])
                d.BankCells.Add(new Vector2I(x, z));

            int near = toWater[x, z] < 0 ? int.MaxValue : toWater[x, z];
            d.Material[x, z] = (byte)Pick(d, x, z, drop, face, gooSide, near, murkearth, muckearth, tor);
        }

        WipeStrandedFloodearth(d);
        FindSummits(d);
    }

    /// <summary>
    /// Floodearth is the flat beside the water: any patch of it that does not
    /// touch fresh water through other floodearth is yellowearth instead. One flood
    /// over the footprint, so it costs nothing worth measuring.
    /// </summary>
    private static void WipeStrandedFloodearth(IslandData d)
    {
        int n = d.Size;
        int[,] linked = Flood.Distance(n,
            (x, z) => d.HasLand(x, z) && d.WaterLevel[x, z] != IslandData.NoLand
                      && d.Fluid[x, z] != (byte)FluidKind.Goo,
            (_, _, nx, nz) => d.HasLand(nx, nz) && d.Material[nx, nz] == (byte)SurfaceMaterial.Floodearth);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (d.Material[x, z] == (byte)SurfaceMaterial.Floodearth && linked[x, z] < 0)
                d.Material[x, z] = (byte)SurfaceMaterial.Yellowearth;
    }

    /// <summary>
    /// The highest dry cells, greedily spaced. The minimum rise is absolute (half
    /// the mountain cap above the lowest ground), so a flat island has no summits.
    /// </summary>
    private static void FindSummits(IslandData d)
    {
        int n = d.Size;
        short low = short.MaxValue;
        var peaks = new List<Vector2I>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            short eff = d.EffectiveLevel(x, z);
            if (eff == IslandData.NoLand) continue;
            if (eff < low) low = eff;
            if (d.WaterLevel[x, z] == IslandData.NoLand) peaks.Add(new Vector2I(x, z));
        }
        if (peaks.Count == 0) return;

        float cap = Habitat.MountainCap(d.Size);
        int minRise = Math.Max(8, Mathf.RoundToInt(cap / 2f));
        int spacing = Math.Max(8, n / 8);

        peaks.Sort((a, b) => d.SurfaceLevel(b.X, b.Y).CompareTo(d.SurfaceLevel(a.X, a.Y)));

        foreach (Vector2I c in peaks)
        {
            if (d.SurfaceLevel(c.X, c.Y) - low < minRise) break;
            bool crowded = false;
            foreach (Vector2I had in d.Summits)
                if (Math.Abs(had.X - c.X) + Math.Abs(had.Y - c.Y) < spacing)
                {
                    crowded = true;
                    break;
                }
            if (!crowded) d.Summits.Add(c);
        }
    }

    /// <summary>Whether the landform is made of rock: the only ground that bares stone off a tall face, and the ground drought patches fall on.</summary>
    internal static bool Rocky(LandformType form)
        => form is LandformType.Mountain or LandformType.Massif or LandformType.Karst
                or LandformType.Badlands or LandformType.Sinkholes;

    /// <summary>
    /// The material at one cell. Beds first; goo's bed and shore are stone; then
    /// snow; then rock — a tall face bares stone at its brink and drops scree at its
    /// foot whatever the landform, and a rock landform shows stone and scree
    /// wherever it is broken, so a mountain is stone up to its snow; then the dunes
    /// and the sculpted rock (a dune field is sand only where it is not cold); then
    /// a delta's fan, the wet ground of its row; then
    /// the tors, small outcrops of stone in soft country; then the water in excess
    /// — muckearth on warm-to-hot ground, murkearth on cold-to-cool — and then the climate
    /// grid, warmth against moisture. A beach is ground like any other — nothing
    /// washes it — and a plateau rung in soft country changes nothing: the ground
    /// runs up to the edge.
    /// </summary>
    private static SurfaceMaterial Pick(IslandData d, int x, int z, int drop, int face,
                                        bool gooSide, int near, Noise murkearth, Noise muckearth, Noise tor)
    {
        if (d.WaterLevel[x, z] != IslandData.NoLand)
        {
            if (d.Fluid[x, z] == (byte)FluidKind.Goo) return SurfaceMaterial.Stone;
            return d.WaterDepth(x, z) >= DeepBed ? SurfaceMaterial.Ooze : SurfaceMaterial.Silt;
        }
        if (gooSide) return SurfaceMaterial.Stone;

        byte warmth = d.Warmth[x, z];
        if (warmth < SnowBelow) return SurfaceMaterial.Snow;

        byte rugged = d.Ruggedness[x, z];
        var form = (LandformType)d.Landform[x, z];
        bool rocky = Rocky(form) || d.Canyon[x, z];

        if (drop >= TallFace) return SurfaceMaterial.Stone;
        if (face >= TallFace) return SurfaceMaterial.Scree;        // talus under the face

        if (rocky && (drop >= RockFace || face >= RockFace || rugged >= RockyStoneAt))
            return SurfaceMaterial.Stone;
        if (rocky && rugged >= RockyScreeAt) return SurfaceMaterial.Scree;
        if (rugged >= BrokenAt) return SurfaceMaterial.Scree;

        // A dune field is sand where it is warm enough to be one; in the cold band and
        // under it the ridges stay and wear the climate's ground, a frozen dune field
        // under frostearth rather than a pile of sand in it.
        if (form == LandformType.Dunes && warmth >= ColdBelow) return SurfaceMaterial.Sand;
        // Broken rock, not a desert: dustearth here put a hot-band ground in cold country.
        if (form is LandformType.Badlands or LandformType.Karst or LandformType.Sinkholes)
            return SurfaceMaterial.Scree;

        // A delta's fan is the river's own wet ground: the wet cell of its row
        // (floodearth on a hot Domain, brownearth on a temperate, shadowearth on a cold,
        // frostearth where it is frigid), never a hot ground in a cold country.
        if (d.Delta[x, z])
            return Climate(warmth, Math.Max(d.Moisture[x, z], (byte)WetFrom), 1, rugged, 0f, 0f);

        // A tor: building stone in soft country, where no rock landform is.
        if (form is LandformType.Plain or LandformType.Hills && tor.At(x, z) > TorBar)
            return SurfaceMaterial.Stone;

        // The climate grid, for the ground that is not rock.
        return Climate(warmth, d.Moisture[x, z], near, rugged, murkearth.At(x, z), muckearth.At(x, z));
    }

    /// <summary>
    /// The living ground for one climate: warmth against moisture, with
    /// <paramref name="near"/> cells to fresh water (<c>int.MaxValue</c> for none)
    /// and the ruggedness for muckearth's flatness, and the two noise values that
    /// gate the patches of water in excess. Muckearth first on the warm-to-hot half and
    /// murkearth on the cold-to-cool half, since neither is the rule — muckearth wants
    /// extreme moisture, a high background and the water's strip both, on flat
    /// ground beside the water; murkearth only asks for the excess, so there is more
    /// murkearth than muckearth. Then the bands: frigid ground is frostearth whatever the
    /// moisture; the cold band splits frostearth, bleachearth, shadowearth; floodearth lies
    /// along hot water; and the hot row's redearth beats the sand as floodearth
    /// does. The chart the audit draws (<c>ClimateChart</c>) is this function.
    /// </summary>
    internal static SurfaceMaterial Climate(byte warmth, byte moist, int near, byte rugged,
                                            float murkearthNoise, float muckearthNoise)
    {
        bool wet = moist >= WetFrom, dryGround = moist < DryBelow;
        if (warmth >= WarmFrom)
        {
            if (moist >= MuckearthFrom && near <= MuckearthReach && rugged <= MuckearthFlat
                && muckearthNoise > MuckearthBar)
                return SurfaceMaterial.Muckearth;
        }
        else if (moist >= MurkearthFrom && murkearthNoise > MurkearthBar) return SurfaceMaterial.Murkearth;

        if (warmth < FrigidBelow) return SurfaceMaterial.Frostearth;
        if (warmth < ColdBelow)
        {
            if (dryGround) return SurfaceMaterial.Frostearth;
            return wet ? SurfaceMaterial.Shadowearth : SurfaceMaterial.Bleachearth;
        }

        if (wet && near <= FloodearthReach && warmth >= FloodearthFrom) return SurfaceMaterial.Floodearth;
        if (warmth >= HotFrom)
        {
            if (moist >= HotWetFrom) return SurfaceMaterial.Redearth;
            if (warmth >= SandFrom) return SurfaceMaterial.Sand;
            return dryGround ? SurfaceMaterial.Dustearth : SurfaceMaterial.Yellowearth;
        }
        if (wet) return SurfaceMaterial.Brownearth;
        return dryGround ? SurfaceMaterial.Dryearth : SurfaceMaterial.Blackearth;
    }

    /// <summary>The warmth byte the open lowland reads at a warmth knob of 0 and of 1: what the chart brackets as the knob's own range.</summary>
    internal const int LowlandWarmthAt0 = 60, LowlandWarmthAt1 = 240;

    /// <summary>The band lines on the warmth axis, for the chart: name and byte.</summary>
    internal static readonly (string Name, int At)[] WarmthLines =
    {
        ("SNOW", SnowBelow), ("FRIGID", FrigidBelow), ("COLD", ColdBelow), ("MURKEARTH/MUCKEARTH", WarmFrom),
        ("HOT", HotFrom), ("SAND", SandFrom),
    };

    /// <summary>The band lines on the moisture axis, for the chart: name and byte.</summary>
    internal static readonly (string Name, int At)[] MoistureLines =
    {
        ("DRY", DryBelow), ("WET", WetFrom), ("MURKEARTH", MurkearthFrom), ("REDEARTH", HotWetFrom), ("MUCKEARTH", MuckearthFrom),
    };
}
