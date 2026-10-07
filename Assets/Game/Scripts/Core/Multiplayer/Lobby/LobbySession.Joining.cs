using System;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace SpaceGame.Core.Lobbies
{
    /// <summary>
    /// The joiner's half of <see cref="LobbySession"/>: entering a lobby by id or by code, and
    /// connecting to the Relay server it advertises.
    /// </summary>
    public partial class LobbySession
    {
        public Task<bool> JoinByIdAsync(string lobbyId) => JoinAsync(
            () => LobbyService.Instance.JoinLobbyByIdAsync(lobbyId,
                new JoinLobbyByIdOptions { Player = LobbyOptions.LocalPlayer(PlayerName, SuitColor) }),
            "Could not join that lobby.");

        public Task<bool> JoinByCodeAsync(string lobbyCode)
        {
            string code = SessionLauncher.NormalizeJoinCode(lobbyCode);

            if (string.IsNullOrEmpty(code))
            {
                Fail("Enter a lobby code first.");
                return Task.FromResult(false);
            }

            return JoinAsync(
                () => LobbyService.Instance.JoinLobbyByCodeAsync(code,
                    new JoinLobbyByCodeOptions { Player = LobbyOptions.LocalPlayer(PlayerName, SuitColor) }),
                "Could not join with that code.");
        }

        /// <summary>
        /// Joins, then connects to the Relay server the lobby advertises.
        ///
        /// Lobby membership is rolled back if the Relay connection fails. Otherwise a failed
        /// connection leaves a ghost occupying a slot in a lobby it is not in, which is how a
        /// four-player lobby ends up refusing a third player.
        ///
        /// The join itself goes through <see cref="LobbyJoinRecovery"/>, which clears out any
        /// membership an earlier session failed to give back.
        /// </summary>
        private async Task<bool> JoinAsync(Func<Task<Lobby>> join, string failureHeadline)
        {
            if (!TryBegin()) return false;

            try
            {
                if (!await EnsureReadyAsync()) return false;

                Lobby lobby = await LobbyJoinRecovery.JoinAsync(join,
                    () => LobbyService.Instance.GetJoinedLobbiesAsync(),
                    lobbyId => LobbyService.Instance.RemovePlayerAsync(lobbyId, LocalPlayerId));

                if (lobby == null) { Fail("The lobby service returned nothing."); return false; }

                string relayCode = LobbyData.Text(lobby, LobbyKeys.RelayJoinCode);
                if (string.IsNullOrEmpty(relayCode))
                {
                    await RemoveSelfQuietly(lobby.Id);
                    Fail("That lobby has no Relay server attached. The host may still be setting it up.");
                    return false;
                }

                SessionResult connected = await SessionLauncher.JoinRelayAsync(relayCode);
                if (!connected.Success)
                {
                    await RemoveSelfQuietly(lobby.Id);
                    Fail(connected.Error);
                    return false;
                }

                Adopt(lobby, LobbyRoster.IsPlaying(lobby) ? LobbyState.InGame : LobbyState.InLobby);

                Debug.Log($"[LobbySession] Joined '{lobby.Name}' ({State}).");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Fail(LobbyServiceErrors.Describe(e, failureHeadline));
                return false;
            }
            finally { busy = false; }
        }
    }
}
