// The wire half of the NetMessaging channel — see NetMessaging.cs for the why.
//
// One component, three RPCs, and every gameplay message in the game rides on them. Add it beside a
// NetworkObject and everything under that object can talk; leave it off and everything under that
// object still works, locally, on each machine.
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Core
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetRelay : NetworkBehaviour
    {
        private NetChannel channel;

        /// <summary>
        /// True when a message sent now would actually reach somebody. False before spawn, after
        /// despawn and in offline play — in all of which the caller runs the handler locally
        /// instead, which is the whole degradation contract.
        /// </summary>
        public bool CanSend => IsSpawned && Network.IsNetworked;

        /// <summary>The relay for <paramref name="self"/>'s entity, or null if it has none.</summary>
        public static NetRelay Find(Component self)
        {
            GameObject root = NetChannel.RootOf(self);
            return root != null && root.TryGetComponent(out NetRelay relay) ? relay : null;
        }

        private void Awake() => channel = NetChannel.GetOrAdd(this);

        public void Send(ushort id, in NetArg arg, NetTo to, ulong except = NetTarget.Self)
        {
            switch (to)
            {
                case NetTo.Server:
                    // Already the server: no round trip, just run it. Keeps the host and a
                    // single-player session on the same code path as a remote client.
                    if (IsServer) Deliver(id, arg, Network.LocalClientId);
                    else ToServerRpc(id, arg);
                    break;

                case NetTo.All:
                    if (!RequireServer(id, to)) return;
                    ToAllRpc(id, arg);
                    break;

                case NetTo.Others:
                    if (!RequireServer(id, to)) return;
                    ToOthersRpc(id, arg, except == NetTarget.Self ? Network.LocalClientId : except);
                    break;
            }
        }

        /// <summary>
        /// Broadcasts are the server's to make. A client that tries is refused rather than
        /// silently ignored, because "my effect never showed up for anyone" is otherwise
        /// indistinguishable from a dropped packet.
        /// </summary>
        private bool RequireServer(ushort id, NetTo to)
        {
            if (IsServer) return true;

            Debug.LogWarning($"[Net] '{name}' tried to send message {id} to {to} from a client. " +
                             "Only the server may broadcast — send it to the server first.", this);
            return false;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ToServerRpc(ushort id, NetArg arg, RpcParams rpcParams = default) =>
            Deliver(id, arg, rpcParams.Receive.SenderClientId);

        [Rpc(SendTo.ClientsAndHost)]
        private void ToAllRpc(ushort id, NetArg arg) => Deliver(id, arg, NetworkManager.ServerClientId);

        [Rpc(SendTo.ClientsAndHost)]
        private void ToOthersRpc(ushort id, NetArg arg, ulong exclude)
        {
            // Netcode has no "everyone but the server" target, so the sender filters itself out on
            // arrival. One wasted local call, no extra RPC surface.
            if (Network.LocalClientId == exclude) return;
            Deliver(id, arg, NetworkManager.ServerClientId);
        }

        private void Deliver(ushort id, in NetArg arg, ulong sender)
        {
            // A message can land in the frame the object is being torn down. Dropping it is right:
            // nothing it could do to a dying entity matters, and dispatching would touch destroyed
            // components.
            if (channel == null) channel = NetChannel.Find(this);
            if (channel == null) return;

            channel.Dispatch(id, arg, sender);
        }
    }
}
