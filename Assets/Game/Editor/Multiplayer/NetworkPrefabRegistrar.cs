// Keeps the NetworkManager's prefab list in sync with the prefabs that actually exist.
//
// Netcode replicates a spawn by hash, not by reference: the server sends the prefab's
// GlobalObjectIdHash and every client looks it up in its own copy of the list. A prefab missing
// from that list therefore fails on CLIENTS ONLY — the server instantiates its own copy directly
// and never consults the list, so the host sees a perfectly working game while every client sees
// nothing. That asymmetry is why this drifted unnoticed: the project shipped with the NetworkManager
// pointing at a list holding a single legacy prefab, so no client could spawn a player.
//
// Run this after adding any prefab with a NetworkObject.
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class NetworkPrefabRegistrar
    {
        private const string NetworkManagerPrefabPath = "Assets/Game/Prefabs/Systems/NetworkManager.prefab";

        [MenuItem("Tools/SpaceGame/Multiplayer/Sync Network Prefabs")]
        public static void SyncMenu()
        {
            string report = Sync(out int added, out int total);
            Debug.Log(report);
            EditorUtility.DisplayDialog("Sync Network Prefabs",
                added == 0
                    ? $"Already in sync — {total} networked prefabs registered."
                    : $"Added {added} missing prefab(s). {total} now registered.",
                "OK");
        }

        /// <summary>
        /// Adds every prefab carrying a root <see cref="NetworkObject"/> to the list the
        /// NetworkManager references. Returns a human-readable report.
        /// </summary>
        public static string Sync(out int added, out int total)
        {
            added = 0;
            total = 0;

            var networkManagerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath);
            if (networkManagerPrefab == null)
                return $"[NetworkPrefabRegistrar] No NetworkManager prefab at {NetworkManagerPrefabPath}.";

            var networkManager = networkManagerPrefab.GetComponent<NetworkManager>();
            if (networkManager == null)
                return $"[NetworkPrefabRegistrar] {NetworkManagerPrefabPath} has no NetworkManager component.";

            List<NetworkPrefabsList> lists = networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists;
            lists.RemoveAll(l => l == null);

            if (lists.Count == 0)
                return "[NetworkPrefabRegistrar] NetworkManager references no NetworkPrefabsList. " +
                       "Assign one in the inspector, then re-run.";

            // Pick the list that already holds the most prefabs rather than the first. The project
            // has several near-duplicate lists left over from a restructure ("DefaultNetworkPrefabs"
            // vs "DefaultNetworkPrefabs Unused"), and picking the fullest one avoids re-adding
            // dozens of entries to whichever empty stub happened to be wired up first.
            NetworkPrefabsList target = lists.OrderByDescending(l => l.PrefabList.Count).First();

            var missing = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                // Root only. Netcode requires the NetworkObject on the prefab root, and a prefab
                // whose NetworkObject sits on a child is a separate authoring bug this tool must
                // not paper over by registering something Netcode will reject at runtime.
                if (prefab.GetComponent<NetworkObject>() == null) continue;

                total++;

                if (target.Contains(prefab)) continue;

                target.Add(new NetworkPrefab { Prefab = prefab });
                missing.Add(path);
                added++;
            }

            if (added > 0)
            {
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssets();
            }

            // The NetworkManager may still point at a thinner list than the one just filled. Rewire
            // it, otherwise the sync silently writes to an asset nothing loads.
            if (!lists.Contains(target) || lists[0] != target)
            {
                lists.Remove(target);
                lists.Insert(0, target);
                EditorUtility.SetDirty(networkManagerPrefab);
                PrefabUtility.SavePrefabAsset(networkManagerPrefab);
                AssetDatabase.SaveAssets();
            }

            string listPath = AssetDatabase.GetAssetPath(target);
            var report = new System.Text.StringBuilder();
            report.AppendLine($"[NetworkPrefabRegistrar] Target list: {listPath}");
            report.AppendLine($"[NetworkPrefabRegistrar] {total} networked prefabs in project, {added} added.");
            foreach (string path in missing)
                report.AppendLine($"    + {path}");

            return report.ToString();
        }

        /// <summary>
        /// Reports without changing anything — what the NetworkManager would load right now.
        /// </summary>
        public static string Audit()
        {
            var networkManagerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath);
            var networkManager = networkManagerPrefab != null ? networkManagerPrefab.GetComponent<NetworkManager>() : null;
            if (networkManager == null) return "[NetworkPrefabRegistrar] NetworkManager prefab not found.";

            var report = new System.Text.StringBuilder();
            report.AppendLine($"PlayerPrefab: {(networkManager.NetworkConfig.PlayerPrefab == null ? "<none>" : networkManager.NetworkConfig.PlayerPrefab.name)}");

            foreach (NetworkPrefabsList list in networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists)
            {
                if (list == null) { report.AppendLine("  <null list entry>"); continue; }
                report.AppendLine($"  list {AssetDatabase.GetAssetPath(list)} -> {list.PrefabList.Count} prefabs");
            }

            int projectTotal = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Count(p => p != null && p.GetComponent<NetworkObject>() != null);

            report.AppendLine($"  networked prefabs in project: {projectTotal}");
            return report.ToString();
        }
    }
}
