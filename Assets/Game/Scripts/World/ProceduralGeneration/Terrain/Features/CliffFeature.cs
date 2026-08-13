using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Per-feature settings for <see cref="CliffFeature"/>. Exposed separately so a designer can
    /// attach these to the spawner and tune independently of the shared <see cref="TerrainFeatureTuning"/>
    /// knobs. All values drive deterministic math — nothing mutable or frame-dependent.
    /// </summary>
    [System.Serializable]
    public class CliffFeatureSettings
    {
        /// <summary>
        /// Fraction of the footprint (or spline half-width) that the steep face occupies. 0.1 = a thin
        /// knife-edge; 0.4 = a broad craggy wall. The remainder is flat walkable top or bottom.
        /// </summary>
        [Range(0.05f, 0.6f)]
        [Tooltip("Fraction of the cross-section width the steep face occupies. Wider = more ramp, narrower = more vertical.")]
        public float faceWidthFraction = 0.18f;

        /// <summary>
        /// Amplitude of the low-frequency perturbation that makes the cliff edge irregular.
        /// 0 = perfectly straight edge line; 1 = very wavy, organic edge.
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("How wavy/organic the cliff-edge line is. 0 = ruler-straight, 1 = heavily eroded.")]
        public float edgeIrregularity = 0.55f;

        /// <summary>
        /// Rock-body shaping for the cliff face. When <see cref="OverhangSettings.enableOverhangs"/> is
        /// off (default) the cliff stays on the cheap heightfield path. When on, the cliff is rebuilt as
        /// a linear <see cref="RockBodySdf"/> wall: its face profile (how far rock extends out from the
        /// edge line) varies with height and along the run, so the face bulges and undercuts as eroded
        /// canyon-wall rock instead of a flat plane with shelves bolted on.
        /// </summary>
        public OverhangSettings overhang = new OverhangSettings();
    }

    /// <summary>
    /// Escarpment (cliff) terrain feature: a height step — flat low ground on one side, raised plateau
    /// on the other, with a steep, dramatically eroded rock face between. The cliff edge follows the
    /// spline <see cref="FeatureContext.Path"/> when a valid path is provided (≥2 points), making it a
    /// freely-winding escarpment. When no path is present it falls back to a straight step across the
    /// box <see cref="FeatureContext.LocalBounds"/>, making it an area-wide terrain shelf.
    ///
    /// Shape contract:
    ///   - LOW side: sits on natural ground (weight-blended at the footprint edge).
    ///   - FACE band: steeply rising rock, heavily noised with vertical striations and craggy surface.
    ///   - HIGH side (plateau): raised by <see cref="TerrainFeatureTuning.height"/> above natural ground,
    ///     gently noised so it reads as lived-in ancient rock.
    ///   - The step position is perturbed by low-frequency noise → organic, non-ruler edge.
    ///   - Jaggedness concentrates on the steep face band → dramatic, eroded look.
    /// </summary>
    public sealed class CliffFeature : TerrainFeature
    {
        // -------------------------------------------------------------------------
        // Per-feature settings (may be null when invoked from registry default ctor)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Optional per-spawner override settings. When null the feature falls back to code defaults.
        /// Designers attach a <see cref="CliffFeatureSettings"/> component to the spawner GameObject
        /// and the spawner forwards it; otherwise default values are used.
        /// </summary>
        public CliffFeatureSettings Settings { get; set; }

        // -------------------------------------------------------------------------
        // TerrainFeature contract
        // -------------------------------------------------------------------------

        /// <inheritdoc/>
        public override TerrainFeatureType FeatureType => TerrainFeatureType.Cliff;

        /// <summary>
        /// Dynamic density model: <see cref="TerrainDensityKind.Heightfield"/> (cheap, area-scaled) when
        /// overhangs are disabled, <see cref="TerrainDensityKind.Voxel"/> when enabled. The mesher
        /// actually keys off the returned density's <see cref="ITerrainDensity.IsHeightfield"/>, so this
        /// property is advisory — but it is kept honest so any other consumer sees the true model.
        /// </summary>
        public override TerrainDensityKind DensityKind =>
            Settings != null && Settings.overhang != null && Settings.overhang.enableOverhangs
                ? TerrainDensityKind.Voxel
                : TerrainDensityKind.Heightfield;

        /// <inheritdoc/>
        public override object CreateDefaultSettings() => new CliffFeatureSettings();

        /// <inheritdoc/>
        public override void ApplySettings(object settings)
        {
            Settings = settings as CliffFeatureSettings;
        }

        /// <summary>
        /// Builds the escarpment heightfield density. The height lambda evaluates the lateral distance
        /// from the cliff edge (spline- or box-derived), feeds it through
        /// <see cref="TerrainProfiles.CliffStep"/>, adds face-concentrated erosion noise, and blends
        /// the result into the natural ground at the footprint boundary via
        /// <see cref="TerrainNoiseHelper.OverlapWeight"/>.
        /// </summary>
        public override ITerrainDensity BuildDensity(FeatureContext context)
        {
            Bounds box          = context.LocalBounds;
            TerrainFeatureTuning tuning = context.Tuning;
            int seed            = context.Seed;

            // Resolve per-feature settings, falling back to defaults.
            float faceWidth     = Settings != null ? Settings.faceWidthFraction : 0.18f;
            float edgeIrreg     = Settings != null ? Settings.edgeIrregularity  : 0.55f;

            // Per-feature deterministic height (applies heightVariation jitter).
            float stepHeight    = TerrainNoiseHelper.VariedHeight(tuning.height, tuning, seed);

            // Deterministic low-frequency spatial frequency for the edge wiggle.
            // Salt 3 keeps it independent of the surface-noise seed path.
            float edgeFreq      = 0.015f + TerrainNoiseHelper.Hash01(seed, 3) * 0.01f;

            // Pre-compute box geometry used by the fallback straight-step path.
            Vector2 centreXZ    = new Vector2(box.center.x, box.center.z);
            Vector2 halfXZ      = new Vector2(box.extents.x, box.extents.z);

            // Choose the primary axis for the fallback straight step: step across the LONGER box axis
            // so the cliff spans the feature's width, not depth.
            bool stepAlongX     = box.size.z >= box.size.x;

            // Decide whether to use spline mode.
            var spline          = new FeatureSpline(context.Path);
            bool useSpline      = spline.IsValid;

            // -------------------------------------------------------------------------
            // Height lambda — the only thing this feature implements.
            // -------------------------------------------------------------------------
            System.Func<float, float, float> heightFn = (x, z) =>
            {
                float groundY   = context.LocalGroundHeight(x, z);

                // --- 1. Footprint overlap weight (fades feature into surrounding terrain) ----------
                float distInside = context.FootprintDistanceInside(x, z);
                float weight    = TerrainNoiseHelper.OverlapWeight(distInside, tuning);
                if (weight <= 0f) return groundY;

                // --- 2. Signed lateral distance from the cliff edge line -------------------------
                // lateralT in [-1, 1]: -1 = deep low side, +1 = deep high side.
                float lateralT;

                if (useSpline)
                {
                    // Spline path: signed lateral distance from the centre-line, normalised by the
                    // spline half-width so ±1 maps to the path's designed edge.
                    float hw = Mathf.Max(0.1f, spline.HalfWidth);
                    spline.ClosestParam(new Vector3(x, 0f, z), out float latDist, out _);
                    lateralT = Mathf.Clamp(latDist / hw, -1f, 1f);
                }
                else
                {
                    // Box fallback: step perpendicular to the longer axis.
                    if (stepAlongX)
                    {
                        // Step runs along X, so lateral is Z.
                        lateralT = Mathf.Clamp((z - centreXZ.y) / Mathf.Max(0.1f, halfXZ.y), -1f, 1f);
                    }
                    else
                    {
                        lateralT = Mathf.Clamp((x - centreXZ.x) / Mathf.Max(0.1f, halfXZ.x), -1f, 1f);
                    }
                }

                // --- 3. Edge irregularity: perturb lateralT with low-frequency noise -------------
                // A slow fbm in XZ shifts the apparent cliff-edge position locally, breaking any
                // straight-line appearance without adding high-frequency jaggedness at this stage.
                float edgeWiggle = TerrainNoiseHelper.Fbm(
                    new Vector3(x, 0f, z), edgeFreq, seed + 11, 3);
                // Scale wiggle by edgeIrregularity and the face width so it perturbs the edge
                // position by at most one face-width on either side.
                lateralT += edgeWiggle * edgeIrreg * faceWidth * 2f;
                lateralT  = Mathf.Clamp(lateralT, -1.5f, 1.5f);   // allow slight over/undershoot

                // --- 4. CliffStep profile in [-1, 1] range ------------------------------------
                // edge=0 centres the transition; width is the face-width fraction of the full span.
                float profile   = TerrainProfiles.CliffStep(lateralT, 0f, faceWidth * 2f);

                // --- 5. Compute face-band mask for concentrating erosion on the steep section -----
                // Map lateralT into [0,1] and derive how close we are to the midpoint (0 = edge centre).
                // This is 1 at the face and ~0 on the flats.
                float faceMid   = 0f;                              // edge sits at lateralT = 0
                float faceBandT = Mathf.Clamp01(1f - Mathf.Abs(lateralT - faceMid) / (faceWidth * 2f + 0.01f));
                float faceBoost = faceBandT * faceBandT;           // concentrate at the sheer face

                // --- 6. Surface noise — vertical striations + craggy erosion on the face ----------
                // Use the standard SurfaceNoise (drives tuning.noiseAmount & jaggedness) for the
                // baseline organic surface. Then overlay a higher-frequency striation pass on the face.
                float surfNoise = TerrainNoiseHelper.SurfaceNoise(new Vector3(x, 0f, z), tuning, seed);

                // Striation: tall narrow fbm sampled primarily in X (or Z perpendicular to step) at
                // higher frequency to simulate eroded vertical channels in the rock face.
                Vector3 striationP = stepAlongX || useSpline
                    ? new Vector3(x * 2.5f, 0f, z * 0.4f)        // stretch X = vertical channels
                    : new Vector3(x * 0.4f, 0f, z * 2.5f);
                float striation    = TerrainNoiseHelper.Fbm(striationP, tuning.noiseScale * 1.8f, seed + 97, 4);
                striation          = TerrainNoiseHelper.ApplyJaggedness(striation, tuning.jaggedness);
                // Striations only show on the steep face, scaled by noiseAmount for consistency.
                float faceNoise    = striation * tuning.noiseAmount * faceBoost;

                float totalNoise   = surfNoise + faceNoise;

                // --- 7. Compose final surface height -----------------------------------------
                float raised       = groundY + stepHeight * profile + totalNoise;

                // Blend by footprint weight (fades to groundY at the boundary).
                return Mathf.Lerp(groundY, raised, weight);
            };

            // -------------------------------------------------------------------------
            // Vertical extent for the marching-cubes band.
            // -------------------------------------------------------------------------
            float centreGroundY = context.LocalGroundHeight(box.center.x, box.center.z);
            float noiseMargin   = tuning.noiseAmount + 4f;
            float minY          = centreGroundY - noiseMargin;
            float maxY          = centreGroundY + stepHeight + noiseMargin;
            float bandPadding   = context.VoxelSize * 2f;

            // OVERHANG SWITCH. Disabled (default) → cheap heightfield path, the mesher walks only a
            // thin surface band. Enabled → discard the heightfield and build a LINEAR RockBodySdf — an
            // escarpment wall along the cliff-edge line whose face profile varies with height and along
            // its length, so the face bulges and undercuts. Only the voxel branch pays the full-volume
            // walk cost — the heightfield fast-path is preserved.
            OverhangSettings oh = Settings != null ? Settings.overhang : null;
            if (oh != null && oh.enableOverhangs)
            {
                // Resolve the cliff-edge centre-line. Spline mode → first/last spline points; box
                // fallback → the centre-line across the longer axis.
                Vector2 lineA, lineB;
                if (useSpline)
                {
                    Vector3 a3 = spline.Evaluate(0f);
                    Vector3 b3 = spline.Evaluate(1f);
                    lineA = new Vector2(a3.x, a3.z);
                    lineB = new Vector2(b3.x, b3.z);
                }
                else if (stepAlongX)
                {
                    lineA = new Vector2(box.min.x, centreXZ.y);
                    lineB = new Vector2(box.max.x, centreXZ.y);
                }
                else
                {
                    lineA = new Vector2(centreXZ.x, box.min.z);
                    lineB = new Vector2(centreXZ.x, box.max.z);
                }

                // Nominal perpendicular reach of the wall body: the face-width fraction of the half
                // span perpendicular to the line. Stays inside the footprint at the base; the radius
                // profile lets it bulge OUT higher up (the overhang).
                float perpHalf = useSpline ? Mathf.Max(0.1f, spline.HalfWidth)
                                           : (stepAlongX ? halfXZ.y : halfXZ.x);
                float nominalReach = Mathf.Max(2f, perpHalf * Mathf.Clamp01(faceWidth + 0.25f));

                float bodyGround = centreGroundY;
                float bodySummit = centreGroundY + stepHeight;

                // Volume must contain the widest face bulge plus erosion warp and craggy jaggedness.
                float maxReach = nominalReach * RockBodyProfile.MaxRadiusMultiplier(oh)
                                 + oh.erosion + oh.sideJaggedness + 2f;
                float vMinY = bodyGround - 4f;
                float vMaxY = bodySummit + oh.erosion + oh.sideJaggedness + 2f;
                // Expand the box laterally so the bulging face fits inside the marched bounds.
                Vector3 volCentre = new Vector3(box.center.x, (vMinY + vMaxY) * 0.5f, box.center.z);
                Vector3 volSize = new Vector3(
                    box.size.x + maxReach * 2f, vMaxY - vMinY, box.size.z + maxReach * 2f);
                Bounds volume = new Bounds(volCentre, volSize);

                return new RockBodySdf(
                    lineA, lineB, nominalReach, bodyGround, bodySummit,
                    context.LocalGroundHeight, volume, oh, seed);
            }

            // Coverage mask: only mesh the raised part of the escarpment — the steep face and the
            // high plateau above it. The LOW side of a cliff step sits at natural ground level, so
            // meshing it would just build a flat ground apron, which is not the feature. A column is
            // covered when the footprint overlap weight is non-zero AND the CliffStep profile lifts
            // the surface meaningfully above ground. The face/high side stay; the low side is dropped.
            System.Func<float, float, bool> coverageFn = (x, z) =>
            {
                float distInside = context.FootprintDistanceInside(x, z);
                if (TerrainNoiseHelper.OverlapWeight(distInside, tuning) <= 0f) return false;

                // Re-derive the cliff-step profile (mirrors the height lambda, steps 2–4).
                float lateralT;
                if (useSpline)
                {
                    float hw = Mathf.Max(0.1f, spline.HalfWidth);
                    spline.ClosestParam(new Vector3(x, 0f, z), out float latDist, out _);
                    lateralT = Mathf.Clamp(latDist / hw, -1f, 1f);
                }
                else if (stepAlongX)
                {
                    lateralT = Mathf.Clamp((z - centreXZ.y) / Mathf.Max(0.1f, halfXZ.y), -1f, 1f);
                }
                else
                {
                    lateralT = Mathf.Clamp((x - centreXZ.x) / Mathf.Max(0.1f, halfXZ.x), -1f, 1f);
                }
                float edgeWiggle = TerrainNoiseHelper.Fbm(new Vector3(x, 0f, z), edgeFreq, seed + 11, 3);
                lateralT += edgeWiggle * edgeIrreg * faceWidth * 2f;
                lateralT  = Mathf.Clamp(lateralT, -1.5f, 1.5f);
                float profile = TerrainProfiles.CliffStep(lateralT, 0f, faceWidth * 2f);
                return profile > 0.02f;
            };

            return new HeightfieldDensity(heightFn, box, minY, maxY, bandPadding, coverageFn);
        }
    }
}
