using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay.Status;

namespace SpaceGame.Items
{
    /// <summary>
    /// A held lance that throws a continuous cone of fire. What it touches keeps burning after the
    /// flame has moved on.
    ///
    /// <para>
    /// <b>It never resolves damage.</b> Everything the fire does — the damage over time, the corpse
    /// check, the attribution, a creature breaking off and fleeing — belongs to
    /// <see cref="StatusKind.Burning"/> and to <c>StatusReactionModule</c>. This item's whole job is
    /// to decide, on one machine, which bodies are in the cone, and to say so. A jet swept across a
    /// crowd at fifteen ticks a second that billed its own damage would charge each body once per
    /// tick and once per overlapping pass, and a second fire source would charge it again; a fire is
    /// a fact about the body, not about the thing that lit it (GDC-L1-ARCH-0003 — the sender
    /// announces, it does not call).
    /// </para>
    /// <para>
    /// <b>Hold to fire, and that is a deliberate opposite of the laser staff.</b>
    /// <see cref="UsableItem.WantsHold"/> stays false: the flame stops when the button does, with no
    /// self-timed burst to commit to and no recharge to wait out. The staff sits at the committed
    /// end of the responsiveness–commitment axis because its cost is paid up front in a long
    /// recharge; this one sits at the responsive end because its cost is paid <i>during</i> the
    /// shot, out of a tank the player is watching drain (GDC-L1-FEEL-0008, GDC-L1-ECON-0002). Two
    /// continuous items, two different decisions to make about them.
    /// </para>
    /// <para>
    /// <b>What travels.</b> The owner's aim ray — origin in <c>P</c>, rotation in <c>R</c> — on the
    /// press and on every hold tick, and nothing else. No new message and no bit of <c>B</c>: the
    /// ordinary <c>UseItem</c>/<c>UseItemHold</c> pair already carries a continuous item, and
    /// <c>NetArg.A</c> is the slot code the server's stale-slot guard reads. The cone is traced from
    /// that ray on every machine that needs it, so the fire the server deals and the jet a peer
    /// draws are the same geometry rather than two guesses at it.
    /// </para>
    /// <para>
    /// <b>The tank runs everywhere.</b> See <see cref="SupplyReservoir"/>: the drain is a pure
    /// function of the hold stream, which every machine receives, so no fill has to be replicated
    /// per tick — and the value itself rides the hotbar slot's charge byte, because a prefab with a
    /// reservoir on it is one <c>SupplyCharge.Carries</c> answers true for.
    /// </para>
    /// </summary>
    public class FlamethrowerArtifact : ToolItem
    {
        /// <summary>The jet keeps acting for as long as the button is down.</summary>
        public override bool IsContinuous => true;

        /// <summary>
        /// A committed burst outlives the press: the hold stream has to keep carrying the aim
        /// until the tank ends the burn, or every other machine burns along a ray frozen at the
        /// moment the finger came up. See <see cref="UsableItem.WantsHold"/>.
        /// </summary>
        public override bool WantsHold => commitToBurst && firing;

        /// <summary>
        /// Server-run. The fire applies a condition to shared world state, which exactly one machine
        /// may decide (GDC-L1-MP-0004). The jet a peer sees is drawn by <see cref="PresentHold"/>.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Server;

        [Header("Cone")]
        [Tooltip("How far the flame reaches, in metres. Short on purpose — this is a crowd tool, " +
                 "not a rifle.")]
        [SerializeField] private float range = 6f;

        [Tooltip("Half the cone's opening angle, in degrees. Wide enough to catch a group at close " +
                 "range; the full spread is twice this.")]
        [SerializeField] private float coneHalfAngle = 25f;

        [Tooltip("What the cone may catch. Everything on these layers burns — a receiver is put " +
                 "on whatever does not already have one — so this mask is the only thing deciding " +
                 "what the flame can and cannot set alight.")]
        [SerializeField] private LayerMask coneMask = ~0;

        [Tooltip("What stops the flame reaching a body. Set to nothing to let fire pass through " +
                 "walls, which is almost never what is wanted at six metres.")]
        [SerializeField] private LayerMask sightBlockers = ~0;

        [Tooltip("How often the cone is swept, in sweeps per second. Costs no bandwidth: every " +
                 "machine runs the loop over its own colliders and none of it goes on the wire. " +
                 "Fifteen matches the hold stream, which is the rate the fire was tuned against.")]
        [SerializeField] private float sweepsPerSecond = 15f;

        [Header("Tank")]
        [Tooltip("The fuel bottle. Its drain and refill are the item's only cost — see " +
                 "SupplyReservoir. Found on this prefab when unset.")]
        [SerializeField] private SupplyReservoir tank;

        [Header("Ground fire")]
        [Tooltip("The patch of fire left where the jet lands. NOT a network prefab and must not " +
                 "become one: every machine lays its own from the same aim, and only the deciding " +
                 "machine's copies set anything alight. See GroundFire.")]
        [SerializeField] private GameObject groundFirePrefab;

        [Tooltip("How often fire is laid on the ground, in patches per second. Merged onto a world " +
                 "grid by GroundFireField, so this is how quickly a swept trail fills in rather " +
                 "than how many patches a burst produces.")]
        [SerializeField] private float firesPerSecond = 10f;

        [Tooltip("What the flame can set alight underfoot. Terrain and scenery; a body is caught " +
                 "by the cone instead, and is skipped here whatever this says.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("How level a surface has to be to hold fire, as the upward part of its normal. " +
                 "At 0 the patches stand out of walls at right angles to the ground, which is the " +
                 "one place a flat disc of flame reads as a decal rather than as fire.")]
        [SerializeField, Range(0f, 1f)] private float minGroundSlope = 0.45f;

        [Tooltip("How far off the surface a patch is placed, in metres. Enough to keep the flames " +
                 "out of the ground they are standing on and no more.")]
        [SerializeField] private float groundOffset = 0.04f;

        [Tooltip("Seconds between the trigger going down and the first patch being laid — roughly " +
                 "how long the flame takes to cross the range.")]
        [SerializeField] private float groundFireDelay = 0.2f;

        [Tooltip("How far below the flame the ground still catches, in metres. This is what lets a " +
                 "jet fired ACROSS open sand set it alight rather than only the wall it is pointed " +
                 "at. Roughly the height of the burning cloud the jet leaves behind it.")]
        [SerializeField] private float groundReach = 1.6f;

        [Tooltip("Metres between the points along the jet that look for ground under them. Just " +
                 "under GroundFireField's cell size, so a swept trail has no cold gaps in it and " +
                 "no two samples in one lay ever land in the same cell.")]
        [SerializeField] private float groundSampleStep = 1f;

        [Tooltip("How far down the jet the sampling starts, in metres. The flame at the muzzle is " +
                 "at the holder's own feet, and fire laid there sets the player who fired it " +
                 "alight the instant they pull the trigger.")]
        [SerializeField] private float groundSampleStart = 2f;

        [Header("Jet")]
        [Tooltip("The flame's presentation. Optional: the item works with none, silently and " +
                 "invisibly, which is what a peer with a stripped display copy would get.")]
        [SerializeField] private FlameJet jet;

        [Tooltip("Where the flame leaves the lance. Falls back to the prefab root, which sits at " +
                 "the grip, so leaving this empty puts the jet in the holder's fist.")]
        [SerializeField] private Transform muzzle;

        [Header("Feel")]
        [Tooltip("Once lit, burn until the tank is dry whatever the trigger does. Off, the lance's " +
                 "way: the flame stops when the button does. On, the Flame Gauntlet's way: one press " +
                 "is one whole burst, sized by the tank (a 3 s tank that must be full to start is " +
                 "a 3 s burst, every time), and the trigger cannot end it early -- the committed end " +
                 "of the responsiveness-commitment axis, like the laser staff (GDC-L1-FEEL-0008).")]
        [SerializeField] private bool commitToBurst;

        [Tooltip("Seconds for the jet to reach full throttle when the trigger goes down.")]
        [SerializeField] private float igniteTime = 0.06f;

        [Tooltip("Seconds for the jet to die back once the trigger comes up. Longer than the " +
                 "ignition: a flame that stops instantly reads as a light switch.")]
        [SerializeField] private float fadeTime = 0.18f;

        [Tooltip("How fast a peer's copy of the jet catches up to each aim tick. Higher is snappier " +
                 "and more jittery.")]
        [SerializeField] private float aimSmoothing = 22f;

        [Tooltip("Seconds of silence after which the jet puts itself out. The safety net for a " +
                 "release that never arrived — a dropped packet, or a player who disconnected " +
                 "mid-burn. Must comfortably exceed UseChannel's keepalive interval.")]
        [SerializeField] private float holdTimeout = 0.5f;

        // ── Runtime state ──────────────────────────────────────────────────────

        /// <summary>Is the jet on, on this machine?</summary>
        private bool firing;

        private float lastHoldTime;
        private float sweepTimer;
        private float layTimer;

        /// <summary>0 with the trigger up, 1 at full jet. Drives the presentation and nothing else.</summary>
        private float throttle;

        /// <summary>The aim ray as last reported. On the owner it is refreshed every frame.</summary>
        private Vector3 rayOrigin;
        private Vector3 rayDirection = Vector3.forward;

        /// <summary>What a peer actually draws along, chasing <see cref="rayDirection"/>.</summary>
        private Vector3 smoothedDirection = Vector3.forward;

        /// <summary>
        /// The bodies the cone is on this sweep, one entry each — <see cref="ConeSweep"/> is what
        /// keeps a creature that puts a handful of colliders in the cone from being announced
        /// several times. Reused between sweeps: this runs fifteen times a second for as long as
        /// the trigger is down.
        /// </summary>
        private readonly List<ConeBody> alight = new List<ConeBody>();

        /// <summary>
        /// What the flame counts as a body, handed to <see cref="ConeSweep"/>. Cached because a
        /// method group becomes a fresh delegate at every call site it is written at, and this one
        /// is written at fifteen a second.
        /// </summary>
        private static readonly Func<GameObject, StatusReceiver> Catches = Ignition.Receiver;

        // ── The press ──────────────────────────────────────────────────────────

        /// <summary>
        /// Owner-side, on the press: the aim the burn starts from.
        ///
        /// Sent even though hold ticks carry the same thing, because the first tick is a frame away
        /// and the jet is drawn on the press. Without it the first frame of every burst points
        /// wherever the lance happened to be lying.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            WriteAim(ref arg);
        }

        /// <summary>
        /// Authority-side ignition. Nothing burns here — the cone is swept in <see cref="Update"/>
        /// for as long as the trigger is down — but the server has to start its own jet, because a
        /// dedicated server never receives <see cref="Present"/>.
        /// </summary>
        protected override void Use() => Ignite(UseArg);

        /// <summary>Every machine's ignition, including the owner's, immediately.</summary>
        protected override void Present() => Ignite(UseArg);

        /// <summary>
        /// Authority-side gate on the press. An empty tank refuses the press outright rather than
        /// lighting a jet that stops on the next frame.
        /// </summary>
        protected override bool CanUse() => base.CanUse() && (tank == null || tank.CanStart);

        /// <summary>
        /// A refilling item is never spent, so this must stay silent: the default raises
        /// <c>OnItemDepleted</c>, which <c>EquipmentController</c> answers by taking the item out of
        /// the inventory altogether. Unreachable while the prefab leaves <c>maxUses</c> unlimited,
        /// and present so that giving the lance a charge limit does not quietly delete it.
        /// </summary>
        protected override void OnMaxUsesReached() { }

        // ── The hold stream ────────────────────────────────────────────────────

        /// <summary>
        /// Owner-side, once per tick: put the aim ray in the message.
        ///
        /// The ray from <c>AimProvider</c>, never the eye's own forward. Mounted, those are
        /// different things — the eye is still where the flame is aimed from, but the view the
        /// player is looking down is the mount's, and a cone that ignored it would set fire to the
        /// craft they are sitting in.
        /// </summary>
        public override void OnRequestHold(ref NetArg arg, bool active)
        {
            if (!active) return;

            WriteAim(ref arg);
        }

        /// <summary>Authority-side. Only records the aim; the cone is swept in <see cref="Update"/>.</summary>
        protected override void Hold(NetArg arg, bool active) => ApplyHold(arg, active);

        /// <summary>Every machine: the same, so the jet a peer draws follows the same ray.</summary>
        protected override void PresentHold(NetArg arg, bool active) => ApplyHold(arg, active);

        /// <summary>
        /// Shared by both halves, and idempotent, because on a host both halves run.
        ///
        /// <para>
        /// A dedicated server never receives <see cref="PresentHold"/> at all — it is not among the
        /// "others" its own broadcast goes to — so the aim has to be recorded on the authority path
        /// too, or the one machine that decides what is burning is the one machine that does not
        /// know where the player is pointing.
        /// </para>
        /// <para>
        /// The final tick arrives with <paramref name="active"/> false and stops the jet
        /// unconditionally, including on a machine that never saw the ticks before it. A release is
        /// one message; a disconnect is none at all, which is what <see cref="holdTimeout"/> is for.
        /// </para>
        /// </summary>
        private void ApplyHold(NetArg arg, bool active)
        {
            if (!active)
            {
                // A committed burst ignores the button. The stream itself only ends once WantsHold
                // has gone false, i.e. once the tank has already put the jet out, so the release
                // that does arrive here finds nothing burning to stop.
                if (commitToBurst && firing) return;
                Extinguish();
                return;
            }

            lastHoldTime = Time.time;

            if (arg.HasOrientation)
            {
                rayOrigin = arg.P;
                rayDirection = arg.R * Vector3.forward;
            }

            // A machine that missed the press — a late joiner, or a peer whose UseItem was dropped —
            // still lights on the first tick it does hear rather than streaming a dark jet.
            if (!firing) Ignite(arg);
        }

        /// <summary>
        /// Light the jet, unless it is already lit or the tank refuses.
        ///
        /// Idempotent, because on a host both <see cref="Use"/> and <see cref="Present"/> run for
        /// the same press.
        /// </summary>
        private void Ignite(NetArg arg)
        {
            if (arg.HasOrientation)
            {
                rayOrigin = arg.P;
                rayDirection = arg.R * Vector3.forward;
            }

            if (firing) return;
            if (tank != null && !tank.CanStart) return;

            firing = true;
            smoothedDirection = rayDirection;
            lastHoldTime = Time.time;
            sweepTimer = 0f;

            // Negative, so the first patch is laid a beat AFTER the trigger rather than on the
            // frame it goes down. The jet needs that long to actually reach the ground, and fire
            // appearing six metres away before the flame gets there is the tell that the patch is
            // being placed rather than landing.
            layTimer = -groundFireDelay;
        }

        /// <summary>Put the jet out. Safe on an already-dark lance, and reached that way constantly.</summary>
        private void Extinguish()
        {
            firing = false;
            sweepTimer = 0f;
            alight.Clear();
        }

        // ── Per frame ──────────────────────────────────────────────────────────

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            // The safety net. A release is one message, and one message is exactly the kind of thing
            // that goes missing — along with the player who was holding the trigger. Without this
            // that leaves a jet burning forever with nobody able to stop it.
            if (firing && Time.time - lastHoldTime > holdTimeout) Extinguish();

            // Every frame, not only while firing: the pilot flame hangs off the same rig, and the
            // jet has to be pointing the right way on the frame the trigger goes down rather than
            // one smoothing constant later.
            RefreshOwnerAim(deltaTime);

            // Runs whether or not the jet is on, because the refill is the half that happens when
            // nothing is happening. Every machine runs it, off the same stream — SupplyReservoir.
            if (tank != null && !tank.Tick(deltaTime, firing) && firing) Extinguish();

            throttle = Mathf.MoveTowards(
                throttle,
                firing ? 1f : 0f,
                deltaTime / Mathf.Max(0.001f, firing ? igniteTime : fadeTime));

            // On EVERY machine, not only the authority — which is the price of the line above
            // creating receivers. A status arrives as a message on the body's own relay, and a
            // relay with nothing subscribed drops it without a word, so a receiver the server
            // invented alone is a body that burns for the server and for nobody else. Running the
            // sweep everywhere puts the same component on the same bodies everywhere; only the
            // deciding machine bills anything, because StatusReceiver.Apply returns early when it
            // is not the one that decides.
            if (firing) Sweep(deltaTime);

            // Every machine, unlike the sweep: the patches are what the fire LOOKS like, and a peer
            // watching a teammate torch a dune has to see the dune burning. Only the deciding
            // machine's patches set anything alight, which is the flag Lay passes down.
            if (firing) Lay(deltaTime);

            DrawJet();
        }

        /// <summary>
        /// On the machine holding the camera the aim is available right now and is better than
        /// anything that arrived over the wire. Everywhere else, ease toward the last tick.
        /// </summary>
        private void RefreshOwnerAim(float deltaTime)
        {
            if (OwnerIsLocal())
            {
                if (aimProvider != null && aimProvider.AimTransform != null)
                {
                    Ray aim = aimProvider.GetAimRay();
                    rayOrigin = aim.origin;
                    rayDirection = aim.direction;
                }

                smoothedDirection = rayDirection;
                return;
            }

            smoothedDirection = Vector3.Slerp(
                smoothedDirection,
                rayDirection,
                1f - Mathf.Exp(-aimSmoothing * deltaTime));
        }

        // ── Authority side: the fire ───────────────────────────────────────────

        /// <summary>
        /// Sweep the cone on the schedule, and no faster.
        ///
        /// <para>
        /// Not a catch-up loop, unlike a damage tick: a fire is a flag with an expiry rather than an
        /// accumulating quantity, so a sweep a slow frame skipped owes nothing and paying it twice
        /// would change nothing. Driven from here rather than straight out of <see cref="Hold"/>
        /// because the hold stream throttles itself — an aim that has not moved goes out only as a
        /// keepalive every fifth of a second, and a creature that walked into a steady jet would
        /// wait that long to catch fire.
        /// </para>
        /// </summary>
        private void Sweep(float deltaTime)
        {
            if (sweepsPerSecond <= 0f) return;

            sweepTimer += deltaTime;

            float step = 1f / sweepsPerSecond;
            if (sweepTimer < step) return;

            sweepTimer = 0f;
            Burn();
        }

        /// <summary>
        /// Set fire to everything standing in the cone.
        ///
        /// <para>
        /// The apex is the aim ray's own origin rather than the muzzle, for the reason the laser
        /// staff traces from there: the ray is the one thing every machine agrees on, while a muzzle
        /// is a bone on an animated arm that each machine poses for itself. It also errs in the safe
        /// direction — a cone opening from the eye is narrower beside the player than one opening
        /// from the muzzle, so nothing standing at the holder's shoulder is caught.
        /// </para>
        /// <para>
        /// A body with no <see cref="StatusReceiver"/> does not burn, and one is deliberately not
        /// added here. A receiver put on the server's copy alone is a body that burns for the server
        /// and for nobody else: the condition travels as a message on that body's own relay, and a
        /// relay with nothing subscribed drops it without a word. Receivers are authored on the
        /// prefab or added by <c>StatusReactionModule</c>, both of which happen on every machine.
        /// </para>
        /// </summary>
        private void Burn()
        {
            if (owner == null) return;

            // The lance itself needs no exclusion of its own: while equipped it is parented into the
            // holder, so its root IS the owner's root. The root also covers the machine they are
            // riding, which mounting parents them under.
            //
            // Anything the flame touches burns — a creature, another player, a crate, a prefab
            // nobody thought to author a receiver on. Asking only for receivers that already existed
            // is what made the weapon do nothing to most of the world; Ignition draws the line at
            // bodies, so a dune does not become one burning object.
            ConeSweep.Bodies(rayOrigin, Direction(), range, coneHalfAngle, coneMask, sightBlockers,
                             owner.transform.root, StatusReceiver.Of(owner), Catches, alight);

            foreach (ConeBody caught in alight)
                Ignition.Light(caught.Collider.gameObject, owner.transform);
        }

        // ── Every machine: the fire left behind ────────────────────────────────

        /// <summary>
        /// Lay fire under the jet, all the way along it.
        ///
        /// <para>
        /// This is what makes the item a flamethrower rather than a torch: the flame keeps burning
        /// after the jet has moved on, so sweeping a line across the sand leaves a line of fire
        /// standing in it and the player is choosing where the ground is dangerous, not only what
        /// is in front of the barrel right now (GDC-L1-FEEL-0004 — the same action, answered in a
        /// second channel that outlasts it).
        /// </para>
        /// <para>
        /// <b>The whole path is sampled, not just the impact point.</b> A single ray down the aim
        /// only ever lights what the player is POINTING AT, so a jet fired across open sand — the
        /// ordinary way this weapon is used — set nothing alight at all and the ground only caught
        /// when someone happened to aim into it. Instead the jet's path is walked in
        /// <see cref="groundSampleStep"/> strides and a short ray dropped from each one: wherever
        /// the flame passes within <see cref="groundReach"/> of the ground, the ground catches.
        /// </para>
        /// <para>
        /// Traced down the SMOOTHED direction — the one the jet is drawn along — rather than down
        /// the raw aim the authority sweeps with. The two differ by at most a tick's worth of
        /// smoothing on a peer, and of the two mismatches available, fire that is not quite where
        /// the server thinks it is beats fire that is not where the flame visibly went.
        /// </para>
        /// </summary>
        private void Lay(float deltaTime)
        {
            if (groundFirePrefab == null || firesPerSecond <= 0f) return;

            layTimer += deltaTime;

            float step = 1f / firesPerSecond;
            if (layTimer < step) return;

            layTimer = 0f;

            Vector3 direction = Direction();

            // How far the flame actually gets. Sampling past whatever stopped it would lay fire on
            // the far side of the rock the player is standing behind, which is the same mistake the
            // cone's own line-of-sight check exists to avoid.
            float carry = range;

            if (Physics.Raycast(rayOrigin, direction, out RaycastHit blocked, range, groundMask,
                                QueryTriggerInteraction.Ignore))
            {
                // A BODY does not stop the flame. Shortening the walk at a creature would mean that
                // pointing at one put out the fire on the sand behind it, which is the opposite of
                // what aiming at something should do. Only scenery ends the jet.
                if (StatusReceiver.Of(blocked.collider.gameObject) == null)
                {
                    carry = blocked.distance;
                    Scorch(blocked.point, blocked.normal, blocked.collider);
                }
            }

            // Floored here and not only in OnValidate: that runs in the editor, and a prefab
            // authored with a stride of zero would walk this loop forever on a player's machine.
            float stride = Mathf.Max(0.25f, groundSampleStep);

            for (float along = groundSampleStart; along <= carry; along += stride)
            {
                Vector3 overhead = rayOrigin + direction * along;

                if (Physics.Raycast(overhead, Vector3.down, out RaycastHit under, groundReach,
                                    groundMask, QueryTriggerInteraction.Ignore))
                    Scorch(under.point, under.normal, under.collider);
            }
        }

        /// <summary>
        /// Set fire to one piece of ground, if it is ground at all.
        ///
        /// <para>
        /// A body catches fire as a body, through the cone and its own <see cref="StatusReceiver"/>.
        /// A patch standing on one would be a disc of flame welded to a creature's hip that stays
        /// behind in the air the moment it walks off.
        /// </para>
        /// </summary>
        private void Scorch(Vector3 point, Vector3 normal, Collider surface)
        {
            if (StatusReceiver.Of(surface.gameObject) != null) return;

            if (normal.y < minGroundSlope) return;
            if (owner != null && surface.transform.IsChildOf(owner.transform.root)) return;

            GroundFireField.Kindle(groundFirePrefab,
                                   point + normal * groundOffset,
                                   owner != null ? owner.transform : transform);
        }

        // ── Every machine: the jet ─────────────────────────────────────────────

        private void DrawJet()
        {
            if (jet == null) return;

            jet.Aim(MuzzlePoint(), Direction());
            jet.SetThrottle(throttle);
        }

        private Vector3 MuzzlePoint() => muzzle != null ? muzzle.position : transform.position;

        private Vector3 Direction() =>
            smoothedDirection.sqrMagnitude > 1e-6f ? smoothedDirection.normalized : transform.forward;

        private void WriteAim(ref NetArg arg)
        {
            if (aimProvider == null || aimProvider.AimTransform == null) return;

            Ray aim = aimProvider.GetAimRay();
            arg.P = aim.origin;
            arg.R = Quaternion.LookRotation(aim.direction);
        }

        // ── Per-instance state ─────────────────────────────────────────────────
        //
        // No CaptureItemState / RestoreItemState override. The fill is the only thing this lance
        // BECOMES, and UsableItem already writes the sibling reservoir's charge into the slot's bag
        // for every item there is — under SupplyCharge's own key, which is what makes it survive a
        // save, a hotbar scroll, a stow on the pack and a drop.

        // ── Lifecycle ──────────────────────────────────────────────────────────

        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);

            if (tank == null)
            {
                Debug.LogWarning($"[Flamethrower] '{name}' has no SupplyReservoir, so it will never " +
                                 "run out of fuel. Add one to the prefab root.", this);
            }

            // Lit for as long as it is held. The pilot flame is what says the lance is live before
            // the trigger is touched, which is the only warning anything standing in front of it
            // gets (GDC-L1-FEEL-0004).
            if (jet != null) jet.SetPilot(true);
        }

        public override void OnUnequipped(GameObject holder)
        {
            base.OnUnequipped(holder);

            // Put it out AND clear the visual in the same breath. This object is usually destroyed
            // straight after, but it is also what a dropped lance becomes, and a discarded
            // flamethrower lying in the sand still throwing a jet across the desert is the failure.
            Douse();
            if (jet != null) jet.SetPilot(false);
        }

        /// <summary>
        /// Find the fuel bottle once, before any frame runs. In Awake rather than on equip because
        /// <see cref="Update"/> ticks the reservoir on a lance lying in the sand as readily as on
        /// one in a hand, and a bottle resolved only at equip would leave that one never refilling.
        /// </summary>
        private void Awake()
        {
            if (tank == null) tank = SupplyReservoir.On(gameObject);
        }

        private void OnDisable() => Douse();

        private void Douse()
        {
            Extinguish();
            throttle = 0f;
            DrawJet();
        }

        private void OnValidate()
        {
            range = Mathf.Max(0.5f, range);
            coneHalfAngle = Mathf.Clamp(coneHalfAngle, 1f, 90f);
            sweepsPerSecond = Mathf.Clamp(sweepsPerSecond, 1f, 60f);
            firesPerSecond = Mathf.Clamp(firesPerSecond, 0f, 60f);
            groundOffset = Mathf.Clamp(groundOffset, 0f, 0.5f);
            groundFireDelay = Mathf.Clamp(groundFireDelay, 0f, 1f);

            groundReach = Mathf.Clamp(groundReach, 0f, 20f);
            groundSampleStart = Mathf.Clamp(groundSampleStart, 0f, range);

            // Floored well above zero rather than merely above it: Lay walks the jet in strides of
            // this, so a stride of nothing is a loop that never advances and hangs the frame.
            groundSampleStep = Mathf.Clamp(groundSampleStep, 0.25f, range);

            igniteTime = Mathf.Max(0.001f, igniteTime);
            fadeTime = Mathf.Max(0.001f, fadeTime);
            aimSmoothing = Mathf.Max(0.1f, aimSmoothing);

            // A timeout inside the keepalive interval would cut the jet between two perfectly
            // ordinary ticks, so it is floored well clear of it rather than left to whoever edits it.
            holdTimeout = Mathf.Max(UseChannel.HoldKeepAliveInterval * 2f, holdTimeout);
        }
    }
}
