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
/// <para>Six knobs on <see cref="IslandParams"/> steer it, all Auto-able, and the
/// interesting thing about the model is that they do not steer it smoothly: the
/// pattern <em>kind</em> changes across the parameter plane, so consecutive seeds
/// give a Domain of scattered magickal wells, one veined with filaments, or one
/// almost saturated with inert holes punched through it. Nothing reads the byte
/// yet; what the Magicks system makes of it is design to come.</para>
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

    // ---- what the knobs map onto --------------------------------------------
    // The live band of the Gray-Scott plane is narrow and oddly shaped, and most
    // of the rectangle around it is a dead field (the producer dies out) or a
    // full one (it fills everything). The knobs are therefore mapped onto the
    // band rather than onto the raw coefficients, so that every Domain patterns.

    /// <summary>How fast the inhibitor is replenished: Gray–Scott's feed rate F.</summary>
    private const float SupplyLow = 0.014f, SupplyHigh = 0.060f;

    /// <summary>
    /// The producer's removal rate k is set as a multiple of the saddle-node curve
    /// <c>√(ρF) / 2 − F</c> — the line under which the reaction has a second, live
    /// steady state, and which the reproduction rate ρ moves, so a slower
    /// reproduction lowers it with itself. The pattern-forming band straddles that
    /// line narrowly: well under it the producer floods the whole island, well over
    /// it the producer dies out, and everything worth looking at is within a few
    /// per cent either side. Expressing k as a multiple of the curve rather than
    /// absolutely is what lets the supply knob range over the whole feed axis
    /// without walking out of the band.
    /// </summary>
    private const float DecayCentre = 1.0f;

    /// <summary>
    /// How far either side of <see cref="DecayCentre"/> the decay knob reaches, at
    /// no supply and at full supply. The band is not a fixed width: the faster the
    /// inhibitor is fed the narrower the live band round the curve gets, and a
    /// width that suits a starved Domain kills a well-fed one outright.
    /// </summary>
    private const float DecayReachLow = 0.075f, DecayReachHigh = 0.035f;

    /// <summary>The inhibitor's diffusion. Bounded so that explicit Euler at dt = 1 stays stable.</summary>
    private const float InhibitorLow = 0.14f, InhibitorHigh = 0.21f;

    /// <summary>
    /// The producer's diffusion, as a fraction of the inhibitor's. Never 1: the
    /// inhibitor must outrun the producer or there is no Turing instability and
    /// the field settles flat, so this is expressed relative rather than absolute.
    /// </summary>
    private const float ProducerLow = 0.40f, ProducerHigh = 0.55f;

    /// <summary>Scale on the autocatalytic term. Kept near 1, which is the classical form.</summary>
    private const float ReproductionLow = 0.85f, ReproductionHigh = 1.20f;

    /// <summary>Steps of the reaction. Too few and the seeding still shows through.</summary>
    private const int SettleLow = 400, SettleHigh = 2600;

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
    /// What the producer must have come to for the field to be worth stretching: a
    /// peak this high, and a range this wide under it. A settled pattern peaks
    /// around 0.3 with inert ground at 0 beneath it. Both tests are needed and both
    /// catch a real failure — a reaction that died leaves no peak, and one that
    /// flooded leaves a high peak with no range under it, whose residue stretched
    /// over the byte would be numerical noise drawn as if it were country. Either
    /// way the layer falls back to the seeding field, so the byte is never flat.
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

        float supply = Lerp(SupplyLow, SupplyHigh, p.MagickSupply);
        float reproduction = Lerp(ReproductionLow, ReproductionHigh, p.MagickReproduction);
        float ceiling = MathF.Sqrt(reproduction * supply) * 0.5f - supply;
        float reach = Lerp(DecayReachLow, DecayReachHigh, p.MagickSupply);
        float decay = ceiling * (DecayCentre + reach * (2f * Mathf.Clamp(p.MagickDecay, 0f, 1f) - 1f));
        float spreadU = Lerp(InhibitorLow, InhibitorHigh, p.MagickInhibitorSpread);
        float spreadV = spreadU * Lerp(ProducerLow, ProducerHigh, p.MagickProducerSpread);
        int steps = SettleLow
                  + Mathf.RoundToInt((SettleHigh - SettleLow) * Mathf.Clamp(p.MagickSettling, 0f, 1f));

        (u, v) = React(u, v, near, cells, steps, spreadU, spreadV, reproduction, supply, decay);

        // The producer rarely uses more than a third of 0–1, and how much it uses
        // depends on the knobs; stretched to the island's own range, the byte reads
        // as the pattern rather than as the settings.
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < cells; i++)
        {
            lo = MathF.Min(lo, v[i]);
            hi = MathF.Max(hi, v[i]);
        }
        if (hi < LivePeak || hi - lo < LiveRange)
        {
            Waves(seeding, d);
            return;
        }

        float span = hi - lo;
        for (int i = 0; i < cells; i++)
            d.Magick[at[i] / n, at[i] % n] =
                (byte)Mathf.Clamp(Mathf.RoundToInt((v[i] - lo) / span * 255f), 0, 255);
    }

    /// <summary>
    /// The reaction itself, stepped with explicit Euler at dt = 1. The producer
    /// eats the inhibitor autocatalytically (<c>u v²</c>: it takes two of itself to
    /// make a third), the inhibitor is fed back toward 1 everywhere, and the
    /// producer is removed at <paramref name="decay"/> over the feed. Both are held
    /// in 0–1: the clamp never bites in the live band, and stops a knob pushed to
    /// its edge from running away into infinities.
    ///
    /// <para>Each step reads one pair of fields and writes the other, so the two
    /// are swapped rather than copied; which pair the answer ends up in depends on
    /// the parity of <paramref name="steps"/>, and is returned rather than guessed at.</para>
    /// </summary>
    private static (float[] U, float[] V) React(
        float[] u, float[] v, int[] near, int cells, int steps,
        float spreadU, float spreadV, float reproduction, float supply, float decay)
    {
        var nextU = new float[cells];
        var nextV = new float[cells];
        float removal = supply + decay;

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

                float reacted = reproduction * ui * vi * vi;
                nextU[i] = Math.Clamp(ui + spreadU * lapU - reacted + supply * (1f - ui), 0f, 1f);
                nextV[i] = Math.Clamp(vi + spreadV * lapV + reacted - removal * vi, 0f, 1f);
            }

            (u, nextU) = (nextU, u);
            (v, nextV) = (nextV, v);
        }

        return (u, v);
    }

    /// <summary>
    /// What the layer was before the reaction, and what it falls back to when the
    /// reaction leaves no contrast: the seeding field itself, stretched about its
    /// middle so the byte uses most of its range. Soft waves, with nothing behind them.
    /// </summary>
    private static void Waves(Noise seeding, IslandData d)
    {
        int n = d.Size;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            float value = 0.5f + 0.5f * MathF.Tanh((seeding.At(x, z) - 0.5f) * FallbackStretch);
            d.Magick[x, z] = (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
        }
    }

    private static float Lerp(float lo, float hi, float knob) => lo + (hi - lo) * Mathf.Clamp(knob, 0f, 1f);
}
