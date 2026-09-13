using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpaceGame.World.NavMeshTools
{
    /// <summary>
    /// Decides whether the baked world NavMesh still matches the world.
    ///
    /// A pre-baked NavMesh trades a runtime cost for a correctness risk: edit a chunk scene, forget
    /// to re-bake, and agents walk on a mesh that no longer describes the ground. This is the thing
    /// that stops that being a silent failure — comparing each chunk scene's current dependency
    /// hash against the one recorded at bake time. The dependency hash, not a timestamp, because it
    /// also moves when something the scene *references* changes: a re-baked feature mesh, an edited
    /// TerrainData.
    /// </summary>
    public static class WorldNavMeshStaleness
    {
        public readonly struct Result
        {
            public readonly List<string> Reasons;
            public bool IsStale => Reasons.Count > 0;

            public Result(List<string> reasons) => Reasons = reasons;

            public string Describe() => IsStale
                ? string.Join("\n  ", Reasons.Take(12))
                  + (Reasons.Count > 12 ? $"\n  ...and {Reasons.Count - 12} more" : "")
                : "up to date";
        }

        /// <summary>
        /// Compares the baked asset against the chunk scenes the config currently lists.
        /// Every reason is reported, not just the first, so one re-bake fixes everything.
        /// </summary>
        public static Result Check(WorldStreamingConfig config, WorldNavMeshAsset asset)
        {
            var reasons = new List<string>();

            if (asset == null)
            {
                reasons.Add($"no baked asset at {WorldNavMeshBaker.AssetPath}");
                return new Result(reasons);
            }

            if (asset.bakedData == null)
                reasons.Add("baked asset has no NavMesh data");

            if (config == null || config.chunks == null || config.chunks.Length == 0)
            {
                reasons.Add("streaming config lists no chunks");
                return new Result(reasons);
            }

            var baked = new Dictionary<string, WorldNavMeshAsset.Stamp>();
            foreach (var stamp in asset.stamps)
            {
                if (!string.IsNullOrEmpty(stamp.sceneGuid))
                    baked[stamp.sceneGuid] = stamp;
            }

            var seen = new HashSet<string>();

            foreach (var chunk in config.chunks)
            {
                if (string.IsNullOrEmpty(chunk.scenePath)) continue;

                string guid = AssetDatabase.AssetPathToGUID(chunk.scenePath);
                if (string.IsNullOrEmpty(guid))
                {
                    reasons.Add($"{chunk.sceneName}: scene missing from the project");
                    continue;
                }

                seen.Add(guid);

                if (!baked.TryGetValue(guid, out var stamp))
                {
                    reasons.Add($"{chunk.sceneName}: in the config but not in the bake");
                    continue;
                }

                string current = AssetDatabase.GetAssetDependencyHash(chunk.scenePath).ToString();
                if (current != stamp.dependencyHash)
                    reasons.Add($"{chunk.sceneName}: changed since the bake");
            }

            foreach (var stamp in asset.stamps)
            {
                if (!string.IsNullOrEmpty(stamp.sceneGuid) && !seen.Contains(stamp.sceneGuid))
                    reasons.Add($"{stamp.sceneName}: baked in but no longer in the config");
            }

            return new Result(reasons);
        }

        /// <summary>
        /// Checks the baked asset against the config it records. Falls back to
        /// <see cref="WorldNavMeshBaker.LoadConfig"/> only when the asset names no config — an
        /// un-baked asset — so this can never compare a bake against a different world's chunk list.
        /// </summary>
        public static Result Check()
        {
            var asset = AssetDatabase.LoadAssetAtPath<WorldNavMeshAsset>(WorldNavMeshBaker.AssetPath);
            var config = asset != null && asset.config != null
                ? asset.config
                : WorldNavMeshBaker.LoadConfig();

            return Check(config, asset);
        }

        [MenuItem("World/Streaming/Check World NavMesh Is Current", priority = 1)]
        private static void CheckMenu()
        {
            var result = Check();
            string message = result.IsStale
                ? "The baked world NavMesh is STALE:\n  " + result.Describe() +
                  "\n\nRun World/Streaming/Bake World NavMesh."
                : "The baked world NavMesh matches every chunk scene in the config.";

            Debug.Log("[WorldNavMesh] " + message);
            EditorUtility.DisplayDialog("World NavMesh", message, "OK");
        }
    }

    /// <summary>
    /// Fails the build when the baked NavMesh is stale.
    ///
    /// The requirement was that keeping this current be automatic. A menu item is not automatic —
    /// someone has to remember. Making the build refuse is what actually holds the guarantee.
    /// </summary>
    public class WorldNavMeshBuildCheck : IPreprocessBuildWithReport
    {
        /// <summary>
        /// Set while a build that ships NO chunk scenes is running.
        ///
        /// <para>
        /// The guard below exists because agents would otherwise navigate a world that has since
        /// been edited. A player that contains none of that world cannot get it wrong: there is no
        /// chunk to stream, so there is no baked mesh in use to be stale.
        /// <c>MultiplayerTestPlayerBuilder</c> builds exactly such a player on purpose, and a
        /// stale bake there is a refusal for a risk that is not present.
        /// </para>
        /// <para>
        /// Declared by the builder rather than worked out here because a
        /// <see cref="BuildReport"/> does not carry the scene list at preprocess time — the caller
        /// is the only thing that knows, so the caller is what says so.
        /// </para>
        /// </summary>
        public static bool BuildingWithoutChunks;

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (BuildingWithoutChunks) return;

            var result = WorldNavMeshStaleness.Check();
            if (!result.IsStale) return;

            throw new BuildFailedException(
                "The baked world NavMesh is out of date, so NPCs would navigate a world that no " +
                "longer exists. Run World/Streaming/Bake World NavMesh.\n  " + result.Describe());
        }
    }
}
