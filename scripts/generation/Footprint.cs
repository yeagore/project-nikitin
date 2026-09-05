using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>The land mask: lobes laid out per arrangement, rasterised, then bitten.</summary>
internal static class Footprint
{
    /// <summary>Turns around the circumference sampled for coastline lobes.</summary>
    private const float LobeRings = 1.7f;

    /// <summary>
    /// Narrowest a strait may pinch to, in cells. Just over one: a crack may be a
    /// single step across but never heals shut, or the arrangement quietly delivers
    /// fewer landmasses than it promised.
    /// </summary>
    private const float StraitNarrowest = 1.05f;

    /// <summary>
    /// One blob of the footprint: an ellipse with a wandering radius. The ellipse is
    /// squashed along its rotation, so a lobe meant to lie along a tangent is rotated
    /// to the radial direction.
    /// </summary>
    private readonly struct Lobe
    {
        public readonly float Cx, Cz, Radius, Aspect, Cos, Sin;
        public readonly float Rings;      // how many wobbles go round its coast

        /// <summary>Share of Irregularity the radius may wander by.</summary>
        public readonly float Wander;

        /// <summary>
        /// Piece of the arrangement this lobe belongs to: -1 is a piece of its own;
        /// lobes sharing a group >= 0 are one piece and the seam between them is
        /// never cut.
        /// </summary>
        public readonly int Group;

        public Lobe(float cx, float cz, float radius, float aspect, float rot, float rings,
                    float wander, int group = -1)
        {
            Cx = cx;
            Cz = cz;
            Radius = radius;
            Aspect = aspect;
            Cos = MathF.Cos(rot);
            Sin = MathF.Sin(rot);
            Rings = rings;
            Wander = wander;
            Group = group;
        }

        /// <summary>The same lobe somewhere else at another size — the fit pass's move.</summary>
        public Lobe(Lobe from, float cx, float cz, float radius)
        {
            Cx = cx;
            Cz = cz;
            Radius = radius;
            Aspect = from.Aspect;
            Cos = from.Cos;
            Sin = from.Sin;
            Rings = from.Rings;
            Wander = from.Wander;
            Group = from.Group;
        }

        /// <summary>The same lobe as a member of <paramref name="group"/>.</summary>
        public Lobe InGroup(int group)
            => new(Cx, Cz, Radius, Aspect, MathF.Atan2(Sin, Cos), Rings, Wander, group);

        /// <summary>
        /// Normalised distance to the wandering edge; <paramref name="rEff"/> is the
        /// radius it was normalised by, in cells, which turns a seam back into a width.
        /// </summary>
        public float Distance(float x, float z, Noise lobes, float irr, out float rEff)
        {
            float dx = x - Cx, dz = z - Cz;
            float rx = (dx * Cos + dz * Sin) * Aspect;
            float rz = (-dx * Sin + dz * Cos) / Aspect;
            float dist = MathF.Sqrt(rx * rx + rz * rz);

            // Sampled on the unit circle so the noise is seamless in angle; offset
            // per lobe so two islets never share a coastline.
            float ang = MathF.Atan2(rz, rx);
            float lobe = lobes.At(MathF.Cos(ang) * Rings + Cx, MathF.Sin(ang) * Rings + Cz);
            rEff = MathF.Max(1e-3f, Radius * (1f + irr * Wander * (lobe * 2f - 1f)));
            return dist / rEff;
        }
    }

    /// <summary>
    /// A footprint's blobs and what to do where two meet. Straits are decided by the
    /// arrangement, not the geometry: the same ring of blobs is a Ring with its seams
    /// fused and a BrokenRing with them cut.
    /// </summary>
    private readonly struct Layout
    {
        public readonly Lobe[] Lobes;

        /// <summary>Radius of water cleared round <see cref="LagoonX"/>, <see cref="LagoonZ"/>, or 0 for none.</summary>
        public readonly float Lagoon;

        /// <summary>Where the lagoon is cleared: the centre for a ring, off it for a block's hole.</summary>
        public readonly float LagoonX, LagoonZ;

        /// <summary>A pinch carved whatever the lobes say, or null for none.</summary>
        public readonly Waist? Waist;

        /// <summary>Whether the seam between two blobs is carved into a strait.</summary>
        public readonly bool Straits;

        /// <summary>Widest that strait may open, in cells; 0 takes the Domain's bridge span.</summary>
        public readonly float StraitWide;

        /// <summary>
        /// A floor under <see cref="IslandParams.Coverage"/>, or 0 to take it as
        /// authored. Coverage is applied per blob, so a thin continuous shape needs a
        /// floor or it perforates into islets.
        /// </summary>
        public readonly float Solid;

        public Layout(Lobe[] lobes, float lagoon, float lagoonX, float lagoonZ, Waist? waist,
                      bool straits, float straitWide = 0f, float solid = 0f)
        {
            Lobes = lobes;
            Lagoon = lagoon;
            LagoonX = lagoonX;
            LagoonZ = lagoonZ;
            Waist = waist;
            Straits = straits;
            StraitWide = straitWide;
            Solid = solid;
        }
    }

    /// <summary>
    /// A neck cut to a width: two bays cleared either side of an axis, each a wedge that
    /// is <see cref="HalfWidth"/> from the axis at the middle and flares to
    /// <see cref="HalfWidth"/> + <see cref="Flare"/> at <see cref="HalfLength"/> along
    /// it. Carved outright, so the neck is a neck however the heads bulge.
    /// </summary>
    private readonly struct Waist
    {
        public readonly float Cx, Cz, Cos, Sin, HalfLength, HalfWidth, Flare;

        public Waist(float cx, float cz, float angle, float halfLength, float halfWidth, float flare)
        {
            Cx = cx;
            Cz = cz;
            Cos = MathF.Cos(angle);
            Sin = MathF.Sin(angle);
            HalfLength = halfLength;
            HalfWidth = halfWidth;
            Flare = flare;
        }

        /// <summary>The same waist scaled about a centre, as the fit pass scales the lobes.</summary>
        public Waist Scaled(float cx, float cz, float scale)
            => new(cx + (Cx - cx) * scale, cz + (Cz - cz) * scale, MathF.Atan2(Sin, Cos),
                   HalfLength * scale, HalfWidth * scale, Flare * scale);

        /// <summary>Whether a cell is in one of the two bays; the neck's edge wanders on <paramref name="wobble"/>.</summary>
        public bool Cuts(float x, float z, Noise wobble)
        {
            float dx = x - Cx, dz = z - Cz;
            float signedAlong = dx * Cos + dz * Sin;
            float along = MathF.Abs(signedAlong);
            float across = MathF.Abs(-dx * Sin + dz * Cos);
            if (along >= HalfLength) return false;
            float t = along / HalfLength;
            float edge = HalfWidth * (0.75f + 0.5f * wobble.At(signedAlong * 0.13f + 31f, 17f))
                         + Flare * t * t;
            return across > edge;
        }
    }

    /// <summary>
    /// What an arrangement is beyond where its blobs go: whether seams are cut and how
    /// wide, the coverage floor, and how many landmasses it must deliver to be itself.
    /// </summary>
    private readonly record struct ArrangementTraits(bool Straits, float StraitWide, float Solid,
                                                     int Masses);

    /// <summary>
    /// The trait table. Shapes (Ring, Cross, Fractal, the blocks) fuse their seams and
    /// count as one mass; scatters cut them. Harmony's commas overlap so deeply that a
    /// default-width strait heals shut, hence its 5.4; Caldera's moat is wide for the
    /// same reason and so that it reads as a moat.
    /// </summary>
    private static ArrangementTraits Traits(IslandArrangement how) => how switch
    {
        IslandArrangement.Single => new(false, 0f, 0f, 1),
        IslandArrangement.Satellites => new(true, 0f, 0f, 3),
        IslandArrangement.Twins => new(true, 0f, 0f, 2),
        IslandArrangement.Triplets => new(true, 0f, 0f, 3),
        IslandArrangement.Archipelago => new(true, 0f, 0f, 4),
        IslandArrangement.Ring => new(false, 0f, 0f, 1),
        IslandArrangement.BrokenRing => new(true, 0f, 0f, 4),
        IslandArrangement.Arc => new(false, 0f, 0f, 1),
        IslandArrangement.BrokenArc => new(true, 0f, 0f, 3),
        IslandArrangement.Atoll => new(true, 1.7f, 0f, 5),
        IslandArrangement.ThousandIsles => new(true, 0f, 0f, 8),
        IslandArrangement.Cross => new(false, 0f, 0f, 1),
        IslandArrangement.TShape => new(false, 0f, 0f, 1),
        IslandArrangement.LShape => new(false, 0f, 0f, 1),
        IslandArrangement.BrokenCross => new(true, 0f, 0f, 4),
        IslandArrangement.BrokenT => new(true, 0f, 0f, 3),
        IslandArrangement.BrokenL => new(true, 0f, 0f, 2),
        IslandArrangement.Fractal => new(false, 0f, 0.86f, 1),
        IslandArrangement.Caldera => new(true, 4.2f, 0f, 2),
        IslandArrangement.Rosette => new(false, 0f, 0f, 1),
        IslandArrangement.Star => new(false, 0f, 0f, 1),
        IslandArrangement.Shards => new(true, 1.9f, 0f, 4),
        IslandArrangement.Square => new(false, 0f, 0.85f, 1),
        IslandArrangement.Rhomb => new(false, 0f, 0.85f, 1),
        IslandArrangement.NShape => new(false, 0f, 0.86f, 1),
        IslandArrangement.Quarters => new(true, 0f, 0f, 4),
        IslandArrangement.Halves => new(true, 0f, 0f, 2),
        IslandArrangement.Harmony => new(true, 5.4f, 0.82f, 2),
        IslandArrangement.Isthmus => new(false, 0f, 0.84f, 1),
        IslandArrangement.Reef => new(true, 0f, 0.8f, 3),
        _ => new(true, 0f, 0f, 1),
    };

    /// <summary>How many separate landmasses an arrangement has to deliver to be that arrangement.</summary>
    private static int MassesWanted(IslandArrangement how) => Traits(how).Masses;

    /// <summary>
    /// Keeps a lobe's centre a margin inside the grid so a later nudge cannot push it
    /// into the wall: the radius plus three cells, whatever the lobe's shape. The pad
    /// is capped at half the footprint: past that Math.Clamp's minimum would exceed
    /// its maximum and throw. This is the pad every layout was tuned against, so it
    /// stays the pad at placement; the fit pass has its own (below).
    /// </summary>
    private static (float x, float z) ClampIntoFootprint(int n, float x, float z, float r)
    {
        float pad = Math.Min(r + 3f, (n - 1) * 0.5f);
        return (Math.Clamp(x, pad, n - 1 - pad), Math.Clamp(z, pad, n - 1 - pad));
    }

    /// <summary>
    /// The fit pass's clamp: on each axis the pad is the lobe's own reach — an ellipse
    /// of <paramref name="r"/> / aspect along its axis and <paramref name="r"/> × aspect
    /// across, turned by (<paramref name="cos"/>, <paramref name="sin"/>) — or the
    /// radius, whichever is less, plus three cells. Padding a stretched lobe by its
    /// long axis on both axes pinned every scaled-up split layout to the centre, and
    /// two lobes pinned together have no seam for the strait to follow, so the cut
    /// shredded both (Halves at 64² was the worst of it). Never stricter than the
    /// placement pad, because padding an arm by its long axis pinned it onto its hub
    /// instead, and BrokenL came out in eight pieces.
    /// </summary>
    private static (float x, float z) ClampIntoFootprint(int n, float x, float z, float r,
                                                         float aspect, float cos, float sin)
    {
        float along = r / aspect, across = r * aspect;
        float ex = MathF.Sqrt(along * along * cos * cos + across * across * sin * sin);
        float ez = MathF.Sqrt(along * along * sin * sin + across * across * cos * cos);
        float half = (n - 1) * 0.5f;
        float padX = Math.Min(Math.Min(ex, r) + 3f, half), padZ = Math.Min(Math.Min(ez, r) + 3f, half);
        return (Math.Clamp(x, padX, n - 1 - padX), Math.Clamp(z, padZ, n - 1 - padZ));
    }

    /// <summary>
    /// Lays the lobes out per arrangement. Neighbours are placed to nearly touch so the
    /// linker has something to work with; separation is carved (straits), so a lobe
    /// with neighbours may stretch and wander as freely as a lone one.
    /// </summary>
    private static Layout PlaceLobes(int seed, IslandParams p, IslandArrangement how,
                                     float radius, float cx, float cz, float spread)
        => new LobePlacer(seed, p, how, radius, cx, cz, spread).Place(how);

    /// <summary>The state one <see cref="PlaceLobes"/> call works in: the seed, the frame, the lobes made so far.</summary>
    private sealed class LobePlacer
    {
        private const float Stretch = 1.8f;

        private readonly int seed;
        private readonly IslandParams p;
        private readonly float irr, radius, cx, cz, spread, wander;
        private readonly List<Lobe> made = new();
        private float lagoon, lagoonX, lagoonZ;
        private Waist? waist;

        public LobePlacer(int seed, IslandParams p, IslandArrangement how, float radius,
                          float cx, float cz, float spread)
        {
            this.seed = seed;
            this.p = p;
            this.radius = radius;
            this.cx = cx;
            this.cz = cz;
            this.spread = spread;
            lagoonX = cx;
            lagoonZ = cz;
            irr = Math.Clamp(p.Irregularity, 0f, 1f);
            wander = how == IslandArrangement.Single ? 0.55f : 0.5f;
        }

        public Layout Place(IslandArrangement how)
        {
            switch (how)
            {
                case IslandArrangement.Satellites:
                case IslandArrangement.Twins:
                case IslandArrangement.Triplets:
                case IslandArrangement.Archipelago:
                case IslandArrangement.BrokenRing:
                case IslandArrangement.Ring:
                case IslandArrangement.Arc:
                case IslandArrangement.BrokenArc:
                case IslandArrangement.Atoll:
                case IslandArrangement.Shards:
                case IslandArrangement.Caldera:
                    PlaceRings(how);
                    break;

                case IslandArrangement.Cross:
                case IslandArrangement.BrokenCross:
                case IslandArrangement.TShape:
                case IslandArrangement.BrokenT:
                case IslandArrangement.LShape:
                case IslandArrangement.BrokenL:
                case IslandArrangement.Star:
                    PlaceArms(how);
                    break;

                case IslandArrangement.Fractal:
                case IslandArrangement.Rosette:
                case IslandArrangement.NShape:
                case IslandArrangement.Harmony:
                case IslandArrangement.Isthmus:
                case IslandArrangement.Reef:
                    PlaceChains(how);
                    break;

                case IslandArrangement.Square:
                case IslandArrangement.Rhomb:
                case IslandArrangement.Quarters:
                case IslandArrangement.Halves:
                    PlaceBlocks(how);
                    break;

                case IslandArrangement.ThousandIsles:
                    PlaceQuilt();
                    break;

                default:
                    Add(cx, cz, radius, 0x0001u);
                    break;
            }

            ArrangementTraits t = Traits(how);
            return new Layout(made.ToArray(), lagoon, lagoonX, lagoonZ, waist,
                              t.Straits, t.StraitWide, t.Solid);
        }

        private float Aspect(uint salt) => Mathf.Lerp(1f, Stretch, irr * Hash01(seed, salt));
        private float Angle(uint salt) => Hash01(seed, salt) * Mathf.Tau;

        private void Add(float x, float z, float r, uint salt, float aspect = 0f, float rot = float.NaN,
                         int group = -1)
        {
            (x, z) = ClampIntoFootprint(p.Size, x, z, r);
            made.Add(new Lobe(x, z, r,
                              aspect > 0f ? aspect : Aspect(salt),
                              float.IsNaN(rot) ? Angle(salt ^ 0x77u) : rot,
                              LobeRings * (0.8f + 0.5f * Hash01(seed, salt ^ 0xB3u)), wander,
                              group));
        }

        /// <summary>
        /// A ring of blobs, evenly spaced then jittered; <paramref name="tangential"/>
        /// turns each broadside to the ring, an arc rather than a bead.
        /// </summary>
        private void Ring(int count, float ringRadius, float blobRadius, float jitter, uint salt,
                          float tangential = 0f)
            => Sweep(count, ringRadius, blobRadius, jitter, salt, tangential, Mathf.Tau);

        /// <summary>
        /// <see cref="Ring"/> over <paramref name="arc"/> radians of the circle; a full
        /// Tau is the ring, less is a crescent. Jitter scales with the step.
        /// </summary>
        private void Sweep(int count, float ringRadius, float blobRadius, float jitter, uint salt,
                           float tangential, float arc)
        {
            float phase = Hash01(seed, salt) * Mathf.Tau;
            float step = arc >= Mathf.Tau - 0.001f ? arc / count : arc / Math.Max(1, count - 1);
            for (int i = 0; i < count; i++)
            {
                uint s = salt ^ (uint)(i + 1) * 2654435761u;
                float a = phase + step * i + (Hash01(seed, s) - 0.5f) * step * 0.7f;
                float rr = ringRadius * (1f - jitter * 0.5f + jitter * Hash01(seed, s ^ 0x5u));
                float br = blobRadius * (0.75f + 0.5f * Hash01(seed, s ^ 0x9u));
                float aspect = tangential > 0f
                    ? tangential * (0.85f + 0.4f * Hash01(seed, s ^ 0x11u))
                    : 0f;
                Add(cx + MathF.Cos(a) * rr, cz + MathF.Sin(a) * rr, br, s,
                    aspect, tangential > 0f ? a : float.NaN);
            }
        }

        /// <summary>
        /// A wide hub with thick arms at the given fractions of a turn. Axis-aligned,
        /// always: an arm points at an edge, and so at a Gate.
        /// </summary>
        private void Arms(float[] spokes, uint salt, float hubBack = float.NaN)
        {
            // hubBack, in turns, is the direction the hub is set back in, by an eighth
            // of the radius; NaN leaves it on the centre.
            float hx = cx, hz = cz;
            if (!float.IsNaN(hubBack))
            {
                hx += MathF.Cos(hubBack * Mathf.Tau) * radius * 0.125f;
                hz += MathF.Sin(hubBack * Mathf.Tau) * radius * 0.125f;
            }
            Add(hx, hz, radius * 0.45f, salt, 1f, 0f);
            float reach = radius * 0.58f * spread;

            for (int i = 0; i < spokes.Length; i++)
            {
                float a = spokes[i] * Mathf.Tau;
                uint s = salt ^ (uint)(i + 1) * 2654435761u;
                float arm = reach * (0.82f + 0.30f * Hash01(seed, s));
                Add(cx + MathF.Cos(a) * arm, cz + MathF.Sin(a) * arm,
                    radius * 0.37f, s, 1.7f, a + Mathf.Pi * 0.5f);
            }
        }

        /// <summary>
        /// A coil of blobs from the rim inward, <paramref name="sweep"/> turns of an
        /// arm <paramref name="thick"/> radii fat. Stopped short of the centre so the
        /// turns stay apart: (outer - inner) / sweep must exceed 2 * thick.
        /// </summary>
        private void Coil(uint salt, float sweep, float thick, int links)
        {
            const float inner = 0.08f;
            float phase = Hash01(seed, salt ^ 0x11u) * Mathf.Tau;
            float outer = radius * 0.86f * spread;

            for (int i = 0; i < links; i++)
            {
                float t = i / (float)(links - 1);
                float a = phase + t * Mathf.Tau * sweep;
                float rr = Mathf.Lerp(outer, radius * inner, t);
                uint s = salt ^ (uint)(i + 3) * 2654435761u;
                Add(cx + MathF.Cos(a) * rr, cz + MathF.Sin(a) * rr,
                    radius * thick * (0.85f + 0.3f * Hash01(seed, s)), s, 1.8f,
                    a + Mathf.Pi * 0.5f);
            }
        }

        /// <summary>Hubs with islets, and rings round a lagoon that is cleared outright.</summary>
        private void PlaceRings(IslandArrangement how)
        {
            switch (how)
            {
                // A dominant landmass with islets round it.
                case IslandArrangement.Satellites:
                    Add(cx, cz, radius * 0.61f, 0x1000u);
                    Ring(2 + (int)(Hash01(seed, 0x1001u) * 3f), radius * 0.84f * spread,
                         radius * 0.23f, 0.26f, 0x1002u);
                    break;

                // Two halves of one ragged mass, overlapping on purpose: the cut parts them.
                case IslandArrangement.Twins:
                {
                    float a = Angle(0x2000u);
                    float half = radius * 0.44f * spread;
                    Add(cx + MathF.Cos(a) * half, cz + MathF.Sin(a) * half, radius * 0.62f, 0x2001u);
                    Add(cx - MathF.Cos(a) * half, cz - MathF.Sin(a) * half, radius * 0.56f, 0x2002u);
                    break;
                }

                // The same in three, so the cracks meet at a junction inland.
                case IslandArrangement.Triplets:
                    Ring(3, radius * 0.46f * spread, radius * 0.50f, 0.16f, 0x3000u);
                    break;

                // Scattered and unequal: a few near the middle, more further out.
                case IslandArrangement.Archipelago:
                    Ring(2 + (int)(Hash01(seed, 0x4000u) * 2f), radius * 0.34f * spread,
                         radius * 0.24f, 0.55f, 0x4001u);
                    Ring(3 + (int)(Hash01(seed, 0x4002u) * 3f), radius * 0.80f * spread,
                         radius * 0.23f, 0.55f, 0x4003u);
                    break;

                // A broken rim of arcs round a cleared lagoon.
                case IslandArrangement.BrokenRing:
                {
                    float ring = radius * 0.76f * spread;
                    float blob = radius * 0.33f;
                    Ring(6 + (int)(Hash01(seed, 0x5000u) * 4f), ring, blob, 0.10f, 0x5001u, 2.1f);
                    lagoon = MathF.Max(4f, ring - blob * 0.55f);
                    break;
                }

                // The same rim unbroken: one landmass with aether in the middle of it.
                case IslandArrangement.Ring:
                {
                    float ring = radius * 0.74f * spread;
                    float blob = radius * 0.34f;
                    Ring(9 + (int)(Hash01(seed, 0x5100u) * 4f), ring, blob, 0.07f, 0x5101u, 2.2f);
                    lagoon = MathF.Max(4f, ring - blob * 0.75f);
                    break;
                }

                // A crescent round an open bay: two thirds of the circle or so. The lobes
                // are fat and only mildly tangential, so the crescent is as thick as a
                // cross's arm rather than a thread.
                case IslandArrangement.Arc:
                case IslandArrangement.BrokenArc:
                {
                    bool whole = how == IslandArrangement.Arc;
                    float ring = radius * 0.70f * spread;
                    float blob = radius * (whole ? 0.38f : 0.36f);
                    float arc = Mathf.Tau * (0.52f + 0.18f * Hash01(seed, 0x5200u));
                    // The whole arc keeps more lobes than it looks to need: fat lobes
                    // spaced by their length still part when the jitter and the
                    // coverage crop both go against a seam.
                    int count = (whole ? 8 : 4) + (int)(Hash01(seed, 0x5201u) * 3f);
                    Sweep(count, ring, blob, whole ? 0.07f : 0.12f, 0x5202u, 1.45f, arc);
                    lagoon = MathF.Max(4f, ring - blob * (whole ? 0.72f : 0.60f));
                    break;
                }

                // A ring of land round an island, the moat between them the only way in.
                case IslandArrangement.Caldera:
                {
                    float ring = radius * 0.76f * spread;
                    float blob = radius * 0.32f;
                    int count = 10 + (int)(Hash01(seed, 0x5400u) * 4f);
                    Ring(count, ring, blob, 0.07f, 0x5401u, 1.9f);
                    for (int i = 0; i < made.Count; i++) made[i] = made[i].InGroup(1);
                    Add(cx + (Hash01(seed, 0x5402u) - 0.5f) * radius * 0.16f,
                        cz + (Hash01(seed, 0x5403u) - 0.5f) * radius * 0.16f,
                        radius * (0.36f + 0.12f * Hash01(seed, 0x5404u)), 0x5405u, 1f, 0f,
                        group: 2);
                    break;
                }

                // Beads on a string: round islets whose capes overlap, cut to a step of water.
                case IslandArrangement.Atoll:
                {
                    float ring = radius * 0.74f * spread;
                    float blob = radius * 0.29f;
                    Ring(7 + (int)(Hash01(seed, 0x5300u) * 3f), ring, blob, 0.05f, 0x5301u, 1.15f);
                    lagoon = MathF.Max(4f, ring - blob * 0.62f);
                    break;
                }

                // One island cracked: a tight cluster, the seams cut narrow.
                case IslandArrangement.Shards:
                    Add(cx, cz, radius * 0.44f, 0x9000u);
                    Ring(3 + (int)(Hash01(seed, 0x9001u) * 3f), radius * 0.42f * spread,
                         radius * 0.42f, 0.18f, 0x9002u);
                    break;
            }
        }

        /// <summary>The cross / T / L / star family: one hub, a different set of spokes.</summary>
        private void PlaceArms(IslandArrangement how)
        {
            switch (how)
            {
                case IslandArrangement.Cross:
                case IslandArrangement.BrokenCross:
                    Arms(new[] { 0f, 0.25f, 0.5f, 0.75f }, 0x7000u);
                    break;

                case IslandArrangement.TShape:
                case IslandArrangement.BrokenT:
                    Arms(new[] { 0f, 0.25f, 0.75f }, 0x7100u);
                    break;

                // The hub sits back toward the outer corner: centred, its round edge
                // poked into the bay between the two arms as a spur, which on the
                // broken form stood clear of both straits as a third petal.
                case IslandArrangement.LShape:
                case IslandArrangement.BrokenL:
                    Arms(new[] { 0f, 0.25f }, 0x7200u, hubBack: 0.625f);
                    break;

                // Five or six, so no two face each other and every bay is a wedge.
                case IslandArrangement.Star:
                {
                    int points = 5 + (int)(Hash01(seed, 0x7300u) * 2f);
                    var spokes = new float[points];
                    for (int i = 0; i < points; i++) spokes[i] = (float)i / points;
                    Arms(spokes, 0x7301u);
                    break;
                }
            }
        }

        /// <summary>Chains of overlapping lobes: the snake, the coil, the letter, the commas, the neck, the barrier.</summary>
        private void PlaceChains(IslandArrangement how)
        {
            switch (how)
            {
                // A snake: each blob a stride on from the last, the heading turning by up
                // to a right angle and bouncing off the edge of the footprint.
                case IslandArrangement.Fractal:
                {
                    float blob = radius * 0.27f;
                    float heading = Angle(0x8000u);
                    float wx = cx + MathF.Cos(heading + Mathf.Pi) * radius * 0.45f;
                    float wz = cz + MathF.Sin(heading + Mathf.Pi) * radius * 0.45f;
                    int links = 6 + (int)(Hash01(seed, 0x8001u) * 3f);

                    for (int i = 0; i < links; i++)
                    {
                        uint s = 0x8002u ^ (uint)(i + 1) * 2654435761u;
                        float br = blob * (0.78f + 0.44f * Hash01(seed, s));
                        Add(wx, wz, br, s, 1.5f, heading + Mathf.Pi * 0.5f);

                        // Turn, then step: turning first makes the chain wind rather than fan out.
                        heading += (Hash01(seed, s ^ 0x3Bu) - 0.5f) * Mathf.Pi * 0.62f;
                        float stride = br * 1.35f;
                        float nx = wx + MathF.Cos(heading) * stride;
                        float nz = wz + MathF.Sin(heading) * stride;
                        // Bounce, not clamp: a clamped walk piles every remaining blob on one wall.
                        float pad = radius * 0.30f;
                        if (nx < cx - radius + pad || nx > cx + radius - pad)
                        {
                            heading = Mathf.Pi - heading;
                            nx = wx + MathF.Cos(heading) * stride;
                            nz = wz + MathF.Sin(heading) * stride;
                        }
                        if (nz < cz - radius + pad || nz > cz + radius - pad)
                        {
                            heading = -heading;
                            nx = wx + MathF.Cos(heading) * stride;
                            nz = wz + MathF.Sin(heading) * stride;
                        }
                        wx = nx;
                        wz = nz;
                    }
                    break;
                }

                // A coil of one turn and a bit: a spray of narrow petals fused at a small heart,
                // the thin, busy cousin of Star. (Meant as a ring of round bays over a full
                // hub; the petals are what the coil makes, and are kept.)
                case IslandArrangement.Rosette:
                    Coil(0xA000u, sweep: 1.35f, thick: 0.26f,
                         links: 9 + (int)(Hash01(seed, 0xA000u) * 4f));
                    break;

                // The letter: two uprights and the diagonal joining top-left to bottom-right.
                // Strokes as fat as a cross's arm, spaced by length so the diagonal, the
                // longest, is not the thinnest.
                case IslandArrangement.NShape:
                {
                    float w = radius * 0.58f, h = radius * 0.62f;
                    var strokes = new (float Ax, float Az, float Bx, float Bz)[]
                    {
                        (-w, h, -w, -h),      // left upright (north is -z: top is -h)
                        (-w, -h, w, h),       // the diagonal, top-left to bottom-right
                        (w, h, w, -h),        // right upright
                    };
                    int i = 0;
                    foreach (var (ax, az, bx, bz) in strokes)
                    {
                        float ang = MathF.Atan2(bz - az, bx - ax);
                        float len = MathF.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
                        int links = Math.Max(4, (int)MathF.Round(len / (radius * 0.30f)) + 1);
                        for (int t = 0; t < links; t++)
                        {
                            float f = t / (float)(links - 1);
                            Add(cx + Mathf.Lerp(ax, bx, f), cz + Mathf.Lerp(az, bz, f),
                                radius * 0.25f, 0xB200u ^ (uint)(++i * 2654435761u),
                                1.55f, ang + Mathf.Pi * 0.5f);
                        }
                    }
                    break;
                }

                // The yin-yang: two grouped commas, each a fat head inside the disc and a
                // tail thinning out to the rim, so only the S between them is cut.
                case IslandArrangement.Harmony:
                {
                    float disc = radius * 0.74f;
                    float phase = (int)(Hash01(seed, 0xB500u) * 4f) * Mathf.Tau / 4f;
                    for (int half = 0; half < 2; half++)
                    {
                        float flip = half == 0 ? 0f : Mathf.Pi;
                        for (int t = 0; t < 5; t++)
                        {
                            float f = t / 4f;
                            float a = phase + flip - Mathf.Pi * 0.5f + f * Mathf.Pi * 0.98f;
                            float ring = disc * Mathf.Lerp(0.34f, 0.70f, f * f * 0.6f + f * 0.4f);
                            float size = radius * Mathf.Lerp(0.37f, 0.21f, f);
                            Add(cx + MathF.Cos(a) * ring, cz + MathF.Sin(a) * ring, size,
                                0xB501u ^ (uint)((half * 8 + t + 1) * 2654435761u),
                                1.4f, a, group: half + 1);
                        }
                    }
                    break;
                }

                // Two broad heads and the neck between them. The heads lie broadside to
                // the axis and are staggered across it, so the layout fills its box and
                // the fit pass has no cause to blow it up; the neck is carved by a waist
                // whatever the heads do, since heads that bulge into each other were
                // the way an isthmus turned into a Single.
                case IslandArrangement.Isthmus:
                {
                    float a = (int)(Hash01(seed, 0xB600u) * 4f) * Mathf.Tau / 4f
                              + (Hash01(seed, 0xB601u) - 0.5f) * 0.5f;
                    float apart = radius * 0.60f * spread;
                    float stagger = radius * (0.04f + 0.18f * Hash01(seed, 0xB605u))
                                    * (Hash01(seed, 0xB606u) < 0.5f ? 1f : -1f);
                    float ax = MathF.Cos(a), az = MathF.Sin(a);       // along the neck
                    float px = -az, pz = ax;                            // across it
                    float h1x = cx + ax * apart + px * stagger, h1z = cz + az * apart + pz * stagger;
                    float h2x = cx - ax * apart - px * stagger, h2z = cz - az * apart - pz * stagger;
                    Add(h1x, h1z, radius * 0.44f, 0xB602u, 1.25f, a);
                    Add(h2x, h2z, radius * 0.41f, 0xB603u, 1.25f, a);
                    // Where the heads actually landed: Add keeps a centre off the wall.
                    (h1x, h1z) = (made[^2].Cx, made[^2].Cz);
                    (h2x, h2z) = (made[^1].Cx, made[^1].Cz);

                    // The neck runs head to head, which the stagger tilts off the axis.
                    float neck = MathF.Atan2(h1z - h2z, h1x - h2x);
                    for (int t = 1; t <= 2; t++)
                    {
                        float f = t / 3f;
                        Add(Mathf.Lerp(h2x, h1x, f), Mathf.Lerp(h2z, h1z, f), radius * 0.20f,
                            0xB604u ^ (uint)(t * 2654435761u), 1.6f, neck + Mathf.Pi * 0.5f);
                    }
                    waist = new Waist((h1x + h2x) * 0.5f, (h1z + h2z) * 0.5f, neck,
                                      halfLength: radius * 0.24f,
                                      halfWidth: MathF.Max(2.5f, radius * (0.08f + 0.05f * Hash01(seed, 0xB607u))),
                                      flare: radius * 0.50f);
                    break;
                }

                // A main island behind a barrier chain of tangential islets, a sound between.
                case IslandArrangement.Reef:
                {
                    float a = (int)(Hash01(seed, 0xB700u) * 4f) * Mathf.Tau / 4f;
                    float back = radius * 0.30f;
                    Add(cx - MathF.Cos(a) * back, cz - MathF.Sin(a) * back,
                        radius * 0.52f, 0xB701u);
                    float arc = Mathf.Tau * 0.30f;
                    for (int t = 0; t < 5; t++)
                    {
                        float f = t / 4f - 0.5f;
                        float ba = a + f * arc;
                        float ring = radius * 0.80f * spread;
                        Add(cx + MathF.Cos(ba) * ring, cz + MathF.Sin(ba) * ring,
                            radius * 0.16f, 0xB702u ^ (uint)((t + 1) * 2654435761u),
                            2.2f, ba);
                    }
                    break;
                }
            }
        }

        /// <summary>Blocks: fused grids of round lobes, and the symmetric splits.</summary>
        private void PlaceBlocks(IslandArrangement how)
        {
            switch (how)
            {
                // A 3 x 3 grid of fused lobes — hub, corners, edge midpoints — so the
                // silhouette squares off; without the edge four it is a quatrefoil.
                case IslandArrangement.Square:
                {
                    float d = radius * 0.35f;
                    Add(cx, cz, radius * 0.52f, 0xB000u, 1f, 0f);
                    int i = 0;
                    foreach (float sx in new[] { -1f, 1f })
                    foreach (float sz in new[] { -1f, 1f })
                        Add(cx + sx * d, cz + sz * d, radius * 0.33f,
                            0xB001u ^ (uint)(++i * 2654435761u), 1f, 0f);
                    for (int k = 0; k < 4; k++)
                        Add(cx + Dx[k] * d, cz + Dz[k] * d, radius * 0.33f,
                            0xB002u ^ (uint)((k + 1) * 2654435761u), 1f, 0f);
                    Hole(0xB0F0u);
                    break;
                }

                // The square's grid stood on a corner: hub, four points on the axes, a
                // lobe at each edge midpoint. All round — elongated points read as spikes.
                case IslandArrangement.Rhomb:
                {
                    Add(cx, cz, radius * 0.50f, 0xB100u, 1f, 0f);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.Tau / 4f;
                        Add(cx + MathF.Cos(a) * radius * 0.44f,
                            cz + MathF.Sin(a) * radius * 0.44f, radius * 0.30f,
                            0xB101u ^ (uint)((i + 1) * 2654435761u), 1f, 0f);
                        float e = a + Mathf.Tau / 8f;
                        Add(cx + MathF.Cos(e) * radius * 0.31f,
                            cz + MathF.Sin(e) * radius * 0.31f, radius * 0.32f,
                            0xB102u ^ (uint)((i + 1) * 2654435761u), 1f, 0f);
                    }
                    Hole(0xB1F0u);
                    break;
                }

                // One mass sliced twice: four lobes overlapping deeply, one per quadrant,
                // so only the cross of straits between them says it is not a Single.
                case IslandArrangement.Quarters:
                {
                    float d = radius * 0.30f * spread;
                    int i = 0;
                    foreach (float sx in new[] { -1f, 1f })
                    foreach (float sz in new[] { -1f, 1f })
                    {
                        uint s = 0xB300u ^ (uint)(++i * 2654435761u);
                        Add(cx + sx * d, cz + sz * d,
                            radius * 0.58f * (0.94f + 0.12f * Hash01(seed, s)), s,
                            1f + 0.15f * Hash01(seed, s ^ 0x7u), 0f);
                    }
                    break;
                }

                // Two equal halves split along an axis, the strait pointing at two Gates.
                case IslandArrangement.Halves:
                {
                    bool tall = Hash01(seed, 0xB400u) < 0.5f;
                    float d = radius * 0.31f * spread;
                    float dx = tall ? d : 0f, dz = tall ? 0f : d;
                    Add(cx + dx, cz + dz, radius * 0.56f, 0xB401u, 1.25f,
                        tall ? 0f : Mathf.Pi * 0.5f);
                    Add(cx - dx, cz - dz, radius * 0.56f, 0xB402u, 1.25f,
                        tall ? 0f : Mathf.Pi * 0.5f);
                    break;
                }
            }
        }

        /// <summary>
        /// A block's hole, some of the time: a lagoon of a rolled size, a little off
        /// the centre, cleared outright as a ring's is. Cleared, not bitten, so the
        /// hole is aether through the Domain and not a lake.
        /// </summary>
        private void Hole(uint salt)
        {
            if (Hash01(seed, salt) >= 0.45f) return;
            lagoon = radius * (0.10f + 0.22f * Hash01(seed, salt ^ 0x3u));
            float a = Angle(salt ^ 0x5u);
            float off = radius * 0.16f * Hash01(seed, salt ^ 0x9u);
            lagoonX = cx + MathF.Cos(a) * off;
            lagoonZ = cz + MathF.Sin(a) * off;
        }

        /// <summary>
        /// ThousandIsles: a jittered grid of lobes big enough to nearly touch, quilted
        /// over the whole footprint with every seam a strait. A scatter thrown wider
        /// than the bridge span only gets huddled back together by the linker.
        /// </summary>
        private void PlaceQuilt()
        {
            int grid = p.Size >= 112 ? 6 : p.Size >= 80 ? 5 : 4;
            const float pad = 4f;
            float cell = (p.Size - 1 - 2f * pad) / grid;
            int i = 0;
            for (int gx = 0; gx < grid; gx++)
            for (int gz = 0; gz < grid; gz++)
            {
                uint s = 0x6001u ^ (uint)(++i * 2654435761u);
                if (Hash01(seed, s ^ 0xEu) < 0.12f) continue;    // a hole in the quilt
                float px = pad + (gx + 0.30f + 0.40f * Hash01(seed, s)) * cell;
                float pz = pad + (gz + 0.30f + 0.40f * Hash01(seed, s ^ 0x9u)) * cell;
                Add(px, pz, cell * 0.55f * (0.8f + 0.4f * Hash01(seed, s ^ 0x5u)), s);
            }
        }
    }

    /// <summary>The fit band's floor: the landmass's bounding rectangle should cover at least this share of the grid.</summary>
    internal const float ExtentFloor = 0.55f;

    /// <summary>The fit band's ceiling, above which the fit pass pulls back — softly, and erring big.</summary>
    internal const float ExtentCeiling = 0.85f;

    /// <summary>
    /// Builds the footprint, widening the spread and trying again (three times) when
    /// the layout came out with fewer landmasses than its arrangement names. Spacing
    /// analytically would make every Twins two small islands in an empty field.
    /// </summary>
    internal static bool[,] BuildMask(int seed, IslandParams p, IslandArrangement how,
                                     float scale = 1f)
    {
        bool[,] mask = BuildMaskOnce(seed, p, how, 1f, scale);

        int wanted = MassesWanted(how);
        if (wanted <= 1) return mask;

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            Landmasses.DropComponentsUnder(mask, Landmasses.MinIsletCells);
            if (CountMasses(mask) >= wanted) return mask;
            mask = BuildMaskOnce(seed, p, how, 1f + 0.16f * attempt, scale);
        }
        Landmasses.DropComponentsUnder(mask, Landmasses.MinIsletCells);
        return mask;
    }

    /// <summary>Share of the grid the mask's bounding rectangle covers, 0 for no land.</summary>
    internal static float ExtentShare(bool[,] mask)
    {
        int n = mask.GetLength(0);
        int xLo = n, xHi = -1, zLo = n, zHi = -1;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!mask[x, z]) continue;
            if (x < xLo) xLo = x;
            if (x > xHi) xHi = x;
            if (z < zLo) zLo = z;
            if (z > zHi) zHi = z;
        }
        if (xHi < 0) return 0f;
        return (xHi - xLo + 1) * (float)(zHi - zLo + 1) / (n * (float)n);
    }

    private static int CountMasses(bool[,] mask)
    {
        int n = mask.GetLength(0);
        return Landmasses.Components(mask, new int[n, n]).Count;
    }

    /// <summary>The fit pass's move: every lobe grows or shrinks about the centre, radii included, so the shape keeps its proportions.</summary>

    private static void ScaleLobes(Lobe[] lobes, int n, float cx, float cz, float scale)
    {
        for (int i = 0; i < lobes.Length; i++)
        {
            Lobe l = lobes[i];
            float r = l.Radius * scale;
            var (x, z) = ClampIntoFootprint(n, cx + (l.Cx - cx) * scale, cz + (l.Cz - cz) * scale, r,
                                            l.Aspect, l.Cos, l.Sin);
            lobes[i] = new Lobe(l, x, z, r);
        }
    }

    /// <summary>
    /// Rasterises one layout: the nearest lobe owns each cell, each lobe keeps its own
    /// share of its disc, seams between pieces are carved into straits, the lagoon is
    /// cleared, and a one-cell border is left empty.
    /// </summary>
    private static bool[,] BuildMaskOnce(int seed, IslandParams p, IslandArrangement how,
                                         float spread, float scale = 1f)
    {
        int n = p.Size;
        float radius = AutoRadius(p);
        float cx = (n - 1) * 0.5f, cz = (n - 1) * 0.5f;
        float irr = Math.Clamp(p.Irregularity, 0f, 1f);

        Layout layout = PlaceLobes(seed, p, how, radius, cx, cz, spread);
        Lobe[] lobes = layout.Lobes;
        float lagoon = layout.Lagoon;
        float lagoonX = layout.LagoonX, lagoonZ = layout.LagoonZ;
        Waist? waist = layout.Waist;

        if (MathF.Abs(scale - 1f) > 0.001f)
        {
            ScaleLobes(lobes, n, cx, cz, scale);
            lagoon *= scale;
            lagoonX = cx + (lagoonX - cx) * scale;
            lagoonZ = cz + (lagoonZ - cz) * scale;
            waist = waist?.Scaled(cx, cz, scale);
        }

        var wobble = new Noise(seed + 23, frequency: 1f, octaves: 2);
        // The shape noise is island-relative, like its warp: the same number of
        // periods across a lobe at every footprint, normalised to 64² (which it leaves
        // bit-identical). At a fixed frequency per cell a 128² block's hub, almost all
        // interior, had a dozen low patches of noise inside it, and the coverage cut
        // took each one: a scatter of one-cell pits where 64² had one round hole.
        var shape = new Noise(seed, frequency: 0.05f * 64f / n, octaves: 4)
            .WithWarp(amplitude: (0.25f + 0.55f * irr) * n, frequency: 0.6f / n);
        // Strait width wanders, so it narrows to a step across in places and opens elsewhere.
        var strait = new Noise(seed + 907, frequency: 0.09f, octaves: 3);
        // Widest just inside the bridge span, so the geometry is crossable as it stands.
        float straitCells = layout.StraitWide > 0f
            ? layout.StraitWide
            : MathF.Max(1.4f, (int)p.Crossings + 0.4f);

        var field = new float[n, n];
        var norm = new float[n, n];
        var owner = new int[n, n];
        var cut = new bool[n, n];
        // Inside two lobes at once: interior by construction, so the coverage cut
        // leaves it alone. The cut ranks a lobe's cells by the shape noise and drops
        // the lowest share, which shapes a coast; on a lobe that is all interior (a
        // block's hub) it had nowhere to land but the middle, and the blocks came
        // out with a scatter of pits where a single rolled hole was meant.
        var overlap = new bool[n, n];
        var candidates = new List<float>[lobes.Length];
        for (int i = 0; i < lobes.Length; i++) candidates[i] = new List<float>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            // The nearest lobe owns the cell (min, not sum, so islets do not fuse). The
            // runner-up is tracked per PIECE (Group), not per lobe: the strait goes
            // where two pieces' fields agree.
            float d = float.MaxValue, rd = 1f;
            int mine = 0, bestPiece = int.MinValue;
            float dBest = float.MaxValue, rBest = 1f;
            float dOther = float.MaxValue, rOther = 1f;
            int within = 0;
            for (int i = 0; i < lobes.Length; i++)
            {
                float di = lobes[i].Distance(x, z, wobble, irr, out float ri);
                if (di < 1f) within++;
                if (di < d) { d = di; rd = ri; mine = i; }

                int piece = lobes[i].Group >= 0 ? lobes[i].Group : -(i + 1);
                if (piece == bestPiece)
                {
                    if (di < dBest) { dBest = di; rBest = ri; }
                }
                else if (di < dBest)
                {
                    if (dBest < dOther) { dOther = dBest; rOther = rBest; }
                    dBest = di; rBest = ri; bestPiece = piece;
                }
                else if (di < dOther) { dOther = di; rOther = ri; }
            }
            norm[x, z] = d;
            owner[x, z] = mine;
            overlap[x, z] = within >= 2;

            // The seam in cells (a normalised unit is one lobe radius); the band never
            // closes completely.
            if (layout.Straits && lobes.Length > 1 && dOther < float.MaxValue)
            {
                float seam = (dOther - dBest) * 0.5f * (rBest + rOther);
                float width = StraitNarrowest
                              + (straitCells - StraitNarrowest) * strait.At(x, z);
                cut[x, z] = seam < width;
            }

            // The lagoon is cleared outright: left to the shape noise, the middle of
            // a ring fills in as often as not.
            if (lagoon > 0f)
            {
                float lx = x - lagoonX, lz = z - lagoonZ;
                float wob = 0.86f + 0.28f * wobble.At(lx * 0.09f, lz * 0.09f);
                if (lx * lx + lz * lz < lagoon * lagoon * wob) cut[x, z] = true;
            }

            // So is a waist: the bays either side of a neck are aether by decree.
            if (waist is Waist w && w.Cuts(x, z, wobble)) cut[x, z] = true;

            float fall = 1f - FieldOps.SmoothStep(0.40f, 1f, d);
            float body = 0.35f + 0.65f * shape.At(x, z);
            field[x, z] = fall * body;

            // fall is 0 at d >= 1, so only the blobs feed the quantile; wider, the
            // guaranteed zeroes drag the threshold to 0 and Coverage goes inert.
            if (d < 1f) candidates[mine].Add(field[x, z]);
        }

        // A threshold per lobe: one global cut deletes any lobe that sits under a low
        // patch of the shape noise.
        float want = 1f - Math.Clamp(MathF.Max(p.Coverage, layout.Solid), 0.01f, 0.99f);
        var threshold = new float[lobes.Length];
        for (int i = 0; i < lobes.Length; i++)
            threshold[i] = FieldOps.Quantile(candidates[i], want);

        var mask = new bool[n, n];
        // One-cell empty border so every land cell has a reachable coast.
        for (int x = 1; x < n - 1; x++)
        for (int z = 1; z < n - 1; z++)
            mask[x, z] = norm[x, z] < 1f && (overlap[x, z] || field[x, z] > threshold[owner[x, z]])
                         && !cut[x, z];

        return mask;
    }

    /// <summary>
    /// Notches the coast by deleting whole regions rather than cutting a disc out of
    /// the mask, so the new coastline follows region borders; a bite well inside
    /// punches a hole. Only regions wholly on the largest landmass are eaten — a
    /// bite must not take a satellite.
    /// </summary>
    internal static void BiteRegions(int seed, IslandParams p, bool[,] land, int[,] region, int count)
    {
        float irr = Math.Clamp(p.Irregularity, 0f, 1f);
        if (irr < 0.15f || count == 0) return;

        int n = p.Size;
        float radius = AutoRadius(p);
        float cx = (n - 1) * 0.5f, cz = (n - 1) * 0.5f;

        int[] cells = CountRegionCells(land, region, count, n, out int remaining);
        int original = remaining;

        var massOf = new int[n, n];
        int largest = Landmasses.LargestComponent(land, massOf);
        var offMain = new bool[count];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z] && massOf[x, z] != largest) offMain[region[x, z]] = true;

        int bites = 1 + (int)(Hash01(seed, 0x77A3) * (0.5f + 2.7f * irr));
        for (int i = 0; i < bites; i++)
        {
            uint salt = 0x9100u + (uint)i * 977u;
            float ang = Hash01(seed, salt) * Mathf.Tau;

            // An interior bite is placed well inside and kept small: a hole, not a notch.
            bool interior = i == 0 && Hash01(seed, salt ^ 0xA5u) < 0.35f;
            float from = radius * (interior ? 0.10f + 0.35f * Hash01(seed, salt ^ 0x31u)
                                            : 0.25f + 0.85f * Hash01(seed, salt ^ 0x31u));
            float reach = radius * (interior ? 0.20f + 0.25f * Hash01(seed, salt ^ 0x57u)
                                             : 0.30f + 0.75f * Hash01(seed, salt ^ 0x57u));
            var at = new Vector2(cx + MathF.Cos(ang) * from, cz + MathF.Sin(ang) * from);

            // The bite's own outline is lobed too, so a circle does not decide the patches.
            var lobe = new Noise(seed + 3300 + i, frequency: 1f, octaves: 2);
            int[] inside = CellsInsideBite(land, region, count, n, at, reach, lobe);

            var doomed = new bool[count];
            int loss = 0;
            for (int r = 0; r < count; r++)
                if (cells[r] > 0 && !offMain[r] && inside[r] >= cells[r] * 0.5f)
                {
                    doomed[r] = true;
                    loss += cells[r];
                }

            // No single bite takes a third of what is left, and the bites together
            // never drop the island below 60% of what it started with.
            if (loss == 0) continue;
            if (loss > remaining * 0.33f) continue;
            if (remaining - loss < original * 0.60f) continue;

            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                if (land[x, z] && doomed[region[x, z]]) land[x, z] = false;

            for (int r = 0; r < count; r++) if (doomed[r]) cells[r] = 0;
            remaining -= loss;
        }
    }

    /// <summary>Land cells per region; <paramref name="total"/> is their sum.</summary>
    private static int[] CountRegionCells(bool[,] land, int[,] region, int count, int n, out int total)
    {
        var cells = new int[count];
        total = 0;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z]) { cells[region[x, z]]++; total++; }
        return cells;
    }

    /// <summary>Land cells per region inside one lobed bite of <paramref name="reach"/> round <paramref name="at"/>.</summary>
    private static int[] CellsInsideBite(bool[,] land, int[,] region, int count, int n,
                                         Vector2 at, float reach, Noise lobe)
    {
        var inside = new int[count];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            Vector2 d = new Vector2(x, z) - at;
            float a = MathF.Atan2(d.Y, d.X);
            float rEff = reach * (1f + 0.45f * (lobe.At(MathF.Cos(a) * 1.9f, MathF.Sin(a) * 1.9f) * 2f - 1f));
            if (d.Length() < rEff) inside[region[x, z]]++;
        }
        return inside;
    }

    /// <summary>The lobe radius: as authored, or 45% of the footprint.</summary>
    internal static float AutoRadius(IslandParams p)
        => p.Radius > 0f ? p.Radius : p.Size * 0.45f;
}
