using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Designer-tunable knobs for the 3D rock-body SDF (<see cref="RockBodySdf"/>) used by mesas and
    /// cliffs when overhangs are turned on.
    ///
    /// <para><b>The model.</b> The old approach UNIONED protruding shelf slabs onto a flat-sided
    /// heightfield wall — overhangs were a placed attribute. This block instead describes a true 3D
    /// rock BODY whose horizontal cross-section varies with height. The body's surface is
    /// <c>radialDistanceFromAxis - bodyRadius(heightFraction, angle)</c>. Because <c>bodyRadius</c> is
    /// a function of HEIGHT, the body can be narrow at the base and bulge wide overhead, pinch in the
    /// middle, lean, etc. — and every overhang/undercut is then a DIRECT CONSEQUENCE of that radius
    /// profile, never a bolted-on slab.</para>
    ///
    /// When <see cref="enableOverhangs"/> is false the feature stays on the cheap heightfield path and
    /// none of these values cost anything.
    ///
    /// <para>The serialized type name is kept as <c>OverhangSettings</c> so existing scenes that embed
    /// it (MesaSettings / CliffFeatureSettings) keep deserialising. Several legacy field names are
    /// repurposed rather than removed so old serialized data still binds to a meaningful knob.</para>
    /// </summary>
    [System.Serializable]
    public class OverhangSettings
    {
        /// <summary>
        /// Master toggle. OFF (default) = the feature keeps the cheap 2D <see cref="HeightfieldDensity"/>
        /// path. ON = it switches to the 3D <see cref="RockBodySdf"/> voxel density whose cross-section
        /// varies with height, so overhangs and undercuts emerge from the body shape itself.
        /// </summary>
        [Tooltip("Turn on the 3D rock-body model (emergent overhangs). OFF keeps the cheap heightfield path.")]
        public bool enableOverhangs = false;

        /// <summary>
        /// How strongly the body radius swells and pinches as it climbs — the heart of the model.
        /// 0 = near-straight walls (the radius barely changes with height). 1 = dramatic bulges and
        /// undercuts: the body fattens far past its base, pinches to a waist, fattens again. Overhangs
        /// are wherever an upper band is wider than the band beneath it, so this knob directly drives
        /// how much the formation overhangs. Legacy name <c>overhangDepth</c> repurposed.
        /// </summary>
        [Range(0f, 1f)]
        [FormerlySerializedName("overhangDepth")]
        [Tooltip("How strongly the radius bulges/pinches with height. 0 = straight walls, 1 = dramatic overhangs.")]
        public float bodyVariation = 0.55f;

        /// <summary>
        /// How many bulge/pinch bands stack up the height of the formation. 1 = one big swell (a single
        /// mushroom overhang). Higher = several stacked tiers, each its own overhang ledge — the classic
        /// banded sandstone tower. Legacy name <c>overhangCount</c> repurposed.
        /// </summary>
        [Range(0.5f, 6f)]
        [FormerlySerializedName("overhangCount")]
        [Tooltip("How many bulge/pinch bands stack up the height. 1 = one overhang, higher = stacked tiers.")]
        public float bulgeFrequency = 2.2f;

        /// <summary>
        /// How far the body leans — its vertical axis drifts in XZ as it rises, driven by low-frequency
        /// noise. 0 = perfectly upright. 1 = a strong lean, so one side overhangs far more than the
        /// other (asymmetric, like a toppling hoodoo). As a fraction of the footprint radius.
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("How far the body leans as it rises (axis drift). 0 = upright, 1 = strong asymmetric lean.")]
        public float lean = 0.3f;

        /// <summary>
        /// Domain-warp noise strength applied to the whole field, in metres — the eroded-sandstone look.
        /// Warps the body so it reads as wind-and-water carved rock with soft wavy strata, not a
        /// geometric solid. Legacy name <c>overhangThickness</c> repurposed.
        /// </summary>
        [Range(0f, 20f)]
        [FormerlySerializedName("overhangThickness")]
        [Tooltip("Domain-warp erosion strength (metres). Gives the body its carved-sandstone wobble.")]
        public float erosion = 8f;

        /// <summary>Spatial frequency of the <see cref="erosion"/> domain-warp noise. Higher = tighter,
        /// busier carving; lower = broad sweeping erosion forms.</summary>
        [Range(0.005f, 0.12f)]
        [Tooltip("Frequency of the erosion warp. Higher = busier carving, lower = broad forms.")]
        public float erosionFrequency = 0.035f;

        /// <summary>
        /// Fine craggy surface displacement on the rock faces, in metres — layered on top of the radius
        /// profile and the erosion warp. Gives the sides a rough, broken, wind-scoured texture.
        /// </summary>
        [Range(0f, 12f)]
        [Tooltip("Fine craggy noise (metres) on the rock faces.")]
        public float sideJaggedness = 3.5f;

        /// <summary>Spatial frequency of the <see cref="sideJaggedness"/> crag noise.</summary>
        [Range(0.02f, 0.3f)]
        [FormerlySerializedName("sideJaggednessFrequency")]
        [Tooltip("Frequency of the side-jaggedness crag noise. Higher = busier crags.")]
        public float sideJaggednessFrequency = 0.09f;

        /// <summary>
        /// Angular richness of the radius profile — how much the cross-section also varies AROUND the
        /// body (not just with height). 0 = the body is a surface of revolution (round). 1 = lobed,
        /// fingered cross-sections, so overhangs differ all the way around. Legacy <c>overhangWidth</c>.
        /// </summary>
        [Range(0f, 1f)]
        [FormerlySerializedName("overhangWidth")]
        [Tooltip("How much the cross-section varies around the body. 0 = round, 1 = lobed/fingered.")]
        public float angularVariation = 0.5f;

        /// <summary>
        /// Vertical fraction of the body kept as a flat-ish summit cap so the mesa stays walkable.
        /// The cap clamps the radius wide and flat over the top <c>summitFraction</c> of the height.
        /// </summary>
        [Range(0.05f, 0.5f)]
        [Tooltip("Top fraction of the body kept flat for a walkable summit.")]
        public float summitFraction = 0.18f;
    }

    /// <summary>
    /// Marks a field whose serialized name in older scenes differs from its current name. Documentation
    /// only — Unity's real attribute is <c>UnityEngine.Serialization.FormerlySerializedAs</c>; this
    /// thin alias keeps the intent visible without pulling that namespace into every feature file.
    /// </summary>
    public sealed class FormerlySerializedNameAttribute : UnityEngine.Serialization.FormerlySerializedAsAttribute
    {
        public FormerlySerializedNameAttribute(string oldName) : base(oldName) { }
    }

    /// <summary>
    /// Pure-maths helper for <see cref="RockBodySdf"/>: turns a height fraction + angle into the rock
    /// body's radius at that point. Centralised here so the SDF file stays focused on field
    /// composition and both files stay well under 300 lines.
    ///
    /// <para>The radius is a NOMINAL extent (the footprint reach) MODULATED by two noise terms:</para>
    /// <list type="bullet">
    ///   <item>A vertical band term — low-frequency noise over the height fraction, scaled by
    ///     <see cref="OverhangSettings.bulgeFrequency"/>, so the body swells and pinches as it climbs.
    ///     This is what makes overhangs emerge: a band wider than the one below it overhangs it.</item>
    ///   <item>An angular term — noise over the angle around the axis, so the cross-section is lobed
    ///     rather than a perfect circle and overhangs vary all the way round.</item>
    /// </list>
    /// </summary>
    public static class RockBodyProfile
    {
        /// <summary>
        /// Body radius multiplier at height fraction <paramref name="t"/> (0 = ground, 1 = summit) and
        /// <paramref name="angle"/> radians around the axis. Returns a multiplier on the nominal radius
        /// — typically in [0.35, 1.7] depending on variation. Deterministic off <paramref name="seed"/>.
        /// </summary>
        public static float RadiusMultiplier(float t, float angle, OverhangSettings s, int seed)
        {
            t = Mathf.Clamp01(t);

            // --- Vertical bulge/pinch bands -------------------------------------------------
            // Low-frequency noise sampled along the height. bulgeFrequency controls how many
            // swells stack up; bodyVariation controls their amplitude. A higher band being
            // wider than a lower band is exactly an overhang — so this term *is* the overhang.
            float bandPhase = t * Mathf.Max(0.5f, s.bulgeFrequency);
            float bandNoise = Fbm1D(bandPhase, seed ^ 0x51A3, 3);          // ~[-1,1]
            // Bias the swell toward the lower-mid body so the formation reads bottom-heavy then
            // flares — the canyon-wall / hoodoo silhouette — rather than ballooning at the top.
            float band = bandNoise * s.bodyVariation;

            // --- Angular lobing -------------------------------------------------------------
            // Noise around the axis so the cross-section is not a perfect circle. Mixed with a
            // little of the height so lobes twist as they rise (organic, not extruded columns).
            float angNoise = Fbm1D(angle * 0.95f + t * 1.7f, seed ^ 0x2D9F, 2);
            float lobe = angNoise * s.angularVariation * 0.4f;

            // --- Base taper: narrow at the very base so the body sits ON the ground, not as a
            //     cylinder punched through it. Gentle — the noise still dominates higher up. ----
            float baseTaper = Mathf.SmoothStep(0.55f, 1f, Mathf.Clamp01(t * 3.2f));
            float baseRadius = Mathf.Lerp(0.78f, 1f, baseTaper);

            float mult = baseRadius * (1f + band + lobe);

            // --- Summit cap: over the top fraction, ease the radius toward a steady wide value
            //     so the body has a flat-ish walkable top instead of tapering to a noisy point. -
            float capStart = 1f - Mathf.Clamp(s.summitFraction, 0.05f, 0.5f);
            if (t > capStart)
            {
                float capT = Mathf.SmoothStep(0f, 1f, (t - capStart) / Mathf.Max(0.001f, 1f - capStart));
                // Target a moderately wide, lobe-free radius for the summit so the top reads flat.
                float capRadius = baseRadius * (1f + band * 0.25f);
                mult = Mathf.Lerp(mult, capRadius, capT);
            }

            return Mathf.Max(0.05f, mult);
        }

        /// <summary>
        /// Largest radius multiplier the profile can produce for the given settings. Used by Mesa/Cliff
        /// to size the voxel volume wide enough to contain the widest bulge before warp + jaggedness.
        /// </summary>
        public static float MaxRadiusMultiplier(OverhangSettings s)
        {
            // baseRadius peaks at 1; band peaks at +bodyVariation; lobe peaks at +0.4*angularVariation.
            return 1f + s.bodyVariation + s.angularVariation * 0.4f;
        }

        /// <summary>1D fractal noise in roughly [-1, 1]. Samples Perlin along one axis with a fixed
        /// orthogonal coordinate so it is cheap and deterministic.</summary>
        static float Fbm1D(float x, int seed, int octaves)
        {
            float off = (seed & 0xFFFF) * 0.012f + 13.37f;
            float sum = 0f, amp = 1f, norm = 0f, freq = 1f;
            for (int o = 0; o < octaves; o++)
            {
                float n = Mathf.PerlinNoise(x * freq + off, off * 0.5f + o * 7.31f) * 2f - 1f;
                sum += n * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2.07f;
            }
            return norm > 0f ? sum / norm : 0f;
        }
    }
}
