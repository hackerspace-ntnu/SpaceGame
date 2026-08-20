using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.World.Safety;

namespace SpaceGame.Gameplay
{
    public class SpawnManager : NetworkBehaviour
    {
        public static SpawnManager Instance;

        [Tooltip("The body every player gets. One prefab for every session: singleplayer runs as a " +
                 "host, so there is no un-networked spawn path to keep a second prefab for.")]
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

        /// <summary>
        /// The active spawn point's authored position, for choosing which chunks to preload.
        /// Available before any world geometry exists, which is exactly when the streamer needs it
        /// and exactly why it is not validated. Not a spawn position — see <see cref="SpawnPoint.Anchor"/>.
        /// </summary>
        public bool TryGetSpawnAnchor(out Vector3 anchor)
        {
            var spawnPoints = FindSpawnPoints();
            if (spawnPoints.Length == 0)
            {
                anchor = Vector3.zero;
                return false;
            }

            anchor = spawnPoints[0].Anchor;
            return true;
        }

        /// <summary>
        /// A validated spawn position, or false when no spawn point can currently vouch for one —
        /// which in the streamed world means the chunk there has not loaded yet. Callers must wait
        /// and ask again rather than substitute a position of their own.
        /// </summary>
        public bool TryGetSpawnPoint(out Vector3 spawnPosition)
        {
            var spawnPoints = FindSpawnPoints();
            if (spawnPoints.Length == 0)
            {
                Debug.LogError("No SpawnPoint found in scene!");
                spawnPosition = Vector3.zero;
                return false;
            }

            return spawnPoints[0].TryGetSpawnPoint(out spawnPosition);
        }

        /// <summary>
        /// Last check before a body exists at this position. Everything upstream has already
        /// validated it, so this only catches the gap between validation and use — a chunk that
        /// finished loading in between, raising the ground above a position that was fine when it
        /// was chosen.
        /// </summary>
        private static Vector3 ClampAboveTerrain(Vector3 spawnPosition)
        {
            if (!TerrainProbe.TryGetTerrainHeight(spawnPosition, out float terrainY))
                return spawnPosition;

            if (spawnPosition.y >= terrainY)
                return spawnPosition;

            Debug.LogWarning($"[SpawnManager] spawn position {spawnPosition} resolved below the " +
                             $"terrain surface (y={terrainY:F1}) — clamped above it.");

            return new Vector3(spawnPosition.x, terrainY + SpawnSurfaceClearance, spawnPosition.z);
        }

        /// <summary>Matches SpawnPoint.groundClearance — see the note there on the player capsule.</summary>
        private const float SpawnSurfaceClearance = 1.2f;

        /// <summary>
        /// A validated position to put a player back on their feet at, terrain-clamped and ready to
        /// be moved to — as opposed to <see cref="TryGetSpawnPoint"/>, which answers the raw spawn
        /// point and leaves clamping to whoever is about to instantiate a body there.
        ///
        /// Respawning deliberately does NOT go through this class any further than this. An earlier
        /// version despawned the player's NetworkObject and spawned a fresh one, which round-tripped
        /// the corpse through the save system: the despawn captured the dead body's health and
        /// position into the player's profile record, and the replacement body read it straight back
        /// out — so you respawned dead, at your own grave. Respawn is a state change on a living
        /// object, not a new object; see <c>PlayerRespawn</c>.
        /// </summary>
        public bool TryGetRespawnPosition(out Vector3 respawnPosition)
        {
            if (!TryGetSpawnPoint(out respawnPosition)) return false;

            respawnPosition = ClampAboveTerrain(respawnPosition);
            return true;
        }

        public void SpawnPlayerForClient(ulong clientId)
        {
            if (!TryGetSpawnPoint(out Vector3 spawnPosition))
            {
                Debug.LogError($"Cannot spawn client {clientId}: no valid SpawnPoint position!");
                return;
            }

            SpawnPlayerForClient(clientId, spawnPosition);
        }

        /// <summary>
        /// Spawns at a position the caller already resolved.
        ///
        /// This overload exists because resolving twice is a bug, not a convenience. SpawnPoint
        /// scatters its result inside a radius, so two calls return two different positions —
        /// NetworkGameManager used to preload the world's chunks around the first and then spawn
        /// the player at the second, which is how a player ends up standing in a chunk that was
        /// never loaded and falls straight through it.
        /// </summary>
        public void SpawnPlayerForClient(ulong clientId, Vector3 spawnPosition) =>
            SpawnPlayerForClient(clientId, spawnPosition, Quaternion.identity);

        /// <summary>
        /// As above, but facing a specific direction. Loading a save is the only caller that has an
        /// opinion about it — a spawn point deliberately does not, since it scatters its position
        /// and an authored facing would put every player in a group looking the same way.
        /// </summary>
        public void SpawnPlayerForClient(ulong clientId, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            spawnPosition = ClampAboveTerrain(spawnPosition);
            GameObject playerObj = Instantiate(networkPlayerPrefab, spawnPosition, spawnRotation);

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
