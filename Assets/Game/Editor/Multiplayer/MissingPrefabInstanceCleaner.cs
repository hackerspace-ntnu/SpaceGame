// Removes prefab instances whose source asset no longer exists.
//
// Deleting a prefab does not touch the scenes that instanced it. Each one keeps a PrefabInstance
// block pointing at a GUID nothing resolves, which Unity shows as "Missing Prefab" and skips at
// runtime. Harmless on its own — but it hides in scene YAML where a plain grep for a component's
// script GUID will never find it (a PrefabInstance lists modifications, not components), so the
// only reliable way to find and remove these is to ask Unity.
//
// This is deliberately a menu command rather than something automatic. Deleting objects out of a
// scene is not a decision a tool should take on its own; the report below is meant to be read first.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools
{
    public static class MissingPrefabInstanceCleaner
    {
        private const string ReportMenu = "Tools/SpaceGame/Cleanup/Report Missing Prefab Instances";
        private const string CleanMenu = "Tools/SpaceGame/Cleanup/Remove Missing Prefab Instances";

        [MenuItem(ReportMenu)]
        private static void Report() => Run(deleting: false);

        [MenuItem(CleanMenu)]
        private static void Clean()
        {
            if (!EditorUtility.DisplayDialog(
                    "Remove missing prefab instances",
                    "This opens every scene in the project, deletes any object whose source prefab no " +
                    "longer exists, and saves the scenes that changed.\n\n" +
                    "Run the report first if you have not.",
                    "Delete them", "Cancel"))
                return;

            Run(deleting: true);
        }

        private static void Run(bool deleting)
        {
            if (deleting && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            string[] scenePaths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .ToArray();

            var findings = new List<string>();
            int total = 0;

            foreach (string path in scenePaths)
            {
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

                List<GameObject> missing = FindMissingInstances(scene);
                if (missing.Count == 0) continue;

                total += missing.Count;
                findings.Add($"{path}: {missing.Count}\n    " +
                             string.Join("\n    ", missing.Select(GetPath)));

                if (!deleting) continue;

                foreach (GameObject go in missing)
                    Object.DestroyImmediate(go);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            string verb = deleting ? "Removed" : "Found";
            Debug.Log(findings.Count == 0
                ? "[Cleanup] No missing prefab instances in any scene."
                : $"[Cleanup] {verb} {total} missing prefab instance(s):\n  " + string.Join("\n  ", findings));
        }

        /// <summary>
        /// Roots only. A missing prefab instance brings its whole subtree with it, so collecting
        /// descendants as well would queue objects that the root's deletion already destroys.
        /// </summary>
        private static List<GameObject> FindMissingInstances(Scene scene)
        {
            var found = new List<GameObject>();

            foreach (GameObject root in scene.GetRootGameObjects())
                Collect(root, found);

            return found;
        }

        private static void Collect(GameObject go, List<GameObject> into)
        {
            if (PrefabUtility.IsPrefabAssetMissing(go))
            {
                into.Add(go);
                return;
            }

            foreach (Transform child in go.transform)
                Collect(child.gameObject, into);
        }

        private static string GetPath(GameObject go)
        {
            string path = go.name;
            for (Transform t = go.transform.parent; t != null; t = t.parent)
                path = t.name + "/" + path;

            return path;
        }
    }
}
