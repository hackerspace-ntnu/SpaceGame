using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Guards the registration of network prefabs, which fails in a way testing is unusually well
    /// suited to catch.
    ///
    /// Netcode spawns by hash: the server sends a prefab's GlobalObjectIdHash and each client looks
    /// it up in its own copy of the NetworkManager's prefab list. The server never performs that
    /// lookup — it instantiates the prefab it was handed. So an unregistered prefab produces a HOST
    /// THAT WORKS PERFECTLY and clients that silently spawn nothing. Playtesting alone (which for a
    /// solo developer means playing as the host) cannot find it.
    ///
    /// The project shipped with one prefab registered out of twenty-two, so no client could ever
    /// spawn a player.
    /// </summary>
    public class NetworkPrefabRegistrationTests
    {
        private const string NetworkManagerPrefabPath = "Assets/Game/Prefabs/Systems/NetworkManager.prefab";
        private const string NetworkedPlayerPrefabPath = "Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab";

        private static NetworkManager LoadNetworkManager()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath);
            Assert.IsNotNull(prefab, $"No NetworkManager prefab at {NetworkManagerPrefabPath}");

            var networkManager = prefab.GetComponent<NetworkManager>();
            Assert.IsNotNull(networkManager, "The NetworkManager prefab has no NetworkManager component.");
            return networkManager;
        }

        private static List<GameObject> RegisteredPrefabs(NetworkManager networkManager)
        {
            var registered = new List<GameObject>();

            foreach (NetworkPrefabsList list in networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists)
            {
                if (list == null) continue;

                foreach (NetworkPrefab entry in list.PrefabList)
                    if (entry?.Prefab != null)
                        registered.Add(entry.Prefab);
            }

            return registered;
        }

        private static List<string> NetworkedPrefabPathsInProject()
        {
            return AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path =>
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    return prefab != null && prefab.GetComponent<NetworkObject>() != null;
                })
                .ToList();
        }

        [Test]
        public void NetworkManager_ReferencesAtLeastOnePrefabList()
        {
            NetworkManager networkManager = LoadNetworkManager();

            List<NetworkPrefabsList> lists = networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists
                .Where(l => l != null)
                .ToList();

            Assert.IsNotEmpty(lists,
                "The NetworkManager references no NetworkPrefabsList, so clients can spawn nothing.");
        }

        [Test]
        public void NetworkedPlayerPrefab_IsRegistered()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkedPlayerPrefabPath);
            Assert.IsNotNull(player, $"No networked player prefab at {NetworkedPlayerPrefabPath}");

            CollectionAssert.Contains(RegisteredPrefabs(LoadNetworkManager()), player,
                "The networked player prefab is not in the NetworkManager's prefab list. The host " +
                "will spawn fine and every client will spawn no player at all. " +
                "Run Tools/SpaceGame/Multiplayer/Sync Network Prefabs.");
        }

        [Test]
        public void EveryNetworkedPrefabInTheProject_IsRegistered()
        {
            List<GameObject> registered = RegisteredPrefabs(LoadNetworkManager());
            var registeredPaths = new HashSet<string>(registered.Select(AssetDatabase.GetAssetPath));

            List<string> missing = NetworkedPrefabPathsInProject()
                .Where(path => !registeredPaths.Contains(path))
                .ToList();

            Assert.IsEmpty(missing,
                "These prefabs carry a NetworkObject but are not registered, so only the host will " +
                "ever see them:\n  " + string.Join("\n  ", missing) +
                "\nRun Tools/SpaceGame/Multiplayer/Sync Network Prefabs.");
        }

        /// <summary>
        /// Every item a player can hold is also an item a player can DROP, and dropping routes
        /// through PlayerDropService to GameServices.World.Spawn — which needs a NetworkObject on
        /// the prefab and that prefab in the list above.
        ///
        /// The test above only sees prefabs that already carry a NetworkObject, so it cannot catch
        /// the worse version of this mistake: an item prefab built without one at all. That is
        /// what happened to PortalGun. It equipped, aimed and fired perfectly, and the first drop
        /// logged "[WorldService] Prefab 'PortalGun' has no NetworkObject, so it will only ever
        /// exist on the server" and left nothing behind — the gun was simply gone.
        /// </summary>
        [Test]
        public void EveryInventoryItemPrefab_IsANetworkedAndRegisteredPrefab()
        {
            var registeredPaths = new HashSet<string>(
                RegisteredPrefabs(LoadNetworkManager()).Select(AssetDatabase.GetAssetPath));

            var unnetworked = new List<string>();
            var unregistered = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:InventoryItem"))
            {
                string itemPath = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<SpaceGame.Items.InventoryItem>(itemPath);

                // A blank itemPrefab is a separate defect and is not this test's business —
                // asserting it here would make one broken item fail two tests for two reasons.
                if (item == null || item.itemPrefab == null) continue;

                string prefabPath = AssetDatabase.GetAssetPath(item.itemPrefab);

                if (item.itemPrefab.GetComponent<NetworkObject>() == null)
                    unnetworked.Add($"{itemPath} -> {prefabPath}");
                else if (!registeredPaths.Contains(prefabPath))
                    unregistered.Add($"{itemPath} -> {prefabPath}");
            }

            Assert.IsEmpty(unnetworked,
                "These item prefabs have no NetworkObject on their root, so dropping one spawns " +
                "nothing and the item is destroyed:\n  " + string.Join("\n  ", unnetworked) +
                "\nAdd a NetworkObject (plus PickupableItem, a collider, a Rigidbody and " +
                "DropItemPhysics) — see LaserStaffBuilder for the whole block.");

            Assert.IsEmpty(unregistered,
                "These item prefabs are networked but unregistered, so only the host will ever " +
                "see one lying in the sand:\n  " + string.Join("\n  ", unregistered) +
                "\nRun Tools/SpaceGame/Multiplayer/Sync Network Prefabs.");
        }

        [Test]
        public void RegisteredPrefabs_AllStillExist()
        {
            NetworkManager networkManager = LoadNetworkManager();

            var broken = new List<string>();

            foreach (NetworkPrefabsList list in networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists)
            {
                if (list == null) continue;

                for (int i = 0; i < list.PrefabList.Count; i++)
                    if (list.PrefabList[i]?.Prefab == null)
                        broken.Add($"{AssetDatabase.GetAssetPath(list)} entry {i}");
            }

            // A null entry is not cosmetic: Netcode logs a validation error and refuses to start
            // with a broken list, which reads at runtime as "multiplayer is down" with no clue why.
            Assert.IsEmpty(broken, "Null entries in the network prefab list:\n  " + string.Join("\n  ", broken));
        }
    }
}
