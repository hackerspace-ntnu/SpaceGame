// One place a crew member stands, and the one place the rule "exactly one of them is standing
// here" is decided.
//
// Every control on a crewed vehicle is the same problem wearing different clothes: somebody claims
// it, works it, and lets go of it, and every machine in the session has to agree about all three.
// Written per station that is four near-identical copies of a claim protocol on one prefab — four
// chances to forget the late-joiner query, four places for a stand-down to be trusted from the
// wrong client. So it is written once, here, and a station says what it does rather than how it is
// replicated.
//
// ── The protocol ──
// NetMsg.StationClaim (65) and NetMsg.StationState (66), addressed to the VEHICLE's channel. See
// the comment block above them in NetMessaging.cs for why the vehicle rather than the player: the
// vehicle owns the fact that exactly one person is steering it, which is precisely the state two
// players racing for the same wheel would otherwise both believe they had won.
//
//   StationClaim   player → server.   A = station index, Target = the player.
//                  B = -1 what is this station's state?   (the late joiner's question)
//                       0 I am standing down
//                       1 I am taking this station / renewing my claim
//                  P.x = what the occupant is asking the control to do. For a held station that is
//                        the control's position itself (the helm reports its rudder); for a tapped
//                        one it is a direction, +1 or -1. NetMessaging documents A/B/Target because
//                        that is all a claim needs; P is free on this message and carrying the
//                        input in it is what lets one pair of ids serve a wheel and a winch alike.
//
//   StationState   server → everyone. A = station index, Target = the occupant (0 = nobody).
//                  B = 0 free, 1 manned.
//                  P.x = where the control now IS. This is the half that stops a continuous
//                        control from drifting: the value on the wire is absolute, so applying it
//                        twice is applying it once, and a machine that misses a message is
//                        corrected by the next one rather than staying wrong forever.
//
// ── Who decides what ──
//   • The SERVER owns the claim table and every station's value. It is the only machine that says
//     who is at the wheel, and the only one that broadcasts.
//   • The OCCUPANT's machine is ahead of the wire by a round trip, so it drives its own control
//     directly and ignores the echo of its own input. Everyone else takes the published value as
//     truth. That one rule is why a helm still feels immediate on a client.
//   • Nothing here touches the vehicle's simulation. A station that hands the vehicle to its
//     occupant says so with TakesVehicleOwnership, and the rest is Netcode's.
//
// A plain MonoBehaviour on purpose, like ArticulatedPartInteraction and for the same reason: a
// NetworkBehaviour on an object with no NetworkObject above it is an error in Netcode, and stations
// turn up on props and test rigs that nobody has spawned. With no relay every send falls through to
// a local dispatch and the station works single-player-style, which is what it did before it had
// any netcode at all.
//
// ── Why this is not NetLatch ──
// NetLatch is the project's shared helper for exactly this shape of protocol and the structure here
// is lifted from it. It is not reused because a latch replicates one BIT — a door is open or it is
// not — while a station replicates two things that a bit cannot carry: WHO is at it, which is the
// whole point of a claim, and WHERE the control sits, which is a float that has to keep arriving or
// a continuous control drifts apart between machines. It is also a base class rather than a field,
// because a station is a thing in the scene with its own collider and its own prompt, whereas a
// latch is a field of one — and one fixture may own several latches, which is what put NetLatch in
// a constructor in the first place.
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Vehicles
{
    public abstract class VehicleStation : MonoBehaviour, IInteractable
    {
        // ── Wire verbs. See the header for the table. ──
        private const int AskVerb = -1;
        private const int FreeVerb = 0;
        private const int MannedVerb = 1;

        private int stationIndex = -1;

        private GameObject occupant;
        private bool manned;

        /// <summary>
        /// The occupant's client, remembered on the server so a disconnect is still recognisable
        /// after Netcode has destroyed the body that would otherwise have named them.
        /// </summary>
        private ulong occupantClientId = NetworkManager.ServerClientId;

        /// <summary>What the occupant last asked this control to do. Server side.</summary>
        private float request;

        /// <summary>Where the control is. Authoritative on the server, a cache everywhere else.</summary>
        private float value;

        private float claimExpiry;
        private float graceUntil;
        private float nextPublish;
        private float nextRenew;

        // ── What a station is ───────────────────────────────────────────────────

        /// <summary>Somebody is at this station.</summary>
        public bool IsManned => manned;

        /// <summary>Who, or null. The player's body, not their Interactor.</summary>
        public GameObject Occupant => occupant;

        /// <summary>
        /// Is the person at this station the one whose screen this is?
        ///
        /// Ownership rather than an identity comparison, so it answers the same way offline, on the
        /// host and on a client, and so a remote player's controls are never touched by a machine
        /// that is only watching them.
        /// </summary>
        public bool IsMannedByLocalPlayer =>
            manned && occupant != null && Network.Owns(occupant.transform);

        /// <summary>
        /// Which station on this vehicle we are — the <c>A</c> field of every message it sends.
        ///
        /// Positional, over every <see cref="VehicleStation"/> under the entity, and that is the
        /// same deliberate trade ArticulatedPartInteraction and NetLatch both document: every
        /// machine in a session runs the same prefab out of the same build, so the order they
        /// enumerate is identical and the numbers agree without anything being authored or
        /// serialized. It does NOT survive reordering a prefab's children between builds — which is
        /// fine, because these numbers never outlive a session, but is worth knowing before somebody
        /// rearranges a deck and wonders why the wheel answers the jib.
        ///
        /// Resolved on first use rather than in Awake, the way NetLatch.Index is, so that a station
        /// added at runtime and a station instantiated by an EditMode test — where Awake never runs
        /// at all — both number themselves correctly.
        /// </summary>
        public int StationIndex
        {
            get
            {
                if (stationIndex < 0) stationIndex = ResolveStationIndex();
                return stationIndex;
            }
        }

        // ── What a subclass fills in ────────────────────────────────────────────

        /// <summary>
        /// May only one person work this at a time?
        ///
        /// True for a wheel: the whole point of routing a claim through the server is that two
        /// players pressing E on the same frame do not both end up steering. False for a control
        /// that is tapped rather than held — a winch two crew are both hauling on is not a conflict,
        /// it is a crew, and refusing the second press would eat half of them.
        /// </summary>
        protected virtual bool Exclusive => true;

        /// <summary>
        /// Seconds a claim survives without being renewed, or 0 for a claim held until its owner
        /// stands down.
        ///
        /// A timeout is not only about feel. It is also what stops a player who drops mid-haul from
        /// holding a winch for the rest of the voyage: the claim simply runs out. Held stations pay
        /// for that with the liveness check in <see cref="ServerTick"/> instead.
        /// </summary>
        protected virtual float ClaimTimeout => 0f;

        /// <summary>
        /// Travel applied the instant a claim lands, in the same units <see cref="AdvanceOnServer"/>
        /// takes. A tapped control needs it: with the whole effect deferred to the next frame a tap
        /// does nothing at all if anything interrupts that frame, and the control feels dead even
        /// when it is wired up correctly.
        /// </summary>
        protected virtual float ImmediateStep => 0f;

        /// <summary>
        /// Hand the whole vehicle to whoever takes this station, and back to the server when they
        /// stand down.
        ///
        /// True for the helm and nothing else. The vehicle's transform is owner-authoritative, so
        /// without the handoff the helmsman steers a body they do not own and the server's
        /// NetworkTransform overwrites their input every tick. Two stations both claiming ownership
        /// would mean the last claim wins, which is why this is a helm's property and not a
        /// station's in general.
        /// </summary>
        protected virtual bool TakesVehicleOwnership => false;

        /// <summary>
        /// How often the server republishes a manned station, and how often its occupant reports
        /// what they are doing with it. Ten times a second: enough for a wheel to look like it is
        /// being turned, far short of anything worth compressing.
        /// </summary>
        protected virtual float PublishInterval => 0.1f;

        /// <summary>Server side: may this player take it? Refusals are ordinary, not errors.</summary>
        protected virtual bool CanBeManned(GameObject player) => true;

        /// <summary>
        /// Server side: has the occupant stopped being a plausible occupant — teleported away,
        /// died, fallen off the deck? Polled while manned. Disconnects and despawns are handled for
        /// every station and do not need this.
        /// </summary>
        protected virtual bool ShouldRelease(GameObject player) => false;

        /// <summary>
        /// How long after a claim <see cref="ShouldRelease"/> is ignored.
        ///
        /// A station that judges its occupant on where they are has to wait for them to get there,
        /// and "getting there" is now a round trip: the claim has to reach the occupant's machine,
        /// that machine has to put them in position, and the position has to replicate back. The
        /// helm is the case that needs it — its release radius is 3.5 m and the interaction ray
        /// reaches 5, so a player who takes the wheel from across the deck is legitimately out of
        /// range on the frame the claim lands, and judged immediately would be stood down again
        /// before their own machine had even heard they got it.
        /// </summary>
        protected virtual float ReleaseGrace => 0.5f;

        /// <summary>Every machine: somebody took this station.</summary>
        protected virtual void OnManned(GameObject player) { }

        /// <summary>
        /// Every machine: the station is free again. <paramref name="player"/> may already be
        /// destroyed — this is also the disconnect path — so treat it as a token, not a body.
        /// </summary>
        protected virtual void OnUnmanned(GameObject player) { }

        /// <summary>
        /// Server side: turn what the occupant is asking for into where the control now is.
        ///
        /// The default passes the request straight through, which is right for a station whose
        /// occupant already integrated the control on their own machine — the helm ramps its own
        /// rudder, because a wheel that waits a round trip for its own input is a wheel made of
        /// treacle. A control the server integrates instead (a winch, worked in taps) overrides
        /// this and returns where it ended up.
        /// </summary>
        protected virtual float AdvanceOnServer(float wanted, float deltaTime) => wanted;

        /// <summary>
        /// Server side: where this control actually is, right now, for the value that goes out with
        /// every announcement.
        ///
        /// Overridden by anything whose position lives somewhere real — a winch's position is the
        /// sail's sheet setting, not a number this class happens to be holding. The default is that
        /// number, which is right for a control the server only ever learns about second hand.
        ///
        /// Getting this wrong is not cosmetic. A station nobody has touched since the session began
        /// has never run <see cref="AdvanceOnServer"/>, so the cached value is still zero — and the
        /// first thing a joining client does is ask. Answered from the cache, the answer is "this
        /// sail is sheeted right in", and the joiner obediently hauls a sail that was set at half
        /// on every other machine.
        /// </summary>
        protected virtual float ReadValue() => value;

        /// <summary>
        /// Put the control at the published position. Called on every machine EXCEPT the occupant's,
        /// which is a round trip ahead of the wire and would only be dragged backwards by its own
        /// echo.
        ///
        /// Absolute, never incremental, and that is what makes it safe to run twice, out of order,
        /// or after a dropped message.
        /// </summary>
        protected virtual void ApplyValue(float position) { }

        /// <summary>
        /// The occupant's machine: what this control is being asked to do right now. Sent to the
        /// server on every renewal. Only called on held stations — a tapped one says what it wants
        /// in the claim itself and then stops talking.
        /// </summary>
        protected virtual float LocalRequest() => 0f;

        // IInteractable. Left abstract rather than implemented here: a wheel and a winch answer E
        // completely differently, and the shared part is the claim, not the button.
        public abstract bool CanInteract();

        public abstract void Interact(Interactor interactor);

        // ── Lifecycle ───────────────────────────────────────────────────────────

        // There is deliberately no Awake here: the only thing this class needs resolving is the
        // station index, and that is done lazily by StationIndex above. Anything added later that
        // does need an Awake must be `protected virtual` and every subclass must chain it — Unity
        // resolves its magic methods on the most derived type, so a subclass's own Awake would
        // REPLACE this one rather than add to it, silently and with no warning. See Update.

        private int ResolveStationIndex()
        {
            GameObject root = NetChannel.RootOf(this);
            if (root == null) return 0;

            var siblings = root.GetComponentsInChildren<VehicleStation>(true);
            for (int i = 0; i < siblings.Length; i++)
                if (siblings[i] == this) return i;

            // Not under the entity we resolved — a station reparented out of its vehicle. The
            // numbering this machine produces then does not match anybody else's, so say so rather
            // than quietly answering somebody else's messages.
            Debug.LogWarning($"[{nameof(VehicleStation)}] '{name}' is not among the stations under " +
                             "its own entity, so its station number cannot be matched against the " +
                             "other machines'. It will answer to index 0.", this);
            return 0;
        }

        protected virtual void OnEnable()
        {
            this.NetOn(NetMsg.StationClaim, OnClaimRequested);
            this.NetOn(NetMsg.StationState, OnStateAnnounced);

            // Decided here rather than in the coroutine's first line, the way NetLatch.Enable does
            // it. It costs nothing — StartCoroutine runs a body up to its first yield inline, so a
            // coroutine that bails immediately and one never started are the same thing — and it
            // keeps a whole class of hosts out of the coroutine machinery: an EditMode test, or a
            // scene opened straight from the editor, has no business there, and the DuneFoil prefab
            // is instantiated and stepped by EditMode tests.
            if (Network.IsNetworked && !Network.Server && isActiveAndEnabled)
                StartCoroutine(AskForStateWhenConnected());
        }

        protected virtual void OnDisable()
        {
            this.NetOff(NetMsg.StationClaim, OnClaimRequested);
            this.NetOff(NetMsg.StationState, OnStateAnnounced);

            // Give the occupant their body back on the way out. Local only, and deliberately
            // silent: an object being torn down — a chunk unloading, a session ending — must not
            // put a message on a wire that may already be gone, and the server's own copy of this
            // station is doing the same thing on its own machine.
            SetOccupant(null);
        }

        /// <summary>
        /// A joining client asks what state this station is in, once there is somebody to ask.
        ///
        /// Waits for the vehicle's NetworkObject to actually be spawned rather than sending on the
        /// first frame: before that there is no relay, the send falls through to a local dispatch,
        /// and the client answers its own question with the state it already had — which is the
        /// prefab's, which is the thing being corrected. A wheel that somebody took before you
        /// connected must not read as free.
        /// </summary>
        private IEnumerator AskForStateWhenConnected()
        {
            GameObject root = NetChannel.RootOf(this);
            NetworkObject netObj = root != null ? root.GetComponent<NetworkObject>() : null;
            if (netObj == null) yield break;

            while (!netObj.IsSpawned)
            {
                if (!Network.IsNetworked) yield break;
                yield return null;
            }

            this.NetToServer(NetMsg.StationClaim, new NetArg { A = StationIndex, B = AskVerb });
        }

        /// <summary>
        /// Virtual, and a subclass that needs its own Update MUST call this one.
        ///
        /// Unity resolves its magic methods on the most derived type, so a private Update declared
        /// on a subclass does not add to this one — it REPLACES it, silently, with no compiler
        /// warning and no error at runtime. The station would keep answering the crosshair and
        /// quietly stop ever publishing or expiring a claim.
        /// </summary>
        protected virtual void Update()
        {
            if (Network.Simulates(this)) ServerTick();
            RenewClaim();
        }

        // ── Asking (any machine) ────────────────────────────────────────────────

        /// <summary>
        /// Ask for this station. Returns immediately — the claim happens when the server says so,
        /// and offline that is inside this call.
        /// </summary>
        /// <param name="wanted">What to do with the control, in <see cref="AdvanceOnServer"/>'s units.</param>
        protected void RequestClaim(Interactor interactor, float wanted = 0f) =>
            RequestClaim(ResolvePlayer(interactor), wanted);

        /// <summary>As above, for a caller that already knows whose body it means.</summary>
        protected void RequestClaim(GameObject player, float wanted = 0f)
        {
            if (player != null) Send(MannedVerb, wanted, player);
        }

        /// <summary>
        /// Ask to stand down. The server refuses this from anybody but the person actually at the
        /// station — the rule the helm used to enforce locally, moved to the one machine that can
        /// still be trusted with it once there is more than one of them.
        /// </summary>
        protected void RequestRelease(Interactor interactor) =>
            RequestRelease(ResolvePlayer(interactor));

        /// <summary>As above, for a caller that already knows whose body it means.</summary>
        protected void RequestRelease(GameObject player)
        {
            if (player != null) Send(FreeVerb, 0f, player);
        }

        private void Send(int verb, float wanted, GameObject player) =>
            this.NetToServer(NetMsg.StationClaim,
                             new NetArg { A = StationIndex, B = verb, P = new Vector3(wanted, 0f, 0f) }
                                 .With(player));

        /// <summary>
        /// The body an interactor belongs to: its NetworkObject when there is one, its root when
        /// there is not. The same shape MountNetworkSync builds, so a station and a mount name the
        /// same player the same way and NetArg.With covers networked and offline alike.
        /// </summary>
        protected static GameObject ResolvePlayer(Interactor interactor)
        {
            if (interactor == null) return null;

            NetworkObject netObj = interactor.GetComponentInParent<NetworkObject>();
            return netObj != null ? netObj.gameObject : interactor.transform.root.gameObject;
        }

        /// <summary>
        /// The occupant tells the server what they are doing with the control, ten times a second.
        ///
        /// Only for stations held rather than tapped. A tapped one said everything it had to say in
        /// its claim and must be allowed to expire; renewing it would mean a player who taps once
        /// holds the winch until they log out.
        /// </summary>
        private void RenewClaim()
        {
            if (ClaimTimeout > 0f) return;
            if (!IsMannedByLocalPlayer) return;

            // Nobody to tell. Offline the local value IS the truth and the round trip would only
            // burn a dispatch every tenth of a second to hand us back what we already have.
            if (!Network.IsNetworked) return;

            if (Time.time < nextRenew) return;
            nextRenew = Time.time + PublishInterval;

            Send(MannedVerb, LocalRequest(), occupant);
        }

        // ── Deciding (the server) ───────────────────────────────────────────────

        private void OnClaimRequested(in NetArg arg, ulong sender)
        {
            if (arg.A != StationIndex) return;
            if (!Network.Simulates(this)) return;

            if (arg.B == AskVerb)
            {
                // Answered by announcing to everybody rather than to the asker: this layer has no
                // unicast, the message is a dozen bytes, and applying it again on a machine that
                // already agreed is by construction a no-op.
                Announce();
                return;
            }

            GameObject player = arg.Resolve();
            if (player == null) return;

            // A client may only speak for its own body. Checked rather than trusted, because the
            // whole reason the claim goes through the server is that the clients cannot be — a
            // station claim names the VEHICLE's channel, and every machine knows every vehicle's
            // NetworkObjectId because that is how it draws one, so an unchecked handler would let
            // anybody in the session stand anybody else up from the wheel.
            //
            // The server is waved through the way MountNetworkSync.MayDismount waves it through: it
            // seats and unseats people for reasons no client asked for, and offline every send is
            // attributed to the server id. So is a player with no spawned NetworkObject, which is
            // single-player and tests, where there is no id to compare against.
            if (!MayActFor(player, sender)) return;

            if (arg.B == FreeVerb)
            {
                // Only the person actually at the station may leave it, so a second player looking
                // at the wheel cannot take it out from under them.
                if (!manned || player != occupant)
                {
                    // Tell them what is really true rather than dropping it. A refusal that says
                    // nothing leaves a machine that guessed wrong guessing wrong forever.
                    Announce();
                    return;
                }

                ReleaseOnServer();
                return;
            }

            if (arg.B != MannedVerb) return;

            // A claim from the person already here is a renewal: it carries their current input and
            // nothing else changes. This is the helm's heartbeat.
            if (manned && player == occupant)
            {
                request = arg.P.x;
                return;
            }

            if (manned && Exclusive)
            {
                Announce();
                return;
            }

            if (!CanBeManned(player))
            {
                Announce();
                return;
            }

            ClaimOnServer(player, arg.P.x);
        }

        /// <summary>May <paramref name="sender"/> speak for <paramref name="player"/>? See the
        /// call site for why this is checked and why the server and unnetworked bodies are not.</summary>
        private static bool MayActFor(GameObject player, ulong sender)
        {
            if (!Network.IsNetworked) return true;
            if (sender == NetworkManager.ServerClientId) return true;

            NetworkObject body = player.GetComponent<NetworkObject>();
            if (body == null || !body.IsSpawned) return true;

            return body.OwnerClientId == sender;
        }

        private void ClaimOnServer(GameObject player, float wanted)
        {
            request = wanted;
            claimExpiry = Time.time + ClaimTimeout;
            graceUntil = Time.time + ReleaseGrace;

            NetworkObject body = player.GetComponent<NetworkObject>();
            occupantClientId = body != null && body.IsSpawned
                ? body.OwnerClientId
                : NetworkManager.ServerClientId;

            SetOccupant(player);
            HandVehicleOwnership(player);

            // The instant part of a tap, applied before anybody is told, so the value that goes out
            // with the claim already includes it and no machine sees the control twitch twice.
            if (ImmediateStep > 0f) value = AdvanceOnServer(request, ImmediateStep);

            Announce();
        }

        private void ReleaseOnServer()
        {
            request = 0f;
            SetOccupant(null);
            HandVehicleOwnership(null);
            Announce();
        }

        private void ServerTick()
        {
            if (!manned) return;

            // Everything that ends a claim without anybody asking, in one place.
            //
            // Polled rather than driven off NetworkManager.OnClientDisconnectCallback, and that is
            // the point: a poll needs no subscription and therefore cannot leak one, it converges
            // rather than depending on an event arriving exactly once, and it catches every way an
            // occupant can stop being one — a dropped connection, a despawned body, a chunk
            // unloading under them — with a single condition instead of one handler apiece.
            //
            // A vanished body and a vanished client are never graced: destroyed is destroyed, and
            // no amount of waiting brings them back. Only the station's own judgement about where
            // its occupant is standing gets the grace period, for the reason on ReleaseGrace.
            if (occupant == null || !OccupantStillConnected())
            {
                ReleaseOnServer();
                return;
            }

            if (Time.time >= graceUntil && ShouldRelease(occupant))
            {
                ReleaseOnServer();
                return;
            }

            if (ClaimTimeout > 0f && Time.time >= claimExpiry)
            {
                ReleaseOnServer();
                return;
            }

            value = AdvanceOnServer(request, Time.deltaTime);

            // Offline there is nobody to publish to, and the local value is already the truth.
            if (Network.IsNetworked && Time.time >= nextPublish) Announce();
        }

        private bool OccupantStillConnected()
        {
            if (!Network.IsNetworked) return true;

            // The server never disconnects from itself, and it is also the id an occupant with no
            // NetworkObject of its own is filed under. Answering from ConnectedClients instead would
            // be right on a host — which is a client too, and is in that dictionary — and wrong on a
            // dedicated server, where nothing would ever be allowed to hold a station.
            if (occupantClientId == NetworkManager.ServerClientId) return true;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer) return true;

            return manager.ConnectedClients.ContainsKey(occupantClientId);
        }

        /// <summary>
        /// Move the vehicle's ownership to the station's occupant, or back to the server.
        ///
        /// Exactly what MountNetworkSync.SeatOnServer does for a mount, and for the same reason:
        /// the hull's transform is owner-authoritative, so the machine driving it has to be the one
        /// that owns it. Idempotent — it is called from the claim path, the release path and the
        /// liveness poll, and re-handing an object to the client that already has it is a wasted
        /// state change every peer has to process.
        ///
        /// NOTE for whoever authors the prefab: the vehicle's NetworkObject MUST have
        /// DontDestroyWithOwner ticked. With it off — Unity's default — Netcode DESPAWNS AND
        /// DESTROYS every object a disconnecting client owned, so the helmsman closing their laptop
        /// would delete the boat for everybody. With it on, ownership simply reverts to the server,
        /// which is what the liveness poll above then reconciles the station state against.
        /// </summary>
        private void HandVehicleOwnership(GameObject player)
        {
            if (!TakesVehicleOwnership || !Network.IsNetworked) return;

            NetworkObject vehicle = GetComponentInParent<NetworkObject>();
            if (vehicle == null || !vehicle.IsSpawned) return;

            ulong target = NetworkManager.ServerClientId;
            if (player != null)
            {
                NetworkObject body = player.GetComponent<NetworkObject>();

                // A body with no id of its own cannot be handed anything. Leaving the vehicle where
                // it is beats handing it to client 0 by accident, which would silently make the
                // host the helmsman.
                if (body == null || !body.IsSpawned) return;
                target = body.OwnerClientId;
            }

            if (vehicle.OwnerClientId == target) return;
            vehicle.ChangeOwnership(target);
        }

        private void Announce()
        {
            nextPublish = Time.time + PublishInterval;

            NetArg arg = new NetArg
            {
                A = StationIndex,
                B = manned ? MannedVerb : FreeVerb,
                P = new Vector3(ReadValue(), 0f, 0f),
            };

            if (manned && occupant != null) arg = arg.With(occupant);

            this.NetToAll(NetMsg.StationState, arg);
        }

        // ── Hearing about it (every machine) ────────────────────────────────────

        private void OnStateAnnounced(in NetArg arg, ulong sender)
        {
            if (arg.A != StationIndex) return;

            SetOccupant(arg.B == MannedVerb ? arg.Resolve() : null);

            // The occupant's own machine is a round trip ahead of this message: it drove the control
            // itself and has already moved on. Applying the echo would drag a wheel backwards by
            // however long the last packet took, which is exactly the mush the handoff exists to
            // avoid. Everyone else takes it as the truth, absolutely, so a missed message costs
            // nothing but the next tenth of a second.
            if (IsMannedByLocalPlayer) return;

            value = arg.P.x;
            ApplyValue(value);
        }

        /// <summary>
        /// The one place manned/unmanned changes, so the two hooks are always paired.
        ///
        /// Idempotent by construction: told the same occupant twice it does nothing, and told to
        /// clear a station that is already free it does nothing. The manned flag is kept separately
        /// from the reference because the disconnect path arrives with a body Netcode has already
        /// destroyed, and a destroyed reference compares equal to null — so without the flag,
        /// "the helmsman vanished" would look identical to "nothing changed" and the helm would
        /// never give anybody their legs back.
        /// </summary>
        private void SetOccupant(GameObject player)
        {
            bool wanted = player != null;
            if (manned == wanted && occupant == player) return;

            if (manned)
            {
                GameObject was = occupant;
                manned = false;
                occupant = null;
                OnUnmanned(was);
            }

            if (!wanted) return;

            occupant = player;
            manned = true;
            OnManned(player);
        }

        // There is deliberately no ServerClaim/ServerRelease pair here, unlike
        // MountNetworkSync.ServerMount. That one exists because a saved rider has to be put back in
        // their seat on load, and doing it by calling MountModule directly would seat them on the
        // server and nowhere else. Nothing saves who was at a wheel — a helm is not somewhere you
        // are, it is something you are doing — so the entry point would be a public surface with no
        // caller. When helm state does become persistent, the restore must come through
        // ClaimOnServer rather than round the side of it, for exactly the reason ServerMount does.
    }
}
