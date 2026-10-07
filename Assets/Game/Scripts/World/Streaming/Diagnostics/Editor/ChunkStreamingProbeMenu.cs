using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SpaceGame.World.Diagnostics;

namespace SpaceGame.World.DiagnosticsTools
{
    /// <summary>
    /// Arms and starts a single <see cref="ChunkStreamingProbe"/> run.
    ///
    /// Deletes the previous results file first. A stale report that looks like a fresh one is the
    /// worst possible outcome of a measurement harness, so absence of the file is treated as the
    /// only proof that the run did not produce one.
    /// </summary>
    public static class ChunkStreamingProbeMenu
    {
        [MenuItem("World/Streaming/Run Chunk Traversal Probe", priority = 100)]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[ChunkStreamingProbe] exit Play mode first.");
                return;
            }

            var active = EditorSceneManager.GetActiveScene();
            if (!active.name.Contains("persistentScene"))
            {
                Debug.LogWarning($"[ChunkStreamingProbe] active scene is '{active.name}'. " +
                                 "The probe needs a scene containing the WorldStreamer " +
                                 "(persistentScene). Running anyway.");
            }

            var path = ChunkStreamingProbe.ResultsPath;
            if (File.Exists(path)) File.Delete(path);

            EditorPrefs.SetBool(ChunkStreamingProbe.ArmedKey, true);
            EditorApplication.EnterPlaymode();
            Debug.Log("[ChunkStreamingProbe] armed; entering Play mode.");
        }

        [MenuItem("World/Streaming/Run Chunk Traversal Probe", validate = true)]
        private static bool CanRun() => !EditorApplication.isPlaying;
    }
}
