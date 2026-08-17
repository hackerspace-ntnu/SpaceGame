// Replicates mount/dismount across the network.
//
// MountModule can't be a NetworkBehaviour itself — it extends BehaviourModuleBase (a MonoBehaviour)
// so the agent module system can tick it. So this sits alongside it and owns the networked half.
//
// Authority model:
//   • Mounting is server-decided. A client asks; the server runs the real TryMount and, if it took,
//     tells everyone. This keeps two players from mounting the same animal on the same frame.
//   • Ownership of the mount transfers to the rider so their SteerModule can drive it and have the
//     resulting motion replicate through the mount's NetworkTransform. On dismount it goes back.
//   • Remote peers run the same TryMount/Dismount so the rider is visibly parented into the seat,
//     but MountModule.OnEnable/Update only drives cameras and input for the local owner anyway.
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(MountModule))]
    public class MountNetworkSync : MonoBehaviour
    {
        private MountModule mount;

        // Set while a replicated mount/dismount is being applied, so the local events those raise
        // don't bounce straight back out as another request.
        private bool applyingRemote;

        private void Awake() => mount = GetComponent<MountModule>();

        private void OnEnable()
        {
            this.NetOn(NetMsg.Mount, OnMountRequested);
            this.NetOn(NetMsg.Dismount, OnDismountRequested);
            this.NetOn(NetMsg.Mounted, OnMountedElsewhere);
            this.NetOn(NetMsg.Dismounted, OnDismountedElsewhere);
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.Mount, OnMountRequested);
            this.NetOff(NetMsg.Dismount, OnDismountRequested);
            this.NetOff(NetMsg.Mounted, OnMountedElsewhere);
            this.NetOff(NetMsg.Dismounted, OnDismountedElsewhere);
        }

        // ─────────── Requests ───────────

        /// <summary>
        /// Entry point for interaction. Replaces a direct MountModule.TryMount call so the request
        /// goes through the server first. Returns immediately — the mount happens when the server says so.
        /// </summary>
        public void RequestMount(Interactor interactor)
        {
            if (interactor == null) return;

            // The rider is whatever body the interactor belongs to — its NetworkObject when there
            // is one, the interactor itself offline. NetArg.With covers both.
            Component rider = (Component)interactor.GetComponentInParent<NetworkObject>() ?? interactor;
            this.NetToServer(NetMsg.Mount, new NetArg().With(rider));
        }

        public void RequestDismount() => this.NetToServer(NetMsg.Dismount);

        // ─────────── Server-side truth ───────────

        private void OnMountRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this) || mount.IsMounted) return;

            // Offline the rider never travelled as an id, because there is no spawn manager to
            // resolve it against. Falling back to the local interactor keeps single-player on the
            // same path rather than a second one that can rot.
            GameObject riderObject = arg.Resolve();
            Interactor interactor = riderObject != null
                ? riderObject.GetComponentInChildren<Interactor>(true)
                : null;

            if (interactor == null) return;

            SeatOnServer(interactor, riderObject, arg, except: sender);
        }

        /// <summary>
        /// Seats a rider on the server's own initiative, with nobody having asked — a save being
        /// restored.
        ///
        /// Deliberately the same code path as a player pressing E. A restore that mounted by calling
        /// <c>MountModule.TryMount</c> directly would seat the rider on the server and on nothing else:
        /// no peer would be told, and ownership would stay with the server, so the returning player
        /// would sit in a seat they cannot steer while every other client saw them standing in the
        /// sand. Mount replication is not something a loading path may opt out of.
        ///
        /// Returns false when the mount refuses — already occupied, rider not viable, this peer not the
        /// server — all of which are ordinary answers for a saved rider who is no longer here.
        /// </summary>
        public bool ServerMount(Interactor interactor)
        {
            if (!Network.Simulates(this) || mount.IsMounted || interactor == null) return false;

            NetworkObject riderNet = interactor.GetComponentInParent<NetworkObject>();

            GameObject riderObject = riderNet != null ? riderNet.gameObject : interactor.gameObject;

            // The same NetArg shape RequestMount builds, so peers resolve the rider identically.
            Component rider = riderNet != null ? riderNet : (Component)interactor;

            return SeatOnServer(interactor, riderObject, new NetArg().With(rider), except: NetTarget.Self);
        }

        /// <summary>
        /// The server's half of taking a seat: mount locally, hand ownership to the rider, tell the
        /// other peers. Shared so a restored mount and an interacted mount cannot drift apart.
        /// </summary>
        private bool SeatOnServer(Interactor interactor, GameObject riderObject, NetArg arg, ulong except)
        {
            if (!mount.CanMount(interactor)) return false;
            if (!ApplyMount(interactor)) return false;

            // Hand the mount to the rider so their local SteerModule input moves it and the motion
            // replicates outward from them. Without this the rider steers a body they don't own and
            // the server's NetworkTransform overwrites it every tick.
            NetworkObject mountObject = GetComponentInParent<NetworkObject>();
            NetworkObject riderNet = riderObject != null ? riderObject.GetComponent<NetworkObject>() : null;

            if (Network.IsNetworked && mountObject != null && riderNet != null
                && mountObject.IsSpawned && mountObject.OwnerClientId != riderNet.OwnerClientId)
            {
                mountObject.ChangeOwnership(riderNet.OwnerClientId);
            }

            this.NetToOthers(NetMsg.Mounted, arg, except);
            return true;
        }

        private void OnDismountRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this) || !mount.IsMounted) return;

            ApplyDismount();

            NetworkObject mountObject = GetComponentInParent<NetworkObject>();
            if (Network.IsNetworked && mountObject != null && mountObject.IsSpawned
                && mountObject.OwnerClientId != NetworkManager.ServerClientId)
            {
                mountObject.ChangeOwnership(NetworkManager.ServerClientId);
            }

            this.NetToOthers(NetMsg.Dismounted, arg, except: sender);
        }

        // ─────────── Replication to peers ───────────

        private void OnMountedElsewhere(in NetArg arg, ulong sender)
        {
            GameObject riderObject = arg.Resolve();
            Interactor interactor = riderObject != null
                ? riderObject.GetComponentInChildren<Interactor>(true)
                : null;

            if (interactor != null) ApplyMount(interactor);
        }

        private void OnDismountedElsewhere(in NetArg arg, ulong sender) => ApplyDismount();

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
        /// True while a server/relayed change is being applied locally. MountModule raises its
        /// Mounted/Dismounted events during that window; anything listening and re-requesting should
        /// check this to avoid a feedback loop.
        /// </summary>
        public bool IsApplyingReplicatedChange => applyingRemote;
    }
}
