// Settlement layout "recipe". Create via Assets → Create → Settlement → Robot Settlement Recipe,
// fill in prefab slots & counts, then drag the asset into a RobotSettlementGenerator.
using UnityEngine;

[CreateAssetMenu(fileName = "RobotSettlementRecipe", menuName = "Settlement/Robot Settlement Recipe")]
public class RobotSettlementRecipe : ScriptableObject
{
    [Header("Core building (single, dead center)")]
    public GameObject shieldGeneratorPrefab;

    [Header("Inner ring buildings")]
    public GameObject barracksPrefab;
    public Vector2Int barracksCount = new Vector2Int(3, 8);

    public GameObject ecoHubPrefab;
    public Vector2Int ecoHubCount = new Vector2Int(1, 3);

    [Header("Outer ring buildings")]
    public GameObject satelliteDishPrefab;
    public Vector2Int satelliteDishCount = new Vector2Int(1, 2);

    public GameObject turretPrefab;
    public Vector2Int turretCount = new Vector2Int(2, 3);

    [Header("Scenery")]
    public GameObject[] rockPrefabs;
    public Vector2Int rockCount = new Vector2Int(6, 12);
    public Vector2 rockScaleRange = new Vector2(0.8f, 1.4f);
    [Tooltip("Max allowed terrain elevation difference under a rock, as a fraction of the rock's height. Above this the rock is rejected (terrain too steep to embed cleanly).")]
    [Range(0.1f, 1f)] public float rockMaxTerrainVariance = 0.6f;
    [Tooltip("Max attempts to find a compatible terrain spot for each rock before giving up.")]
    public int rockPlacementAttempts = 8;

    [Header("Robots (patrol groups)")]
    public GameObject[] robotPrefabs;
    public Vector2Int robotGroupCount = new Vector2Int(3, 5);
    public Vector2Int robotsPerGroup = new Vector2Int(3, 5);
    public float robotGroupSpread = 4f;

    [Header("Vehicles (parked, scattered)")]
    public GameObject[] vehiclePrefabs;
    public int vehicleTotal = 10;

    [Header("Layout")]
    [Tooltip("Inner radius for barracks/eco hubs.")]
    public float innerRadius = 22f;
    [Tooltip("Mid radius for satellite dishes.")]
    public float midRadius = 36f;
    [Tooltip("Outer radius for turrets and rocks.")]
    public float outerRadius = 50f;
    [Tooltip("Min spacing between any two structures (footprint clamp).")]
    public float minStructureSpacing = 8f;
    [Tooltip("Building footprint size used for foundation pads & spacing checks.")]
    public float buildingFootprint = 14f;
    [Tooltip("Extra gap added on top of buildingFootprint when checking spacing between buildings.")]
    public float buildingPadding = 2f;
    [Tooltip("Max attempts when searching for a non-overlapping placement.")]
    public int maxPlacementAttempts = 40;

    [Header("Organized Layout")]
    [Tooltip("Place buildings on evenly-spaced angular slots around concentric rings instead of randomly inside annuli. Produces a planned, organized look.")]
    public bool organizedLayout = true;
    [Tooltip("Random radial offset applied per slot when organizedLayout is on. Keep small for a tightly planned look.")]
    public float slotRadialJitter = 0.5f;
    [Tooltip("Random angular offset (degrees) applied per slot when organizedLayout is on.")]
    public float slotAngularJitter = 2f;
    [Tooltip("Rotate each ring building so its forward axis points toward the settlement center.")]
    public bool faceCenter = true;

    [Header("Foundation Pads")]
    [Tooltip("Spawn a thin pad under buildings if the terrain dips this far below the building base.")]
    public float foundationPadThreshold = 0.35f;
    [Tooltip("Extra overhang added on every side of the pad beyond the prefab's actual footprint.")]
    public float foundationPadOverhang = 0.5f;
    [Tooltip("Material for the foundation pads. If null, the cube's default material is used.")]
    public Material foundationPadMaterial;
}
