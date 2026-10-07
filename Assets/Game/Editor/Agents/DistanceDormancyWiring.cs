// Getting DistanceDormant onto every person and animal prefab, and keeping it there and off every
// machine (SimulationDistance.md). Called from BOTH ends, like AgentGroundConformWiring: the menu item
// fixes every prefab nobody generates, and each builder calls Ensure so a rebuild cannot drop it.
//
// Scope is by folder because the folders already sort the agents: people, robots and animals under
// Agents/Characters, Robots and creatures, and the caravan's mounted variants of them under
// Agents/Caravan; vehicles, ships and the sky fleet elsewhere. Matched without case — git holds both
// Prefabs/Agents/ and Prefabs/agents/.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public static class DistanceDormancyWiring
    {
        public enum WireResult { Unchanged, Marked, Stripped, Failed }

        private const string PrefabRoot = "Assets/Game/Prefabs";

        private static readonly string[] SubjectFolders =
        {
            "/prefabs/agents/characters/",
            "/prefabs/agents/robots/",
            "/prefabs/agents/creatures/",
            "/prefabs/agents/caravan/",
        };

        public static bool IsSubjectPath(string assetPath)
        {
            string lower = assetPath.Replace('\\', '/').ToLowerInvariant();
            return SubjectFolders.Any(lower.Contains);
        }

        /// <summary>Puts a DistanceDormant beside every AgentController under <paramref name="root"/>. True if anything was added.</summary>
        public static bool Ensure(GameObject root)
        {
            bool changed = false;
            foreach (AgentController agent in root.GetComponentsInChildren<AgentController>(true))
            {
                if (agent.GetComponent<DistanceDormant>() != null) continue;
                agent.gameObject.AddComponent<DistanceDormant>();
                changed = true;
            }

            return changed;
        }

        private static bool Strip(GameObject root)
        {
            bool changed = false;
            foreach (DistanceDormant marker in root.GetComponentsInChildren<DistanceDormant>(true))
            {
                Object.DestroyImmediate(marker, true);
                changed = true;
            }

            return changed;
        }

        [MenuItem("Tools/SpaceGame/Agents/Wire Distance Dormancy")]
        public static void WireAll()
        {
            int added = 0, removed = 0, failed = 0;
            foreach (string path in PrefabPathsInOrder())
            {
                WireResult result = WirePrefab(path);
                if (result == WireResult.Marked) added++;
                else if (result == WireResult.Stripped) removed++;
                else if (result == WireResult.Failed) failed++;
            }

            Debug.Log($"[DistanceDormancyWiring] Marked {added} prefab(s), stripped {removed}, failed to save {failed}.");
        }

        /// <summary>
        /// Every prefab the pass visits, bases before variants: a variant visited first would get its own
        /// copy, and then inherit a second from its base. Public so a long pass can be run in batches.
        /// </summary>
        public static IReadOnlyList<string> PrefabPathsInOrder() =>
            AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.ToLowerInvariant().Contains("/prefabs/spikes/"))
                .OrderBy(VariantDepth)
                .ToList();

        /// <summary>
        /// Marks or strips the one prefab at <paramref name="path"/> to match its folder, saving only on a change.
        /// Failed when Unity refuses the save -- a prefab with a missing script cannot be saved, and Unity logs why.
        /// </summary>
        public static WireResult WirePrefab(string path)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset.GetComponentInChildren<AgentController>(true) == null) return WireResult.Unchanged;

            bool subject = IsSubjectPath(path);
            bool marked = asset.GetComponentInChildren<DistanceDormant>(true) != null;
            if (subject == marked && (!subject || AllMarked(asset))) return WireResult.Unchanged;

            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!(subject ? Ensure(contents) : Strip(contents))) return WireResult.Unchanged;

                PrefabUtility.SaveAsPrefabAsset(contents, path, out bool saved);
                if (!saved) return WireResult.Failed;
                return subject ? WireResult.Marked : WireResult.Stripped;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static bool AllMarked(GameObject asset) =>
            asset.GetComponentsInChildren<AgentController>(true).All(a => a.GetComponent<DistanceDormant>() != null);

        private static int VariantDepth(string path)
        {
            int depth = 0;
            Object source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            while ((source = PrefabUtility.GetCorrespondingObjectFromSource(source)) != null)
                depth++;

            return depth;
        }
    }
}
