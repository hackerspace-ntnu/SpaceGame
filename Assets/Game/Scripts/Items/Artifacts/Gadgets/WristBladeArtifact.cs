using System.Collections.Generic;
using SpaceGame.Agents;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Tap Use and a blade snaps out of the sheath on the forearm, over the back of the hand, and
    /// whatever it meets within <see cref="reach"/> takes <see cref="damage"/>. It holds for a
    /// beat and slides back in.
    ///
    /// <para>
    /// This is the quiet cousin of the Sucker Puncher, and it is deliberately not that item with
    /// smaller numbers (GDC-L1-BAL-0002): the punch launches and has a shockwave, the blade does
    /// neither. It only cuts — no push, no wave, no recoil — and it cuts hard: one clean hit is
    /// most of a Clanker. What it costs is reach (2.9 m, the length of an arm and a blade) and
    /// the cooldown; what it gives back is that a whiff costs nothing but the cooldown, so the
    /// blade rewards closing and committing where the punch rewards positioning.
    /// </para>
    /// <para>
    /// The arm moves on the frame the press arrives, whether or not it will hit anything
    /// (GDC-L1-FEEL-0002): the lunge IS the acknowledgement. The steel follows once the arm has
    /// locked, and <b>the cut lands with the steel</b>, not with the press: the trace runs on a
    /// timer, <see cref="cutAtSlide"/> of the way through the slide, so what the blade bites is
    /// what it is visibly reaching into. That is half a second of latency on the damage, chosen
    /// with eyes open: a cut that landed before the blade left the sheath read as the item
    /// hitting with nothing. Damage is server-authoritative (<see cref="UseAuthority.Server"/>);
    /// the aim RAY travels in the message, and every machine traces it for itself, at the same
    /// moment, so the authority's cut and each machine's hit effects land in one place.
    /// </para>
    /// <para>
    /// <b>Persistence:</b> nothing, as a decision. The only runtime state is a sub-second cooldown
    /// and the blade's slide, and a blade restored half out of its sheath would be a bug.
    /// </para>
    /// </summary>
    public class WristBladeArtifact : ToolItem
    {
        public override UseAuthority Authority => UseAuthority.Server;

        private const int MissVerb = 0;
        private const int CutVerb = 1;

        [Header("Reach")]
        [Tooltip("How far in front of the holder the blade reaches, in metres.")]
        [SerializeField] private float reach = 2.9f;
        [Tooltip("Radius of the cut's sweep — aim forgiveness (GDC-L1-FEEL-0003), not a wider blade.")]
        [SerializeField] private float sweepRadius = 0.35f;
        [Tooltip("Cut origin height above the holder's feet.")]
        [SerializeField] private float originHeight = 1.35f;

        [Header("Cut")]
        [Tooltip("Damage to the first thing the blade meets. The headline number: fifty is most of a Clanker.")]
        [SerializeField] private int damage = 50;
        [Tooltip("Seconds before the blade can fire again. This is the whole cost of the item. " +
                 "The blade's whole cycle (wait, slide, hold, return) is about 1.2 s; a shorter " +
                 "cooldown lets a second press restart the steel mid-return.")]
        [SerializeField] private float cooldownTime = 1.0f;
        [Tooltip("How far through the slide the cut lands, 0..1. The steel has to be out before it " +
                 "can bite; 1 is the moment it locks at full stroke.")]
        [SerializeField, Range(0f, 1f)] private float cutAtSlide = 0.7f;

        [Header("Blade")]
        [Tooltip("The blade object(s), all sharing one origin at the sheath's tang so they slide by one offset. Assigned by the builder.")]
        [SerializeField] private Transform[] bladeParts;
        [Tooltip("Slide direction in the blade's parent space. Derived by the builder from the prefab's forward.")]
        [SerializeField] private Vector3 bladeAxis = Vector3.forward;
        [Tooltip("Stroke, in metres: the blade's own length, so it clears the sheath fully and no more.")]
        [SerializeField] private float bladeThrow = 0.55f;
        [Tooltip("Seconds after the press before the steel starts to move: the arm's chamber and " +
                 "thrust (0.37 s) plus a beat, so the blade leaves a LOCKED arm and the eye is " +
                 "already on the wrist when it does.")]
        [SerializeField] private float bladeDelay = 0.40f;
        [Tooltip("Seconds the blade takes to slide out. Slow enough to be seen extending; the " +
                 "gesture holds the arm out for it.")]
        [SerializeField] private float bladeOutTime = 0.20f;
        [Tooltip("Seconds the blade stays out on a connect: hitstop done to the geometry (GDC-L1-FEEL-0005).")]
        [SerializeField] private float bladeHoldOnHit = 0.25f;
        [Tooltip("Seconds it stays out on a whiff, so a miss still reads as a full thrust.")]
        [SerializeField] private float bladeHoldOnMiss = 0.15f;
        [SerializeField] private float bladeReturnTime = 0.35f;

        [Header("Presentation")]
        [Tooltip("Sparks at the sheath's mouth as the blade comes out: played on the frame the steel " +
                 "starts to move, not on the press. Assigned by the builder.")]
        [SerializeField] private ParticleSystem mouthSparks;
        [SerializeField] private SfxId hitId = SfxId.WeaponMeleeImpact;
        [Tooltip("Trigger on the wearer's animator for the arm's thrust, played on the Upper Body " +
                 "layer through PlayerAimRig so the layer is actually up while it runs. Built by " +
                 "Tools > SpaceGame > Player > Build Gestures. Empty for no gesture.")]
        [SerializeField] private string stabTrigger = "Stab";
        [Tooltip("Seconds the gesture holds the arm layer up: the clip's length.")]
        [SerializeField] private float stabSeconds = 1.2f;

        // Presentation state — per machine, driven by Present.
        private float cooldownUntil;
        private float bladeStart = float.NegativeInfinity;
        private float bladeHold;
        private bool sparked;
        private Vector3[] bladeRest;
        private PlayerAimRig aimRig;
        // The presentation cut, pending: where the blade is going and when it gets there.
        private Vector3 cutDir;
        private float cutAt = float.PositiveInfinity;

        // Authority state — a second clock on purpose. On a host, Present runs before Use for the
        // same press; one shared clock would be stamped by the cosmetic half and then read by
        // CanUse as "still cooling down", and the host's own cut would never land.
        private float authCooldownUntil;
        private Vector3 authCutDir;
        private float authCutAt = float.PositiveInfinity;

        /// <summary>
        /// Owner, before the press leaves. The aim ray travels, not the point it hits, so every
        /// machine traces the same ray rather than re-aiming from the host's camera.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            arg.B = Time.time >= cooldownUntil ? CutVerb : MissVerb;
            if (arg.B != CutVerb) return;

            Ray aim = aimProvider != null
                ? aimProvider.GetAimRay()
                : new Ray(transform.position, transform.forward);
            arg.P = aim.origin;
            arg.R = Quaternion.LookRotation(aim.direction);
        }

        protected override bool CanUse() => base.CanUse() && Time.time >= authCooldownUntil;

        /// <summary>Authority. Arms the cut; <see cref="TickCuts"/> lands it when the steel is out.</summary>
        protected override void Use()
        {
            if (UseArg.B != CutVerb || owner == null) return;
            authCooldownUntil = Time.time + cooldownTime;

            authCutDir = UseArg.R * Vector3.forward;
            authCutAt = Time.time + CutDelay;
        }

        /// <summary>Seconds from the press to the cut: the arm's beat, then most of the slide.</summary>
        private float CutDelay => bladeDelay + bladeOutTime * cutAtSlide;

        /// <summary>
        /// Every machine, immediately on the wearer's. The blade fires whether or not it connects —
        /// the machine does not know yet, and a blade that only moves on contact reads as an
        /// input the game ignored.
        /// </summary>
        protected override void Present()
        {
            if (UseArg.B != CutVerb || owner == null) return;
            cooldownUntil = Time.time + cooldownTime;

            cutDir = UseArg.R * Vector3.forward;
            cutAt = Time.time + CutDelay;
            FireBlade();
            // The arm thrusts on every machine, like the blade: a peer watching sees the stab,
            // and on the wearer's own screen it is what brings the blade into view. On the arm
            // the blade is worn on, whatever the other arm carries.
            if (aimRig != null) aimRig.PlayGesture(stabTrigger, stabSeconds, WornOn);
        }

        /// <summary>
        /// The two cuts, each on its own clock: the authority's, which deals the damage, and this
        /// machine's, which decides the hold and plays the hit. On a host both fire in the same
        /// frame from the same ray, so they agree; on a client only the second exists.
        /// </summary>
        private void TickCuts()
        {
            if (Time.time >= authCutAt)
            {
                authCutAt = float.PositiveInfinity;
                if (owner != null && damage > 0 && TryTrace(authCutDir, out RaycastHit cut))
                    NetDamage.Apply(cut.transform.root.gameObject, damage, owner.transform);
            }

            if (Time.time >= cutAt)
            {
                cutAt = float.PositiveInfinity;
                bool connected = TryTrace(cutDir, out RaycastHit hit);
                // Hitstop done to the geometry: a connect holds the steel out longer than a whiff.
                bladeHold = connected ? bladeHoldOnHit : bladeHoldOnMiss;
                if (!connected) return;

                Sfx.Play(hitId, hit.point, default, GetInstanceID());
                // Animator triggers do not replicate, so the flinch is raised per machine off the
                // same trace the authority made.
                hit.transform.root.GetComponentInChildren<AgentAnimatorDriver>()?.TriggerHurt();
            }
        }

        /// <summary>
        /// The cut, traced identically on every machine from the owner's reported ray. A sphere,
        /// not a line: a blade that grazes past a shoulder should still bite (GDC-L1-FEEL-0003).
        /// </summary>
        private bool TryTrace(Vector3 dir, out RaycastHit hit)
        {
            hit = default;
            if (owner == null) return false;

            Vector3 origin = owner.transform.position + Vector3.up * originHeight;
            GameObject ownerRoot = owner.transform.root.gameObject;

            RaycastHit[] hits = Physics.SphereCastAll(origin, sweepRadius, dir, reach, ~0,
                                                      QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;

            foreach (RaycastHit candidate in hits)
            {
                GameObject root = candidate.transform.root.gameObject;
                if (root == ownerRoot || root == gameObject) continue;
                if (candidate.distance >= best) continue;

                best = candidate.distance;
                hit = candidate;
                // A sphere cast that starts overlapping reports distance 0 with `point` at the
                // origin; the sound would play inside the wearer's chest.
                if (candidate.distance <= 0f) hit.point = origin + dir * (reach * 0.5f);
                found = true;
            }

            return found;
        }

        // ── The blade ───────────────────────────────────────────────────────────

        private void FireBlade()
        {
            CaptureBladeRest();
            bladeHold = bladeHoldOnMiss;   // until the cut says otherwise
            bladeStart = Time.time + bladeDelay;
            sparked = false;
        }

        /// <summary>
        /// Where the blade sits at rest, read off the transforms once: it is a child of the
        /// imported model and rests wherever the FBX put it.
        /// </summary>
        private void CaptureBladeRest()
        {
            if (bladeRest != null || bladeParts == null) return;

            bladeRest = new Vector3[bladeParts.Length];
            for (int i = 0; i < bladeParts.Length; i++)
                if (bladeParts[i] != null) bladeRest[i] = bladeParts[i].localPosition;
        }

        private void SetBladeOffset(float distance)
        {
            if (bladeParts == null || bladeRest == null) return;

            Vector3 step = bladeAxis.normalized * distance;
            for (int i = 0; i < bladeParts.Length; i++)
                if (bladeParts[i] != null) bladeParts[i].localPosition = bladeRest[i] + step;
        }

        private void Update()
        {
            TickCuts();
            TickBlade();
        }

        private void TickBlade()
        {
            if (float.IsNegativeInfinity(bladeStart)) return;

            float t = Time.time - bladeStart;
            if (t < 0f) return;   // waiting for the arm

            // Steel on steel: the sparks belong to the moment the blade starts to move, which is
            // the arm's beat after the press, or they fire on an empty sheath and read as nothing.
            if (!sparked)
            {
                sparked = true;
                if (mouthSparks != null) mouthSparks.Play();
            }
            float outEnd = bladeOutTime;
            float holdEnd = outEnd + bladeHold;
            float total = holdEnd + bladeReturnTime;

            if (t >= total)
            {
                bladeStart = float.NegativeInfinity;
                SetBladeOffset(0f);
                return;
            }

            float offset;
            if (t < outEnd)
            {
                // Out: quick off the mark, easing into the lock, over a stroke long enough to watch.
                float u = Mathf.Clamp01(t / Mathf.Max(outEnd, 0.001f));
                offset = bladeThrow * (1f - (1f - u) * (1f - u) * (1f - u));
            }
            else if (t < holdEnd)
            {
                offset = bladeThrow;
            }
            else
            {
                // Drawn back under the actuator's return, easing in.
                float u = Mathf.Clamp01((t - holdEnd) / Mathf.Max(bladeReturnTime, 0.001f));
                offset = bladeThrow * (1f - u * u);
            }

            SetBladeOffset(offset);
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────

        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);
            aimRig = holder != null ? holder.GetComponent<PlayerAimRig>() : null;
            CaptureBladeRest();
            SetBladeOffset(0f);
        }

        public override void OnUnequipped(GameObject holder)
        {
            aimRig = null;
            bladeStart = float.NegativeInfinity;
            cutAt = float.PositiveInfinity;
            authCutAt = float.PositiveInfinity;
            if (mouthSparks != null) mouthSparks.Stop();
            SetBladeOffset(0f);
            base.OnUnequipped(holder);
        }

        private void OnValidate()
        {
            reach = Mathf.Max(0.5f, reach);
            damage = Mathf.Max(0, damage);
            bladeOutTime = Mathf.Max(0.01f, bladeOutTime);
            bladeReturnTime = Mathf.Max(0.01f, bladeReturnTime);
        }
    }
}
