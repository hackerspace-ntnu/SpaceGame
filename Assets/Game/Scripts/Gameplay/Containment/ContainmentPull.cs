// Everything about a container that is currently drawing something in.
//
// This lives on the CAPTOR rather than on the container, and the reason is SnareReceiver's, one
// system over: a container in a hand is an item instance destroyed on every equip, so a pull kept
// in fields on it would be abandoned by switching hotbar slot — the captive left mid-fight on every
// other machine with nothing alive to finish or cancel it. It also has to be here for the wire.
// NetOn registers against the channel of the nearest NetworkObject, and an equipped item has one of
// its own that is never spawned, so a handler on the container would sit on a channel no relay can
// reach and never hear a word.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Gameplay.Containment
{
    /// <summary>
    /// One captor's live pull: the contest, the four message handlers, and the containers they
    /// belong to.
    ///
    /// <para>
    /// <b>One pull at a time.</b> A captor has one Use button and one hand, and the contest is what
    /// that button is doing. Drawing a second target simply replaces the first, which frees it.
    /// </para>
    /// <para>
    /// <b>The struggle is the net gun's, reused rather than reimplemented.</b>
    /// <see cref="SnareStruggleMeter"/> already answers "how hard is this captive fighting, on a
    /// scale that cannot be cheated by pressing faster" for a net and for the leash's hogtie; this
    /// is its third consumer. Two meters exist per fight, and the duplication is the design: the
    /// captive's own machine keeps one to decide what it may SEND (<see cref="PulledBody"/>), and
    /// this one runs on the authority to decide what those messages are WORTH. Sharing one would
    /// mean putting a level on the wire, which is handing the client the escape
    /// (GDC-L1-MP-0004).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    public sealed class ContainmentPull : MonoBehaviour
    {
        /// <summary>
        /// What a containment pull puts in <c>NetArg.A</c> of <c>Snared</c> and <c>SnareFreed</c>,
        /// so the net gun's receiver on the same body drops it.
        ///
        /// <para>
        /// Zero because <c>SnareReceiver</c> keys its live nets by the shot's rolled seed — a full
        /// 32-bit random — and looks each message up in that dictionary, so an id it has no net for
        /// is dropped without a word. Zero is a value a seed can take, once in about four billion
        /// shots, and the whole consequence of that coincidence would be one net being asked to
        /// capture a body it is nowhere near, which <c>SnareCatch.Capture</c> refuses on its own.
        /// The two systems share these ids by design; this is what keeps them out of each other's
        /// way.
        /// </para>
        /// </summary>
        private const int PullCode = 0;

        /// <summary>
        /// Seconds with nothing to do before this retires itself.
        ///
        /// Not zero, for <c>SnareReceiver.RetireSeconds</c>' reason: destroying the component the
        /// instant the last container is put away leaves a window in which re-equipping on the same
        /// frame attaches to an object already queued for destruction.
        /// </summary>
        private const float RetireSeconds = 2f;

        /// <summary>Every container this captor is carrying. Usually exactly one.</summary>
        private readonly List<ContainerHold> attached = new();

        private ContainerHold container;
        private GameObject captive;

        /// <summary>
        /// A target this pull has already refused, remembered until the beam goes off.
        ///
        /// Without it <see cref="Draw"/> re-asks the gate fifteen times a second for as long as the
        /// trigger is held — which repeats the refusal's log line at the same rate, and walks every
        /// collider on the body to measure it again for an answer that cannot have changed.
        /// </summary>
        private GameObject refused;
        private ContainmentSettings settings;
        private SnareStruggleMeter meter;
        private float progress;
        private float idleSeconds;

        /// <summary>How far in the current captive is, 0..1. Zero when nothing is being drawn.</summary>
        public float Progress => progress;

        /// <summary>What is being drawn in right now, or null.</summary>
        public GameObject Captive => captive;

        /// <summary>How hard the captive is fighting, 0..1, as the AUTHORITY scores it.</summary>
        public float StruggleLevel => meter?.Level ?? 0f;

        /// <summary>A pull started on this machine or elsewhere. For the beam and the sound.</summary>
        public event System.Action<GameObject> PullBegan;

        /// <summary>The pull ended, however it ended. Idempotent — may fire without a matching begin.</summary>
        public event System.Action<GameObject> PullEnded;

        /// <summary>A captive came out of a container this captor is carrying, at this point.</summary>
        public event System.Action<Vector3> Uncorked;

        /// <summary>
        /// Is this the machine that decides what this captor's containers do?
        ///
        /// Asked of this component, which lives on the captor's spawned NetworkObject, so in a real
        /// session it is the server and nobody else. <c>Network.Simulates</c> rather than
        /// <c>Network.Server</c>, which answers false with no NetworkManager listening — a scene
        /// played straight out of the editor is exactly that, and the whole contest would be
        /// switched off in it, silently.
        /// </summary>
        private bool Decides => Network.Simulates(this);

        public static ContainmentPull Ensure(GameObject captor)
        {
            if (captor == null) return null;

            return captor.TryGetComponent(out ContainmentPull existing)
                ? existing
                : captor.AddComponent<ContainmentPull>();
        }

        /// <summary>The captor's pull if they have one. Never creates — for teardown paths.</summary>
        public static ContainmentPull Find(GameObject captor) =>
            captor != null && captor.TryGetComponent(out ContainmentPull existing) ? existing : null;

        /// <summary>This captor has picked up or equipped a container.</summary>
        public void Attach(ContainerHold hold)
        {
            if (hold == null || attached.Contains(hold)) return;

            attached.Add(hold);
            idleSeconds = 0f;
        }

        /// <summary>This captor has put a container away. A pull it was running ends with it.</summary>
        public void Detach(ContainerHold hold)
        {
            if (hold == null) return;

            attached.Remove(hold);
            if (container == hold) Stop();
        }

        // ── The contest ────────────────────────────────────────────────────────

        /// <summary>
        /// Keep drawing <paramref name="target"/> into <paramref name="hold"/>. Authority only;
        /// call once per hold tick.
        ///
        /// <para>
        /// The draw rate is the whole fill divided by the authored fill time, less whatever the
        /// captive's struggle is worth. At a struggle multiplier of 1 a saturated fight exactly
        /// cancels the draw — a promise to the person being aimed at, not a balance knob — and
        /// above 1 it runs backwards, so letting up costs them ground.
        /// </para>
        /// </summary>
        /// <returns>True on the tick the target actually went in.</returns>
        public bool Draw(ContainerHold hold, GameObject target, float delta)
        {
            if (!Decides) return false;

            if (hold == null || target == null || hold.IsFull)
            {
                // The beam is off whatever it was on, so a refusal it was carrying is spent: the
                // next press gets a fresh answer, which is what lets a rider dismounting or a
                // prefab being registered change one.
                refused = null;
                Stop();
                return false;
            }

            // A different target under the beam is a different fight. Ending the old one frees it
            // rather than leaving it half drawn on every machine.
            if (target != captive || hold != container)
            {
                if (ReferenceEquals(target, refused)) return false;

                Stop();

                if (!Begin(hold, target))
                {
                    refused = target;
                    return false;
                }
            }

            meter.Advance(delta);

            float rate = (1f - settings.StruggleMultiplier * meter.Level) / settings.FillSeconds;
            progress = Mathf.Clamp01(progress + rate * delta);

            if (progress < 1f) return false;

            GameObject drawn = captive;
            bool filled = hold.TryFill(drawn);

            // Either way the fight is over: a fill that was refused at the last moment must not
            // leave the captive pinned at full progress forever.
            Stop();
            return filled;
        }

        /// <summary>
        /// End whatever is being drawn, and tell everyone. Safe from anywhere and safe twice.
        ///
        /// <para>
        /// Called from the authority's own <c>Hold</c> path — the release tick, the beam finding
        /// nothing — and from <see cref="Detach"/>, which runs on every machine because putting the
        /// container away is a local fact. On a machine that does not decide it ends the pull
        /// locally and announces nothing, which is right: the authority's own copy is still running
        /// and will announce the end when it happens.
        /// </para>
        /// </summary>
        public void Stop()
        {
            // Guarded on the CONTAINER rather than on the captive, because Unity reports a
            // destroyed object as null: a captive that was despawned under the beam leaves a live
            // pull whose captive field already reads as nothing, and guarding on that field would
            // make exactly that case unstoppable.
            if (container == null) return;

            GameObject was = captive;
            bool wasDeciding = Decides;

            Forget();

            if (wasDeciding)
                NetMessaging.NetSendTo(gameObject, NetMsg.SnareFreed, new NetArg(a: PullCode), NetTo.All);

            PullEnded?.Invoke(was);
        }

        /// <summary>Start a fight. Authority only — <see cref="Draw"/> is the one caller.</summary>
        private bool Begin(ContainerHold hold, GameObject target)
        {
            if (!ContainmentFit.TryFit(target, hold.Settings, out _)) return false;

            Adopt(hold, target);

            NetArg arg = new NetArg(a: PullCode, b: IndexOfContainer(hold)).With(target);
            NetMessaging.NetSendTo(gameObject, NetMsg.Snared, arg, NetTo.All);

            return true;
        }

        /// <summary>
        /// Take up a fight, on any machine. The authority reaches this through
        /// <see cref="Begin"/>; every other machine reaches it through <see cref="OnSnared"/>.
        /// </summary>
        private void Adopt(ContainerHold hold, GameObject target)
        {
            container = hold;
            captive = target;
            settings = hold.Settings;
            refused = null;
            progress = 0f;
            meter = new SnareStruggleMeter(settings.MaxUsefulStruggleRate, settings.StruggleDecaySeconds);
            idleSeconds = 0f;

            // The captive's own half of the fight: their keys, throttled, reported back here. Bound
            // on every machine, because the one that matters is the captive's and nothing here
            // knows which that is.
            PulledBody.Ensure(target)?.Begin(gameObject, hold, settings);

            PullBegan?.Invoke(target);
        }

        /// <summary>Drop every trace of a fight without announcing anything.</summary>
        private void Forget()
        {
            if (captive != null) PulledBody.Find(captive)?.End();

            container = null;
            captive = null;
            settings = null;
            meter = null;
            progress = 0f;
        }

        // ── Messages ───────────────────────────────────────────────────────────
        //
        // Snared and SnareFreed are broadcast to All, the sender included, so both have to be
        // idempotent — a machine that receives one it already acted on locally does nothing the
        // second time. SnareStruggled is the exception, deliberately: it reports an EVENT rather
        // than a state, so it is delivered once, to the server only, and counted once.

        private void OnEnable()
        {
            this.NetOn(NetMsg.Snared, OnSnared);
            this.NetOn(NetMsg.SnareFreed, OnSnareFreed);
            this.NetOn(NetMsg.SnareStruggled, OnSnareStruggled);
            this.NetOn(NetMsg.Released, OnReleased);
        }

        /// <summary>
        /// Going away takes this machine's pull with it.
        ///
        /// Locally only, and the omission is the same one <c>SnareReceiver.OnDisable</c> documents:
        /// this is reached when the captor despawns or their chunk unloads, and at that moment
        /// there is no relay left to announce anything from. Every other machine's copy is ended by
        /// its own captor going away, so the pull cannot outlive the captor anywhere.
        /// </summary>
        private void OnDisable()
        {
            this.NetOff(NetMsg.Snared, OnSnared);
            this.NetOff(NetMsg.SnareFreed, OnSnareFreed);
            this.NetOff(NetMsg.SnareStruggled, OnSnareStruggled);
            this.NetOff(NetMsg.Released, OnReleased);

            GameObject was = captive;
            Forget();

            if (was != null) PullEnded?.Invoke(was);
        }

        /// <summary>
        /// The authority says this captor has hold of somebody.
        ///
        /// <c>A</c> tells a containment pull from one of this captor's nets — see
        /// <see cref="PullCode"/> — and <c>B</c> says which of their containers is doing it, by
        /// the positional index every machine agrees on for a component an entity has more than
        /// one of.
        /// </summary>
        private void OnSnared(in NetArg arg, ulong sender)
        {
            if (arg.A != PullCode) return;

            GameObject target = arg.Resolve();
            if (target == null || target == captive) return;

            ContainerHold hold = ContainerAt(arg.B);
            if (hold == null) return;

            Adopt(hold, target);
        }

        private void OnSnareFreed(in NetArg arg, ulong sender)
        {
            if (arg.A != PullCode || container == null) return;

            GameObject was = captive;
            Forget();
            PullEnded?.Invoke(was);
        }

        /// <summary>
        /// The captive fought, once.
        ///
        /// <para>
        /// Not idempotent, and must not be: the whole content of the message is that one more input
        /// happened, so acting on it twice would count it twice. That is why it is a
        /// <c>NetTo.Server</c> message rather than a broadcast.
        /// </para>
        /// <para>
        /// <see cref="Network.MayActFor"/> is what stops one player reporting struggles on another's
        /// behalf. <c>NetRelay</c>'s server RPC is <c>InvokePermission.Everyone</c> — which is what
        /// lets a captive send on their captor's relay at all — so without it anybody in the session
        /// could hold somebody else out of a container from across the map.
        /// </para>
        /// <para>
        /// The captive is checked against the one being drawn rather than searched for, which is
        /// also what keeps this and <c>SnareReceiver</c> out of each other's way on a captor who is
        /// carrying both: this id names no net and no container, so each listener answers only for
        /// the body it is holding.
        /// </para>
        /// </summary>
        private void OnSnareStruggled(in NetArg arg, ulong sender)
        {
            if (!Decides || captive == null || meter == null) return;

            GameObject who = arg.Resolve();
            if (who == null || who != captive) return;
            if (!Network.MayActFor(who, sender)) return;

            // Offered, not added: this meter carries the same authored cooldown the sender's does,
            // so a client sending a hundred a second is discarded here exactly as it would be
            // there. That is what makes it safe for the wire to carry no magnitude at all.
            meter.Push();
        }

        /// <summary>
        /// A captive has come out of one of this captor's containers. Presentation, and the fact
        /// that the container is empty again.
        /// </summary>
        private void OnReleased(in NetArg arg, ulong sender)
        {
            foreach (ContainerHold hold in attached)
                if (hold != null) hold.MarkToldFull(false);

            Uncorked?.Invoke(arg.P);
        }

        /// <summary>
        /// Which of this captor's containers a message names, and the number that names it.
        ///
        /// <para>
        /// Positional over the containers under this captor — the shared answer
        /// <c>NetChannel.IndexOf</c> gives for an entity carrying several of one component, walked
        /// here rather than called there because that helper numbers within the nearest
        /// NetworkObject, and a held item has a dormant one of its own that would make every
        /// container number zero.
        /// </para>
        /// <para>
        /// The numbers agree because every machine runs the same build and equips the same prefabs
        /// into the same sockets, and they never outlive a session. Out of range means the machines
        /// disagree about what this captor is carrying, which is a state to ignore rather than to
        /// guess at.
        /// </para>
        /// </summary>
        private ContainerHold ContainerAt(int index)
        {
            ContainerHold[] held = GetComponentsInChildren<ContainerHold>(true);
            return index >= 0 && index < held.Length ? held[index] : null;
        }

        /// <summary>The number <see cref="ContainerAt"/> reads back. See there.</summary>
        private int IndexOfContainer(ContainerHold hold)
        {
            ContainerHold[] held = GetComponentsInChildren<ContainerHold>(true);

            for (int i = 0; i < held.Length; i++)
                if (ReferenceEquals(held[i], hold)) return i;

            return 0;
        }

        private void Update()
        {
            // Unity reports a destroyed object as null, so a captive despawned or unloaded under
            // the beam turns up here as a pull with a container and nobody in it. This is the only
            // place a peer notices — a peer never calls Draw.
            if (container != null && captive == null) Stop();

            if (container != null || attached.Count > 0)
            {
                idleSeconds = 0f;
                return;
            }

            // Nothing carried and nothing being drawn. A player who bottled something an hour ago
            // should not still be carrying the machinery for it.
            idleSeconds += Time.deltaTime;
            if (idleSeconds >= RetireSeconds) Destroy(this);
        }
    }
}
