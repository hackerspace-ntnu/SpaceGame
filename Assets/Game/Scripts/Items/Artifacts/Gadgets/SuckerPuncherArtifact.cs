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
    /// Tap Use to fire a steam ram down the gauntlet's rails. Whatever the fist connects with takes
    /// heavy damage and is launched the way the punch was travelling; everything else within
    /// <see cref="shockRadius"/> of the point of contact is shoved outward by the shockwave.
    ///
    /// <para>
    /// Deliberately instant, where the Repulsor Gauntlet charges. Two knockback tools that both ask
    /// the player to hold a button would be the same item twice (GDC-L1-BAL-0002); the punch's
    /// identity is that it costs no wind-up but demands you close to <see cref="reach"/> metres,
    /// and the repulsor's is that it reaches across a room but has to be paid for in advance.
    /// Acknowledging the press on the frame it arrives is also the whole feel of a punch
    /// (GDC-L1-FEEL-0002).
    /// </para>
    /// <para>
    /// <b>The shockwave only happens on contact.</b> A whiffed punch launches nobody. Otherwise the
    /// item is a no-aim area attack you can spam at the cooldown, which is the dominant strategy
    /// GDC-L1-BAL-0002 warns about — and it would make the repulsor pointless. Punching the ground
    /// still counts, because the ground is something you connected with: that is the ground-pound,
    /// and it is earned by aiming down instead of at a body.
    /// </para>
    /// <para>
    /// Physics is server-authoritative (<see cref="UseAuthority.Server"/>): loose bodies are pushed
    /// directly, players via <see cref="NetMsg.Flung"/> applied by their own machine
    /// (<see cref="FlungBody"/>, because a player's transform is owner-authoritative and a
    /// server-side shove is silently overwritten within a tick), and leap-capable mounts via
    /// <see cref="IMountLeapMotor"/>. Cosmetics — the ram, steam, ring, shake, hurt flinches, and
    /// the caster's own recoil — run per machine in <see cref="Present"/>.
    /// </para>
    /// <para>
    /// <b>The knockback is deliberately extreme.</b> A direct hit leaves at 34 m/s and the wave
    /// carries 7 m, which is well past what the numbers need to be to kill — the launch IS the
    /// point of the item, and a punch that merely nudges is the version nobody reaches for. What
    /// keeps it from dominating (GDC-L1-BAL-0002) is unchanged and load-bearing: it costs a
    /// 2.4 m approach, it does nothing at all on a whiff, and the cooldown is the whole price.
    /// Watch the recoil in particular — it is the same figure that sets the rocket-jump, so it
    /// buys traversal as well as feel.
    /// </para>
    /// <para>
    /// <b>Persistence:</b> nothing here is worth saving, and that is a decision rather than an
    /// oversight. The only runtime state is a sub-second cooldown and the ram's animation position,
    /// and both *should* come back reset — a fist restored mid-swing, or still cooling down from a
    /// punch thrown before a quit, would be a bug. The item's own existence, ownership and
    /// hotbar slot are carried by PickupableItem/SaveableEntity/ItemState like every artifact.
    /// </para>
    /// </summary>
    public class SuckerPuncherArtifact : ToolItem
    {
        public override UseAuthority Authority => UseAuthority.Server;

        private const int MissVerb = 0;
        private const int PunchVerb = 1;

        [Header("Reach")]
        [Tooltip("How far in front of the holder the fist lands, in metres.")]
        [SerializeField] private float reach = 2.4f;
        [Tooltip("Radius of the punch's sweep. This is aim forgiveness, not a bigger fist " +
                 "(GDC-L1-FEEL-0003): a punch that grazes past a shoulder should still connect.")]
        [SerializeField] private float punchRadius = 0.45f;
        [Tooltip("Punch origin height above the holder's feet.")]
        [SerializeField] private float punchOriginHeight = 1.35f;

        [Header("Direct hit")]
        [SerializeField] private int directDamage = 55;
        [Tooltip("Launch speed for whatever the fist actually lands on. Below ~9 m/s CarryMomentum " +
                 "self-cancels, so keep this well clear of it. This is the headline number of " +
                 "the item: a direct hit should read as being HIT BY A TRAIN, not shoved.")]
        [SerializeField] private float directFlingSpeed = 34f;
        [Tooltip("Upward tilt of the direct launch, degrees. Load-bearing: the vertical half is " +
                 "the one PlayerMovement never deletes.")]
        [SerializeField] private float directUpwardTilt = 26f;

        [Header("Shockwave")]
        [Tooltip("Radius of the wave off the point of contact.")]
        [SerializeField] private float shockRadius = 7f;
        [Tooltip("Damage to everything caught in the wave, excluding what was punched directly.")]
        [SerializeField] private int shockDamage = 15;
        [SerializeField] private float shockMinSpeed = 20f;
        [SerializeField] private float shockMaxSpeed = 28f;
        [SerializeField] private float shockUpwardTilt = 30f;
        [Tooltip("Fraction of the radius that takes undiminished force. Zero on purpose: this wave " +
                 "is centred on the point of CONTACT, so a body at the centre IS the body that was " +
                 "already punched, and falling off from there is the behaviour a punch wants. The " +
                 "repulsor's wave centres on the caster's own chest and needs a core instead.")]
        [SerializeField, Range(0f, 1f)] private float shockCoreFraction = 0f;
        [Tooltip("Launch strength at the edge of the wave relative to the centre.")]
        [SerializeField, Range(0f, 1f)] private float shockEdgeFalloff = 0.35f;
        [Tooltip("Impulse scaling reference for loose items: a body this heavy takes the full speed.")]
        [SerializeField] private float itemMassReference = 10f;

        [Header("Recoil")]
        [Tooltip("Backward speed handed to the puncher. Punching the ground while airborne is the " +
                 "traversal use, and it falls out of this rather than being a separate mode — so " +
                 "raising it lengthens the rocket-jump as much as it sells the punch.")]
        [SerializeField] private float recoilSpeed = 11f;
        [Tooltip("Upward tilt of the recoil, degrees. Load-bearing for the same reason as the " +
                 "other tilts: the vertical half is what PlayerMovement never deletes, so it is " +
                 "what keeps the shove alive into the next tick.")]
        [SerializeField] private float recoilUpwardTilt = 35f;

        [Header("Cadence")]
        [Tooltip("Seconds before the ram can be fired again. This is the whole cost of the item.")]
        [SerializeField] private float cooldownTime = 1f;

        [Header("Ram")]
        [Tooltip("The carriage, its frame, the knuckle block and the cylinder's piston rod. All " +
                 "four share one origin on the rail axis, so they slide together by the same " +
                 "offset. Assigned by the builder.")]
        [SerializeField] private Transform[] ramParts;
        [Tooltip("Slide direction in the ram parts' own parent space. The builder derives it from " +
                 "the prefab's forward rather than assuming the FBX importer's axis convention.")]
        [SerializeField] private Vector3 ramAxis = Vector3.forward;
        [Tooltip("Stroke, in metres. The rails' stop yoke is at 0.178 — past that the ram is " +
                 "drawn through its own frame.")]
        [SerializeField] private float ramThrow = 0.17f;
        [SerializeField] private float ramOutTime = 0.07f;
        [Tooltip("Seconds the ram holds at full extension on a connect, and only on a connect. " +
                 "This is hitstop (GDC-L1-FEEL-0005) done to the striking actor's geometry rather " +
                 "than to Time.timeScale, which on a host would stall the authoritative simulation " +
                 "for every other player. The camera, particles and the player's next input all " +
                 "keep running, which is what that principle asks for anyway.")]
        [SerializeField] private float ramHoldTime = 0.1f;
        [SerializeField] private float ramReturnTime = 0.55f;

        [Header("Presentation")]
        [Tooltip("Steam vented at the gland when the ram fires. Assigned by the builder.")]
        [SerializeField] private ParticleSystem steamBurst;
        [Tooltip("RepulsorShockwave-shader material for the ground ring — the same wave the " +
                 "repulsor draws, because it is the same event. Assigned by the builder.")]
        [SerializeField] private Material ringMaterial;
        [SerializeField] private float ringDuration = 0.3f;
        [SerializeField] private ShakeData punchShake;
        [Tooltip("Only cameras within this range of the impact shake.")]
        [SerializeField] private float shakeRadius = 18f;
        [SerializeField] private SfxId shockId = SfxId.ImpactExplosion;
        [SerializeField] private float fovKick = 5f;
        [SerializeField] private float fovKickDuration = 0.18f;

        // Presentation state — per machine, driven by Present.
        private float cooldownUntil;
        private float ramStart = float.NegativeInfinity;
        private float ramHold;
        private Vector3[] ramRest;
        private PlayerLook look;
        private float fovKickUntil = float.NegativeInfinity;
        private bool fovKickArmed;

        // Authority state — only meaningful on the server (or the single machine offline).
        //
        // Deliberately a SECOND clock rather than sharing `cooldownUntil`. On a host, Present runs
        // before Use for the same press; one shared clock would therefore be stamped by the
        // cosmetic half and then read by CanUse as "still cooling down", and the host's own punch
        // would silently never fire.
        private float authCooldownUntil;

        /// <summary>
        /// Owner, before the press leaves. The aim RAY travels, not the point it hits: every
        /// machine traces the same ray for itself, so the authority's damage sweep and each
        /// machine's ring provably land in the same place. Recomputing the aim on the receiving
        /// side instead would trace from the host's camera for every client.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            arg.B = Time.time >= cooldownUntil ? PunchVerb : MissVerb;
            if (arg.B != PunchVerb) return;

            Ray aim = aimProvider != null
                ? aimProvider.GetAimRay()
                : new Ray(transform.position, transform.forward);
            arg.P = aim.origin;
            arg.R = Quaternion.LookRotation(aim.direction);
        }

        protected override bool CanUse() => base.CanUse() && Time.time >= authCooldownUntil;

        /// <summary>Authority. The damage and the physics.</summary>
        protected override void Use()
        {
            if (UseArg.B != PunchVerb || owner == null) return;
            authCooldownUntil = Time.time + cooldownTime;

            Vector3 dir = UseArg.R * Vector3.forward;
            if (!TryTrace(dir, out RaycastHit hit)) return; // a whiff moves nothing

            GameObject ownerRoot = owner.transform.root.gameObject;
            GameObject struck = hit.transform.root.gameObject;
            var seen = new HashSet<GameObject> { ownerRoot, gameObject };

            // The direct hit travels the way the fist was going, not away from the impact point —
            // that difference is the whole reason a punch reads differently from a blast.
            if (seen.Add(struck))
            {
                Push(hit.collider, struck, RepulsorBlast.Launch(dir, directUpwardTilt, directFlingSpeed));
                if (directDamage > 0) NetDamage.Apply(struck, directDamage, owner.transform);
            }

            foreach (Collider caught in Physics.OverlapSphere(hit.point, shockRadius, ~0,
                                                             QueryTriggerInteraction.Ignore))
            {
                GameObject root = caught.transform.root.gameObject;
                if (!seen.Add(root)) continue; // the struck body already took the better hit

                Vector3 fling = RepulsorBlast.FlingVelocity(
                    hit.point, dir, caught.bounds.center, 1f, shockRadius,
                    shockMinSpeed, shockMaxSpeed, shockUpwardTilt, shockCoreFraction, shockEdgeFalloff);

                Push(caught, root, fling);
                if (shockDamage > 0) NetDamage.Apply(root, shockDamage, owner.transform);
            }
        }

        /// <summary>
        /// Every machine, and immediately on the puncher's so the fist never waits for a round
        /// trip. Layered on purpose — ram, steam, ring, sound, shake, flinch, FOV all land on the
        /// same frame (GDC-L1-FEEL-0004); one of them alone reads as a bug rather than a punch.
        /// </summary>
        protected override void Present()
        {
            if (UseArg.B != PunchVerb || owner == null) return;
            cooldownUntil = Time.time + cooldownTime;

            Vector3 dir = UseArg.R * Vector3.forward;
            bool connected = TryTrace(dir, out RaycastHit hit);

            // The ram fires whether or not it hit anything — the machine does not know yet, and a
            // punch that only animates on contact reads as an input the game ignored.
            FireRam(connected);
            if (steamBurst != null) steamBurst.Play();

            if (!connected) return;

            RepulsorBlastRing.Spawn(hit.point, shockRadius, ringDuration, ringMaterial);
            Sfx.Play(shockId, hit.point, default, GetInstanceID());

            if (punchShake != null && Camera.main != null &&
                (Camera.main.transform.position - hit.point).sqrMagnitude < shakeRadius * shakeRadius)
                CameraShakerHandler.Shake(punchShake);

            // Animator triggers do not replicate, so the flinch is raised per machine, off the same
            // sphere the authority swept. Same query, same exclusions, so the two agree.
            GameObject ownerRoot = owner.transform.root.gameObject;
            var seen = new HashSet<GameObject> { ownerRoot, gameObject };
            foreach (Collider caught in Physics.OverlapSphere(hit.point, shockRadius, ~0,
                                                             QueryTriggerInteraction.Ignore))
            {
                GameObject root = caught.transform.root.gameObject;
                if (!seen.Add(root)) continue;
                root.GetComponentInChildren<AgentAnimatorDriver>()?.TriggerHurt();
            }

            if (!OwnerIsLocal()) return;
            ApplyRecoil(dir);
            if (look != null && fovKick > 0f)
            {
                look.SetFovOffset(fovKick);
                fovKickUntil = Time.time + fovKickDuration;
                fovKickArmed = true;
            }
        }

        /// <summary>
        /// The punch sweep, run identically on every machine from the owner's reported ray.
        /// A sphere rather than a line: a fist is not a laser, and a hit that grazes past a
        /// shoulder should land (GDC-L1-FEEL-0003).
        /// </summary>
        private bool TryTrace(Vector3 dir, out RaycastHit hit)
        {
            hit = default;
            if (owner == null) return false;

            Vector3 origin = owner.transform.position + Vector3.up * punchOriginHeight;
            GameObject ownerRoot = owner.transform.root.gameObject;

            RaycastHit[] hits = Physics.SphereCastAll(origin, punchRadius, dir, reach, ~0,
                                                      QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;

            foreach (RaycastHit candidate in hits)
            {
                GameObject root = candidate.transform.root.gameObject;
                if (root == ownerRoot || root == gameObject) continue;
                if (candidate.distance >= best) continue;

                // A SphereCast that starts already overlapping reports distance 0 and a zero
                // normal, with `point` left at the origin. Using that point would drop the
                // shockwave inside the puncher's own chest, so it is replaced with the fist's
                // reach along the aim.
                best = candidate.distance;
                hit = candidate;
                if (candidate.distance <= 0f) hit.point = origin + dir * (reach * 0.5f);
                found = true;
            }

            return found;
        }

        /// <summary>
        /// The leap a creature caught by the punch is asked for.
        ///
        /// The floor is 2 m rather than 0: a creature at the very edge of the wave should still
        /// visibly hop, because on a punch the edge of the wave is still contact. The repulsor
        /// wants the opposite and uses a proportional leap — see <see cref="BlastPush.Leap"/>.
        /// </summary>
        private static readonly BlastPush.Leap PunchLeap =
            new BlastPush.Leap(2f, 6f, 1.2f, 1.2f, 0.45f);

        /// <summary>Clamp on the mass compensation handed to a loose Rigidbody.</summary>
        private static readonly Vector2 PunchMassScaleRange = new Vector2(0.2f, 1.5f);

        /// <summary>
        /// Hand `velocity` to whatever kind of thing this is. Three kinds, three routes — the
        /// split lives in <see cref="BlastPush"/> because the reasons are properties of the
        /// targets rather than of the weapon, and the repulsor and the dragon bazooka need the
        /// identical rules.
        /// </summary>
        private void Push(Collider collider, GameObject root, Vector3 velocity) =>
            BlastPush.Apply(collider, root, velocity, shockMaxSpeed, PunchLeap,
                            itemMassReference, PunchMassScaleRange);

        private void ApplyRecoil(Vector3 dir)
        {
            var movement = owner != null ? owner.GetComponent<PlayerMovement>() : null;
            var body = owner != null ? owner.GetComponent<Rigidbody>() : null;
            if (movement == null || body == null) return;

            movement.EnsureMovableBody();
            if (body.isKinematic) return;

            body.linearVelocity += RepulsorBlast.Launch(-dir, recoilUpwardTilt, recoilSpeed);
            // Without the latch, air control lerps the horizontal half back to walk speed in ~0.2 s.
            movement.CarryMomentum();
        }

        // ── The ram ────────────────────────────────────────────────────────────

        private void FireRam(bool connected)
        {
            CaptureRamRest();
            ramHold = connected ? ramHoldTime : 0f;
            ramStart = Time.time;
        }

        /// <summary>
        /// Record where the ram sits at rest, once. Read from the transforms rather than assumed to
        /// be zero: the parts are children of the imported model, so their rest positions are
        /// whatever the FBX put there.
        /// </summary>
        private void CaptureRamRest()
        {
            if (ramRest != null || ramParts == null) return;

            ramRest = new Vector3[ramParts.Length];
            for (int i = 0; i < ramParts.Length; i++)
                if (ramParts[i] != null) ramRest[i] = ramParts[i].localPosition;
        }

        private void SetRamOffset(float distance)
        {
            if (ramParts == null || ramRest == null) return;

            Vector3 step = ramAxis.normalized * distance;
            for (int i = 0; i < ramParts.Length; i++)
                if (ramParts[i] != null) ramParts[i].localPosition = ramRest[i] + step;
        }

        private void Update()
        {
            TickRam();

            if (fovKickArmed && Time.time >= fovKickUntil)
            {
                fovKickArmed = false;
                if (look != null) look.SetFovOffset(0f);
            }
        }

        private void TickRam()
        {
            if (float.IsNegativeInfinity(ramStart)) return;

            float t = Time.time - ramStart;
            float outEnd = ramOutTime;
            float holdEnd = outEnd + ramHold;
            float total = holdEnd + ramReturnTime;

            if (t >= total)
            {
                ramStart = float.NegativeInfinity;
                SetRamOffset(0f);
                return;
            }

            float offset;
            if (t < outEnd)
            {
                // Fast out: nearly all the travel in the first third of the stroke.
                float u = Mathf.Clamp01(t / Mathf.Max(outEnd, 0.001f));
                offset = ramThrow * (1f - (1f - u) * (1f - u) * (1f - u));
            }
            else if (t < holdEnd)
            {
                offset = ramThrow;
            }
            else
            {
                // Slow back, under spring — the concept sheet's "frame slowly moves back".
                float u = Mathf.Clamp01((t - holdEnd) / Mathf.Max(ramReturnTime, 0.001f));
                offset = ramThrow * (1f - u * u);
            }

            SetRamOffset(offset);
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);
            look = holder != null ? holder.GetComponent<PlayerLook>() : null;
            CaptureRamRest();
            SetRamOffset(0f);
        }

        public override void OnUnequipped(GameObject holder)
        {
            // SetFovOffset's contract (PlayerLook.cs) is that whoever sets it must clear it.
            // Unequipping mid-kick otherwise leaves the camera permanently pulled out, because
            // Update stops ticking and the reset in it never runs.
            if (fovKickArmed && look != null) look.SetFovOffset(0f);
            fovKickArmed = false;
            look = null;

            ramStart = float.NegativeInfinity;
            SetRamOffset(0f);

            base.OnUnequipped(holder);
        }

        private void OnValidate()
        {
            reach = Mathf.Max(0.5f, reach);
            shockMaxSpeed = Mathf.Max(shockMaxSpeed, shockMinSpeed);
            ramOutTime = Mathf.Max(0.01f, ramOutTime);
            ramReturnTime = Mathf.Max(0.01f, ramReturnTime);
        }
    }
}
