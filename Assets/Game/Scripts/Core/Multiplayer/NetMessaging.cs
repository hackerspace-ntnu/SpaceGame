// The one channel every gameplay system uses to talk across the network.
//
// It exists because the alternative was measured: each feature that got networked grew its own
// NetworkBehaviour with a hand-written Request/Server/Broadcast RPC triple — 40 to 175 lines
// apiece, times every artifact, agent, vehicle and weapon still to come. Here a feature registers
// a handler and sends a message, and the transport is somebody else's problem.
//
// The other half of the reason is failure behaviour. Forgetting the bespoke component was silent:
// the host worked, clients did nothing. Every entry point below instead falls back to running the
// handler locally, which is exactly what the game did before it had any netcode at all. A system
// nobody has networked yet keeps working single-player-style on each machine rather than throwing.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>
    /// The payload for every networked message.
    ///
    /// Deliberately one fixed struct rather than a type per message. Every call site in the game
    /// fits in these fields, a struct costs no allocation, and it means adding a message is a
    /// constant in <see cref="NetMsg"/> and nothing else — no serializer, no registration, no
    /// generated code.
    /// </summary>
    public struct NetArg : INetworkSerializable
    {
        /// <summary>A NetworkObjectId — the subject of the message. 0 means "none".</summary>
        public ulong Target;

        public int A;
        public int B;
        public Vector3 P;
        public Quaternion R;

        // Deliberately absent from NetworkSerialize, so it never leaves this machine.
        //
        // An id can only be minted for a spawned NetworkObject, which means that offline — and for
        // anything not networked — Target is 0 and Resolve would answer null. That would make
        // every message carrying a subject work online and break in single-player, which is the
        // exact failure this whole layer exists to prevent. On the local dispatch path the struct
        // is handed straight to the handler, so the reference is simply still here.
        private GameObject localTarget;

        public NetArg(ulong target = 0, int a = 0, int b = 0) : this()
        {
            Target = target;
            A = a;
            B = b;
            R = Quaternion.identity;
        }

        /// <summary>Point this message at <paramref name="go"/>. Works networked and offline.</summary>
        public NetArg With(GameObject go)
        {
            Target = IdOf(go);
            localTarget = go;
            return this;
        }

        /// <summary>As <see cref="With(GameObject)"/>, for a component on the subject.</summary>
        public NetArg With(Component component) =>
            With(component != null ? component.gameObject : null);

        /// <summary>The GameObject this message is about, or null if there is none or it is gone.</summary>
        public readonly GameObject Resolve()
        {
            if (localTarget != null) return localTarget;
            if (Target == 0 || !Network.IsNetworked) return null;

            var spawned = NetworkManager.Singleton.SpawnManager;
            if (spawned == null) return null;

            return spawned.SpawnedObjects.TryGetValue(Target, out NetworkObject obj) && obj != null
                ? obj.gameObject
                : null;
        }

        /// <summary>
        /// True when <see cref="R"/> carries a real orientation.
        ///
        /// A default-constructed NetArg leaves it all-zero, which is not a rotation — so this is how
        /// a handler tells "the sender told me where they were aiming" from "nobody filled this in".
        /// Distinguishing those matters: the alternative is for a peer to fall back on its own
        /// camera, which on the server means firing along the host's crosshair.
        /// </summary>
        public readonly bool HasOrientation =>
            R.x * R.x + R.y * R.y + R.z * R.z + R.w * R.w > 1e-4f;

        /// <summary>The id to put in <see cref="Target"/> for <paramref name="go"/>, or 0.</summary>
        public static ulong IdOf(GameObject go)
        {
            if (go == null) return 0;
            NetworkObject netObj = go.GetComponentInParent<NetworkObject>();
            return netObj != null && netObj.IsSpawned ? netObj.NetworkObjectId : 0;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Target);
            serializer.SerializeValue(ref A);
            serializer.SerializeValue(ref B);
            serializer.SerializeValue(ref P);
            serializer.SerializeValue(ref R);
        }
    }

    /// <summary>Receives a networked message. <paramref name="sender"/> is the client that sent it.</summary>
    public delegate void NetHandler(in NetArg arg, ulong sender);

    /// <summary>
    /// Every message id in the game, in one place so they are greppable and provably unique
    /// (NetMessagingTests asserts it). Ids are only ever appended — a reused number would route a
    /// message to the wrong handler, and the ids travel over the wire between builds.
    /// </summary>
    public static class NetMsg
    {
        // ── Items ──
        // (3 was Equip, retired: the hotbar selection already replicates, so every machine
        //  reaches the same equip on its own. Not reused — ids travel between builds.)
        public const ushort UseItem   = 1;  // owner → server: use what I have equipped
        public const ushort ItemUsed  = 2;  // server → peers: play the presentation for it

        // ── Combat ──
        public const ushort Damage    = 10; // → server, on the TARGET's relay. A = amount, Target = source

        // ── Riding ──
        public const ushort Mount     = 20; // → server, on the MOUNT's relay. Target = rider
        public const ushort Mounted   = 21; // server → peers
        public const ushort Dismount  = 22; // → server, on the MOUNT's relay
        public const ushort Dismounted = 23;

        // (30 was LaunchCraft, retired: a wing-pack launch is just a server-authoritative item use
        //  like any other. Not reused — ids travel between builds.)

        // ── Trading ──
        // A = index of the accepted offer on the trader. Target = the player taking it.
        // Sent to the TRADER's relay: the trader owns its own stock, and the server is the only
        // machine allowed to decide that two players did not both take the last water cell.
        public const ushort Trade     = 50;

        // ── Life cycle ──
        // No matching "Respawned" id: the server's answer to this is a heal (which the health
        // NetworkVariable already publishes) plus a placement (which NetworkedTeleport routes to
        // the owner). Neither needs a message of its own.
        public const ushort Respawn   = 40; // owner → server, on the PLAYER's relay
    }

    /// <summary>Sentinels for the client-id arguments.</summary>
    public static class NetTarget
    {
        /// <summary>"This machine" — resolved at send time, since offline there is no client id yet.</summary>
        public const ulong Self = ulong.MaxValue;
    }

    /// <summary>Where a message goes. Offline every direction collapses to "run it here".</summary>
    public enum NetTo
    {
        /// <summary>The server, which is the only machine allowed to change shared state.</summary>
        Server,
        /// <summary>Every machine including this one. Server-only — a client's send is dropped.</summary>
        All,
        /// <summary>Every machine except this one, for when the caller already acted locally.</summary>
        Others,
    }

    /// <summary>
    /// The API. Extension methods on Component so any script — networked or not, at any depth in
    /// the hierarchy — can join in without a reference to anything.
    /// </summary>
    public static class NetMessaging
    {
        /// <summary>
        /// Listen for <paramref name="id"/> on this entity. Pair with <see cref="NetOff"/> in
        /// OnDisable/OnDestroy.
        /// </summary>
        public static void NetOn(this Component self, ushort id, NetHandler handler)
        {
            NetChannel channel = NetChannel.GetOrAdd(self);
            if (channel != null) channel.Register(id, handler);
        }

        public static void NetOff(this Component self, ushort id, NetHandler handler)
        {
            // Find, never create: an object being torn down should not sprout a component on the
            // way out, and OnDisable ordering means the channel may already be gone.
            NetChannel channel = NetChannel.Find(self);
            if (channel != null) channel.Unregister(id, handler);
        }

        /// <summary>Ask the server to run <paramref name="id"/>. Runs it here if we are the server or offline.</summary>
        public static void NetToServer(this Component self, ushort id, NetArg arg = default) =>
            Send(self, id, arg, NetTo.Server);

        /// <summary>Server-side: run <paramref name="id"/> everywhere, including here.</summary>
        public static void NetToAll(this Component self, ushort id, NetArg arg = default) =>
            Send(self, id, arg, NetTo.All);

        /// <summary>
        /// Server-side: run <paramref name="id"/> everywhere except one machine.
        ///
        /// <paramref name="except"/> defaults to this one. Pass the client that made the request
        /// when relaying it onward: that player already acted locally the moment they pressed the
        /// button, and sending it back would play their effect twice.
        /// </summary>
        public static void NetToOthers(this Component self, ushort id, NetArg arg = default,
                                       ulong except = NetTarget.Self) =>
            Send(self, id, arg, NetTo.Others, except);

        /// <summary>
        /// As the others, but addressed to a different entity — the one that owns the state being
        /// changed. Damage uses this: the message belongs on the victim's channel, and it is the
        /// attacker who sends it.
        /// </summary>
        public static void NetSendTo(GameObject target, ushort id, NetArg arg, NetTo to = NetTo.Server)
        {
            if (target == null) return;
            Send(target.transform, id, arg, to);
        }

        private static void Send(Component self, ushort id, in NetArg arg, NetTo to,
                                 ulong except = NetTarget.Self)
        {
            if (self == null) return;

            NetRelay relay = NetRelay.Find(self);

            // No relay, or nothing to relay to. Running the handler here is the single-player
            // behaviour, which is the best available answer and never an error — see the file
            // header. NetChannel.WarnUnrelayed says so once per entity.
            if (relay == null || !relay.CanSend)
            {
                // Others means "everyone but me", so with no wire it is a no-op by definition —
                // and not worth warning about, since the local machine has already acted.
                if (to == NetTo.Others) return;

                NetChannel channel = NetChannel.Find(self);
                if (channel == null) return;

                if (Network.IsNetworked) channel.WarnUnrelayed(id);

                channel.Dispatch(id, arg, Network.LocalClientId);
                return;
            }

            relay.Send(id, arg, to, except);
        }
    }
}
