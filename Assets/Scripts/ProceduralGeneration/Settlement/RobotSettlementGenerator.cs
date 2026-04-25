// Drop-in robot settlement generator. Place this component anywhere in the world,
// fill in the prefab slots, then right-click the component header → "Generate".
// All spawned objects parent under a "Generated" child so "Clear" wipes them cleanly.
using System.Collections.Generic;
using UnityEngine;

public class RobotSettlementGenerator : MonoBehaviour
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
    public float innerRadius = 35f;
    [Tooltip("Outer radius for satellite dishes.")]
    public float midRadius = 55f;
    [Tooltip("Outer radius for turrets and rocks.")]
    public float outerRadius = 75f;
    [Tooltip("Min spacing between any two structures (footprint clamp).")]
    public float minStructureSpacing = 14f;
    [Tooltip("Building footprint size used for foundation pads & spacing checks.")]
    public float buildingFootprint = 20f;
    [Tooltip("Max attempts when searching for a non-overlapping placement.")]
    public int maxPlacementAttempts = 40;

    [Header("Terrain")]
    [Tooltip("Layers to raycast against when sampling ground height.")]
    public LayerMask terrainMask = ~0;
    [Tooltip("Spawn a thin foundation pad under buildings if the terrain dips this far below the building base.")]
    public float foundationPadThreshold = 0.35f;
    [Tooltip("Foundation pad thickness (Y).")]
    public float foundationPadThickness = 0.5f;
    [Tooltip("Material for the foundation pads (optional). If null, a default unlit grey material is used.")]
    public Material foundationPadMaterial;

    [Header("Determinism")]
    public bool useSeed = false;
    public int seed = 0;

    private const string GeneratedRootName = "Generated";

    [ContextMenu("Generate")]
    public void Generate()
    {
        Clear();

        if (useSeed) Random.InitState(seed);

        Transform root = new GameObject(GeneratedRootName).transform;
        root.SetParent(transform, worldPositionStays: false);
        root.localPosition = Vector3.zero;

        List<Vector2> usedXZ = new();

        // Shield generator: dead center.
        SpawnBuilding(shieldGeneratorPrefab, Vector2.zero, root, usedXZ, isBuilding: true);

        // Inner ring: barracks + eco hubs.
        int barracks = RandRange(barracksCount);
        for (int i = 0; i < barracks; i++)
            TryPlaceBuilding(barracksPrefab, 0f, innerRadius, root, usedXZ);

        int ecoHubs = RandRange(ecoHubCount);
        for (int i = 0; i < ecoHubs; i++)
            TryPlaceBuilding(ecoHubPrefab, 0f, innerRadius, root, usedXZ);

        // Mid ring: satellite dishes (toward outside).
        int dishes = RandRange(satelliteDishCount);
        for (int i = 0; i < dishes; i++)
            TryPlaceBuilding(satelliteDishPrefab, innerRadius * 0.8f, midRadius, root, usedXZ);

        // Outer ring: turrets.
        int turrets = RandRange(turretCount);
        for (int i = 0; i < turrets; i++)
            TryPlaceBuilding(turretPrefab, midRadius * 0.9f, outerRadius, root, usedXZ);

        // Scenery rocks, anywhere from inner ring to past outer (no spacing check — they overlap fine).
        if (rockPrefabs != null && rockPrefabs.Length > 0)
        {
            int rocks = RandRange(rockCount);
            for (int i = 0; i < rocks; i++)
            {
                Vector2 xz = RandomPointInAnnulus(innerRadius * 0.5f, outerRadius * 1.15f);
                GameObject prefab = rockPrefabs[Random.Range(0, rockPrefabs.Length)];
                var go = SpawnAtGround(prefab, xz, root, randomYaw: true, isBuilding: false);
                if (go)
                {
                    float s = Random.Range(rockScaleRange.x, rockScaleRange.y);
                    go.transform.localScale *= s;
                }
            }
        }

        // Robot patrol groups.
        if (robotPrefabs != null && robotPrefabs.Length > 0)
        {
            int groups = RandRange(robotGroupCount);
            for (int g = 0; g < groups; g++)
            {
                Vector2 groupCenter = RandomPointInAnnulus(innerRadius * 0.6f, outerRadius * 0.9f);
                int count = RandRange(robotsPerGroup);
                for (int r = 0; r < count; r++)
                {
                    Vector2 offset = Random.insideUnitCircle * robotGroupSpread;
                    GameObject prefab = robotPrefabs[Random.Range(0, robotPrefabs.Length)];
                    SpawnAtGround(prefab, groupCenter + offset, root, randomYaw: true, isBuilding: false);
                }
            }
        }

        // Vehicles, parked at random angles around the settlement.
        if (vehiclePrefabs != null && vehiclePrefabs.Length > 0)
        {
            for (int i = 0; i < vehicleTotal; i++)
            {
                Vector2 xz = RandomPointInAnnulus(innerRadius * 0.5f, outerRadius);
                GameObject prefab = vehiclePrefabs[Random.Range(0, vehiclePrefabs.Length)];
                SpawnAtGround(prefab, xz, root, randomYaw: true, isBuilding: false);
            }
        }

        Debug.Log($"[RobotSettlementGenerator] Generated settlement under {root.name}.", root);
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name == GeneratedRootName)
            {
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }
    }

    // ── placement helpers ────────────────────────────────────────────────────

    private void TryPlaceBuilding(GameObject prefab, float minR, float maxR, Transform root, List<Vector2> usedXZ)
    {
        if (!prefab) return;
        for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
        {
            Vector2 xz = RandomPointInAnnulus(minR, maxR);
            if (IsTooCloseToOthers(xz, usedXZ)) continue;
            SpawnBuilding(prefab, xz, root, usedXZ, isBuilding: true);
            return;
        }
    }

    private bool IsTooCloseToOthers(Vector2 xz, List<Vector2> usedXZ)
    {
        float minSqr = minStructureSpacing * minStructureSpacing;
        for (int i = 0; i < usedXZ.Count; i++)
            if ((usedXZ[i] - xz).sqrMagnitude < minSqr) return true;
        return false;
    }

    private void SpawnBuilding(GameObject prefab, Vector2 localXZ, Transform root, List<Vector2> usedXZ, bool isBuilding)
    {
        if (!prefab) return;
        var go = SpawnAtGround(prefab, localXZ, root, randomYaw: true, isBuilding: isBuilding);
        if (go) usedXZ.Add(localXZ);
    }

    private GameObject SpawnAtGround(GameObject prefab, Vector2 localXZ, Transform root, bool randomYaw, bool isBuilding)
    {
        if (!prefab) return null;

        Vector3 worldXZ = transform.TransformPoint(new Vector3(localXZ.x, 0f, localXZ.y));

        if (!SampleGround(worldXZ, out float centerY))
            return null;

        Quaternion rot = randomYaw
            ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
            : Quaternion.identity;

        Vector3 spawnPos = new Vector3(worldXZ.x, centerY, worldXZ.z);

#if UNITY_EDITOR
        GameObject go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, root);
        go.transform.SetPositionAndRotation(spawnPos, rot);
#else
        GameObject go = Instantiate(prefab, spawnPos, rot, root);
#endif

        if (isBuilding)
            MaybeAddFoundationPad(go.transform, worldXZ, centerY, root);

        return go;
    }

    private bool SampleGround(Vector3 worldXZ, out float groundY)
    {
        Vector3 origin = new Vector3(worldXZ.x, worldXZ.y + 500f, worldXZ.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2000f, terrainMask, QueryTriggerInteraction.Ignore))
        {
            groundY = hit.point.y;
            return true;
        }
        groundY = worldXZ.y;
        return false;
    }

    // Sample 4 corners of the footprint. If any corner sits more than the threshold below the
    // building's base, drop a flat pad sized to the footprint to bridge the gap.
    private void MaybeAddFoundationPad(Transform building, Vector3 worldXZ, float baseY, Transform root)
    {
        float half = buildingFootprint * 0.5f;
        Vector3[] corners =
        {
            worldXZ + new Vector3(+half, 0f, +half),
            worldXZ + new Vector3(-half, 0f, +half),
            worldXZ + new Vector3(+half, 0f, -half),
            worldXZ + new Vector3(-half, 0f, -half),
        };

        float minY = baseY;
        for (int i = 0; i < corners.Length; i++)
            if (SampleGround(corners[i], out float y) && y < minY)
                minY = y;

        float dip = baseY - minY;
        if (dip < foundationPadThreshold) return;

        float padTop = baseY + 0.02f; // slight overlap so no z-fighting at building base
        float padBottom = minY - 0.1f;
        float padHeight = padTop - padBottom;
        float padCenterY = (padTop + padBottom) * 0.5f;

        var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pad.name = $"FoundationPad_{building.name}";
        pad.transform.SetParent(root, worldPositionStays: true);
        pad.transform.position = new Vector3(worldXZ.x, padCenterY, worldXZ.z);
        pad.transform.localScale = new Vector3(buildingFootprint, padHeight, buildingFootprint);

        var renderer = pad.GetComponent<MeshRenderer>();
        if (renderer)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (foundationPadMaterial) renderer.sharedMaterial = foundationPadMaterial;
        }
    }

    // ── math helpers ─────────────────────────────────────────────────────────

    private static int RandRange(Vector2Int range)
    {
        int min = Mathf.Min(range.x, range.y);
        int max = Mathf.Max(range.x, range.y);
        return Random.Range(min, max + 1);
    }

    private static Vector2 RandomPointInAnnulus(float minR, float maxR)
    {
        float r = Mathf.Sqrt(Random.Range(minR * minR, maxR * maxR));
        float a = Random.Range(0f, Mathf.PI * 2f);
        return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
    }

    // ── gizmos ───────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Vector3 c = transform.position;
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.4f);
        DrawCircle(c, innerRadius);
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.4f);
        DrawCircle(c, midRadius);
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.4f);
        DrawCircle(c, outerRadius);
    }

    private static void DrawCircle(Vector3 center, float radius, int segments = 48)
    {
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float t = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(t) * radius, 0f, Mathf.Sin(t) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
