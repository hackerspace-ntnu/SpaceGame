// The slick can.
//
// Hold Use and a fan of frictionless film comes out of the nozzle. Ground under it gets a Slick
// SurfaceCoat patch; a body in it gets the Slick status. Neither of those is this item's work: the
// coat field owns the patch, its merge rule, its expiry, its cap and its replication, and the
// status receiver owns the flag on a body and how long it lasts. What the can owns is WHERE the
// film lands and what it costs out of its own tank — and nothing else. Every number the film itself
// is made of (twenty seconds, a 1.2 m dab, a twentieth of normal grip) is authored on SlickCoat and
// SlickStatus, and the can deliberately passes zero for radius and duration so that it cannot
// disagree with them.
//
// WHAT IT DOES NOT DO. It does not scale anybody's grip, it does not push a body, and it does not
// make a rope slide off one. Movers ask GroundGrip themselves, on the machine that owns them, which
// is what keeps a slicked player's own movement owner-authoritative — a server writing their
// velocity would be overwritten within a tick, silently.
//
// WHAT DOES NOT GO ON THE WIRE. Nothing of the can's own. arg.A is the hotbar slot on the press and
// on every hold tick and belongs to the server's stale-slot guard; arg.B's low bit belongs to
// EquipmentController's active flag. The can needs neither, so it writes only the aim ray (origin
// in P, rotation in R) and lets the coat field send its own patches.
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay.Status;
using SpaceGame.Gameplay.Surface;

namespace SpaceGame.Items
{
    /// <summary>
    /// A hand-sized aerosol that makes a surface, or a body, impossible to get purchase on.
    ///
    /// <para>
    /// <b>Why it HOLDS a <see cref="SupplyReservoir"/>.</b> Its tank is a
    /// <see cref="SupplyCharge"/> fraction, which is not a style choice: a fraction on the ITEM
    /// INSTANCE is the only form that already survives an equip, a stow, a drag across the pack, a
    /// drop, a pickup, a save and — crucially — the wire. That last one is what rules out holding
    /// the number in a private float and writing it into the state bag by hand. The bag is the
    /// server's own, and a client only receives a slot's charge byte for an item that
    /// <see cref="SupplyCharge.Carries"/>, which asks whether the item's prefab has a reservoir on
    /// it. Without one, a client's can would equip at the authored starting charge and stay there
    /// while the server drained it — the documented failure that put the byte on the wire.
    /// </para>
    /// <para>
    /// The can was briefly a <c>DockableSupply</c> subclass, because the reservoir and a dockable
    /// item's verb-less <c>UsableItem</c> were one class and two <c>UsableItem</c>s cannot share a
    /// prefab root — <c>EquipmentController</c> resolves the held item with a single
    /// <c>GetComponent&lt;UsableItem&gt;</c>. Inheriting made a sprayer claim a dock and a
    /// <c>SupplyKind</c> it does not have. The reservoir is a plain component now, so the can
    /// simply has one (<c>GDC-L1-ARCH-0002</c>).
    /// </para>
    /// <para>
    /// <b>Authority is Server</b>, because a film changes shared world surfaces and other people's
    /// bodies — both contested, neither the holder's own (GDC-L1-MP-0004). The tank is nevertheless
    /// drained by every machine off its own clock, from the value they all equipped with: the
    /// server's is the one that is captured and saved, the peers' only drive the gauge on the model,
    /// and the coats they draw came from the server rather than from their own arithmetic, so a
    /// hair of drift near empty costs a sputter and nothing else.
    /// </para>
    /// </summary>
    public sealed class SlickCanArtifact : ToolItem
    {
        [Header("Spray")]
        [Tooltip("How far the film reaches, in metres. Short on purpose — this is an aerosol, not " +
                 "a hose, and the range is what stops it denying footing across a room.")]
        [SerializeField] private float range = 4f;

        [Tooltip("Half-angle of the fan, in degrees. Wide, because this paints ground rather than " +
                 "hitting a target. Keep SlickCanNozzle's open cone in step with it.")]
        [SerializeField, Range(0f, 60f)] private float fanHalfAngle = 30f;

        [Tooltip("How many dabs the fan is made of. One goes out per hold tick and the pattern is " +
                 "walked a step at a time, so this is the width of the band a held spray paints, " +
                 "not a cost per tick. See SlickCanFan.")]
        [SerializeField, Min(1)] private int fanSamples = 3;

        [Tooltip("Steepest surface that still takes a patch, in degrees off level. A coat is a " +
                 "horizontal disc laid at the point it was sprayed, so film put on a wall would " +
                 "hang in the air as a rink nobody is standing on.")]
        [SerializeField, Range(0f, 89f)] private float maxGroundAngle = 50f;

        [Tooltip("What the spray can land on. Triggers are always ignored.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Tank")]
        [Tooltip("The can's own reservoir — the shared SupplyReservoir, tuned on this prefab to " +
                 "0.12/s while the film is coming out and 0.06/s back while the trigger is up. " +
                 "Half the drain rate, so the can is a resource to spend rather than a tap to " +
                 "leave running. Found on this prefab when unset.")]
        [SerializeField] private SupplyReservoir tank;

        [Header("Stream")]
        [Tooltip("Seconds of silence after which the can shuts itself off. The safety net for a " +
                 "release that never arrived — a dropped packet, or a player who disconnected " +
                 "mid-spray. Must comfortably exceed EquipmentController's 0.2 s keepalive.")]
        [SerializeField] private float holdTimeout = 0.5f;

        [Header("Presentation")]
        [Tooltip("The nozzle, jet and hiss. Found under this object when left empty.")]
        [SerializeField] private SlickCanNozzle nozzle;

        /// <summary>Is the trigger down? Set from the hold stream, on every machine.</summary>
        private bool spraying;

        /// <summary>When this machine last heard a hold tick. See <see cref="holdTimeout"/>.</summary>
        private float lastHoldTime;

        /// <summary>Which dab of the fan goes out next. See <see cref="SlickCanFan"/>.</summary>
        private int fanCursor;

        /// <summary>
        /// The trace buffer. An instance field rather than a static one: two cans are one hotbar
        /// scroll apart, and a buffer shared between them would be a surprise waiting for the day
        /// somebody sprays from a vehicle with a second can in a turret.
        /// </summary>
        private readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>The world change is contested and shared, so exactly one machine decides it.</summary>
        public override UseAuthority Authority => UseAuthority.Server;

        /// <summary>The trigger is a spray, so the can rides the hold stream. See UsableItem.</summary>
        public override bool IsContinuous => true;

        /// <summary>
        /// Nothing self-timed: the jet stops when the finger comes up.
        ///
        /// A dry tank deliberately does NOT end the hold. The player keeps holding, the jet stops,
        /// and the gauge up the side of the can is what tells them why — which is the whole reason
        /// the reading is on the object in their hands rather than in a HUD (GDC-L1-SYS-0006).
        /// </summary>
        public override bool WantsHold => false;

        // ── Owner side: the aim ────────────────────────────────────────────────

        /// <summary>
        /// Owner-side, on the press. Sent even though hold ticks carry the same thing, because the
        /// first tick is a frame away and a tap has to leave film behind.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg) => WriteAim(ref arg);

        /// <summary>Owner-side, once per tick: where the can is pointed now.</summary>
        public override void OnRequestHold(ref NetArg arg, bool active)
        {
            if (active) WriteAim(ref arg);
        }

        /// <summary>
        /// The aim RAY, from the one machine that has a live camera.
        ///
        /// The ray rather than the point it lands on, so every machine traces its own fan out of
        /// it — and <c>AimProvider</c> rather than the nozzle's forward, because mounted, the eye
        /// is pitched with the seat and the view the player is aiming down is the mount's.
        /// </summary>
        private void WriteAim(ref NetArg arg)
        {
            AimProvider aim = aimProvider;
            if (aim == null || aim.AimTransform == null) return;

            Ray ray = aim.GetAimRay();
            arg.P = ray.origin;
            arg.R = Quaternion.LookRotation(ray.direction);
        }

        // ── The press ──────────────────────────────────────────────────────────

        /// <summary>
        /// Authority-side. Opens the valve and lays the press's own dab, so that a tap the hold
        /// stream never got a chance to carry still paints something.
        /// </summary>
        protected override void Use()
        {
            OpenValve();
            LayFilm(UseArg);
        }

        /// <summary>Every machine's valve, including the owner's, immediately.</summary>
        protected override void Present() => OpenValve();

        // ── The hold ───────────────────────────────────────────────────────────

        /// <summary>
        /// Authority-side, once per tick: hold the valve open, then lay one dab of the fan.
        ///
        /// The film is laid from THIS tick's own arg rather than from a remembered ray, because
        /// the authority is the only machine that lays any — a dedicated server never receives
        /// <see cref="PresentHold"/> at all, being no part of the "others" its own broadcast goes
        /// to, so there is no second path that would need the same aim.
        /// </summary>
        protected override void Hold(NetArg arg, bool active)
        {
            ApplyHold(active);

            if (active) LayFilm(arg);
        }

        /// <summary>Every machine: the jet. See <see cref="ApplyHold"/>.</summary>
        protected override void PresentHold(NetArg arg, bool active) => ApplyHold(active);

        /// <summary>
        /// Shared by both halves, and idempotent, because on a host both halves run for the same
        /// tick.
        ///
        /// <para>
        /// The final tick arrives with <paramref name="active"/> false, and shutting the valve on
        /// it must work on a machine that never saw the ticks before it — which is why it is a
        /// plain assignment rather than anything that reads the current state first.
        /// </para>
        /// </summary>
        private void ApplyHold(bool active)
        {
            if (!active)
            {
                ShutValve();
                return;
            }

            OpenValve();
        }

        private void OpenValve()
        {
            spraying = true;
            lastHoldTime = Time.time;
        }

        private void ShutValve()
        {
            spraying = false;

            // Pushed straight through rather than left to the next Update, because the two paths
            // that shut a valve for good — an unequip and a disable — both run on a frame this
            // object may not see the end of.
            if (nozzle != null) nozzle.SetSpraying(false);
        }

        // ── The film ───────────────────────────────────────────────────────────

        /// <summary>
        /// Authority-side. Trace one dab of the fan and leave whatever it found covered in film.
        ///
        /// <para>
        /// A body wears the film and the sand behind it does not: the two are alternatives, not
        /// both, because a creature standing in the jet is what the jet hit. A body is one that
        /// already carries a <c>StatusReceiver</c> — this deliberately does not <c>Ensure</c> one,
        /// because a receiver added here would exist on the server alone, and a message addressed
        /// to a body nothing has subscribed on is dropped without a word on every other machine.
        /// A body that can be slicked says so on its own prefab.
        /// </para>
        /// </summary>
        private void LayFilm(NetArg arg)
        {
            if (!arg.HasOrientation || owner == null || (tank != null && tank.Charge <= 0f)) return;

            Vector3 forward = arg.R * Vector3.forward;
            Vector3 direction = SlickCanFan.Direction(forward, fanCursor, fanSamples, fanHalfAngle);

            fanCursor = (fanCursor + 1) % Mathf.Max(1, fanSamples);

            if (!Trace(arg.P, direction, out RaycastHit hit)) return;

            StatusReceiver body = StatusReceiver.Of(hit.collider.gameObject);
            if (body != null)
            {
                // Zero seconds is the status's own authored duration. A spray can does not get to
                // decide how long a film lasts, and the body's clock and the ground's are one
                // number by design.
                body.Apply(StatusKind.Slick, source: owner.transform);
                return;
            }

            // A coat is a horizontal disc at the height it was sprayed, so anything much steeper
            // than a ramp would leave film hanging in the air beside the wall it was aimed at.
            if (Vector3.Angle(hit.normal, Vector3.up) > maxGroundAngle) return;

            // Radius and duration left at zero: the kind's own. Spraying Slick onto ground that is
            // already slick GROWS and refreshes that patch rather than laying a second one, which
            // is what lets a held spray run at fifteen ticks a second without a throttle here.
            SurfaceCoats.Spray(SurfaceCoatKind.Slick, hit.point);
        }

        /// <summary>
        /// The nearest thing one dab can land on, skipping the holder's own body and the machine
        /// they are strapped into.
        ///
        /// <para>
        /// The ray starts at the holder's eye, inside the holder, so an unfiltered trace slicks the
        /// person spraying. <c>AimProvider.NearestOutside</c> is the shared rule for that and is
        /// static precisely so a trace like this one can borrow it; it also picks the nearest hit
        /// by hand, because <c>RaycastNonAlloc</c> neither sorts its buffer nor tells you when it
        /// truncated one.
        /// </para>
        /// <para>
        /// The carrier is the owner's transform ROOT. Mounting parents the rider under the mount,
        /// so the root IS the machine they are riding — and on their own feet the root is the owner
        /// themselves, which makes the second filter a repeat of the first rather than a case that
        /// has to be branched on.
        /// </para>
        /// </summary>
        private bool Trace(Vector3 origin, Vector3 direction, out RaycastHit hit)
        {
            int count = Physics.RaycastNonAlloc(new Ray(origin, direction), hits, range, hitMask,
                                                QueryTriggerInteraction.Ignore);

            return AimProvider.NearestOutside(hits, count, owner.transform, owner.transform.root,
                                              out hit);
        }

        // ── The tank ───────────────────────────────────────────────────────────

        /// <summary>
        /// Drive the tank and the nozzle. Runs on every machine off its own clock, which is what
        /// keeps the rates per SECOND rather than per tick — a fifteen-hertz stream would otherwise
        /// make an empty can a function of the frame rate.
        ///
        /// <para>
        /// Safe to declare: nothing in this hierarchy has an <c>Update</c> for this one to hide.
        /// The trigger held down on an empty can neither drains nor refills — see
        /// <see cref="SupplyReservoir.Tick"/>, which is where that rule lives for every tank in the
        /// game, along with the repaint the gauge is spared when the reading has not moved.
        /// </para>
        /// </summary>
        private void Update()
        {
            // A release is one message, and one message is exactly the kind of thing that goes
            // missing — along with the player who was holding the button.
            if (spraying && Time.time - lastHoldTime > holdTimeout) ShutValve();

            bool emitting = tank == null ? spraying : tank.Tick(Time.deltaTime, spraying);

            if (nozzle != null) nozzle.SetSpraying(emitting);
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        /// <summary>
        /// Find the presentation rig and the tank once.
        ///
        /// <para>
        /// In <c>OnEnable</c> rather than <c>Awake</c> because a can is enabled and disabled as it
        /// moves between the hand, the pack and the sand, and both lookups are cheap to confirm.
        /// The reservoir keeps its own <c>Awake</c> on its own component, so there is no longer a
        /// base message here for a subclass to hide.
        /// </para>
        /// </summary>
        private void OnEnable()
        {
            if (nozzle == null) nozzle = GetComponentInChildren<SlickCanNozzle>(true);
            if (tank == null) tank = SupplyReservoir.On(gameObject);
        }

        private void OnDisable() => ShutValve();

        /// <summary>
        /// Shut the valve as the can leaves the hand — including when what it leaves the hand as is
        /// an object lying in the sand, which must not go on hissing there.
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
        // a stow on the pack and a drop. Whether the trigger is down is not state: it is a button,
        // and a quicksave that loaded a can already spraying would hand the player a press they did
        // not make.
    }
}
