using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom inspector for <see cref="TerrainFeatureSpawner"/>. Mirrors <c>CaveSpawnerEditor</c>'s
/// bake-and-save workflow, and adds the live-preview / footprint-gizmo tooling the project lead
/// asked for.
///
/// Workflow:
///   1. Drop a TerrainFeatureSpawner, pick a feature type, drag the Scene-view box / spline
///      handles (drawn by <see cref="TerrainFeatureHandles"/>) to define the footprint.
///   2. Tune the shared noise / overlap / height / jaggedness sliders. With "Live preview" on,
///      every change instantly regenerates the in-scene mesh so the result is visible immediately.
///   3. Click "Bake &amp; Save Mesh". This generates the feature at edit time and writes the mesh
///      to a TerrainFeatureBakes/ folder next to the scene; the spawner's bakedMesh field is
///      auto-assigned. Runtime then skips generation entirely.
///
/// NavMesh is intentionally NOT baked here — feature meshes contribute to the shared WORLD
/// NavMesh via their MeshCollider, so the world's NavMeshSurface rebuild owns that.
/// </summary>
[CustomEditor(typeof(TerrainFeatureSpawner))]
public class TerrainFeatureSpawnerEditor : Editor
{
    bool _livePreview;

    void OnSceneGUI()
    {
        TerrainFeatureHandles.Draw((TerrainFeatureSpawner)target);
    }

    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        bool settingsChanged = EditorGUI.EndChangeCheck();

        var spawner = (TerrainFeatureSpawner)target;

        EditorGUILayout.Space();
        DrawImplementationStatus(spawner);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

        _livePreview = EditorGUILayout.Toggle(
            new GUIContent("Live preview", "Regenerate the in-scene mesh whenever a setting changes."),
            _livePreview);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Regenerate Preview", GUILayout.Height(24)))
                Regenerate(spawner);
            if (GUILayout.Button("Clear Preview", GUILayout.Width(120), GUILayout.Height(24)))
            {
                spawner.ClearSpawned();
                MarkSceneDirty(spawner);
            }
        }

        if (_livePreview && settingsChanged)
            Regenerate(spawner);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Bake", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Bake & Save Mesh", GUILayout.Height(28)))
                BakeAndSave(spawner);
            if (GUILayout.Button("Clear Baked", GUILayout.Width(110), GUILayout.Height(28)))
            {
                spawner.AssignBakedMesh(null);
                EditorUtility.SetDirty(spawner);
            }
        }

        if (spawner.HasBakedMesh)
            EditorGUILayout.HelpBox(
                "Baked mesh assigned. Runtime skips generation and instantiates it directly. " +
                "The feature contributes to the world NavMesh via its MeshCollider.",
                MessageType.Info);
        else
            EditorGUILayout.HelpBox(
                "No baked mesh. Runtime will generate the feature live on Awake (slow).",
                MessageType.Warning);
    }

    // -------------------------------------------------------------------------
    // Implementation-status banner
    // -------------------------------------------------------------------------

    static void DrawImplementationStatus(TerrainFeatureSpawner spawner)
    {
        if (TerrainFeatureRegistry.IsImplemented(spawner.FeatureType))
        {
            EditorGUILayout.HelpBox(
                $"Feature '{spawner.FeatureType}' is implemented and ready to generate.",
                MessageType.None);
        }
        else
        {
            EditorGUILayout.HelpBox(
                $"Feature '{spawner.FeatureType}' has NO implementation registered yet. " +
                "Implement a TerrainFeature subclass and register it in TerrainFeatureRegistry.",
                MessageType.Error);
        }
    }

    // -------------------------------------------------------------------------
    // Preview + bake
    // -------------------------------------------------------------------------

    static void Regenerate(TerrainFeatureSpawner spawner)
    {
        if (!TerrainFeatureRegistry.IsImplemented(spawner.FeatureType)) return;
        spawner.GenerateNow();
        MarkSceneDirty(spawner);
    }

    static void BakeAndSave(TerrainFeatureSpawner spawner)
    {
        if (!TerrainFeatureRegistry.IsImplemented(spawner.FeatureType))
        {
            EditorUtility.DisplayDialog("Bake failed",
                $"Feature '{spawner.FeatureType}' is not implemented.", "OK");
            return;
        }

        EditorUtility.DisplayProgressBar("Terrain feature bake", "Generating mesh…", 0.2f);
        try
        {
            TerrainFeatureResult result = spawner.Generate();
            if (result == null || !result.IsValid)
            {
                EditorUtility.DisplayDialog("Bake failed", "Generation produced no mesh.", "OK");
                return;
            }

            EditorUtility.DisplayProgressBar("Terrain feature bake", "Writing asset…", 0.8f);

            string folder = ResolveBakeFolder(spawner);
            string meshPath = $"{folder}/{spawner.FeatureType}_seed_{spawner.Seed}_Mesh.asset";
            ReplaceAsset(result.Mesh, meshPath);

            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            spawner.AssignBakedMesh(saved);
            // Spawn the saved asset so the scene shows the persistent mesh, not the temporary.
            spawner.SpawnBaked();

            EditorUtility.SetDirty(spawner);
            MarkSceneDirty(spawner);
            Debug.Log($"[TerrainFeatureSpawnerEditor] baked {spawner.FeatureType} " +
                      $"seed {spawner.Seed} → {meshPath} ({result.Mesh.vertexCount} verts).");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    static string ResolveBakeFolder(TerrainFeatureSpawner spawner)
    {
        string scenePath = spawner.gameObject.scene.path;
        string parent = string.IsNullOrEmpty(scenePath)
            ? "Assets"
            : Path.GetDirectoryName(scenePath).Replace('\\', '/');
        string folder = $"{parent}/TerrainFeatureBakes";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder(parent, "TerrainFeatureBakes");
        return folder;
    }

    static void ReplaceAsset(Object asset, string path)
    {
        if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
    }

    static void MarkSceneDirty(TerrainFeatureSpawner spawner)
    {
        if (spawner.gameObject.scene.IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
    }
}
