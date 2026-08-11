using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// ====================================================================================
    /// STAGE 3 — THE GLOBAL DENSITY FIELD (a carved cave, not unioned blobs).
    /// ====================================================================================
    ///
    /// Realises the planned <see cref="ArchingCavePlan"/> as a single, continuous signed distance
    /// field over the WHOLE site. It implements <see cref="ITerrainDensity"/> as a VOXEL field
    /// (negative = solid rock, positive = air, zero = surface — the terrain convention) and is
    /// GLOBAL: every internal sub-tile (see <see cref="ArchingCaveChunker"/>) samples this exact same
    /// field, so the sub-meshes seam together perfectly.
    ///
    /// <para><b>The model — carve the cavity out of solid rock.</b> The old model unioned solid
    /// pillar/arch/canopy primitives, which read as a blobby kit of parts. This model does the
    /// OPPOSITE — exactly like the cave system's <c>CaveSdfField</c>:</para>
    /// <list type="number">
    ///   <item>Start from a SOLID ROCK BLOCK — a slab between the walkable floor and the rock
    ///         ceiling. Everything is solid rock by default.</item>
    ///   <item>Build the open CAVITY the cave way — every chamber a sphere / vertical capsule of
    ///         open space, every passage a capsule, all <see cref="SdfPrimitives.SmoothMin"/>-unioned
    ///         into ONE connected void. SUBTRACT it from the rock block.</item>
    ///   <item>The rock that SURVIVES between adjacent cavities is the PILLARS; the rock that
    ///         survives spanning over a passage is the ARCHES — both emergent leftover rock, never
    ///         placed solids. Keep-solid pillar hints push the cavity away so columns survive.</item>
    ///   <item>OPEN chambers carve straight through the ceiling (open to the sky); CANOPIED
    ///         chambers keep a rock roof, pierced by carved-up SKYLIGHT holes.</item>
    ///   <item>The cavity floor is flattened (<see cref="SdfPrimitives.ApplyFloorFlatten"/>) so a
    ///         NavMeshAgent can cross the whole connected cave, and the walls are domain-warp eroded.</item>
    /// </list>
    ///
    /// <para><b>Sign convention.</b> The cavity SDF is built in CAVE convention (cavity &lt; 0 inside
    /// open space). The terrain solid is then <c>max(rockBlock, -cavity)</c>: a point inside the
    /// cavity has <c>cavity &lt; 0</c> so <c>-cavity &gt; 0</c> and the max is positive (air); a point
    /// in rock away from the cavity has <c>rockBlock &lt; 0</c> and <c>-cavity &lt; 0</c> so the max is
    /// negative (solid). Verified by the two test points in <see cref="Sample"/>'s comments.</para>
    /// </summary>
    public sealed class ArchingCaveSdf : ITerrainDensity
    {
        readonly ArchingCavePlan _plan;
        readonly ArchingCaveSettings _settings;
        readonly int _seed;
        readonly Bounds _bounds;
        readonly float _erosionAmp;

        /// <summary>Shared feature tuning — captured so the wall erosion runs through the central
        /// <see cref="TerrainNoiseHelper.DetailedNoise"/> and the "Surface detail" dials reach this
        /// feature too. Optional: when null the erosion falls back to its standalone domain-warp noise.</summary>
        TerrainFeatureTuning _tuning;

        // Cave-convention smooth-union radius for blending chambers + passages into one cavity.
        readonly float _caveK;
        // Per-chamber carved-cavity vertical span (floor → cavity ceiling), precomputed once.
        readonly float[] _chamberTopY;
        // Per-chamber random floor slope (rise/run in XZ) — gentle, slope-limited, walkable.
        readonly Vector2[] _zoneSlopes;

        /// <summary>Local-space volume the field occupies — the whole site plus padding.</summary>
        public Bounds Bounds => _bounds;

        /// <summary>Always a voxel field — the carved cave has genuine overhangs (roofs, arches).</summary>
        public bool IsHeightfield => false;

        /// <summary>Captures the shared feature tuning so the wall erosion obeys the central
        /// "Surface detail" dials. Returns <c>this</c> for fluent use right after construction.</summary>
        public ArchingCaveSdf WithTuning(TerrainFeatureTuning tuning)
        {
            _tuning = tuning;
            return this;
        }

        public ArchingCaveSdf(ArchingCavePlan plan, ArchingCaveSettings settings, int seed, Bounds siteBounds)
        {
            _plan = plan;
            _settings = settings;
            _seed = seed;
            _bounds = siteBounds;

            _caveK = Mathf.Max(0.25f, settings.caveSmoothness);

            // Erosion displacement in metres — modest so it ripples the cave walls without
            // dissolving thin surviving pillars.
            float avgThick = (settings.pillarThickness.x + settings.pillarThickness.y) * 0.5f;
            _erosionAmp = settings.erosion * avgThick * 0.4f;

            float ceilSpan = plan.CeilingY - plan.FloorY;

            // Precompute each chamber's cavity ceiling. OPEN chambers carve well past the rock
            // ceiling (so the roof rock is fully removed and the chamber is open to the sky);
            // CANOPIED chambers stop below it so a rock roof survives.
            _chamberTopY = new float[plan.Zones.Count];
            for (int i = 0; i < plan.Zones.Count; i++)
            {
                var z = plan.Zones[i];
                float chamberSpan = ceilSpan * settings.chamberHeight * z.HeightScale;
                float canopiedTop = plan.FloorY + Mathf.Min(chamberSpan, ceilSpan * 0.82f);
                // Open: carve a chunk above the ceiling so the sky opens straight in.
                float openTop = plan.CeilingY + ceilSpan * 0.45f + chamberSpan * 0.15f;
                _chamberTopY[i] = Mathf.Lerp(canopiedTop, openTop, Mathf.SmoothStep(0f, 1f, z.Openness));
            }

            // One gentle slope per chamber so cavity floors are not dead-level but stay walkable.
            var rng = new System.Random(seed * 31 + 1777);
            _zoneSlopes = new Vector2[plan.Zones.Count];
            for (int i = 0; i < _zoneSlopes.Length; i++)
            {
                float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float mag = 0.12f * (0.3f + (float)rng.NextDouble() * 0.7f);   // safely walkable
                _zoneSlopes[i] = new Vector2(Mathf.Cos(ang) * mag, Mathf.Sin(ang) * mag);
            }
        }

        /// <summary>Folded SDF — no single surface height. Returns the floor Y as a sane stub.</summary>
        public float SurfaceHeight(float localX, float localZ) => _plan.FloorY;

        /// <summary>
        /// Signed density at a feature-local point. Negative = solid rock, positive = air. This is the
        /// one function every sub-tile shares, which is what makes the chunked sub-meshes seamless.
        /// </summary>
        public float Sample(Vector3 p)
        {
            // --- 1) THE SOLID ROCK BLOCK ----------------------------------------------------------
            // A solid rock massif: everything from the ceiling DOWN is rock. SDF in terrain
            // convention: negative below the ceiling (solid rock), positive above it (open sky).
            // It is solid all the way down, so the carved cave always has solid rock beneath its
            // walkable floor — there is never a void under the player.
            float rockBlock = p.y - _plan.CeilingY;

            // --- 2) THE OPEN CAVITY (cave convention: < 0 inside open space) ----------------------
            // Built exactly like CaveSdfField: chambers + passages + skylights, SmoothMin-unioned.
            float cavity = CavityDensity(p);

            // --- 3) CARVE: solid rock minus the cavity --------------------------------------------
            // result = SmoothMax(rockBlock, -cavity). A point INSIDE the cavity has cavity < 0 so
            // -cavity > 0 → result > 0 (air). A SOLID point in the rock block, away from any cavity,
            // has rockBlock < 0 and -cavity < 0 → result < 0 (solid). SmoothMax rounds the rock
            // walls where they meet the cavity so the carve reads as eroded cave, not a boolean cut.
            float d = SmoothMax(rockBlock, -cavity, _caveK);

            // --- 4) DOMAIN-WARP EROSION — ripple the cave walls into cohesive eroded sandstone ----
            // Applied only near the iso-band so deep-solid / deep-air regions cannot pop pockets or
            // float shards. The amplitude is the feature's own erosion PLUS the shared surface-detail
            // layer, so the central "Surface detail" dials sharpen the cave walls too.
            float erosionAmp = _erosionAmp + (_tuning != null ? _tuning.detailStrength : 0f);
            if (erosionAmp > 0f)
            {
                float band = erosionAmp * 2.5f;
                float falloff = 1f - Mathf.Clamp01(Mathf.Abs(d) / Mathf.Max(0.01f, band));
                if (falloff > 0f)
                {
                    // The shared unit detail field shapes the wall crags; the standalone domain-warp
                    // noise is the fallback when no tuning was supplied.
                    float n = _tuning != null
                        ? TerrainNoiseHelper.DetailUnit(p, _tuning, _seed ^ 0x51A3)
                        : NoiseDistortion.DomainWarpedFbm(p, _settings.erosion * 0.04f + 0.02f, _seed ^ 0x51A3, 4f);
                    d += n * erosionAmp * falloff;
                }
            }

            return d;
        }

        // -------------------------------------------------------------------------
        // The open cavity — chambers + passages + skylights, the cave way.
        // -------------------------------------------------------------------------

        /// <summary>
        /// Signed distance of the connected OPEN CAVITY in cave convention (negative = inside the open
        /// space, positive = solid rock). Every chamber is a vertical capsule of open space, every
        /// passage a capsule, all <see cref="SdfPrimitives.SmoothMin"/>-unioned so the cave is one
        /// connected void. Keep-solid pillar hints push the cavity back so rock columns survive, and
        /// the floor is flattened so the whole cave is walkable.
        /// </summary>
        float CavityDensity(Vector3 p)
        {
            float cavity = 1e6f;
            Vector2 xz = new Vector2(p.x, p.z);

            // Floor roughness — coherent 2D (XZ) noise so the flattened floor has organic bumps.
            float roughness = 0f;
            if (_settings.floorRoughness > 0f)
            {
                float fx = (p.x + _seed * 0.7531f) * 0.32f;
                float fz = (p.z + _seed * 2.1313f) * 0.32f;
                float n = Mathf.PerlinNoise(fx, fz) * 2f - 1f;
                float n2 = (Mathf.PerlinNoise(fx * 2.17f + 13.7f, fz * 2.17f + 7.3f) * 2f - 1f) * 0.5f;
                roughness = (n + n2) / 1.5f * _settings.floorRoughness;
            }

            // Chambers — a vertical capsule of open space from the floor to the chamber's cavity
            // ceiling, with its floor flattened so it is walkable.
            for (int i = 0; i < _plan.Zones.Count; i++)
            {
                var z = _plan.Zones[i];
                Vector3 floorPt = new Vector3(z.Center.x, _plan.FloorY, z.Center.y);
                Vector3 topPt = new Vector3(z.Center.x, _chamberTopY[i], z.Center.y);
                float s = SdfPrimitives.Capsule(p, floorPt, topPt, z.Radius);
                s = SdfPrimitives.ApplyFloorFlatten(
                    s, p, floorPt, 0f, _zoneSlopes[i], roughness, _settings.floorFlattenStrength);
                cavity = SdfPrimitives.SmoothMin(cavity, s, _caveK);
            }

            // Passages — a capsule of open space joining two chambers. The capsule axis runs at
            // roughly head height (floor + radius) so the open space spans floor-to-ceiling-of-tunnel
            // with comfortable headroom, while staying a corridor rather than a full chamber.
            float passageRadius = _settings.corridorWidth * 0.5f;
            for (int e = 0; e < _plan.Edges.Count; e++)
            {
                var edge = _plan.Edges[e];
                Vector2 a = _plan.Zones[edge.FromZone].Center;
                Vector2 b = _plan.Zones[edge.ToZone].Center;
                float axisY = _plan.FloorY + passageRadius;
                Vector3 a3 = new Vector3(a.x, axisY, a.y);
                Vector3 b3 = new Vector3(b.x, axisY, b.y);
                float s = SdfPrimitives.Capsule(p, a3, b3, passageRadius);
                // Flatten the passage floor against an anchor on the segment so the corridor bottom
                // is a level, walkable plane rather than a rounded tube bottom.
                Vector3 anchor = ClosestOnSegment(p, a3, b3);
                Vector3 floorAnchor = new Vector3(anchor.x, _plan.FloorY, anchor.z);
                s = SdfPrimitives.ApplyFloorFlatten(
                    s, p, floorAnchor, 0f, Vector2.zero, roughness * 0.7f, _settings.floorFlattenStrength);
                cavity = SdfPrimitives.SmoothMin(cavity, s, _caveK);
            }

            // Skylights — vertical shafts of open space carved up through a canopied chamber's
            // surviving rock roof. Unioned into the cavity so the carve removes the roof rock there.
            for (int s = 0; s < _plan.Skylights.Count; s++)
            {
                var sky = _plan.Skylights[s];
                float top = _chamberTopY[sky.ZoneId];
                float shaft = SkylightShaft(xz, p.y, sky, top);
                cavity = SdfPrimitives.SmoothMin(cavity, shaft, _caveK * 0.6f);
            }

            // Keep-solid pillar hints — INTERSECT the cavity with the OUTSIDE of a protected column
            // so a vertical pillar of cave rock survives the carve. cave-convention intersection is
            // SmoothMax: max(cavity, -pillarColumn) keeps open space only where it is also outside
            // the column. The surviving rock between near-touching columns becomes an arch.
            for (int i = 0; i < _plan.Pillars.Count; i++)
            {
                float column = PillarColumn(p, _plan.Pillars[i]);
                cavity = -SdfPrimitives.SmoothMin(-cavity, -column, _caveK * 0.5f);
            }

            return cavity;
        }

        // -------------------------------------------------------------------------
        // Keep-solid pillar columns
        // -------------------------------------------------------------------------

        /// <summary>
        /// Cave-convention SDF of one keep-solid pillar column: a tapered vertical capsule. Negative
        /// inside the protected rock core, positive outside. The cavity carve is intersected against
        /// the OUTSIDE of this so the core is never opened up — a free-standing rock pillar survives.
        /// </summary>
        static float PillarColumn(Vector3 p, ArchingCavePillar pillar)
        {
            Vector3 foot = new Vector3(pillar.Center.x, pillar.FootY - 3f, pillar.Center.y);
            Vector3 top = new Vector3(pillar.Center.x, pillar.TopY + 2f, pillar.Center.y);
            float span = Mathf.Max(1f, top.y - foot.y);
            float t = Mathf.Clamp01((p.y - foot.y) / span);
            float radius = pillar.BaseRadius * Mathf.Lerp(1f, 1f - 0.7f * pillar.Taper, t);
            return SdfPrimitives.Capsule(p, foot, top, Mathf.Max(0.5f, radius));
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        /// <summary>Cave-convention SDF of one skylight shaft: a vertical cylinder of open space from
        /// the chamber's cavity top up well past the rock ceiling, so the roof is pierced through.</summary>
        float SkylightShaft(Vector2 xz, float py, ArchingCaveSkylight sky, float chamberTop)
        {
            float horiz = Vector2.Distance(xz, sky.Center) - sky.Radius;
            // The shaft spans from a little below the chamber top up past the ceiling — a finite
            // vertical extent so it does not open the rock below the chamber.
            float lowY = chamberTop - sky.Radius;
            float highY = _plan.CeilingY + (_plan.CeilingY - _plan.FloorY) * 0.4f;
            float midY = (lowY + highY) * 0.5f;
            float vert = Mathf.Abs(py - midY) - (highY - lowY) * 0.5f;
            return Mathf.Max(horiz, vert);
        }

        /// <summary>Smooth maximum (intersection of two SDFs) — the soft-blended counterpart of
        /// <see cref="SdfPrimitives.SmoothMin"/>, so rock walls meet the cavity without a hard cut.</summary>
        static float SmoothMax(float a, float b, float k)
        {
            return -SdfPrimitives.SmoothMin(-a, -b, k);
        }

        /// <summary>Nearest point on the 3D segment a→b to <paramref name="p"/> — the floor anchor.</summary>
        static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len2 = Vector3.Dot(ab, ab);
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
            return a + ab * t;
        }
    }
}
