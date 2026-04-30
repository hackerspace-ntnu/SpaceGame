using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor tool that bakes a NavMesh per chunk scene and stores the resulting
/// NavMeshData as an asset alongside the scene. At runtime, WorldStreamer
/// adds/removes that prebaked data when the chunk loads/unloads instead of
/// rebuilding the navmesh from scratch.
/// Open via Tools > World Streaming > Bake Chunk NavMeshes.
/// </summary>
public class ChunkNavMeshBaker : EditorWindow
{
    private const string NavMeshSurfaceObjectName = "ChunkNavMeshSurface";

    private WorldStreamingConfig config;
    private string navMeshDataFolder = "Assets/Terrain/ChunkNavMesh";
    private float boundsOverlap = 4f;
    private float boundsHeight = 500f;
    private bool onlySelected;
    private readonly HashSet<Vector2Int> selectedCoords = new();

    [MenuItem("Tools/World Streaming/Bake Chunk NavMeshes")]
    public static void ShowWindow() => GetWindow<ChunkNavMeshBaker>("Chunk NavMesh Baker");

    private void OnEnable()
    {
        if (config == null)
            config = AssetDatabase.LoadAssetAtPath<WorldStreamingConfig>("Assets/Settings/WorldStreamingConfig.asset");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Chunk NavMesh Baker", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Bakes a NavMesh per chunk scene and saves the data as an asset. " +
            "WorldStreamer registers the prebaked data when the chunk loads — no runtime rebuild.",
            MessageType.Info);

        config = (WorldStreamingConfig)EditorGUILayout.ObjectField("Config", config, typeof(WorldStreamingConfig), false);
        navMeshDataFolder = EditorGUILayout.TextField("Output Folder", navMeshDataFolder);
        boundsOverlap = EditorGUILayout.FloatField(
            new GUIContent("Bounds Overlap", "Extends each chunk's bake bounds outward so neighbouring tiles stitch together. ~2x agent radius is usually enough."),
            boundsOverlap);
        boundsHeight = EditorGUILayout.FloatField(
            new GUIContent("Bounds Height", "Vertical extent of the bake volume. Must cover all walkable elevations in the chunk."),
            boundsHeight);

        EditorGUILayout.Space();
        onlySelected = EditorGUILayout.Toggle("Only Selected Chunks", onlySelected);
        if (onlySelected)
        {
            EditorGUILayout.HelpBox(
                "Type chunk coords as 'x,y' separated by commas (e.g. '0,0;3,2;5,4'). Empty = bake all.",
                MessageType.None);
            string current = string.Join(";", selectedCoords.Select(c => $"{c.x},{c.y}"));
            string entered = EditorGUILayout.TextField("Coords", current);
            if (entered != current)
            {
                selectedCoords.Clear();
                foreach (var part in entered.Split(';', ','))
                {
                    var trimmed = part.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                }
                // Re-parse properly as pairs
                selectedCoords.Clear();
                foreach (var pair in entered.Split(';'))
                {
                    var pieces = pair.Split(',');
                    if (pieces.Length != 2) continue;
                    if (int.TryParse(pieces[0].Trim(), out var x) && int.TryParse(pieces[1].Trim(), out var y))
                        selectedCoords.Add(new Vector2Int(x, y));
                }
            }
        }

        EditorGUILayout.Space();
        GUI.enabled = config != null && config.chunks != null && config.chunks.Length > 0;
        if (GUILayout.Button("Bake NavMeshes", GUILayout.Height(30)))
        {
            BakeAll();
        }
        GUI.enabled = true;

        if (config == null)
            EditorGUILayout.HelpBox("Assign a WorldStreamingConfig to bake.", MessageType.Warning);
    }

    private void BakeAll()
    {
        if (config == null || config.chunks == null) return;

        if (!Directory.Exists(navMeshDataFolder))
            Directory.CreateDirectory(navMeshDataFolder);

        var activeScene = EditorSceneManager.GetActiveScene();
        string activeScenePath = activeScene.path;
        EditorSceneManager.SaveOpenScenes();

        int total = config.chunks.Length;
        int processed = 0;
        int baked = 0;

        try
        {
            for (int i = 0; i < config.chunks.Length; i++)
            {
                var chunk = config.chunks[i];
                processed++;

                if (onlySelected && selectedCoords.Count > 0 && !selectedCoords.Contains(chunk.gridCoord))
                    continue;

                if (string.IsNullOrEmpty(chunk.scenePath) || !File.Exists(chunk.scenePath))
                {
                    Debug.LogWarning($"[ChunkNavMeshBaker] Skipping {chunk.gridCoord} — scene path missing.");
                    continue;
                }

                EditorUtility.DisplayProgressBar(
                    "Baking Chunk NavMeshes",
                    $"{chunk.sceneName} ({processed}/{total})",
                    (float)processed / total);

                if (BakeChunk(chunk))
                    baked++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();

            // Restore the originally-active scene if it still exists.
            if (!string.IsNullOrEmpty(activeScenePath) && File.Exists(activeScenePath))
            {
                if (EditorSceneManager.GetActiveScene().path != activeScenePath)
                    EditorSceneManager.OpenScene(activeScenePath, OpenSceneMode.Single);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ChunkNavMeshBaker] Baked {baked}/{total} chunks.");
        EditorUtility.DisplayDialog("Bake Complete", $"Baked {baked}/{total} chunks.\nData saved under {navMeshDataFolder}.", "OK");
    }

    private bool BakeChunk(ChunkInfo chunk)
    {
        // Open the chunk scene as the only loaded scene so collected geometry is just this chunk.
        Scene scene = EditorSceneManager.OpenScene(chunk.scenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError($"[ChunkNavMeshBaker] Failed to open {chunk.scenePath}");
            return false;
        }

        // Find or create the surface GO at the scene root.
        NavMeshSurface surface = FindOrCreateSurface(scene, chunk);

        // Configure the bake volume.
        Vector3 chunkOrigin = config.ChunkToWorldPosition(chunk.gridCoord);
        Vector3 chunkCenter = new Vector3(
            chunkOrigin.x + config.chunkSize.x * 0.5f,
            0f,
            chunkOrigin.z + config.chunkSize.y * 0.5f);

        surface.collectObjects = CollectObjects.Volume;
        surface.center = chunkCenter - surface.transform.position;
        surface.size = new Vector3(
            config.chunkSize.x + boundsOverlap * 2f,
            boundsHeight,
            config.chunkSize.y + boundsOverlap * 2f);

        // Bake. BuildNavMesh writes into surface.navMeshData (creating it if null).
        surface.BuildNavMesh();

        if (surface.navMeshData == null)
        {
            Debug.LogError($"[ChunkNavMeshBaker] Bake produced no data for {chunk.sceneName}.");
            return false;
        }

        // Save the NavMeshData as a standalone asset so it can be referenced
        // by the chunk scene and committed to source control.
        string dataAssetPath = $"{navMeshDataFolder}/{chunk.sceneName}_NavMesh.asset";

        var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(dataAssetPath);
        if (existing != null && existing != surface.navMeshData)
        {
            // Overwrite the existing asset's contents in-place so any references stay valid.
            EditorUtility.CopySerialized(surface.navMeshData, existing);
            surface.navMeshData = existing;
        }
        else if (existing == null)
        {
            AssetDatabase.CreateAsset(surface.navMeshData, dataAssetPath);
        }

        EditorUtility.SetDirty(surface);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        return true;
    }

    private static NavMeshSurface FindOrCreateSurface(Scene scene, ChunkInfo chunk)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var existing = root.GetComponent<NavMeshSurface>();
            if (existing != null && root.name == NavMeshSurfaceObjectName)
                return existing;

            var inChildren = root.GetComponentInChildren<NavMeshSurface>(true);
            if (inChildren != null)
                return inChildren;
        }

        var go = new GameObject(NavMeshSurfaceObjectName);
        SceneManager.MoveGameObjectToScene(go, scene);
        return go.AddComponent<NavMeshSurface>();
    }
}
