using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.World;
using SpaceGame.World.NavMeshTools;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// A <see cref="Settlement"/>'s inspector button, or right-click it or a
    /// <see cref="RobotSettlementGenerator"/> → "Generate + Bake World NavMesh": generates it (a Settlement
    /// through <see cref="SettlementEditor.Generate"/>, residents and all), saves its chunk scene, and
    /// re-bakes the world NavMesh so agents can walk the new layout.
    ///
    /// A plain Generate leaves the world NavMesh stale — it is one author-time bake of every chunk
    /// scene as saved on disk (NavMeshSystem.md), so NPCs keep pathing through the new buildings
    /// and the player build refuses to start. The baker also refuses while any chunk scene is open,
    /// which is exactly when a settlement has just been generated; this closes them around the bake
    /// and puts the editor's scenes back afterwards.
    /// </summary>
    public static class SettlementNavMeshBake
    {
        public const string MenuLabel = "Generate + Bake World NavMesh";
        private const string DialogTitle = "Generate + Bake World NavMesh";

        [MenuItem("CONTEXT/Settlement/" + MenuLabel)]
        private static void GenerateSettlement(MenuCommand command) => GenerateAndBake((Settlement)command.context);

        public static void GenerateAndBake(Settlement settlement) =>
            GenerateAndBake(settlement, () => SettlementEditor.Generate(settlement));

        [MenuItem("CONTEXT/RobotSettlementGenerator/" + MenuLabel)]
        private static void GenerateRobotSettlement(MenuCommand command)
        {
            var generator = (RobotSettlementGenerator)command.context;
            GenerateAndBake(generator, generator.Generate);
        }

        [MenuItem("CONTEXT/Settlement/" + MenuLabel, true)]
        [MenuItem("CONTEXT/RobotSettlementGenerator/" + MenuLabel, true)]
        private static bool CanGenerateAndBake() => !EditorApplication.isPlaying;

        private static void GenerateAndBake(Component settlement, Action generate)
        {
            WorldStreamingConfig config = WorldNavMeshBaker.LoadConfig();
            if (config == null)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "No world streaming config to bake for — see the console.", "OK");
                return;
            }

            Scene scene = settlement.gameObject.scene;
            if (!IsChunkScene(config, scene.path))
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    $"'{settlement.name}' is in '{scene.path}', which is not one of the world's chunk " +
                    "scenes, so the world NavMesh never sees it. Move it into a chunk scene first.", "OK");
                return;
            }

            // The bake closes every chunk scene and reopens it from disk, so an unsaved edit in any
            // open scene would be thrown away.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string unsaved = DirtyScenes();
            if (unsaved.Length > 0)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "The bake reloads chunk scenes from disk. Save or revert these first:\n" + unsaved, "OK");
                return;
            }

            generate();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    $"Generated, but '{scene.path}' could not be saved, so the bake would not see it. " +
                    "Nothing was baked.", "OK");
                return;
            }
            SaveSculptedTerrain();

            GlobalObjectId selected = GlobalObjectId.GetGlobalObjectIdSlow(settlement.gameObject);
            string report = BakeWithChunkScenesClosed(config);
            Selection.activeObject = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(selected);

            Debug.Log(report);
            EditorUtility.DisplayDialog(DialogTitle, report, "OK");
        }

        /// <summary>
        /// Case-insensitive on purpose: the streaming config says <c>Scenes/World/Chunks</c> while
        /// the folder on disk is <c>Scenes/world/Chunks</c> (DEFECTS.md), and a scene's own path
        /// reports the disk spelling.
        /// </summary>
        private static bool IsChunkScene(WorldStreamingConfig config, string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath) || config.chunks == null) return false;
            foreach (var chunk in config.chunks)
            {
                if (string.Equals(chunk.scenePath, scenePath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string DirtyScenes()
        {
            var dirty = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isDirty) dirty.Add(string.IsNullOrEmpty(s.path) ? "(untitled)" : s.path);
            }
            return string.Join("\n", dirty);
        }

        /// <summary>
        /// <see cref="Settlement"/> sculpts <see cref="TerrainData"/> assets, which saving the scene
        /// does not write — without this the committed terrain would not be the one that was baked.
        /// </summary>
        private static void SaveSculptedTerrain()
        {
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                if (terrain != null && terrain.terrainData != null)
                    AssetDatabase.SaveAssetIfDirty(terrain.terrainData);
            }
        }

        private static string BakeWithChunkScenesClosed(WorldStreamingConfig config)
        {
            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                CloseChunkScenes(config);
                return WorldNavMeshBaker.Bake(config);
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        private static void CloseChunkScenes(WorldStreamingConfig config)
        {
            var chunks = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (IsChunkScene(config, s.path)) chunks.Add(s);
            }

            // The editor cannot close its last scene; an empty placeholder holds the slot and is
            // dropped again when the setup is restored.
            if (chunks.Count == SceneManager.sceneCount)
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            foreach (Scene s in chunks)
                EditorSceneManager.CloseScene(s, removeScene: true);
        }
    }
}
