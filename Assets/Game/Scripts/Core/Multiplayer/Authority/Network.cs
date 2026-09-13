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
        /// Server-side: may <paramref name="sender"/> speak for <paramref name="subject"/>?
        ///
        /// <para>
        /// The counterpart of <see cref="Owns"/>, asked from the other end. A request arriving over
        /// <see cref="NetTo.Server"/> carries both a sender id and a reference to whatever it wants
        /// done, and nothing about the transport ties the two together — a client may name any body
        /// in the session. Every handler that acts on a body named in <c>NetArg.Target</c> has to
        /// ask this before it does, or one player can leave another's seat, claim another's
        /// station, or report struggles on another's behalf.
        /// </para>
        /// <para>
        /// A <paramref name="subject"/> with no spawned <see cref="NetworkObject"/> is REFUSED in a
        /// live session, and that is the strict half of each of three copies that had drifted
        /// apart. <c>SeatedRider</c> refused a missing NetworkObject but never asked whether the
        /// one it found was spawned; <c>VehicleStation</c> and <c>SnareReceiver</c> asked about the
        /// spawn and then waved the whole case through anyway. Nobody chose the divergence, and
        /// refusing is the answer that survives being wrong: an unnetworked body has no owner, so
        /// there is no sense in which a remote client is entitled to it, and every subject these
        /// callers actually name is a spawned player. A reference the server cannot tie to a
        /// network identity in a networked session is a bug or an attack, and neither deserves to
        /// be honoured.
        /// </para>
        /// <para>
        /// The two early-outs are not laxity. Offline there is one machine and no ownership to
        /// disagree about, and the server speaks for everyone by definition — a host acting on its
        /// own local dispatch arrives here as <see cref="NetworkManager.ServerClientId"/>.
        /// </para>
        /// </summary>
        public static bool MayActFor(GameObject subject, ulong sender)
        {
            if (!IsNetworked) return true;
            if (sender == NetworkManager.ServerClientId) return true;
            if (subject == null) return false;

            // GetComponent, not GetComponentInParent: NetArg.Resolve hands back the NetworkObject's
            // own GameObject, so a subject whose identity is on an ancestor did not come from a
            // resolve and is not something a client may name.
            NetworkObject body = subject.GetComponent<NetworkObject>();
            if (body == null || !body.IsSpawned) return false;

            return body.OwnerClientId == sender;
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
