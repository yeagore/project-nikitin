using System;
using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// The magickal density layer: one byte per column, <see cref="IslandData.Magick"/>,
/// 0 inert … 255 saturated.
///
/// <para>The field is grown, not sampled. Two substances live on the land — a
/// <b>producer</b>, the magick itself, which is autocatalytic (where there is some,
/// more is made), and an <b>inhibitor</b>, the aether the producing consumes, which
/// is replenished everywhere and spreads faster than the producer does. That last
/// clause is Turing's condition, and it is the whole trick: a substance that makes
/// more of itself locally while starving its own surroundings at a distance cannot
/// settle into a flat field, and instead breaks into spots, worms, mazes, cells and
/// lace — the same instability that puts the spots on a leopard. It is the
/// Gray–Scott form of the reaction, integrated with explicit Euler on the cell
/// lattice, the coast a no-flux wall (the aether takes nothing away).</para>
///
/// <para>The reaction has six coefficients and <b>none of them is a knob</b>. The
/// settings that pattern at all are islands in a sea of dead and flooded ones, and
/// which island you are standing on decides the <em>kind</em> of thing the Domain
/// grows rather than its degree; sliding between two of them mostly passes through
/// country that grows nothing. So the interesting points are named instead —
/// <see cref="MagickPattern"/>, one <see cref="Recipe"/> apiece — and the layer
/// shows two parameters to the rest of the game: <b>which pattern</b>, and
/// <see cref="IslandParams.MagickDensity"/>, <b>how much magick</b> the Domain ends
/// up holding. Nothing reads the byte yet; what the Magicks system makes of it is
/// design to come.</para>
/// </summary>
internal static class Magicks
{
    // ---- the lattice --------------------------------------------------------

    /// <summary>
    /// The nine-point Laplacian's weights, the centre being −1. The five-point
    /// stencil makes the pattern grow along the grid axes, which reads as a
    /// woven texture rather than a natural one; the diagonals cost nothing here,
    /// since the neighbourhood is walked anyway.
    /// </summary>
    private const float Cardinal = 0.2f, Diagonal = 0.05f;

    // ---- the named points ---------------------------------------------------

    /// <summary>
    /// One place to stand on the Gray–Scott plane, and how long it takes to get
    /// there. <paramref name="Supply"/> is the feed F, the rate the inhibitor is
    /// replenished at. <paramref name="Removal"/> is k, the rate the producer is
    /// taken off at over the feed. <paramref name="Reach"/> is how far the density
    /// may walk k either side of that without the pattern changing kind — the live
    /// band round each point is a thousandth or two wide, so the reach is quoted per
    /// pattern; it is <b>signed</b>, because which way thickens the pattern is not
    /// the same at every feed, and at the low feed the motes sit at it is the other
    /// way about. <paramref name="Spread"/> is the inhibitor's diffusion Dᵤ, which
    /// sets the pattern's <em>scale</em>: how far apart two wells of magick can
    /// stand and still starve each other.
    /// </summary>
    private readonly record struct Recipe(float Supply, float Removal, float Reach, float Spread, int Steps);

    /// <summary>
    /// Where each pattern lives. The six were found by sweeping the plane and
    /// looking: F and k pick the kind, Dᵤ the coarseness, and the step count is what
    /// that regime needs to grow out from the sown patches and cover the island —
    /// the slower-growing kinds are given longer rather than left half-finished.
    /// </summary>
    private static Recipe RecipeFor(MagickPattern pattern) => pattern switch
    {
        MagickPattern.Motes     => new Recipe(0.018f, 0.0530f, -0.0007f, 0.14f, 3500),
        MagickPattern.Wells     => new Recipe(0.030f, 0.0610f,  0.0009f, 0.21f, 3500),
        MagickPattern.Veins     => new Recipe(0.030f, 0.0590f,  0.0008f, 0.17f, 3500),
        MagickPattern.Labyrinth => new Recipe(0.030f, 0.0570f,  0.0005f, 0.17f, 3000),
        MagickPattern.Lace      => new Recipe(0.024f, 0.0530f,  0.0006f, 0.11f, 3500),
        MagickPattern.Hollows   => new Recipe(0.030f, 0.0556f,  0.0005f, 0.17f, 3000),
        _                       => new Recipe(0.030f, 0.0590f,  0.0008f, 0.17f, 3500),
    };

    /// <summary>
    /// The producer's diffusion, as a fraction of the inhibitor's. Never 1: the
    /// inhibitor must outrun the producer or there is no Turing instability and the
    /// field settles flat, so this is a share and not a figure of its own.
    /// </summary>
    private const float ProducerShare = 0.5f;

    /// <summary>Scale on the autocatalytic term; 1 is the classical form, and every recipe is quoted against it.</summary>
    private const float Reproduction = 1f;

    // ---- how much magick ----------------------------------------------------

    /// <summary>
    /// What <see cref="IslandParams.MagickDensity"/> asks the island's mean magick
    /// to come to, as a share of the byte, at a density of 1; the ask runs down to a
    /// flat 0, and 0 means <em>none</em>. A Turing reaction cannot give you that on
    /// its own — the producer is always somewhere, and a pattern rescaled to its own
    /// range always fills the byte however little of it there was — so the emptying
    /// is done here, on the way out, and not asked of the reaction.
    /// </summary>
    private const float MeanFull = 0.72f;

    /// <summary>
    /// The strongest lift the level may apply, as the exponent of its power curve.
    /// Past this the curve starts drawing the difference between two nearly inert
    /// cells as if it were country.
    /// </summary>
    private const float LiftMost = 0.15f;

    /// <summary>
    /// The level's one parameter, over which the mean rises from nothing to
    /// everything: 0 is an empty island, 1 the pattern as the reaction left it, and
    /// <see cref="LevelMost"/> the hardest lift. See <see cref="Shape"/>.
    /// </summary>
    private const float LevelNone = 0f, LevelMost = 2f;

    /// <summary>Bins the level's search reads the field through, and how many halvings it takes.</summary>
    private const int LevelBins = 512, LevelSteps = 24;

    // ---- the seeding --------------------------------------------------------

    /// <summary>
    /// Where the producer starts: the cells above this far up a warped noise field's
    /// range, so roughly the top third of the island in a few broad irregular
    /// patches. A pattern grown from one blob takes far longer to reach the far
    /// coast than the settling allows, and one grown from single cells is a regular
    /// dotting; broad patches break up from their own edges inward and fill the
    /// island in the time given. The share is read against the island's own range
    /// rather than a fixed level, because a small landmass can sit entirely under a
    /// fixed one — nothing is sown, the reaction has nothing to run on, and the
    /// Domain falls back to the seeding field for want of a single seeded cell.
    /// </summary>
    private const float SeedShare = 0.55f, SeedProducer = 0.25f, SeedInhibitor = 0.5f;

    /// <summary>The seeding field: soft, island-scale patches, at about forty cells a wave.</summary>
    private const float SeedWavelength = 0.024f;

    /// <summary>How far the seeding's crests are bent, in cells, and the frequency of the bending.</summary>
    private const float WarpAmplitude = 14f, WarpFrequency = 0.02f;

    /// <summary>
    /// What the producer must have come to for the field to be worth reading as a
    /// pattern: a peak this high, and a range this wide under it. A settled pattern
    /// peaks around 0.3 with inert ground at 0 beneath it. Both tests are needed and
    /// both catch a real failure — a reaction that died leaves no peak, and one that
    /// flooded leaves a high peak with no range under it, whose residue spread over
    /// the byte would be numerical noise drawn as if it were country. Either way the
    /// layer falls back to the seeding field, so the byte is never flat.
    /// </summary>
    private const float LivePeak = 0.05f, LiveRange = 0.02f;

    /// <summary>Contrast of the fallback field about its middle, through a tanh so the ends saturate softly.</summary>
    private const float FallbackStretch = 3.2f;

    /// <summary>
    /// Fills <see cref="IslandData.Magick"/> over the land; nothing else is read
    /// and nothing else is written.
    /// </summary>
    public static void Measure(int seed, IslandParams p, IslandData d)
    {
        int n = d.Size;
        var seeding = new Noise(seed + 71_041, SeedWavelength, octaves: 2, gain: 0.35f)
            .WithWarp(WarpAmplitude, WarpFrequency);

        // The land as a flat list, each cell carrying the eight neighbours it
        // exchanges with. A neighbour off the land is the cell itself, which is a
        // no-flux wall: neither substance crosses the coast into the aether.
        var slot = new int[n * n];
        Array.Fill(slot, -1);
        int cells = 0;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (d.HasLand(x, z)) slot[x * n + z] = cells++;
        if (cells == 0) return;

        var at = new int[cells];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (slot[x * n + z] >= 0) at[slot[x * n + z]] = x * n + z;

        var near = new int[cells * 8];
        for (int i = 0; i < cells; i++)
        {
            int x = at[i] / n, z = at[i] % n;
            for (int k = 0; k < 8; k++)
            {
                int ax = x + Grid.Dx8[k], az = z + Grid.Dz8[k];
                int j = ax >= 0 && ax < n && az >= 0 && az < n ? slot[ax * n + az] : -1;
                near[i * 8 + k] = j < 0 ? i : j;
            }
        }

        // Inhibitor everywhere, producer in the seeded patches only.
        var sowing = new float[cells];
        float sowLo = float.MaxValue, sowHi = float.MinValue;
        for (int i = 0; i < cells; i++)
        {
            sowing[i] = seeding.At(at[i] / n, at[i] % n);
            sowLo = MathF.Min(sowLo, sowing[i]);
            sowHi = MathF.Max(sowHi, sowing[i]);
        }
        float mark = sowLo + (sowHi - sowLo) * SeedShare;

        var u = new float[cells];
        var v = new float[cells];
        for (int i = 0; i < cells; i++)
        {
            bool sown = sowing[i] > mark;
            u[i] = sown ? SeedInhibitor : 1f;
            v[i] = sown ? SeedProducer : 0f;
        }

        float density = Mathf.Clamp(p.MagickDensity, 0f, 1f);
        Recipe how = RecipeFor(p.MagickPattern);

        // The one thing the density does inside the reaction: walk the removal rate
        // along the pattern's own band, so that a denser Domain grows a thicker
        // pattern and not merely a brighter one. The reach is small enough that the
        // pattern thickens rather than turning into the next one along.
        float removal = how.Removal + how.Reach * (1f - 2f * density);
        float spreadU = how.Spread;
        (u, v) = React(u, v, near, cells, how.Steps,
                       spreadU, spreadU * ProducerShare, how.Supply, removal);

        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < cells; i++)
        {
            lo = MathF.Min(lo, v[i]);
            hi = MathF.Max(hi, v[i]);
        }

        // The producer never uses more than a third of 0–1, and how much it uses
        // depends on the recipe; stretched to the island's own range, the field
        // reads as the pattern rather than as the settings. A dead or flooded
        // reaction leaves the seeding field itself, tanh-stretched: soft waves,
        // with nothing behind them, rather than a flat byte.
        var field = new float[cells];
        if (hi < LivePeak || hi - lo < LiveRange)
            for (int i = 0; i < cells; i++)
                field[i] = 0.5f + 0.5f * MathF.Tanh((sowing[i] - 0.5f) * FallbackStretch);
        else
            for (int i = 0; i < cells; i++)
                field[i] = (v[i] - lo) / (hi - lo);

        Paint(field, at, n, d, density);
    }

    /// <summary>
    /// The reaction itself, stepped with explicit Euler at dt = 1. The producer
    /// eats the inhibitor autocatalytically (<c>u v²</c>: it takes two of itself to
    /// make a third), the inhibitor is fed back toward 1 everywhere, and the
    /// producer is taken off at <paramref name="removal"/> over the feed. Both are
    /// held in 0–1: the clamp never bites at a named recipe, and stops a density
    /// pushed to its edge from running away into infinities.
    ///
    /// <para>Each step reads one pair of fields and writes the other, so the two
    /// are swapped rather than copied; which pair the answer ends up in depends on
    /// the parity of <paramref name="steps"/>, and is returned rather than guessed at.</para>
    /// </summary>
    private static (float[] U, float[] V) React(
        float[] u, float[] v, int[] near, int cells, int steps,
        float spreadU, float spreadV, float supply, float removal)
    {
        var nextU = new float[cells];
        var nextV = new float[cells];
        float loss = supply + removal;

        for (int step = 0; step < steps; step++)
        {
            for (int i = 0; i < cells; i++)
            {
                int b = i * 8;
                // Grid.Dx8 alternates cardinal and diagonal from its first entry.
                int e = near[b], se = near[b + 1], s = near[b + 2], sw = near[b + 3];
                int w = near[b + 4], nw = near[b + 5], nn = near[b + 6], ne = near[b + 7];

                float ui = u[i], vi = v[i];
                float lapU = (u[e] + u[s] + u[w] + u[nn]) * Cardinal
                           + (u[se] + u[sw] + u[nw] + u[ne]) * Diagonal - ui;
                float lapV = (v[e] + v[s] + v[w] + v[nn]) * Cardinal
                           + (v[se] + v[sw] + v[nw] + v[ne]) * Diagonal - vi;

                float reacted = Reproduction * ui * vi * vi;
                nextU[i] = Math.Clamp(ui + spreadU * lapU - reacted + supply * (1f - ui), 0f, 1f);
                nextV[i] = Math.Clamp(vi + spreadV * lapV + reacted - loss * vi, 0f, 1f);
            }

            (u, nextU) = (nextU, u);
            (v, nextV) = (nextV, v);
        }

        return (u, v);
    }

    /// <summary>
    /// The settled field, already stretched to 0–1, written out as the byte at the
    /// level the density asked for. A Domain of scattered wells and one of hollows
    /// draw their pattern at wildly different means, and the density is a promise
    /// about how much magick the place holds, so the field is put through
    /// <see cref="Shape"/> until its mean lands on <c>density × MeanFull</c>.
    /// Halving finds the level through a histogram rather than through the cells, so
    /// the search costs bins and not land.
    ///
    /// <para>The two ends are the point of the shape. Below 1 it <b>cuts</b>, and a
    /// cut is the only thing that makes a real zero: the reaction always leaves
    /// producer somewhere, and stretching what it left to the island's own range
    /// fills the byte however faint the pattern was, so an empty Domain has to be
    /// made by subtracting a level and not by asking the reaction for less. At a
    /// density of 0 the cut takes everything and the island is inert; just above it,
    /// only the crowns of the strongest wells stand above the cut, which reads as a
    /// handful of small bright places on dead ground rather than as a dim wash.
    /// Above 1 it lifts instead, which is what a Domain steeped in magick wants.</para>
    /// </summary>
    private static void Paint(float[] field, int[] at, int n, IslandData d, float density)
    {
        float target = MeanFull * density;
        if (target <= 0f) return;       // Magick starts zeroed: an inert Domain is already written.

        int cells = field.Length;
        var bins = new int[LevelBins];
        foreach (float t in field)
            bins[Math.Clamp((int)(t * LevelBins), 0, LevelBins - 1)]++;

        float lo = LevelNone, hi = LevelMost;
        for (int step = 0; step < LevelSteps; step++)
        {
            float mid = 0.5f * (lo + hi);
            double sum = 0;
            for (int b = 0; b < LevelBins; b++)
                if (bins[b] > 0) sum += bins[b] * Shape((b + 0.5f) / LevelBins, mid);
            // The mean rises with the level, so a mean under the target wants more.
            if (sum / cells < target) lo = mid; else hi = mid;
        }

        float level = 0.5f * (lo + hi);
        for (int i = 0; i < cells; i++)
            d.Magick[at[i] / n, at[i] % n] =
                (byte)Mathf.Clamp(Mathf.RoundToInt(Shape(field[i], level) * 255f), 0, 255);
    }

    /// <summary>
    /// The level curve: one parameter, monotone in both <c>t</c> and the level, so
    /// the pattern is emptied, thinned or fattened and never rearranged — nowhere
    /// becomes more magickal than a place that outranked it.
    ///
    /// <para>Under 1 the level is a <b>cut</b>: everything below <c>1 − level</c>
    /// goes to nothing and what is left is stretched back over the byte, so 0 is an
    /// inert island and a small level leaves only the tips of the pattern. Over 1 it
    /// is a <b>lift</b>, the power curve <c>t^g</c> with g falling from 1 to
    /// <see cref="LiftMost"/>. The two meet at 1, where the curve is the identity
    /// and the byte is the pattern as the reaction left it.</para>
    /// </summary>
    private static float Shape(float t, float level)
    {
        if (level >= 1f)
            return MathF.Pow(t, 1f / (1f + (level - 1f) * (1f / LiftMost - 1f)));
        return level <= 0f ? 0f : Math.Clamp((t - (1f - level)) / level, 0f, 1f);
    }
}
