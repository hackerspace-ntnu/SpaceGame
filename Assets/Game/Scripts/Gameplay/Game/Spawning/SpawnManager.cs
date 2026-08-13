using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;
using SpaceGame.Agents;
using SpaceGame.Core;

namespace SpaceGame.Gameplay
{
    public class SpawnManager : NetworkBehaviour
    {
        public static SpawnManager Instance;

        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private GameObject networkPlayerPrefab;

        [Header("Targeting")]
        [Tooltip("Faction assigned to every spawned player so AI can see them. Without this the " +
                 "player is absent from EntityTargetRegistry and no enemy will ever target them. " +
                 "MatchManager overrides this with a team/solo faction while a match is running.")]
        [SerializeField] private FactionDefinition playerFaction;
        [SerializeField] private FactionRelationshipTable relationshipTable;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        /// <summary>
        /// Spawn points are re-scanned on every call rather than cached, since SpawnManager lives in
        /// persistentScene and activates before scenes loaded additively on top of it (e.g. the
        /// minigame scene) exist. Prefers a SpawnPoint in the current active scene — falls back to
        /// any loaded SpawnPoint so the main world (which has no additive scene on top) still works.
        /// </summary>
        private SpawnPoint[] FindSpawnPoints()
        {
            var all = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (all.Length == 0) return all;

            Scene active = SceneManager.GetActiveScene();
            var inActiveScene = Array.FindAll(all, sp => sp.gameObject.scene == active);
            return inActiveScene.Length > 0 ? inActiveScene : all;
        }

        public bool SpawnPointsAvailable()
        {
            return FindSpawnPoints().Length > 0;
        }

        public Vector3 GetSpawnPoint()
        {
            var spawnPoints = FindSpawnPoints();
            if (spawnPoints.Length == 0)
            {
                Debug.LogError("No SpawnPoint found in scene!");
                return Vector3.zero;
            }

            return spawnPoints[0].GetSpawnPoint();
        }
    
        private void SpawnPlayer()
        {
            if (!SpawnPointsAvailable())
            {
                Debug.LogError("Cannot spawn player: No SpawnPoint found!");
                return;
            }

            Vector3 spawnPosition = GetSpawnPoint();
            GameObject player = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
            EntityFaction.Ensure(player, playerFaction, relationshipTable);
        }

        public void SpawnNetworkPlayer()
        {
            RequestSpawnServerRpc(NetworkManager.Singleton.LocalClientId);
        }
    
    
        public void RespawnPlayer(GameObject player)
        {
            if (Network.IsNetworked)
            {
                NetworkObject netObj = player.GetComponent<NetworkObject>();
                RequestSpawnServerRpc(NetworkManager.Singleton.LocalClientId);
            }
            else
            {
                Destroy(player);
                SpawnPlayer();
            }
        }
    
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestSpawnServerRpc(ulong clientId)
        {
            var client = NetworkManager.Singleton.ConnectedClients[clientId];
            if (client.PlayerObject != null && client.PlayerObject.IsSpawned)
            {
                client.PlayerObject.Despawn();
            }
            SpawnPlayerForClient(clientId);
        }
    
        public void SpawnPlayerForClient(ulong clientId)
        {

            Vector3 spawnPosition = GetSpawnPoint();
            GameObject playerObj = Instantiate(networkPlayerPrefab, spawnPosition, Quaternion.identity);

            // Before the network spawn, so the entity is registered for targeting from its first
            // frame. MatchManager reassigns the faction below when a match is running.
            EntityFaction.Ensure(playerObj, playerFaction, relationshipTable);

            // Spawn it specifically as the Player Object for that ID
            playerObj.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);

            // MatchManager picks the team/faction and moves the player to that side's
            // spawn point. Only runs when one is present in the loaded scene (i.e. the
            // minigame flow), so the main game's plain spawn path is unaffected.
            var matchManager = FindFirstObjectByType<MatchManager>();
            if (matchManager != null)
                matchManager.RegisterPlayerEntity(playerObj);
        }
    }
}
