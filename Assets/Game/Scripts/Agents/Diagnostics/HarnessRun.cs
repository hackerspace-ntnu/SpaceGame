// What every play-mode diagnostic harness (the agent benchmark, the residents baseline) does the
// same way: wait out the game's own boot chain before touching anything, and leave its report in a
// file whose last line says it is complete.
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace SpaceGame.Agents
{
    public static class HarnessRun
    {
        /// <summary>A report's last line once it is complete. A poller that does not see it keeps waiting.</summary>
        public const string DoneLine = "DONE";

        // The game's Bootstrapper sends every play start to build index 0 and then on to a later
        // scene. Until a scene past index 0 is active, that chain is still running and would load
        // its scene on top of whatever the harness builds.
        private const int BootstrapSceneBuildIndex = 0;

        /// <summary>
        /// Yields until the Bootstrap scene chain has finished loading, or calls
        /// <paramref name="onTimeout"/> with the reason after <paramref name="timeoutFrames"/>.
        /// </summary>
        public static IEnumerator WaitForBootstrap(int timeoutFrames, Action<string> onTimeout)
        {
            for (int frame = 0; frame < timeoutFrames; frame++)
            {
                if (BootstrapSettled()) yield break;
                yield return null;
            }

            onTimeout("the Bootstrap scene chain had not finished loading after " + timeoutFrames + " frames");
        }

        private static bool BootstrapSettled()
        {
            if (SceneManager.GetActiveScene().buildIndex <= BootstrapSceneBuildIndex) return false;

            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (!SceneManager.GetSceneAt(i).isLoaded) return false;

            return true;
        }

        /// <summary>
        /// Moves into a fresh empty scene named <paramref name="stageName"/> and unloads every other
        /// scene, so the menu's objects are neither measured nor in the way.
        /// </summary>
        public static IEnumerator MoveToEmptyStage(string stageName)
        {
            Scene stage = SceneManager.CreateScene(stageName);
            SceneManager.SetActiveScene(stage);

            var unloads = new List<AsyncOperation>();
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene == stage || !scene.isLoaded) continue;

                AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                if (unload != null) unloads.Add(unload);
            }

            foreach (AsyncOperation unload in unloads)
                while (!unload.isDone) yield return null;
        }

        /// <summary>
        /// A flat square slab whose top face is the walking surface at <paramref name="centre"/>,
        /// with a NavMesh for every agent type over it. One Box source over the cube's own matrix:
        /// CollectSources over physics colliders finds nothing for a collider made this frame.
        /// </summary>
        public static GameObject BuildNavMeshSlab(string name, Vector3 centre, float size, float thickness, float boundsHeight)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = name;
            ground.transform.localScale = new Vector3(size, thickness, size);
            ground.transform.position = centre + Vector3.down * (thickness * 0.5f);

            var sources = new List<NavMeshBuildSource>
            {
                new()
                {
                    shape = NavMeshBuildSourceShape.Box,
                    size = Vector3.one,
                    transform = ground.transform.localToWorldMatrix,
                },
            };
            var bounds = new Bounds(centre, new Vector3(size, boundsHeight, size));

            for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                NavMeshData data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(i), sources, bounds,
                                                                   Vector3.zero, Quaternion.identity);
                if (data != null) NavMesh.AddNavMeshData(data);
            }

            return ground;
        }

        /// <summary>A report's first line: which harness, when, on which editor.</summary>
        public static StringBuilder BeginReport(string harness)
        {
            var report = new StringBuilder();
            report.Append(harness).Append(' ')
                  .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                  .Append(" | Unity ").AppendLine(Application.unityVersion);
            return report;
        }

        /// <summary>A report that says only why there is no result, ending in <see cref="DoneLine"/>.</summary>
        public static void WriteFailure(string harness, string reportPath, string reason)
        {
            StringBuilder report = BeginReport(harness);
            report.Append("status: FAILED — ").AppendLine(reason);
            report.AppendLine(DoneLine);
            WriteText(reportPath, report.ToString());
        }

        /// <summary>Appends <paramref name="text"/> to <paramref name="path"/>, which <see cref="WriteText"/> created.</summary>
        public static void AppendText(string path, string text) => File.AppendAllText(path, text);

        /// <summary>Writes <paramref name="text"/> to <paramref name="path"/> (relative to the project root), creating its folder.</summary>
        public static void WriteText(string path, string text)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, text);
        }
    }
}
