// Opts the world's stateful objects into the save system.
//
// The save architecture was complete long before anything used it: SaveableEntity marks what
// persists, and the world store captures those objects per streamed chunk. But the component was
// on exactly ONE prefab (the player), so every save wrote `scenes: {}` — a technically correct
// record that nothing in the world was saveable. Creatures kept no health, vehicles forgot where
// they were parked, and a killed robot came back on every load.
//
// Marking ~25 prefabs by hand is the kind of job that gets done once, incompletely, and silently
// rots as prefabs are added. So it is a tool: it decides what needs saving from what components an
// object HAS, which means a new creature added next month is covered by re-running it rather than
// by remembering this file exists.
//
// Running it is no longer REQUIRED. The rule now lives in SaveablePolicy, and the world store
// applies the same rule at runtime as each scene is hydrated, so an object nobody wired still
// persists. What this pass buys is a better identity: it bakes a GUID into the prefab or scene
// file, which survives the object being renamed or moved in the hierarchy, where the runtime
// fallback derives an identity from exactly those things. Run it after adding content; nothing
// breaks if you forget.
//
// Run from: Tools ▸ Save System ▸ Wire Saveable Prefabs
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Gameplay;

namespace SpaceGame.Core.Persistence.EditorTools
{
    public static class SaveableWiring
    {
        /// <summary>
        /// Folders whose prefabs are world objects. Deliberately not "every prefab": UI, VFX,
        /// projectiles and system prefabs have no state worth a save record, and marking them would
        /// bloat every file with objects nobody can tell apart.
        /// </summary>
        private static readonly string[] SearchFolders =
        {
            "Assets/Game/Prefabs/Agents",
            "Assets/Game/Prefabs/Items",
            "Assets/Game/Prefabs/Environment",
            "Assets/Game/Prefabs/Vehicles",
        };

        [MenuItem("Tools/Save System/Wire Saveable Prefabs")]
        public static void WirePrefabs()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[SaveableWiring] Exit Play mode first — prefab edits made in play " +
                               "mode are discarded when it ends.");
                return;
            }

            var report = new StringBuilder("[SaveableWiring] Prefabs\n");
            int changed = 0;
            int skipped = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", SearchFolders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                // Unity refuses to save a prefab that has a missing script, and the attempt logs an
                // error per component. Report it once and move on rather than failing the pass —
                // a broken asset elsewhere in the project is not this tool's to fix.
                if (HasMissingScript(prefab))
                {
                    report.Append("  ! ").Append(System.IO.Path.GetFileNameWithoutExtension(path))
                          .Append("  SKIPPED: has a missing script and cannot be saved — ")
                          .Append(path).Append('\n');
                    skipped++;
                    continue;
                }

                if (!NeedsSaving(prefab, out string why))
                {
                    skipped++;
                    continue;
                }

                if (Wire(prefab, out string added))
                {
                    changed++;
                    report.Append("  + ").Append(System.IO.Path.GetFileNameWithoutExtension(path))
                          .Append("  [").Append(why).Append("]  added: ").Append(added).Append('\n');
                    EditorUtility.SetDirty(prefab);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report.Append($"  {changed} prefab(s) wired, {skipped} skipped (no state worth saving).");
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Adds identity and savers to every already-placed instance in the open scenes, then saves
        /// them.
        ///
        /// Separate from the prefab pass because the two fix different halves. Wiring the PREFAB
        /// covers objects spawned from it later; wiring the SCENE covers the hundreds already
        /// placed, which need an authored instanceId stored in the scene file. A prefab-only pass
        /// leaves every existing creature in the world unsaveable.
        /// </summary>
        [MenuItem("Tools/Save System/Wire Saveable Scene Objects")]
        public static void WireOpenScenes()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[SaveableWiring] Exit Play mode first.");
                return;
            }

            var report = new StringBuilder("[SaveableWiring] Scenes\n");
            int totalChanged = 0;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                int changed = 0;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        GameObject go = t.gameObject;
                        if (!NeedsSaving(go, out _)) continue;
                        if (Wire(go, out _)) changed++;
                    }
                }

                if (changed > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    totalChanged += changed;
                }

                report.Append("  ").Append(scene.name).Append(": ").Append(changed).Append(" object(s)\n");
            }

            report.Append($"  {totalChanged} object(s) wired across the open scenes.");
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Every chunk scene in turn, so the whole streamed world is covered rather than whichever
        /// chunks happen to be open. Chunk scenes are where the placed robots and props live.
        /// </summary>
        [MenuItem("Tools/Save System/Wire Saveable Chunk Scenes")]
        public static void WireChunkScenes()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[SaveableWiring] Exit Play mode first.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[SaveableWiring] Cancelled — unsaved changes were kept.");
                return;
            }

            string[] chunkGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Game/Scenes" });
            var report = new StringBuilder("[SaveableWiring] Chunk scenes\n");
            int touched = 0;
            int totalChanged = 0;

            foreach (string guid in chunkGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // Only the streamed world and its chunks. Test scenes, menus and the arena either
                // have no persistent world or are not part of one.
                if (!path.Contains("/Chunks/") && !path.EndsWith("persistentScene.unity")) continue;

                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int changed = 0;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        GameObject go = t.gameObject;
                        if (!NeedsSaving(go, out _)) continue;
                        if (Wire(go, out _)) changed++;
                    }
                }

                touched++;

                if (changed > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    totalChanged += changed;
                    report.Append("  ").Append(scene.name).Append(": ").Append(changed).Append('\n');
                }
            }

            report.Append($"  {totalChanged} object(s) across {touched} scene(s).");
            Debug.Log(report.ToString());
        }

        // ─────────────────────────────────────────────
        //  What counts as worth saving
        // ─────────────────────────────────────────────

        // The rule itself lives in SaveablePolicy, in the runtime assembly, so this pass and the
        // one the world store runs as it hydrates a scene cannot drift apart. Running this tool is
        // now an optimisation rather than a requirement: it bakes a GUID identity into the asset,
        // which survives renaming and re-parenting, where the runtime fallback derives an identity
        // from where the object sits.

        private static bool NeedsSaving(GameObject go, out string why) =>
            SaveablePolicy.NeedsSaving(go, out why);

        private static bool Wire(GameObject go, out string added) =>
            SaveablePolicy.Ensure(go, out added);

        /// <summary>
        /// A null in the component list is a script Unity could not resolve. Saving such a prefab
        /// throws, so they are reported and skipped instead.
        /// </summary>
        private static bool HasMissingScript(GameObject go)
        {
            foreach (Component c in go.GetComponentsInChildren<Component>(true))
                if (c == null) return true;

            return false;
        }
    }
}
