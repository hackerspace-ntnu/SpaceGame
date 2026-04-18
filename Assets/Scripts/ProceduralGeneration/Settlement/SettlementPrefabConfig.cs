using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// ScriptableObject holding all prefab references for the ancient ruins generator.
///
/// All prefabs are modelled at 3× scale (tileSize = 1, prefab local scale = 3).
/// Each array holds 1+ variants — builder picks variant % length.
///
/// Create via Assets > Create > Settlement > Prefab Config
/// </summary>
[CreateAssetMenu(fileName = "SettlementPrefabConfig", menuName = "Settlement/Prefab Config")]
public class SettlementPrefabConfig : ScriptableObject
{
    [Header("Scale")]
    [Tooltip("World units per grid tile. Prefabs should be 3× this size.")]
    public float tileSize      = 1f;
    public float slabThickness = 0.1f;

    // ── Horizontal slabs ─────────────────────────────────────────────────────
    [Header("Slabs")]
    public GameObject[] floorVariants         = new GameObject[1];
    public GameObject[] interiorFloorVariants = new GameObject[1];
    public GameObject[] roofVariants          = new GameObject[1];
    public GameObject[] friezeVariants        = new GameObject[1];

    // ── Walls ─────────────────────────────────────────────────────────────────
    [Header("Walls")]
    [Tooltip("Thick blank stone wall — the main surface type.")]
    public GameObject[] wallMonolithVariants  = new GameObject[1];
    [Tooltip("Monolith wall with one tall narrow slit window.")]
    public GameObject[] wallSlitVariants      = new GameObject[1];
    [Tooltip("Ground-floor wall with a massive arch opening.")]
    public GameObject[] wallGrandArchVariants = new GameObject[1];
    [Tooltip("Heavy vertical pier / buttress rib on the wall face.")]
    public GameObject[] wallButtressVariants  = new GameObject[1];
    [Tooltip("Shallow recessed rectangular panel.")]
    public GameObject[] wallPanelVariants     = new GameObject[1];
    [Tooltip("Wall-mounted sci-fi relief / greeble insert.")]
    public GameObject[] wallTechVariants      = new GameObject[1];
    [Tooltip("Vented or mechanical facade insert for large wall planes.")]
    public GameObject[] wallVentVariants      = new GameObject[1];

    // ── Columns ──────────────────────────────────────────────────────────────
    [Header("Columns")]
    [Tooltip("Thin corner column (exterior edge only).")]
    public GameObject[] pillarVariants        = new GameObject[1];
    [Tooltip("Thick free-standing drum column for colonnades.")]
    public GameObject[] colonnadeVariants     = new GameObject[1];
    [Tooltip("Wall-mounted balcony or ledge piece.")]
    public GameObject[] balconyVariants       = new GameObject[1];

    // ── Rooftop ──────────────────────────────────────────────────────────────
    [Header("Rooftop")]
    public GameObject[] roofParapetVariants   = new GameObject[1];
    [Tooltip("Small temple-top structure placed on the highest interior tiles.")]
    public GameObject[] roofTempleVariants    = new GameObject[1];
    [Tooltip("Sci-fi roof machinery cluster for top decks and terraces.")]
    public GameObject[] roofMachineryVariants = new GameObject[1];

    // ── Large structural ─────────────────────────────────────────────────────
    [Header("Large Structural")]
    [Tooltip("Free-standing 3-floor arch spanning a gap between structures.")]
    public GameObject[] grandArchVariants     = new GameObject[1];
    public GameObject[] bridgeVariants        = new GameObject[1];
    [Tooltip("Single arch pier for aqueduct-style elevated spans.")]
    public GameObject[] aqueductArchVariants  = new GameObject[1];

    // ── Traversal ─────────────────────────────────────────────────────────────
    [Header("Traversal")]
    public GameObject[] stairVariants         = new GameObject[1];
    [Tooltip("Wide monumental external approach ramp.")]
    public GameObject[] exteriorRampVariants  = new GameObject[1];

    // ── Environmental accents ─────────────────────────────────────────────────
    [Header("Environmental")]
    [Tooltip("Tall tapered needle obelisk.")]
    public GameObject[] obeliskVariants       = new GameObject[1];
    [Tooltip("Small rock/boulder scatter (uses real rock meshes).")]
    public GameObject[] boulderSmallVariants  = new GameObject[1];
    [Tooltip("Large feature boulder (uses real rock meshes, 3× scale).")]
    public GameObject[] boulderLargeVariants  = new GameObject[1];
    [Tooltip("Uniform local scale applied to every spawned boulder.")]
    public float boulderUniformScale          = 1f;

    [Header("Optimization")]
    [Tooltip("Combine generated settlement visuals into a small number of meshes after spawning.")]
    public bool optimizeAfterBuild            = true;
    [Tooltip("Destroy decorative colliders after building. Keeps traversal collision on major structure pieces.")]
    public bool stripDetailColliders          = true;
    [Tooltip("Bake combined non-convex MeshColliders for structural pieces like walls and floors.")]
    public bool bakeCombinedColliders         = true;
    [Tooltip("Tile span per render/collider chunk. Smaller chunks improve culling.")]
    public int combinedChunkSizeInTiles       = 4;
    [Tooltip("Maximum source mesh count per combined mesh batch.")]
    public int combinedMeshBatchSize          = 256;
    [Tooltip("Maximum source mesh count per baked collider mesh batch.")]
    public int combinedColliderBatchSize      = 192;
    [Tooltip("Index format used for combined meshes.")]
    public IndexFormat combinedMeshIndexFormat = IndexFormat.UInt32;

    // ── Helpers ───────────────────────────────────────────────────────────────

    public GameObject Pick(GameObject[] arr, int variant)
    {
        if (arr == null || arr.Length == 0) return null;
        for (int i = 0; i < arr.Length; i++)
        {
            var v = arr[((variant + i) % arr.Length + arr.Length) % arr.Length];
            if (v != null) return v;
        }
        return null;
    }
}
