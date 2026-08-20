using System;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>
    /// The one place anything asks "who is allowed to do this here".
    ///
    /// Every member answers safely with no NetworkManager at all, because a scene opened straight
    /// from the editor, a unit test and a torn-down session all look like that, and none of them
    /// should be a crash. Absent netcode means "offline single-player", which is the state in which
    /// this machine may do everything.
    /// </summary>
    public static class Network
    {
        public static bool IsNetworked =>
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening;

        public static bool Server =>
            IsNetworked && NetworkManager.Singleton.IsServer;

        public static bool Client =>
            IsNetworked && NetworkManager.Singleton.IsClient;

        /// <summary>This machine's client id, or 0 (the server's id) when offline.</summary>
        public static ulong LocalClientId =>
            IsNetworked ? NetworkManager.Singleton.LocalClientId : NetworkManager.ServerClientId;

        /// <summary>
        /// May this machine run <paramref name="entity"/>'s simulation — its AI, its physics, the
        /// decisions that change shared state?
        ///
        /// True offline and on the server. True as well for an entity with no NetworkObject: an
        /// unnetworked thing has no remote truth to defer to, so every machine simulating its own
        /// copy is the best available answer, and refusing would freeze it solid.
        /// </summary>
        public static bool Simulates(Component entity)
        {
            if (!IsNetworked) return true;
            if (Server) return true;

            NetworkObject netObj = entity != null ? entity.GetComponentInParent<NetworkObject>() : null;
            return netObj == null || !netObj.IsSpawned;
        }

        /// <summary>
        /// Is <paramref name="entity"/> this machine's to drive from local input?
        ///
        /// Ownership rather than authority: the player's own body, and a vehicle handed to them
        /// while they ride it. Same fallback as <see cref="Simulates"/> for unnetworked objects.
        /// </summary>
        public static bool Owns(Component entity)
        {
            if (!IsNetworked) return true;

            NetworkObject netObj = entity != null ? entity.GetComponentInParent<NetworkObject>() : null;
            if (netObj == null || !netObj.IsSpawned) return true;

            return netObj.IsOwner;
        }

        /// <summary>
        /// Executes an action locally if server or offline, otherwise calls the RPC action.
        /// </summary>
        public static void Execute(Action local, Action client)
        {
            if (!IsNetworked || Server)
            {
                // Local execution: either offline or we are the server
                local?.Invoke();
            }
            else
            {
                // Client: send to server
                client?.Invoke();
            }
        }
    }
}
