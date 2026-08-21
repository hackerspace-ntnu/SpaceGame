using Unity.Netcode;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    /// <summary>
    /// A staff that emits a solid beam for as long as the use button is held, cutting whatever it
    /// rests on.
    ///
    /// It is the first item in the game that acts continuously, and it does that through the same
    /// request/authority/present split every artifact already uses — see
    /// <see cref="UsableItem.IsContinuous"/>. The only thing it adds is that the split runs many
    /// times instead of once.
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
        /// <summary>The beam burns for as long as the button is down. See the class summary.</summary>
        public override bool IsContinuous => true;

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

        [Tooltip("The beam itself. Two points, muzzle to impact, drawn with the LaserBeam shader.")]
        [SerializeField] private LineRenderer beam;

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
        /// </summary>
        private void ApplyHold(NetArg arg, bool active)
        {
            if (!active)
            {
                Extinguish();
                return;
            }

            _lastHoldTime = Time.time;

            if (arg.HasOrientation)
            {
                _rayOrigin = arg.P;
                _rayDirection = arg.R * Vector3.forward;
            }

            if (!_lit)
            {
                _lit = true;
                _smoothedDirection = _rayDirection;
                _damageCarry = 0f;
                _damageTimer = 0f;
            }
        }

        private void Update()
        {
            // The safety net. A release is one message, and one message is exactly the kind of
            // thing that goes missing — along with the player who was holding the button. Without
            // this, that leaves a beam burning at full damage forever with nobody able to stop it.
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
                    beam.positionCount = 2;
                    beam.SetPosition(0, start);
                    beam.SetPosition(1, end);

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

        private void Extinguish()
        {
            _lit = false;
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

        /// <summary>True when the local player is the one holding this staff.</summary>
        private bool OwnerIsLocal()
        {
            if (!Network.IsNetworked) return true;

            if (owner != null && owner.TryGetComponent(out NetworkObject netObj) && netObj.IsSpawned)
                return netObj.IsOwner;

            return true;
        }

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

            // A timeout inside the send interval would cut the beam between two perfectly ordinary
            // ticks, so it is floored well clear of it rather than left to whoever edits it.
            holdTimeout = Mathf.Max(0.2f, holdTimeout);
        }
    }
}
