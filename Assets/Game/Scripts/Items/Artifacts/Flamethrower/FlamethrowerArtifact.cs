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

        [Tooltip("What the cone may catch. Bodies without a StatusReceiver are ignored whatever " +
                 "this says, so it is a cost filter rather than a rule.")]
        [SerializeField] private LayerMask coneMask = ~0;

        [Tooltip("What stops the flame reaching a body. Set to nothing to let fire pass through " +
                 "walls, which is almost never what is wanted at six metres.")]
        [SerializeField] private LayerMask sightBlockers = ~0;

        [Tooltip("How often the cone is swept, in sweeps per second. Costs no bandwidth: the loop " +
                 "runs on the deciding machine only. Fifteen matches the hold stream, which is the " +
                 "rate the fire was tuned against.")]
        [SerializeField] private float sweepsPerSecond = 15f;

        [Header("Tank")]
        [Tooltip("The fuel bottle. Its drain and refill are the item's only cost — see " +
                 "SupplyReservoir. Found on this prefab when unset.")]
        [SerializeField] private SupplyReservoir tank;

        [Header("Jet")]
        [Tooltip("The flame's presentation. Optional: the item works with none, silently and " +
                 "invisibly, which is what a peer with a stripped display copy would get.")]
        [SerializeField] private FlameJet jet;

        [Tooltip("Where the flame leaves the lance. Falls back to the prefab root, which sits at " +
                 "the grip, so leaving this empty puts the jet in the holder's fist.")]
        [SerializeField] private Transform muzzle;

        [Header("Feel")]
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

        /// <summary>0 with the trigger up, 1 at full jet. Drives the presentation and nothing else.</summary>
        private float throttle;

        /// <summary>The aim ray as last reported. On the owner it is refreshed every frame.</summary>
        private Vector3 rayOrigin;
        private Vector3 rayDirection = Vector3.forward;

        /// <summary>What a peer actually draws along, chasing <see cref="rayDirection"/>.</summary>
        private Vector3 smoothedDirection = Vector3.forward;

        /// <summary>
        /// The bodies already set alight by the sweep in progress.
        ///
        /// A body is a handful of colliders and every one of them can be in the cone, so without
        /// this the same creature is announced several times per sweep — harmless, since a refresh
        /// is idempotent, and several times the traffic for nothing. Reused between sweeps: this
        /// runs fifteen times a second for as long as the trigger is down.
        /// </summary>
        private readonly HashSet<StatusReceiver> alight = new HashSet<StatusReceiver>();

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

            if (firing && IsAuthority()) Sweep(deltaTime);

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

            Vector3 direction = Direction();
            Transform ownerRoot = owner.transform.root;
            StatusReceiver holder = StatusReceiver.Of(owner);

            alight.Clear();

            foreach (Collider hit in Physics.OverlapSphere(rayOrigin, range, coneMask,
                                                           QueryTriggerInteraction.Ignore))
            {
                // The lance itself needs no exclusion of its own: while equipped it is parented into
                // the holder, so its root IS ownerRoot. The root also covers the machine they are
                // riding, which mounting parents them under.
                if (hit.transform.IsChildOf(ownerRoot)) continue;

                Vector3 point = hit.bounds.center;
                if (!RepulsorBlast.InCone(rayOrigin, direction, point, range, coneHalfAngle)) continue;

                StatusReceiver body = StatusReceiver.Of(hit.gameObject);
                if (body == null || body == holder) continue;

                // Tested, not claimed, until the body has actually caught: a creature is several
                // colliders and the first of them the overlap happens to return may be the one
                // outside the cone or the one behind the rock. Marking it burnt on that collider
                // would cost it the fire the collider beside it earned.
                if (alight.Contains(body)) continue;

                if (!Reaches(point, body, ownerRoot)) continue;

                alight.Add(body);

                // No duration and no magnitude: five seconds is what the fire says it is worth, and
                // a flamethrower does not get to decide how long it burns. The source is the HOLDER,
                // which is what ProvocationModule reads to work out who a creature is now afraid of.
                body.Apply(StatusKind.Burning, source: owner.transform);
            }
        }

        /// <summary>
        /// Is there a clear line from the cone's apex to <paramref name="point"/>?
        ///
        /// <para>
        /// The cone alone is a volume and knows nothing about what is standing in it, so without
        /// this a six-metre jet sets fire to whatever is on the far side of the rock the player is
        /// hiding behind. The body itself is what the ray meets whenever nothing else is in the way,
        /// and the holder's own body is what it meets first when the eye sits inside their head —
        /// neither of those is an obstruction.
        /// </para>
        /// </summary>
        private bool Reaches(Vector3 point, StatusReceiver body, Transform ownerRoot)
        {
            if (sightBlockers.value == 0) return true;

            Vector3 to = point - rayOrigin;
            float distance = to.magnitude;
            if (distance < 1e-3f) return true;

            if (!Physics.Raycast(rayOrigin, to / distance, out RaycastHit blocker, distance,
                                 sightBlockers, QueryTriggerInteraction.Ignore))
                return true;

            Transform obstruction = blocker.collider.transform;
            return obstruction.IsChildOf(body.transform) || obstruction.IsChildOf(ownerRoot);
        }

        /// <summary>
        /// Is this the machine that decides what the flame sets alight? Offline, or the server.
        ///
        /// Asked of the OWNER rather than of this item. An equipped artifact is instantiated into a
        /// hand and never spawned, so its own NetworkObject is dormant and
        /// <see cref="Network.Simulates"/> would answer "yes, you simulate it" on every machine in
        /// the session — and every player watching would announce the same fire.
        /// </summary>
        private bool IsAuthority() => owner != null && Network.Simulates(owner.transform);

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

            igniteTime = Mathf.Max(0.001f, igniteTime);
            fadeTime = Mathf.Max(0.001f, fadeTime);
            aimSmoothing = Mathf.Max(0.1f, aimSmoothing);

            // A timeout inside the keepalive interval would cut the jet between two perfectly
            // ordinary ticks, so it is floored well clear of it rather than left to whoever edits it.
            holdTimeout = Mathf.Max(UseChannel.HoldKeepAliveInterval * 2f, holdTimeout);
        }
    }
}
