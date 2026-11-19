// Starting an arena match: which side each client is on, and standing them at that side's spawn.
//
// The team is learned exactly as a versus match learns it (NetworkGameManager.Versus.cs) — an arena
// lobby is a versus lobby locked to two teams — so the wait and the roster are shared. Only where
// the body goes differs: an ArenaTeamSpawn instead of a team ship.
using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Arena;

namespace SpaceGame.Core
{
    public partial class NetworkGameManager
    {
        /// <summary>
        /// Puts a client on an arena team and spawns them at that team's spawn.
        ///
        /// <para>
        /// Held while a round is live (<see cref="ArenaMatch.AcceptsSpawns"/>): a player who joins
        /// mid-round enters at the next one rather than as a fresh body in an elimination. They
        /// wait behind the loading screen until then.
        /// </para>
        ///
        /// <para>
        /// Pressing Play in the arena scene directly has no lobby and so no versus session; the
        /// roster then simply fills the emptier of the arena's two sides.
        /// </para>
        /// </summary>
        private IEnumerator SpawnIntoArena(ulong clientId, ArenaMatch match)
        {
            int teamCount = ArenaRules.TeamCount;

            if (VersusSession.IsActive)
                yield return WaitForVersusTeam(clientId);

            int team = VersusTeamRoster.Assign(clientId, teamCount);

            while (match != null && !match.AcceptsSpawns)
            {
                if (!IsStillConnected(clientId)) yield break;
                yield return null;
            }

            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            float deadline = Time.time + spawnResolveTimeout;

            while (match == null || !match.TryGetSpawnPose(team, out position, out rotation))
            {
                if (match == null || Time.time >= deadline)
                {
                    Debug.LogError($"[NGM] Could not stand client {clientId} at " +
                                   $"{VersusRules.TeamName(team)}'s arena spawn after {spawnResolveTimeout}s. " +
                                   "Is an ArenaTeamSpawn for that team authored, with ground under it?");
                    yield break;
                }

                yield return null;
            }

            SpawnManager.Instance.SpawnPlayerForClient(clientId, position, rotation);
            PublishTeam(clientId, team);
        }

        private static bool IsStillConnected(ulong clientId) =>
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.ConnectedClientsIds.Contains(clientId);
    }
}
