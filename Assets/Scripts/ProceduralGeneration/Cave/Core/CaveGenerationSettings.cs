using UnityEngine;

/// <summary>
/// All parameters that shape a generated cave. Stored as a plain serialisable class so it can live
/// directly on the <see cref="CaveSpawner"/> MonoBehaviour or be promoted to a ScriptableObject later.
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
    [Tooltip("How strongly 3D noise warps the cave walls. 0 = perfectly smooth SDF primitives.")]
    [Range(0f, 6f)] public float noiseAmplitude = 1.4f;

    [Tooltip("Spatial scale of the noise (larger = bigger blobs).")]
    public float noiseFrequency = 0.08f;

    [Tooltip("How smoothly two intersecting primitives blend together. 0 = sharp union, >0 = smoothed.")]
    [Range(0f, 3f)] public float smoothUnionRadius = 1.8f;

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

    [Tooltip("Duplicate every vertex per triangle so flat shading kicks in without a custom shader.")]
    public bool flatShade = true;

    [Tooltip("Recalculate tangents on the resulting mesh (only needed for normal-mapped materials).")]
    public bool recalculateTangents = false;

    // -------------------------------------------------------------------------
    // NavMesh
    // -------------------------------------------------------------------------

    [Header("NavMesh")]
    public bool bakeNavMeshOnGenerate = true;
}
