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
//   • Remote peers run the same TryMount/Dismount so the rider is visibly parented into the seat.
//     Cameras, look input and steering are the local rider's alone — MountModule.RiderIsLocal.
//
// Two channels, not one, and they answer different questions:
//   • NetMsg.Mount/Mounted/Dismount/Dismounted is the EVENT. It is what everybody in the session at
//     the time acts on, immediately.
//   • seatedRider is the STATE. NetworkVariable change events never replay, so a player who joins
//     while somebody is already in the saddle has nothing else to go on: the event was sent long
//     before they connected. Without it a late joiner saw the rider standing bolt upright on an
//     ostrich that still advertised itself as free to mount.
// The state channel also re-asserts itself every frame, so it repairs the seat whatever went wrong.
//
// Both channels are addressed: a vehicle can carry several mounts on one NetworkObject, and a
// NetChannel belongs to the entity rather than to the component, so every message says which mount
// it means in NetArg.A. See MountIndex for what happened when they did not.
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    // NetworkBehaviour rather than MonoBehaviour purely for the NetworkVariable below. It sits on
    // the same GameObject as the mount's NetworkObject on every prefab that has one, which is what
    // makes that legal.
    [RequireComponent(typeof(MountModule))]
    public class MountNetworkSync : NetworkBehaviour
    {
        private MountModule mount;

        // Set while a replicated mount/dismount is being applied, so the local events those raise
        // don't bounce straight back out as another request.
        private bool applyingRemote;

        /// <summary>
        /// Who is in the seat, as their NetworkObjectId; 0 for empty.
        ///
        /// <para>
        /// Server-write because seating is a server decision, and this is the RECORD of that
        /// decision rather than a second way of making one — nothing acts on a write to it except
        /// a peer bringing its own copy of the mount into line.
        /// </para>
        /// </summary>
        private readonly NetworkVariable<ulong> seatedRider = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>
        /// <see cref="NetArg.B"/> on a <see cref="NetMsg.Dismounted"/> that carries a place to put
        /// the rider in <see cref="NetArg.P"/>. Zero — the default — means "use your own dismount
        /// point", which is what an older build's message looks like.
        /// </summary>
        private const int DismountCarriesPosition = 1;

        private int mountIndex = -1;

        /// <summary>
        /// Which mount on this entity we are — the <see cref="NetArg.A"/> of every message this
        /// sends, and the first thing every handler here checks.
        ///
        /// <para>
        /// A channel belongs to the entity, not to the component, and a vehicle may carry several
        /// mounts on one NetworkObject: PlayerShipBuilder gives every non-helm chair its own
        /// MountModule, which is why NetMsg 92/93 were retired rather than a second way to sit
        /// down being written. Unaddressed, one press therefore mounted the same player in all four
        /// chairs — and the surplus chairs each snapshotted the rider's Rigidbody AFTER the first
        /// had frozen it, so the dismount handed the player back a body with gravity switched off.
        /// See MountSeatAddressingTests.
        /// </para>
        /// <para>
        /// Positional, over every <see cref="MountNetworkSync"/> under the entity — the same trade
        /// <see cref="NetChannel.IndexOf{T}"/> documents, and the same one ArticulatedPartInteraction
        /// and VehicleStation.StationIndex already make.
        /// </para>
        /// </summary>
        public int MountIndex
        {
            get
            {
                if (mountIndex < 0) mountIndex = NetChannel.IndexOf(this);
                return mountIndex;
            }
        }

        /// <summary>Is this message meant for our mount, or for one of the others on this hull?</summary>
        private bool AddressesUs(in NetArg arg) => arg.A == MountIndex;

        private void Awake() => mount = GetComponent<MountModule>();

        private void OnEnable()
        {
            this.NetOn(NetMsg.Mount, OnMountRequested);
            this.NetOn(NetMsg.Dismount, OnDismountRequested);
            this.NetOn(NetMsg.Mounted, OnMountedElsewhere);
            this.NetOn(NetMsg.Dismounted, OnDismountedElsewhere);

            mount.Dismounted += AnnounceDismount;
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.Mount, OnMountRequested);
            this.NetOff(NetMsg.Dismount, OnDismountRequested);
            this.NetOff(NetMsg.Mounted, OnMountedElsewhere);
            this.NetOff(NetMsg.Dismounted, OnDismountedElsewhere);

            mount.Dismounted -= AnnounceDismount;
        }

        // ─────────── The state channel ───────────

        /// <summary>
        /// Keep the seat and the record agreeing — publishing it on the server, obeying it
        /// everywhere else.
        ///
        /// <para>
        /// Polled rather than raised from <see cref="MountModule.Mounted"/>/<c>Dismounted</c>,
        /// which looks like the obvious wiring and is wrong twice: <c>Dismounted</c> fires BEFORE
        /// the rider references are cleared, so a handler reading the seat there still finds the
        /// rider it is being told left, and <c>AbandonRider</c> — the teardown path — raises no
        /// event at all. Comparing two ulongs once a frame has neither problem and cannot miss a
        /// path added later.
        /// </para>
        /// </summary>
        private void Update()
        {
            if (!IsSpawned) return;

            if (IsServer)
            {
                ulong seated = ResolveSeatedRiderId();
                if (seatedRider.Value != seated) seatedRider.Value = seated;
                return;
            }

            ReconcileSeat();
        }

        /// <summary>
        /// Tell everyone the seat is empty, and where the rider was put down.
        ///
        /// <para>
        /// Answered from the mount's own <see cref="MountModule.Dismounted"/> rather than from the
        /// request handler, which is the only way to cover the dismounts nobody requested. A rider
        /// is thrown out of the saddle by an ornithopter landing, by dying, by their pack being
        /// unequipped, by a rider's own teardown — none of those come through
        /// <see cref="OnDismountRequested"/>, and before this none of them reached another machine
        /// at all. Peers were left with a rider still welded into a seat the server had emptied,
        /// and only found out when the mount itself was destroyed underneath them.
        /// </para>
        /// <para>
        /// The event and not the <see cref="seatedRider"/> poll, even though the poll is what the
        /// rest of this class trusts, because some dismounts do not survive until the next frame:
        /// a landed ornithopter is dismounted and despawned inside one call, so a poll would find
        /// the component gone before it ever noticed the seat empty.
        /// </para>
        /// <para>
        /// It also closes the hole in the requested path: that one excluded the client who asked,
        /// on the reasoning that a requester has already acted locally — but a mount request is
        /// sent WITHOUT acting locally, so the one machine that most needed telling was the one
        /// deliberately skipped. Announcing from the dismount itself tells everybody, them included.
        /// </para>
        /// <para>
        /// The position travels because a peer cannot work it out. Landing dismounts the pilot at
        /// ground the server probed for; a peer that fell back on its own dismount marker would
        /// put them under the wreck, and on their own machine — where their body is
        /// owner-authoritative — that wrong answer is the one that sticks.
        /// </para>
        /// </summary>
        private void AnnounceDismount(PlayerMovement rider)
        {
            if (!IsServer || !IsSpawned || rider == null) return;

            var arg = new NetArg { A = MountIndex }.With(rider);

            if (mount.HasLastDismountPosition)
            {
                arg.P = mount.LastDismountPosition;
                arg.B = DismountCarriesPosition;
            }

            // Others rather than All: this machine has just dismounted — that is what raised the
            // event we are answering.
            this.NetToOthers(NetMsg.Dismounted, arg);
        }

        /// <summary>
        /// Client side: seat the rider the server says is in this mount, if nobody is in it here.
        ///
        /// <para>
        /// SEATING ONLY — emptying the seat stays with <see cref="NetMsg.Dismounted"/>, and that is
        /// not a gap. The record is written a tick after the event that caused it, so a reconcile
        /// that also emptied seats would see "mounted here, record still says empty" in the window
        /// between the broadcast landing and the variable arriving, and throw the rider off a
        /// mount they had just climbed onto. Every peer that needs to hear about a dismount is by
        /// definition connected when it happens, so the reliable broadcast already reaches all of
        /// them; only ARRIVING mid-ride has no event to hear, and that is what this covers.
        /// </para>
        /// <para>
        /// The value is re-read every frame rather than latched at spawn, so a rider who leaves
        /// before this machine managed to seat them is simply never seated. The retry exists
        /// because the rider named by the record may not have been spawned here yet — one join
        /// synchronises many objects and their order is not ours to choose.
        /// </para>
        /// </summary>
        private void ReconcileSeat()
        {
            ulong wanted = seatedRider.Value;
            if (wanted == 0) return;

            // Anybody in the seat is enough. The server is the only writer and a seat holds one
            // rider, so there is no second case to distinguish — and reading the seated rider's
            // identity back to compare it would spin forever on a rider with no NetworkObject,
            // which can never match an id.
            if (mount.IsMounted) return;

            if (NetworkManager.Singleton == null) return;
            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects
                    .TryGetValue(wanted, out NetworkObject riderNet) || riderNet == null)
                return;

            Interactor interactor = riderNet.GetComponentInChildren<Interactor>(true);
            if (interactor != null) ApplyMount(interactor);
        }

        /// <summary>
        /// The NetworkObjectId of whoever is in the seat, or 0.
        ///
        /// <para>
        /// Zero also covers "mounted by a rider with no spawned NetworkObject", which is not a lie
        /// the clients can be hurt by: every rider in a session is a spawned player object, and the
        /// case only arises offline, where there is nobody to tell. The mount's OWN NetworkObject
        /// is explicitly excluded — a rider is parented INTO the seat, so an unnetworked one
        /// resolves upward to the mount, and naming the mount as its own rider would have a late
        /// joiner trying to seat the ostrich on itself.
        /// </para>
        /// </summary>
        private ulong ResolveSeatedRiderId()
        {
            Transform rider = mount.MountedPlayerTransform;
            if (rider == null) return 0;

            NetworkObject riderNet = rider.GetComponentInParent<NetworkObject>();
            if (riderNet == null || !riderNet.IsSpawned) return 0;
            if (riderNet == GetComponentInParent<NetworkObject>()) return 0;

            return riderNet.NetworkObjectId;
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
            this.NetToServer(NetMsg.Mount, new NetArg { A = MountIndex }.With(rider));
        }

        public void RequestDismount() => this.NetToServer(NetMsg.Dismount, new NetArg { A = MountIndex });

        // ─────────── Server-side truth ───────────

        private void OnMountRequested(in NetArg arg, ulong sender)
        {
            if (!AddressesUs(arg)) return;
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

            return SeatOnServer(interactor, riderObject, new NetArg { A = MountIndex }.With(rider),
                                except: NetTarget.Self);
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

        /// <summary>
        /// Server side: the only machine allowed to say a dismount happened.
        ///
        /// <para>
        /// Unlike <c>PlayerRespawn.OnRespawnRequested</c>, this one does check the sender, and the
        /// difference is what the message can be aimed at. A respawn arrives on the player's own
        /// channel and asks for something that player is already asking for, so the worst a forged
        /// one can do is resurrect a teammate who wanted resurrecting. A dismount arrives on the
        /// MOUNT's channel — every client knows every mount's NetworkObjectId, because that is how
        /// they draw it — so an unchecked handler lets anybody in the session throw anybody else
        /// off their walker at any moment, from anywhere on the map.
        /// </para>
        /// <para>
        /// The server is allowed through unconditionally: it dismounts riders for reasons no client
        /// asked for — a death, a teardown, a save being restored — and offline every send is
        /// attributed to the server id, so single-player takes this path as it always did.
        /// </para>
        /// </summary>
        private void OnDismountRequested(in NetArg arg, ulong sender)
        {
            if (!AddressesUs(arg)) return;
            if (!Network.Simulates(this) || !mount.IsMounted) return;
            if (!MayDismount(sender)) return;

            ApplyDismount();

            NetworkObject mountObject = GetComponentInParent<NetworkObject>();
            if (Network.IsNetworked && mountObject != null && mountObject.IsSpawned
                && mountObject.OwnerClientId != NetworkManager.ServerClientId)
            {
                mountObject.ChangeOwnership(NetworkManager.ServerClientId);
            }

            // No broadcast here. ApplyDismount above raised MountModule.Dismounted, and
            // AnnounceDismount answered it — for this dismount and for the ones that never come
            // through here at all. One announcement, one shape, whatever emptied the seat.
        }

        /// <summary>
        /// May <paramref name="sender"/> throw the current rider off? Resolves who the rider is and
        /// hands the decision to <see cref="IsDismountAllowed"/>.
        /// </summary>
        private bool MayDismount(ulong sender)
        {
            if (!Network.IsNetworked) return true;

            return IsDismountAllowed(sender, NetworkManager.ServerClientId, ResolveRiderOwner());
        }

        /// <summary>
        /// The rule itself, with the lookups taken out so it can be tested without a session.
        ///
        /// <para>
        /// Only the rider themselves, or the server. A null <paramref name="riderOwner"/> means
        /// nobody could be identified — an unnetworked rider, a rider not spawned, a mount seated
        /// by a save being restored — and that answers true: there is no client id to compare
        /// against, and refusing would mean nobody could ever get off. The check exists for exactly
        /// one thing, which is a client naming somebody else's mount, and it should not start
        /// deciding anything else.
        /// </para>
        /// </summary>
        public static bool IsDismountAllowed(ulong sender, ulong serverClientId, ulong? riderOwner)
        {
            if (sender == serverClientId) return true;
            if (riderOwner == null) return true;

            return sender == riderOwner.Value;
        }

        /// <summary>The client id of whoever is in the seat, or null if there is no telling.</summary>
        private ulong? ResolveRiderOwner()
        {
            Transform rider = mount.MountedPlayerTransform;
            if (rider == null) return null;

            NetworkObject riderNet = rider.GetComponentInParent<NetworkObject>();
            if (riderNet == null || !riderNet.IsSpawned) return null;

            // A rider is parented INTO the seat while mounted, so a rider with no NetworkObject of
            // its own resolves to the mount's — whose owner is the rider's client, since seating
            // hands ownership over. Comparing the sender against that is comparing them with
            // themselves and would wave anybody through. Treat it as "no rider identity" instead.
            if (riderNet == GetComponentInParent<NetworkObject>()) return null;

            return riderNet.OwnerClientId;
        }

        // ─────────── Replication to peers ───────────

        private void OnMountedElsewhere(in NetArg arg, ulong sender)
        {
            if (!AddressesUs(arg)) return;

            GameObject riderObject = arg.Resolve();
            Interactor interactor = riderObject != null
                ? riderObject.GetComponentInChildren<Interactor>(true)
                : null;

            if (interactor != null) ApplyMount(interactor);
        }

        private void OnDismountedElsewhere(in NetArg arg, ulong sender)
        {
            if (!AddressesUs(arg)) return;

            // Where the server put them, when it said. Falling back to this mount's own dismount
            // point is right for a mount that has not moved since — an ostrich somebody stepped
            // off — and is all there was before the position travelled.
            if (arg.B == DismountCarriesPosition) ApplyDismountAt(arg.P);
            else ApplyDismount();
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

        private void ApplyDismountAt(Vector3 position)
        {
            applyingRemote = true;
            try
            {
                mount.DismountAt(position);
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
