using System;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace SpaceGame.Core.Lobbies
{
    /// <summary>
    /// Refreshes the lobby this peer is in, so the roster follows everyone else's changes.
    ///
    /// Fetches every <see cref="Interval"/> for as long as it is handed a lobby id, one request at
    /// a time. What comes back is handed to the owner rather than applied here: the owner is the
    /// one who knows whether the lobby was left while the request was in flight, and writing a
    /// stale result back would resurrect a lobby already left.
    /// </summary>
    public sealed class LobbyPoll
    {
        /// <summary>Lobby's GET rate limit is one call per second per lobby; 2s stays clear of it.</summary>
        private const float Interval = 2f;

        private readonly Action<Lobby> onPolled;
        private readonly Action onClosed;

        private float timer;
        private bool inFlight;

        /// <param name="onPolled">A fresh copy of the lobby.</param>
        /// <param name="onClosed">The lobby no longer exists — the host closed it.</param>
        public LobbyPoll(Action<Lobby> onPolled, Action onClosed)
        {
            this.onPolled = onPolled ?? throw new ArgumentNullException(nameof(onPolled));
            this.onClosed = onClosed ?? throw new ArgumentNullException(nameof(onClosed));
        }

        /// <summary>Counts down and fetches when due. Pass null when there is nothing to watch.</summary>
        public void Tick(float deltaTime, string lobbyId)
        {
            if (string.IsNullOrEmpty(lobbyId) || inFlight) return;

            timer -= deltaTime;
            if (timer > 0f) return;

            timer = Interval;
            Fetch(lobbyId);
        }

        private async void Fetch(string lobbyId)
        {
            inFlight = true;

            try
            {
                onPolled(await LobbyService.Instance.GetLobbyAsync(lobbyId));
            }
            catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.LobbyNotFound)
            {
                onClosed();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbyPoll] Poll failed: {e.Message}");
            }
            finally { inFlight = false; }
        }
    }
}
