using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(WorldStreamer))]
public class WorldStreamerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var streamer = (WorldStreamer)target;
        var config = GetConfig(streamer);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Editor Tools (Edit Mode)", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(Application.isPlaying || config == null || config.chunks == null))
        {
            if (GUILayout.Button("Load All Chunks Into Scene View"))
                LoadAllChunks(config);

            if (GUILayout.Button("Unload All Chunks"))
                UnloadAllChunks(config);
        }

        if (config == null)
            EditorGUILayout.HelpBox("Assign a WorldStreamingConfig to enable editor chunk loading.", MessageType.Info);
        else if (Application.isPlaying)
            EditorGUILayout.HelpBox("Editor chunk loading is only available in edit mode.", MessageType.Info);
    }

    private static WorldStreamingConfig GetConfig(WorldStreamer streamer)
    {
        var so = new SerializedObject(streamer);
        var prop = so.FindProperty("config");
        return prop != null ? prop.objectReferenceValue as WorldStreamingConfig : null;
    }

    private static void LoadAllChunks(WorldStreamingConfig config)
    {
        var alreadyLoaded = new HashSet<string>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
            alreadyLoaded.Add(SceneManager.GetSceneAt(i).path);

        int loaded = 0, skipped = 0, failed = 0;
        try
        {
            for (int i = 0; i < config.chunks.Length; i++)
            {
                var chunk = config.chunks[i];
                if (string.IsNullOrEmpty(chunk.scenePath))
                {
                    failed++;
                    continue;
                }

                if (alreadyLoaded.Contains(chunk.scenePath))
                {
                    skipped++;
                    continue;
                }

                EditorUtility.DisplayProgressBar(
                    "Loading Chunks",
                    $"{chunk.sceneName} ({i + 1}/{config.chunks.Length})",
                    (float)i / config.chunks.Length);

                var scene = EditorSceneManager.OpenScene(chunk.scenePath, OpenSceneMode.Additive);
                if (scene.IsValid())
                {
                    AlignChunkTerrain(scene, config, chunk.gridCoord);
                    loaded++;
                }
                else
                {
                    failed++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"[WorldStreamer] Editor load: {loaded} loaded, {skipped} already open, {failed} failed.");
    }

    private static void UnloadAllChunks(WorldStreamingConfig config)
    {
        var chunkPaths = new HashSet<string>();
        foreach (var chunk in config.chunks)
        {
            if (!string.IsNullOrEmpty(chunk.scenePath))
                chunkPaths.Add(chunk.scenePath);
        }

        var toClose = new List<Scene>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (chunkPaths.Contains(scene.path))
                toClose.Add(scene);
        }

        if (toClose.Count == SceneManager.sceneCount)
        {
            Debug.LogWarning("[WorldStreamer] Refusing to close all scenes — keep at least one non-chunk scene open.");
            return;
        }

        EditorSceneManager.SaveModifiedScenesIfUserWantsTo(toClose.ToArray());

        int closed = 0;
        foreach (var scene in toClose)
        {
            if (EditorSceneManager.CloseScene(scene, true))
                closed++;
        }

        Debug.Log($"[WorldStreamer] Editor unload: closed {closed} chunk scenes.");
    }

    private static void AlignChunkTerrain(Scene scene, WorldStreamingConfig config, Vector2Int coord)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;

        Vector3 expected = config.ChunkToWorldPosition(coord);
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
            {
                var p = terrain.transform.position;
                terrain.transform.position = new Vector3(expected.x, p.y, expected.z);
            }
        }
    }
}
