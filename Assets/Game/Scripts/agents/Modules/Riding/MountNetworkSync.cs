// Replicates mount/dismount across the network.
//
// MountModule can't be a NetworkBehaviour itself — it extends BehaviourModuleBase (a MonoBehaviour)
// so the agent module system can tick it. So this sits alongside it and owns the networked half,
// the same split EquipmentNetworkSync/EquipmentController already uses.
//
// Authority model (minimum viable sync):
//   • Mounting is server-decided. A client asks; the server runs the real TryMount and, if it took,
//     tells everyone. This keeps two players from mounting the same animal on the same frame.
//   • Ownership of the mount transfers to the rider so their SteerModule can drive it and have the
//     resulting motion replicate through the mount's NetworkTransform. On dismount it goes back.
//   • Remote peers run the same TryMount/Dismount so the rider is visibly parented into the seat,
//     but MountModule.OnEnable/Update only drives cameras and input for the local owner anyway.
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(MountModule))]
    [RequireComponent(typeof(NetworkObject))]
    public class MountNetworkSync : NetworkBehaviour
    {
        private MountModule mount;

        // Set while a replicated mount/dismount is being applied, so the local events those raise
        // don't bounce straight back out as another RPC.
        private bool applyingRemote;

        private void Awake()
        {
            mount = GetComponent<MountModule>();
        }

        /// <summary>
        /// Entry point for interaction. Replaces a direct MountModule.TryMount call so the request
        /// goes through the server first. Returns immediately — the mount happens when the server says so.
        /// </summary>
        public void RequestMount(Interactor interactor)
        {
            if (interactor == null)
                return;

            NetworkObject rider = interactor.GetComponentInParent<NetworkObject>();

            Network.Execute(
                local: () => ServerMount(rider),
                client: () =>
                {
                    if (rider != null)
                        RequestMountServerRpc(rider);
                });
        }

        public void RequestDismount()
        {
            Network.Execute(
                local: ServerDismount,
                client: RequestDismountServerRpc);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestMountServerRpc(NetworkObjectReference riderRef)
        {
            if (!riderRef.TryGet(out NetworkObject rider))
                return;
            ServerMount(rider);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestDismountServerRpc() => ServerDismount();

        // ─────────── Server-side truth ───────────

        private void ServerMount(NetworkObject rider)
        {
            if (rider == null || mount.IsMounted)
                return;

            Interactor interactor = rider.GetComponentInChildren<Interactor>(true);
            if (interactor == null || !mount.CanMount(interactor))
                return;

            if (!ApplyMount(interactor))
                return;

            // Hand the mount to the rider so their local SteerModule input moves it and the motion
            // replicates outward from them. Without this the rider steers a body they don't own and
            // the server's NetworkTransform overwrites it every tick.
            if (Network.IsNetworked && NetworkObject.OwnerClientId != rider.OwnerClientId)
                NetworkObject.ChangeOwnership(rider.OwnerClientId);

            if (Network.IsNetworked)
                MountClientRpc(rider);
        }

        private void ServerDismount()
        {
            if (!mount.IsMounted)
                return;

            ApplyDismount();

            if (Network.IsNetworked)
            {
                if (NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
                    NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);

                DismountClientRpc();
            }
        }

        // ─────────── Replication to peers ───────────

        [Rpc(SendTo.ClientsAndHost)]
        private void MountClientRpc(NetworkObjectReference riderRef)
        {
            // The server already ran the real thing.
            if (IsServer) return;

            if (!riderRef.TryGet(out NetworkObject rider))
                return;

            Interactor interactor = rider.GetComponentInChildren<Interactor>(true);
            if (interactor == null)
                return;

            ApplyMount(interactor);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DismountClientRpc()
        {
            if (IsServer) return;
            ApplyDismount();
        }

        // ─────────── Local application ───────────

        private bool ApplyMount(Interactor interactor)
        {
            applyingRemote = true;
            try
            {
                return mount.TryMount(interactor, null);
            }
            finally
            {
                applyingRemote = false;
            }
        }

        private void ApplyDismount()
        {
            applyingRemote = true;
            try
            {
                mount.Dismount();
            }
            finally
            {
                applyingRemote = false;
            }
        }

        /// <summary>
        /// True while a server/RPC-driven change is being applied locally. MountModule raises its
        /// Mounted/Dismounted events during that window; anything listening and re-requesting should
        /// check this to avoid a feedback loop.
        /// </summary>
        public bool IsApplyingReplicatedChange => applyingRemote;
    }
}
