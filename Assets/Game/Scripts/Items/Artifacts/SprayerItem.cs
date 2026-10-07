using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// A held sprayer: hold Use and a plume comes out of a tank until the trigger comes up or the
    /// tank runs dry. Lifted out of the cryo sprayer when the fire extinguisher needed exactly the
    /// same valve, so the two cannot drift apart.
    ///
    /// <para>
    /// This owns the plumbing every sprayer shares — the press and the hold stream, the valve and
    /// its timeout, the tank (a <see cref="SupplyReservoir"/>), the sweep schedule and the
    /// presentation rig (<see cref="CryoSprayerNozzle"/>). What a sprayer DOES to what it covers is
    /// its own <see cref="Land"/>, run on every machine on the sweep schedule; anything that changes
    /// the world inside it gates on <see cref="Decides"/>.
    /// </para>
    /// <para>
    /// The aim travels as the ray (origin in <c>P</c>, rotation in <c>R</c>), never as a hit point,
    /// so every machine traces the same plume.
    /// </para>
    /// </summary>
    public abstract class SprayerItem : ToolItem
    {

        [Tooltip("How often the plume is resolved, in sweeps per second. Fifteen matches the " +
                 "hold stream, which is the rate every other continuous item in this game already " +
                 "runs at — and the rate the landing burst is moved at, so a slower one makes the " +
                 "frost at the plume's end jump between points.")]
        [SerializeField, Min(1f)] private float sweepsPerSecond = 15f;

        [Header("Tank")]
        [Tooltip("The sprayer's own reservoir — the shared SupplyReservoir, tuned on this prefab " +
                 "to SupplyKind.Reagent, 0.15/s while the plume is out and 0.06/s back while the " +
                 "trigger is up. Found on this prefab when unset.")]
        [SerializeField] private SupplyReservoir tank;

        [Header("Stream")]
        [Tooltip("Seconds of silence after which the sprayer shuts itself off. The safety net for " +
                 "a release that never arrived — a dropped packet, or a player who disconnected " +
                 "mid-spray. Must comfortably exceed UseChannel's keepalive interval.")]
        [SerializeField] private float holdTimeout = 0.5f;

        [Header("Presentation")]
        [Tooltip("The plume, the landing bursts and the hiss. Found under this object when empty.")]
        [SerializeField] private CryoSprayerNozzle nozzle;

        /// <summary>Is the trigger down? Set from the hold stream, on every machine.</summary>
        private bool spraying;

        /// <summary>When this machine last heard a hold tick. See <see cref="holdTimeout"/>.</summary>
        private float lastHoldTime;

        /// <summary>Seconds banked toward the next sweep.</summary>
        private float sweepTimer;

        /// <summary>The aim ray as last reported. Every machine works from the same one.</summary>
        private Vector3 rayOrigin;
        private Vector3 rayDirection = Vector3.forward;

        /// <summary>Where the plume starts and which way it points, as last reported.</summary>
        protected Vector3 RayOrigin => rayOrigin;
        protected Vector3 RayDirection => rayDirection;

        /// <summary>The plume, the landing bursts and the hiss, if this sprayer has them.</summary>
        protected CryoSprayerNozzle Nozzle => nozzle;

        /// <summary>A freeze is contested, so exactly one machine decides it.</summary>
        public override UseAuthority Authority => UseAuthority.Server;

        /// <summary>The trigger is a spray, so the sprayer rides the hold stream. See UsableItem.</summary>
        public override bool IsContinuous => true;

        /// <summary>
        /// Nothing self-timed: the plume stops when the finger comes up.
        ///
        /// A dry tank deliberately does NOT end the hold. The player keeps holding, the plume
        /// stops, and the gauge on the bottle is what tells them why — which is the whole reason
        /// the reading is on the object in their hands rather than in a HUD (GDC-L1-SYS-0006).
        /// </summary>
        public override bool WantsHold => false;

        // ── Owner side: the aim ────────────────────────────────────────────────

        /// <summary>
        /// Owner-side, on the press. Sent even though hold ticks carry the same thing, because the
        /// first tick is a frame away and a tap has to put its vapour somewhere.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg) => WriteAim(ref arg);

        /// <summary>Owner-side, once per tick: where the sprayer is pointed now.</summary>
        public override void OnRequestHold(ref NetArg arg, bool active)
        {
            if (active) WriteAim(ref arg);
        }

        /// <summary>
        /// The aim RAY, from the one machine that has a live camera.
        ///
        /// The ray rather than the point it lands on, so every machine traces its own plume out of
        /// it — and <c>AimProvider</c> rather than the muzzle's forward, because mounted, the eye
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

        /// <summary>Authority-side. The plume is resolved in <see cref="Update"/>; this opens it.</summary>
        protected override void Use() => OpenValve(UseArg);

        /// <summary>Every machine's valve, including the owner's, immediately.</summary>
        protected override void Present() => OpenValve(UseArg);

        /// <summary>
        /// An empty tank refuses the press outright rather than opening a valve that shuts again on
        /// the next frame. Running out is the item's only real cost, so it has to read as an
        /// interruption rather than as a stutter — see <see cref="SupplyReservoir.CanStart"/>.
        /// </summary>
        protected override bool CanUse() => base.CanUse() && (tank == null || tank.CanStart);

        /// <summary>
        /// A refilling item is never spent, so this must stay silent: the default raises
        /// <c>OnItemDepleted</c>, which <c>EquipmentController</c> answers by taking the item out
        /// of the inventory altogether. Unreachable while the prefab leaves <c>maxUses</c>
        /// unlimited, and present so that giving the sprayer a charge limit does not quietly
        /// delete it.
        /// </summary>
        protected override void OnMaxUsesReached() { }

        // ── The hold ───────────────────────────────────────────────────────────

        /// <summary>Authority-side. Only records the aim; the plume is resolved in <see cref="Update"/>.</summary>
        protected override void Hold(NetArg arg, bool active) => ApplyHold(arg, active);

        /// <summary>Every machine: the same, so the plume a peer draws follows the same ray.</summary>
        protected override void PresentHold(NetArg arg, bool active) => ApplyHold(arg, active);

        /// <summary>
        /// Shared by both halves, and idempotent, because on a host both halves run for the same
        /// tick.
        ///
        /// <para>
        /// A dedicated server never receives <see cref="PresentHold"/> at all — it is no part of
        /// the "others" its own broadcast goes to — so the aim has to be recorded on the authority
        /// path too, or the one machine that decides what is freezing is the one machine that does
        /// not know where the player is pointing.
        /// </para>
        /// <para>
        /// The final tick arrives with <paramref name="active"/> false and shuts the valve
        /// unconditionally, including on a machine that never saw the ticks before it. A release is
        /// one message; a disconnect is none at all, which is what <see cref="holdTimeout"/> is for.
        /// </para>
        /// </summary>
        private void ApplyHold(NetArg arg, bool active)
        {
            if (!active)
            {
                ShutValve();
                return;
            }

            ReadAim(arg);
            lastHoldTime = Time.time;

            // A machine that missed the press — a late joiner, or a peer whose UseItem was dropped
            // — still opens on the first tick it does hear rather than streaming a dead gun.
            if (!spraying) OpenValve(arg);
        }

        private void OpenValve(NetArg arg)
        {
            ReadAim(arg);

            if (spraying) return;

            // Asked only of a NEW draw. A tank that ran dry mid-burst refills past its restart
            // threshold and the next tick opens the valve again, which is the hysteresis working
            // rather than being circumvented.
            if (tank != null && !tank.CanStart) return;

            spraying = true;
            lastHoldTime = Time.time;
            sweepTimer = 0f;
        }

        private void ShutValve()
        {
            spraying = false;
            sweepTimer = 0f;

            // Pushed straight through rather than left to the next Update, because the two paths
            // that shut a valve for good — an unequip and a disable — both run on a frame this
            // object may not see the end of.
            if (nozzle == null) return;

            nozzle.SetSpraying(false);
            nozzle.SetLanding(false, Vector3.zero, false);
        }

        private void ReadAim(NetArg arg)
        {
            if (!arg.HasOrientation) return;

            rayOrigin = arg.P;
            rayDirection = arg.R * Vector3.forward;
        }

        // ── Per frame ──────────────────────────────────────────────────────────

        /// <summary>
        /// Drive the tank, the sweep and the gun. Runs on every machine off its own clock, which is
        /// what keeps the rates per SECOND rather than per tick — a fifteen-hertz stream would
        /// otherwise make an empty tank a function of the frame rate.
        ///
        /// <para>
        /// The sweep is driven from here rather than straight out of <see cref="Hold"/> because the
        /// hold stream throttles itself: an aim that has not moved goes out only as a keepalive
        /// every fifth of a second, so a player holding still on a creature would freeze it in
        /// jerks. Safe to declare — nothing in this hierarchy has an <c>Update</c> for this one to
        /// hide, and the reservoir keeps its own.
        /// </para>
        /// </summary>
        protected virtual void Update()
        {
            float deltaTime = Time.deltaTime;

            // A release is one message, and one message is exactly the kind of thing that goes
            // missing — along with the player who was holding the button.
            if (spraying && Time.time - lastHoldTime > holdTimeout) ShutValve();

            // Every frame whether or not the trigger is down, because the refill is the half that
            // runs when nothing is happening. The trigger held on an empty tank neither drains nor
            // refills — see SupplyReservoir.Tick, where that rule lives for every tank in the game.
            bool emitting = tank == null ? spraying : tank.Tick(deltaTime, spraying);

            if (emitting) Sweep(deltaTime);
            else sweepTimer = 0f;

            if (nozzle == null) return;

            nozzle.SetSpraying(emitting);
            if (!emitting) nozzle.SetLanding(false, Vector3.zero, false);
        }

        /// <summary>
        /// Resolve the plume on the schedule, and no faster.
        ///
        /// <para>
        /// Not a catch-up loop: what a sweep does is advance a fraction by the time that has
        /// actually elapsed, so a frame the schedule skipped is paid for by the next sweep rather
        /// than owed. That is what makes a second and a half of spray a second and a half on a
        /// machine that cannot hold fifteen sweeps a second as well as on one that can.
        /// </para>
        /// </summary>
        private void Sweep(float deltaTime)
        {
            sweepTimer += deltaTime;

            float step = 1f / sweepsPerSecond;
            if (sweepTimer < step) return;

            float elapsed = sweepTimer;
            sweepTimer = 0f;

            Land(elapsed);
        }

        /// <summary>
        /// Is this the machine that decides what freezes? Offline, or the server.
        ///
        /// Asked of the OWNER rather than of this item. An equipped artifact is instantiated into a
        /// hand and never spawned, so its own NetworkObject is dormant and
        /// <see cref="Network.Simulates"/> would answer "yes, you simulate it" on every machine in
        /// the session — and every player watching would announce the same freeze.
        /// </summary>
        protected bool Decides => owner != null && Network.Simulates(owner.transform);

        // ── Lifecycle ──────────────────────────────────────────────────────────

        /// <summary>
        /// Find the tank and the presentation rig once, before any frame runs. In Awake rather than
        /// on equip because <see cref="Update"/> ticks the reservoir on a sprayer lying in the sand
        /// as readily as on one in a hand, and a bottle resolved only at equip would leave that one
        /// never refilling.
        /// </summary>
        protected virtual void Awake()
        {
            if (tank == null) tank = SupplyReservoir.On(gameObject);
            if (nozzle == null) nozzle = GetComponentInChildren<CryoSprayerNozzle>(true);
        }

        protected virtual void OnDisable() => ShutValve();

        /// <summary>
        /// Shut the valve as the sprayer leaves the hand — including when what it leaves the hand
        /// as is an object lying in the sand, which must not go on hissing there.
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

        /// <summary>
        /// Resolve the plume: what it covers, and what that does. Every machine, on the sweep
        /// schedule; <paramref name="elapsed"/> is the seconds since the last sweep.
        /// </summary>
        protected abstract void Land(float elapsed);

        /// <summary>
        /// A timeout inside the keepalive interval would cut the plume between two perfectly
        /// ordinary ticks, so it is floored well clear of it rather than left to whoever edits it.
        /// </summary>
        protected virtual void OnValidate()
        {
            holdTimeout = Mathf.Max(UseChannel.HoldKeepAliveInterval * 2f, holdTimeout);
        }
    }
}
