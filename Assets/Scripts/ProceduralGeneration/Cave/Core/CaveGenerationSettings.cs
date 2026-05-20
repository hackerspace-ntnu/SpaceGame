using UnityEngine;

public enum CaveNoiseType
{
    /// <summary>Classic perlin fbm. Smooth, bubbly, slightly repetitive. Earth-like sandstone.</summary>
    Perlin,
    /// <summary>Perlin warped by a second noise layer. Swirling, chaotic, hard to read at a glance. Alien.</summary>
    DomainWarped,
    /// <summary>Voronoi/cellular cracks. Chunky, broken rock. Volcanic / fresh rockfall.</summary>
    Cellular,
}

/// <summary>
/// Core shape + meshing parameters for a cave. Lives inside a <see cref="CaveProfile"/> alongside
/// decoration / liquid / material sub-configs. Designed so two profiles can share an identical
/// shape config but differ in everything visual/decorative.
/// </summary>
[System.Serializable]
public class CaveGenerationSettings
{
    // -------------------------------------------------------------------------
    // Seed + bounds
    // -------------------------------------------------------------------------

    [Header("Seed & footprint")]
    public int seed = 0;

    [Tooltip("Half-extents (in metres) of the box the cave will be carved inside.")]
    public Vector3 halfExtents = new Vector3(60f, 35f, 60f);

    [Tooltip("Voxel edge length in metres. Smaller = more detail + much slower. 1.0–1.5 is a sweet spot for low-poly.")]
    [Range(0.5f, 3f)] public float voxelSize = 1.25f;

    [Tooltip("How far past the bounds we still sample, to make sure the cave seals against solid rock.")]
    [Range(0f, 8f)] public float boundsPadding = 2f;

    // -------------------------------------------------------------------------
    // Graph generation (rooms + corridors)
    // -------------------------------------------------------------------------

    [Header("Room graph")]
    [Range(4, 40)] public int roomCount = 14;

    [Tooltip("Min/max room radius (sphere SDF radius, metres).")]
    public Vector2 roomRadius = new Vector2(3.5f, 9f);

    [Tooltip("Probability a generated room is 'big' (uses the upper half of roomRadius).")]
    [Range(0f, 1f)] public float bigRoomChance = 0.25f;

    [Tooltip("Maximum distance between two connected rooms (metres).")]
    public float maxCorridorLength = 22f;

    [Tooltip("Minimum number of corridors each room tries to have.")]
    [Range(1, 5)] public int minConnectionsPerRoom = 2;

    [Tooltip("Extra random connections added on top, expressed as a fraction of room count.")]
    [Range(0f, 1f)] public float extraConnectionRatio = 0.25f;

    [Header("Corridor sizing")]
    [Tooltip("Min/max corridor (capsule SDF) radius in metres.")]
    public Vector2 corridorRadius = new Vector2(1.4f, 3.0f);

    [Tooltip("Chance any given corridor is 'wide' (uses upper half of corridorRadius).")]
    [Range(0f, 1f)] public float wideCorridorChance = 0.3f;

    [Header("Verticality")]
    [Tooltip("How much rooms may drift vertically from the ground plane (metres). The graph generator clamps room positions to ±verticalRange.")]
    public float verticalRange = 18f;

    [Tooltip("Bias for how aggressively the random walk pushes new rooms vertically. 0 = mostly flat, 1 = strong up/down jumps every step.")]
    [Range(0f, 1f)] public float verticalWalkBias = 0.55f;

    [Tooltip("Extra random Y jitter (metres) added to each generated room independently of the walk. Gives every room its own elevation rather than chaining off neighbours.")]
    public float perRoomVerticalJitter = 4f;

    [Tooltip("Maximum permitted corridor slope, expressed as |dy|/horizontal distance. 0.6 ≈ 31°, safely under the default NavMeshAgent 45° slope. Pairs that exceed this are skipped or routed via an intermediate elevation.")]
    [Range(0.1f, 1.5f)] public float maxCorridorSlope = 0.6f;

    // -------------------------------------------------------------------------
    // Density field shaping
    // -------------------------------------------------------------------------

    [Header("Organic noise")]
    [Tooltip("Which noise function shapes the walls. Perlin = smooth bubbly, DomainWarped = swirling alien, Cellular = chunky broken rock.")]
    public CaveNoiseType noiseType = CaveNoiseType.Perlin;

    [Tooltip("How strongly 3D noise warps the cave walls. 0 = perfectly smooth SDF primitives.")]
    [Range(0f, 6f)] public float noiseAmplitude = 1.4f;

    [Tooltip("Spatial scale of the noise (larger = bigger blobs).")]
    public float noiseFrequency = 0.08f;

    [Tooltip("DomainWarped only: how strongly the warp noise displaces sample coordinates before the primary noise. 0 = identical to perlin.")]
    [Range(0f, 8f)] public float domainWarpStrength = 2.5f;

    [Tooltip("How smoothly two intersecting primitives blend together. 0 = sharp union, >0 = smoothed.")]
    [Range(0f, 3f)] public float smoothUnionRadius = 1.8f;

    // -------------------------------------------------------------------------
    // Shape modifiers — toggleable large-scale topology variants. Each runs on
    // top of the base graph; turn any combination on/off per cave-profile.
    // -------------------------------------------------------------------------

    [Header("Shape modifier — Cathedral chambers")]
    [Tooltip("Lift the ceiling of BigChamber rooms for dramatic vertical scale without enlarging the footprint.")]
    public bool enableCathedralChambers = false;

    [Tooltip("Extra ceiling height (metres) added to BigChamber rooms when enabled. Stretches the sphere's top hemisphere upward.")]
    [Range(0f, 30f)] public float cathedralCeilingLift = 10f;

    [Header("Shape modifier — Slot canyons")]
    [Tooltip("Some corridors become tall narrow slits — claustrophobic vertical extent, very narrow horizontal radius.")]
    public bool enableSlotCanyons = false;

    [Tooltip("Probability a Tight corridor is upgraded to a slot canyon when enabled.")]
    [Range(0f, 1f)] public float slotCanyonChance = 0.3f;

    [Tooltip("Vertical-stretch multiplier applied to slot canyon corridors (height / horizontal radius).")]
    [Range(1f, 6f)] public float slotCanyonHeightStretch = 2.5f;

    [Header("Shape modifier — Vertical shafts")]
    [Tooltip("Allow near-vertical shaft corridors between vertically-stacked rooms. Currently the slope clamp prevents these — enabling adds a Shaft corridor kind that uses a vertical capsule and skips slope checks.")]
    public bool enableVerticalShafts = false;

    [Tooltip("Maximum probability of inserting a vertical shaft between two vertically-aligned rooms during graph generation.")]
    [Range(0f, 1f)] public float verticalShaftChance = 0.25f;

    [Tooltip("Minimum vertical separation (metres) for two rooms to be eligible as a shaft pair.")]
    public float verticalShaftMinDrop = 8f;

    [Tooltip("Shaft corridor radius (metres). Wider than tight corridors so a player can reasonably climb down.")]
    public float verticalShaftRadius = 2.5f;

    // -------------------------------------------------------------------------
    // Entrance — every cave gets one entrance tunnel: a "thick spaghetti" of
    // uniform-radius segments winding gently downwards from a mouth node at the
    // top of the footprint into the highest cave room. No big chamber — just a
    // small walkable tube. The CaveSpawner hooks the InteriorAnchor + player
    // spawn + dark exit cut to the mouth node.
    // -------------------------------------------------------------------------

    [Header("Entrance")]
    [Tooltip("Generate the dedicated entrance tunnel. Leave on — the transition/spawn system depends on it.")]
    public bool generateEntrance = true;

    [Tooltip("Uniform radius (metres) of every entrance tunnel segment. A 'thick spaghetti' — wide enough to walk, but a tube, not a room.")]
    [Range(1f, 4f)] public float entranceTubeRadius = 1.8f;

    [Tooltip("Number of segments the entrance tube is built from. More = longer, windier spaghetti.")]
    [Range(2, 12)] public int entranceSegmentCount = 5;

    [Tooltip("Length (metres) of each entrance tube segment.")]
    [Range(3f, 14f)] public float entranceSegmentLength = 7f;

    [Tooltip("Downward slope of the entrance tube as |dy|/horizontal — kept low so the tube is comfortably walkable. 0.25 ≈ 14°.")]
    [Range(0.05f, 0.5f)] public float entranceTubeSlope = 0.22f;

    [Tooltip("How much each segment may wander left/right in heading (degrees) — gives the tube its winding spaghetti shape without breaking walkability.")]
    [Range(0f, 70f)] public float entranceTubeWander = 35f;

    // -------------------------------------------------------------------------
    // Floor flattening (critical for NavMesh quality)
    // -------------------------------------------------------------------------

    [Header("Floor shaping")]
    [Tooltip("Enable floor shaping. Without this, marching cubes produces a curved bowl floor that NavMesh struggles with.")]
    public bool flattenFloors = true;

    [Tooltip("How far below the primitive's centre line the 'floor' is anchored. Larger = floors sit lower (more headroom).")]
    [Range(0f, 5f)] public float floorFlattenDepth = 1.5f;

    [Tooltip("How aggressively the floor plane is enforced. 1 = perfectly flat plane, 0 = no flattening at all. Use 0.5–0.8 for a 'mostly flat with bumps' feel.")]
    [Range(0f, 1f)] public float floorFlattenStrength = 0.65f;

    [Tooltip("Maximum directional slope baked into floors (metres of rise per metre of horizontal travel). 0 = level floors, 0.25 = gentle inclines. Each room/corridor gets a random slope direction.")]
    [Range(0f, 0.5f)] public float floorSlopeAmount = 0.18f;

    [Tooltip("Strength of small-scale noise added on top of the floor plane (metres). Gives floors a rough, organic feel without breaking NavMesh.")]
    [Range(0f, 1.5f)] public float floorRoughness = 0.55f;

    [Tooltip("Spatial frequency of the floor roughness noise. Higher = bumpier/more detailed.")]
    public float floorRoughnessFrequency = 0.35f;

    [Tooltip("Hard plane below which the cave is always solid. Stops corridors from punching through the bottom of the world.")]
    public float floorClampY = -16f;

    // -------------------------------------------------------------------------
    // Meshing
    // -------------------------------------------------------------------------

    [Header("Meshing")]
    [Tooltip("Use 32-bit indices — required for large caves (>65k verts).")]
    public bool use32BitIndices = true;

    [Tooltip("Duplicate every vertex per triangle so flat shading kicks in without a custom shader. Mutually exclusive with smoothing — when smoothing > 0 this is ignored and welded smooth normals are used instead.")]
    public bool flatShade = true;

    [Tooltip("Recalculate tangents on the resulting mesh (only needed for normal-mapped materials).")]
    public bool recalculateTangents = false;

    [Header("Mesh smoothing (post-marching-cubes)")]
    [Tooltip("Number of Laplacian smoothing iterations applied after marching cubes. 0 = no smoothing (raw blocky MC output). 2–3 is usually enough to round off voxel artefacts without losing shape.")]
    [Range(0, 8)] public int smoothingIterations = 0;

    [Tooltip("Per-iteration blend factor. 1.0 = fully average to neighbours each pass, lower = gentler. 0.5 is safe.")]
    [Range(0f, 1f)] public float smoothingStrength = 0.5f;

    [Tooltip("Taubin volume preservation — alternates an inflate pass with each shrink pass, so the cave doesn't shrink as it smooths. Recommended on.")]
    public bool smoothingPreserveVolume = true;

    [Tooltip("After smoothing, override normals with the analytic SDF gradient. Gives perfectly smooth shading even with low-poly geometry. Set false to keep triangle-averaged normals (cheaper, but slightly more faceted).")]
    public bool useGradientNormals = false;

    [Tooltip("Finite-difference epsilon (metres) used to estimate the SDF gradient. ~half the voxel size is a good default.")]
    public float gradientEpsilon = 0.5f;

    // -------------------------------------------------------------------------
    // NavMesh
    // -------------------------------------------------------------------------

    [Header("NavMesh")]
    public bool bakeNavMeshOnGenerate = true;
}
