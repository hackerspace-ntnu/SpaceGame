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
//
// The pieces, one per file in this folder: NetArg is the payload, NetMsg the id catalog, NetHandler
// the delegate, NetTo/NetTarget the addressing, NetChannel the per-entity handler table and
// NetRelay the wire. Vocabulary/ holds the per-message constants some ids carry in A or B.
using UnityEngine;

namespace SpaceGame.Core
{
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
