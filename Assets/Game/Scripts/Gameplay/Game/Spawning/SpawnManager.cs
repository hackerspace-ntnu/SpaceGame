using System;
using System.Collections.Generic;
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

        // ─────────────────────────────────────────────
        //  Keeping four players out of one another
        // ─────────────────────────────────────────────
        //
        // Every client and every respawn used to be handed spawnPoints[0].TryGetSpawnPoint, and
        // that point's scatter has no idea who it has already placed. On the world's only spawn
        // point — a child of the drivable ShipRV, scattering inside half a metre on the cargo bay
        // floor — four players arriving together landed inside one another's colliders.
        //
        // It cannot be fixed by making the geometry test notice them: SpawnClearance.HasRoomToStand
        // excludes player bodies on purpose, because on a bay that small the first player standing
        // there blocks every candidate the point can produce and nobody else spawns indoors at all.
        // So who is where is tracked here instead, and handed to the point as a preference.
        //
        // Two sources, because a player can be in either state. A body that exists is found by its
        // tag. A position that has been handed out but not built on yet is not findable at all —
        // there is nothing there — so it is remembered until it either becomes a body or goes stale.

        /// <summary>
        /// How far apart two players placed in the same breath should be, when the room allows it.
        /// Five player-capsule radii: enough that nobody is inside anybody, close enough that a
        /// cargo bay can still seat four.
        /// </summary>
        private const float SpawnSeparation = 2.5f;

        /// <summary>
        /// How long a handed-out position keeps its claim.
        ///
        /// Only has to bridge the gap between resolving a position and a body appearing at it,
        /// which is the same frame in the normal case. The window exists for the abnormal one — a
        /// spawn that resolves and then never happens, because the client dropped out during the
        /// wait — where a claim held forever would push everybody else off a bay that is empty.
        /// Comfortably longer than NetworkGameManager's own spawn resolve timeout.
        /// </summary>
        private const float ReservationSeconds = 20f;

        private readonly List<Reservation> reservations = new();

        /// <summary>Reused between calls; spawning happens on the server, one client at a time.</summary>
        private readonly List<Vector3> occupied = new();

        private struct Reservation
        {
            public Vector3 Position;
            public float Expiry;
        }

        /// <summary>
        /// A validated spawn position, or false when no spawn point can currently vouch for one —
        /// which in the streamed world means the chunk there has not loaded yet. Callers must wait
        /// and ask again rather than substitute a position of their own.
        ///
        /// <para>
        /// Handing a position out CLAIMS it, so the next caller is steered away from it. That side
        /// effect is deliberate and belongs here rather than at the spawn call: a position resolved
        /// and then not used is exactly the case the claim's expiry covers, and every caller that
        /// asks this question is about to put a body there.
        /// </para>
        ///
        /// <para>
        /// With several spawn points it takes the roomiest answer rather than the first. That is
        /// safe in a streamed world without breaking the ordering NetworkGameManager documents,
        /// because a point in a chunk that has not loaded has no colliders to raycast and so can
        /// produce nothing at all — the geometry checks already refuse to answer for ground that
        /// does not exist. The anchor, which decides WHICH chunks load, is still
        /// <see cref="TryGetSpawnAnchor"/>'s single authored point and is deliberately not touched.
        /// </para>
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

            CollectOccupied(occupied);

            bool found = false;
            float bestClearance = float.NegativeInfinity;
            spawnPosition = Vector3.zero;

            foreach (SpawnPoint point in spawnPoints)
            {
                if (!point.TryGetSpawnPoint(occupied, SpawnSeparation,
                                            out Vector3 candidate, out float clearance))
                    continue;

                if (!found || clearance > bestClearance)
                {
                    found = true;
                    bestClearance = clearance;
                    spawnPosition = candidate;
                }

                // Already clear of everybody, so no other point can do better. In the main world,
                // which has exactly one spawn point, this loop is what it always was.
                if (clearance >= SpawnSeparation) break;
            }

            if (!found) return false;

            Reserve(spawnPosition);
            return true;
        }

        /// <summary>
        /// Where players are, or are about to be. Rebuilt on every ask because both halves move:
        /// bodies walk, and reservations expire.
        /// </summary>
        private void CollectOccupied(List<Vector3> into)
        {
            into.Clear();

            // The tag rather than a component, matching SpawnClearance — nothing but the player
            // character carries it, and it is the same question asked from the other side. Bodies
            // found this way include players parented to a mount, whose root keeps the tag.
            foreach (GameObject player in GameObject.FindGameObjectsWithTag(SpawnClearance.PlayerTag))
                if (player != null) into.Add(player.transform.position);

            float now = Time.time;

            for (int i = reservations.Count - 1; i >= 0; i--)
            {
                if (reservations[i].Expiry <= now)
                {
                    reservations.RemoveAt(i);
                    continue;
                }

                into.Add(reservations[i].Position);
            }
        }

        private void Reserve(Vector3 position)
        {
            reservations.Add(new Reservation
            {
                Position = position,
                Expiry = Time.time + ReservationSeconds,
            });
        }

        /// <summary>
        /// Last check before a body exists at this position. Everything upstream has already
        /// validated it, so this only catches the gap between validation and use — a chunk that
        /// finished loading in between, raising the ground above a position that was fine when it
        /// was chosen.
        ///
        /// A position standing on a built floor is exempt, and that exemption is what keeps this
        /// from being the very fault it exists to prevent. Being below the heightmap is only
        /// trouble when nothing is holding you up: inside a hull it is ordinary — the ship's cargo
        /// bay clears the sand outside by 0.91 m today and would be under it after one settle —
        /// and lifting a player from the floor they were standing on to the height of the ground
        /// outside puts them in that floor rather than out of it. This runs on the save-restore
        /// path too, where the position is wherever the player logged out, so the question has to
        /// be answered from the geometry rather than from who asked.
        /// </summary>
        private static Vector3 ClampAboveTerrain(Vector3 spawnPosition)
        {
            if (!TerrainProbe.TryGetTerrainHeight(spawnPosition, out float terrainY))
                return spawnPosition;

            if (spawnPosition.y >= terrainY)
                return spawnPosition;

            if (SpawnClearance.StandsOnStructure(spawnPosition, StructureFloorReach))
                return spawnPosition;

            Debug.LogWarning($"[SpawnManager] spawn position {spawnPosition} resolved below the " +
                             $"terrain surface (y={terrainY:F1}) with nothing under it — clamped above it.");

            return new Vector3(spawnPosition.x, terrainY + SpawnSurfaceClearance, spawnPosition.z);
        }

        /// <summary>
        /// How far under a spawn position to look for a floor. A shade over the clearance the
        /// position was lifted by, so the floor it was measured from is inside the reach and the
        /// deck below is not.
        /// </summary>
        private const float StructureFloorReach = SpawnSurfaceClearance + 0.5f;

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
        ///
        /// <para>
        /// Steers around the living for the same reason a first spawn does, and it matters more
        /// here: a respawn lands in a bay that is already occupied by definition, since the people
        /// who did not die are standing in it. The respawning player's own body is among the
        /// occupants — it is still there, dead, wherever they fell — which costs nothing unless
        /// they died on the spawn point, where being nudged off their own corpse is the outcome
        /// anybody would have asked for.
        /// </para>
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
