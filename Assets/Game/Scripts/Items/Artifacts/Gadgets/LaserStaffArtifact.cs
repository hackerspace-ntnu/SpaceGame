using UnityEngine;
using UnityEngine.VFX;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    /// <summary>
    /// A staff that throws a lightning arc for three seconds when fired, burning whatever it rests
    /// on, and then needs ten to recharge.
    ///
    /// It is the first item in the game that acts continuously, and it does that through the same
    /// request/authority/present split every artifact already uses — see
    /// <see cref="UsableItem.IsContinuous"/>. The only thing it adds is that the split runs many
    /// times instead of once.
    ///
    /// <para>
    /// <b>Why the button no longer decides how long it burns.</b> The press ignites it and the
    /// staff times itself out; holding does not extend it and releasing does not cut it short. That
    /// makes the trigger a commitment rather than a dial — with a fixed burn and a long recharge,
    /// the interesting decision is WHEN to spend it, which a hold-to-fire beam does not have. The
    /// hold ticks are still streamed for the whole three seconds, because they are the only channel
    /// that carries an aim; see <see cref="UsableItem.WantsHold"/> for why that had to be said out
    /// loud rather than left to the button.
    /// </para>
    /// <para>
    /// <b>Why the arc is geometry and not just a shader.</b> The kinks are real displacement of the
    /// LineRenderer's points, re-rolled several times a second. A bolt painted into the UV of a
    /// straight ribbon looks like a bolt only until the beam sweeps, at which point the squiggle
    /// slides along a visibly straight strip. SpaceGame/LightningBeam draws the discharge — the
    /// filament, the segment breaks, the strobe — and this class draws the path.
    /// </para>
    ///
    /// <para>
    /// <b>What travels, and why it is the ray rather than the endpoint.</b> The owner reports its
    /// aim ray — origin in <c>P</c>, rotation in <c>R</c> — and every machine traces that same ray
    /// for itself. Sending the point the beam landed on would have been fewer steps, but it would
    /// also have let a client name any target it liked, and it would have left peers drawing a beam
    /// that ends wherever the owner last said rather than where the server is actually dealing
    /// damage. Tracing a shared ray keeps those two the same thing.
    /// </para>
    /// <para>
    /// <b>What does not travel.</b> The owner never reads its own messages. Ticks arrive at 15 Hz
    /// and a beam that turned with the player only fifteen times a second would smear visibly
    /// against a mouse, so the machine holding the camera re-reads it every frame and only peers
    /// interpolate between ticks. This is the same reason
    /// <see cref="UsableItem.OnRequestUse"/> exists at all: an aim is honest on exactly one machine.
    /// </para>
    /// </summary>
    public class LaserStaffArtifact : ToolItem
    {
        /// <summary>The beam keeps acting after the press. See the class summary.</summary>
        public override bool IsContinuous => true;

        /// <summary>
        /// Keep the hold stream — and with it the aim — running for the whole burn, however
        /// briefly the button was actually down. See <see cref="UsableItem.WantsHold"/>.
        /// </summary>
        public override bool WantsHold => _lit;

        /// <summary>
        /// Server-run. Unlike the grapple, whose whole effect is the holder's own body, this one
        /// hurts other things — and damage is shared world state that exactly one machine may
        /// decide. The beam a peer sees is drawn by <see cref="PresentHold"/>, not by this.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Server;

        [Header("Beam")]
        [Tooltip("How far the beam reaches, in metres.")]
        [SerializeField] private float range = 120f;

        [Tooltip("What the beam can hit. Triggers are always ignored.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Tooltip("Where the beam leaves the staff. Falls back to the prefab root, which sits at the grip, so leaving this empty puts the beam in the holder's fist.")]
        [SerializeField] private Transform muzzle;

        [Tooltip("The arc itself. Many points, muzzle to impact, drawn with the LightningBeam shader.")]
        [SerializeField] private LineRenderer beam;

        [Header("Burst")]
        [Tooltip("Seconds the arc burns for once fired. The button does not extend or shorten it.")]
        [SerializeField] private float burnDuration = 3f;

        [Tooltip("Seconds after the burn ends before the staff will fire again.")]
        [SerializeField] private float cooldown = 10f;

        [Header("Arc")]
        [Tooltip("How many segments the arc is drawn with. More is a finer bolt and a longer line to rebuild each frame; below about eight it stops reading as lightning and starts reading as a bent stick.")]
        [SerializeField] private int arcSegments = 26;

        [Tooltip("How far the arc wanders sideways, as a fraction of its own length. Scaled with distance so a short arc is not a wild scribble and a long one is not a straight line.")]
        [SerializeField] private float arcSpread = 0.035f;

        [Tooltip("Ceiling on that wander, in metres. Without it a 120 m arc would swing six metres wide and miss what the beam is actually burning.")]
        [SerializeField] private float arcMaxOffset = 0.55f;

        [Tooltip("How many times a second the bolt jumps to a completely new shape. This is the number that decides whether it reads as lightning or as a wobbling rope.")]
        [SerializeField] private float arcRestrikeRate = 22f;

        [Header("Damage")]
        [Tooltip("Damage per second while the beam rests on something. This is the dial to tune — the tick rate below only decides how finely it is sampled.")]
        [SerializeField] private float damagePerSecond = 100f;

        [Tooltip("How often damage is sampled, in ticks per second. Costs no bandwidth: the whole loop runs on the server only, where NetDamage lands as a direct call.")]
        [SerializeField] private float damageTicksPerSecond = 50f;

        [Header("Impact")]
        [Tooltip("Parent of the whole impact rig. Moved to the hit point and turned to face along the surface normal, so every emitter under it sprays out of the surface instead of along some fixed axis.")]
        [SerializeField] private Transform impactRoot;

        [Tooltip("Optional glow placed where the beam lands. Scaled with the beam's ignition, hidden when the beam reaches nothing.")]
        [SerializeField] private Transform impactGlow;

        [Tooltip("Fast white-hot sparks thrown off the cut.")]
        [SerializeField] private ParticleSystem sparks;

        [Tooltip("Slower embers that fall and cool.")]
        [SerializeField] private ParticleSystem embers;

        [Tooltip("Smoke rising off what is being burned through.")]
        [SerializeField] private ParticleSystem smoke;

        [Tooltip("Optional light at the impact point.")]
        [SerializeField] private Light impactLight;

        [Tooltip("The Lightning VFX graph, struck at whatever the arc is resting on. Leave empty for no strike.")]
        [SerializeField] private GameObject strikeVfx;

        [Tooltip("Seconds between strikes while the arc stays on something. One lands the instant it bites.")]
        [SerializeField] private float strikeInterval = 0.7f;

        [Tooltip("Seconds a spawned strike is left alive before it is cleaned up. The graph is a one-shot burst; nothing else destroys it.")]
        [SerializeField] private float strikeLifetime = 3f;

        [Tooltip("Recolours the strike graph's two exposed colours. Red, like the rest of the weapon.")]
        [ColorUsage(true, true)]
        [SerializeField] private Color strikeColor = new Color(1f, 0.09f, 0.06f, 1f);

        [SerializeField] private float impactLightIntensity = 7f;

        [Tooltip("How hard the impact light flickers, 0 for a steady lamp.")]
        [SerializeField] private float impactLightFlicker = 0.35f;

        [Tooltip("How much the glow quad breathes in and out.")]
        [SerializeField] private float glowPulse = 0.18f;

        [Tooltip("Base size of the glow quad, in metres.")]
        [SerializeField] private float glowSize = 0.7f;

        [Tooltip("Extra sparks thrown in one go the instant the beam bites a new surface. The steady spray alone ramps in too politely for a cutting beam.")]
        [SerializeField] private int biteBurst = 45;

        [Header("Feel")]
        [Tooltip("Seconds for the beam to reach full length when it lights.")]
        [SerializeField] private float igniteTime = 0.08f;

        [Tooltip("Seconds for the beam to die back once released.")]
        [SerializeField] private float fadeTime = 0.12f;

        [Tooltip("How fast a peer's copy of the beam catches up to each aim tick. Higher is snappier and more jittery.")]
        [SerializeField] private float aimSmoothing = 22f;

        [Tooltip("Seconds of silence after which the beam puts itself out. The safety net for a release that never arrived — a dropped packet, or a player who disconnected mid-beam. Must comfortably exceed EquipmentController's send interval.")]
        [SerializeField] private float holdTimeout = 0.5f;

        // ── Runtime state ──────────────────────────────────────────────────────

        private bool _lit;
        private float _lastHoldTime;

        /// <summary>When the current burn ends. Meaningless while <see cref="_lit"/> is false.</summary>
        private float _burnEndsAt;

        /// <summary>
        /// When the staff will fire again — burn included, so it covers the whole shot.
        ///
        /// <para>
        /// Stamped at IGNITION, not when the burn ends, and that ordering is load-bearing. On a
        /// host both halves of a press run, and <see cref="Present"/> runs first: a gate that read
        /// "not currently burning" would be false by the time the server's <see cref="Use"/>
        /// reached <see cref="CanUse"/>, so the press would be refused after the arc had already
        /// lit — spending a charge on a shot the server never took. A deadline that is already in
        /// the future the moment the arc lights answers both questions with one number.
        /// </para>
        /// <para>
        /// Every machine keeps its own, rather than the server keeping one and telling the others.
        /// The burn starts from a press each of them saw and runs for a constant, so they all reach
        /// the same answer within a frame of each other — and the machine whose answer actually has
        /// to be right NOW is the owner's, which is the one being asked to press the button again.
        /// </para>
        /// </summary>
        private float _cooldownEndsAt;

        /// <summary>When the next strike graph may be spawned. See <see cref="strikeInterval"/>.</summary>
        private float _nextStrikeTime;

        /// <summary>
        /// Set by <see cref="Ignite"/> when a press actually lights the arc, and consumed by
        /// <see cref="Use"/>.
        ///
        /// It exists to answer one awkward question honestly: when the authority half of a press
        /// reaches <see cref="CanUse"/>, has THIS press already been accepted? On a host it has —
        /// <see cref="Present"/> ran first — so by then the arc is burning and the recharge is
        /// stamped, and both of the obvious gates ("not burning", "recharged") say no to a press
        /// that plainly succeeded. Refusing there does not stop anything, since the arc is already
        /// lit; it only skips the charge, so a limited-use staff would fire forever on a host and
        /// deplete normally on a dedicated server.
        ///
        /// A flag the ignition itself raises separates "this press" from "another press during the
        /// same burn", which is the distinction the two timers cannot make.
        /// </summary>
        private bool _pressLitTheArc;

        /// <summary>
        /// Per-instance offset into the arc's noise, so two staffs firing side by side do not throw
        /// the same bolt in perfect lockstep.
        /// </summary>
        private float _arcSeed;

        /// <summary>Reused between frames. The arc is rebuilt every frame and this is 27 Vector3s.</summary>
        private Vector3[] _arcPoints;

        /// <summary>The aim ray as last reported. On the owner it is refreshed every frame.</summary>
        private Vector3 _rayOrigin;
        private Vector3 _rayDirection = Vector3.forward;

        /// <summary>What a peer actually draws along, chasing <see cref="_rayDirection"/>.</summary>
        private Vector3 _smoothedDirection = Vector3.forward;

        private Vector3 _endPoint;

        /// <summary>
        /// The surface the beam is resting on, or the beam's own reverse where it reaches nothing.
        ///
        /// Kept because every part of the impact needs it: sparks spray out of a surface, not along
        /// the beam, and the glow quad has to sit on the surface rather than intersect it. The
        /// trace used to discard this, which is why the first impact was a single flat sprite.
        /// </summary>
        private Vector3 _hitNormal = Vector3.up;

        private GameObject _hitObject;

        /// <summary>Was the beam landing on something last frame? Drives the emitter transitions.</summary>
        private bool _wasLanded;

        /// <summary>0 while out, 1 at full length. Drives both the visual and nothing else.</summary>
        private float _ignition;

        private float _damageTimer;

        /// <summary>
        /// Damage earned but not yet spent, because <see cref="NetDamage"/> deals whole points and
        /// discards anything that rounds to zero.
        ///
        /// Without this the tick rate would quietly become the damage number: at 50 ticks a second
        /// every fractional per-tick amount would floor to 0 and the beam would do nothing at all,
        /// while a rate of 4 would make the same <see cref="damagePerSecond"/> land in full. Carry
        /// the remainder and the rate stops mattering, which is what lets it be a sampling rate.
        /// </summary>
        private float _damageCarry;

        private MaterialPropertyBlock _beamProperties;

        private static readonly int IgniteId = Shader.PropertyToID("_Ignite");
        private static readonly int BeamLengthId = Shader.PropertyToID("_BeamLength");

        /// <summary>The Lightning graph's two exposed colours. Names, not guesses — see Lightning.vfx.</summary>
        private static readonly int LightningColorId = Shader.PropertyToID("LightningColor");
        private static readonly int Color01Id = Shader.PropertyToID("Color01");

        private void Awake() => _arcSeed = Random.value * 1000f;

        // ── The recharge, across instances ─────────────────────────────────────
        //
        // The held object is a fresh Instantiate of the prefab, destroyed the moment the player
        // scrolls to the next hotbar slot — so a cooldown living on the instance is a cooldown you
        // can skip by scrolling down and back up. It belongs in the slot, like the charge count.

        /// <summary>State key for the recharge. Written into save files — never rename.</summary>
        private const string CooldownKey = "staffCooldown";

        public override void CaptureItemState(ItemState state)
        {
            base.CaptureItemState(state);
            if (state == null) return;

            // Seconds remaining, never a deadline: Time.time restarts at zero each session, so a
            // stored deadline comes back either already spent or hours away.
            float remaining = Mathf.Max(0f, _cooldownEndsAt - Time.time);

            if (remaining > 0.01f) state.Set(CooldownKey, remaining);
        }

        public override void RestoreItemState(ItemState state)
        {
            base.RestoreItemState(state);

            float remaining = state == null ? 0f : state.GetFloat(CooldownKey, 0f);
            _cooldownEndsAt = remaining > 0f ? Time.time + remaining : 0f;
        }

        // ── The press: ignition ────────────────────────────────────────────────

        /// <summary>
        /// Owner-side, on the press: the aim the burn starts from.
        ///
        /// Sent even though hold ticks carry the same thing, because the first tick is a frame away
        /// and the arc is drawn on the press. Without it the first frame of every shot points
        /// wherever the staff happened to be lying.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            Transform aim = aimProvider != null ? aimProvider.AimTransform : null;
            if (aim == null) return;

            arg.P = aim.position;
            arg.R = aim.rotation;
        }

        /// <summary>
        /// Authority-side ignition. Nothing is spawned and nothing is billed here — the damage runs
        /// in <see cref="Update"/> for as long as the burn lasts — but the server has to start its
        /// own burn, because a dedicated server never receives <see cref="Present"/>.
        /// </summary>
        protected override void Use()
        {
            Ignite(UseArg);
            _pressLitTheArc = false;
        }

        /// <summary>Every machine's ignition, including the owner's, immediately.</summary>
        protected override void Present() => Ignite(UseArg);

        /// <summary>
        /// Light the arc, unless it is already lit or the staff is still recharging.
        ///
        /// Idempotent, because on a host both <see cref="Use"/> and <see cref="Present"/> run for
        /// the same press and a second ignition would restart the three seconds.
        /// </summary>
        private void Ignite(NetArg arg)
        {
            if (_lit || Time.time < _cooldownEndsAt) return;

            if (arg.HasOrientation)
            {
                _rayOrigin = arg.P;
                _rayDirection = arg.R * Vector3.forward;
            }

            _lit = true;
            _smoothedDirection = _rayDirection;
            _damageCarry = 0f;
            _damageTimer = 0f;
            _lastHoldTime = Time.time;
            _burnEndsAt = Time.time + burnDuration;
            _cooldownEndsAt = Time.time + burnDuration + cooldown;
            _nextStrikeTime = 0f;
            _pressLitTheArc = true;
        }

        /// <summary>
        /// Authority-side gate on the press.
        ///
        /// Charges are spent in <see cref="UsableItem.TryUse"/> whether or not the item does
        /// anything, so a limited-use staff whose button was mashed through its own cooldown would
        /// burn through its charges without ever firing.
        ///
        /// One condition, not two: <see cref="_cooldownEndsAt"/> already covers the burn. See its
        /// summary for why "is it burning?" must not be asked here.
        /// </summary>
        protected override bool CanUse() =>
            base.CanUse() && (_pressLitTheArc || Time.time >= _cooldownEndsAt);

        // ── Owner side: describe the aim ───────────────────────────────────────

        /// <summary>
        /// Owner-side, once per tick: put the aim ray in the message.
        ///
        /// Read straight off <see cref="AimProvider.AimTransform"/> rather than through
        /// <see cref="AimProvider.GetRayCast"/>, which logs a warning whenever the ray hits
        /// nothing. Aiming at open sky is a completely ordinary thing to do with a beam weapon,
        /// and at fifteen ticks a second it would bury the console.
        /// </summary>
        public override void OnRequestHold(ref NetArg arg, bool active)
        {
            if (!active) return;

            Transform aim = aimProvider != null ? aimProvider.AimTransform : null;
            if (aim == null) return;

            arg.P = aim.position;
            arg.R = aim.rotation;
        }

        // ── Authority side: the damage ─────────────────────────────────────────

        /// <summary>
        /// Server-side. Only records the aim — the damage runs in <see cref="Update"/>, because it
        /// ticks more than three times as often as the messages arrive.
        /// </summary>
        protected override void Hold(NetArg arg, bool active) => ApplyHold(arg, active);

        // ── Every machine: the beam ────────────────────────────────────────────

        protected override void PresentHold(NetArg arg, bool active) => ApplyHold(arg, active);

        /// <summary>
        /// Shared by both halves, and idempotent, because on a host both halves run.
        ///
        /// A dedicated server never receives <see cref="PresentHold"/> at all — it is not among
        /// the "others" its own broadcast goes to — so the aim has to be recorded on the authority
        /// path too, or the one machine that decides what got hit would be the one machine that
        /// does not know where the player is pointing.
        ///
        /// <para>
        /// A tick no longer LIGHTS anything: the press does that, in <see cref="Ignite"/>. This
        /// only steers. That is what makes the button unable to extend a burn — ticks keep arriving
        /// for as long as the finger is down, and once the three seconds are spent they steer
        /// nothing.
        /// </para>
        /// </summary>
        private void ApplyHold(NetArg arg, bool active)
        {
            if (!active)
            {
                Extinguish();
                return;
            }

            if (!_lit) return;

            _lastHoldTime = Time.time;

            if (arg.HasOrientation)
            {
                _rayOrigin = arg.P;
                _rayDirection = arg.R * Vector3.forward;
            }
        }

        private void Update()
        {
            // The burn is over when the staff says so, not when the player does.
            if (_lit && Time.time >= _burnEndsAt) Extinguish();

            // The safety net. A release is one message, and one message is exactly the kind of
            // thing that goes missing — along with the player who was holding the button. Without
            // this, that leaves a beam burning at full damage forever with nobody able to stop it.
            //
            // Still needed alongside the burn timer: this one catches a machine that stopped
            // hearing from the owner mid-burn, and puts the arc out where it is rather than letting
            // it sit for the remaining seconds pointing at a stale aim.
            if (_lit && Time.time - _lastHoldTime > holdTimeout) Extinguish();

            if (_lit) RefreshOwnerAim();

            _ignition = Mathf.MoveTowards(
                _ignition,
                _lit ? 1f : 0f,
                Time.deltaTime / Mathf.Max(0.001f, _lit ? igniteTime : fadeTime));

            if (_lit || _ignition > 0f) Trace();

            if (_lit && IsAuthority()) TickDamage();

            DrawBeam();
        }

        /// <summary>
        /// On the machine holding the camera, the aim is available right now and is better than
        /// anything that arrived over the wire. Everywhere else, ease toward the last tick.
        /// </summary>
        private void RefreshOwnerAim()
        {
            if (OwnerIsLocal())
            {
                Transform aim = aimProvider != null ? aimProvider.AimTransform : null;
                if (aim != null)
                {
                    _rayOrigin = aim.position;
                    _rayDirection = aim.forward;
                }

                _smoothedDirection = _rayDirection;
                return;
            }

            _smoothedDirection = Vector3.Slerp(
                _smoothedDirection,
                _rayDirection,
                1f - Mathf.Exp(-aimSmoothing * Time.deltaTime));
        }

        /// <summary>
        /// How many times the trace may step past the holder's own colliders before giving up.
        ///
        /// A body is a handful of colliders at most, and a bound is needed only so that a pathological
        /// case cannot spin here — it is not a budget anything normal is expected to spend.
        /// </summary>
        private const int MaxSelfSkips = 6;

        /// <summary>Nudge past a skipped hit, so the next trace does not re-find the same surface.</summary>
        private const float SkipEpsilon = 0.01f;

        /// <summary>
        /// Where the beam ends, and what is standing there.
        ///
        /// The complication is that the ray starts at the holder's own camera, inside the holder's
        /// own body, so the first thing it meets is very often the person firing — and a beam
        /// weapon that shoots its wielder is not a weapon.
        ///
        /// Solved by re-tracing from just past each of the holder's own hits rather than by
        /// gathering every hit and sorting. <see cref="Physics.RaycastNonAlloc"/> would have been
        /// the obvious way to do that, and it is quietly wrong here: it fills an UNSORTED buffer
        /// of bounded size, so down a 120 m beam through a busy scene it can return sixteen distant
        /// hits and omit the near one the beam should have stopped at. Asking for the nearest hit
        /// repeatedly keeps the engine's own ordering guarantee, and costs one extra cast per
        /// collider actually skipped — normally none, since the muzzle usually clears the body.
        /// </summary>
        private void Trace()
        {
            Vector3 direction = _smoothedDirection.sqrMagnitude > 1e-6f
                ? _smoothedDirection.normalized
                : transform.forward;

            _hitObject = null;
            _endPoint = _rayOrigin + direction * range;

            // Facing back down the beam. Only used when nothing is hit, and only so the impact rig
            // never holds a stale normal from the last surface it touched.
            _hitNormal = -direction;

            Vector3 origin = _rayOrigin;
            float remaining = range;

            for (int step = 0; step < MaxSelfSkips && remaining > 0f; step++)
            {
                if (!Physics.Raycast(origin, direction, out RaycastHit hit, remaining,
                                     hitMask, QueryTriggerInteraction.Ignore))
                    return;

                if (owner == null || !hit.collider.transform.IsChildOf(owner.transform))
                {
                    _endPoint = hit.point;
                    _hitNormal = hit.normal;
                    _hitObject = hit.collider.gameObject;
                    return;
                }

                origin = hit.point + direction * SkipEpsilon;
                remaining -= hit.distance + SkipEpsilon;
            }
        }

        /// <summary>
        /// Spend <see cref="damagePerSecond"/> in whole points, at the sampling rate.
        ///
        /// The loop is a while rather than an if so that a long frame pays what it owes instead of
        /// silently dropping ticks — otherwise the beam quietly does less damage on a machine that
        /// is struggling, which is the machine already having the worst time.
        /// </summary>
        private void TickDamage()
        {
            float step = 1f / Mathf.Max(1f, damageTicksPerSecond);

            _damageTimer += Time.deltaTime;

            while (_damageTimer >= step)
            {
                _damageTimer -= step;

                // Sweeping off a target stops the damage but keeps the fraction already earned,
                // so flicking across a crowd is not a way to lose every remainder.
                if (_hitObject == null) continue;

                _damageCarry += damagePerSecond * step;

                int whole = Mathf.FloorToInt(_damageCarry);
                if (whole <= 0) continue;

                _damageCarry -= whole;
                NetDamage.Apply(_hitObject, whole, transform);
            }
        }

        private void DrawBeam()
        {
            bool visible = _ignition > 0.001f;

            if (beam != null)
            {
                if (beam.enabled != visible) beam.enabled = visible;

                if (visible)
                {
                    Vector3 start = MuzzlePoint();
                    Vector3 end = Vector3.Lerp(start, _endPoint, _ignition);

                    beam.useWorldSpace = true;
                    BuildArc(start, end);

                    _beamProperties ??= new MaterialPropertyBlock();
                    beam.GetPropertyBlock(_beamProperties);
                    _beamProperties.SetFloat(IgniteId, _ignition);

                    // The shader scrolls its flow along the beam in metres rather than in UV, so
                    // the ripple keeps one speed whether the beam is a metre long or a hundred.
                    _beamProperties.SetFloat(BeamLengthId, Vector3.Distance(start, end));
                    beam.SetPropertyBlock(_beamProperties);
                }
            }

            DrawImpact(visible && _hitObject != null);
        }

        /// <summary>
        /// Lay the LineRenderer's points along the discharge path from <paramref name="start"/> to
        /// <paramref name="end"/>.
        ///
        /// <para>
        /// Two displacements, doing two different jobs. The QUANTISED one re-rolls
        /// <see cref="arcRestrikeRate"/> times a second and gives the bolt its kinks; the SMOOTH one
        /// drifts continuously and makes the whole channel sway. Either alone is wrong — quantised
        /// alone strobes in place like a fluorescent tube, smooth alone is a rope in the wind.
        /// </para>
        /// <para>
        /// Both are faded out at the ends by a sine envelope, which is what pins the arc to the
        /// muzzle and to the exact point <see cref="Trace"/> found. An arc whose ends wander is an
        /// arc that visibly misses the thing it is billing for damage.
        /// </para>
        /// </summary>
        private void BuildArc(Vector3 start, Vector3 end)
        {
            int segments = Mathf.Max(1, arcSegments);
            int points = segments + 1;

            if (_arcPoints == null || _arcPoints.Length != points) _arcPoints = new Vector3[points];

            Vector3 span = end - start;
            float length = span.magnitude;

            if (length < 1e-4f)
            {
                beam.positionCount = 2;
                beam.SetPosition(0, start);
                beam.SetPosition(1, end);
                return;
            }

            Vector3 forward = span / length;

            // Any two axes across the beam will do — the noise has no preferred direction — but
            // they have to be perpendicular to it, or the "sideways" wander would shorten and
            // lengthen the arc instead of bending it.
            Vector3 right = Vector3.Cross(forward, Mathf.Abs(forward.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
            Vector3 up = Vector3.Cross(forward, right);

            float amplitude = Mathf.Min(length * Mathf.Max(0f, arcSpread), Mathf.Max(0f, arcMaxOffset));
            float phase = Mathf.Floor(Time.time * Mathf.Max(1f, arcRestrikeRate));

            _arcPoints[0] = start;
            _arcPoints[points - 1] = end;

            for (int i = 1; i < points - 1; i++)
            {
                float t = (float)i / segments;
                float envelope = Mathf.Sin(t * Mathf.PI);

                float kinkR = Jitter(i * 1.37f + _arcSeed, phase);
                float kinkU = Jitter(i * 2.71f + _arcSeed + 19.3f, phase + 7.1f);

                float swayR = Mathf.PerlinNoise(_arcSeed + t * 2.2f, Time.time * 1.7f) * 2f - 1f;
                float swayU = Mathf.PerlinNoise(_arcSeed + 51.7f + t * 2.2f, Time.time * 1.4f) * 2f - 1f;

                Vector3 offset = right * (kinkR * 0.75f + swayR * 0.5f)
                               + up * (kinkU * 0.75f + swayU * 0.5f);

                _arcPoints[i] = start + span * t + offset * (amplitude * envelope);
            }

            beam.positionCount = points;
            beam.SetPositions(_arcPoints);
        }

        /// <summary>
        /// A hash rather than Perlin noise, and that is the whole point: it is DISCONTINUOUS. Feed
        /// it a quantised time and the bolt snaps to an unrelated shape instead of easing into a
        /// neighbouring one, which is the difference between lightning and a wobble.
        /// </summary>
        private static float Jitter(float a, float b)
        {
            float h = Mathf.Sin(a * 127.1f + b * 311.7f) * 43758.5453f;
            return (h - Mathf.Floor(h)) * 2f - 1f;
        }

        /// <summary>
        /// Everything that happens where the beam meets a surface: the glow, the sparks, the
        /// embers, the smoke and the light.
        ///
        /// <para>
        /// The rig is parented to one transform that is moved to the hit point and turned so its
        /// +Z is the surface normal. That is what makes the emitters believable — a spark cone
        /// aimed along a fixed axis sprays into the wall as often as out of it, and the difference
        /// between "sparks flying off a surface" and "sparks near a surface" is entirely whether
        /// they leave along the normal.
        /// </para>
        /// <para>
        /// Emitters are started and stopped on the LANDED EDGE rather than every frame. Calling
        /// Play on a system that is already playing restarts it, which would clear the sparks
        /// already in the air sixty times a second and leave a permanent stub of a spray.
        /// </para>
        /// </summary>
        private void DrawImpact(bool landed)
        {
            if (impactRoot != null && landed)
            {
                impactRoot.SetPositionAndRotation(
                    // Lifted a hair off the surface. The glow quad is coplanar with what it is
                    // burning otherwise, and coplanar geometry z-fights however the shader is
                    // configured — the quad's ZTest Always hides that for the quad but not for the
                    // particles, which do test depth and would sink halfway into the floor.
                    _endPoint + _hitNormal * 0.02f,
                    Quaternion.LookRotation(_hitNormal));
            }

            if (impactGlow != null)
            {
                if (impactGlow.gameObject.activeSelf != landed) impactGlow.gameObject.SetActive(landed);

                if (landed)
                {
                    if (impactRoot == null) impactGlow.position = _endPoint;

                    float breathe = 1f + glowPulse * Mathf.Sin(Time.time * 11f);
                    impactGlow.localScale = Vector3.one * (glowSize * _ignition * breathe);
                }
            }

            if (landed != _wasLanded)
            {
                SetEmitting(sparks, landed);
                SetEmitting(embers, landed);
                SetEmitting(smoke, landed);

                // The bite. Emitted only on the rising edge, and emitted rather than folded into
                // the rate curve, because the whole point is that it is not spread over time — a
                // beam meeting metal throws its biggest shower in the first instant and settles
                // into a steady spray afterwards.
                if (landed && sparks != null && biteBurst > 0) sparks.Emit(biteBurst);

                _wasLanded = landed;
            }

            // Gated on _lit rather than on `landed`, which stays true through the fade-out: a strike
            // that landed after the burn ended would read as a fourth shot the player did not take.
            if (_lit && landed && strikeVfx != null && Time.time >= _nextStrikeTime)
            {
                _nextStrikeTime = Time.time + Mathf.Max(0.05f, strikeInterval);
                SpawnStrike();
            }

            if (impactLight != null)
            {
                if (impactLight.enabled != landed) impactLight.enabled = landed;

                if (landed)
                {
                    if (impactRoot == null) impactLight.transform.position = _endPoint;

                    // Two incommensurable frequencies rather than one, so the flicker never settles
                    // into a rhythm the eye can predict and start reading as a pulse.
                    float flicker = 1f
                        + impactLightFlicker * 0.6f * Mathf.Sin(Time.time * 37f)
                        + impactLightFlicker * 0.4f * Mathf.Sin(Time.time * 61.7f);

                    impactLight.intensity = impactLightIntensity * _ignition * flicker;
                }
            }
        }

        /// <summary>
        /// Drop a lightning strike on whatever the arc is resting on.
        ///
        /// Purely cosmetic, and therefore run on every machine rather than on the authority: the
        /// damage is already being billed tick by tick in <see cref="TickDamage"/>, and spawning
        /// this on the server instead would mean nobody saw it.
        /// </summary>
        private void SpawnStrike()
        {
            // Posed exactly as LightningSpell poses it. The graph was authored striking downwards,
            // and the −90° about X is what stands it back up; it is the item's posing, not a units
            // fix, so it belongs at every call site rather than baked into the prefab.
            GameObject strike = Instantiate(strikeVfx, _endPoint + _hitNormal * 0.05f,
                                            Quaternion.Euler(90f, 0f, 0f));

            foreach (VisualEffect graph in strike.GetComponentsInChildren<VisualEffect>(true))
            {
                if (graph.HasVector4(LightningColorId)) graph.SetVector4(LightningColorId, strikeColor);
                if (graph.HasVector4(Color01Id)) graph.SetVector4(Color01Id, strikeColor);
            }

            // The graph is a one-shot burst and nothing else owns what it leaves behind.
            // LightningSpell leaks one of these per cast; an arc striking every 0.7 s would leak
            // several times faster, and they are not cheap objects.
            Destroy(strike, Mathf.Max(0.1f, strikeLifetime));
        }

        private static void SetEmitting(ParticleSystem system, bool on)
        {
            if (system == null) return;

            if (on)
            {
                system.Play(withChildren: true);
                return;
            }

            // Stop emitting but let what is already in the air finish its life. StopEmittingAndClear
            // would make every spark vanish the instant the beam swept off an edge, which looks
            // like a rendering glitch rather than like the beam moving on.
            system.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmitting);
        }

        private Vector3 MuzzlePoint() => muzzle != null ? muzzle.position : transform.position;

        /// <summary>
        /// Put the arc out.
        ///
        /// It does not touch the recharge, because <see cref="Ignite"/> already set it for the
        /// whole shot. That is also what makes this safe to call on an already-dark staff — and it
        /// is called that way constantly, from OnDisable, from OnUnequipped, and from every release
        /// tick that arrives after the burn has timed out. A recharge stamped here instead would be
        /// pushed ten seconds further away every time the player scrolled past the staff.
        ///
        /// A shot cut short — swapped away, or a stream that went quiet — is simply a shot wasted:
        /// the deadline stands where ignition put it, so the seconds the player did not get to
        /// spend are not handed back.
        /// </summary>
        private void Extinguish()
        {
            _lit = false;
            _pressLitTheArc = false;
            _hitObject = null;
            _damageCarry = 0f;
            _damageTimer = 0f;
        }

        /// <summary>
        /// Is this the machine that decides what the beam does? Offline, or the server.
        ///
        /// Asked of the OWNER rather than of this item. An equipped artifact is instantiated into
        /// a hand and never spawned, so its own NetworkObject is dormant and
        /// <see cref="Network.Simulates"/> would answer "yes, you simulate it" on every machine
        /// in the session — and the beam would bill its target once per player watching.
        /// </summary>
        private bool IsAuthority() =>
            !Network.IsNetworked || Network.Server;

        public override void OnUnequipped(GameObject holder)
        {
            base.OnUnequipped(holder);

            // Put it out AND clear the visual in the same breath. This object is usually destroyed
            // straight after, but it is also what a dropped staff becomes, and a discarded staff
            // lying in the sand still drawing a beam across the desert is the failure here.
            Extinguish();
            _ignition = 0f;
            DrawBeam();
        }

        private void OnDisable()
        {
            Extinguish();
            _ignition = 0f;
            DrawBeam();
        }

        private void OnValidate()
        {
            range = Mathf.Max(1f, range);
            damagePerSecond = Mathf.Max(0f, damagePerSecond);
            damageTicksPerSecond = Mathf.Clamp(damageTicksPerSecond, 1f, 200f);
            igniteTime = Mathf.Max(0.001f, igniteTime);
            fadeTime = Mathf.Max(0.001f, fadeTime);

            burnDuration = Mathf.Max(0.1f, burnDuration);
            cooldown = Mathf.Max(0f, cooldown);
            strikeInterval = Mathf.Max(0.05f, strikeInterval);
            strikeLifetime = Mathf.Max(0.1f, strikeLifetime);

            // Below about eight the displacement stops reading as lightning and starts reading as a
            // bent stick; the ceiling is a guard on rebuilding the whole line every frame.
            arcSegments = Mathf.Clamp(arcSegments, 8, 96);
            arcSpread = Mathf.Max(0f, arcSpread);
            arcMaxOffset = Mathf.Max(0f, arcMaxOffset);
            arcRestrikeRate = Mathf.Clamp(arcRestrikeRate, 1f, 120f);

            // A timeout inside the send interval would cut the beam between two perfectly ordinary
            // ticks, so it is floored well clear of it rather than left to whoever edits it.
            holdTimeout = Mathf.Max(0.2f, holdTimeout);
        }
    }
}
