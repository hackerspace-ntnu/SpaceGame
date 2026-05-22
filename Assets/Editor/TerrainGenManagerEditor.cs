using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom inspector for <see cref="TerrainGenManager"/>. Adds the bulk edit-time workflow buttons:
/// <b>Bake All</b> runs the same generate-and-save-mesh pipeline as
/// <see cref="TerrainFeatureSpawnerEditor"/> across every child <see cref="TerrainFeatureSpawner"/>
/// in one click, plus folder-wide Regenerate / Spawn-baked / Clear / configuration helpers.
///
/// Asset writing is editor-only, so the bake itself lives here rather than on the runtime
/// <see cref="TerrainGenManager"/> component; it reuses <see cref="TerrainFeatureBakeUtility"/> so
/// the per-feature and bulk bake paths produce byte-identical assets.
/// </summary>
[CustomEditor(typeof(TerrainGenManager))]
public class TerrainGenManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var manager = (TerrainGenManager)target;

        DrawDefaultInspector();

        List<TerrainFeatureSpawner> spawners = manager.Spawners;
        int implemented = 0, baked = 0, noTerrain = 0;
        foreach (var s in spawners)
        {
            if (s == null) continue;
            if (TerrainFeatureRegistry.IsImplemented(s.FeatureType)) implemented++;
            if (s.HasBakedMesh) baked++;
            if (s.TargetTerrain == null) noTerrain++;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Folder", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"{spawners.Count} terrain feature(s) found in this folder.\n" +
            $"{implemented} implemented · {baked} with a baked mesh.",
            spawners.Count == 0 ? MessageType.Warning : MessageType.Info);

        // A null Target Terrain is the usual cause of "Bake All makes features disappear": the
        // feature falls back to Terrain.activeTerrain and bakes against the wrong ground height.
        if (noTerrain > 0)
            EditorGUILayout.HelpBox(
                $"{noTerrain} feature(s) have no Target Terrain. During Bake All they fall back to " +
                "the active Terrain and may bake off-screen. Click 'Auto-Assign Terrains' (Bake All " +
                "also runs it automatically), or set a Shared Terrain above.",
                MessageType.Warning);

        using (new EditorGUI.DisabledScope(spawners.Count == 0))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bake", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Bake All Meshes", GUILayout.Height(28)))
                    BakeAll(manager);
                if (GUILayout.Button("Clear All Baked", GUILayout.Width(130), GUILayout.Height(28)))
                    ClearAllBaked(manager);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Regenerate All", GUILayout.Height(24)))
                {
                    manager.RegenerateAll();
                    MarkDirty(manager);
                }
                if (GUILayout.Button("Spawn All Baked", GUILayout.Height(24)))
                {
                    manager.SpawnAllBaked();
                    MarkDirty(manager);
                }
                if (GUILayout.Button("Clear All", GUILayout.Width(90), GUILayout.Height(24)))
                {
                    manager.ClearAllSpawned();
                    MarkDirty(manager);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bulk configuration", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply Terrain To All", GUILayout.Height(24)))
                {
                    Undo.RecordObjects(SpawnerObjects(manager), "Apply Terrain To All");
                    manager.ApplyTerrainToAll();
                    SetSpawnersDirty(manager);
                }
                if (GUILayout.Button("Apply Layer To All", GUILayout.Height(24)))
                {
                    Undo.RecordObjects(SpawnerObjects(manager), "Apply Layer To All");
                    manager.ApplyLayerToAll();
                    SetSpawnersDirty(manager);
                }
            }

            if (GUILayout.Button("Auto-Assign Terrains", GUILayout.Height(24)))
            {
                Undo.RecordObjects(SpawnerObjects(manager), "Auto-Assign Terrains");
                manager.AutoAssignTerrains();
                SetSpawnersDirty(manager);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Bulk bake
    // -------------------------------------------------------------------------

    /// <summary>
    /// Generates and saves the mesh of every child feature, mirroring
    /// <c>TerrainFeatureSpawnerEditor.BakeAndSave</c> per feature. Unimplemented or empty-result
    /// features are skipped and reported; a single shared progress bar tracks the whole run.
    /// </summary>
    static void BakeAll(TerrainGenManager manager)
    {
        // Ensure every feature has a terrain BEFORE baking. Without this a feature with a null
        // Target Terrain falls back to Terrain.activeTerrain and bakes against the wrong ground
        // height — the mesh ends up off-screen ("the object disappears").
        manager.AutoAssignTerrains();

        List<TerrainFeatureSpawner> spawners = manager.Spawners;
        int ok = 0, skipped = 0;

        try
        {
            for (int i = 0; i < spawners.Count; i++)
            {
                TerrainFeatureSpawner spawner = spawners[i];
                if (spawner == null) continue;

                EditorUtility.DisplayProgressBar(
                    "Baking terrain features",
                    $"{spawner.name} ({i + 1}/{spawners.Count})",
                    (float)i / spawners.Count);

                if (!TerrainFeatureRegistry.IsImplemented(spawner.FeatureType))
                {
                    Debug.LogWarning($"[TerrainGenManager] skipped '{spawner.name}' — feature " +
                                     $"'{spawner.FeatureType}' is not implemented.");
                    skipped++;
                    continue;
                }

                TerrainFeatureResult result = spawner.Generate();
                if (result == null || !result.IsValid)
                {
                    Debug.LogWarning($"[TerrainGenManager] skipped '{spawner.name}' — generation " +
                                     "produced no mesh.");
                    skipped++;
                    continue;
                }

                string folder = TerrainFeatureBakeUtility.ResolveBakeFolder(spawner);

                if (result.IsMultiMesh)
                {
                    Mesh[] saved = TerrainFeatureBakeUtility.SaveSubMeshes(
                        result.SubMeshes, folder, spawner);
                    spawner.AssignBakedSubMeshes(saved);
                }
                else
                {
                    Mesh saved = TerrainFeatureBakeUtility.SaveSingle(result.Mesh, folder, spawner);
                    spawner.AssignBakedMesh(saved);
                }

                spawner.SpawnBaked();
                EditorUtility.SetDirty(spawner);
                ok++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        MarkDirty(manager);
        Debug.Log($"[TerrainGenManager] bake complete — {ok} baked, {skipped} skipped.");
    }

    /// <summary>Clears the baked mesh assignment on every child feature (does not delete the
    /// .asset files; they are simply unreferenced).</summary>
    static void ClearAllBaked(TerrainGenManager manager)
    {
        int count = 0;
        foreach (var spawner in manager.Spawners)
        {
            if (spawner == null) continue;
            spawner.AssignBakedMesh(null);
            spawner.AssignBakedSubMeshes(null);
            EditorUtility.SetDirty(spawner);
            count++;
        }
        MarkDirty(manager);
        Debug.Log($"[TerrainGenManager] cleared baked mesh on {count} feature(s).");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    static Object[] SpawnerObjects(TerrainGenManager manager)
    {
        List<TerrainFeatureSpawner> spawners = manager.Spawners;
        var objs = new Object[spawners.Count];
        for (int i = 0; i < spawners.Count; i++) objs[i] = spawners[i];
        return objs;
    }

    static void SetSpawnersDirty(TerrainGenManager manager)
    {
        foreach (var spawner in manager.Spawners)
            if (spawner != null) EditorUtility.SetDirty(spawner);
        MarkDirty(manager);
    }

    static void MarkDirty(TerrainGenManager manager)
    {
        if (manager.gameObject.scene.IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
    }
}
