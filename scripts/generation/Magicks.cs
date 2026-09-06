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
/// <para>The lattice is not even-handed. Each cell has a <b>leaning</b> — uphill,
/// upwind, and up the watercourse — and the two substances take it opposite ways:
/// the magick climbs it and the aether it feeds on runs down it. So the pattern is
/// the same pattern everywhere, but there is more of it on the tops, the weather
/// side and the headwaters, and less toward the coast, the lee and the mouth. This
/// is the one thing in the stage that reads the land it sits on.</para>
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

    // ---- which way the land leans -------------------------------------------
    // The lattice above is even-handed: a substance spreads as readily one way as
    // another, and the pattern owes nothing to the ground it sits on. It does now.
    // Each cell has a leaning — uphill, upwind, and up the watercourse — and the
    // two substances take it opposite ways. The magick climbs it: it spreads more
    // readily toward high ground, into the wind and toward the headwaters, so it
    // gathers on the tops and the weather side and thins toward the coast. The
    // aether it feeds on runs the other way, downhill and downwind and down the
    // rivers, which pulls the starving with it and sharpens the same slope.

    /// <summary>
    /// How far the neighbourhood may lean, as a share of each neighbour's weight.
    /// The weights are renormalised after leaning, so the stencil stays a weighted
    /// average and the scheme stays as stable as the even-handed one; at 1 a cell
    /// directly downhill would contribute nothing at all, which is a wall and not a
    /// slope, so the lean is kept well under it.
    /// </summary>
    private const float Lean = 0.12f;

    /// <summary>
    /// What the leaning is made of, before it is capped at a unit vector: the fall
    /// of the ground, the one wind, and the watercourse. The ground is the strongest
    /// where there is any, but most land is flat enough that the wind is what a cell
    /// actually leans on; the watercourse speaks only on the channel itself, and
    /// speaks loudly there because it is one cell wide with the whole neighbourhood
    /// diffusing against it.
    ///
    /// <para>The wind is the quietest of the three, for a reason the audit's lean
    /// table made plain. The slope and the channel point every which way across an
    /// island and cancel in the large; the wind is one direction over the whole
    /// Domain, so it does not merely tilt the pattern, it carries the field downwind
    /// until it banks against the far coast. At half it left a Domain's windward side
    /// sixty bytes richer than its lee, and undid the slope's own gathering with it.
    /// It leans the pattern now rather than sweeping it.</para>
    /// </summary>
    private const float SlopeLean = 1f, WindLean = 0.3f, StreamLean = 0.9f;

    /// <summary>
    /// The fall, in slabs across one cell, at which the ground's say is full. Under
    /// it the slope leans in proportion, so flat country is led by the wind and a
    /// mountainside by its own fall rather than both being pushed equally hard.
    /// </summary>
    private const float SlopeFull = 2f;

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
        var (toward, against) = Stencils(d, at, near, cells, n);
        (u, v) = React(u, v, near, toward, against, cells, how.Steps,
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
        float[] u, float[] v, int[] near, float[] toward, float[] against, int cells, int steps,
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
                float ui = u[i], vi = v[i];
                float sumU = 0f, sumV = 0f;
                for (int k = 0; k < 8; k++)
                {
                    int j = near[b + k];
                    sumU += against[b + k] * u[j];
                    sumV += toward[b + k] * v[j];
                }

                float lapU = sumU - ui;
                float lapV = sumV - vi;

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
    /// The two leaning stencils, one per substance, eight weights a cell. Each is the
    /// even-handed nine-point stencil with every neighbour's weight scaled by how far
    /// that way lies with the cell's leaning (<see cref="Leaning"/>) or against it,
    /// then renormalised so the eight still sum to one. Renormalising is what keeps
    /// this a weighted average of the neighbourhood rather than a source or a drain:
    /// the substance is carried, not made, and explicit Euler stays as stable as it
    /// was even-handed.
    ///
    /// <para><paramref name="toward"/> is the magick's, leaning the way the cell
    /// leans; <paramref name="against"/> is the aether's, leaning the other way. They
    /// are built once and read every step, which is why they are stencils and not a
    /// dot product in the inner loop.</para>
    /// </summary>
    private static (float[] Toward, float[] Against) Stencils(
        IslandData d, int[] at, int[] near, int cells, int n)
    {
        var toward = new float[cells * 8];
        var against = new float[cells * 8];

        // The unit direction of each neighbour, in Grid.Dx8 order; the diagonals are
        // a step of root two, so they are shortened to unit length before any angle
        // is taken off them, or a corner would count as more of a direction.
        var dirX = new float[8];
        var dirZ = new float[8];
        for (int k = 0; k < 8; k++)
        {
            float len = MathF.Sqrt(Grid.Dx8[k] * Grid.Dx8[k] + Grid.Dz8[k] * Grid.Dz8[k]);
            dirX[k] = Grid.Dx8[k] / len;
            dirZ[k] = Grid.Dz8[k] / len;
        }

        for (int i = 0; i < cells; i++)
        {
            var (leanX, leanZ) = Leaning(d, at[i] / n, at[i] % n);
            int b = i * 8;
            float sumT = 0f, sumA = 0f;
            for (int k = 0; k < 8; k++)
            {
                float weight = (k & 1) == 0 ? Cardinal : Diagonal;
                float with = dirX[k] * leanX + dirZ[k] * leanZ;   // −1 against … 1 with
                // The sign here is the opposite of the one it looks like it should be,
                // and the audit's lean table is what caught it. A cell takes from its
                // neighbours, so weighting the uphill neighbour heavier makes the cell
                // draw magick *down* off the hill. To carry a substance up the leaning,
                // the cell must draw it from the low side — so the stencil that climbs
                // is the one that leans away.
                toward[b + k] = weight * (1f - Lean * with);
                against[b + k] = weight * (1f + Lean * with);
                sumT += toward[b + k];
                sumA += against[b + k];
            }
            for (int k = 0; k < 8; k++)
            {
                toward[b + k] /= sumT;
                against[b + k] /= sumA;
            }
        }

        return (toward, against);
    }

    /// <summary>
    /// Which way one cell leans, as a vector no longer than a unit: uphill by the
    /// fall of the effective surface, upwind against the Domain's one wind, and
    /// upstream along a watercourse. The three are added and then capped rather than
    /// normalised, so a flat, sheltered cell away from any water leans hardly at all
    /// and its neighbourhood stays even-handed, which is the honest answer for ground
    /// with nothing to say.
    ///
    /// <para>Upstream is read off the drainage accumulation, which rises down a
    /// channel: the neighbour on the watercourse carrying the most is downstream, so
    /// the way to the headwaters is away from it. The slope alone would nearly say
    /// this — water runs downhill — but a navigable reach is a stair of pools whose
    /// surface is flat for cells at a time, and that is exactly where the channel
    /// still has a direction and the ground has none.</para>
    /// </summary>
    private static (float X, float Z) Leaning(IslandData d, int x, int z)
    {
        int n = d.Size;
        short here = d.EffectiveLevel(x, z);
        float leanX = 0f, leanZ = 0f;

        // Uphill: the fall across the four cardinal neighbours, as a central
        // difference where both sides are land and a one-sided one at the coast.
        float FallAlong(int dx, int dz)
        {
            int ax = x + dx, az = z + dz, bx = x - dx, bz = z - dz;
            bool aheadOn = ax >= 0 && ax < n && az >= 0 && az < n && d.HasLand(ax, az);
            bool behindOn = bx >= 0 && bx < n && bz >= 0 && bz < n && d.HasLand(bx, bz);
            float ahead = aheadOn ? d.EffectiveLevel(ax, az) : here;
            float behind = behindOn ? d.EffectiveLevel(bx, bz) : here;
            return aheadOn && behindOn ? (ahead - behind) * 0.5f : ahead - behind;
        }

        float slopeX = FallAlong(1, 0), slopeZ = FallAlong(0, 1);
        float fall = MathF.Sqrt(slopeX * slopeX + slopeZ * slopeZ);
        if (fall > 0.0001f)
        {
            // The gradient points uphill already, which is the way the magick goes.
            float say = SlopeLean * MathF.Min(1f, fall / SlopeFull) / fall;
            leanX += slopeX * say;
            leanZ += slopeZ * say;
        }

        // Upwind: the wind blows along DuneGrain, so into it is the other way.
        int grain = d.DuneGrain & 7;
        float windLen = MathF.Sqrt(Grid.Dx8[grain] * Grid.Dx8[grain] + Grid.Dz8[grain] * Grid.Dz8[grain]);
        leanX -= WindLean * Grid.Dx8[grain] / windLen;
        leanZ -= WindLean * Grid.Dz8[grain] / windLen;

        // Upstream: away from the neighbour on the watercourse carrying the most.
        if (d.River[x, z])
        {
            int downstream = -1;
            int most = d.Flow[x, z];
            for (int k = 0; k < 8; k++)
            {
                int ax = x + Grid.Dx8[k], az = z + Grid.Dz8[k];
                if (ax < 0 || ax >= n || az < 0 || az >= n) continue;
                if (!d.River[ax, az] || d.Flow[ax, az] <= most) continue;
                most = d.Flow[ax, az];
                downstream = k;
            }
            if (downstream >= 0)
            {
                float len = MathF.Sqrt(Grid.Dx8[downstream] * Grid.Dx8[downstream]
                                     + Grid.Dz8[downstream] * Grid.Dz8[downstream]);
                leanX -= StreamLean * Grid.Dx8[downstream] / len;
                leanZ -= StreamLean * Grid.Dz8[downstream] / len;
            }
        }

        float length = MathF.Sqrt(leanX * leanX + leanZ * leanZ);
        if (length > 1f) { leanX /= length; leanZ /= length; }
        return (leanX, leanZ);
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
