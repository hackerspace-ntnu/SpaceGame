// The inflator nozzle.
//
// Hold Use on a crate, a creature or a player and it swells: bigger, and lighter faster than it is
// bigger, until it is drifting off the sand. Stop and it sinks back down on its own. Keep going past
// the top of the gauge and it bursts, harmlessly, back to the size it started.
//
// WHAT THIS ITEM OWNS, AND WHAT IT DOES NOT.
//
// It owns WHERE the pressure goes and WHAT IT COSTS, and nothing else. Scale, mass, the shape of the
// curve between them and the whole of the deflation are StatusKind.Inflated's — one signed scalar on
// the body, presented by InflatedStatus, replicated by StatusReceiver, derived identically on every
// machine from three facts that travel once each. This file never touches a localScale and never
// touches a mass. That is not tidiness: a second implementation of "make this thing big" would be a
// second answer to what happens on a load, on a join and on a death.
//
// WHO DOES WHAT.
//
//   • OnRequestUse / OnRequestHold — the OWNER, the one machine with a live camera. It writes the
//     aim RAY (origin in P, rotation in R) and times the one thing that needs a clock rather than a
//     reading: how long the target has been sitting at the top of the range. That verdict travels as
//     PopBit.
//   • Use / Hold — the SERVER, because another body's size and weight is contested world state that
//     two players can push at once (GDC-L1-MP-0004). It traces the same ray, raises the scalar by
//     the seconds that really elapsed, and validates PopBit against its OWN reading before bursting
//     anything — a client that could name a victim could burst one across the map.
//   • Present / PresentHold — EVERY machine: the plunger, the hiss, the jet, the pressure dial and
//     the burst. All of it derived from the replicated status, none of it sent.
//
// WHAT MAY NOT BE WRITTEN TO. arg.A is the hotbar slot code on the press AND on every hold tick, and
// the server reads it back as its stale-slot guard, so flags put there are silently refused for
// every slot but the matching one. Bit 0 of B is EquipmentController's active flag. This item's one
// flag sits in bit 1.
//
// THE TANK IS NOT A NEW SYSTEM. It is a SupplyReservoir on the prefab root, ticked from Update on
// every machine off the same hold stream, captured and replicated and saved for free because
// UsableItem forwards a slot's state bag to whatever reservoir sits beside it.
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay.Status;

namespace SpaceGame.Items
{
    /// <summary>
    /// A hand pump that puts pressure into whatever it is pointed at.
    ///
    /// <para>
    /// <b>maxUses must stay unlimited (−1) on the prefab.</b> The tank is this item's only cost, and
    /// a charge count beside it would deplete the nozzle out of the player's inventory the first
    /// time it emptied. There is deliberately no <c>OnMaxUsesReached</c> override here, because with
    /// an unlimited item it would be a method that can never run.
    /// </para>
    /// <para>
    /// <b>Griefing is bounded by the design rather than by a rule bolted on</b>
    /// (<c>GDC-L1-MP-0002</c>). Three things cap what one player can do to another with it: the
    /// reach is five metres, so it is a thing done at arm's length; the condition deflates on its
    /// own clock the instant the pumping stops; and holding somebody at the top of the range does
    /// not hold them there — a second past full it bursts and puts them back exactly as they were.
    /// So the worst case is a few seconds of being large, ending in a puff and no damage, and the
    /// cost of it is a tank that refills slowly.
    /// </para>
    /// </summary>
    public sealed class InflatorNozzleArtifact : ToolItem
    {
        [Header("Reach")]
        [Tooltip("How far the hose reaches, in metres. Short on purpose: inflating somebody is " +
                 "something you do standing next to them, which is what makes it answerable.")]
        [SerializeField, Min(0.5f)] private float range = 5f;

        [Tooltip("What the nozzle can put pressure into. Triggers are always ignored — a detection " +
                 "volume is not a body.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Pump")]
        [Tooltip("The scalar this nozzle pumps toward, -1..+1. +1 is the inflator: bigger and " +
                 "lighter. A ballast item is this same component at a negative value — smaller and " +
                 "heavier — rather than a second script, which is why every number here is written " +
                 "as a share of this range instead of as a size.")]
        [SerializeField, Range(-1f, 1f)] private float pumpsToward = 1f;

        [Tooltip("Seconds of held pumping to take a body from its own size to the far end of the " +
                 "range above. The deflation is the status's own clock and is deliberately not " +
                 "here: it is twice this, so a thing comes back down at half the speed it went up.")]
        [SerializeField, Min(0.05f)] private float secondsToFull = 3f;

        [Header("Pop")]
        [Tooltip("How far along the range counts as the top of the gauge, 0..1. Just short of the " +
                 "end, because the pressure is a MoveTowards and a threshold at exactly 1 would " +
                 "depend on where a frame boundary happened to fall.")]
        [SerializeField, Range(0.5f, 1f)] private float popAtShare = 0.98f;

        [Tooltip("Seconds a body must sit at the top of the gauge, still being pumped, before it " +
                 "bursts. The whole punishment for over-pumping is that the pumping was wasted — " +
                 "the burst does no damage and puts the body back at its authored size.")]
        [SerializeField, Min(0f)] private float popHoldSeconds = 1f;

        [Header("Tank")]
        [Tooltip("The nozzle's own reservoir — the shared SupplyReservoir, tuned on this prefab. " +
                 "Found on this prefab when unset.")]
        [SerializeField] private SupplyReservoir tank;

        [Header("Stream")]
        [Tooltip("Seconds of silence after which the pump shuts itself off. The safety net for a " +
                 "release that never arrived — a dropped packet, or a player who disconnected " +
                 "mid-pump. Must stay comfortably above UseChannel's 0.2 s keepalive, or an " +
                 "ordinary steady hold would cut itself off between two perfectly normal ticks.")]
        [SerializeField, Min(0.25f)] private float holdTimeout = 0.5f;

        [Header("Presentation")]
        [Tooltip("The plunger, hose, dial, jet and hiss. Found under this object when left empty.")]
        [SerializeField] private InflatorNozzlePump pump;

        /// <summary>
        /// Bit 1 of <c>NetArg.B</c>: the owner says its target has been at the top of the gauge for
        /// <see cref="popHoldSeconds"/>. Bit 0 is EquipmentController's active flag and A is the
        /// slot code — see the file header.
        /// </summary>
        private const int PopBit = 1 << 1;

        /// <summary>
        /// The trace buffer. An instance field rather than a static one, because two of these are
        /// one hotbar scroll apart and a shared buffer would be a surprise waiting for the day
        /// somebody pumps from a turret with a second nozzle in the hold.
        /// </summary>
        private readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>Is the trigger down? Set from the hold stream, on every machine.</summary>
        private bool pumping;

        /// <summary>When this machine last heard a hold tick. See <see cref="holdTimeout"/>.</summary>
        private float lastHoldAt;

        /// <summary>
        /// Did the tank agree to this pull of the trigger?
        ///
        /// <para>
        /// Asked once, on the press, and never again — which is the whole point of
        /// <see cref="SupplyReservoir.CanStart"/>. Re-asking every tick would let a tank that had
        /// just run dry deliver one frame's worth of trickle, empty, and strobe for as long as the
        /// button stayed down. Every machine asks its own reservoir on the same press and they all
        /// equipped at the same replicated fill, so they all reach the same verdict.
        /// </para>
        /// </summary>
        private bool armed;

        /// <summary>
        /// When the server last raised the pressure. The pump runs on seconds rather than on ticks,
        /// so a stream that hitches or a frame rate that drops does not change how long a body takes
        /// to fill.
        /// </summary>
        private float lastPumpAt;

        /// <summary>Owner-side: the body its own trace is on, so a sweep away resets the pop clock.</summary>
        private StatusReceiver topTarget;

        /// <summary>Owner-side: when <see cref="topTarget"/> reached the top of the gauge, or −1.</summary>
        private float topSince = -1f;

        /// <summary>
        /// Owner-side: the burst has been asked for and must not be asked for again until the
        /// reading has come back down. Without it the second past full sends a burst per tick, and
        /// every machine draws one puff each.
        /// </summary>
        private bool popSent;

        /// <summary>Another body's size and weight is contested state, so one machine decides it.</summary>
        public override UseAuthority Authority => UseAuthority.Server;

        /// <summary>Pumping is something you do for a while, so the nozzle rides the hold stream.</summary>
        public override bool IsContinuous => true;

        /// <summary>
        /// Nothing self-timed: the pressure stops going in when the finger comes up.
        ///
        /// A dry tank deliberately does NOT end the hold. The player keeps holding, nothing swells,
        /// and the bar on the barrel is what tells them why — which is the whole reason the reading
        /// is on the object in their hands rather than in a HUD (GDC-L1-SYS-0006).
        /// </summary>
        public override bool WantsHold => false;

        // ── Owner side: the aim, and the one clock that is not a reading ───────

        /// <summary>
        /// Owner-side, on the press. The aim is sent even though hold ticks carry the same thing,
        /// because the first tick is a frame away and the server needs somewhere to point.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg) => WriteAim(ref arg);

        /// <summary>Owner-side, once per tick: where the nozzle points, and whether it is time.</summary>
        public override void OnRequestHold(ref NetArg arg, bool active)
        {
            if (!active)
            {
                ForgetPopClock();
                return;
            }

            WriteAim(ref arg);

            if (RipeForPop(arg)) arg.B |= PopBit;
        }

        /// <summary>
        /// The aim RAY, from the one machine that has a live camera.
        ///
        /// The ray rather than the point it lands on, so every machine traces its own — and
        /// <c>AimProvider</c> rather than the nozzle's own forward, because mounted, the eye is
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
        /// Owner-side: has the thing under the nozzle been at the top of the gauge long enough?
        ///
        /// <para>
        /// Timed here rather than on the server because it is the one part of the pop that is a
        /// CLOCK rather than a reading, and a clock has to belong to one machine or the second past
        /// full is a different second on each of them. The reading it is timing is the replicated
        /// one, so the owner is watching the same number the server is; the server still checks that
        /// number for itself before it bursts anything.
        /// </para>
        /// </summary>
        private bool RipeForPop(NetArg arg)
        {
            TraceTarget(arg, out _, out StatusReceiver body);

            // A sweep onto a different body starts a new second, not the tail of the last one.
            if (body != topTarget)
            {
                topTarget = body;
                topSince = -1f;
                popSent = false;
            }

            if (body == null) return false;

            float share = InflationScalar.Progress(InflationScalar.Presented(body), pumpsToward);
            if (share < popAtShare)
            {
                topSince = -1f;
                popSent = false;
                return false;
            }

            if (popSent) return false;

            if (topSince < 0f)
            {
                topSince = Time.time;
                return false;
            }

            if (Time.time - topSince < popHoldSeconds) return false;

            popSent = true;
            return true;
        }

        private void ForgetPopClock()
        {
            topTarget = null;
            topSince = -1f;
            popSent = false;
        }

        // ── The press ──────────────────────────────────────────────────────────

        /// <summary>
        /// Authority-side. A press by itself puts no measurable pressure anywhere — a single stroke
        /// of an air pump does not — so all it does is open the valve and start the clock the first
        /// hold tick will measure against.
        /// </summary>
        protected override void Use() => Prime();

        /// <summary>
        /// Every machine's valve, including the owner's, immediately: the plunger moves and the
        /// hiss starts on the frame of the press rather than after a round trip
        /// (<c>GDC-L1-FEEL-0002</c>).
        /// </summary>
        protected override void Present() => Prime();

        // ── The hold ───────────────────────────────────────────────────────────

        /// <summary>
        /// Authority-side, once per tick: hold the valve open, then put this tick's seconds of
        /// pressure into whatever the ray is on.
        ///
        /// The target is traced from THIS tick's own arg rather than remembered, because the
        /// authority is the only machine that raises anything — a dedicated server never receives
        /// <see cref="PresentHold"/> at all, being no part of the "others" its own broadcast goes to.
        /// </summary>
        protected override void Hold(NetArg arg, bool active)
        {
            ApplyHold(active);

            if (active) PutPressureIn(arg);
        }

        /// <summary>Every machine: the pump, the dial and the burst. See <see cref="ApplyHold"/>.</summary>
        protected override void PresentHold(NetArg arg, bool active)
        {
            ApplyHold(active);

            if (!active || pump == null) return;

            bool found = TraceTarget(arg, out RaycastHit hit, out StatusReceiver body);

            // The needle reads the TARGET's pressure, not the tank's — the one gauge in the set
            // that is about something other than this item. A body this nozzle has made no
            // progress on reads zero, which is also what open sky reads.
            pump.SetPressure(InflationScalar.Progress(InflationScalar.Presented(body), pumpsToward));

            if ((arg.B & PopBit) == 0) return;

            // Where the burst is drawn is each machine's own trace rather than a point in the
            // message, because P is already carrying the ray every machine needs. A machine whose
            // trace missed draws it at the end of the reach instead of dropping it.
            pump.Burst(found ? hit.point : AimRay(arg).GetPoint(range));
        }

        /// <summary>
        /// Shared by both halves, and idempotent, because on a host both halves run for the same
        /// tick.
        ///
        /// <para>
        /// The final tick arrives with <paramref name="active"/> false, and shutting the valve on it
        /// must work on a machine that never saw the ticks before it — which is why it is a plain
        /// assignment rather than anything that reads the current state first.
        /// </para>
        /// </summary>
        private void ApplyHold(bool active)
        {
            if (active) OpenValve();
            else ShutValve();
        }

        /// <summary>
        /// Open the valve for a NEW draw: ask the tank once, and start the server's pump clock so
        /// the first hold tick measures a real interval rather than zero.
        /// </summary>
        private void Prime()
        {
            armed = tank == null || tank.CanStart;
            lastPumpAt = Time.time;

            ForgetPopClock();
            OpenValve();
        }

        private void OpenValve()
        {
            pumping = true;
            lastHoldAt = Time.time;
        }

        private void ShutValve()
        {
            pumping = false;

            // Pushed straight through rather than left to the next Update, because the two paths
            // that shut a valve for good — an unequip and a disable — both run on a frame this
            // object may not see the end of.
            if (pump == null) return;

            pump.SetPumping(false);
            pump.SetPressure(0f);
        }

        // ── The pressure ───────────────────────────────────────────────────────

        /// <summary>
        /// Authority-side. Put one tick's worth of pressure into whatever the ray is on, or burst it.
        ///
        /// <para>
        /// The new scalar is derived from what the body is PRESENTING rather than integrated on this
        /// item, so two players pumping the same crate add up instead of fighting, and a body left
        /// half deflated is picked up where it actually is rather than snapped back to where it once
        /// was. The item holds no per-target number at all, which is also why an unequip in the
        /// middle of a pump loses nothing worth keeping.
        /// </para>
        /// </summary>
        private void PutPressureIn(NetArg arg)
        {
            // Capped at the timeout, which is already this stream's definition of "stale": a tick
            // that arrives after a hitch, or after a press whose message went missing, is worth one
            // gap's pumping and not the whole minute the item spent in a pocket.
            float elapsed = Mathf.Min(Time.time - lastPumpAt, holdTimeout);
            lastPumpAt = Time.time;

            if (!armed || elapsed <= 0f) return;
            if (tank != null && tank.Charge <= 0f) return;

            TraceTarget(arg, out _, out StatusReceiver body);
            if (body == null || !CanTakePressure(body)) return;

            float presented = InflationScalar.Presented(body);

            // The owner's word on WHEN, checked against this machine's own reading of WHAT before
            // anything happens to the body (GDC-L1-MP-0004). A client that could burst on its own
            // say-so could cancel anybody's inflation from five metres away.
            if ((arg.B & PopBit) != 0 &&
                InflationScalar.Progress(presented, pumpsToward) >= popAtShare)
            {
                body.Clear(StatusKind.Inflated);
                return;
            }

            // Zero seconds is the condition's own duration, refreshed every tick — which is what
            // holds a body at size while it is being pumped and starts it easing back down the
            // moment it is not. The pump does not get to decide how long a deflation takes.
            body.Apply(StatusKind.Inflated,
                       magnitude: InflationScalar.Pumped(presented, pumpsToward, secondsToFull,
                                                         elapsed),
                       source: owner != null ? owner.transform : null);
        }

        /// <summary>
        /// May this body be pumped at all?
        ///
        /// <para>
        /// Only a body PhysX can push. Inflating something grows its colliders, and a body that
        /// grows into a wall is depenetrated back out of it — which is the whole comedy of the item,
        /// and is free for anything dynamic. A KINEMATIC body cannot be depenetrated and cannot be
        /// lifted by the buoyancy either, so pumping one would end with a creature welded halfway
        /// into a cliff and no way out of it. That case is not hypothetical: mounting makes a
        /// rider's body kinematic, a ragdoll pins one to hold a body down, and a parked vehicle is
        /// one all the time. A body with no Rigidbody at all is refused for the same reason — there
        /// is nothing to resolve the overlap it would make.
        /// </para>
        /// <para>
        /// Refusing a mounted rider is also the anti-griefing half of this rule
        /// (<c>GDC-L1-MP-0002</c>): a player strapped into a seat cannot be inflated out of it.
        /// </para>
        /// </summary>
        private static bool CanTakePressure(StatusReceiver body)
        {
            // From the parent, like everything else that resolves a body off the collider an aim
            // happened to hit: the Rigidbody sits on the root and the receiver may not.
            Rigidbody weighted = body.GetComponentInParent<Rigidbody>();
            return weighted != null && !weighted.isKinematic;
        }

        // ── The trace ──────────────────────────────────────────────────────────

        /// <summary>The ray the owner reported, rebuilt on whichever machine is asking.</summary>
        private static Ray AimRay(NetArg arg) => new(arg.P, arg.R * Vector3.forward);

        /// <summary>
        /// The nearest thing the nozzle is on, skipping the holder's own body and the machine they
        /// are strapped into, plus the status receiver it carries if it has one.
        ///
        /// <para>
        /// The ray starts at the holder's eye, inside the holder, so an unfiltered trace inflates
        /// the person pumping. <c>AimProvider.NearestOutside</c> is the shared rule for that and is
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
        /// A receiver is never created here. One added by the server alone would inflate for the
        /// server and for nobody else, because the status message is addressed to the body's own
        /// relay and a body nothing has subscribed on drops it without a word. A thing that can be
        /// inflated says so on its own prefab.
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

        // ── The tank ───────────────────────────────────────────────────────────

        /// <summary>
        /// Drive the tank and the pump. Runs on every machine off its own clock, which is what keeps
        /// the rates per SECOND rather than per tick — a fifteen-hertz stream would otherwise make
        /// an empty tank a function of the frame rate.
        ///
        /// <para>
        /// Safe to declare: nothing in this hierarchy has an <c>Update</c> for this one to hide. A
        /// held trigger on an empty tank neither drains nor refills — see
        /// <see cref="SupplyReservoir.Tick"/>, which is where that rule lives for every tank in the
        /// game.
        /// </para>
        /// </summary>
        private void Update()
        {
            // A release is one message, and one message is exactly the kind of thing that goes
            // missing — along with the player who was holding the button.
            if (pumping && Time.time - lastHoldAt > holdTimeout) ShutValve();

            bool drawing = pumping && armed;
            bool delivering = tank == null ? drawing : tank.Tick(Time.deltaTime, drawing);

            if (pump != null) pump.SetPumping(delivering);
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        /// <summary>
        /// Find the presentation rig and the tank once.
        ///
        /// In <c>OnEnable</c> rather than <c>Awake</c> because a nozzle is enabled and disabled as it
        /// moves between the hand, the pack and the sand, and both lookups are cheap to confirm.
        /// </summary>
        private void OnEnable()
        {
            if (pump == null) pump = GetComponentInChildren<InflatorNozzlePump>(true);
            if (tank == null) tank = SupplyReservoir.On(gameObject);
        }

        private void OnDisable() => ShutValve();

        /// <summary>
        /// Shut the valve as the nozzle leaves the hand — including when what it leaves the hand as
        /// is an object lying in the sand, which must not go on hissing there.
        ///
        /// <para>
        /// Nothing here touches the tank, and that is deliberate rather than incidental: the slot's
        /// <c>ItemState</c> is captured BEFORE this runs, so a level written here would be the one
        /// level the save never sees.
        /// </para>
        /// </summary>
        public override void OnUnequipped(GameObject holder)
        {
            base.OnUnequipped(holder);
            ShutValve();
        }

        // No CaptureItemState / RestoreItemState override. The only thing this instance BECOMES is
        // its tank level, and UsableItem already writes the sibling reservoir's fill into the slot's
        // bag under SupplyCharge's own key — which is what makes it survive a save, a hotbar scroll,
        // a stow on the pack and a drop. Nothing else here is state: whether the trigger is down is
        // a button, the pop clock is worth a second, and the pressure itself lives on the BODY,
        // whose condition is deliberately not saved at all.
    }
}
