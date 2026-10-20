// Every world and its chunk scenes, for editor tools that visit them all. The configs' own scene paths can
// differ from the disk in case (Scenes/World vs Scenes/world) and the AssetDatabase is case-sensitive, so a
// path is resolved against the scenes that actually exist rather than trusted. A world with no chunks is played
// in one scene (its GameWorld's), and that scene stands in for its chunks.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using SpaceGame.Core;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class WorldChunkScenes
    {
        /// <summary>Every <see cref="WorldStreamingConfig"/> in the project: one per world.</summary>
        public static List<WorldStreamingConfig> AllConfigs()
        {
            var configs = new List<WorldStreamingConfig>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(WorldStreamingConfig)))
            {
                var config = AssetDatabase.LoadAssetAtPath<WorldStreamingConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (config != null) configs.Add(config);
            }
            return configs;
        }

        /// <summary>
        /// The on-disk path of each of <paramref name="config"/>'s chunk scenes, in grid order; for a world with no
        /// chunks, the one scene it is played in. A chunk whose scene does not exist is reported through
        /// <paramref name="problem"/> and skipped.
        /// </summary>
        public static List<string> ScenePaths(WorldStreamingConfig config, Action<string> problem)
        {
            var onDisk = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                onDisk[path] = path;
            }

            var paths = new List<string>();
            if (config.chunks == null || config.chunks.Length == 0)
            {
                string single = SingleScenePath(config, onDisk.Values, problem);
                if (single != null) paths.Add(single);
                return paths;
            }

            foreach (ChunkInfo chunk in config.chunks)
            {
                if (string.IsNullOrEmpty(chunk.scenePath)) continue;
                if (!onDisk.TryGetValue(chunk.scenePath, out string path))
                {
                    problem($"{config.name}: chunk {chunk.gridCoord} has no scene at '{chunk.scenePath}'");
                    continue;
                }
                if (!paths.Contains(path)) paths.Add(path);
            }
            return paths;
        }

        /// <summary>
        /// The scene of the <see cref="GameWorld"/> whose save identity is <paramref name="config"/>, or null when no
        /// world uses it. A GameWorld names its scene only by file name, so the path is looked up among the scenes on disk.
        /// </summary>
        private static string SingleScenePath(WorldStreamingConfig config, IEnumerable<string> scenesOnDisk, Action<string> problem)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(GameWorld)))
            {
                var world = AssetDatabase.LoadAssetAtPath<GameWorld>(AssetDatabase.GUIDToAssetPath(guid));
                if (world == null || world.Identity != config) continue;

                foreach (string path in scenesOnDisk)
                    if (Path.GetFileNameWithoutExtension(path) == world.SceneName) return path;

                problem($"{config.name}: world '{world.name}' has no scene named '{world.SceneName}'");
                return null;
            }
            return null;
        }
    }
}
