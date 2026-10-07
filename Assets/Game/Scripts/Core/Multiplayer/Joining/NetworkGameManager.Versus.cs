// Starting a versus match: which side each client is on, and putting them in that side's ship.
//
// The team is learned the same way the save profile next door is, and for the same reason — the
// server has to know it BEFORE the client has a body, because it decides where that body is made.
// Connection approval is off in this project, so the answer arrives on this scene object's own
// channel, which every client has as soon as it has persistentScene.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using Unity.Services.Lobbies.Models;
using SpaceGame.Characters;
using SpaceGame.Core.Lobbies;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Arrival;

namespace SpaceGame.Core
{
    public partial class NetworkGameManager
    {
        /// <summary>
        /// Adopts the match this peer is about to load into, from the lobby it is standing in.
        ///
        /// <para>
        /// Run by EVERY peer, host and client alike, before anything spawns. On the server that is
        /// load-bearing: <see cref="SpawnWhenReady"/> only takes the versus route when
        /// <see cref="VersusSession.IsActive"/>, so a match that never announced itself would spawn
        /// its players on the story world's spawn point — which is what happened before this
        /// existed, because nothing in the project ever called <c>VersusSession.Begin</c> at all.
        /// </para>
        ///
        /// <para>
        /// Derived from the lobby rather than staged by the screen that started the match, so the
        /// host and every client compute it from the same source and cannot disagree. It CLEARS as
        /// readily as it begins: a peer who played a versus match and then loaded a story world
        /// would otherwise still be carrying an active session, and would be sent looking for a
        /// team ship in a world that has none.
        /// </para>
        /// </summary>
        private static void AdoptVersusSessionFromLobby()
        {
            // Existing, never Instance: asking this question in singleplayer must not conjure a
            // lobby session that then outlives every scene.
            Lobby lobby = LobbySession.Existing?.Current;

            if (lobby == null || !LobbyTeams.IsVersus(lobby))
            {
                VersusSession.Clear();
                return;
            }

            VersusSession.Begin(LobbyTeams.TeamCountOf(lobby),
                                LobbyTeams.TeamSizeOf(lobby),
                                LobbySession.Existing.LocalTeam,
                                LobbyTeams.TeamColorsOf(lobby, SuitPalette.Count));
        }

        /// <summary>
        /// A client telling the server which side it picked, before it has a body.
        ///
        /// Trusted only as far as it goes, exactly like the profile report beside it: this decides
        /// which ship someone starts in and nothing else. <see cref="VersusTeamRoster.Claim"/>
        /// checks the index against the match's own team count rather than believing it.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReportVersusTeamServerRpc(int team, RpcParams rpcParams = default)
        {
            VersusTeamRoster.Claim(rpcParams.Receive.SenderClientId, team, VersusSession.TeamCount);
        }

        /// <summary>
        /// Waits until this client has said which side it is on, or gives up and lets the roster
        /// choose for it.
        ///
        /// <para>
        /// Bounded for the reason <see cref="WaitForProfile"/> is: a client that never reports must
        /// not hold its own spawn open forever. Giving up costs the player their chosen side, which
        /// is worth a loud warning and is still better than never spawning.
        /// </para>
        /// </summary>
        private IEnumerator WaitForVersusTeam(ulong clientId)
        {
            // The host does not have to ask itself. Reading it locally also closes the gap where
            // the host's own spawn flow starts before its own RPC has been dispatched to itself.
            if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
            {
                VersusTeamRoster.Claim(clientId, VersusSession.LocalTeam, VersusSession.TeamCount);
                yield break;
            }

            float deadline = Time.time + Mathf.Max(0f, profileReportTimeout);

            while (!VersusTeamRoster.TryGet(clientId, out _))
            {
                if (NetworkManager.Singleton == null ||
                    !NetworkManager.Singleton.ConnectedClientsIds.Contains(clientId))
                    yield break;

                if (Time.time >= deadline)
                {
                    Debug.LogWarning($"[NGM] Client {clientId} never reported which team it picked, so " +
                                     "it is being put on whichever side is emptiest. It may not start " +
                                     "with the people it joined with.");
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>
        /// Puts a client on a team and spawns them in that team's ship.
        ///
        /// <para>
        /// The same two questions <see cref="SpawnWhenReady"/>'s streamed branch asks, in the same
        /// order and for the same reason. First WHICH CHUNKS, from the team's authored point, which
        /// is answerable before any ground exists and must be — the preload it feeds is what makes
        /// the ground exist. Only then WHERE TO STAND, from a ship grounded against terrain that
        /// has actually arrived.
        /// </para>
        ///
        /// <para>
        /// The team is settled before either, since it decides which point both of them are about.
        /// </para>
        /// </summary>
        private IEnumerator SpawnIntoTeamShip(ulong clientId)
        {
            VersusShipSpawner spawner = VersusShipSpawner.Instance;

            yield return WaitForVersusTeam(clientId);

            int team = VersusTeamRoster.Assign(clientId, VersusSession.TeamCount);

            if (worldStreamer)
            {
                while (!worldStreamer.IsReady)
                    yield return null;

                // EVERY team's anchor, not just this player's. The arrival lands a ship for every
                // team in the match — including ones nobody is on — and ground can only be measured
                // on chunks something has streamed, so a preload around this client's own side alone
                // would leave the opposition's ships circling terrain that does not exist. The
                // teams share an arena, so in practice this is the same handful of chunks.
                var anchors = new List<Vector3>();

                if (!spawner.TryGetAnchors(anchors))
                {
                    Debug.LogError($"[NGM] No spawn points for {VersusRules.TeamName(team)}'s arena — " +
                                   "refusing to stream and spawn around the world origin.");
                    yield break;
                }

                yield return WaitForWorldReady(anchors);
            }

            // A brand new match flies everybody down in their team's ship rather than starting them
            // parked in it, so it takes a different route to a body entirely. Deliberately after the
            // preload above, which is what gives every team's landing site ground to be measured
            // against.
            if (ArrivalDirector.Instance != null && ArrivalDirector.Instance.IsPending)
            {
                var attempt = new ArrivalDirector.Attempt();
                yield return ArrivalDirector.Instance.SpawnIntoVersusArrival(clientId, team, attempt);

                if (attempt.Handled)
                {
                    PublishTeam(clientId, team);
                    yield break;
                }

                // Not handled means the arrival gave up and said so; the ordinary placement below is
                // the fallback, and it is the same one a player joining after the landing takes.
            }

            // The ship needs ground under it, which in a streamed world arrives with the chunk. A
            // refusal here means "not yet" and is retried, exactly as the spawn point's is.
            Vector3 seatPosition = Vector3.zero;
            Quaternion seatRotation = Quaternion.identity;
            float deadline = Time.time + spawnResolveTimeout;

            while (!spawner.TryClaimSeat(team, out seatPosition, out seatRotation))
            {
                if (Time.time >= deadline)
                {
                    Debug.LogError($"[NGM] Could not put client {clientId} in " +
                                   $"{VersusRules.TeamName(team)}'s ship after {spawnResolveTimeout}s. " +
                                   "Leaving them unspawned rather than dropping them in open space.");
                    yield break;
                }

                yield return null;
            }

            SpawnManager.Instance.SpawnPlayerForClient(clientId, seatPosition, seatRotation);
            PublishTeam(clientId, team);
        }

        /// <summary>
        /// Tells every peer which side this player is on, now that they have a body to hang it off.
        ///
        /// Server-write, so it happens here rather than on the client that owns the body — see
        /// <c>PlayerIdentity.SetTeam</c> for why a team is not an owner-published value.
        /// </summary>
        private static void PublishTeam(ulong clientId, int team)
        {
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
                return;

            NetworkObject body = client.PlayerObject;
            if (body == null) return;

            var identity = body.GetComponent<PlayerIdentity>();
            if (identity != null) identity.SetTeam(team);
        }
    }
}
