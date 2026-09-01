using System;
using Unity.Services.Lobbies;
using UnityEngine;

namespace SpaceGame.Core.Lobbies
{
    /// <summary>
    /// Keeps a hosted lobby listed.
    ///
    /// Lobby delists a lobby that has not been heartbeated inside 30 seconds. This pings it every
    /// <see cref="Interval"/> for as long as it is handed a lobby id, and never lets a slow ping
    /// pile a second one behind it — a request reissued every frame until it returned tripped the
    /// rate limiter and buried the real response under a pile of 429s.
    /// </summary>
    public sealed class LobbyHeartbeat
    {
        /// <summary>15s leaves room for a hiccup inside Lobby's 30s window.</summary>
        private const float Interval = 15f;

        private float timer;
        private bool inFlight;

        /// <summary>Counts down and pings when due. Pass null when there is nothing to keep alive.</summary>
        public void Tick(float deltaTime, string lobbyId)
        {
            if (string.IsNullOrEmpty(lobbyId) || inFlight) return;

            timer -= deltaTime;
            if (timer > 0f) return;

            timer = Interval;
            Ping(lobbyId);
        }

        private async void Ping(string lobbyId)
        {
            inFlight = true;

            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
            }
            catch (Exception e)
            {
                // Not surfaced to the player: a missed heartbeat only delists the lobby from
                // search, and the session itself keeps working. A warning here would fire
                // repeatedly over a flaky connection and bury messages that need acting on.
                Debug.LogWarning($"[LobbyHeartbeat] Heartbeat failed: {e.Message}");
            }
            finally { inFlight = false; }
        }
    }
}
