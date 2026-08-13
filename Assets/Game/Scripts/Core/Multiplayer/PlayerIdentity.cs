using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>
    /// Carries a player's chosen name across the session, so every peer can put a name to every
    /// other player.
    /// <para>
    /// Nothing in the project synced one before this: the lobby screen invents a
    /// <c>"Player" + Random</c> for the Unity Lobby service and drops it at the loading screen, and
    /// the match leaderboard falls back to <c>"Player {clientId + 1}"</c>. Both are per-screen
    /// labels, not an identity.
    /// </para>
    /// <para>
    /// It lives on the player prefab rather than on a session-wide manager because the player
    /// object is the one networked thing that already exists once per human, spawns and despawns on
    /// exactly the right events, and is replicated to every peer without anything extra being
    /// placed in a scene. The name is owner-write, so each client is the authority on its own name
    /// and no RPC round-trip is needed to change it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerIdentity : NetworkBehaviour
    {
        // 64 rather than 32 bytes: the limit players are shown is 20 characters, and a name in a
        // non-Latin script can run to four bytes a character.
        private readonly NetworkVariable<FixedString64Bytes> displayName = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private static readonly List<PlayerIdentity> instances = new();

        /// <summary>Raised when a player joins, leaves, or renames — anything that changes the list.</summary>
        public static event Action RosterChanged;

        /// <summary>Every player object currently spawned on this peer, host first.</summary>
        public static IReadOnlyList<PlayerIdentity> All => instances;

        /// <summary>
        /// The name to show. Falls back to the client id while the name is still in flight, so a
        /// player list never has a blank row for someone who has only just joined.
        /// </summary>
        public string DisplayName
        {
            get
            {
                string value = displayName.Value.ToString();
                return string.IsNullOrWhiteSpace(value) ? $"Player {OwnerClientId + 1}" : value;
            }
        }

        /// <summary>True for the peer hosting the session, which is worth marking in a player list.</summary>
        public bool IsSessionHost => OwnerClientId == NetworkManager.ServerClientId;

        public override void OnNetworkSpawn()
        {
            instances.Add(this);
            displayName.OnValueChanged += OnNameChanged;

            if (IsOwner)
            {
                PublishLocalName();
                GameSettings.Changed += PublishLocalName;
            }

            RosterChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            instances.Remove(this);
            displayName.OnValueChanged -= OnNameChanged;

            if (IsOwner)
                GameSettings.Changed -= PublishLocalName;

            RosterChanged?.Invoke();
        }

        // OnNetworkDespawn does not run for an object destroyed without a despawn (a hard
        // disconnect, or a scene unload during shutdown), and a stale entry in a static list
        // outlives the session it belonged to.
        private void OnDestroy()
        {
            if (instances.Remove(this))
                RosterChanged?.Invoke();
        }

        private void OnNameChanged(FixedString64Bytes previous, FixedString64Bytes current)
            => RosterChanged?.Invoke();

        private void PublishLocalName()
        {
            if (!IsOwner || !IsSpawned) return;

            var wanted = new FixedString64Bytes(Truncate(GameSettings.PlayerName));
            if (wanted.Equals(displayName.Value)) return;

            displayName.Value = wanted;
        }

        /// <summary>
        /// Assigning a string longer than the FixedString's capacity throws, so the byte length —
        /// not the character count — is what has to be checked before it is written.
        /// </summary>
        private static string Truncate(string name)
        {
            const int maxBytes = 60; // FixedString64Bytes holds 61; one spare for safety.

            string trimmed = GameSettings.SanitiseName(name);
            while (System.Text.Encoding.UTF8.GetByteCount(trimmed) > maxBytes && trimmed.Length > 1)
                trimmed = trimmed[..^1];

            return trimmed;
        }

        // ------------------------------------------------------------------- roster

        /// <summary>One row of the pause menu's player list.</summary>
        public readonly struct Entry
        {
            public readonly ulong ClientId;
            public readonly string Name;
            public readonly bool IsLocal;
            public readonly bool IsHost;

            /// <summary>Round-trip time in ms, or -1 where this peer cannot measure it.</summary>
            public readonly int PingMilliseconds;

            public Entry(ulong clientId, string name, bool isLocal, bool isHost, int ping)
            {
                ClientId = clientId;
                Name = name;
                IsLocal = isLocal;
                IsHost = isHost;
                PingMilliseconds = ping;
            }
        }

        /// <summary>
        /// The current session's players, host first then by join order.
        /// <para>
        /// Built from spawned player objects rather than from
        /// <c>NetworkManager.ConnectedClientsList</c> because that list is server-only — a client
        /// asking it gets an empty list and would show a session of one. Player objects are
        /// replicated to everyone, so every peer sees the same roster.
        /// </para>
        /// </summary>
        public static List<Entry> BuildRoster()
        {
            var rows = new List<Entry>(instances.Count);

            for (int i = 0; i < instances.Count; i++)
            {
                PlayerIdentity identity = instances[i];
                if (identity == null || !identity.IsSpawned) continue;

                rows.Add(new Entry(
                    identity.OwnerClientId,
                    identity.DisplayName,
                    identity.IsOwner,
                    identity.IsSessionHost,
                    MeasurePing(identity.OwnerClientId)));
            }

            rows.Sort((a, b) =>
            {
                if (a.IsHost != b.IsHost) return a.IsHost ? -1 : 1;
                return a.ClientId.CompareTo(b.ClientId);
            });

            return rows;
        }

        /// <summary>
        /// RTT is measured by the transport against a connection it owns. The server holds a
        /// connection to every client so it can report all of them; a client only holds one, to the
        /// server, so it can report its own and nothing else.
        /// </summary>
        private static int MeasurePing(ulong clientId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening) return -1;

            bool measurable = manager.IsServer || clientId == manager.LocalClientId;
            if (!measurable) return -1;

            // The host measuring itself is a loopback with no meaningful number in it.
            if (manager.IsServer && clientId == manager.LocalClientId) return 0;

            NetworkTransport transport = manager.NetworkConfig?.NetworkTransport;
            if (transport == null) return -1;

            ulong rtt = transport.GetCurrentRtt(manager.IsServer ? clientId : NetworkManager.ServerClientId);
            return (int)rtt;
        }
    }
}
