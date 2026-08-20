// The robot ostrich, as four choices.
//
// Everything that makes a legged machine walk lives in LeggedLocomotion. What makes THIS one an
// ostrich rather than a walking table is four policies, and each of them is a decision that cost
// something to get right:
//
//  1. STRIDE COMES FROM THE HIP, NOT THE COXA. The six-legged station sizes its stride from the
//     coxa's yaw arc, which works because its legs stick out sideways. An ostrich's foot sits
//     almost directly under its hip -- a few centimetres of rest radius against a metre of leg --
//     so a yaw arc gives it a stride of nearly nothing. HipBudgetStride sizes it from what the hip
//     PITCH can reach on the ground, and stands the bird down to buy that reach back. The coxa is
//     left to do the job it is actually needed for: holding a planted foot still while the body
//     turns.
//  2. THERE IS NO STATIC STABILITY TO PRESERVE. The station keeps three feet down and is safe at
//     any moment you stop the clock. A biped is never statically stable, spends half its cycle on
//     one foot and, at speed, has moments with no feet down at all. AlternatingGait has no
//     minimum-planted rule, because there is no count of planted feet a biped could promise.
//  3. THE BODY IS NOT HELD LEVEL. The station levels its deck because people stand on it. An
//     ostrich bobs, sways over the stance foot and pitches toward horizontal as it speeds up, and
//     that motion is most of what reads as a bird rather than a table walking.
//  4. THE FOOT IS A JOINT, NOT A PAD. The station sets its soles flat and leaves them there. A bird
//     rolls onto its toe to push off, carries the foot toed-up through the swing, and reaches
//     toe-first for the landing.
//
// Balance is procedural, not physical: the body follows curves driven by the gait clock rather than
// solving a real centre-of-mass problem. A real balance solve fights the rider for control and
// falls over when it loses; this reads correct and stays steerable.
//
// The fields below are the only ones this component adds. Everything else -- joint travel, ground
// mask, step duration and clearance, ride height, gravity -- is on the base and appears above these
// in the inspector.
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Creatures.Ostrich
{
    public class OstrichLocomotion : LeggedLocomotion
    {
        [Header("Ostrich — stride")]
        [Tooltip("Hip height while walking, as a fraction of its height in the model's rest pose.\n\n" +
                 "This is the single most important number here. The legs are modelled almost " +
                 "straight, which leaves no length spare to swing fore and aft with -- a straight leg " +
                 "can only reach the ground directly beneath it. Standing the bird down slightly bends " +
                 "the hock and buys back the reach the stride is made of. Lower is a longer stride and " +
                 "a more crouched, sprinting look; 1 stands it up and it can barely step at all.")]
        [Range(0.70f, 1.0f)]
        [SerializeField] private float hipHeightFraction = 0.86f;

        [Header("Ostrich — gait")]
        [Tooltip("Fraction of the cycle a foot is airborne at a walk. Below 0.5 both feet are down " +
                 "for part of the cycle, which is what makes it a walk.")]
        [Range(0.30f, 0.49f)]
        [SerializeField] private float walkSwingDuty = 0.44f;
        [Tooltip("Same at a run. Above 0.5 the swings overlap and the bird is briefly airborne with " +
                 "no feet down -- the flight phase that makes a run read as a run.")]
        [Range(0.51f, 0.75f)]
        [SerializeField] private float runSwingDuty = 0.62f;

        [Header("Ostrich — foot")]
        [Tooltip("Degrees the sole rolls onto its toe at the ends of a stance: heel up to push off, " +
                 "toe down to reach for the landing, flat through the middle where the weight is.")]
        [SerializeField] private float toeOffAngle = 18f;
        [Tooltip("Degrees the toes lift through mid-swing, to clear the ground the foot is crossing. " +
                 "Too much and the bird high-steps like a dressage horse.")]
        [SerializeField] private float swingToeAngle = 12f;

        [Header("Ostrich — body motion")]
        [Tooltip("Vertical bob, as a fraction of leg reach. Runs at twice the stride frequency: the " +
                 "body dips onto each footfall and rises through mid-stance.")]
        [SerializeField] private float bobAmount = 0.055f;
        [Tooltip("How far the body leans over the foot that is carrying it, as a fraction of the " +
                 "stance width. Small, but it is what stops the walk reading as a shopping trolley.\n\n" +
                 "BOUNDED BY THE LEGS, not by taste. A sway moves the HIPS sideways, and this rig " +
                 "has no joint that can answer that: the coxa turns the leg's plane about a vertical " +
                 "axis, which lets the leg reach sideways only by twisting the hock out of line. So " +
                 "whatever the hips are swayed by, the planted foot is dragged with them -- measured " +
                 "at very nearly the whole of it. At 0.55 that was 16 cm of the foot sliding sideways " +
                 "every stride at a run; 0.12 keeps it inside 4 cm, which is under the width of the " +
                 "sole. Raising it costs about 3.5 cm of foot slide per 0.1. Getting the full waddle " +
                 "back needs an abduction joint modelled at the hip, not a bigger number here.")]
        [Range(0f, 0.6f)]
        [SerializeField] private float swayAmount = 0.12f;
        [Tooltip("Degrees the body pitches toward horizontal at top speed.")]
        [SerializeField] private float runPitch = 16f;
        [Tooltip("Degrees the body rolls into a turn at top speed.")]
        [SerializeField] private float turnRoll = 8f;
        [SerializeField] private float attitudeSmooth = 8f;
        [Tooltip("How much of the ground's tilt the body takes on. A bird can afford more of this than " +
                 "a crewed deck can -- nothing is standing on its back -- and a body held rigidly level " +
                 "across a slope strands its downhill leg exactly as a deck does.")]
        [Range(0f, 1f)]
        [SerializeField] private float slopeFollow = 0.8f;
        [Range(0f, 45f)]
        [SerializeField] private float maxSlopeTilt = 25f;

        [Header("Ostrich — steering")]
        [Tooltip("Fastest the bird may turn. A biped pivots about its own feet, so this is authored " +
                 "rather than derived -- there is no long outboard leg to be dragged.")]
        [SerializeField] private float maxYawRate = 120f;

        protected override IStrideModel CreateStride() => new HipBudgetStride(hipHeightFraction);
        protected override IGaitPattern CreateGait() => new AlternatingGait(walkSwingDuty, runSwingDuty);
        protected override IFootStyle CreateFeet() => new ArticulatedSole(toeOffAngle, swingToeAngle);

        protected override IBodyMotion CreateBody()
            => new BobbingBody(bobAmount, swayAmount, runPitch, turnRoll, attitudeSmooth,
                               slopeFollow, maxSlopeTilt);

        protected override float DeriveMaxYawRate() => maxYawRate;

        /// Lower than the walking station's 0.85 on purpose. The station commits a foothold and its
        /// hull creeps; a bird covers most of a leg-length while the foot is in the air, so a foothold
        /// that was comfortable at lift-off is at full stretch on landing. The extra margin is what
        /// stops every step arriving over-extended and triggering the step-early rule.
        protected override float FootholdReachFraction => 0.72f;
    }
}
