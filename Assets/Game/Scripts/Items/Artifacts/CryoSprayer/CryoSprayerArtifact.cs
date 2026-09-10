// The cryo sprayer.
//
// Hold Use and a plume of vapour comes out. Everything it touches loses its grip. What it lands on
// decides HOW: liquid and wet ground become a standable sheet of ice, any other ground level enough
// to hold a disc takes a film of frost, and a body wears that film until it has had enough cold to
// freeze solid.
//
// None of those is this item's work. StatusKind.Frozen owns the helplessness and the ten seconds it
// lasts; StatusKind.Slick owns the film on a body; SurfaceCoatKind.Ice and .Slick own the patches,
// their colliders, their merge rules, their cap, their replication and their save records;
// SupplyReservoir owns the tank. What the sprayer owns is WHERE the cold lands, how long it has to
// stay there, and what the gun looks like putting it there — and nothing else. Every number the
// freeze and the film are made of is authored on FrozenStatus, SlickStatus, IceCoat and SlickCoat,
// and this deliberately passes no duration and no radius so that it cannot disagree with them.
//
// WHY ONE GUN DOES BOTH. The slick can used to lay this film, over four metres, at a fifth of the
// grip reduction, and did nothing else — a strictly narrower version of a job the sprayer's plume
// already covered at eighteen. Two systems doing substantially the same job is the case
// GDC-L1-SYS-0005 says to merge rather than to differentiate, and the merged tool is not a strict
// gain: the frost is symmetric, so the ground the holder is denying is ground they cannot brake on
// either, and the film that slides a rope off a creature slides it off one they were trying to
// catch (GDC-L1-BAL-0004).
//
// WHAT DOES NOT GO ON THE WIRE. Nothing of the sprayer's own. arg.A is the hotbar slot on the press
// and on every hold tick and belongs to the server's stale-slot guard; arg.B's low bit belongs to
// EquipmentController's active flag. The sprayer needs neither: it writes only the aim ray (origin
// in P, rotation in R), and the status receiver and the coat field each send their own facts.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay.Status;
using SpaceGame.Gameplay.Surface;

namespace SpaceGame.Items
{
    /// <summary>
    /// A one-handed sprayer that freezes what it is pointed at.
    ///
    /// <para>
    /// <b>Authority is Server</b>, because a freeze is a condition on somebody else's body and a
    /// sheet of ice is a change to shared ground — both contested, neither the holder's own
    /// (GDC-L1-MP-0004). Only the server applies the status and only the server lays the coat.
    /// </para>
    /// <para>
    /// <b>The build-up is derived rather than replicated.</b> Freezing a body takes three quarters
    /// of a second of spray, and every machine has to see it coming or the moment a creature
    /// stops being a creature is one frame of substitution with no warning (GDC-L1-FEEL-0004). It
    /// needs no message: the aim ray already reaches the owner, the server and every peer on the
    /// ordinary hold stream, so each of them traces the same ray and reaches the same fraction
    /// within a frame of the others. That is the same argument <see cref="SupplyReservoir"/> makes
    /// for its tank and <c>LaserStaffArtifact</c> makes for its recharge. <see cref="FrozenBody"/>
    /// holds the fraction and draws it.
    /// </para>
    /// <para>
    /// <b>A telegraph the victim can act on is the anti-lock design</b> (GDC-L1-MP-0002). The rime
    /// creeps over them on their own machine for three quarters of a second before anything
    /// happens to them, breaking line of sight or leaving the plume's reach sheds it, and a body that
    /// is already frozen does not push its expiry out. The freeze itself deals no damage and cannot
    /// kill — see <see cref="StatusKind.Frozen"/> — so the whole of what it costs the victim is ten
    /// seconds they can see coming. The consequence of a hold is legible from the moment it starts,
    /// which is what makes it a fight rather than a click (GDC-L1-DESIGN-0006).
    /// </para>
    /// <para>
    /// <b>The plume freezes the cone it is drawn as, not only its centre line.</b> What the player
    /// sees leaving the barrel is a fifteen-degree spray, so vapour visibly washing over a creature
    /// and doing nothing to it reads as a broken gun however precisely the crosshair rule is
    /// documented (GDC-L1-FEEL-0003 — answer what the player meant, and the cone they can see is
    /// what they meant). Every body inside that cone chills, and it is a cone rather than a fan of
    /// sampled dabs so that the build-up cannot stall and restart for reasons nobody can see.
    /// </para>
    /// <para>
    /// <b>Aiming still pays, because the cold falls off across the cone.</b> A body on the
    /// crosshair freezes in the authored three quarters of a second and one at the rim takes
    /// <see cref="edgeChill"/> of that rate — see <see cref="ConeSweep.Falloff"/>. Without the
    /// falloff an eighteen-metre cone is nine metres across at its far end and the sprayer becomes
    /// a better flamethrower held at four times the range (GDC-L1-BAL-0004). The spread itself is
    /// authored on <see cref="CryoSprayerNozzle"/>, which draws it.
    /// </para>
    /// </summary>
    public sealed class CryoSprayerArtifact : ToolItem
    {
        [Header("Plume")]
        [Tooltip("How far the cold reaches, in metres. Three times the flamethrower's six, " +
                 "because the flame is a cone that catches a crowd at arm's length and this is a " +
                 "single line held on one target — the reach is what the narrowness buys. The " +
                 "visible plume is built to the same number: see CryoPlumeBuilder.\n\n" +
                 "This value is SERIALIZED on the prefab, which is what actually ships. Changing " +
                 "the default here alone changes nothing in the game.")]
        [SerializeField] private float range = 18f;

        [Tooltip("What the plume can land on. Triggers are always ignored.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Tooltip("Half the freezing cone's opening angle, in degrees. Must match the spread the " +
                 "plume is DRAWN at — CryoSprayerNozzle.openConeDegrees — or vapour washes over a " +
                 "creature and does nothing to it; OnValidate warns when the two drift apart.")]
        [SerializeField, Range(0f, 45f)] private float coneHalfAngle = 15f;

        [Tooltip("How much of the freezing rate a body standing at the very rim of the cone " +
                 "takes, as a fraction. One freezes the whole cone as fast as the crosshair, " +
                 "which at eighteen metres is a nine-metre-wide crowd freeze; the authored value " +
                 "keeps a rim target roughly three times slower than an aimed one.")]
        [SerializeField, Range(0.05f, 1f)] private float edgeChill = 0.35f;

        [Tooltip("What stops the cold reaching a body. Set to nothing to let the plume freeze " +
                 "through walls, which is almost never what is wanted at eighteen metres.")]
        [SerializeField] private LayerMask sightBlockers = ~0;

        [Tooltip("Steepest surface that still takes a coat, in degrees off level. A coat is a " +
                 "horizontal disc laid at the point it was sprayed, so frost put on a wall would " +
                 "hang in the air beside it as a rink nobody is standing on. The same gate for " +
                 "ice and for frost: what it is really asking is whether anything stands here.")]
        [SerializeField, Range(0f, 89f)] private float maxGroundAngle = 50f;

        [Tooltip("How often the plume is resolved, in sweeps per second. Costs nothing in " +
                 "bandwidth by itself, but the authority lays a coat per sweep and each of those " +
                 "is one message — fifteen matches the hold stream, which is the rate every other " +
                 "continuous item in this game already costs.")]
        [SerializeField, Min(1f)] private float sweepsPerSecond = 15f;

        [Header("Freeze")]
        [Tooltip("Seconds of continuous spray on one target before it freezes solid. Short " +
                 "enough that landing a freeze is a shot rather than a siege, long enough that " +
                 "the victim's rime is a warning they can still break line of sight against " +
                 "(GDC-L1-MP-0002); how long the freeze then LASTS is FrozenStatus's own number, " +
                 "not this one. Serialized on the prefab — see the note on range.")]
        [SerializeField, Min(0.05f)] private float freezeSeconds = 0.75f;

        [Tooltip("What the ice on a frozen body is made of. Handed to every body this gun chills " +
                 "— see FrostLook.")]
        [SerializeField] private FrostLook frost = new FrostLook();

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

        /// <summary>
        /// The trace buffer. An instance field rather than a static one: two sprayers are one
        /// hotbar scroll apart, and a buffer shared between them would be a surprise waiting for
        /// the day somebody sprays from a vehicle with a second one in a turret.
        /// </summary>
        private readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>
        /// The bodies the cone is on this sweep, one entry each. Reused between sweeps: this runs
        /// fifteen times a second for as long as the trigger is down.
        /// </summary>
        private readonly List<ConeBody> chilled = new List<ConeBody>();

        /// <summary>
        /// What the plume counts as a body, handed to <see cref="ConeSweep"/>. Cached because a
        /// method group becomes a fresh delegate at every call site it is written at, and this one
        /// is written at fifteen a second.
        /// </summary>
        private static readonly Func<GameObject, StatusReceiver> ResolveBody =
            StatusReceiver.EnsureOnBody;

        /// <summary>The freeze and the ice are both contested, so exactly one machine decides them.</summary>
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
        private void Update()
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
        /// Sweep the plume and leave whatever it found colder: every body in the cone, and the
        /// ground the centre line ends on.
        ///
        /// <para>
        /// The cone and the centre line answer two different questions and are not alternatives.
        /// Bodies come from the cone, because the vapour the player watched wash over a creature is
        /// what they aimed (GDC-L1-FEEL-0003); the COAT comes from the single trace, because a coat
        /// is a disc laid at one point and a cone has no one point. A creature standing in the way
        /// still shields the ground behind it — the trace stops on the body, and a body takes no
        /// coat.
        /// </para>
        /// <para>
        /// What counts as a body is <see cref="StatusReceiver.EnsureOnBody"/> — anything alive,
        /// anything loose, and anything somebody authored a receiver onto — which is the same rule
        /// the flame uses, and the reason a creature nobody remembered to tick a box on still
        /// freezes. Asked on EVERY machine and not only the authority: a receiver the server
        /// invented alone is a body that freezes for the server and nobody else, because the status
        /// arrives on that body's own relay and a relay with nothing subscribed drops it without a
        /// word.
        /// </para>
        /// </summary>
        private void Land(float elapsed)
        {
            if (owner == null) return;

            bool authority = Decides;

            ConeSweep.Bodies(rayOrigin, rayDirection, range, coneHalfAngle, hitMask, sightBlockers,
                             owner.transform.root, StatusReceiver.Of(owner), ResolveBody, chilled);

            foreach (ConeBody caught in chilled)
                Chill(caught.Body, elapsed,
                      ConeSweep.Falloff(caught.OffAxisDegrees, coneHalfAngle, edgeChill), authority);

            if (!Trace(out RaycastHit hit))
            {
                if (nozzle != null) nozzle.SetLanding(false, Vector3.zero, false);
                return;
            }

            // The landing effects follow the centre line, which is where the player is looking. A
            // body on it is always freezing — it is the least off-axis thing in the cone — so the
            // burst sticks without asking the sweep about it.
            if (StatusReceiver.EnsureOnBody(hit.collider.gameObject) != null)
            {
                if (nozzle != null) nozzle.SetLanding(true, hit.point, true);
                return;
            }

            SurfaceCoatKind? coat = CoatFor(hit);

            // Radius and duration left at zero: the kind's own, which for ice is a metre and a half
            // and forever, and for frost is 1.2 m and twenty seconds. Spraying a kind onto ground
            // that already carries it GROWS and refreshes that patch rather than laying a second
            // one, which is what lets a held spray run at fifteen sweeps a second without
            // carpeting a chunk.
            if (authority && coat.HasValue) SurfaceCoats.Spray(coat.Value, hit.point);

            if (nozzle != null) nozzle.SetLanding(true, hit.point, coat.HasValue);
        }

        /// <summary>
        /// Another <paramref name="elapsed"/> seconds of cold on one body, on every machine, and
        /// the film and the freeze on the one that decides.
        ///
        /// <para>
        /// <paramref name="rate"/> is how much of the freezing rate this body takes: 1 on the
        /// crosshair, falling to <see cref="edgeChill"/> at the rim of the plume. Only the BUILD-UP
        /// is scaled by it. The film is not: a body the vapour touched at all is slithering
        /// immediately, which is what makes the graze a warning rather than a surprise.
        /// </para>
        ///
        /// <para>
        /// Neither status carries a duration or a magnitude: twenty seconds of film and ten of
        /// freeze are what those conditions are worth, and a sprayer does not get to decide them.
        /// The source is the HOLDER, which is what <c>ProvocationModule</c> reads to work out who a
        /// creature is now afraid of.
        /// </para>
        /// <para>
        /// <b>The film lands on the first touch and the freeze only after the build-up.</b> That
        /// ordering is the telegraph: the moment a body is grazed it is visibly slithering, which
        /// is the warning it has three quarters of a second to act on before the pose locks
        /// (GDC-L1-MP-0002). It also outlives the freeze by ten seconds, so a thaw hands the victim
        /// their body back on ground they still cannot brake on rather than at a standstill.
        /// </para>
        /// </summary>
        private void Chill(StatusReceiver body, float elapsed, float rate, bool authority)
        {
            FrozenBody frozen = FrozenBody.Ensure(body, frost);
            if (frozen == null) return;

            frozen.Chill(elapsed, freezeSeconds, rate);

            if (!authority) return;

            // Reapplying refreshes the expiry rather than stacking, so a held plume simply keeps
            // the film topped up for as long as it is on the target. See StatusReceiver.
            body.Apply(StatusKind.Slick, source: owner.transform);

            if (frozen.Progress >= 1f)
                body.Apply(StatusKind.Frozen, source: owner.transform);
        }

        /// <summary>
        /// What the plume leaves on the surface it landed on, or null for one that takes nothing.
        ///
        /// <para>
        /// Two questions, and the first of them belongs here rather than to the coat: the ITEM
        /// answers "is this level enough to stand a horizontal disc on", which is true of every
        /// kind and so is asked once; the KIND answers "is this liquid or wet ground", which only
        /// <see cref="SurfaceCoatKind.Ice"/> cares about. Ice wins where it can, because ice is
        /// geometry as well as grip and frost laid over a pool would be a film on water nobody can
        /// stand on.
        /// </para>
        /// <para>
        /// A wall is the one surface that still takes nothing, and it says so: the plume ends in a
        /// puff of blow-off rather than in frost, which is the reading the player gets before they
        /// have spent a tank finding out (GDC-L1-SYS-0006). Asked on every machine and not only on
        /// the authority for exactly that reason.
        /// </para>
        /// </summary>
        private SurfaceCoatKind? CoatFor(RaycastHit hit)
        {
            if (Vector3.Angle(hit.normal, Vector3.up) > maxGroundAngle) return null;

            return SurfaceCoats.CanCoat(SurfaceCoatKind.Ice, hit.point)
                ? SurfaceCoatKind.Ice
                : SurfaceCoatKind.Slick;
        }

        /// <summary>
        /// The nearest thing the plume can land on, skipping the holder's own body and the machine
        /// they are strapped into.
        ///
        /// <para>
        /// The ray starts at the holder's eye, inside the holder, so an unfiltered trace freezes
        /// the person spraying. <c>AimProvider.NearestOutside</c> is the shared rule for that and is
        /// static precisely so a trace like this one can borrow it; it also picks the nearest hit by
        /// hand, because <c>RaycastNonAlloc</c> neither sorts its buffer nor tells you when it
        /// truncated one. Occlusion needs no second test: a ray stops at the first thing in the way,
        /// so nothing behind a rock is ever reached.
        /// </para>
        /// <para>
        /// The carrier is the owner's transform ROOT. Mounting parents the rider under the mount,
        /// so the root IS the machine they are riding — and on their own feet the root is the owner
        /// themselves, which makes the second filter a repeat of the first rather than a case that
        /// has to be branched on.
        /// </para>
        /// </summary>
        private bool Trace(out RaycastHit hit)
        {
            int count = Physics.RaycastNonAlloc(new Ray(rayOrigin, rayDirection), hits, range,
                                                hitMask, QueryTriggerInteraction.Ignore);

            return AimProvider.NearestOutside(hits, count, owner.transform, owner.transform.root,
                                              out hit);
        }

        /// <summary>
        /// Is this the machine that decides what freezes? Offline, or the server.
        ///
        /// Asked of the OWNER rather than of this item. An equipped artifact is instantiated into a
        /// hand and never spawned, so its own NetworkObject is dormant and
        /// <see cref="Network.Simulates"/> would answer "yes, you simulate it" on every machine in
        /// the session — and every player watching would announce the same freeze.
        /// </summary>
        private bool Decides => owner != null && Network.Simulates(owner.transform);

        // ── Lifecycle ──────────────────────────────────────────────────────────

        /// <summary>
        /// Find the tank and the presentation rig once, before any frame runs. In Awake rather than
        /// on equip because <see cref="Update"/> ticks the reservoir on a sprayer lying in the sand
        /// as readily as on one in a hand, and a bottle resolved only at equip would leave that one
        /// never refilling.
        /// </summary>
        private void Awake()
        {
            if (tank == null) tank = SupplyReservoir.On(gameObject);
            if (nozzle == null) nozzle = GetComponentInChildren<CryoSprayerNozzle>(true);
        }

        private void OnDisable() => ShutValve();

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
        /// Keep the cone that freezes and the cone that is drawn in step.
        ///
        /// A warning rather than an assignment: the plume is authored by <c>CryoPlumeBuilder</c>
        /// and the nozzle may be missing altogether on a stripped display copy, so silently taking
        /// the visual's angle would make what freezes depend on what happens to be on the prefab.
        /// Same rule, and the same reason, as the builder's reach check.
        /// </summary>
        private void OnValidate()
        {
            range = Mathf.Max(0.5f, range);

            CryoSprayerNozzle drawn = nozzle != null
                ? nozzle
                : GetComponentInChildren<CryoSprayerNozzle>(true);

            if (drawn != null && Mathf.Abs(drawn.OpenConeDegrees - coneHalfAngle) > 0.5f)
                Debug.LogWarning($"[CryoSprayer] The plume is drawn at {drawn.OpenConeDegrees}° and " +
                                 $"freezes at {coneHalfAngle}°. Vapour a player can see washing " +
                                 "over a creature has to freeze it.", this);

            // A timeout inside the keepalive interval would cut the plume between two perfectly
            // ordinary ticks, so it is floored well clear of it rather than left to whoever edits it.
            holdTimeout = Mathf.Max(UseChannel.HoldKeepAliveInterval * 2f, holdTimeout);
        }

        // No CaptureItemState / RestoreItemState override. The only thing this instance BECOMES is
        // its tank level, and UsableItem already writes the sibling reservoir's fill into the slot's
        // bag under SupplyCharge's own key — which is what makes it survive a save, a hotbar scroll,
        // a stow on the pack and a drop. Whether the trigger is down is not state: it is a button,
        // and a quicksave that loaded a sprayer already spraying would hand the player a press they
        // did not make. Frozen is not saved either, by StatusEffects' own decision, and a sheet of
        // ice is saved by the coat field rather than by the gun that made it.
    }
}
