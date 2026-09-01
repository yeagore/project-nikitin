using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>Stage 1: the land mask — lobes laid out per arrangement, rasterised, bitten.</summary>
internal static class Footprint
{
    /// <summary>Turns around the circumference sampled for coastline lobes.</summary>
    private const float LobeRings = 1.7f;

    /// <summary>
    /// Narrowest a strait between two lobes may pinch to, in cells. Just over one:
    /// the water may narrow to a single step across — which is what makes a crack
    /// read as a crack rather than as a channel — but it may never close, because
    /// a strait that heals is an arrangement quietly delivering fewer landmasses
    /// than it promised.
    /// </summary>
    private const float StraitNarrowest = 1.05f;

    /// <summary>One blob of the footprint: an ellipse with a wandering radius.</summary>
    private readonly struct Lobe
    {
        public readonly float Cx, Cz, Radius, Aspect, Cos, Sin;
        public readonly float Rings;      // how many wobbles go round its coast

        /// <summary>
        /// How far this lobe's radius is allowed to wander, as a share of the
        /// island's Irregularity. A lone landmass can wobble freely; a lobe placed
        /// next to another cannot, because a coast that swings by a third of its
        /// radius decides for itself whether two islands are two islands.
        /// </summary>
        public readonly float Wander;

        /// <summary>
        /// Which piece of the arrangement this lobe belongs to. <b>−1 — the
        /// default — is a piece of its own</b>: under a cutting layout, every
        /// seam it shares is carved, which is how every arrangement behaved
        /// before groups existed. Two lobes sharing a non-negative group are one
        /// piece, and the seam between them is left alone whatever the layout —
        /// which is what lets a yin-yang comma be a <i>chain</i> of lobes and
        /// still one island, with only the S between the two commas cut.
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

        /// <summary>
        /// As above, and reports the wandering radius it measured against, in
        /// cells. The strait carving needs it: the seam between two lobes is where
        /// their normalised distances agree, and turning that back into a width on
        /// the ground takes the radius it was normalised by.
        /// </summary>
        public float Distance(float x, float z, Noise lobes, float irr, out float rEff)
        {
            float dx = x - Cx, dz = z - Cz;
            float rx = (dx * Cos + dz * Sin) * Aspect;
            float rz = (-dx * Sin + dz * Cos) / Aspect;
            float dist = MathF.Sqrt(rx * rx + rz * rz);

            // Sampled on the unit circle so it is seamless in angle — sampling the
            // angle itself would seam at +-pi. The offset per lobe keeps two
            // islets from having the same coastline.
            float ang = MathF.Atan2(rz, rx);
            float lobe = lobes.At(MathF.Cos(ang) * Rings + Cx, MathF.Sin(ang) * Rings + Cz);
            rEff = MathF.Max(1e-3f, Radius * (1f + irr * Wander * (lobe * 2f - 1f)));
            return dist / rEff;
        }
    }

    /// <summary>
    /// A footprint's blobs and what to do where two of them meet.
    ///
    /// <b>Straits are a property of the arrangement, not of the geometry.</b> The
    /// same ring of blobs is a <see cref="IslandArrangement.Ring"/> if the seams
    /// are left alone and a <see cref="IslandArrangement.BrokenRing"/> if they are
    /// cut, and an <see cref="IslandArrangement.Atoll"/> if they are cut narrowly
    /// enough that the islets still all but touch. So the layout says.
    /// </summary>
    private readonly struct Layout
    {
        public readonly Lobe[] Lobes;

        /// <summary>Radius of water cleared in the middle, or 0 for none.</summary>
        public readonly float Lagoon;

        /// <summary>Whether the seam between two blobs is carved into a strait.</summary>
        public readonly bool Straits;

        /// <summary>
        /// Widest that strait may open, in cells; 0 takes the Domain's bridge
        /// span, which is the width that keeps every arrangement crossable.
        /// </summary>
        public readonly float StraitWide;

        /// <summary>
        /// A floor under <see cref="IslandParams.Coverage"/> for this layout, or 0
        /// to take it as authored.
        ///
        /// Coverage is applied <i>per blob</i> — each keeps that share of its own
        /// disc — which is what stops one lobe being deleted by a low patch of the
        /// shape noise. On a thick blob the leftovers are a ragged coast; on a
        /// thin arm they are holes, and the arm stops being one landmass: a
        /// <c>Fractal</c> two cells wide came out as twenty separate islets. A
        /// layout whose shape depends on being <b>continuous</b> says so here, and
        /// takes its coastline from its wandering radius instead.
        /// </summary>
        public readonly float Solid;

        public Layout(Lobe[] lobes, float lagoon, bool straits, float straitWide = 0f,
                      float solid = 0f)
        {
            Lobes = lobes;
            Lagoon = lagoon;
            Straits = straits;
            StraitWide = straitWide;
            Solid = solid;
        }
    }

    /// <summary>
    /// Where the footprint's blobs go, per <see cref="IslandArrangement"/>. Laid
    /// out deliberately rather than thresholded out of noise: "one big island with
    /// three satellites" is a thing a Domain wants to *be*, and no single
    /// fragmentation number reliably produces it.
    ///
    /// Neighbouring blobs are placed so their edges land within a couple of cells
    /// of each other, which is what gives the bridge repair something to work
    /// with; the coastline noise then decides whether they touch, nearly touch, or
    /// need nudging.
    /// </summary>
    private static Layout PlaceLobes(int seed, IslandParams p, IslandArrangement how,
                                     float radius, float cx, float cz, float spread)
    {
        float irr = Math.Clamp(p.Irregularity, 0f, 1f);
        bool alone = how == IslandArrangement.Single;
        float lagoon = 0f;

        // <b>Separation is cut, not hoped for.</b> Where two lobes meet, the seam
        // between them is carved into a strait (see BuildMaskOnce), so a lobe with
        // a neighbour may stretch and let its coast swing exactly as far as a lone
        // one. Damping those two numbers was the previous answer — it stopped
        // Twins fusing and it also made every multi-island layout a field of
        // discs, which is the wrong trade: the point of an arrangement is where
        // the land is, and the point of the noise is that no coastline is a
        // circle. Now the layout decides the first and the noise decides the
        // second, and neither has to do the other's job.
        const float stretch = 1.8f;
        float wander = alone ? 0.55f : 0.5f;

        float Aspect(uint salt) => Mathf.Lerp(1f, stretch, irr * TerrainHash01(seed, salt));
        float Angle(uint salt) => TerrainHash01(seed, salt) * Mathf.Tau;

        var made = new List<Lobe>();

        void Add(float x, float z, float r, uint salt, float aspect = 0f, float rot = float.NaN,
                 int group = -1)
        {
            // Keep every blob inside the footprint with a margin, or a nudge later
            // will push it into the wall.
            //
            // <b>The margin cannot exceed half the footprint.</b> A lobe wider than
            // the Domain wants a pad bigger than the room there is for it, and
            // `Math.Clamp` throws when its minimum passes its maximum — so any
            // `Size` small enough for the auto radius to fill it crashed outright.
            // At 64 cells the radius is 28.8, the pad 31.8, and the room 31.2.
            // Where a blob really is that big the only sensible place for it is the
            // middle, which is what a pad of half the footprint says.
            int n = p.Size;
            float pad = Math.Min(r + 3f, (n - 1) * 0.5f);
            x = Math.Clamp(x, pad, n - 1 - pad);
            z = Math.Clamp(z, pad, n - 1 - pad);
            made.Add(new Lobe(x, z, r,
                              aspect > 0f ? aspect : Aspect(salt),
                              float.IsNaN(rot) ? Angle(salt ^ 0x77u) : rot,
                              LobeRings * (0.8f + 0.5f * TerrainHash01(seed, salt ^ 0xB3u)), wander,
                              group));
        }

        /// A ring of blobs at a given radius, evenly spaced then jittered.
        /// <paramref name="tangential"/> turns each blob broadside to the ring, so
        /// the ring reads as a chain of arcs rather than as a necklace of beads.
        void Ring(int count, float ringRadius, float blobRadius, float spread, uint salt,
                  float tangential = 0f)
            => Sweep(count, ringRadius, blobRadius, spread, salt, tangential, Mathf.Tau);

        /// As <c>Ring</c>, over part of the circle: <paramref name="arc"/> radians
        /// of it, starting where the seed says. A full <c>Tau</c> is the ring; less
        /// is a crescent, and the jitter is scaled with the sweep so a short arc
        /// does not shake its blobs out of line.
        void Sweep(int count, float ringRadius, float blobRadius, float spread, uint salt,
                   float tangential, float arc)
        {
            float phase = TerrainHash01(seed, salt) * Mathf.Tau;
            float step = arc >= Mathf.Tau - 0.001f ? arc / count : arc / Math.Max(1, count - 1);
            for (int i = 0; i < count; i++)
            {
                uint s = salt ^ (uint)(i + 1) * 2654435761u;
                float a = phase + step * i + (TerrainHash01(seed, s) - 0.5f) * step * 0.7f;
                float rr = ringRadius * (1f - spread * 0.5f + spread * TerrainHash01(seed, s ^ 0x5u));
                float br = blobRadius * (0.75f + 0.5f * TerrainHash01(seed, s ^ 0x9u));
                // An ellipse is squashed along its rotation and stretched across
                // it, so rotating to the radial direction elongates the blob along
                // the tangent — around the lagoon rather than into it.
                float aspect = tangential > 0f
                    ? tangential * (0.85f + 0.4f * TerrainHash01(seed, s ^ 0x11u))
                    : 0f;
                Add(cx + MathF.Cos(a) * rr, cz + MathF.Sin(a) * rr, br, s,
                    aspect, tangential > 0f ? a : float.NaN);
            }
        }

        /// A hub with arms off it, at the given fractions of a turn. The whole
        /// cross / T / L / star family is this one shape with a different set of
        /// spokes — and the *broken* forms are the same again with the seams cut,
        /// which is why they share a case.
        ///
        /// An ellipse is squashed along its own rotation, so an arm is rotated to
        /// the *tangent* to make it point outward. The hub is deliberately wide
        /// and the arms are thick: a cross of thin arms reads as a starfish, and
        /// what is wanted is country with four ways out of it.
        void Arms(float[] spokes, uint salt)
        {
            // **Axis-aligned, always.** A cross rotated 30° is a cross that has
            // stopped meaning "four arms, one per compass point" and started
            // meaning "some arms" — and since the Gates are on the four edges, an
            // arm pointing at an edge is the whole use of the shape.
            //
            // Hub and arms fattened 0.40/0.34 → 0.45/0.37 (2026-09-01): measured,
            // an L was 12% land against a Single's 34%, and what read as "a
            // corner of country" was a pair of causeways. See the audit's Bulk
            // table — the arms family sat in the thin half of it entire.
            Add(cx, cz, radius * 0.45f, salt, 1f, 0f);
            float reach = radius * 0.58f * spread;

            for (int i = 0; i < spokes.Length; i++)
            {
                float a = spokes[i] * Mathf.Tau;
                uint s = salt ^ (uint)(i + 1) * 2654435761u;
                float arm = reach * (0.82f + 0.30f * TerrainHash01(seed, s));
                Add(cx + MathF.Cos(a) * arm, cz + MathF.Sin(a) * arm,
                    radius * 0.37f, s, 1.7f, a + Mathf.Pi * 0.5f);
            }
        }

        /// A coil of blobs from the rim inward.
        ///
        /// <paramref name="sweep"/> is how many turns it makes and
        /// <paramref name="thick"/> how fat the arm is, and those two numbers are
        /// the whole difference between a rosette and a spiral. At one and a bit
        /// turns with a thick arm the lobes overlap into a ring of round bays — a
        /// flower, which is what this produced when it was *meant* to be a spiral
        /// and was good enough to keep. At two and a half turns with a thin arm
        /// the coil stays open and the coast runs alongside itself.
        void Coil(uint salt, float sweep, float thick, int links)
        {
            const float inner = 0.08f;
            float phase = TerrainHash01(seed, salt ^ 0x11u) * Mathf.Tau;
            float outer = radius * 0.86f * spread;

            // For the turns to stay apart, the radius has to fall faster per turn
            // than the arm is wide: (outer - inner) / sweep > 2 * thick. Stopping
            // the coil short of the centre is what buys that room — wound all the
            // way in, the last turns touch and the spiral fills itself in.
            for (int i = 0; i < links; i++)
            {
                float t = i / (float)(links - 1);
                float a = phase + t * Mathf.Tau * sweep;
                float rr = Mathf.Lerp(outer, radius * inner, t);
                uint s = salt ^ (uint)(i + 3) * 2654435761u;
                Add(cx + MathF.Cos(a) * rr, cz + MathF.Sin(a) * rr,
                    radius * thick * (0.85f + 0.3f * TerrainHash01(seed, s)), s, 1.8f,
                    a + Mathf.Pi * 0.5f);
            }
        }

        switch (how)
        {
            // A dominant landmass with islets round it. The islets are placed
            // clear of the main blob; where one lands close enough to touch, the
            // strait carving parts them along the seam.
            case IslandArrangement.Satellites:
                Add(cx, cz, radius * 0.61f, 0x1000u);
                Ring(2 + (int)(TerrainHash01(seed, 0x1001u) * 3f), radius * 0.84f * spread,
                     radius * 0.23f, 0.26f, 0x1002u);
                break;

            // Two halves of one irregular mass, split by the strait that runs
            // between them: a crack rather than a channel between two discs. The
            // blobs are placed close enough to overlap on purpose — what makes
            // them two islands is the cut, so the silhouette can be as ragged as
            // a lone island's.
            case IslandArrangement.Twins:
            {
                float a = Angle(0x2000u);
                float half = radius * 0.44f * spread;
                Add(cx + MathF.Cos(a) * half, cz + MathF.Sin(a) * half, radius * 0.62f, 0x2001u);
                Add(cx - MathF.Cos(a) * half, cz - MathF.Sin(a) * half, radius * 0.56f, 0x2002u);
                break;
            }

            // The same again in three, so the cracks meet at a junction inland.
            case IslandArrangement.Triplets:
                Ring(3, radius * 0.46f * spread, radius * 0.50f, 0.16f, 0x3000u);
                break;

            // Scattered and unequal: two or three near the middle, four or five
            // further out, radii varying by half. An archipelago is defined by
            // having no order to it, which is what separates it from an atoll.
            // Blobs fattened 0.20/0.19 → 0.24/0.23 (2026-09-01): the thinnest
            // arrangement of the twenty-two at 10% land, every islet a skipping
            // stone. Scatter is the identity; starvation is not.
            case IslandArrangement.Archipelago:
                Ring(2 + (int)(TerrainHash01(seed, 0x4000u) * 2f), radius * 0.34f * spread,
                     radius * 0.24f, 0.55f, 0x4001u);
                Ring(3 + (int)(TerrainHash01(seed, 0x4002u) * 3f), radius * 0.80f * spread,
                     radius * 0.23f, 0.55f, 0x4003u);
                break;

            // A ring, and the lagoon is what is *not* placed. Two things separate
            // it from an archipelago, and the old version had neither: the islets
            // are elongated along the ring, so each is an arc of a broken rim
            // rather than a bead, and the water inside is cleared outright — a
            // ring of blobs alone leaves the middle to the shape noise, which
            // fills it in about as often as not.
            case IslandArrangement.BrokenRing:
            {
                float ring = radius * 0.76f * spread;
                float blob = radius * 0.33f;
                Ring(6 + (int)(TerrainHash01(seed, 0x5000u) * 4f), ring, blob, 0.10f, 0x5001u, 2.1f);
                lagoon = MathF.Max(4f, ring - blob * 0.55f);
                break;
            }

            // The same rim, unbroken: more arcs, overlapping, and the seams left
            // alone. What you get is one landmass with a lake of aether in the
            // middle of it — a coast on both sides, which is a thing no other
            // arrangement produces.
            case IslandArrangement.Ring:
            {
                float ring = radius * 0.74f * spread;
                float blob = radius * 0.34f;
                Ring(9 + (int)(TerrainHash01(seed, 0x5100u) * 4f), ring, blob, 0.07f, 0x5101u, 2.2f);
                lagoon = MathF.Max(4f, ring - blob * 0.75f);
                break;
            }

            // Part of a ring: a crescent round an open bay. Two thirds of the
            // circle or so — much less reads as a fat island with a dent, much
            // more closes into a ring.
            case IslandArrangement.Arc:
            case IslandArrangement.BrokenArc:
            {
                bool whole = how == IslandArrangement.Arc;
                float ring = radius * 0.74f * spread;
                float blob = radius * (whole ? 0.34f : 0.33f);
                float arc = Mathf.Tau * (0.52f + 0.18f * TerrainHash01(seed, 0x5200u));
                int count = (whole ? 7 : 5) + (int)(TerrainHash01(seed, 0x5201u) * 3f);
                Sweep(count, ring, blob, whole ? 0.07f : 0.12f, 0x5202u, 2.1f, arc);
                lagoon = MathF.Max(4f, ring - blob * (whole ? 0.75f : 0.55f));
                break;
            }

            // Beads on a string. The islets are round rather than drawn out along
            // the rim, they are placed so their capes overlap, and the strait
            // between each pair is cut to a single step of water — so the ring
            // reads as a row of separate islands that very nearly touch, which is
            // the thing a real atoll looks like from above.
            case IslandArrangement.Atoll:
            {
                float ring = radius * 0.74f * spread;
                float blob = radius * 0.29f;
                Ring(7 + (int)(TerrainHash01(seed, 0x5300u) * 3f), ring, blob, 0.05f, 0x5301u, 1.15f);
                lagoon = MathF.Max(4f, ring - blob * 0.62f);
                break;
            }

            // Too many islands to name, in three loose rings so the middle is as
            // busy as the rim. Each is small enough to be one place and large
            // enough to survive the islet filter.
            // Scattered over the <b>whole</b> footprint, corners included. Three
            // rings round the middle left a third of the bounding box dark —
            // rings never reach a corner — so the isles are thrown like darts:
            // uniform over the square, each keeping a minimum distance from the
            // ones already down. The spacing widens with the separation retries,
            // which is what `spread` means here.
            case IslandArrangement.ThousandIsles:
            {
                // <b>A quilt, not a scatter.</b> Every piece of a layout must be
                // bridgeable, and the linker enforces it by dragging strays
                // bodily toward the rest — so isles thrown wide are isles
                // huddled in the middle by the time the mask is legal, which is
                // exactly what both the old rings and a stratified scatter came
                // out as. The only spread that survives the law is one that is
                // already legal: a jittered grid of lobes big enough to nearly
                // touch, quilted over the whole footprint corner to corner, with
                // every seam carved to a strait. What parts the isles is then
                // exactly the water a bridge can cross.
                int grid = p.Size >= 112 ? 6 : p.Size >= 80 ? 5 : 4;
                const float pad = 4f;
                float cell = (p.Size - 1 - 2f * pad) / grid;
                int i = 0;
                for (int gx = 0; gx < grid; gx++)
                for (int gz = 0; gz < grid; gz++)
                {
                    uint s = 0x6001u ^ (uint)(++i * 2654435761u);
                    if (TerrainHash01(seed, s ^ 0xEu) < 0.12f) continue;    // a hole in the quilt
                    float px = pad + (gx + 0.30f + 0.40f * TerrainHash01(seed, s)) * cell;
                    float pz = pad + (gz + 0.30f + 0.40f * TerrainHash01(seed, s ^ 0x9u)) * cell;
                    Add(px, pz, cell * 0.55f * (0.8f + 0.4f * TerrainHash01(seed, s ^ 0x5u)), s);
                }
                break;
            }

            // One mass with four arms on the cardinal axes. The arms are elongated
            // *radially* — an ellipse is squashed along its own rotation, so the
            // rotation given is the tangent — and they overlap the hub, so what
            // comes out is one landmass with four long peninsulas and four deep
            // bays between them.
            case IslandArrangement.Cross:
            case IslandArrangement.BrokenCross:
                Arms(new[] { 0f, 0.25f, 0.5f, 0.75f }, 0x7000u);
                break;

            // Three arms: a bar with a stem off the middle of it.
            case IslandArrangement.TShape:
            case IslandArrangement.BrokenT:
                Arms(new[] { 0f, 0.25f, 0.75f }, 0x7100u);
                break;

            // Two, meeting at a right angle: a corner of land round one wide bay.
            case IslandArrangement.LShape:
            case IslandArrangement.BrokenL:
                Arms(new[] { 0f, 0.25f }, 0x7200u);
                break;

            // Five or six, so no two face each other and every bay is a wedge.
            case IslandArrangement.Star:
            {
                int points = 5 + (int)(TerrainHash01(seed, 0x7300u) * 2f);
                var spokes = new float[points];
                for (int i = 0; i < points; i++) spokes[i] = (float)i / points;
                Arms(spokes, 0x7301u);
                break;
            }

            // A snake. Each blob is placed a stride on from the last, the heading
            // turning by up to a right angle each time and bouncing off the edge of
            // the footprint, so the land doubles back on itself and the coast has
            // as much length as the island has area. The blobs overlap, so it is
            // one winding landmass rather than a row of islets.
            case IslandArrangement.Fractal:
            case IslandArrangement.BrokenFractal:
            {
                float blob = radius * 0.27f;
                float heading = Angle(0x8000u);
                float wx = cx + MathF.Cos(heading + Mathf.Pi) * radius * 0.45f;
                float wz = cz + MathF.Sin(heading + Mathf.Pi) * radius * 0.45f;
                int links = 6 + (int)(TerrainHash01(seed, 0x8001u) * 3f);

                for (int i = 0; i < links; i++)
                {
                    uint s = 0x8002u ^ (uint)(i + 1) * 2654435761u;
                    float br = blob * (0.78f + 0.44f * TerrainHash01(seed, s));
                    Add(wx, wz, br, s, 1.5f, heading + Mathf.Pi * 0.5f);

                    // Turn, then step. Turning first is what makes the chain wind
                    // rather than fan out from its first blob.
                    heading += (TerrainHash01(seed, s ^ 0x3Bu) - 0.5f) * Mathf.Pi * 0.62f;
                    float stride = br * 1.35f;
                    float nx = wx + MathF.Cos(heading) * stride;
                    float nz = wz + MathF.Sin(heading) * stride;
                    // Bounce off the footprint rather than clamping into it: a
                    // clamped walk piles every remaining blob against one wall.
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

            case IslandArrangement.Rosette:
                Coil(0xA000u, sweep: 1.35f, thick: 0.26f,
                     links: 9 + (int)(TerrainHash01(seed, 0xA000u) * 4f));
                break;

            // One island cracked. The blobs are laid over each other in a tight
            // cluster and the seams are cut narrow, so what parts the pieces reads
            // as a fracture rather than as a channel.
            case IslandArrangement.Shards:
                Add(cx, cz, radius * 0.44f, 0x9000u);
                Ring(3 + (int)(TerrainHash01(seed, 0x9001u) * 3f), radius * 0.42f * spread,
                     radius * 0.42f, 0.18f, 0x9002u);
                break;

            // A blocky mass filling a square: a round hub and four round corner
            // lobes fused, so the silhouette squares off where the corners are
            // and the shape noise keeps the sides from being ruled lines.
            case IslandArrangement.Square:
            {
                // A 3 × 3 grid of fused lobes: hub, corners, and the edge
                // midpoints — without the edge four, the corners read as a
                // quatrefoil with bays where the sides should be. Deeply
                // overlapped and floored solid, or what reads is nine conjoined
                // blobs with holes in the middle rather than a block.
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
                break;
            }

            // The square stood on its corner: four points on the axes, elongated
            // outward the way an arm is, so the diamond tapers where it points.
            case IslandArrangement.Rhomb:
            {
                // The square's own grid stood on a corner: hub, four round
                // points on the axes, and a lobe at each edge midpoint so the
                // diagonals fill in. All round — elongating the points was
                // tried first and drew a caltrop of spikes, not a diamond —
                // and floored solid like the square, for the same reason.
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
                break;
            }

            // The letter: two uprights and the diagonal that joins the top of
            // the left to the bottom of the right. Three strokes of elongated
            // lobes, fused into one winding mass — Fractal's cousin that knows
            // where it is going.
            case IslandArrangement.NShape:
            {
                float w = radius * 0.52f, h = radius * 0.60f;
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
                    for (int t = 0; t < 4; t++)
                    {
                        float f = t / 3f;
                        Add(cx + Mathf.Lerp(ax, bx, f), cz + Mathf.Lerp(az, bz, f),
                            radius * 0.185f, 0xB200u ^ (uint)(++i * 2654435761u),
                            1.8f, ang + Mathf.Pi * 0.5f);
                    }
                }
                break;
            }

            // Four roughly symmetric parts, one per quadrant, the same size give
            // or take a wobble — parted by the cross of straits their seams make.
            case IslandArrangement.Quarters:
            {
                float d = radius * 0.42f * spread;
                int i = 0;
                foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    uint s = 0xB300u ^ (uint)(++i * 2654435761u);
                    Add(cx + sx * d, cz + sz * d,
                        radius * 0.40f * (0.92f + 0.16f * TerrainHash01(seed, s)), s,
                        1f + 0.25f * TerrainHash01(seed, s ^ 0x7u), 0f);
                }
                break;
            }

            // Two roughly symmetric halves, split along an axis — Twins' formal
            // sibling: same size, straight seam, the strait pointing at two of
            // the four Gates.
            case IslandArrangement.Halves:
            {
                bool tall = TerrainHash01(seed, 0xB400u) < 0.5f;
                float d = radius * 0.31f * spread;
                float dx = tall ? d : 0f, dz = tall ? 0f : d;
                Add(cx + dx, cz + dz, radius * 0.56f, 0xB401u, 1.25f,
                    tall ? 0f : Mathf.Pi * 0.5f);
                Add(cx - dx, cz - dz, radius * 0.56f, 0xB402u, 1.25f,
                    tall ? 0f : Mathf.Pi * 0.5f);
                break;
            }

            // The yin-yang: two commas chasing each other round one disc. Each
            // comma is a <b>grouped</b> chain — a fat head and a tail that thins
            // as it sweeps half the rim — so its own seams fuse and the only cut
            // is the S between the two.
            case IslandArrangement.Harmony:
            {
                // Each comma: a fat head well inside the disc — the heads are
                // what fill the middle — and a tail that migrates out to the rim
                // as it sweeps its half-turn. First drawn with the whole chain
                // at the rim, which produced a hollow broken ring: a yin-yang is
                // a full disc with an S through it, not an O with a gap.
                float disc = radius * 0.74f;
                float phase = (int)(TerrainHash01(seed, 0xB500u) * 4f) * Mathf.Tau / 4f;
                for (int half = 0; half < 2; half++)
                {
                    float flip = half == 0 ? 0f : Mathf.Pi;
                    for (int t = 0; t < 5; t++)
                    {
                        float f = t / 4f;
                        float a = phase + flip - Mathf.Pi * 0.5f + f * Mathf.Pi * 0.98f;
                        float ring = disc * Mathf.Lerp(0.34f, 0.70f, f * f * 0.6f + f * 0.4f);
                        float size = radius * Mathf.Lerp(0.37f, 0.15f, f);
                        Add(cx + MathF.Cos(a) * ring, cz + MathF.Sin(a) * ring, size,
                            0xB501u ^ (uint)((half * 8 + t + 1) * 2654435761u),
                            1.4f, a, group: half + 1);
                    }
                }
                break;
            }

            // Two broad heads and the neck between them: one mass with a waist,
            // which is a chokepoint the settlement layer will thank us for.
            case IslandArrangement.Isthmus:
            {
                float a = (int)(TerrainHash01(seed, 0xB600u) * 4f) * Mathf.Tau / 4f
                          + (TerrainHash01(seed, 0xB601u) - 0.5f) * 0.5f;
                float apart = radius * 0.58f * spread;
                float hx = MathF.Cos(a) * apart, hz = MathF.Sin(a) * apart;
                Add(cx + hx, cz + hz, radius * 0.42f, 0xB602u);
                Add(cx - hx, cz - hz, radius * 0.40f, 0xB603u);
                for (int t = 1; t <= 2; t++)
                {
                    float f = t / 3f - 0.5f;
                    Add(cx + hx * f * 2f, cz + hz * f * 2f, radius * 0.16f,
                        0xB604u ^ (uint)(t * 2654435761u), 1.7f, a + Mathf.Pi * 0.5f);
                }
                break;
            }

            // A main island sheltering behind a barrier: a long thin chain of
            // tangential islets off one side, with a sound between chain and
            // shore. The cut is what makes the barrier beads rather than a wall.
            case IslandArrangement.Reef:
            {
                float a = (int)(TerrainHash01(seed, 0xB700u) * 4f) * Mathf.Tau / 4f;
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

            default:
                Add(cx, cz, radius, 0x0001u);
                break;
        }

        // Which arrangements are one landmass with a shape, and which are several
        // pieces. The seam carving is the whole difference — see Layout.
        bool cut = how switch
        {
            IslandArrangement.Single => false,
            IslandArrangement.Ring => false,
            IslandArrangement.Arc => false,
            IslandArrangement.Cross => false,
            IslandArrangement.Fractal => false,
            IslandArrangement.TShape => false,
            IslandArrangement.LShape => false,
            IslandArrangement.Rosette => false,
            IslandArrangement.Star => false,
            IslandArrangement.Square => false,
            IslandArrangement.Rhomb => false,
            IslandArrangement.NShape => false,
            IslandArrangement.Isthmus => false,
            _ => true,
        };
        // An atoll's islets all but touch, and a shard's crack is a crack. The
        // yin-yang's S goes the other way: its commas overlap so deeply that a
        // strait at the default width heals shut on half the seeds, and a
        // Harmony with one landmass is a blob — the S is the whole shape. It is
        // still crossable, because the strait noise pinches it to a step across
        // in places whatever its widest reach is.
        float narrow = how switch
        {
            IslandArrangement.Atoll => 1.7f,
            IslandArrangement.Shards => 1.9f,
            IslandArrangement.Harmony => 5.4f,
            _ => 0f,
        };
        // The layouts that are a shape rather than a scatter: a thin arm perforated
        // by the coverage threshold stops being an arm.
        float solid = how switch
        {
            IslandArrangement.Fractal => 0.86f,
            IslandArrangement.BrokenFractal => 0.86f,
            // Thin strokes and thin necks perforate the same way a thin arm does.
            IslandArrangement.NShape => 0.86f,
            IslandArrangement.Isthmus => 0.8f,
            IslandArrangement.Harmony => 0.82f,
            IslandArrangement.Reef => 0.8f,
            // Blocks read as blocks only while they are solid: the coverage
            // threshold pocking holes through a square turns it into nine
            // conjoined blobs, which is what Maxim saw.
            IslandArrangement.Square => 0.85f,
            IslandArrangement.Rhomb => 0.85f,
            _ => 0f,
        };
        return new Layout(made.ToArray(), lagoon, cut, narrow, solid);
    }

    /// <summary>
    /// How many separate landmasses an arrangement has to deliver to be that
    /// arrangement. Twins with one island is not Twins; an Archipelago whose
    /// islets partly merge still reads as an archipelago, so the bar is lower
    /// where merging is in character.
    /// </summary>
    private static int MassesWanted(IslandArrangement how) => how switch
    {
        IslandArrangement.Twins => 2,
        IslandArrangement.Triplets => 3,
        IslandArrangement.Satellites => 3,
        IslandArrangement.Archipelago => 4,
        IslandArrangement.BrokenRing => 4,
        IslandArrangement.BrokenArc => 3,
        IslandArrangement.Atoll => 5,
        IslandArrangement.ThousandIsles => 8,
        IslandArrangement.Shards => 4,
        IslandArrangement.BrokenCross => 4,
        IslandArrangement.BrokenT => 3,
        IslandArrangement.BrokenL => 2,
        IslandArrangement.BrokenFractal => 4,
        IslandArrangement.Quarters => 4,
        IslandArrangement.Halves => 2,
        IslandArrangement.Harmony => 2,
        IslandArrangement.Reef => 3,
        // Ring, Arc, Cross and Fractal are one landmass with a shape: their blobs
        // are meant to fuse, so counting pieces would push them apart until they
        // stopped being the shape they name.
        _ => 1,
    };

    /// <summary>
    /// Builds the footprint, pushing the blobs further apart and trying again if
    /// the layout did not come out as the arrangement it claims to be.
    ///
    /// Placing them "far enough apart" analytically does not work: a lobe's reach
    /// is its radius times its ellipse aspect times its coastline wander, so the
    /// spacing that never fuses is wide enough to make Twins two small islands in
    /// a large empty field. Measuring the result and widening only when it
    /// actually fused keeps the common case tight.
    /// </summary>
    /// <summary>
    /// The footprint band: the landmass's bounding rectangle should cover more
    /// than this share of the grid. Maxim's number — an arrangement crouched in
    /// the middle of its own Domain is room nobody is using.
    /// </summary>
    internal const float ExtentFloor = 0.55f;

    /// <summary>
    /// And past this it is gently pulled back toward four fifths. Soft on
    /// purpose, and well above the stated 0.80: the brief says err on the big
    /// side, never on the small.
    /// </summary>
    internal const float ExtentCeiling = 0.85f;

    internal static bool[,] BuildMask(int seed, IslandParams p, IslandArrangement how,
                                     float scale = 1f)
    {
        // `scale` is the fit pass's lever — see Build. The layouts were
        // authored against a 128 grid by eye, and by eye half of them crouched:
        // measured, Twins took 38% of the box it was given.
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

        // The fit pass — see BuildMask. Everything grows or shrinks about the
        // centre, radii included, so the shape keeps its proportions; the same
        // clamp Add uses keeps a grown lobe off the walls.
        if (MathF.Abs(scale - 1f) > 0.001f)
        {
            for (int i = 0; i < lobes.Length; i++)
            {
                Lobe l = lobes[i];
                float r = l.Radius * scale;
                float pad = Math.Min(r + 3f, (n - 1) * 0.5f);
                lobes[i] = new Lobe(l,
                    Math.Clamp(cx + (l.Cx - cx) * scale, pad, n - 1 - pad),
                    Math.Clamp(cz + (l.Cz - cz) * scale, pad, n - 1 - pad), r);
            }
            lagoon *= scale;
        }

        var wobble = new Noise(seed + 23, frequency: 1f, octaves: 2);
        var shape = new Noise(seed, frequency: 0.05f, octaves: 4)
            .WithWarp(amplitude: (0.25f + 0.55f * irr) * n, frequency: 0.6f / n);
        // How wide the water is where two lobes meet. Wandering, so the strait
        // narrows to a step across in places and opens to a channel in others.
        var strait = new Noise(seed + 907, frequency: 0.09f, octaves: 3);
        // A bridge reaches `Crossings` cells, so a strait that opens wider than
        // that would only have to be dragged shut again by the linker. Keeping the
        // widest part just inside the span means the arrangement's own geometry is
        // crossable as it stands.
        float straitCells = layout.StraitWide > 0f
            ? layout.StraitWide
            : MathF.Max(1.4f, (int)p.Crossings + 0.4f);

        // Bites are not taken here: cutting a shape out of the raw mask leaves an
        // arc across whatever patches it crosses. They are applied to whole
        // regions once those exist — see BiteRegions.

        var field = new float[n, n];
        var norm = new float[n, n];
        var owner = new int[n, n];
        var cut = new bool[n, n];
        var candidates = new List<float>[lobes.Length];
        for (int i = 0; i < lobes.Length; i++) candidates[i] = new List<float>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            // The nearest blob wins, and owns the cell. Taking the minimum rather
            // than summing keeps two islets from fusing into a peanut just because
            // they are close.
            //
            // The runner-up is tracked <b>per piece</b>, not per lobe: a lobe's
            // Group of −1 — the default — is a piece of its own, so on every
            // arrangement that existed before groups the two are the same thing
            // to the cell. On a grouped layout they are not. Measured lobe
            // against lobe, a cell deep in the overlap of the yin-yang's two
            // commas had both of its nearest lobes from ONE comma — no seam was
            // seen there at all — and the S healed shut on a quarter of 128²
            // seeds, which no width could fix because the cut was being drawn
            // in the wrong place. The seam that matters is where two PIECES'
            // fields agree, and that is where the strait goes.
            float d = float.MaxValue, rd = 1f;
            int mine = 0, bestPiece = int.MinValue;
            float dBest = float.MaxValue, rBest = 1f;
            float dOther = float.MaxValue, rOther = 1f;
            for (int i = 0; i < lobes.Length; i++)
            {
                float di = lobes[i].Distance(x, z, wobble, irr, out float ri);
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

            // Turn the difference between the two pieces' normalised distances
            // back into cells — a normalised unit is one lobe radius — and clear
            // a band of them either side of the seam. The band never closes
            // completely: a strait that heals is an arrangement that quietly
            // delivered fewer landmasses than it promised, which is exactly what
            // used to happen to Twins.
            if (layout.Straits && lobes.Length > 1 && dOther < float.MaxValue)
            {
                float seam = (dOther - dBest) * 0.5f * (rBest + rOther);
                float width = StraitNarrowest
                              + (straitCells - StraitNarrowest) * strait.At(x, z);
                cut[x, z] = seam < width;
            }

            // An atoll's lagoon is cleared outright rather than left to the shape
            // noise, which fills the middle of the ring as often as not — and a
            // filled atoll is an archipelago.
            if (lagoon > 0f)
            {
                float lx = x - cx, lz = z - cz;
                float wob = 0.86f + 0.28f * wobble.At(lx * 0.09f, lz * 0.09f);
                if (lx * lx + lz * lz < lagoon * lagoon * wob) cut[x, z] = true;
            }

            float fall = 1f - FieldOps.SmoothStep(0.40f, 1f, d);
            float body = 0.35f + 0.65f * shape.At(x, z);
            field[x, z] = fall * body;

            // `fall` is already 0 at d >= 1, so only the blobs themselves can be
            // land. Sampling wider would pad the quantile with guaranteed zeroes
            // and drag the threshold to 0, which is what made Coverage inert.
            if (d < 1f) candidates[mine].Add(field[x, z]);
        }

        // A threshold *per lobe*. One global cut makes Coverage a fraction of the
        // whole layout, so a lobe that happens to sit under a low patch of the
        // shape noise is simply deleted — which is what left a third of Twins with
        // one island. Per lobe it means what it says: this share of each blob
        // becomes land.
        float want = 1f - Math.Clamp(MathF.Max(p.Coverage, layout.Solid), 0.01f, 0.99f);
        var threshold = new float[lobes.Length];
        for (int i = 0; i < lobes.Length; i++)
            threshold[i] = FieldOps.Quantile(candidates[i], want);

        var mask = new bool[n, n];
        // Leave a one-cell border empty so every land cell has a reachable coast.
        for (int x = 1; x < n - 1; x++)
        for (int z = 1; z < n - 1; z++)
            mask[x, z] = norm[x, z] < 1f && field[x, z] > threshold[owner[x, z]]
                         && !cut[x, z];

        return mask;
    }

    /// <summary>
    /// Takes bites out of the island by deleting whole regions, not by cutting a
    /// shape out of the mask.
    ///
    /// Erasing a shape leaves that shape's outline on the coast — an arc, however
    /// the edge is softened — and slices in half whatever patches it crosses. A
    /// region that is mostly inside the bite is removed entirely instead, so the
    /// new coastline runs along region borders, which are already organic. It
    /// also makes the two bites on an island differ in size, since what each
    /// removes depends on the patches it happens to land on rather than on its
    /// own radius. A bite well inside the island punches a hole through it.
    /// </summary>
    internal static void BiteRegions(int seed, IslandParams p, bool[,] land, int[,] region, int count)
    {
        float irr = Math.Clamp(p.Irregularity, 0f, 1f);
        if (irr < 0.15f || count == 0) return;

        int n = p.Size;
        float radius = AutoRadius(p);
        float cx = (n - 1) * 0.5f, cz = (n - 1) * 0.5f;

        var cells = new int[count];
        int remaining = 0;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z]) { cells[region[x, z]]++; remaining++; }
        int original = remaining;

        // A bite eats coastline, never a satellite. Only Single and Satellites
        // take bites at all, and on Satellites the total-land guards below are
        // no protection for an islet: it is a tenth of the land, well inside
        // the per-bite cap, and one bite landing on it deleted it whole — which
        // is how the layout came out an islet short at every footprint. A
        // region with any cell off the largest landmass is therefore exempt.
        var massOf = new int[n, n];
        int largest = Landmasses.LargestComponent(land, massOf);
        var offMain = new bool[count];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z] && massOf[x, z] != largest) offMain[region[x, z]] = true;

        int bites = 1 + (int)(TerrainHash01(seed, 0x77A3) * (0.5f + 2.7f * irr));
        for (int i = 0; i < bites; i++)
        {
            uint salt = 0x9100u + (uint)i * 977u;
            float ang = TerrainHash01(seed, salt) * Mathf.Tau;

            // Some bites are placed well inside and kept small, which takes out
            // interior patches and leaves a hole through the island rather than a
            // notch in its coast.
            bool interior = i == 0 && TerrainHash01(seed, salt ^ 0xA5u) < 0.35f;
            float from = radius * (interior ? 0.10f + 0.35f * TerrainHash01(seed, salt ^ 0x31u)
                                            : 0.25f + 0.85f * TerrainHash01(seed, salt ^ 0x31u));
            float reach = radius * (interior ? 0.20f + 0.25f * TerrainHash01(seed, salt ^ 0x57u)
                                             : 0.30f + 0.75f * TerrainHash01(seed, salt ^ 0x57u));
            var at = new Vector2(cx + MathF.Cos(ang) * from, cz + MathF.Sin(ang) * from);

            // The bite's own outline is lobed too, so which patches fall inside is
            // not decided by a circle.
            var lobe = new Noise(seed + 3300 + i, frequency: 1f, octaves: 2);

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

            var doomed = new bool[count];
            int loss = 0;
            for (int r = 0; r < count; r++)
                if (cells[r] > 0 && !offMain[r] && inside[r] >= cells[r] * 0.5f)
                {
                    doomed[r] = true;
                    loss += cells[r];
                }

            // Never eat the island. Two guards: no single bite may take a third of
            // what is left, and the bites together may not drop the island below
            // 60% of the land it started with. The per-bite cap alone is not
            // enough — three bites each under it still compound.
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

    internal static float AutoRadius(IslandParams p)
        => p.Radius > 0f ? p.Radius : p.Size * 0.45f;
}
