using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Writes every <see cref="QuestSite"/> in the streamed world into the <see cref="QuestSiteTable"/>
    /// that answers for sites whose chunk is not loaded. Run it after placing or moving a quest site
    /// prefab in a chunk scene; until then the waypoint to an unloaded site simply does not appear.
    ///
    /// <para>
    /// Reads every chunk scene named by every <see cref="WorldStreamingConfig"/>, plus whatever scenes
    /// are open, and puts the scene setup back afterwards.
    /// </para>
    /// </summary>
    public static class QuestSiteBaker
    {
        private const string TableAsset = "Assets/Game/Resources/" + QuestSite.TablePath + ".asset";

        [MenuItem("Tools/SpaceGame/Objectives/Bake Quest Sites")]
        public static void Bake()
        {
            var found = new Dictionary<string, QuestSiteTable.Entry>();
            var savedSetup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    Collect(SceneManager.GetSceneAt(i), found);

                List<string> paths = ChunkScenePaths();
                for (int i = 0; i < paths.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Baking Quest Sites", paths[i], (float)i / paths.Count);
                    if (SceneManager.GetSceneByPath(paths[i]).isLoaded) continue;

                    Scene scene = EditorSceneManager.OpenScene(paths[i], OpenSceneMode.Additive);
                    Collect(scene, found);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (savedSetup != null && savedSetup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(savedSetup);
            }

            var table = AssetDatabase.LoadAssetAtPath<QuestSiteTable>(TableAsset);
            if (table == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TableAsset));
                table = ScriptableObject.CreateInstance<QuestSiteTable>();
                AssetDatabase.CreateAsset(table, TableAsset);
            }

            table.Set(new List<QuestSiteTable.Entry>(found.Values));
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Quest] Baked {found.Count} quest site(s) into {TableAsset}.", table);
        }

        private static void Collect(Scene scene, Dictionary<string, QuestSiteTable.Entry> found)
        {
            if (!scene.isLoaded) return;

            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (QuestSite site in root.GetComponentsInChildren<QuestSite>(true))
            {
                if (string.IsNullOrEmpty(site.SiteId)) continue;

                if (found.ContainsKey(site.SiteId))
                    Debug.LogError($"[Quest] Quest site '{site.SiteId}' appears more than once; keeping the first.", site);
                else
                    found[site.SiteId] = new QuestSiteTable.Entry
                    {
                        siteId = site.SiteId,
                        position = site.transform.position,
                        radius = site.Radius,
                    };
            }
        }

        private static List<string> ChunkScenePaths()
        {
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(WorldStreamingConfig)))
            {
                var config = AssetDatabase.LoadAssetAtPath<WorldStreamingConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (config == null || config.chunks == null) continue;

                foreach (var chunk in config.chunks)
                    if (!string.IsNullOrEmpty(chunk.scenePath) && !paths.Contains(chunk.scenePath))
                        paths.Add(chunk.scenePath);
            }

            return paths;
        }
    }
}
