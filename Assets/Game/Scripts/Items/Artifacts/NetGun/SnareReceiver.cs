// Everything about a net that has already left the gun.
//
// This lives on the SHOOTER rather than on the net gun, and the reason is written down one item
// over. LassoTether's file header lists three defects the lasso paid for, and the third is this
// one exactly: "the whole relationship lived in fields on an item instance that is destroyed on
// every equip, so switching hotbar slot freed the animal." A net gun that kept its live nets in a
// dictionary on the item has the same bug in the same shape — switch slot with a net in the air
// and the catch is never resolved, the tear is never announced, and every other machine holds its
// captives forever.
//
// ItemState exists because of the same truth. Anything that has to outlive an equip cannot live on
// the item, so this is the piece that does not.
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// One shooter's live nets: the registry, the three message handlers, and the landing pass.
    ///
    /// <para>
    /// <c>Snared</c> and <c>SnareFreed</c> carry the net id in <c>A</c>, because <c>B</c> held the
    /// flight seed on the way out and the two must not collide. <c>SnareStruggled</c> names no net
    /// at all — see <see cref="OnSnareStruggled"/>.
    /// </para>
    ///
    /// <para>
    /// On the shooter and not on the net, because the messages ride the shooter's channel.
    /// <c>NetOn</c> registers against <c>NetChannel.GetOrAdd(self)</c> and sends route through
    /// <c>NetRelay.Find(self)</c>, so a listener has to sit inside a hierarchy that HAS a relay.
    /// A <see cref="SnareCatch"/> is a bare world object with none, and could never hear a word.
    /// </para>
    /// <para>
    /// Added on the first shot and removed once the last net is gone, so a player who never fires
    /// a net gun never carries one. Same shape and reasoning as <see cref="SnareTether"/> and
    /// <see cref="SnaredBody"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    public sealed class SnareReceiver : MonoBehaviour
    {
        /// <summary>One net and the capture policy of the gun that fired it.</summary>
        private struct Tracked
        {
            public SnareCatch Net;
            public LayerMask Layers;
            public float CaptureHeight;
        }

        private readonly Dictionary<int, Tracked> live = new Dictionary<int, Tracked>();
        private readonly HashSet<int> announcedTorn = new HashSet<int>();
        private readonly HashSet<int> resolvedLanding = new HashSet<int>();

        /// <summary>
        /// Net ids oldest-first. A Dictionary does not promise an order, and the eviction below
        /// needs one — "the oldest" has to mean the same net on every machine.
        /// </summary>
        private readonly List<int> order = new List<int>();

        /// <summary>Reused so the per-frame passes do not allocate a key list every frame.</summary>
        private readonly List<int> scratchIds = new List<int>();

        /// <summary>
        /// Seconds of having nothing to watch before this retires itself.
        ///
        /// Not zero, and the delay is the point. Destroying the component the instant the last net
        /// goes leaves a window in which a shot fired the same frame would Track onto a component
        /// already queued for destruction — whose OnDisable would then immediately tear the net it
        /// had just been handed. Waiting a couple of seconds closes that, and a shooter who fires
        /// again inside it simply keeps the receiver they already had.
        /// </summary>
        private const float RetireSeconds = 2f;

        /// <summary>
        /// How many nets one gun may have in the world at once. A fourth tears the oldest.
        ///
        /// <para>
        /// Three because the gun carries three charges — but the charges alone do not bound this,
        /// which is why the cap has to exist separately. A charge comes back on a timer while a net
        /// lasts up to its full hold, so a player who fires three, waits for the recharge and fires
        /// again has four nets out, and can keep going. Every one of them is a lattice being solved
        /// at ninety substeps a second.
        /// </para>
        /// <para>
        /// Per GUN rather than per world, and that is not a smaller version of the same thing: it
        /// is the only version that works. Eviction here is purely local — every machine runs the
        /// same <c>Present</c> for the same shots and so evicts the same net without a word being
        /// sent — and that holds only because ONE shooter's shots are ordered identically
        /// everywhere. Two players' shots interleave differently on different machines, so a
        /// world-wide cap would evict different nets on different machines and free a captive on
        /// one that is still held on another. It would also let one player's shot silently destroy
        /// another player's catch, which is a worse game than a slightly busier world.
        /// </para>
        /// </summary>
        private const int MaxLiveNets = 3;

        private float idleSeconds;

        /// <summary>
        /// Seconds after touchdown during which a net is still asked what it has come down on.
        ///
        /// <para>
        /// It used to be a single frame — the one on which the net first reported having landed —
        /// and that is why nets caught nothing. On that frame the net has only just met the ground:
        /// it is still an open sheet arriving edge-first, its captive is not under it yet, and the
        /// drape has not folded a single node over anything. One query at that instant asks the
        /// question at the worst possible moment and then never asks again.
        /// </para>
        /// <para>
        /// So the question is asked every frame until the net has settled. That is forgiveness of
        /// the kind GDC-L1-FEEL-0003 describes: the player aimed at the animal, the net is on the
        /// animal, and whether the two agreed on one particular frame is not a skill worth testing.
        /// It stays a WINDOW rather than becoming permanent, because a net lying in the sand should
        /// not reach up and grab something that wanders over it a minute later.
        /// </para>
        /// </summary>
        private const float SettleSeconds = 0.8f;

        /// <summary>How many nets this shooter still has in the world. For tests and the HUD.</summary>
        public int LiveNetCount => live.Count;

        /// <summary>
        /// Is this the machine that decides what this shooter's nets have caught?
        ///
        /// <para>
        /// <c>Network.Simulates</c> and NOT <c>Network.Server</c>, which is what it used to be and
        /// is a different question. <c>Network.Server</c> is <c>IsNetworked &amp;&amp; IsServer</c>,
        /// so it answers FALSE with no NetworkManager listening — and a scene played straight out
        /// of the editor is exactly that. The whole capture pass and the whole tear announcement
        /// were switched off in that session, silently: nets flew, landed, draped and held nothing
        /// whatever, with no error to say why. <c>Networking</c>'s own class summary states the
        /// rule this restores — absent netcode means offline single-player, the state in which this
        /// machine may do everything.
        /// </para>
        /// <para>
        /// Asked of this component, which lives on the shooter's spawned NetworkObject, so in a
        /// real session it is the server and nobody else. It must not be asked of the gun: an
        /// equipped item is instantiated into a hand and never spawned, so its own NetworkObject is
        /// dormant and every peer would answer yes.
        /// </para>
        /// </summary>
        private bool Decides => Network.Simulates(this);

        public static SnareReceiver Ensure(GameObject shooter)
        {
            if (shooter == null) return null;

            return shooter.TryGetComponent(out SnareReceiver existing)
                ? existing
                : shooter.AddComponent<SnareReceiver>();
        }

        /// <summary>
        /// Start watching a net the gun has just put in the world, tearing the oldest if this one
        /// takes the count past <see cref="MaxLiveNets"/>.
        /// </summary>
        public void Track(int netId, SnareCatch net, LayerMask catchableLayers, float captureHeight)
        {
            if (net == null) return;

            if (!live.ContainsKey(netId)) order.Add(netId);

            live[netId] = new Tracked
            {
                Net = net,
                Layers = catchableLayers,
                CaptureHeight = captureHeight,
            };

            idleSeconds = 0f;

            while (order.Count > MaxLiveNets) Retire(order[0]);
        }

        /// <summary>
        /// Take one net out of the world and stop watching it.
        ///
        /// <para>
        /// <c>Tear</c> rather than <c>Destroy</c>: it releases the captives and starts the rot, so
        /// an evicted net slackens and fades the way one that gave out does instead of blinking
        /// out of existence in front of whoever was watching it.
        /// </para>
        /// </summary>
        private void Retire(int netId)
        {
            if (live.TryGetValue(netId, out Tracked tracked) && tracked.Net != null) tracked.Net.Tear();

            Forget(netId);
        }

        /// <summary>Drop every record of a net. The four collections have to stay in step.</summary>
        private void Forget(int netId)
        {
            live.Remove(netId);
            order.Remove(netId);
            announcedTorn.Remove(netId);
            resolvedLanding.Remove(netId);
        }

        // ── Hearing about a catch ──────────────────────────────────────────────
        //
        // Snared and SnareFreed are broadcast to All, the sender included, so both have to be
        // idempotent: Capture refuses a body the net already holds and Tear refuses a net already
        // rotting. That is what lets a machine receive a message twice, or receive one it already
        // acted on locally, and do nothing the second time.
        //
        // SnareStruggled is the exception, deliberately. It reports an EVENT rather than a state,
        // so it is delivered once, to the server only, and counted once — see OnSnareStruggled.

        private void OnEnable()
        {
            this.NetOn(NetMsg.Snared, OnSnared);
            this.NetOn(NetMsg.SnareFreed, OnSnareFreed);
            this.NetOn(NetMsg.SnareStruggled, OnSnareStruggled);
        }

        /// <summary>
        /// Going away takes this machine's nets with it.
        ///
        /// <para>
        /// Reached when the shooter despawns or their chunk unloads with nets still live. Tearing
        /// them here releases the captives THIS machine is holding, which is the half that can be
        /// fixed cleanly.
        /// </para>
        /// <para>
        /// The other half cannot. A peer's net is waiting to be TOLD it has torn, and that message
        /// goes out on the shooter's relay — which a player being destroyed no longer has. Nothing
        /// can be sent from here, by anyone, at that moment. So the peers are covered instead by
        /// the failsafe in <see cref="SnareCatch"/>: every net knows the longest it could possibly
        /// last and stops by itself. That is a documented limitation rather than a fix — a peer's
        /// net outlives the shooter's death by up to its own worst-case lifetime.
        /// </para>
        /// </summary>
        private void OnDisable()
        {
            this.NetOff(NetMsg.Snared, OnSnared);
            this.NetOff(NetMsg.SnareFreed, OnSnareFreed);
            this.NetOff(NetMsg.SnareStruggled, OnSnareStruggled);

            foreach (Tracked tracked in live.Values)
                if (tracked.Net != null) tracked.Net.Tear();

            live.Clear();
            order.Clear();
        }

        private void OnSnared(in NetArg arg, ulong sender)
        {
            if (live.TryGetValue(arg.A, out Tracked tracked) && tracked.Net != null)
                tracked.Net.Capture(arg.Resolve());
        }

        private void OnSnareFreed(in NetArg arg, ulong sender)
        {
            if (live.TryGetValue(arg.A, out Tracked tracked) && tracked.Net != null)
                tracked.Net.Tear();
        }

        /// <summary>
        /// A captive of one of this shooter's nets fought it, once.
        ///
        /// <para>
        /// Unlike the two above this is not idempotent and must not be: the whole content of the
        /// message is that one more input happened, so acting on it twice would count it twice.
        /// That is what makes it a <c>NetTo.Server</c> message rather than a broadcast — it is
        /// delivered exactly once, to the one machine that spends the pool.
        /// </para>
        /// <para>
        /// Which net is worked out here rather than named on the wire. A body can only be in one:
        /// <c>SnaredBody.Bind</c> and <c>SnareTether.Bind</c> both refuse a second net, so
        /// <c>SnareCatch.Capture</c> never records a captive something else already holds — which
        /// makes the first net that answers the only one that could have.
        /// </para>
        /// </summary>
        private void OnSnareStruggled(in NetArg arg, ulong sender)
        {
            if (!Decides) return;

            GameObject captive = arg.Resolve();
            if (captive == null || !MayActFor(captive, sender)) return;

            foreach (Tracked tracked in live.Values)
                if (tracked.Net != null && tracked.Net.Struggled(captive)) return;
        }

        /// <summary>
        /// May <paramref name="sender"/> speak for <paramref name="captive"/>?
        ///
        /// <para>
        /// Checked rather than trusted, the same way <c>VehicleStation</c> and
        /// <c>SeatedRider.OnLeaveSeatRequested</c> check theirs. Without it any client could report
        /// struggles on any captive's behalf and drain a net holding somebody else's catch, which
        /// is a way of freeing another player's prize while never being netted at all.
        /// </para>
        /// <para>
        /// The server and unnetworked bodies are not checked: the server speaks for everyone by
        /// definition, and offline there is only one machine, whose captives have no owner to
        /// disagree with.
        /// </para>
        /// </summary>
        private static bool MayActFor(GameObject captive, ulong sender)
        {
            if (!Network.IsNetworked) return true;
            if (sender == NetworkManager.ServerClientId) return true;

            NetworkObject body = captive.GetComponent<NetworkObject>();
            if (body == null || !body.IsSpawned) return true;

            return body.OwnerClientId == sender;
        }

        private void Update()
        {
            if (live.Count == 0)
            {
                // Nothing left to watch. A player who fired one net an hour ago should not still be
                // carrying the machinery for it.
                idleSeconds += Time.deltaTime;
                if (idleSeconds >= RetireSeconds) Destroy(this);
                return;
            }

            idleSeconds = 0f;

            ResolveLandedNets();
            AnnounceTornNets();
        }

        /// <summary>
        /// Decide what each landed net caught.
        ///
        /// <para>
        /// Polled against a flag the NET owns rather than waited on by a coroutine. A coroutine
        /// would have to be started somewhere, and the only natural place is the item — which is
        /// destroyed the moment the player switches hotbar slot, taking the pending catch with it.
        /// The net would then land, drape over the animal and hold nothing at all.
        /// </para>
        /// <para>
        /// The query volume is the net's OWN footprint, taken from its nodes. A fixed box around the
        /// muzzle would be wrong twice over: the net has flown some tens of metres by then, and
        /// after the drape its shape is whatever the ground and the captive made of it.
        /// </para>
        /// </summary>
        private void ResolveLandedNets()
        {
            if (!Decides) return;

            scratchIds.Clear();
            scratchIds.AddRange(live.Keys);

            foreach (int netId in scratchIds)
            {
                Tracked tracked = live[netId];
                if (tracked.Net == null || !tracked.Net.HasLanded) continue;

                // Already settled and done with. Capture itself is idempotent, so re-asking inside
                // the window is free; this is only what stops the asking.
                if (resolvedLanding.Contains(netId)) continue;
                if (tracked.Net.SecondsSinceLanding >= SettleSeconds) resolvedLanding.Add(netId);

                Bounds footprint = tracked.Net.Footprint;
                Vector3 extents = footprint.extents + Vector3.up * tracked.CaptureHeight;

                Collider[] hits = Physics.OverlapBox(
                    footprint.center, extents, Quaternion.identity,
                    tracked.Layers, QueryTriggerInteraction.Ignore);

                foreach (Collider hit in hits)
                {
                    GameObject body = hit.attachedRigidbody != null
                        ? hit.attachedRigidbody.gameObject
                        : hit.gameObject;

                    // The shooter is standing at the muzzle and is inside the volume on any shot
                    // fired at their own feet. Netting yourself with your own gun is a bug, not a
                    // feature. Everything that is neither a player nor a creature is refused by
                    // SnareCatch.Capture itself, so scenery cannot be caught however wide the
                    // layer mask is authored.
                    if (body == gameObject) continue;
                    if (!tracked.Net.Capture(body)) continue;

                    NetMessaging.NetSendTo(gameObject, NetMsg.Snared,
                        new NetArg(a: netId).With(body), NetTo.All);
                }
            }
        }

        /// <summary>
        /// Tell everyone a net has given out — once per net, not once per frame.
        ///
        /// Without the <see cref="announcedTorn"/> guard this sends on every frame from the moment
        /// the pool empties until the object is destroyed, on a channel every player in the session
        /// is listening to. The destroyed nets are pruned in the same pass, because the dictionary
        /// otherwise grows for as long as this shooter keeps firing.
        /// </summary>
        private void AnnounceTornNets()
        {
            scratchIds.Clear();
            scratchIds.AddRange(live.Keys);

            foreach (int netId in scratchIds)
            {
                Tracked tracked = live[netId];

                if (tracked.Net == null)
                {
                    Forget(netId);
                    continue;
                }

                if (!Decides || tracked.Net.HoldFraction > 0f) continue;
                if (!announcedTorn.Add(netId)) continue;

                NetMessaging.NetSendTo(gameObject, NetMsg.SnareFreed, new NetArg(a: netId), NetTo.All);
            }
        }
    }
}
