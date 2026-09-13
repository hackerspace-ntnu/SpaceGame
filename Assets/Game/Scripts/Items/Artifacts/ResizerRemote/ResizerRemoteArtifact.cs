// The resizer remote.
//
// A handset with a whip antenna. Point it at somebody and hold Use: they shrink, or they grow,
// depending on which way the polarity dial on the chin is turned. Let go and they come back to
// their own size on their own. Tap Use twice to turn the dial.
//
// WHAT THIS ITEM OWNS, AND WHAT IT DOES NOT.
//
// It owns WHERE the signal goes, WHICH WAY it runs, and WHAT IT COSTS, and nothing else. Scale,
// mass, the curve between them and the whole of the decay are StatusKind.Inflated's — one signed
// scalar on the body, presented by InflatedStatus, replicated by StatusReceiver, derived identically
// on every machine from three facts that travel once each. This file never touches a localScale and
// never touches a mass. That is not tidiness: a second implementation of "make this thing big" would
// be a second answer to what happens on a load, on a join and on a death. The arithmetic of the
// scalar is InflationScalar's, shared with the inflator nozzle, and so is the rule about which
// bodies may be resized at all.
//
// WHAT MAKES THIS A SECOND ITEM AND NOT THE NOZZLE AGAIN (GDC-L1-SYS-0005).
//
// The nozzle is a hose. It reaches five metres, it drives the scalar to its full ±1 — buoyant at
// one end, leaden at the other — and it is something you do standing next to somebody. This is a
// transmitter. It reaches twenty-five, and it pays for that reach in magnitude: `signalStrength`
// caps it well short of either end, so nothing this handset touches ever floats away or shrinks to
// a speck. Reach against magnitude is the whole trade, and it is why the two items are a choice
// rather than one item and its strictly-better sibling. They deliberately share the property they
// pump, which is what makes them compose: a target one player is inflating is a target another can
// pull back down, and neither item had to learn about the other.
//
// WHO DOES WHAT.
//
//   • OnRequestUse / OnRequestHold — the OWNER, the one machine with a live camera. It writes the
//     aim RAY (origin in P, rotation in R) and the polarity, and it is the only machine that knows
//     the dial has been turned at all — the turn is a gesture, and a gesture belongs to the machine
//     the fingers are on.
//   • Use / Hold — the SERVER, because another body's size and weight is contested world state that
//     two players can push at once (GDC-L1-MP-0004). It traces the same ray and moves the scalar by
//     the seconds that really elapsed, toward its OWN serialized limit. The owner's word decides
//     the SIGN and nothing else; the size the signal can reach is the prefab's, not the client's.
//   • Present / PresentHold — EVERY machine: the whip extending, the lamp, the beam, the hum, the
//     dial turning. All of it derived from the hold stream and the replicated status, none of it
//     sent. That is also the target's warning that somebody is pointing this at them.
//
// WHAT MAY NOT BE WRITTEN TO. arg.A is the hotbar slot code on the press AND on every hold tick, and
// the server reads it back as its stale-slot guard, so flags put there are silently refused for
// every slot but the matching one. Bit 0 of B is EquipmentController's active flag. This item's one
// flag sits in bit 1.
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay.Status;

namespace SpaceGame.Items
{
    /// <summary>
    /// A radio handset that drives whatever it is pointed at up or down in size.
    ///
    /// <para>
    /// <b>maxUses must stay unlimited (−1) on the prefab.</b> The battery is this item's only cost,
    /// and a charge count beside it would delete the handset out of the player's inventory the first
    /// time it ran flat. There is deliberately no <c>OnMaxUsesReached</c> override here, because
    /// with an unlimited item it would be a method that can never run.
    /// </para>
    /// <para>
    /// <b>Griefing is bounded by the design rather than by a rule bolted on</b>
    /// (<c>GDC-L1-MP-0002</c>). The nozzle's bound was its reach and this one has given that up, so
    /// it pays four other ways. The magnitude is capped short of both ends of the range, so the
    /// worst case is being noticeably odd-sized rather than helpless. The condition decays on its
    /// own clock the instant the signal stops, and the signal stops the moment the target steps
    /// behind anything. The battery is a real limit on how long one player can hold another. And
    /// the handset announces itself on every machine while it transmits — an extended whip, a lit
    /// lamp and a beam — so being resized is never something that happens out of a clear sky.
    /// </para>
    /// </summary>
    public sealed class ResizerRemoteArtifact : ToolItem
    {
        [Header("Reach")]
        [Tooltip("How far the signal carries, in metres. Long on purpose: reaching something you " +
                 "cannot walk up to is the whole of what this has that the inflator nozzle does " +
                 "not, and it is paid for in Signal strength below.")]
        [SerializeField, Min(1f)] private float range = 25f;

        [Tooltip("What the signal can lock onto. Triggers are always ignored — a detection volume " +
                 "is not a body.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Signal")]
        [Tooltip("How far along the shared inflation range this handset can drive a body, 0..1. " +
                 "Deliberately short of 1: at the top of the range a body is buoyant and at the " +
                 "bottom it is a speck, and a thing that can do either from twenty-five metres is " +
                 "not a gadget, it is a removal tool. The SERVER reads this value, not the client " +
                 "that pressed the button, so the sign is all a machine on the other end gets to " +
                 "decide.")]
        [SerializeField, Range(0.1f, 1f)] private float signalStrength = 0.6f;

        [Tooltip("Seconds of held transmission to take a body from its own size to the far end of " +
                 "this handset's range. Slower than the nozzle's three, because this is done at a " +
                 "distance and the time is what gives the target a chance to break the beam. The " +
                 "decay is the status's own clock and is deliberately not here.")]
        [SerializeField, Min(0.05f)] private float secondsToFull = 5f;

        [Tooltip("Which way the dial starts turned: on, the handset enlarges; off, it shrinks. " +
                 "The starting position only — the player turns it in play with a double tap, and " +
                 "where they left it is saved with the slot.")]
        [SerializeField] private bool startsEnlarging = true;

        [Tooltip("Seconds between the two taps that turn the dial. A fixed window, never scaled " +
                 "with the game clock, because a gesture whose timing depends on the situation is " +
                 "one the player cannot learn.")]
        [SerializeField, Min(0.05f)] private float flipWindow = 0.3f;

        [Header("Battery")]
        [Tooltip("The handset's own cell — the shared SupplyReservoir, tuned on this prefab. Found " +
                 "on this prefab when unset.")]
        [SerializeField] private SupplyReservoir battery;

        [Header("Stream")]
        [Tooltip("Seconds of silence after which the transmitter shuts itself off. The safety net " +
                 "for a release that never arrived — a dropped packet, or a player who " +
                 "disconnected mid-signal. Must stay comfortably above UseChannel's 0.2 s " +
                 "keepalive, or an ordinary steady hold would cut itself off between two perfectly " +
                 "normal ticks.")]
        [SerializeField, Min(0.25f)] private float holdTimeout = 0.5f;

        [Header("Presentation")]
        [Tooltip("The whip, the dial, the lamp, the beam and the hum. Found under this object when " +
                 "left empty.")]
        [SerializeField] private ResizerRemoteRig rig;

        [Header("Target highlight")]
        [Tooltip("The rim drawn round a body the handset would enlarge. Owner's machine only — it " +
                 "answers the holder's question about their own aim and is not a property of the " +
                 "body.")]
        [SerializeField] private Color growHighlight = new(0.45f, 0.85f, 1f, 1f);

        [Tooltip("The rim drawn round a body the handset would shrink.")]
        [SerializeField] private Color shrinkHighlight = new(1f, 0.62f, 0.30f, 1f);

        [Tooltip("Rim thickness as a share of how far away the body is. A share rather than a " +
                 "width: the outline shader inflates in world space, so one constant is a bold " +
                 "border at three metres and invisible at twenty-five.")]
        [SerializeField, Range(0.001f, 0.02f)] private float highlightWidthPerMetre = 0.004f;

        [Tooltip("The thinnest and thickest that rim is ever allowed to be, in metres.")]
        [SerializeField, Min(0.001f)] private float minHighlightWidth = 0.012f;
        [SerializeField, Min(0.002f)] private float maxHighlightWidth = 0.10f;

        /// <summary>
        /// Bit 1 of <c>NetArg.B</c>: the dial is turned to enlarge. Bit 0 is EquipmentController's
        /// active flag and A is the slot code — see the file header.
        /// </summary>
        private const int GrowBit = 1 << 1;

        /// <summary>Key for the dial's position in the slot's state bag. Never rename — it is in
        /// save files.</summary>
        private const string PolarityKey = "resizer.grow";

        /// <summary>
        /// The trace buffer. An instance field rather than a static one, because two of these are
        /// one hotbar scroll apart and a shared buffer would be a surprise waiting for the day
        /// somebody transmits from a turret with a second handset in the hold.
        /// </summary>
        private readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>Two presses inside <see cref="flipWindow"/> turn the dial. Owner-side only.</summary>
        private DoubleTap flip;

        /// <summary>
        /// Which way the dial is turned on THIS machine. The owner's copy is the truth and every
        /// other machine's is a mirror of the last tick it heard — which is why it is read back out
        /// of <c>UseArg</c> in Present rather than assumed.
        /// </summary>
        private bool enlarging;

        /// <summary>Is the trigger down? Set from the hold stream, on every machine.</summary>
        private bool transmitting;

        /// <summary>When this machine last heard a hold tick. See <see cref="holdTimeout"/>.</summary>
        private float lastHoldAt;

        /// <summary>
        /// Did the battery agree to this pull of the trigger?
        ///
        /// <para>
        /// Asked once, on the press, and never again — which is the whole point of
        /// <see cref="SupplyReservoir.CanStart"/>. Re-asking every tick would let a cell that had
        /// just run dry deliver one frame's worth of signal, empty, and strobe for as long as the
        /// button stayed down. Every machine asks its own reservoir on the same press and they all
        /// equipped at the same replicated fill, so they all reach the same verdict.
        /// </para>
        /// </summary>
        private bool armed;

        /// <summary>
        /// When the server last moved the scalar. The signal runs on seconds rather than on ticks,
        /// so a stream that hitches or a frame rate that drops does not change how long a body takes
        /// to reach size.
        /// </summary>
        private float lastSignalAt;

        /// <summary>The rim round whatever the crosshair is on. Owner's machine only; null on
        /// every other copy of this item, which is what keeps the cost off them.</summary>
        private ResizerTargetHighlight highlight;

        /// <summary>Another body's size and weight is contested state, so one machine decides it.</summary>
        public override UseAuthority Authority => UseAuthority.Server;

        /// <summary>Holding a signal on a target is something you do for a while.</summary>
        public override bool IsContinuous => true;

        /// <summary>
        /// Nothing self-timed: the signal stops when the finger comes up.
        ///
        /// A flat battery deliberately does NOT end the hold. The player keeps holding, nothing
        /// changes size, and the bar on the case is what tells them why — which is the whole reason
        /// the reading is on the object in their hands rather than in a HUD.
        /// </summary>
        public override bool WantsHold => false;

        // ── Owner side: the aim, and the one gesture that is not a reading ─────

        /// <summary>
        /// Owner-side, on the press. Two presses inside the window turn the dial.
        ///
        /// <para>
        /// <b>A tap is free here, which is what makes it available as a gesture.</b> A press by
        /// itself puts no signal into anything — see <see cref="Use"/> — so the two taps that turn
        /// the dial cost a body nothing, and a tap has no other meaning on this item: a handset
        /// that needs five seconds on a target cannot be fired in bursts, so there is no burst for
        /// the gesture to be confused with. That is the condition a double tap on a firing button
        /// has to meet before it is allowed to exist (<c>GDC-L1-UX-0004</c>: the wrong action
        /// should be hard to take by accident).
        /// </para>
        /// <para>
        /// Unscaled time, deliberately. A gesture read off the game clock would stretch and shrink
        /// with slow motion and stop working in a paused frame.
        /// </para>
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            flip ??= new DoubleTap(flipWindow);
            if (flip.Press(Time.unscaledTime)) enlarging = !enlarging;

            WriteAim(ref arg);
            WritePolarity(ref arg);
        }

        /// <summary>Owner-side, once per tick: where the handset points and which way it is set.</summary>
        public override void OnRequestHold(ref NetArg arg, bool active)
        {
            if (!active) return;

            WriteAim(ref arg);
            WritePolarity(ref arg);
        }

        /// <summary>
        /// The aim RAY, from the one machine that has a live camera.
        ///
        /// The ray rather than the point it lands on, so every machine traces its own — and
        /// <c>AimProvider</c> rather than the handset's own forward, because mounted, the eye is
        /// pitched with the seat and the view the player is aiming down is the mount's.
        /// </summary>
        private void WriteAim(ref NetArg arg)
        {
            AimProvider aim = aimProvider;
            if (aim == null || aim.AimTransform == null) return;

            Ray ray = aim.GetAimRay();
            arg.P = ray.origin;
            arg.R = Quaternion.LookRotation(ray.direction);
        }

        /// <summary>
        /// OR the dial's position into the payload — never assign, because bit 0 already carries
        /// EquipmentController's active flag on a hold tick.
        ///
        /// <para>
        /// There is nothing for the server to validate here, and that is worth saying out loud
        /// since the neighbouring item validates its own flag hard. Polarity is not a privileged
        /// claim: a client that forged this bit has asserted that they turned a dial on an item
        /// they are holding, which they are entitled to do by pressing a button twice. What a
        /// client cannot forge is HOW FAR the signal goes — <see cref="signalStrength"/> is read
        /// off the server's own copy of the prefab (<c>GDC-L1-MP-0004</c>).
        /// </para>
        /// </summary>
        private void WritePolarity(ref NetArg arg)
        {
            if (enlarging) arg.B |= GrowBit;
        }

        // ── The press ──────────────────────────────────────────────────────────

        /// <summary>
        /// Authority-side. A press by itself moves nothing — a carrier takes a moment to lock — so
        /// all it does is open the channel and start the clock the first hold tick measures against.
        /// </summary>
        protected override void Use() => Prime(UseArg);

        /// <summary>
        /// Every machine's transmitter, including the owner's, immediately: the whip goes up and
        /// the lamp lights on the frame of the press rather than after a round trip
        /// (<c>GDC-L1-FEEL-0002</c>).
        /// </summary>
        protected override void Present() => Prime(UseArg);

        // ── The hold ───────────────────────────────────────────────────────────

        /// <summary>
        /// Authority-side, once per tick: hold the channel open, then put this tick's seconds of
        /// signal into whatever the ray is on.
        ///
        /// The target is traced from THIS tick's own arg rather than remembered, because the
        /// authority is the only machine that moves anything — a dedicated server never receives
        /// <see cref="PresentHold"/> at all, being no part of the "others" its own broadcast goes to.
        /// </summary>
        protected override void Hold(NetArg arg, bool active)
        {
            ApplyHold(arg, active);

            if (active) Transmit(arg);
        }

        /// <summary>Every machine: the whip, the dial, the lamp, the beam and the hum.</summary>
        protected override void PresentHold(NetArg arg, bool active)
        {
            ApplyHold(arg, active);

            if (!active || rig == null) return;

            bool found = TraceTarget(arg, out RaycastHit hit, out StatusReceiver body);

            // Where the beam is drawn is each machine's own trace rather than a point in the
            // message, because P is already carrying the ray every machine needs. A machine whose
            // trace missed draws it out to the end of the reach instead of dropping it, which is
            // also what a real transmitter would do.
            rig.SetBeam(found ? hit.point : AimRay(arg).GetPoint(range));

            // The readout is the TARGET's size, not this handset's battery — the one reading in the
            // set that is about something other than this item. A body the signal has made no
            // progress on reads zero, which is also what open sky reads.
            rig.SetReading(InflationScalar.Progress(InflationScalar.Presented(body), Toward(arg)));
        }

        /// <summary>
        /// Shared by both halves, and idempotent, because on a host both halves run for the same
        /// tick.
        ///
        /// <para>
        /// The final tick arrives with <paramref name="active"/> false, and shutting the channel on
        /// it must work on a machine that never saw the ticks before it — which is why it is a plain
        /// assignment rather than anything that reads the current state first.
        /// </para>
        /// </summary>
        private void ApplyHold(NetArg arg, bool active)
        {
            if (active) OpenChannel(arg);
            else Close();
        }

        /// <summary>
        /// Start a NEW draw, on the press and on the press only: ask the battery once, and start
        /// the server's clock so the first hold tick measures a real interval rather than zero.
        ///
        /// <para>
        /// Kept apart from <see cref="OpenChannel"/>, which runs on every tick, and the split is
        /// not tidiness. Re-asking <see cref="SupplyReservoir.CanStart"/> per tick would let a cell
        /// that had just run dry deliver one tick's signal, empty, and strobe for as long as the
        /// button stayed down — and re-stamping the clock per tick would zero the elapsed seconds
        /// every time, so the handset would hum, drain and change nothing at all.
        /// </para>
        /// </summary>
        private void Prime(NetArg arg)
        {
            armed = battery == null || battery.CanStart;
            lastSignalAt = Time.time;

            OpenChannel(arg);
        }

        /// <summary>Hold the channel open for one more tick, and take the dial off the wire.</summary>
        private void OpenChannel(NetArg arg)
        {
            ReadPolarity(arg);
            transmitting = true;
            lastHoldAt = Time.time;
        }

        private void Close()
        {
            transmitting = false;

            // Pushed straight through rather than left to the next Update, because the two paths
            // that close a channel for good — an unequip and a disable — both run on a frame this
            // object may not see the end of.
            if (rig == null) return;

            rig.SetTransmitting(false);
            rig.SetReading(0f);
            rig.ClearBeam();
        }

        /// <summary>
        /// Take the dial's position off the wire, on every machine that is not the owner's.
        ///
        /// <para>
        /// The owner is skipped because its own field IS the truth and the message is a copy of it
        /// — and because a press and its echo can cross: reading the bit back on the owner would
        /// let a stale <c>ItemUsed</c> from before a flip undo the flip a frame after the player
        /// made it.
        /// </para>
        /// </summary>
        private void ReadPolarity(NetArg arg)
        {
            if (OwnerIsLocal()) return;

            enlarging = (arg.B & GrowBit) != 0;
        }

        /// <summary>The scalar this handset is driving toward, given a tick's payload.</summary>
        private float Toward(NetArg arg) =>
            ((arg.B & GrowBit) != 0 ? 1f : -1f) * signalStrength;

        // ── The signal ─────────────────────────────────────────────────────────

        /// <summary>
        /// Authority-side. Put one tick's worth of signal into whatever the ray is on.
        ///
        /// <para>
        /// The new scalar is derived from what the body is PRESENTING rather than integrated on this
        /// item, so two players working on the same body add up (or cancel) instead of fighting, and
        /// a body left half-changed is picked up where it actually is rather than snapped back to
        /// where it once was. The item holds no per-target number at all, which is also why an
        /// unequip in the middle of a transmission loses nothing worth keeping.
        /// </para>
        /// </summary>
        private void Transmit(NetArg arg)
        {
            // Capped at the timeout, which is already this stream's definition of "stale": a tick
            // that arrives after a hitch, or after a press whose message went missing, is worth one
            // gap's signal and not the whole minute the item spent in a pocket.
            float elapsed = Mathf.Min(Time.time - lastSignalAt, holdTimeout);
            lastSignalAt = Time.time;

            if (!armed || elapsed <= 0f) return;
            if (battery != null && battery.Charge <= 0f) return;

            TraceTarget(arg, out _, out StatusReceiver body);
            if (!InflationScalar.CanResize(body)) return;

            // Zero seconds is the condition's own duration, refreshed every tick — which is what
            // holds a body at size while the signal is on it and starts it easing back the moment
            // it is not. The handset does not get to decide how long a decay takes.
            body.Apply(StatusKind.Inflated,
                       magnitude: InflationScalar.Pumped(InflationScalar.Presented(body),
                                                         Toward(arg), secondsToFull, elapsed),
                       source: owner != null ? owner.transform : null);
        }

        // ── The trace ──────────────────────────────────────────────────────────

        /// <summary>The ray the owner reported, rebuilt on whichever machine is asking.</summary>
        private static Ray AimRay(NetArg arg) => new(arg.P, arg.R * Vector3.forward);

        /// <summary>
        /// The nearest thing the handset is on, skipping the holder's own body and the machine they
        /// are strapped into, plus the status receiver it carries if it has one.
        ///
        /// <para>
        /// The ray starts at the holder's eye, inside the holder, so an unfiltered trace resizes the
        /// person holding it. <c>AimProvider.NearestOutside</c> is the shared rule for that and is
        /// static precisely so a trace like this one can borrow it; it also picks the nearest hit by
        /// hand, because <c>RaycastNonAlloc</c> neither sorts its buffer nor says when it truncated
        /// one, and it asks the COLLIDER's parentage rather than <c>RaycastHit.transform</c>, which
        /// over a vehicle is its root.
        /// </para>
        /// <para>
        /// The carrier is the owner's transform ROOT: mounting parents the rider under the mount, so
        /// the root IS the machine they are riding, and on their own feet it is the owner themselves.
        /// </para>
        /// <para>
        /// A receiver is never created here. One added by the server alone would resize for the
        /// server and for nobody else, because the status message is addressed to the body's own
        /// relay and a body nothing has subscribed on drops it without a word. A thing that can be
        /// resized says so on its own prefab.
        /// </para>
        /// </summary>
        private bool TraceTarget(NetArg arg, out RaycastHit hit, out StatusReceiver body)
        {
            hit = default;
            body = null;

            if (!arg.HasOrientation || owner == null) return false;

            int count = Physics.RaycastNonAlloc(AimRay(arg), hits, range, hitMask,
                                                QueryTriggerInteraction.Ignore);

            if (!AimProvider.NearestOutside(hits, count, owner.transform, owner.transform.root,
                                            out hit))
                return false;

            body = StatusReceiver.Of(hit.collider.gameObject);
            return true;
        }

        // ── The battery, and the rim under the crosshair ───────────────────────

        /// <summary>
        /// Drive the battery, the presentation and — on the holder's machine only — the rim round
        /// whatever they are pointing at.
        ///
        /// <para>
        /// Runs on every machine off its own clock, which is what keeps the rates per SECOND rather
        /// than per tick: a fifteen-hertz stream would otherwise make a flat battery a function of
        /// the frame rate. Safe to declare — nothing in this hierarchy has an <c>Update</c> for this
        /// one to hide.
        /// </para>
        /// </summary>
        private void Update()
        {
            // A release is one message, and one message is exactly the kind of thing that goes
            // missing — along with the player who was holding the button.
            if (transmitting && Time.time - lastHoldAt > holdTimeout) Close();

            bool drawing = transmitting && armed;
            bool delivering = battery == null ? drawing : battery.Tick(Time.deltaTime, drawing);

            if (rig != null)
            {
                rig.SetTransmitting(delivering);
                rig.SetPolarity(enlarging);
            }

            RefreshHighlight();
        }

        /// <summary>
        /// Light the body under the crosshair, in the colour of the current setting.
        ///
        /// <para>
        /// The owner's machine and no other: aim is only honest where there is a live camera behind
        /// it, and the rim is the holder's own readout rather than a fact about the body — see the
        /// file header on <see cref="ResizerTargetHighlight"/>. The trace here is the ITEM's own,
        /// not the one in <see cref="TraceTarget"/>, because the crosshair moves every frame and
        /// there is no <c>NetArg</c> outside a tick.
        /// </para>
        /// <para>
        /// A body the handset could not move anyway — kinematic, or with no Rigidbody at all — is
        /// deliberately left unlit. A rim that lights on something the trigger then does nothing to
        /// is worse than no rim: it is a promise the item does not keep.
        /// </para>
        /// </summary>
        private void RefreshHighlight()
        {
            // Owner FIRST, and a live holder before anything is allocated. `OwnerIsLocal` answers
            // true for an item with no owner at all — which every copy lying in the sand is — so
            // the null check is what keeps a dropped handset from carrying a highlighter around.
            AimProvider aim = aimProvider;
            if (owner == null || aim == null || !OwnerIsLocal())
            {
                highlight?.Clear();
                return;
            }

            highlight ??= new ResizerTargetHighlight(growHighlight, shrinkHighlight,
                                                     highlightWidthPerMetre, minHighlightWidth,
                                                     maxHighlightWidth);

            int count = Physics.RaycastNonAlloc(aim.GetAimRay(), hits, range, hitMask,
                                                QueryTriggerInteraction.Ignore);

            if (!AimProvider.NearestOutside(hits, count, owner.transform, owner.transform.root,
                                            out RaycastHit hit))
            {
                highlight.Clear();
                return;
            }

            StatusReceiver body = StatusReceiver.Of(hit.collider.gameObject);
            highlight.Refresh(InflationScalar.CanResize(body) ? body : null, hit.distance,
                              enlarging);
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        /// <summary>
        /// Find the presentation rig and the battery once.
        ///
        /// In <c>OnEnable</c> rather than <c>Awake</c> because a handset is enabled and disabled as
        /// it moves between the hand, the pack and the sand, and both lookups are cheap to confirm.
        /// </summary>
        private void OnEnable()
        {
            if (rig == null) rig = GetComponentInChildren<ResizerRemoteRig>(true);
            if (battery == null) battery = SupplyReservoir.On(gameObject);
        }

        private void OnDisable()
        {
            Close();
            highlight?.Dispose();
            highlight = null;
        }

        /// <summary>
        /// The dial starts where the prefab says, on every fresh instance.
        ///
        /// <c>OnEquipped</c> rather than <c>Awake</c>, and before <c>RestoreItemState</c> by
        /// design: restoring the hotbar runs the equip first and the per-slot bag second, so a slot
        /// that HAS a saved position overwrites this a moment later and one that does not keeps it.
        /// </summary>
        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);

            enlarging = startsEnlarging;
            flip = new DoubleTap(flipWindow);
        }

        /// <summary>
        /// Close the channel as the handset leaves the hand — including when what it leaves the
        /// hand as is an object lying in the sand, which must not go on humming there.
        ///
        /// <para>
        /// Nothing here touches the battery, and that is deliberate rather than incidental: the
        /// slot's <c>ItemState</c> is captured BEFORE this runs, so a level written here would be
        /// the one level the save never sees.
        /// </para>
        /// </summary>
        public override void OnUnequipped(GameObject holder)
        {
            base.OnUnequipped(holder);

            Close();
            highlight?.Dispose();
            highlight = null;
        }

        // ── State ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Where the player left the dial, into the slot's bag.
        ///
        /// <para>
        /// The one thing this instance BECOMES. Everything else about it is either a button
        /// (whether the trigger is down), somebody else's (the target's size lives on the body,
        /// whose condition is deliberately not saved at all) or already written for free — the
        /// battery's level goes into the same bag under <c>SupplyCharge</c>'s own key, because
        /// <c>UsableItem</c> forwards a slot's state to whatever reservoir sits beside it.
        /// </para>
        /// <para>
        /// A dial that did not survive a save is a small thing that is wrong every single time: a
        /// player who has settled on shrink comes back from lunch enlarging, and finds out by
        /// enlarging something.
        /// </para>
        /// </summary>
        public override void CaptureItemState(ItemState state)
        {
            base.CaptureItemState(state);
            state.Set(PolarityKey, enlarging);
        }

        /// <inheritdoc/>
        public override void RestoreItemState(ItemState state)
        {
            base.RestoreItemState(state);
            enlarging = state.GetBool(PolarityKey, startsEnlarging);
        }

        // No OnMaxUsesReached override. maxUses stays −1 on the prefab and the battery is the whole
        // cost, so an override here would be a method that can never run.
    }
}
