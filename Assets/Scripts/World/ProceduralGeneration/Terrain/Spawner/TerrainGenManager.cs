using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Folder-level conductor for the terrain-feature system. Drop this on the parent GameObject that
/// holds a folder of <see cref="TerrainFeatureSpawner"/> children — it discovers every spawner in
/// its hierarchy and drives them in bulk, so a designer no longer has to click "Bake &amp; Save"
/// on each feature one at a time.
///
/// The runtime surface here covers generation and configuration: regenerate every feature, spawn
/// the pre-baked meshes, clear the spawned children, and push a shared <see cref="Terrain"/> /
/// feature layer onto every spawner. The actual edit-time MESH BAKING (writing .asset files) lives
/// in <c>TerrainGenManagerEditor</c>, since asset writing is editor-only — that editor calls back
/// into the same <see cref="Spawners"/> list this component exposes.
/// </summary>
[DisallowMultipleComponent]
public class TerrainGenManager : MonoBehaviour
{
    [Header("Discovery")]
    [Tooltip("Include spawners on inactive child GameObjects when collecting the folder.")]
    [SerializeField] private bool includeInactive = true;

    [Header("Bulk terrain reference")]
    [Tooltip("Optional. When 'Apply Terrain To All' is used, this Terrain is pushed onto every " +
             "child spawner's Target Terrain field. Leave null to let each feature fall back to " +
             "the active Terrain at bake time.")]
    [SerializeField] private Terrain sharedTerrain;

    [Tooltip("Layer name pushed onto every child spawner by 'Apply Layer To All'. Must be a layer " +
             "the world NavMeshSurface collects so the features feed the shared NavMesh.")]
    [SerializeField] private string sharedFeatureLayer = "Default";

    [Header("Lifecycle")]
    [Tooltip("On Awake, spawn the pre-baked mesh of every child feature. Turn off if another " +
             "system drives spawning, or if the child spawners already self-spawn on Awake.")]
    [SerializeField] private bool spawnBakedOnAwake = false;

    /// <summary>The Terrain pushed by <see cref="ApplyTerrainToAll"/> (editor / inspector use).</summary>
    public Terrain SharedTerrain { get => sharedTerrain; set => sharedTerrain = value; }

    /// <summary>The layer name pushed by <see cref="ApplyLayerToAll"/>.</summary>
    public string SharedFeatureLayer { get => sharedFeatureLayer; set => sharedFeatureLayer = value; }

    /// <summary>Every <see cref="TerrainFeatureSpawner"/> in this manager's hierarchy (this object
    /// included). Re-scanned on each access so newly added / removed features are always picked up.</summary>
    public List<TerrainFeatureSpawner> Spawners
    {
        get
        {
            var list = new List<TerrainFeatureSpawner>();
            GetComponentsInChildren(includeInactive, list);
            return list;
        }
    }

    void Awake()
    {
        if (spawnBakedOnAwake) SpawnAllBaked();
    }

    // --- Bulk generation -----------------------------------------------------

    /// <summary>Regenerates every child feature live and instantiates the result into the scene
    /// (the <see cref="TerrainFeatureSpawner.GenerateNow"/> path). Returns how many were processed.</summary>
    [ContextMenu("Regenerate All")]
    public int RegenerateAll()
    {
        int count = 0;
        foreach (var spawner in Spawners)
        {
            if (spawner == null) continue;
            spawner.GenerateNow();
            count++;
        }
        Debug.Log($"[TerrainGenManager] regenerated {count} terrain feature(s).");
        return count;
    }

    /// <summary>Instantiates the pre-baked mesh of every child feature that has one. Features with
    /// no baked mesh are skipped (and reported). Returns how many were spawned.</summary>
    [ContextMenu("Spawn All Baked")]
    public int SpawnAllBaked()
    {
        int spawned = 0, missing = 0;
        foreach (var spawner in Spawners)
        {
            if (spawner == null) continue;
            if (spawner.HasBakedMesh) { spawner.SpawnBaked(); spawned++; }
            else missing++;
        }
        if (missing > 0)
            Debug.LogWarning($"[TerrainGenManager] spawned {spawned} baked feature(s); " +
                             $"{missing} had no baked mesh and were skipped.");
        else
            Debug.Log($"[TerrainGenManager] spawned {spawned} baked feature(s).");
        return spawned;
    }

    /// <summary>Destroys the spawned mesh roots under every child feature.</summary>
    [ContextMenu("Clear All Spawned")]
    public int ClearAllSpawned()
    {
        int count = 0;
        foreach (var spawner in Spawners)
        {
            if (spawner == null) continue;
            spawner.ClearSpawned();
            count++;
        }
        Debug.Log($"[TerrainGenManager] cleared spawned meshes on {count} feature(s).");
        return count;
    }

    // --- Bulk configuration --------------------------------------------------

    /// <summary>Pushes <see cref="sharedTerrain"/> onto every child spawner's Target Terrain field,
    /// so a whole folder of features blends onto the one terrain.</summary>
    [ContextMenu("Apply Terrain To All")]
    public int ApplyTerrainToAll()
    {
        int count = 0;
        foreach (var spawner in Spawners)
        {
            if (spawner == null) continue;
            spawner.TargetTerrain = sharedTerrain;
            count++;
        }
        Debug.Log($"[TerrainGenManager] set Target Terrain on {count} feature(s).");
        return count;
    }

    /// <summary>Pushes <see cref="sharedFeatureLayer"/> onto every child spawner's feature layer,
    /// so every feature mesh lands on the same NavMesh-collected layer.</summary>
    [ContextMenu("Apply Layer To All")]
    public int ApplyLayerToAll()
    {
        int count = 0;
        foreach (var spawner in Spawners)
        {
            if (spawner == null) continue;
            spawner.FeatureLayer = sharedFeatureLayer;
            count++;
        }
        Debug.Log($"[TerrainGenManager] set feature layer '{sharedFeatureLayer}' on {count} feature(s).");
        return count;
    }

    /// <summary>
    /// Gives every child spawner that lacks a Target Terrain the closest Terrain found IN ITS OWN
    /// SCENE. This is the fix for the "Bake All makes features disappear" bug: a feature with a null
    /// terrain falls back to <c>Terrain.activeTerrain</c>, so during a bulk bake every such feature
    /// is skirt-blended down onto the WRONG ground height and the mesh bakes off-screen. Resolving a
    /// per-feature terrain up front keeps each feature blended onto the ground it actually sits on.
    /// If <see cref="sharedTerrain"/> is set it is used as the fallback when a scene has no Terrain.
    /// Returns how many spawners had a terrain assigned.
    /// </summary>
    [ContextMenu("Auto-Assign Terrains")]
    public int AutoAssignTerrains()
    {
        int assigned = 0, stillNull = 0;
        foreach (var spawner in Spawners)
        {
            if (spawner == null || spawner.TargetTerrain != null) continue;

            Terrain terrain = FindClosestTerrainInScene(spawner) ?? sharedTerrain;
            if (terrain != null) { spawner.TargetTerrain = terrain; assigned++; }
            else stillNull++;
        }
        if (stillNull > 0)
            Debug.LogWarning($"[TerrainGenManager] auto-assigned terrain to {assigned} feature(s); " +
                             $"{stillNull} still have no terrain (no Terrain in their scene and no " +
                             "Shared Terrain set) — those will use the flat fallback height.");
        else
            Debug.Log($"[TerrainGenManager] auto-assigned terrain to {assigned} feature(s).");
        return assigned;
    }

    /// <summary>The Terrain in <paramref name="spawner"/>'s scene whose horizontal bounds contain
    /// the feature (or, failing that, the nearest one). Null when the scene holds no Terrain.</summary>
    static Terrain FindClosestTerrainInScene(TerrainFeatureSpawner spawner)
    {
        UnityEngine.SceneManagement.Scene scene = spawner.gameObject.scene;
        Vector3 pos = spawner.transform.position;

        Terrain best = null;
        float bestDist = float.MaxValue;

        foreach (Terrain t in FindObjectsOfType<Terrain>())
        {
            if (t == null || t.gameObject.scene != scene || t.terrainData == null) continue;

            Vector3 tp = t.transform.position;
            Vector3 size = t.terrainData.size;
            // Inside this terrain's footprint → an exact match, take it immediately.
            if (pos.x >= tp.x && pos.x <= tp.x + size.x &&
                pos.z >= tp.z && pos.z <= tp.z + size.z)
                return t;

            Vector3 centre = tp + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
            float d = (new Vector2(pos.x, pos.z) - new Vector2(centre.x, centre.z)).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = t; }
        }
        return best;
    }
}
