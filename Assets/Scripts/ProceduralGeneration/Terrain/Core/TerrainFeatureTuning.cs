using UnityEngine;

/// <summary>
/// Which procedural noise function shapes a terrain feature's surface. Mirrors the cave system's
/// <c>CaveNoiseType</c> so the two systems read the same and share <see cref="NoiseDistortion"/>.
/// </summary>
public enum TerrainNoiseType
{
    /// <summary>Classic perlin fbm. Smooth, rolling, slightly repetitive. Soft sand and dunes.</summary>
    Perlin,
    /// <summary>Perlin warped by a second noise layer. Swirling, wind-carved, alien. Eroded rock.</summary>
    DomainWarped,
    /// <summary>Voronoi/cellular cracks. Chunky, broken, blocky. Shattered mesa rock.</summary>
    Cellular,
}

/// <summary>
/// Shared, serializable tuning block carried by every <see cref="FeatureContext"/>. These are the
/// four parameters the project lead mandated across all nine terrain features — noise, overlap,
/// height and jaggedness — plus walkability controls so NavMeshAgents can traverse what they should.
///
/// A concrete feature is free to ADD its own extra serialized fields on a per-feature settings
/// class, but it must READ its core knobs from here so the spawner inspector and the nine features
/// stay consistent. Treat this struct as the lowest common denominator contract.
///
/// Determinism: this struct holds no RNG state — only plain values. Same values + same seed always
/// yields identical output. Do not add anything mutable or frame-dependent here.
/// </summary>
[System.Serializable]
public class TerrainFeatureTuning
{
    // -------------------------------------------------------------------------
    // Noise — how much organic surface variation, and at what spatial scale.
    // -------------------------------------------------------------------------

    [Header("Noise")]
    [Tooltip("Which noise function shapes the feature surface. Perlin = soft rolling, DomainWarped = wind-carved, Cellular = broken rock.")]
    public TerrainNoiseType noiseType = TerrainNoiseType.Perlin;

    [Tooltip("How strongly noise displaces the feature surface, in metres. 0 = perfectly smooth analytic shape.")]
    [Range(0f, 30f)] public float noiseAmount = 4f;

    [Tooltip("Spatial scale of the noise (larger value = smaller, busier detail; smaller value = broad swells).")]
    [Range(0.005f, 0.5f)] public float noiseScale = 0.04f;

    [Tooltip("DomainWarped only: how far the warp noise displaces sample coordinates. 0 = identical to Perlin.")]
    [Range(0f, 12f)] public float domainWarpStrength = 3f;

    // -------------------------------------------------------------------------
    // Overlap — how the feature blends with the surrounding terrain and with
    // neighbouring features. Drives the edge falloff width.
    // -------------------------------------------------------------------------

    [Header("Overlap & blending")]
    [Tooltip("Width (metres) of the falloff band at the feature's footprint edge. Larger = the feature fades into the terrain more gradually, so overlapping features merge smoothly.")]
    [Range(0f, 40f)] public float overlap = 8f;

    [Tooltip("Falloff curve across the overlap band. Left edge = footprint boundary (0), right edge = footprint interior (1). Use this to make valleys narrow with steep walls, or mesas with soft shoulders.")]
    public AnimationCurve overlapFalloff = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // -------------------------------------------------------------------------
    // Height — the dominant vertical scale of the feature.
    // -------------------------------------------------------------------------

    [Header("Height")]
    [Tooltip("Primary vertical extent of the feature in metres. For raised features (dunes, mesas) this is peak height above terrain; for carved features (canyons) it is depth below terrain.")]
    [Range(0f, 200f)] public float height = 20f;

    [Tooltip("Per-feature random variation applied to height, as a fraction of 'height'. 0 = exactly 'height' every time.")]
    [Range(0f, 1f)] public float heightVariation = 0.2f;

    // -------------------------------------------------------------------------
    // Jaggedness — how sharp / eroded the surface reads. Distinct from raw
    // noise amount: jaggedness sharpens ridge lines and steepens slopes.
    // -------------------------------------------------------------------------

    [Header("Jaggedness")]
    [Tooltip("How sharp and craggy the surface is. 0 = soft and rounded, 1 = sharp cliffs and knife ridges. Applied as a ridge-sharpening exponent on the noise.")]
    [Range(0f, 1f)] public float jaggedness = 0.3f;

    // -------------------------------------------------------------------------
    // Surface detail — an INDEPENDENT high-frequency detail layer added on top
    // of every feature's surface. This is the central "how bumpy is the rock"
    // control. It is SEPARATE from 'noiseAmount' (the macro shape noise): a
    // feature can be smooth-macro + jagged-detail, or vice versa. The amplitude
    // is an ABSOLUTE metre value (detailStrength) — set it to 0 for a glassy
    // surface, crank it for violently broken rock. Every feature funnels through
    // TerrainNoiseHelper.DetailLayer, so these knobs reach all of them.
    // -------------------------------------------------------------------------

    [Header("Surface detail")]
    [Tooltip("ABSOLUTE displacement of the detail layer, in metres. THE master bumpiness dial. 0 = perfectly smooth surface. 1-3 = naturally bumpy rock. 6+ = harshly broken, craggy rock with deep relief. Independent of 'Noise amount'.")]
    [Range(0f, 15f)] public float detailStrength = 1.5f;

    [Tooltip("Spatial frequency of the detail layer (separate from the macro 'Noise scale'). Larger = finer, busier bumps; smaller = broader lumps. Note: detail finer than ~2x the voxel size cannot survive meshing.")]
    [Range(0.02f, 1f)] public float detailScale = 0.12f;

    [Tooltip("Number of fractal octaves in the detail layer. 1 = a single clean bump frequency. 3-5 stacks progressively finer crags on top, widening the range of bump sizes. Higher = richer, slightly slower bake.")]
    [Range(1, 6)] public int detailOctaves = 4;

    [Tooltip("How strongly each finer octave contributes. 0 = only the base detail frequency (smooth lumps). 0.5 = classic fractal. 0.8+ = fine crags nearly as strong as the base — rough, harsh rock.")]
    [Range(0f, 0.95f)] public float detailRoughness = 0.55f;

    [Tooltip("Frequency multiplier between successive detail octaves. 2 = each octave twice as busy. Higher spreads detail across more scales for a less periodic, more broken look.")]
    [Range(1.6f, 3.2f)] public float detailLacunarity = 2.2f;

    [Tooltip("Crinkles the detail toward sharp eroded ridges and gullies. 0 = soft rounded bumps, 1 = knife-edged crags and deep cracks. Stacks with 'Jaggedness'.")]
    [Range(0f, 1f)] public float detailRidged = 0.3f;

    [Tooltip("Domain-warp applied to the detail layer so the crags swirl and never read as a repeating grid. 0 = plain fractal, higher = more chaotic, wind-carved detail.")]
    [Range(0f, 8f)] public float detailWarp = 2f;

    // -------------------------------------------------------------------------
    // Walkability — keeps NavMeshAgents able to traverse what designers intend.
    // Pairs with the cave system's ApplyFloorFlatten thinking.
    // -------------------------------------------------------------------------

    [Header("Walkability")]
    [Tooltip("When true, the generator flattens/limits slope on the feature's primary walkable surface so NavMeshAgents can climb it. Linear features (canyon paths, ridges, bridges) usually want this on; pure scenery (tall mesa cliffs) wants it off.")]
    public bool keepWalkable = true;

    [Tooltip("Maximum slope of the walkable surface as rise/run. 0.7 ≈ 35°, safely under the default 45° NavMeshAgent limit. Only consulted when keepWalkable is true.")]
    [Range(0.1f, 1.5f)] public float maxWalkableSlope = 0.7f;
}
