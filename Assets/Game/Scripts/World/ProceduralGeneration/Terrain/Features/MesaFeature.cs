using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Extra per-instance knobs for <see cref="MesaFeature"/>. Attach alongside a
    /// <c>TerrainFeatureSpawner</c> — the spawner hands the <see cref="FeatureContext"/> to
    /// <see cref="MesaFeature.BuildDensity"/>, which reads these values if present in the scene.
    /// All four core knobs (noise, overlap, height, jaggedness) live in
    /// <see cref="TerrainFeatureTuning"/> as mandated; this class only adds mesa-specific extras.
    /// </summary>
    [System.Serializable]
    public class MesaSettings
    {
        /// <summary>
        /// Fraction of the normalised footprint half-span that the steep cliff wall occupies before
        /// the profile becomes flat top. 0.15 = narrow cliff band, very broad summit; 0.45 = tall
        /// sheer walls, narrower summit. Feeds directly into <see cref="TerrainProfiles.Plateau"/>.
        /// </summary>
        [Range(0.05f, 0.6f)]
        public float wallFraction = 0.28f;

        /// <summary>
        /// Amplitude of the organic outline modulation applied to the effective edge distance, as a
        /// fraction of the footprint's shorter half-extent. Positive values break the rectangular
        /// silhouette into a naturally irregular perimeter. 0 = perfectly rectangular footprint.
        /// </summary>
        [Range(0f, 0.35f)]
        public float outlineWarpStrength = 0.18f;

        /// <summary>
        /// Frequency (world-space, relative to 1 m) of the outline warp noise. Higher = finer
        /// indentations; lower = broad lobes. Works alongside <see cref="outlineWarpStrength"/>.
        /// </summary>
        [Range(0.005f, 0.12f)]
        public float outlineWarpFrequency = 0.03f;

        /// <summary>
        /// Amplitude of the additional vertical erosion / striation noise layered onto the cliff
        /// walls only, in metres. Gives the steep face a craggy, banded, wind-scoured look.
        /// Blended in proportion to how much of the wall band we are in — zero on the flat top.
        /// </summary>
        [Range(0f, 12f)]
        public float wallStriationStrength = 5f;

        /// <summary>
        /// Spatial frequency of the vertical erosion striations on the cliff face.
        /// Higher = tight horizontal bands; lower = broad erosion swells.
        /// </summary>
        [Range(0.01f, 0.3f)]
        public float wallStriationFrequency = 0.09f;

        /// <summary>
        /// Amplitude of the subtle height variation on the flat summit, in metres. Keeps the top
        /// walkable but not unnaturally perfect — a slight undulation of ancient rock.
        /// </summary>
        [Range(0f, 4f)]
        public float summitRippleStrength = 0.8f;

        /// <summary>
        /// Rock-body shaping for the mesa. When <see cref="OverhangSettings.enableOverhangs"/> is off
        /// (default) the mesa stays on the cheap heightfield path. When on, the mesa is rebuilt as a 3D
        /// <see cref="RockBodySdf"/> whose horizontal cross-section varies with height, so it bulges,
        /// pinches and overhangs as a direct consequence of its body shape — eroded desert rock rather
        /// than a flat wall with shelves bolted on.
        /// </summary>
        public OverhangSettings overhang = new OverhangSettings();

        /// <summary>
        /// Macro-scale elevation relief sculpted onto the flat summit (the "plateau" surface). This is
        /// the big sibling of <see cref="summitRippleStrength"/>: where that knob adds a faint walkable
        /// wobble, this block produces real, designed elevation differences across the top — a tilted
        /// table, a couple of broad hills, a high end and a low end. Applies to BOTH the heightfield
        /// mesa and the overhang rock-body mesa, so the look stays consistent when overhangs toggle.
        /// </summary>
        public MesaPlateauSettings plateau = new MesaPlateauSettings();
    }

    /// <summary>
    /// Runnable knobs for the macro elevation profile of a mesa's flat top. Separated from
    /// <see cref="MesaSettings"/> so the inspector groups it as one block, and so both the
    /// heightfield path and the overhang <see cref="RockBodySdf"/> path can consume the same values.
    ///
    /// <para>The relief is the sum of two terms, both weighted by how deep into the summit a point is
    /// (zero at the cliff rim, full deep on the plateau) so the cliff edge is never disturbed:</para>
    /// <list type="bullet">
    ///   <item>A <b>tilt</b> term — a single planar slope across the whole plateau, so one side of the
    ///     mesa top sits higher than the other.</item>
    ///   <item>A <b>relief</b> term — fractal noise giving broad hills and basins; its character runs
    ///     from soft swells to sharp ridges via <see cref="elevationRidged"/>.</item>
    /// </list>
    /// </summary>
    [System.Serializable]
    public class MesaPlateauSettings
    {
        /// <summary>
        /// Peak-to-trough vertical span of the relief sculpted onto the summit, in metres. 0 = a dead
        /// flat plateau (legacy behaviour). 8–20 m gives a clearly undulating tabletop with real high
        /// and low ground; larger values turn the top into rugged upland.
        /// </summary>
        [Range(0f, 60f)]
        [Tooltip("Peak-to-trough height difference sculpted onto the flat top, in metres. 0 = perfectly flat.")]
        public float elevationStrength = 0f;

        /// <summary>
        /// Spatial frequency of the relief noise. Low (~0.01) = one or two broad swells spanning the
        /// whole plateau; high (~0.06) = many tighter rolling hills. Independent of the cliff/surface
        /// noise scales so the plateau macro-form can be tuned on its own.
        /// </summary>
        [Range(0.004f, 0.08f)]
        [Tooltip("Frequency of the plateau relief. Low = one broad swell, high = many rolling hills.")]
        public float elevationScale = 0.018f;

        /// <summary>
        /// Strength of a single directional slope across the whole plateau, as a fraction of
        /// <see cref="elevationStrength"/>. 0 = no overall lean (relief only). 1 = the full elevation
        /// span is spent on a clean planar tilt, so one mesa edge is high and the opposite edge low.
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("Directional slope across the whole plateau (one edge high, one low), as a fraction of strength.")]
        public float tilt = 0.3f;

        /// <summary>
        /// Compass direction of the <see cref="tilt"/> slope, in degrees (0 = +X high side). Lets the
        /// high end of the tilted plateau be aimed wherever the designer wants.
        /// </summary>
        [Range(0f, 360f)]
        [Tooltip("Direction the plateau tilts toward, in degrees. 0 = +X edge is the high side.")]
        public float tiltDirection = 0f;

        /// <summary>
        /// Shapes the relief noise from soft to sharp. 0 = smooth rounded swells (gentle uplands).
        /// 1 = fully ridged — crisp crests and steep-sided basins, a mesa-on-mesa badland top.
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("0 = soft rounded swells on the plateau, 1 = sharp ridges and steep basins.")]
        public float elevationRidged = 0.2f;
    }

    /// <summary>
    /// Heightfield terrain feature that builds a MESA: a tall, flat-topped mountain with steep
    /// eroded cliff walls and a broad, gently-noisy walkable summit.
    ///
    /// <para><b>Shape approach</b></para>
    /// <list type="bullet">
    ///   <item>The designer-drawn polygon footprint drives the outline via
    ///     <see cref="FeatureContext.FootprintDistanceInside"/>, which returns signed metres inside
    ///     the polygon boundary. This gives the mesa a non-rectangular plateau that exactly follows
    ///     the placed polygon rather than an axis-aligned box.</item>
    ///   <item>The metres-inside distance is normalised to [0, 1] using the shorter polygon
    ///     bounding-box half-extent, then warped by low-frequency noise
    ///     (<see cref="MesaSettings.outlineWarpStrength"/>) for an organic, irregular perimeter.</item>
    ///   <item><see cref="TerrainProfiles.Plateau"/> converts the warped normalised distance into the
    ///     steep-wall→flat-top silhouette: 0 at the edge, rising sharply across the wall fraction,
    ///     then clamped to 1 (the summit).</item>
    ///   <item>Within the wall band, vertical erosion striations are applied via a separate
    ///     <see cref="TerrainNoiseHelper.Fbm"/> pass modulated by wall-band position, giving a
    ///     layered, wind-scoured cliff face.</item>
    ///   <item><see cref="TerrainNoiseHelper.SurfaceNoise"/> with the tuning jaggedness adds
    ///     fine-scale surface crags everywhere; on the summit its amplitude is reduced to preserve
    ///     walkability.</item>
    ///   <item><see cref="TerrainNoiseHelper.OverlapWeight"/> blends the entire feature gracefully
    ///     into the surrounding terrain across the overlap band, fed the raw metres-inside value.</item>
    /// </list>
    ///
    /// <para>All outputs are deterministic off <see cref="FeatureContext.Seed"/>.</para>
    /// </summary>
    public sealed class MesaFeature : TerrainFeature
    {
        /// <inheritdoc/>
        public override TerrainFeatureType FeatureType => TerrainFeatureType.Mesa;

        /// <summary>
        /// Dynamic density model: <see cref="TerrainDensityKind.Heightfield"/> (cheap, area-scaled) when
        /// overhangs are disabled, <see cref="TerrainDensityKind.Voxel"/> when enabled. The mesher
        /// actually keys off the returned density's <see cref="ITerrainDensity.IsHeightfield"/>, so this
        /// property is advisory — but it is kept honest so any other consumer sees the true model.
        /// </summary>
        public override TerrainDensityKind DensityKind =>
            _settings != null && _settings.overhang != null && _settings.overhang.enableOverhangs
                ? TerrainDensityKind.Voxel
                : TerrainDensityKind.Heightfield;

        // Per-instance settings. If no MesaSettings instance is injected, defaults are used.
        private MesaSettings _settings = new MesaSettings();

        /// <summary>
        /// Inject custom settings (e.g. from a spawner companion component or test harness).
        /// If never called the default <see cref="MesaSettings"/> values are used.
        /// </summary>
        public void Configure(MesaSettings settings) => _settings = HealMesaSettings(settings);

        /// <summary>
        /// Called by the spawner to obtain a fresh default settings object for this feature type.
        /// Returns a new <see cref="MesaSettings"/> with all fields at their inspector defaults.
        /// </summary>
        public override object CreateDefaultSettings() => new MesaSettings();

        /// <summary>
        /// Called by the spawner to inject a previously-created settings object.
        /// Casts <paramref name="settings"/> to <see cref="MesaSettings"/> and stores it;
        /// a null or wrong-type value leaves the current settings unchanged.
        /// </summary>
        public override void ApplySettings(object settings)
        {
            if (settings is MesaSettings ms)
                _settings = HealMesaSettings(ms);
        }

        /// <summary>
        /// Null-fills <see cref="MesaSettings"/>'s nested blocks so legacy scenes pick up newly-added
        /// knobs. See <see cref="TerrainFeature.HealSettings"/> for the underlying serialization reason.
        /// </summary>
        public override object HealSettings(object settings)
            => settings is MesaSettings ms ? HealMesaSettings(ms) : settings;

        /// <summary>
        /// Returns a usable <see cref="MesaSettings"/>, repairing nested blocks that come back NULL.
        ///
        /// <para><b>Why this is needed.</b> <c>featureSettings</c> on the spawner is a
        /// <c>[SerializeReference]</c> field. When this class gains a NEW reference-type field
        /// (<see cref="MesaSettings.plateau"/>), Unity reconstructs already-serialized instances via
        /// the serializer, NOT the constructor — so the <c>= new MesaPlateauSettings()</c> field
        /// initialiser never runs and the field deserialises as <c>null</c>. Any Mesa spawner placed
        /// before this field existed would therefore have <c>plateau == null</c>, and the elevation
        /// knobs would silently do nothing. Healing it here guarantees the knobs always exist.</para>
        /// </summary>
        private static MesaSettings HealMesaSettings(MesaSettings s)
        {
            s ??= new MesaSettings();
            s.plateau ??= new MesaPlateauSettings();
            s.overhang ??= new OverhangSettings();
            return s;
        }

        /// <summary>
        /// Macro elevation offset (metres) sculpted onto the plateau surface at world-XZ
        /// (<paramref name="x"/>, <paramref name="z"/>). Used by BOTH the heightfield and overhang
        /// paths so a mesa's top reads the same regardless of which density model built it.
        ///
        /// <para><paramref name="summitFraction"/> is 0 at the cliff rim and 1 deep on the plateau; the
        /// whole offset is scaled by it so the cliff edge is never displaced. The result is centred on
        /// zero (mean ≈ 0) so it raises and lowers the top equally rather than just lifting it.</para>
        /// </summary>
        private static float PlateauElevation(
            float x, float z, float summitFraction, MesaPlateauSettings p, int seed)
        {
            if (p == null || p.elevationStrength <= 0f || summitFraction <= 0f)
                return 0f;

            summitFraction = Mathf.Clamp01(summitFraction);

            // --- Relief term: fractal noise giving broad hills and basins ----------------------
            // Fbm returns ~[-1,1]; recentre and optionally sharpen toward ridges.
            Vector3 np = new Vector3(x, 0f, z);
            float n = TerrainNoiseHelper.Fbm(np, p.elevationScale, seed ^ 0x7E1B3D55, 4);
            if (p.elevationRidged > 0f)
            {
                // Ridge field: 1 - |n| peaks along the zero crossings -> crisp crests. Recentre to
                // ~[-1,1] so it still raises and lowers, then blend by the ridged knob.
                float ridge = (1f - Mathf.Abs(n)) * 2f - 1f;
                n = Mathf.Lerp(n, ridge, p.elevationRidged);
            }
            float relief = n * (1f - p.tilt);

            // --- Tilt term: a single planar slope across the whole plateau ---------------------
            float tilt = 0f;
            if (p.tilt > 0f)
            {
                float rad = p.tiltDirection * Mathf.Deg2Rad;
                // Project XZ onto the tilt direction, scaled small so it stays in roughly [-1,1]
                // across a normal mesa footprint; the strength knob sets the real metres.
                float along = (x * Mathf.Cos(rad) + z * Mathf.Sin(rad)) * 0.01f;
                tilt = Mathf.Clamp(along, -1f, 1f) * p.tilt;
            }

            return (relief + tilt) * p.elevationStrength * summitFraction;
        }

        /// <inheritdoc/>
        public override ITerrainDensity BuildDensity(FeatureContext context)
        {
            Bounds box          = context.LocalBounds;
            TerrainFeatureTuning tuning = context.Tuning;
            // Heal nested [SerializeReference] blocks that legacy scenes deserialise as null.
            MesaSettings s      = _settings = HealMesaSettings(_settings);

            // Deterministic per-feature height with variation applied.
            float mesaHeight = TerrainNoiseHelper.VariedHeight(tuning.height, tuning, context.Seed);

            // Shortest bounding-box half-extent drives the normalisation so the profile is
            // consistent regardless of footprint aspect ratio.
            float halfMin = Mathf.Max(0.01f, Mathf.Min(box.extents.x, box.extents.z));

            // Salt constants so the three noise passes don't correlate.
            int seedOutline    = context.Seed ^ 0x3A7F1C2B;
            int seedStriation  = context.Seed ^ 0x1D8E4F63;
            int seedSummit     = context.Seed ^ 0x5C2A9E17;

            // -----------------------------------------------------------------------
            // Height lambda — the core of the feature.
            // -----------------------------------------------------------------------
            System.Func<float, float, float> heightFn = (x, z) =>
            {
                float groundY = context.LocalGroundHeight(x, z);

                // --- 1. Polygon signed distance from nearest footprint edge ----------
                // Positive = inside the designer-drawn polygon (metres); negative = outside.
                float distInside = context.FootprintDistanceInside(x, z);

                // --- 2. Overlap falloff — drives blending into surrounding terrain --
                // OverlapWeight takes raw metres-inside directly.
                float weight = TerrainNoiseHelper.OverlapWeight(distInside, tuning);
                if (weight <= 0f) return groundY;

                // --- 3. Organic outline warp — perturbs the polygon-derived distance --
                // Low-frequency FBM noise shifts the effective metres-inside value inward or
                // outward, giving the mesa perimeter an irregular, organic look while still
                // following the polygon outline overall.
                float outlineWarp = 0f;
                if (s.outlineWarpStrength > 0f)
                {
                    Vector3 wp = new Vector3(x, 0f, z);
                    outlineWarp = TerrainNoiseHelper.Fbm(wp, s.outlineWarpFrequency, seedOutline, 3);
                    // Scale by shortest half-extent so the warp is proportional to the footprint.
                    outlineWarp *= s.outlineWarpStrength * halfMin;
                }
                float warpedDist = distInside + outlineWarp;

                // Normalise the warped metres-inside to [0,1] where 1 = deep inside the polygon.
                float normDist = Mathf.Clamp01(warpedDist / halfMin);

                // --- 4. Plateau profile: steep wall rising to flat top --------------
                // wallFraction controls how much of normDist is the steep cliff wall.
                float plateau = TerrainProfiles.Plateau(normDist, s.wallFraction);

                // --- 5. Wall erosion striations on the cliff face -------------------
                // Only active in the wall band (normDist < wallFraction).
                // wallBand goes 0 (summit edge) → 1 (base of wall).
                float wallBand = Mathf.Clamp01(1f - normDist / Mathf.Max(0.001f, s.wallFraction));
                float striation = 0f;
                if (s.wallStriationStrength > 0f && wallBand > 0f)
                {
                    // Sample high-frequency FBM on a vector that mixes XZ position and
                    // the wall-band value so the bands are roughly horizontal striations.
                    Vector3 sp = new Vector3(x * s.wallStriationFrequency,
                                             wallBand * 2.5f,           // vertical component
                                             z * s.wallStriationFrequency);
                    float raw = TerrainNoiseHelper.Fbm(sp, 1f, seedStriation, 4);
                    // Apply the tuning jaggedness to sharpen the strata into hard ledges.
                    raw = TerrainNoiseHelper.ApplyJaggedness(raw, tuning.jaggedness);
                    striation = raw * s.wallStriationStrength * wallBand;
                }

                // --- 6. Summit ripple + macro plateau elevation ---------------------
                // summit fraction: 0 at the summit edge, 1 deep inside the flat top.
                float summitFraction = Mathf.Clamp01((normDist - s.wallFraction)
                                                     / Mathf.Max(0.001f, 1f - s.wallFraction));
                float summitNoise = 0f;
                if (s.summitRippleStrength > 0f && summitFraction > 0f)
                {
                    Vector3 rp = new Vector3(x, 0f, z);
                    summitNoise = TerrainNoiseHelper.Fbm(rp, tuning.noiseScale * 0.5f, seedSummit, 2)
                                  * s.summitRippleStrength * summitFraction;
                }
                // Macro elevation relief: real high/low ground sculpted onto the plateau.
                float plateauRelief = PlateauElevation(x, z, summitFraction, s.plateau, context.Seed);

                // --- 7. Global surface noise (crags / jaggedness) -------------------
                // Dampen it on the summit so the top stays walkable.
                float surfNoise = TerrainNoiseHelper.SurfaceNoise(
                    new Vector3(x, 0f, z), tuning, context.Seed);
                // On the summit (plateau ≈ 1) reduce amplitude to 20 %; on walls full amplitude.
                float noiseWallBlend = 1f - Mathf.Clamp01((plateau - 0.85f) / 0.15f) * 0.8f;
                surfNoise *= noiseWallBlend;

                // --- 8. Compose final height ----------------------------------------
                float featureHeight = (plateau * mesaHeight) + striation + summitNoise
                                      + plateauRelief + surfNoise;
                return groundY + featureHeight * weight;
            };

            // Vertical band for the mesher: from well below ground to above the peak.
            float groundAtCentre = context.LocalGroundHeight(box.center.x, box.center.z);
            // Plateau relief can lift the summit by up to its full elevation span — give the
            // mesher band that headroom or the raised hilltops get clipped flat.
            float plateauHeadroom = s.plateau != null ? s.plateau.elevationStrength : 0f;
            float minY = groundAtCentre - 4f;
            float maxY = box.max.y + mesaHeight + tuning.noiseAmount + s.wallStriationStrength
                         + plateauHeadroom + 2f;
            float bandPadding = context.VoxelSize * 2f;

            // OVERHANG SWITCH. Disabled (default) → cheap heightfield path, cost scales with area and
            // the mesher only walks a thin surface band. Enabled → discard the heightfield entirely and
            // build a 3D RockBodySdf: a true rock mass whose cross-section varies with height, so the
            // mesa bulges and overhangs as a consequence of its body shape. Only the voxel branch pays
            // the full-volume walk cost — the heightfield fast-path is preserved.
            if (s.overhang != null && s.overhang.enableOverhangs)
            {
                // Nominal reach = the footprint's shorter half-extent. It is no longer the body
                // RADIUS (the base cross-section now follows the footprint polygon, see SetFootprint
                // below) — it is the metric SCALE for how far the height profile pushes the footprint
                // boundary in or out. Keeping it as halfMin preserves the original bulge magnitude.
                float nominalRadius = halfMin;

                // The body sits on the ground and rises to a summit at groundY + the varied height.
                float bodyGround = groundAtCentre;
                float bodySummit = groundAtCentre + mesaHeight;

                // Volume sizing. The base cross-section spans the full footprint (box extents, not
                // halfMin), the height profile can push the boundary OUT by up to
                // (MaxRadiusMultiplier - 1) × nominal, and erosion warp + craggy jaggedness add more.
                // The marched volume must contain all of that on every side or the widest swell or a
                // long footprint axis gets clipped flat.
                float outwardBulge = (RockBodyProfile.MaxRadiusMultiplier(s.overhang) - 1f)
                                     * nominalRadius;
                float outlineWarpReach = s.outlineWarpStrength * halfMin;
                float lateralMargin = outwardBulge + outlineWarpReach
                                      + s.overhang.erosion + s.overhang.sideJaggedness + 2f;
                // Plateau relief lifts the summit cap by up to its full elevation span — the voxel
                // volume must be tall enough to contain the highest hilltop or it gets clipped.
                float plateauLift = s.plateau != null ? s.plateau.elevationStrength : 0f;
                float vMinY = bodyGround - 4f;
                float vMaxY = bodySummit + s.overhang.erosion + s.overhang.sideJaggedness
                              + plateauLift + 2f;
                Vector3 volCentre = new Vector3(box.center.x, (vMinY + vMaxY) * 0.5f, box.center.z);
                Vector3 volSize = new Vector3(
                    box.size.x + lateralMargin * 2f,
                    vMaxY - vMinY,
                    box.size.z + lateralMargin * 2f);
                Bounds volume = new Bounds(volCentre, volSize);

                RockBodySdf body = new RockBodySdf(
                    new Vector2(box.center.x, box.center.z), nominalRadius,
                    bodyGround, bodySummit, context.LocalGroundHeight,
                    volume, s.overhang, context.Seed);

                // Make the rock body's base cross-section follow the designer-drawn footprint polygon
                // instead of a circle, so footprint Width / Breadth / outline edits — and the mesa's
                // own organic outline warp — actually shape the overhang mesa. Without this the
                // overhang path was a radially-symmetric tower keyed only off the shorter half-extent,
                // and every footprint variable did nothing once overhangs were enabled.
                FeatureContext ctx = context;
                float warpStrength = s.outlineWarpStrength;
                float warpFrequency = s.outlineWarpFrequency;
                int outlineSeed = seedOutline;
                body.SetFootprint((x, z) =>
                {
                    float d = ctx.FootprintDistanceInside(x, z);
                    // Apply the same organic outline warp the heightfield path uses, so the eroded
                    // perimeter reads identically whether or not overhangs are enabled.
                    if (warpStrength > 0f)
                    {
                        float w = TerrainNoiseHelper.Fbm(
                            new Vector3(x, 0f, z), warpFrequency, outlineSeed, 3);
                        d += w * warpStrength * halfMin;
                    }
                    return d;
                });

                // Sculpt the same macro elevation onto the rock-body summit cap so the plateau
                // undulates identically whether or not overhangs are enabled. The summit fraction
                // here is radial: 1 at the axis, fading to 0 at the nominal footprint radius so the
                // relief never disturbs the cliff rim.
                if (s.plateau != null && s.plateau.elevationStrength > 0f)
                {
                    Vector2 axis = new Vector2(box.center.x, box.center.z);
                    float invRadius = 1f / Mathf.Max(0.01f, nominalRadius);
                    int seed = context.Seed;
                    MesaPlateauSettings plateauCfg = s.plateau;
                    body.SetSummitRelief((x, z) =>
                    {
                        float r = Vector2.Distance(new Vector2(x, z), axis) * invRadius;
                        float summitFraction = Mathf.Clamp01(1f - r);
                        return PlateauElevation(x, z, summitFraction, plateauCfg, seed);
                    });
                }

                return body;
            }

            // Coverage mask: the mesa only occupies the columns its footprint+overlap band reaches.
            // Everywhere else is plain ground — meshing it would build a flat apron around the mesa,
            // which is not wanted. A column is covered exactly where the overlap weight is non-zero.
            System.Func<float, float, bool> coverageFn = (x, z) =>
            {
                float distInside = context.FootprintDistanceInside(x, z);
                return TerrainNoiseHelper.OverlapWeight(distInside, tuning) > 0f;
            };

            return new HeightfieldDensity(heightFn, box, minY, maxY, bandPadding, coverageFn);
        }
    }
}
