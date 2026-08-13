// The humanoid robot, as four choices.
//
// Everything that makes a legged machine walk lives in LeggedLocomotion. What makes this one a
// humanoid rather than a bird on two legs is four policies plus two things the base deliberately
// does not own -- the arms (HumanoidArmSwing) and the torso (HumanoidSpineMotion).
//
//  1. STRIDE COMES FROM THE HIP, as it does on the ostrich. The feet sit directly under the hips --
//     three centimetres of rest radius against a metre of leg -- so a yaw arc would give this a
//     stride of nothing. The coxa is left to do the job it is actually needed for: holding a
//     planted foot still while the body turns.
//  2. A BIPED IS A BIPED. AlternatingGait, and no minimum-planted rule, because there is no count
//     of planted feet a two-legged machine could promise.
//  3. THE TORSO STAYS NEAR VERTICAL. This is the one place the tuning is the OPPOSITE of the
//     bird's. An ostrich pitches 16 degrees toward horizontal at speed and bobs and sways hard, and
//     that is exactly what a walking person does not do. Everything in the body motion below is a
//     fraction of the bird's.
//  4. THE FOOT IS A JOINT. Heel-strike to toe-off is the whole read of a human walk, and it is one
//     policy: ArticulatedSole.
//
// ─────────── the knee bends forward ───────────
//
// Both shipping machines have reverse knees. `WalkerLimbGeometry.BendSign` is measured from the
// rest pose -- the 2-D cross product of the first two pitch segments -- so it should handle either
// sense with no code change, and `BendSign_KeepsTheKneeBendingTheWayTheMachineWasBuilt` is the test
// that says so. This is the first rig that exercises the other sign; nothing here selects it.
//
// ─────────── deriving hipHeightFraction ───────────
//
// `HipBudgetStride` sizes a stride from what is left of the leg once the hip height is paid for,
// with a floor under the square root for the case where the hip is riding higher than the linkage
// can spare. On this rig, that floor is reached at a working hip height of 0.800 m -- f = 0.930 --
// and above it the stride stops being geometry and stops responding to the number at all. So the
// usable range is bounded by the RIG, not by taste, and 0.90 sits inside it with margin while
// keeping the stride at 0.70x the hip height, which is a natural step for something this size.
// Standing straighter than that buys a shorter stride and nothing else.
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Creatures.Humanoid
{
    public class HumanoidLocomotion : LeggedLocomotion
    {
        [Header("Humanoid — stride")]
        [Tooltip("Hip height while walking, as a fraction of its height in the model's rest pose.\n\n" +
                 "The single most important number here, and it is bounded above by the rig rather " +
                 "than by taste: past about 0.93 on this model the stride budget collapses onto " +
                 "HipBudgetStride's floor and the machine is standing too tall to step at all. Lower " +
                 "is a longer stride and a more crouched, sprinting look.")]
        [Range(0.70f, 0.93f)]
        [SerializeField] private float hipHeightFraction = 0.90f;

        [Header("Humanoid — gait")]
        [Tooltip("Fraction of the cycle a foot is airborne at a walk. Below 0.5 both feet are down for " +
                 "part of the cycle, which is what makes it a walk.")]
        [Range(0.30f, 0.49f)]
        [SerializeField] private float walkSwingDuty = 0.42f;
        [Tooltip("Same at a run. Above 0.5 the swings overlap and the machine is briefly airborne with " +
                 "no feet down -- the flight phase that makes a run read as a run.")]
        [Range(0.51f, 0.75f)]
        [SerializeField] private float runSwingDuty = 0.60f;

        [Header("Humanoid — foot")]
        [Tooltip("Degrees the sole rolls onto its toe at the ends of a stance: heel down to receive " +
                 "the weight, flat through the middle, toe down to push off. On a plantigrade foot " +
                 "this is the whole read of the walk.")]
        [SerializeField] private float toeOffAngle = 14f;
        [Tooltip("Degrees the toes lift through mid-swing to clear the ground being crossed.")]
        [SerializeField] private float swingToeAngle = 10f;

        [Header("Humanoid — body motion")]
        [Tooltip("Vertical bob, as a fraction of leg reach. A third of the ostrich's: a person's " +
                 "shoulders rise and fall a couple of centimetres over a stride, not a hand's width.")]
        [SerializeField] private float bobAmount = 0.018f;
        [Tooltip("How far the body leans over the foot carrying it. Well under the bird's 0.55 -- a " +
                 "walking person shifts weight without visibly rolling from side to side.")]
        [SerializeField] private float swayAmount = 0.18f;
        [Tooltip("Degrees the body pitches forward at top speed. The ostrich uses 16 and goes toward " +
                 "horizontal; a person leans into a run and stays upright, so this is a quarter of it.")]
        [SerializeField] private float runPitch = 4f;
        [Tooltip("Degrees the body rolls into a turn at top speed.")]
        [SerializeField] private float turnRoll = 3f;
        [SerializeField] private float attitudeSmooth = 8f;
        [Tooltip("How much of the ground's tilt the body takes on. A biped has fewer than three feet " +
                 "down, so the support plane never fits and this only ever applies on the rare frames " +
                 "it does -- but a body pinned level still strands a downhill leg.")]
        [Range(0f, 1f)]
        [SerializeField] private float slopeFollow = 0.5f;
        [Range(0f, 45f)]
        [SerializeField] private float maxSlopeTilt = 18f;

        [Header("Humanoid — arms")]
        [Tooltip("Travel a shoulder is allowed either side of its rest pose. Generous: an arm reaches " +
                 "behind itself and a hip does not, which is why arms get their own limits at all.")]
        [SerializeField] private float shoulderRange = 110f;
        [Tooltip("Travel at the elbow and wrist.")]
        [SerializeField] private float elbowRange = 120f;
        [Tooltip("Travel at the shoulder's own yaw joint -- the arm swinging out from the body.")]
        [SerializeField] private float shoulderYawRange = 60f;

        [Header("Humanoid — steering")]
        [Tooltip("Fastest the machine may turn. A biped pivots about its own feet, so this is authored " +
                 "rather than derived -- there is no long outboard leg to be dragged.")]
        [SerializeField] private float maxYawRate = 140f;

        protected override IStrideModel CreateStride() => new HipBudgetStride(hipHeightFraction);
        protected override IGaitPattern CreateGait() => new AlternatingGait(walkSwingDuty, runSwingDuty);
        protected override IFootStyle CreateFeet() => new ArticulatedSole(toeOffAngle, swingToeAngle);

        protected override IBodyMotion CreateBody()
            => new BobbingBody(bobAmount, swayAmount, runPitch, turnRoll, attitudeSmooth,
                               slopeFollow, maxSlopeTilt);

        protected override float DeriveMaxYawRate() => maxYawRate;

        /// A shoulder and a hip have nothing in common but a name. The base defaults an arm's travel to
        /// the leg's, which is right for a rig that has never thought about it and far too tight for
        /// one that swings behind itself every stride.
        protected override WalkerLimbSolver.Limits BuildArmLimits(in WalkerLimbGeometry g)
        {
            var limits = new WalkerLimbSolver.Limits
            {
                Yaw = shoulderYawRange,
                Roll = 0f,
                Pitch = new float[Mathf.Max(1, g.PitchCount)],
            };
            for (int i = 0; i < limits.Pitch.Length; i++)
                limits.Pitch[i] = i == 0 ? shoulderRange : elbowRange;
            return limits;
        }

        /// Same margin as the bird's, and for the same reason: a machine that covers most of a
        /// leg-length while the foot is in the air lands on a foothold that was comfortable at
        /// lift-off and is at full stretch on arrival.
        protected override float FootholdReachFraction => 0.72f;

        /// This leg's slice of the gait cycle. Exposed so the arms can be paced off the LEGS' own
        /// offsets rather than off an assumed {0, ½} -- a gait pattern that blends its offsets with
        /// speed would otherwise walk the arms out of phase with the footfalls it is meant to mirror.
        public float LegPhaseOffset(int index)
            => index >= 0 && index < legs.Count ? legs[index].PhaseOffset : 0f;

        /// Where leg `index` sits on the body, in body space. The arms work out which leg is their
        /// diagonal from this rather than from an index, exactly as the gait patterns do.
        public Vector3 LegHomeLocal(int index)
            => index >= 0 && index < legs.Count ? legs[index].Measure.HomeLocal : Vector3.zero;
    }
}
