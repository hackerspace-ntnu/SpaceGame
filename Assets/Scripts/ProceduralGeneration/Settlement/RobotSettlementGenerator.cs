// Drop-in robot settlement generator. Create a RobotSettlementRecipe asset, drag it
// into the `recipe` slot, then right-click the component header → "Generate".
// Spawned objects parent under a "Generated" child so "Clear" wipes them cleanly.
using System.Collections.Generic;
using UnityEngine;

public class RobotSettlementGenerator : MonoBehaviour
{
    [Header("Recipe")]
    public RobotSettlementRecipe recipe;

    [Header("Terrain")]
    [Tooltip("Layers to raycast against when sampling ground height.")]
    public LayerMask terrainMask = ~0;

    [Header("Determinism")]
    public bool useSeed = false;
    public int seed = 0;

    private const string GeneratedRootName = "Generated";

    private struct Placement
    {
        public Vector2 xz;
        public float radius;
    }

    [ContextMenu("Reroll (new seed + generate)")]
    public void Reroll()
    {
        seed = (int)(Random.value * int.MaxValue);
        bool prev = useSeed;
        useSeed = true;
        Generate();
        useSeed = prev;
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        if (!recipe)
        {
            Debug.LogError("[RobotSettlementGenerator] No recipe assigned.", this);
            return;
        }

        Clear();

        if (useSeed) Random.InitState(seed);

        Transform root = new GameObject(GeneratedRootName).transform;
        root.SetParent(transform, worldPositionStays: false);
        root.localPosition = Vector3.zero;

        List<Placement> placed = new();

        // Shield generator: dead center.
        SpawnBuilding(recipe.shieldGeneratorPrefab, Vector2.zero, root, placed);

        int barracks = RandRange(recipe.barracksCount);
        int ecoHubs  = RandRange(recipe.ecoHubCount);
        int dishes   = RandRange(recipe.satelliteDishCount);
        int turrets  = RandRange(recipe.turretCount);

        if (recipe.organizedLayout)
        {
            // Inner ring shares barracks + eco hubs evenly around a single radius.
            var innerEntries = new List<GameObject>();
            for (int i = 0; i < barracks; i++) innerEntries.Add(recipe.barracksPrefab);
            for (int i = 0; i < ecoHubs;  i++) innerEntries.Add(recipe.ecoHubPrefab);
            ShuffleInPlace(innerEntries);
            PlaceRingSlots(innerEntries, recipe.innerRadius, root, placed);

            var midEntries = new List<GameObject>();
            for (int i = 0; i < dishes; i++) midEntries.Add(recipe.satelliteDishPrefab);
            PlaceRingSlots(midEntries, recipe.midRadius, root, placed);

            var outerEntries = new List<GameObject>();
            for (int i = 0; i < turrets; i++) outerEntries.Add(recipe.turretPrefab);
            PlaceRingSlots(outerEntries, recipe.outerRadius, root, placed);
        }
        else
        {
            for (int i = 0; i < barracks; i++)
                TryPlaceBuilding(recipe.barracksPrefab, 0f, recipe.innerRadius, root, placed);
            for (int i = 0; i < ecoHubs; i++)
                TryPlaceBuilding(recipe.ecoHubPrefab, 0f, recipe.innerRadius, root, placed);
            for (int i = 0; i < dishes; i++)
                TryPlaceBuilding(recipe.satelliteDishPrefab, recipe.innerRadius * 0.8f, recipe.midRadius, root, placed);
            for (int i = 0; i < turrets; i++)
                TryPlaceBuilding(recipe.turretPrefab, recipe.midRadius * 0.9f, recipe.outerRadius, root, placed);
        }

        // Scenery rocks.
        if (recipe.rockPrefabs != null && recipe.rockPrefabs.Length > 0)
        {
            int rocks = RandRange(recipe.rockCount);
            for (int i = 0; i < rocks; i++)
            {
                GameObject prefab = recipe.rockPrefabs[Random.Range(0, recipe.rockPrefabs.Length)];
                float scale = Random.Range(recipe.rockScaleRange.x, recipe.rockScaleRange.y);
                TryPlaceRock(prefab, scale, root);
            }
        }

        // Robot patrol groups.
        if (recipe.robotPrefabs != null && recipe.robotPrefabs.Length > 0)
        {
            int groups = RandRange(recipe.robotGroupCount);
            for (int g = 0; g < groups; g++)
            {
                Vector2 groupCenter = RandomPointInAnnulus(recipe.innerRadius * 0.6f, recipe.outerRadius * 0.9f);
                int count = RandRange(recipe.robotsPerGroup);
                for (int r = 0; r < count; r++)
                {
                    Vector2 offset = Random.insideUnitCircle * recipe.robotGroupSpread;
                    GameObject prefab = recipe.robotPrefabs[Random.Range(0, recipe.robotPrefabs.Length)];
                    SpawnAtGround(prefab, groupCenter + offset, root, randomYaw: true, isBuilding: false);
                }
            }
        }

        // Vehicles, parked at random angles around the settlement.
        if (recipe.vehiclePrefabs != null && recipe.vehiclePrefabs.Length > 0)
        {
            for (int i = 0; i < recipe.vehicleTotal; i++)
            {
                Vector2 xz = RandomPointInAnnulus(recipe.innerRadius * 0.5f, recipe.outerRadius);
                GameObject prefab = recipe.vehiclePrefabs[Random.Range(0, recipe.vehiclePrefabs.Length)];
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

    private void PlaceRingSlots(List<GameObject> prefabs, float radius, Transform root, List<Placement> placed)
    {
        int count = 0;
        for (int i = 0; i < prefabs.Count; i++) if (prefabs[i]) count++;
        if (count == 0) return;

        float startAngle = Random.Range(0f, 360f);
        float step = 360f / count;
        int idx = 0;
        for (int i = 0; i < prefabs.Count; i++)
        {
            var prefab = prefabs[i];
            if (!prefab) continue;

            float ang = (startAngle + step * idx + Random.Range(-recipe.slotAngularJitter, recipe.slotAngularJitter)) * Mathf.Deg2Rad;
            float r   = radius + Random.Range(-recipe.slotRadialJitter, recipe.slotRadialJitter);
            Vector2 xz = new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r);

            float clearance = BuildingClearanceRadius(prefab);
            if (IsTooCloseToOthers(xz, clearance, placed))
            {
                // Try a couple of nudges along the ring before giving up on this slot.
                bool resolved = false;
                for (int nudge = 1; nudge <= 4 && !resolved; nudge++)
                {
                    float nudgeAng = (startAngle + step * idx + nudge * step * 0.25f) * Mathf.Deg2Rad;
                    Vector2 nudged = new Vector2(Mathf.Cos(nudgeAng) * r, Mathf.Sin(nudgeAng) * r);
                    if (!IsTooCloseToOthers(nudged, clearance, placed))
                    {
                        xz = nudged;
                        resolved = true;
                    }
                }
                if (!resolved) { idx++; continue; }
            }

            SpawnBuildingAtSlot(prefab, xz, root, placed);
            idx++;
        }
    }

    private void SpawnBuildingAtSlot(GameObject prefab, Vector2 localXZ, Transform root, List<Placement> placed)
    {
        if (!prefab) return;
        Vector3 worldXZ = transform.TransformPoint(new Vector3(localXZ.x, 0f, localXZ.y));
        if (!SampleGround(worldXZ, out float baseY)) return;

        Quaternion rot;
        if (recipe.faceCenter && localXZ.sqrMagnitude > 0.01f)
        {
            // Face the settlement center; snap to the nearest 90° so axis-aligned prefabs still read square.
            float yaw = Mathf.Atan2(-localXZ.x, -localXZ.y) * Mathf.Rad2Deg;
            yaw = Mathf.Round(yaw / 90f) * 90f;
            rot = Quaternion.Euler(0f, yaw, 0f);
        }
        else
        {
            rot = Quaternion.Euler(0f, Random.Range(0, 4) * 90f, 0f);
        }

        Vector3 spawnPos = new Vector3(worldXZ.x, baseY, worldXZ.z);

#if UNITY_EDITOR
        GameObject go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, root);
        go.transform.SetPositionAndRotation(spawnPos, rot);
#else
        GameObject go = Instantiate(prefab, spawnPos, rot, root);
#endif

        Vector2 footprint = GetPrefabFootprint(prefab);
        MaybeAddFoundationPad(go.transform, worldXZ, baseY, root, footprint, rot);
        placed.Add(new Placement { xz = localXZ, radius = BuildingClearanceRadius(prefab) });
    }

    private static void ShuffleInPlace<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void TryPlaceBuilding(GameObject prefab, float minR, float maxR, Transform root, List<Placement> placed)
    {
        if (!prefab) return;
        float radius = BuildingClearanceRadius(prefab);
        for (int attempt = 0; attempt < recipe.maxPlacementAttempts; attempt++)
        {
            Vector2 xz = RandomPointInAnnulus(minR, maxR);
            if (IsTooCloseToOthers(xz, radius, placed)) continue;
            SpawnBuilding(prefab, xz, root, placed);
            return;
        }
    }

    private bool IsTooCloseToOthers(Vector2 xz, float radius, List<Placement> placed)
    {
        for (int i = 0; i < placed.Count; i++)
        {
            float minDist = placed[i].radius + radius;
            if ((placed[i].xz - xz).sqrMagnitude < minDist * minDist) return true;
        }
        return false;
    }

    private void SpawnBuilding(GameObject prefab, Vector2 localXZ, Transform root, List<Placement> placed)
    {
        if (!prefab) return;
        var go = SpawnBuildingAtBaseY(prefab, localXZ, root);
        if (go) placed.Add(new Placement { xz = localXZ, radius = BuildingClearanceRadius(prefab) });
    }

    private readonly Dictionary<GameObject, Vector2> footprintCache = new();

    private Vector2 GetPrefabFootprint(GameObject prefab)
    {
        if (footprintCache.TryGetValue(prefab, out var cached)) return cached;

        // Walk the prefab hierarchy in local space and find the XZ extents of all meshes.
        var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
        if (filters.Length == 0)
        {
            footprintCache[prefab] = new Vector2(recipe.buildingFootprint, recipe.buildingFootprint);
            return footprintCache[prefab];
        }

        Bounds? combined = null;
        Transform prefabRoot = prefab.transform;
        foreach (var mf in filters)
        {
            if (mf.sharedMesh == null) continue;
            Bounds local = mf.sharedMesh.bounds;
            Vector3 c = local.center;
            Vector3 e = local.extents;
            Bounds wb = new Bounds();
            bool init = false;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                Vector3 worldCorner = mf.transform.TransformPoint(corner);
                Vector3 inRoot = prefabRoot.InverseTransformPoint(worldCorner);
                if (!init) { wb = new Bounds(inRoot, Vector3.zero); init = true; }
                else wb.Encapsulate(inRoot);
            }
            if (combined == null) combined = wb;
            else { var cb = combined.Value; cb.Encapsulate(wb); combined = cb; }
        }

        Vector2 size = combined.HasValue
            ? new Vector2(combined.Value.size.x, combined.Value.size.z)
            : new Vector2(recipe.buildingFootprint, recipe.buildingFootprint);

        footprintCache[prefab] = size;
        return size;
    }

    private float BuildingClearanceRadius(GameObject prefab)
    {
        // Use the prefab's longest XZ extent so even after a 90° yaw the building has clearance.
        Vector2 fp = GetPrefabFootprint(prefab);
        float longest = Mathf.Max(fp.x, fp.y);
        float required = longest + recipe.buildingPadding;
        return Mathf.Max(required, recipe.minStructureSpacing) * 0.5f;
    }

    private GameObject SpawnBuildingAtBaseY(GameObject prefab, Vector2 localXZ, Transform root)
    {
        if (!prefab) return null;

        Vector3 worldXZ = transform.TransformPoint(new Vector3(localXZ.x, 0f, localXZ.y));

        if (!SampleGround(worldXZ, out float baseY))
            return null;

        Quaternion rot = Quaternion.Euler(0f, Random.Range(0, 4) * 90f, 0f);
        Vector3 spawnPos = new Vector3(worldXZ.x, baseY, worldXZ.z);

#if UNITY_EDITOR
        GameObject go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, root);
        go.transform.SetPositionAndRotation(spawnPos, rot);
#else
        GameObject go = Instantiate(prefab, spawnPos, rot, root);
#endif

        Vector2 footprint = GetPrefabFootprint(prefab);
        MaybeAddFoundationPad(go.transform, worldXZ, baseY, root, footprint, rot);
        return go;
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

        return go;
    }

    private void TryPlaceRock(GameObject prefab, float scale, Transform root)
    {
        for (int attempt = 0; attempt < Mathf.Max(1, recipe.rockPlacementAttempts); attempt++)
        {
            Vector2 xz = RandomPointInAnnulus(recipe.innerRadius * 0.5f, recipe.outerRadius * 1.15f);
            Vector3 worldXZ = transform.TransformPoint(new Vector3(xz.x, 0f, xz.y));

            // Spawn temporarily to inspect bounds; we'll discard if terrain is unsuitable.
#if UNITY_EDITOR
            GameObject go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, root);
            go.transform.SetPositionAndRotation(new Vector3(worldXZ.x, 0f, worldXZ.z), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
#else
            GameObject go = Instantiate(prefab, new Vector3(worldXZ.x, 0f, worldXZ.z), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), root);
#endif
            go.transform.localScale *= scale;

            if (TryEmbedRockHalfway(go))
                return;

            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }

    private bool TryEmbedRockHalfway(GameObject rock)
    {
        if (!TryGetWorldMeshBounds(rock, out Bounds b))
            return false;

        // Sample terrain on a 5x5 grid across the rock's XZ footprint.
        const int grid = 5;
        float minTerrain = float.PositiveInfinity;
        float maxTerrain = float.NegativeInfinity;
        float sumTerrain = 0f;
        int count = 0;

        for (int gx = 0; gx < grid; gx++)
        {
            for (int gz = 0; gz < grid; gz++)
            {
                float tx = gx / (float)(grid - 1);
                float tz = gz / (float)(grid - 1);
                float x = Mathf.Lerp(b.min.x, b.max.x, tx);
                float z = Mathf.Lerp(b.min.z, b.max.z, tz);
                if (TryGetTerrainHeight(new Vector3(x, 0f, z), out float y))
                {
                    if (y < minTerrain) minTerrain = y;
                    if (y > maxTerrain) maxTerrain = y;
                    sumTerrain += y;
                    count++;
                }
            }
        }

        // Need terrain coverage across the whole footprint.
        if (count < grid * grid) return false;

        // Reject if terrain is too uneven for the rock to embed cleanly on all sides.
        float rockHeight = b.size.y;
        float variance = maxTerrain - minTerrain;
        if (variance > rockHeight * recipe.rockMaxTerrainVariance) return false;

        // Place center of rock at average terrain height — half buried, every column intersects terrain.
        float avgTerrain = sumTerrain / count;
        float currentCenterY = b.center.y;
        float dy = avgTerrain - currentCenterY;
        rock.transform.position += new Vector3(0f, dy, 0f);
        return true;
    }

    private bool TryGetWorldMeshBounds(GameObject go, out Bounds bounds)
    {
        var filters = go.GetComponentsInChildren<MeshFilter>();
        bounds = default;
        bool init = false;
        foreach (var mf in filters)
        {
            if (mf.sharedMesh == null) continue;
            Bounds local = mf.sharedMesh.bounds;
            Vector3 c = local.center;
            Vector3 e = local.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                Vector3 world = mf.transform.TransformPoint(corner);
                if (!init) { bounds = new Bounds(world, Vector3.zero); init = true; }
                else bounds.Encapsulate(world);
            }
        }
        return init;
    }

    private static bool TryGetTerrainHeight(Vector3 worldPos, out float height)
    {
        Terrain[] terrains = Terrain.activeTerrains;
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            if (terrain == null || terrain.terrainData == null) continue;

            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            float relX = worldPos.x - origin.x;
            float relZ = worldPos.z - origin.z;
            if (relX < 0f || relZ < 0f || relX > size.x || relZ > size.z) continue;

            height = origin.y + terrain.SampleHeight(worldPos);
            return true;
        }

        height = 0f;
        return false;
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

    private void MaybeAddFoundationPad(Transform building, Vector3 worldXZ, float baseY, Transform root, Vector2 prefabFootprint, Quaternion buildingRot)
    {
        // Pad sized to the actual prefab footprint (with a small overhang), oriented to match the building's yaw.
        float padX = prefabFootprint.x + recipe.foundationPadOverhang * 2f;
        float padZ = prefabFootprint.y + recipe.foundationPadOverhang * 2f;
        float halfX = padX * 0.5f;
        float halfZ = padZ * 0.5f;

        Vector3 right = buildingRot * Vector3.right;
        Vector3 forward = buildingRot * Vector3.forward;

        Vector3[] samples =
        {
            worldXZ,
            worldXZ + right * +halfX + forward * +halfZ,
            worldXZ + right * -halfX + forward * +halfZ,
            worldXZ + right * +halfX + forward * -halfZ,
            worldXZ + right * -halfX + forward * -halfZ,
        };

        float minY = baseY;
        for (int i = 0; i < samples.Length; i++)
            if (SampleGround(samples[i], out float y) && y < minY)
                minY = y;

        float dip = baseY - minY;
        if (dip < recipe.foundationPadThreshold) return;

        float padTop = baseY + 0.02f;
        float padBottom = minY - 0.1f;
        float padHeight = padTop - padBottom;
        float padCenterY = (padTop + padBottom) * 0.5f;

        var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pad.name = $"FoundationPad_{building.name}";
        pad.transform.SetParent(root, worldPositionStays: true);
        pad.transform.SetPositionAndRotation(new Vector3(worldXZ.x, padCenterY, worldXZ.z), buildingRot);
        pad.transform.localScale = new Vector3(padX, padHeight, padZ);

        var renderer = pad.GetComponent<MeshRenderer>();
        if (renderer)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (recipe.foundationPadMaterial) renderer.sharedMaterial = recipe.foundationPadMaterial;
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
        if (!recipe) return;
        Vector3 c = transform.position;
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.4f);
        DrawCircle(c, recipe.innerRadius);
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.4f);
        DrawCircle(c, recipe.midRadius);
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.4f);
        DrawCircle(c, recipe.outerRadius);
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
