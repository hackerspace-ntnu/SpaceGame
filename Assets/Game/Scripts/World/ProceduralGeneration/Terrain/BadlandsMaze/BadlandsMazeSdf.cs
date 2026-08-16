using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// ====================================================================================
    /// STAGE 3 — THE GLOBAL DENSITY FIELD.
    /// ====================================================================================
    ///
    /// Realises the placed <see cref="BadlandsMazePlan"/> as a single, continuous signed distance
    /// field over the WHOLE maze (negative = solid rock, positive = air, zero = surface — the project
    /// convention). Like <see cref="ArchingCaveSdf"/> it implements <see cref="ITerrainDensity"/> as a
    /// VOXEL field, and the key point is the same: it is GLOBAL — every internal sub-tile (see
    /// <see cref="BadlandsMazeChunker"/>) samples this exact field, so the sub-meshes seam perfectly.
    ///
    /// THE INVERSION FROM ArchingCave. ArchingCave UNIONS rock primitives into empty space.
    /// BadlandsMaze starts from a SOLID rock massif and SUBTRACTS the carved channel network. The
    /// field is built as:
    ///
    ///   1. The MASSIF — the union of every mesa lump (a tapered, optionally overhanging,
    ///      optionally cliff-banded blob), forming one cohesive rock mass.
    ///   2. minus the CARVED VOID — the chambers and meandering channels smooth-subtracted out, down
    ///      to the channel floor. This is the "wide river system that ran through".
    ///   3. plus a FLOOR slab so the carved channels have a solid walkable bottom.
    ///   4. plus the BOULDERS — lumpy ellipsoids smooth-unioned onto the channel floor.
    ///   5. a domain-warp erosion pass carves the whole thing into cohesive water-eroded sandstone.
    ///
    /// PERFORMANCE: primitive lists are precomputed in the plan; <see cref="Sample"/> walks them per
    /// voxel. With the chunker meshing modest sub-tiles and noise kept to a couple of octaves, each
    /// tile bakes quickly. Reuses the cave system's <see cref="SdfPrimitives"/> and
    /// <see cref="NoiseDistortion"/> — no reinvented noise or primitives.
    /// </summary>
    public sealed class BadlandsMazeSdf : ITerrainDensity
    {
        readonly BadlandsMazePlan _plan;
        readonly BadlandsMazeSettings _settings;
        readonly int _seed;
        readonly Bounds _bounds;
        readonly float _erosionAmp;
        readonly float _floorHalfThickness;
        readonly float _channelFloorY;

        /// <summary>Shared feature tuning — captured so the rock erosion runs through the central
        /// <see cref="TerrainNoiseHelper.DetailedNoise"/> and the "Surface detail" dials reach this
        /// feature too. Optional: when null the erosion falls back to its standalone domain-warp noise.</summary>
        TerrainFeatureTuning _tuning;

        /// <summary>Local-space volume the field occupies — the whole maze plus padding.</summary>
        public Bounds Bounds => _bounds;

        /// <summary>Always a voxel field — the maze has genuine overhangs.</summary>
        public bool IsHeightfield => false;

        /// <summary>Captures the shared feature tuning so the rock erosion obeys the central
        /// "Surface detail" dials. Returns <c>this</c> for fluent use right after construction.</summary>
        public BadlandsMazeSdf WithTuning(TerrainFeatureTuning tuning)
        {
            _tuning = tuning;
            return this;
        }

        public BadlandsMazeSdf(BadlandsMazePlan plan, BadlandsMazeSettings settings, int seed, Bounds siteBounds)
        {
            _plan = plan;
            _settings = settings;
            _seed = seed;
            _bounds = siteBounds;
            // Erosion displacement in metres — modest so it carves crags without dissolving thin walls.
            _erosionAmp = settings.erosion * 4.5f;
            _floorHalfThickness = 4f;
            // The channel floor sits channelDepth below the surrounding terrain rim.
            _channelFloorY = plan.FloorY;
        }

        /// <summary>Folded SDF — no single surface height. Returns the rim Y as a sane stub.</summary>
        public float SurfaceHeight(float localX, float localZ) => _plan.RimY;

        /// <summary>
        /// Signed density at a feature-local point. Negative = solid rock, positive = air. This is the
        /// one function every sub-tile shares, which is what makes the chunked sub-meshes seamless.
        /// </summary>
        public float Sample(Vector3 p)
        {
            // Smooth-blend radius — generous so the mesa lumps flow into one cohesive massif.
            float k = 4.5f;

            // --- 1) The rock massif: union of every mesa lump -------------------------------------
            float rock = 1e6f;
            for (int i = 0; i < _plan.Mesas.Count; i++)
            {
                float m = MesaDensity(p, _plan.Mesas[i]);
                rock = SdfPrimitives.SmoothMin(rock, m, k);
            }

            // --- 2) Carve the channel network OUT of the massif ----------------------------------
            // The carved void is a vertical prism following the chamber discs and meandering channels,
            // open from the channel floor upward. Smooth-subtract so the channel walls round into the
            // floor instead of meeting it at a hard crease.
            float voidD = CarvedVoidDensity(p);
            rock = SmoothSubtract(rock, voidD, 3.5f);

            // --- 3) The walkable channel floor slab ----------------------------------------------
            // A solid slab at the floor height, covering the carved-void footprint, so the channels
            // the player walks have a continuous solid bottom rather than an open pit.
            float floor = FloorDensity(p);
            rock = SdfPrimitives.SmoothMin(rock, floor, 3f);

            // --- 4) Boulders — lumpy rocks smooth-unioned onto the channel floor -----------------
            if (_settings.enableBoulders)
            {
                for (int i = 0; i < _plan.Boulders.Count; i++)
                {
                    float b = BoulderDensity(p, _plan.Boulders[i]);
                    rock = SdfPrimitives.SmoothMin(rock, b, 1.4f);
                }
            }

            // --- 5) Domain-warp erosion — carve the whole mass into cohesive eroded sandstone -----
            // Applied only near the iso-band so deep-solid / deep-air regions cannot pop pockets or
            // float shards. Low frequency + a couple of octaves — cheap and cohesive.
            // Amplitude = the feature's own erosion PLUS the shared surface-detail layer, so the
            // central "Surface detail" dials sharpen the maze walls too.
            float erosionAmp = _erosionAmp + (_tuning != null ? _tuning.detailStrength : 0f);
            if (erosionAmp > 0f)
            {
                float band = erosionAmp * 2.5f;
                float falloff = 1f - Mathf.Clamp01(Mathf.Abs(rock) / Mathf.Max(0.01f, band));
                if (falloff > 0f)
                {
                    // The shared unit detail field shapes the wall crags; the standalone domain-warp
                    // noise is the fallback when no tuning was supplied.
                    float n = _tuning != null
                        ? TerrainNoiseHelper.DetailUnit(p, _tuning, _seed ^ 0x2D74)
                        : NoiseDistortion.DomainWarpedFbm(p, _settings.erosion * 0.045f + 0.02f, _seed ^ 0x2D74, 4f);
                    rock += n * erosionAmp * falloff;
                }
            }

            return rock;
        }

        // -------------------------------------------------------------------------
        // Mesas — the rock lumps that survive between the channels.
        // -------------------------------------------------------------------------

        /// <summary>
        /// Density of one mesa, built with the EXACT rock-body model <see cref="MesaFeature"/> uses for
        /// its overhang branch — <see cref="RockBodySdf"/>'s radial-tower sampling. The body's
        /// horizontal radius is a function of HEIGHT and ANGLE via <see cref="RockBodyProfile.RadiusMultiplier"/>,
        /// so it bulges, pinches, leans and undercuts as a direct consequence of its body shape: every
        /// mesa is a genuine overhanging mesa, identical in shaping to a standalone MesaFeature with
        /// overhangs enabled. The maze just supplies the per-mesa axis, footprint radius and top height.
        ///
        /// This mirrors <see cref="RockBodySdf.Sample"/> step-for-step (domain-warp erosion → lean →
        /// radius profile → side crags → summit cap → floor fill) so the result is the same rock.
        /// </summary>
        float MesaDensity(Vector3 p, BadlandsMazeMesa mesa)
        {
            OverhangSettings s = _settings.mesaBody ?? DefaultBody;
            int seed = _seed ^ mesa.Salt;

            // Body vertical extent: foot sunk below the channel floor so it fuses into the floor slab.
            float groundY = _channelFloorY - 6f;
            float summitY = Mathf.Max(mesa.TopY, groundY + 1f);
            float totalHeight = summitY - groundY;

            // --- Domain-warp the sample point so the body reads as eroded sandstone (RockBodySdf) ---
            Vector3 q = p;
            if (s.erosion > 0f)
            {
                float wf = s.erosionFrequency;
                Vector3 warp = new Vector3(
                    NoiseDistortion.Sample(p, wf, seed + 411),
                    NoiseDistortion.Sample(p, wf, seed + 822) * 0.6f,
                    NoiseDistortion.Sample(p, wf, seed + 233));
                q = p + warp * s.erosion;
            }
            float warpedT = (q.y - groundY) / totalHeight;
            float t = (p.y - groundY) / totalHeight;

            // --- Lean: the axis drifts in XZ as the body rises (RockBodySdf) -----------------------
            Vector2 leanOffset = Vector2.zero;
            if (s.lean > 0f)
            {
                float lx = Mathf.PerlinNoise(t * 1.3f + seed * 0.013f, 4.7f) * 2f - 1f;
                float lz = Mathf.PerlinNoise(8.9f, t * 1.3f + seed * 0.017f) * 2f - 1f;
                float leanMag = s.lean * mesa.Radius * 0.9f * Mathf.Clamp01(t);
                leanOffset = new Vector2(lx, lz) * leanMag;
            }

            // --- Horizontal distance from the (leaned) axis + angle around it ----------------------
            Vector2 axis = mesa.Center + leanOffset;
            float dx = q.x - axis.x;
            float dz = q.z - axis.y;
            float horiz = Mathf.Sqrt(dx * dx + dz * dz);
            float angle = Mathf.Atan2(dz, dx);

            // --- Body radius at this height + angle — the heart of the Mesa model ------------------
            float radiusMul = RockBodyProfile.RadiusMultiplier(Mathf.Clamp01(warpedT), angle, s, seed);
            float radius = mesa.Radius * radiusMul;

            float solid = horiz - radius;

            // --- Fine craggy surface detail on the rock faces (RockBodySdf) ------------------------
            if (s.sideJaggedness > 0f)
            {
                float crag = TerrainNoiseHelper.Fbm(
                    new Vector3(p.x, p.y * 0.8f, p.z), s.sideJaggednessFrequency,
                    seed ^ 0x6C1A77B3, 3);
                solid += crag * s.sideJaggedness;
            }

            // --- Cap the top into a flat-ish walkable summit; floor-fill below the foot ------------
            float topCut = p.y - summitY;
            solid = SmoothMax(solid, topCut, totalHeight * 0.06f);

            // Floor fill: solid below the mesa foot so it fuses into the channel-floor slab — but
            // gated to the mesa's own base column. Without the gate every mesa would fill the whole
            // sample volume below ground, meshing a flat apron beyond the massif. Inside the base
            // radius (plus a small skirt) the fill applies; outside it the below-ground space stays
            // air so no apron spreads past the rock.
            float baseRadius = mesa.Radius * RockBodyProfile.RadiusMultiplier(0f, angle, s, seed);
            float footprintSkirt = mesa.Radius * 0.15f + s.sideJaggedness;
            if (horiz <= baseRadius + footprintSkirt)
            {
                float footFill = groundY - p.y;
                solid = Mathf.Min(solid, -footFill);
            }
            return solid;
        }

        /// <summary>Fallback rock-body knobs if a scene predates the <c>mesaBody</c> field — overhangs
        /// on, so the maze still produces overhanging mesas.</summary>
        static readonly OverhangSettings DefaultBody = new OverhangSettings { enableOverhangs = true };

        // -------------------------------------------------------------------------
        // The carved channel network — the void subtracted from the massif.
        // -------------------------------------------------------------------------

        /// <summary>
        /// Density of the carved VOID — the open river system. Positive distance values mean "outside
        /// the void" (so subtracting it leaves rock); negative means "inside the void" (carved away).
        /// The void is a vertical prism: its XZ footprint is the chambers + meandering channels, and
        /// it is open from the channel floor all the way up past the massif top.
        /// </summary>
        float CarvedVoidDensity(Vector3 p)
        {
            Vector2 xz = new Vector2(p.x, p.z);

            // Horizontal distance to the nearest carved void edge (chamber disc or channel ribbon).
            float horiz = 1e6f;
            for (int i = 0; i < _plan.Chambers.Count; i++)
            {
                var c = _plan.Chambers[i];
                horiz = Mathf.Min(horiz, Vector2.Distance(xz, c.Center) - c.Radius);
            }
            for (int i = 0; i < _plan.Channels.Count; i++)
            {
                var ch = _plan.Channels[i];
                // The channel half-width fluctuates along its length so it pinches and flares.
                float along;
                float distToCentre = ChannelDistance(ch, xz, out along);
                float w = ch.HalfWidth * WidthModulation(ch, along);
                horiz = Mathf.Min(horiz, distToCentre - w);
            }

            // The void is open above the channel floor. Below the floor it is NOT void (the floor slab
            // is solid). So clamp: a point under the floor is treated as outside the void.
            float belowFloor = _channelFloorY - p.y;     // >0 below the floor
            // void distance = max(horiz, -above-floor-amount) — only carved where over the footprint
            // AND above the floor. Using belowFloor as a positive "outside" term achieves that.
            return Mathf.Max(horiz, belowFloor);
        }

        /// <summary>
        /// Density of the walkable floor slab — a thin solid band at the channel-floor height covering
        /// the carved-void footprint, so the carved channels have a continuous solid bottom. Without
        /// it, subtracting the void would punch a bottomless pit.
        /// </summary>
        float FloorDensity(Vector3 p)
        {
            Vector2 xz = new Vector2(p.x, p.z);

            // Horizontal coverage: inside any chamber disc or channel ribbon.
            float horiz = 1e6f;
            for (int i = 0; i < _plan.Chambers.Count; i++)
            {
                var c = _plan.Chambers[i];
                horiz = Mathf.Min(horiz, Vector2.Distance(xz, c.Center) - c.Radius);
            }
            for (int i = 0; i < _plan.Channels.Count; i++)
            {
                var ch = _plan.Channels[i];
                float along;
                float distToCentre = ChannelDistance(ch, xz, out along);
                float w = ch.HalfWidth * WidthModulation(ch, along);
                horiz = Mathf.Min(horiz, distToCentre - w);
            }

            // Vertical slab band around the floor height.
            float slabY = Mathf.Abs(p.y - _channelFloorY) - _floorHalfThickness;

            // Solid only where both inside the footprint AND inside the slab Y band.
            return Mathf.Max(slabY, horiz);
        }

        // -------------------------------------------------------------------------
        // Boulders
        // -------------------------------------------------------------------------

        /// <summary>
        /// Density of one boulder — a squashed ellipsoid with FBM-driven surface lumpiness, so it reads
        /// as an eroded rock rather than a smooth ball. Sunk slightly into the floor by its placement.
        /// </summary>
        float BoulderDensity(Vector3 p, BadlandsMazeBoulder b)
        {
            // Ellipsoid distance: divide each axis by its squash so a unit sphere maps to the squashed
            // shape, then rescale by the smallest axis for an approximate metric distance.
            Vector3 d = p - b.Center;
            Vector3 s = b.Squash;
            float r = b.Radius;
            Vector3 norm = new Vector3(d.x / s.x, d.y / s.y, d.z / s.z);
            float ellip = (norm.magnitude - r) * Mathf.Min(s.x, Mathf.Min(s.y, s.z));

            // Lumpiness — faceted erosion on the boulder surface.
            if (_settings.boulderLumpiness > 0f)
            {
                float lump = TerrainNoiseHelper.Fbm(
                    p * 0.6f, 1f, _seed ^ b.Salt, 3);
                ellip += lump * _settings.boulderLumpiness * r * 0.4f;
            }
            return ellip;
        }

        // -------------------------------------------------------------------------
        // Channel geometry helpers
        // -------------------------------------------------------------------------

        /// <summary>
        /// Distance from an XZ point to a channel's meandering centre-line (a quadratic Bezier through
        /// the chamber endpoints and the mid control point), plus the along-line parameter [0,1] of
        /// the nearest point — used to fluctuate the channel width.
        /// </summary>
        float ChannelDistance(BadlandsMazeChannel ch, Vector2 p, out float along)
        {
            Vector2 a = _plan.Chambers[ch.FromChamber].Center;
            Vector2 b = _plan.Chambers[ch.ToChamber].Center;
            const int Segments = 12;
            float best = 1e6f;
            along = 0f;
            Vector2 prev = a;
            for (int i = 1; i <= Segments; i++)
            {
                float t1 = (i - 1) / (float)Segments;
                float t2 = i / (float)Segments;
                Vector2 cur = Bezier(a, ch.Mid, b, t2);
                Vector2 near = ClosestOnSegment(p, prev, cur, out float segT);
                float d = Vector2.Distance(p, near);
                if (d < best)
                {
                    best = d;
                    along = Mathf.Lerp(t1, t2, segT);
                }
                prev = cur;
            }
            return best;
        }

        /// <summary>Smooth width fluctuation along a channel — a couple of sine lobes seeded per
        /// channel, scaled by the channelWidthVariation knob, so the wash pinches and flares.</summary>
        float WidthModulation(BadlandsMazeChannel ch, float along)
        {
            if (_settings.channelWidthVariation <= 0f) return 1f;
            // Seed the phase off the channel endpoints so each channel fluctuates differently.
            float phase = (ch.FromChamber * 7 + ch.ToChamber * 13) * 0.7f;
            float wave = Mathf.Sin(along * Mathf.PI * 3f + phase) * 0.5f
                       + Mathf.Sin(along * Mathf.PI * 7f + phase * 1.7f) * 0.25f;
            return 1f + wave * _settings.channelWidthVariation * 0.6f;
        }

        static Vector2 Bezier(Vector2 a, Vector2 m, Vector2 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * m + t * t * b;
        }

        static Vector2 ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            Vector2 ab = b - a;
            float len2 = Vector2.Dot(ab, ab);
            t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return a + ab * t;
        }

        /// <summary>Smooth subtraction of <paramref name="b"/> from <paramref name="a"/> —
        /// <c>max(a, -b)</c> softened, so the carved channel walls round into the rock.</summary>
        static float SmoothSubtract(float a, float b, float k)
            => -SdfPrimitives.SmoothMin(-a, b, k);

        /// <summary>Smooth maximum (intersection of two SDFs) — the soft-blended counterpart of
        /// <see cref="SdfPrimitives.SmoothMin"/>.</summary>
        static float SmoothMax(float a, float b, float k)
            => -SdfPrimitives.SmoothMin(-a, -b, k);
    }
}
