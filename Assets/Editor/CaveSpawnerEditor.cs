using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Custom inspector for <see cref="CaveSpawner"/> with a one-click "Bake &amp; Save" button.
///
/// Workflow:
///   1. Tune settings on the CaveSpawner in the inspector.
///   2. Click "Bake &amp; Save NavMesh". This generates the cave at edit time, runs the synchronous
///      NavMeshSurface bake, and writes both the Mesh and NavMeshData to a folder next to the scene
///      (CaveBakes/seed_NNNN_Mesh.asset + CaveBakes/seed_NNNN_NavMesh.asset).
///   3. The spawner's bakedMesh + bakedNavMeshData fields are auto-assigned to the new assets and
///      the scene is marked dirty. Save the scene; future runs skip generation entirely.
/// </summary>
[CustomEditor(typeof(CaveSpawner))]
public class CaveSpawnerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var spawner = (CaveSpawner)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Pre-bake", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Bake & Save (mesh + navmesh)", GUILayout.Height(28)))
            {
                BakeAndSave(spawner);
            }
            if (GUILayout.Button("Clear Baked", GUILayout.Width(110), GUILayout.Height(28)))
            {
                spawner.AssignBakedAssets(null, null);
                EditorUtility.SetDirty(spawner);
            }
        }

        if (spawner.HasBakedAssets)
        {
            EditorGUILayout.HelpBox(
                "Baked assets assigned. Runtime will skip generation and load these directly.",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "No baked assets assigned. Runtime will generate + bake live (slow on scene transitions).",
                MessageType.Warning);
        }
    }

    void BakeAndSave(CaveSpawner spawner)
    {
        EditorUtility.DisplayProgressBar("Cave bake", "Generating mesh…", 0.1f);

        try
        {
            if (!spawner.EditorBuildForBaking(out var mesh, out var navMeshData) || mesh == null || navMeshData == null)
            {
                EditorUtility.DisplayDialog("Cave bake failed", "Generation or NavMesh bake returned null.", "OK");
                return;
            }

            EditorUtility.DisplayProgressBar("Cave bake", "Writing assets…", 0.8f);

            int seed = spawner.Settings.seed;
            string folder = ResolveBakeFolder(spawner);
            string meshPath = $"{folder}/seed_{seed}_Mesh.asset";
            string navPath  = $"{folder}/seed_{seed}_NavMesh.asset";

            ReplaceAsset(mesh, meshPath);
            ReplaceAsset(navMeshData, navPath);

            // Re-load via AssetDatabase so the spawner references the persistent asset (not the
            // in-memory temporary).
            var savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            var savedNav  = AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath);
            spawner.AssignBakedAssets(savedMesh, savedNav);

            EditorUtility.SetDirty(spawner);
            if (spawner.gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);

            Debug.Log($"[CaveSpawnerEditor] baked + saved cave seed {seed} → {meshPath} / {navPath}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    static string ResolveBakeFolder(CaveSpawner spawner)
    {
        string scenePath = spawner.gameObject.scene.path;
        string parent = string.IsNullOrEmpty(scenePath)
            ? "Assets"
            : Path.GetDirectoryName(scenePath).Replace('\\', '/');
        string folder = $"{parent}/CaveBakes";
        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder(parent, "CaveBakes");
        }
        return folder;
    }

    static void ReplaceAsset(Object asset, string path)
    {
        var existing = AssetDatabase.LoadMainAssetAtPath(path);
        if (existing != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
    }
}
