using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Gameplay;

namespace SpaceGame.Core.Lobbies
{
    /// <summary>
    /// The host's half of <see cref="LobbySession"/>: creating the lobby, starting the game, and
    /// the two live controls only a host can turn — privacy and the VS team rules.
    /// </summary>
    public partial class LobbySession
    {
        private const string HostOnlyControl = "Only the host can change this.";

        /// <summary>
        /// Allocates a Relay server, then advertises it as a lobby.
        ///
        /// Relay first. If it fails there is no lobby to clean up — the reverse order created the
        /// lobby, then allocated Relay, and on an allocation failure left an orphan lobby
        /// advertised to everyone with a join code that led nowhere.
        ///
        /// <para>
        /// A VS host allocates Relay for <see cref="VersusRules.MaxSeats"/> — the hard ceiling —
        /// however small the match they are starting is. Relay's allocation size is fixed the
        /// moment it is made and cannot grow afterward, but the whole point of the rules page's
        /// live steppers is that the host can grow a team once people are already standing in the
        /// lobby. The lobby's own advertised max follows the (possibly much smaller) starting
        /// rules instead, so a joiner reading "3/8" from the browser can trust that an eighth seat
        /// actually exists right now.
        /// </para>
        /// </summary>
        public async Task<bool> CreateAsync(string lobbyName, bool isPrivate, VersusSetup versus)
        {
            if (!TryBegin()) return false;

            try
            {
                if (!await EnsureReadyAsync()) return false;

                int relaySeats = versus.IsVersus ? VersusRules.MaxSeats : MaxPlayers;
                SessionResult host = await SessionLauncher.HostRelayAsync(relaySeats);
                if (!host.Success) { Fail(host.Error); return false; }

                string name = string.IsNullOrWhiteSpace(lobbyName) ? $"{PlayerName}'s game" : lobbyName;
                int lobbySeats = versus.IsVersus ? versus.Seats : MaxPlayers;

                Lobby created = await LobbyService.Instance.CreateLobbyAsync(name, lobbySeats,
                    LobbyOptions.Create(isPrivate, host.JoinCode, PlayerName, SuitColor, versus));

                Adopt(created, LobbyState.InLobby);

                Debug.Log($"[LobbySession] Hosting '{created.Name}' code={created.LobbyCode} relay={host.JoinCode}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Fail(LobbyServiceErrors.Describe(e, "Could not create the lobby."));
                SessionLauncher.Shutdown();
                return false;
            }
            finally { busy = false; }
        }

        /// <summary>
        /// Marks the lobby as playing and moves everyone into the world.
        ///
        /// The lobby is NOT locked and the heartbeat keeps running, so it stays listed and anyone
        /// joining later is synchronised into the running world by Netcode. There is deliberately
        /// no "wait for everyone to connect" gate: the host may start alone, and a client still
        /// completing its handshake is pulled into whatever scene the host is in when it lands.
        /// </summary>
        public async Task<bool> BeginGameAsync(string sceneName)
        {
            if (!TryBegin()) return false;

            try
            {
                if (!RequireHost("Only the host can start the game!")) return false;

                NetworkManager manager = NetworkManager.Singleton;
                if (manager == null || !manager.IsServer)
                {
                    Fail("The host is not running a server. Try recreating the lobby.");
                    return false;
                }

                Lobby started = await LobbyService.Instance.UpdateLobbyAsync(Current.Id, LobbyOptions.BeginGame());
                Adopt(started, LobbyState.InGame);

                Debug.Log($"[LobbySession] Starting '{sceneName}' for {manager.ConnectedClientsIds.Count} " +
                          "client(s). Lobby stays open for late joiners.");

                manager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Fail(LobbyServiceErrors.Describe(e, "Could not start the game."));
                return false;
            }
            finally { busy = false; }
        }

        /// <summary>
        /// Turns the lobby private or public.
        ///
        /// Private delists it: it stops appearing in the browser and is reachable only by its code.
        /// That is the whole of it — the code still works, which is what makes this the control a
        /// host actually wants when they only meant to keep strangers out.
        /// </summary>
        public Task<bool> SetPrivacyAsync(bool isPrivate) =>
            RequireHost(HostOnlyControl)
                ? UpdateLobbyAsync(LobbyOptions.Privacy(isPrivate), "Could not change the session's privacy.")
                : Task.FromResult(false);

        /// <summary>
        /// Retunes a live VS lobby's team rules: how many teams, how big.
        ///
        /// Refused — rather than silently reassigning anyone — when the change would evict someone
        /// already standing where the new rules leave no room: see
        /// <see cref="VersusRules.CanSetTeamCount"/> and <see cref="VersusRules.CanSetTeamSize"/>,
        /// checked against the roster's own <see cref="LobbyTeams.Occupancy(Lobby)"/>.
        /// </summary>
        public Task<bool> SetTeamRulesAsync(int teamCount, int teamSize)
        {
            if (!RequireHost(HostOnlyControl)) return Task.FromResult(false);

            int[] occupancy = LobbyTeams.Occupancy(Current);

            if (!VersusRules.CanSetTeamCount(teamCount, occupancy, out string countRefusal))
            {
                Fail(countRefusal);
                return Task.FromResult(false);
            }

            if (!VersusRules.CanSetTeamSize(teamSize, occupancy, out string sizeRefusal))
            {
                Fail(sizeRefusal);
                return Task.FromResult(false);
            }

            return UpdateLobbyAsync(LobbyOptions.TeamRules(teamCount, teamSize),
                                    "Could not change the team rules.");
        }

        /// <summary>
        /// A change to the live lobby, after <see cref="RequireHost"/> has already said yes.
        ///
        /// Not routed through <see cref="TryBegin"/>. That guard exists to stop a double-click
        /// allocating two Relay servers; this allocates nothing, and blocking it would mean a host
        /// who toggles a control while the roster is mid-poll gets silently ignored.
        /// </summary>
        private async Task<bool> UpdateLobbyAsync(UpdateLobbyOptions options, string failureHeadline)
        {
            try
            {
                Lobby updated = await LobbyService.Instance.UpdateLobbyAsync(Current.Id, options);
                Adopt(updated, State);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Fail(LobbyServiceErrors.Describe(e, failureHeadline));

                // The screen renders from Current, so a failed update has to be announced or the
                // control keeps showing the state the host asked for rather than the one in force.
                Changed?.Invoke();
                return false;
            }
        }

        /// <summary>Whether this peer is hosting a lobby right now, reporting why not otherwise.</summary>
        private bool RequireHost(string notHostRefusal)
        {
            if (Current == null) { Fail("You are not in a lobby."); return false; }
            if (!IsHost) { Fail(notHostRefusal); return false; }
            return true;
        }
    }
}
