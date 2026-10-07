using System.Collections.Generic;
using Unity.Netcode;

namespace SpaceGame.Core
{
    /// <summary>
    /// The current session's players as rows for a player list, read off the spawned
    /// <see cref="PlayerIdentity"/> objects. Redraw from <see cref="PlayerIdentity.RosterChanged"/>.
    /// </summary>
    public static class PlayerRoster
    {
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
        public static List<Entry> Build()
        {
            IReadOnlyList<PlayerIdentity> instances = PlayerIdentity.All;
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
