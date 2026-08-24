using System.Collections.Generic;
using FirstGearGames.SmoothCameraShaker;
using SpaceGame.Agents;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Hold Use to charge, release to fire a cone force blast. Charge follows the Lasso pattern:
    /// it never travels — every machine saw the press and runs its own clock; the authority prices
    /// the blast off its own elapsed time. The release rides the final hold tick with the aim in
    /// P/R; a release with no orientation (the default NetArg EquipmentController sends on
    /// unequip/death) is a CANCEL, never a fire.
    ///
    /// Physics is server-authoritative (Authority=Server): loose bodies are pushed directly,
    /// players via NetMsg.Flung applied by their own machine (FlungBody), leap-capable mounts via
    /// IMountLeapMotor. Cosmetics (ring, sound, hurt flinches, recoil on the caster) run per
    /// machine in Present/PresentHold.
    /// </summary>
    public class RepulsorGauntletArtifact : ToolItem
    {
        public override UseAuthority Authority => UseAuthority.Server;
        public override bool IsContinuous => true; // press opens the hold stream; release fires

        private const int MissVerb = 0;
        private const int ChargeVerb = 1;

        [Header("Charge")]
        [Tooltip("Seconds of hold for a full-power blast.")]
        [SerializeField] private float chargeTime = 1.2f;
        [Tooltip("Charge floor — a tap still fires this fraction of full power.")]
        [SerializeField, Range(0f, 1f)] private float minCharge = 0.25f;
        [Tooltip("Seconds after a blast before the gauntlet can charge again.")]
        [SerializeField] private float cooldownTime = 2.5f;
        [Tooltip("Cancel a charge if no hold tick arrives for this long (dropped release, disconnect). Must exceed the 1/15 s hold send interval.")]
        [SerializeField] private float holdTimeout = 0.5f;

        [Header("Blast")]
        [SerializeField] private float minRange = 6f;
        [SerializeField] private float maxRange = 13f;
        [Tooltip("Full cone angle, degrees.")]
        [SerializeField, Range(10f, 180f)] private float blastAngle = 75f;
        [Tooltip("Fling speed at min charge. Below ~9 m/s CarryMomentum self-cancels, so keep >= 12.")]
        [SerializeField] private float minFlingSpeed = 12f;
        [SerializeField] private float maxFlingSpeed = 22f;
        [Tooltip("Upward tilt of every fling, degrees. Load-bearing: vertical velocity is the half " +
                 "PlayerMovement never deletes, and while the victim is still RISING it holds on " +
                 "to the horizontal half too (PlayerMovement.ShouldEndCarry). Too small a tilt " +
                 "and the fling is deleted on the tick it is applied.")]
        [SerializeField] private float upwardTilt = 27f;
        [Tooltip("Blast origin height above the holder's feet.")]
        [SerializeField] private float blastOriginHeight = 1.2f;
        [Tooltip("Damage per body caught in the blast. 0 = pure force.")]
        [SerializeField] private int blastDamage = 0;
        [Tooltip("Impulse scaling reference for loose items: a body this heavy takes the full fling speed.")]
        [SerializeField] private float itemMassReference = 10f;
        [Tooltip("Fling strength at the cone edge relative to point-blank — an edge hit is a puff, not a launch.")]
        [SerializeField, Range(0f, 1f)] private float edgeFalloff = 0.35f;

        [Header("Recoil (the caster)")]
        [Tooltip("Backward speed handed to the caster at full charge. Full-charge airborne recoil is the repulsor-jump.")]
        [SerializeField] private float recoilSpeed = 12f;
        [Tooltip("Upward fraction mixed into the recoil direction. Load-bearing like upwardTilt: " +
                 "the vertical half is what PlayerMovement never deletes, and the rise it produces " +
                 "is what keeps the backward half from being deleted on the next movement tick.")]
        [SerializeField] private float recoilUpwardBias = 0.35f;

        [Header("Mount leap (kinematic agents that support it)")]
        [SerializeField] private float leapDistanceMin = 2f;
        [SerializeField] private float leapDistanceMax = 6f;
        [SerializeField] private float leapHeight = 1.2f;
        [SerializeField] private float leapDuration = 0.45f;

        [Header("Presentation")]
        [Tooltip("Child scaled up while charging. Assigned by the builder.")]
        [SerializeField] private Transform chargeGlow;
        [Tooltip("Charge glow scale at min and max charge.")]
        [SerializeField] private Vector2 chargeGlowScale = new Vector2(0.03f, 0.14f);
        [Tooltip("RepulsorShockwave-shader material for the ground ring. Assigned by the builder.")]
        [SerializeField] private Material ringMaterial;
        [SerializeField] private float ringDuration = 0.35f;
        [SerializeField] private ShakeData blastShake;
        [Tooltip("Only cameras within this range of the blast shake.")]
        [SerializeField] private float shakeRadius = 20f;
        [SerializeField] private SfxId chargeLoopId = SfxId.WeaponEnergyChargeLoop;
        [SerializeField] private SfxId blastId = SfxId.ImpactExplosion;
        [Tooltip("FOV pull-in (degrees) at full charge — anticipation. Positive number, applied negative.")]
        [SerializeField] private float chargeFovPull = 4f;
        [SerializeField] private float blastFovKick = 6f;
        [SerializeField] private float blastFovKickDuration = 0.2f;

        // Presentation state — per machine, driven by Present/PresentHold.
        private bool charging;
        private float chargeStart;
        private float lastHoldTime;
        private float cooldownUntil;

        // Authority state — only meaningful on the server (or the single machine offline).
        private bool authCharging;
        private float authChargeStart;
        private float authLastHoldTime;

        private readonly LoopingEmitter chargeLoop = new LoopingEmitter();
        private PlayerLook look;
        private float fovKickUntil = float.NegativeInfinity;
        private bool fovKickArmed;

        private float LocalCharge
            => RepulsorBlast.ChargeFrom(Time.time - chargeStart, chargeTime, minCharge);

        /// <summary>Owner, before the press message leaves. CanUse here is the cooldown refusing.</summary>
        public override void OnRequestUse(ref NetArg arg)
            => arg.B = CanUse() ? ChargeVerb : MissVerb;

        protected override bool CanUse() => base.CanUse() && Time.time >= cooldownUntil;

        /// <summary>Authority. Starts the pricing clock for this blast.</summary>
        protected override void Use()
        {
            if (UseArg.B != ChargeVerb) return;
            authCharging = true;
            authChargeStart = Time.time;
            authLastHoldTime = Time.time;
        }

        /// <summary>Every machine. Starts the cosmetic charge — glow, loop, FOV pull.</summary>
        protected override void Present()
        {
            if (UseArg.B != ChargeVerb || charging) return;
            charging = true;
            chargeStart = Time.time;
            lastHoldTime = Time.time;
            chargeLoop.Play(chargeLoopId, gameObject);
            if (chargeGlow != null) chargeGlow.gameObject.SetActive(true);
        }

        /// <summary>Owner, every tick incl. the release tick. Aim only matters on release.</summary>
        public override void OnRequestHold(ref NetArg arg, bool active)
        {
            if (active || !charging) return;
            Ray aim = aimProvider != null ? aimProvider.GetAimRay() : new Ray(transform.position, transform.forward);
            arg.P = aim.origin;
            arg.R = Quaternion.LookRotation(aim.direction);
        }

        /// <summary>Authority. Keep-alive while held; the blast physics on release.</summary>
        protected override void Hold(NetArg arg, bool active)
        {
            if (active) { authLastHoldTime = Time.time; return; }
            if (!authCharging) return;
            authCharging = false;
            if (!arg.HasOrientation) return; // default NetArg = unequip/death = cancel

            float charge = RepulsorBlast.ChargeFrom(Time.time - authChargeStart, chargeTime, minCharge);
            FireBlast(arg.R * Vector3.forward, charge);
        }

        /// <summary>Every machine. Cosmetic release — or cancel.</summary>
        protected override void PresentHold(NetArg arg, bool active)
        {
            if (active) { lastHoldTime = Time.time; return; }
            if (!charging) return;

            float charge = LocalCharge;
            EndChargePresentation();

            if (!arg.HasOrientation) return; // cancel: glow and loop already stopped, no blast

            cooldownUntil = Time.time + cooldownTime;
            PlayBlastFx(arg.R * Vector3.forward, charge);
            if (OwnerIsLocal()) ApplyRecoil(arg.R * Vector3.forward, charge);
        }

        private void FireBlast(Vector3 dir, float charge)
        {
            if (owner == null) return;
            Vector3 origin = owner.transform.position + Vector3.up * blastOriginHeight;
            float radius = Mathf.Lerp(minRange, maxRange, charge);
            // The gauntlet itself needs no exclusion of its own: while equipped it is parented into
            // the holder, so its root IS ownerRoot.
            GameObject ownerRoot = owner.transform.root.gameObject;
            var seen = new HashSet<GameObject>();

            foreach (Collider hit in Physics.OverlapSphere(origin, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                GameObject root = hit.transform.root.gameObject;
                if (root == ownerRoot || !seen.Add(root)) continue;

                Vector3 targetPos = hit.bounds.center;
                if (!RepulsorBlast.InCone(origin, dir, targetPos, radius, blastAngle * 0.5f)) continue;

                Vector3 fling = RepulsorBlast.FlingVelocity(origin, dir, targetPos, charge,
                    radius, minFlingSpeed, maxFlingSpeed, upwardTilt, edgeFalloff);

                if (root.GetComponent<PlayerMovement>() != null)
                {
                    // Owner-authoritative body: the victim's own machine applies it (FlungBody).
                    NetMessaging.NetSendTo(root, NetMsg.Flung, new NetArg { P = fling }, NetTo.All);
                }
                else if (root.GetComponentInChildren<AgentController>() != null)
                {
                    // Kinematic, motor-owned transform — forces never land. Leap if the motor can;
                    // otherwise the cosmetic sweep's hurt flinch is all v1 gives (deferred by spec).
                    var leaper = root.GetComponentInChildren<IMountLeapMotor>();
                    if (leaper != null && leaper.IsLeapAvailable)
                    {
                        float falloff = fling.magnitude / Mathf.Max(maxFlingSpeed, 0.01f);
                        Vector3 away = Vector3.ProjectOnPlane(fling, Vector3.up).normalized;
                        leaper.RequestLeap(away,
                            Mathf.Lerp(leapDistanceMin, leapDistanceMax, charge) * falloff,
                            leapHeight, leapDuration);
                    }
                }
                else
                {
                    Rigidbody body = hit.attachedRigidbody;
                    if (body == null) continue;
                    if (body.isKinematic)
                    {
                        // Only un-kinematic a body this machine simulates — a kinematic replica is
                        // kinematic on purpose (the LassoTether guard).
                        if (!Network.Simulates(body)) continue;
                        body.isKinematic = false;
                    }
                    float massScale = Mathf.Clamp(itemMassReference / Mathf.Max(body.mass, 0.1f), 0.2f, 1.5f);
                    body.AddForce(fling * massScale, ForceMode.VelocityChange);
                }

                if (blastDamage > 0)
                    NetDamage.Apply(root, blastDamage, owner.transform);
            }
        }

        private void PlayBlastFx(Vector3 dir, float charge)
        {
            if (owner == null) return;
            Vector3 feet = owner.transform.position + Vector3.up * 0.1f;
            Vector3 origin = owner.transform.position + Vector3.up * blastOriginHeight;
            float radius = Mathf.Lerp(minRange, maxRange, charge);

            RepulsorBlastRing.Spawn(feet, radius, ringDuration, ringMaterial);
            Sfx.Play(blastId, origin, default, GetInstanceID());

            if (blastShake != null && Camera.main != null &&
                (Camera.main.transform.position - origin).sqrMagnitude < shakeRadius * shakeRadius)
                CameraShakerHandler.Shake(blastShake);

            // Cosmetic hurt flinch on agents in the cone — run per machine because animator
            // triggers do not replicate. Same cone math and exclusions as the authority sweep in
            // FireBlast, so the two provably agree.
            GameObject ownerRoot = owner.transform.root.gameObject;
            var seen = new HashSet<GameObject>();
            foreach (Collider hit in Physics.OverlapSphere(origin, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                GameObject root = hit.transform.root.gameObject;
                if (root == ownerRoot || !seen.Add(root)) continue;
                if (!RepulsorBlast.InCone(origin, dir, hit.bounds.center, radius, blastAngle * 0.5f)) continue;
                root.GetComponentInChildren<AgentAnimatorDriver>()?.TriggerHurt();
            }

            if (OwnerIsLocal() && look != null && blastFovKick > 0f)
            {
                look.SetFovOffset(blastFovKick);
                fovKickUntil = Time.time + blastFovKickDuration;
                fovKickArmed = true;
            }
        }

        private void ApplyRecoil(Vector3 dir, float charge)
        {
            var movement = owner != null ? owner.GetComponent<PlayerMovement>() : null;
            var body = owner != null ? owner.GetComponent<Rigidbody>() : null;
            if (movement == null || body == null) return;

            Vector3 back = (-Vector3.ProjectOnPlane(dir, Vector3.up).normalized + Vector3.up * recoilUpwardBias).normalized;
            movement.EnsureMovableBody();
            if (body.isKinematic) return;
            body.linearVelocity += back * (recoilSpeed * charge);
            movement.CarryMomentum();
        }

        private void EndChargePresentation()
        {
            charging = false;
            chargeLoop.Stop();
            if (chargeGlow != null) chargeGlow.gameObject.SetActive(false);
            if (OwnerIsLocal() && look != null && !fovKickArmed) look.SetFovOffset(0f);
        }

        private void Update()
        {
            if (charging && Time.time - lastHoldTime > holdTimeout) EndChargePresentation();
            if (authCharging && Time.time - authLastHoldTime > holdTimeout) authCharging = false;

            if (charging)
            {
                if (chargeGlow != null)
                    chargeGlow.localScale = Vector3.one * Mathf.Lerp(chargeGlowScale.x, chargeGlowScale.y, LocalCharge);
                if (OwnerIsLocal() && look != null)
                    look.SetFovOffset(-chargeFovPull * LocalCharge);
            }

            if (fovKickArmed && Time.time >= fovKickUntil)
            {
                fovKickArmed = false;
                if (look != null) look.SetFovOffset(0f);
            }
        }

        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);
            look = holder != null ? holder.GetComponent<PlayerLook>() : null;
            if (chargeGlow != null) chargeGlow.gameObject.SetActive(false);
        }

        public override void OnUnequipped(GameObject holder)
        {
            // EndHold(send:false) has already delivered the default-NetArg cancel; this is the
            // belt-and-braces sweep for the FOV and the loop (the grapple's documented reset trap).
            EndChargePresentation();
            authCharging = false;
            if (look != null) look.SetFovOffset(0f);
            look = null;
            base.OnUnequipped(holder);
        }

        private void OnDisable()
        {
            chargeLoop.Stop(false);
        }

        private void OnValidate()
        {
            holdTimeout = Mathf.Max(0.2f, holdTimeout); // must exceed the 1/15 s hold interval
            maxRange = Mathf.Max(maxRange, minRange);
            maxFlingSpeed = Mathf.Max(maxFlingSpeed, minFlingSpeed);
        }
    }
}
