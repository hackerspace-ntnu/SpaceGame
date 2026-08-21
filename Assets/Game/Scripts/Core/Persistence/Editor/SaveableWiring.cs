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
        /// <b>The casing is load-bearing and was wrong.</b> <c>AssetDatabase.FindAssets</c> matches
        /// folder paths case-sensitively, and the folder on disk is <c>agents</c>, not
        /// <c>Agents</c> — so the single largest population of saveable prefabs in the project was
        /// never visited by this pass at all. It found nothing and said nothing, because a folder
        /// that matches no assets is indistinguishable from a folder with no saveable assets in it.
        /// <see cref="ResolveSearchFolders"/> now refuses to run over a path that does not exist.
        private static readonly string[] SearchFolders =
        {
            "Assets/Game/Prefabs/agents",
            "Assets/Game/Prefabs/Items",
            "Assets/Game/Prefabs/Environment",
            "Assets/Game/Prefabs/Vehicles",

            // The player lives here. It is SaveScope.External so it gets no world record, but it
            // still needs its identity and its savers kept in step with the policy.
            "Assets/Game/Prefabs/Characters",

            // Anything a designer filed for runtime restore. Prefabs here are resolved by
            // SaveablePrefabRegistry through their stamped prefabId, so an unstamped one in this
            // folder is exactly the case that logs "has a SaveableEntity with no prefab id".
            "Assets/Game/Resources/Saveable",

            "Assets/Game/Prefabs/Systems",
        };

        /// <summary>
        /// The search folders that actually exist, warning about any that do not.
        ///
        /// A misspelled or re-cased folder in <see cref="SearchFolders"/> is silent otherwise: the
        /// pass reports "0 wired" and looks like it found nothing to do.
        /// </summary>
        private static string[] ResolveSearchFolders()
        {
            var live = new List<string>(SearchFolders.Length);

            foreach (string folder in SearchFolders)
            {
                if (AssetDatabase.IsValidFolder(folder)) live.Add(folder);
                else Debug.LogWarning($"[SaveableWiring] Search folder '{folder}' does not exist — " +
                                      "nothing in it will be wired. Fix the path or remove it.");
            }

            return live.ToArray();
        }

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

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", ResolveSearchFolders()))
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

                bool wired = Wire(prefab, out string added);
                bool stamped = StampPrefabId(prefab, guid);

                if (wired || stamped)
                {
                    changed++;
                    report.Append("  + ").Append(System.IO.Path.GetFileNameWithoutExtension(path))
                          .Append("  [").Append(why).Append(']');
                    if (wired) report.Append("  added: ").Append(added);
                    if (stamped) report.Append("  stamped prefabId");
                    report.Append('\n');
                    EditorUtility.SetDirty(prefab);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report.Append($"  {changed} prefab(s) wired, {skipped} skipped (no state worth saving).");
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Writes the prefab's own GUID into its <see cref="SaveableEntity.PrefabId"/> and returns
        /// whether that changed anything.
        ///
        /// <b>This is the whole reason a build persisted less than the editor did.</b> The field was
        /// only ever assigned by <c>SaveableEntity.OnValidate</c>, which is inside
        /// <c>#if UNITY_EDITOR</c> — so in the editor the value appeared the moment the asset was
        /// loaded into memory, and every runtime spawn inherited it and worked. Nothing wrote it to
        /// disk: this pass called <c>SetDirty</c> only when <see cref="Wire"/> had added a
        /// component, so a re-run over already-wired prefabs saved nothing, and every prefab in the
        /// project shipped with the field empty.
        ///
        /// In a player build <c>OnValidate</c> never runs, so the empty value is what
        /// <see cref="SaveablePrefabRegistry"/> sees. Two of its three lookup routes key on it, so
        /// they registered nothing at all, and every runtime-spawned world object was captured into
        /// the save and then dropped on load.
        ///
        /// Written through <c>SerializedObject</c> rather than by assigning the field, for the same
        /// reason the scene pass does: it is the only way the change is recorded as a real
        /// modification rather than a value that happens to match.
        /// </summary>
        private static bool StampPrefabId(GameObject prefab, string guid)
        {
            var entity = prefab.GetComponent<SaveableEntity>();
            if (entity == null || string.IsNullOrEmpty(guid)) return false;

            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path)) return false;

            // Compared against the FILE, never against entity.PrefabId.
            //
            // OnValidate has already stamped the in-memory value by the time this asset is loaded,
            // so the component always agrees with the GUID and an early-out on `entity.PrefabId ==
            // guid` skips every prefab in the project — which is precisely how the field stayed
            // empty on disk while looking correct everywhere in the editor. The only honest question
            // is what the serialized bytes say.
            if (SaveablePrefabFile.ReadPrefabId(path) == guid) return false;

            var so = new SerializedObject(entity);
            SerializedProperty prop = so.FindProperty("prefabId");
            if (prop == null) return false;

            prop.stringValue = guid;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(entity);

            // SavePrefabAsset, not SetDirty + SaveAssets.
            //
            // A GameObject returned by LoadAssetAtPath is the prefab's contents, and marking it dirty
            // does not make AssetDatabase.SaveAssets write the prefab file — which is the second half
            // of why this never landed. SavePrefabAsset is the call that serializes a prefab asset
            // loaded this way.
            PrefabUtility.SavePrefabAsset(prefab);
            return true;
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
