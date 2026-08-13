// Arm counter-swing: each arm opposite its diagonal leg.
//
// This is the signature of a humanoid gait and the first consumer of the arm seam. LeggedLocomotion
// discovers an `Arm_` limb, measures it and gives it a solver, and then deliberately does nothing
// with it -- `SolveArms` is not part of `Step`, because an arm's target is neither the gait's nor
// the body's. It is this component's, and this is all it takes: a world point, a tip direction, and
// the existing IK.
//
// ─────────── the phase comes from the gait clock, and only from there ───────────
//
// A separate timer ticking at "about the stride frequency" drifts, and what drift looks like is the
// arms sliding out of step with the footfalls over ten seconds and then back in. So the swing is a
// function of `LastFrame.Phase` and the LEGS' OWN phase offsets, which means it cannot drift: at
// any speed, at any duty, through an acceleration, the arm is wherever its diagonal leg is.
//
// Nothing at stride frequency goes through a filter, for the same reason it does not on the body:
// a first-order smooth costs both amplitude and phase at the frequency it passes, and how much
// depends on speed. The only smoothed quantity here is the AMPLITUDE ENVELOPE, which moves at the
// speed the machine accelerates, not at the speed it steps.
//
// ─────────── the target is an arc, not a line ───────────
//
// The hand is swung by ROTATING the rest shoulder-to-hand vector about the body's lateral axis,
// rather than by sliding a point fore and aft. Two things fall out of that and both matter: the
// distance from the shoulder never changes, so the reach fraction is whatever it is at rest and a
// swing can never over-extend the arm; and the hand travels the arc a real shoulder would, so the
// elbow's break comes out of the IK rather than being authored.
//
// Runs at 120: after HumanoidLocomotion (100), which has already posed the body and stepped the
// clock this frame, and before HumanoidSpineMotion (150), which turns the chest the arms hang off.
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Creatures.Humanoid
{
    [DefaultExecutionOrder(120)]
    [RequireComponent(typeof(HumanoidLocomotion))]
    public class HumanoidArmSwing : MonoBehaviour
    {
        [Header("Swing")]
        [Tooltip("How far the hand travels, as a fraction of the foot's stride. Derived from the leg " +
                 "rather than authored in degrees, so rescaling the model re-tunes the swing: a " +
                 "machine that takes longer steps swings its arms further, which is what people do.")]
        [Range(0f, 1f)]
        [SerializeField] private float swingFraction = 0.5f;

        [Tooltip("Hard cap on the swing, in degrees either side of rest, for the case where the stride " +
                 "is long and the arms are short.")]
        [Range(0f, 90f)]
        [SerializeField] private float maxSwingAngle = 35f;

        [Tooltip("How quickly the swing grows and fades with speed. This is an ENVELOPE, not the " +
                 "swing: it moves at the rate the machine accelerates, so it is the one thing here " +
                 "that may be filtered.")]
        [SerializeField] private float effortSmooth = 4f;

        [Tooltip("Degrees the arms hold out from the body at rest, away from the torso. Small; it is " +
                 "what stops the hands clipping through the hips at the ends of a swing.")]
        [Range(0f, 30f)]
        [SerializeField] private float armFlare = 6f;

        private HumanoidLocomotion locomotion;

        // Rest geometry, captured once in BODY space so it survives the machine moving and turning.
        private Vector3[] restReach;      // shoulder -> hand, at rest
        private Vector3[] restTip;        // last hinge -> hand, at rest: which way the hand points
        private float[] phaseOffset;      // the DIAGONAL leg's slice of the cycle
        private float[] flareSign;        // which way "away from the torso" is, per arm
        private float swingAngle;
        private float smoothedEffort;
        private bool bound;

        private void Awake()
        {
            locomotion = GetComponent<HumanoidLocomotion>();
            Bind();
        }

        /// Captures the rest pose and works out which leg each arm mirrors. Public and separate from
        /// Awake because EditMode never fires Awake, and because a re-imported model needs it again.
        ///
        /// Every buffer here is allocated once, at bind: invariant I3, nothing in the per-frame path
        /// allocates.
        public void Bind()
        {
            if (locomotion == null) locomotion = GetComponent<HumanoidLocomotion>();
            bound = false;
            if (locomotion == null || !locomotion.IsReady || locomotion.ArmCount == 0) return;

            int n = locomotion.ArmCount;
            restReach = new Vector3[n];
            restTip = new Vector3[n];
            phaseOffset = new float[n];
            flareSign = new float[n];

            Transform body = transform;
            for (int i = 0; i < n; i++)
            {
                WalkerArm arm = locomotion.Arms[i];
                Vector3 shoulder = arm.Rig.Anchor.position;
                Vector3 hand = arm.Rig.ContactPoint;
                Vector3 wrist = arm.Rig.TipJoint.position;

                restReach[i] = body.InverseTransformDirection(hand - shoulder);
                restTip[i] = body.InverseTransformDirection(hand - wrist).normalized;

                // Which side this arm is on, and therefore which leg is its diagonal. Taken from where
                // the shoulder actually is, never from the arm's index or its id: a rig re-exported
                // with its limbs in a different order still has to counter-swing correctly.
                float armX = body.InverseTransformPoint(shoulder).x;
                flareSign[i] = armX >= 0f ? 1f : -1f;
                phaseOffset[i] = locomotion.LegPhaseOffset(DiagonalLeg(body, armX));
            }

            // The hand should cover a share of the ground the foot covers. Converting that distance to
            // an angle at the shoulder is what keeps it a rig measurement rather than a tuned number.
            float stride = locomotion.TryGetMeasurement(0, out LegMeasurement m) ? m.StrideLength : 0f;
            float reach = Mathf.Max(restReach[0].magnitude, 1e-3f);
            swingAngle = Mathf.Min(
                maxSwingAngle,
                Mathf.Atan2(stride * 0.5f * swingFraction, reach) * Mathf.Rad2Deg);

            bound = true;
        }

        /// The leg on the far side of the machine from `armX`. A humanoid's left arm goes forward with
        /// its right leg; that is the counter-swing.
        private int DiagonalLeg(Transform body, float armX)
        {
            int best = 0;
            float bestX = float.MaxValue;
            for (int i = 0; i < locomotion.LegCount; i++)
            {
                // The leg furthest to the OPPOSITE side. Signed so it works whichever side the arm is.
                float x = locomotion.LegHomeLocal(i).x * Mathf.Sign(armX);
                if (x < bestX) { bestX = x; best = i; }
            }
            return best;
        }

        private void LateUpdate() => Step(Time.deltaTime);

        /// One frame of arm swing. dt-driven so it can be marched from a test with no player loop.
        public void Step(float deltaTime)
        {
            if (!bound) { Bind(); if (!bound) return; }
            float dt = Mathf.Max(deltaTime, 1e-5f);

            // The envelope, and only the envelope, is smoothed. Taken from the achieved speed so the
            // arms still swing when something other than the driver is moving the machine.
            float target = Mathf.Clamp01(Mathf.Abs(locomotion.LastFrame.AchievedSpeed) /
                                         Mathf.Max(locomotion.MaxSpeed, 1e-4f));
            smoothedEffort = Mathf.Lerp(smoothedEffort, target, 1f - Mathf.Exp(-effortSmooth * dt));

            float phase = locomotion.LastFrame.Phase;
            Transform body = transform;

            for (int i = 0; i < locomotion.ArmCount; i++)
            {
                WalkerArm arm = locomotion.Arms[i];

                // A leg lifts at the instant its own phase offset comes round, with its foot at its
                // REARMOST, and lands at `duty` with the foot foremost. So at p = 0 the diagonal leg's
                // foot is behind the machine and this hand must be too.
                //
                // The sign is +cos and not -cos, and it is worth saying why, because the sensible
                // guess is wrong: a POSITIVE rotation about the body's right axis carries a
                // downward-hanging arm BACKWARD, not forward. `+cos` is therefore "back at p = 0",
                // which is what pairs the hand with its diagonal foot. Measured with the sign the
                // other way, the left hand reached its rearmost exactly as the right foot reached its
                // foremost -- a perfect half-cycle out, which reads as a march rather than a walk.
                //
                // One expression, no state, no timer: whatever the speed or the duty, the hand is a
                // pure function of the clock the feet are on.
                float p = Mathf.Repeat(phase - phaseOffset[i], 1f);
                float swing = Mathf.Cos(2f * Mathf.PI * p) * swingAngle * smoothedEffort;

                Quaternion arc = Quaternion.AngleAxis(swing, Vector3.right) *
                                 Quaternion.AngleAxis(flareSign[i] * armFlare, Vector3.forward);

                Vector3 reach = body.TransformDirection(arc * restReach[i]);
                arm.Target = arm.Rig.Anchor.position + reach;
                arm.TipDirection = body.TransformDirection(arc * restTip[i]);
                arm.Solve();
            }
        }

        /// Worst reach fraction across the arms, for the tests and the debug overlay. Above 1 a hand is
        /// visibly detached from where it was asked to be, which on an arc swing should never happen.
        public float WorstArmReach()
        {
            float worst = 0f;
            if (locomotion == null) return worst;
            for (int i = 0; i < locomotion.ArmCount; i++)
                worst = Mathf.Max(worst, locomotion.Arms[i].ReachFraction);
            return worst;
        }

        /// Degrees of swing the rig measured out to. Zero until Bind has run.
        public float SwingAngle => swingAngle;
    }
}
