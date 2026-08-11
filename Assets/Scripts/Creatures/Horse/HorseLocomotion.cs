// The robot horse, as four choices.
//
// Everything that makes a legged machine walk lives in LeggedLocomotion. What makes this one a
// horse rather than a walking table is four policies, three of which are shipped as they are:
//
//  1. STRIDE COMES FROM THE HIP, NOT THE COXA -- `HipBudgetStride`, the ostrich's model. The legs
//     hang under the body with the hooves within ~0.2 m of their own yaw axis, so the coxa's arc
//     would give a stride of centimetres. What matters here is that the model is PER LEG: the
//     forelegs are a longer, straighter linkage than the hind legs and each pair gets a stride
//     sized to itself. Both machines that came before this averaged the two and this is the first
//     rig where that difference is real.
//  2. A QUADRUPED HAS A GAIT LADDER, NOT A GAIT -- `CanterGait`, the one policy this machine adds.
//     Walk, trot and canter/gallop are different shapes, not one shape at different speeds, and the
//     top of the ladder is ASYMMETRIC: a leading foreleg and a stretch of the cycle with no feet
//     down at all. See CanterGait.cs for why no shipped pattern can express that.
//  3. THE BODY IS NOT HELD LEVEL -- `BobbingBody`, tuned much flatter than the bird's. An ostrich
//     pitches 16 degrees toward horizontal at speed; a horse's back stays near level, which is the
//     whole reason it is worth sitting on.
//  4. THE FOOT IS A JOINT -- `ArticulatedSole`. A hoof breaks over its toe to push off and reaches
//     toe-first for the landing, exactly as the bird's does.
//
// Balance is procedural, not physical: the body follows curves driven by the gait clock rather than
// solving a centre-of-mass problem. A real balance solve fights the rider for control and falls
// over when it loses.
//
// The fields below are the only ones this component adds. Everything else -- joint travel, ground
// mask, step duration and clearance, ride height, gravity -- is on the base and appears above these
// in the inspector.
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Creatures.Horse
{
    public class HorseLocomotion : LeggedLocomotion
    {
        [Header("Horse — stride")]
        [Tooltip("Hip height while walking, as a fraction of its height in the model's rest pose.\n\n" +
                 "This buys the stride. A leg carrying the body at height h out of a linkage of length " +
                 "L can only reach the ground within sqrt(L^2 - h^2) of its hip; the rest is spent " +
                 "reaching down. The rig is posed at about 86% extension in front and 83% behind, so " +
                 "there is already some bend to work with and this stands it down far less than the " +
                 "ostrich's 0.86 has to.")]
        [Range(0.80f, 1.0f)]
        [SerializeField] private float hipHeightFraction = 0.95f;

        [Tooltip("How much of the available horizontal reach a stride is sized against. Higher is a " +
                 "longer stride on a straighter leg, with less left over when the ground is not where " +
                 "the foothold search expected it.")]
        [Range(0.55f, 0.85f)]
        [SerializeField] private float strideFraction = 0.72f;

        [Tooltip("How far inside its horizontal reach a fresh foothold is pulled. A machine that " +
                 "covers most of a leg-length while the hoof is in the air lands on a foothold that " +
                 "was comfortable at lift-off and is at full stretch on arrival; this margin is what " +
                 "stops every step arriving over-extended and firing the step-early rule.\n\n" +
                 "Serialized rather than a constant because it trades directly against reach at a " +
                 "gallop, which is the one speed where this machine runs out of leg.")]
        [Range(0.5f, 0.9f)]
        [SerializeField] private float footholdReachFraction = 0.72f;

        [Header("Horse — gait")]
        [Tooltip("Fraction of the cycle a hoof is airborne at a walk. Below 0.5 there is always at " +
                 "least one diagonal on the ground, which is what makes it a walk.")]
        [Range(0.30f, 0.49f)]
        [SerializeField] private float walkSwingDuty = 0.38f;

        [Tooltip("Same at a gallop. This is what buys the suspension: the gallop's four footfalls are " +
                 "spread over half a cycle, so everything above 0.5 is time with no hoof on the ground.")]
        [Range(0.51f, 0.75f)]
        [SerializeField] private float runSwingDuty = 0.62f;

        [Tooltip("Which foreleg leads at a canter. Fixed rather than switched with the turn: changing " +
                 "lead is a table swap by another name, and swapping mid-stride teleports whichever " +
                 "hoof is in the air.")]
        [SerializeField] private bool leadRight = true;

        [Tooltip("Fraction of top speed at which the walk has fully become a trot.")]
        [Range(0.2f, 0.8f)]
        [SerializeField] private float walkToTrot = 0.45f;

        [Tooltip("Fraction of top speed at which the trot starts becoming a canter. The gap between " +
                 "this and the one above is the trot, and it is a plateau on purpose: a horse holds a " +
                 "trot over a range of speeds rather than passing through it.")]
        [Range(0.3f, 0.95f)]
        [SerializeField] private float trotToCanter = 0.55f;

        [Header("Horse — foot")]
        [Tooltip("Degrees the hoof breaks over its toe at the ends of a stance: heel up to push off, " +
                 "toe down to reach for the landing, flat through the middle where the weight is.")]
        [SerializeField] private float toeOffAngle = 14f;
        [Tooltip("Degrees the toe lifts through mid-swing. Too much and it high-steps like a dressage " +
                 "horse, which is a different animal from the one this is.")]
        [SerializeField] private float swingToeAngle = 9f;

        [Header("Horse — body motion")]
        [Tooltip("Vertical bob, as a fraction of the shortest leg's reach. Runs at twice the stride " +
                 "frequency: the body dips onto each footfall and rises through mid-stance.")]
        [SerializeField] private float bobAmount = 0.035f;
        [Tooltip("How far the body leans over the loaded side. Small on a quadruped — it has a foot " +
                 "on each corner and nothing like a biped's need to get its mass over one of them.")]
        [SerializeField] private float swayAmount = 0.25f;
        [Tooltip("Degrees the body pitches nose-down at top speed. A horse's back stays near level, " +
                 "which is the whole reason it is worth sitting on; the ostrich's 16 is what NOT to do.")]
        [SerializeField] private float runPitch = 5f;
        [Tooltip("Degrees the body rolls into a turn at top speed.")]
        [SerializeField] private float turnRoll = 6f;
        [SerializeField] private float attitudeSmooth = 8f;
        [Tooltip("How much of the ground's tilt the body takes on. A body held rigidly level across a " +
                 "hillside strands its downhill legs; a rider can take rather more tilt than that.")]
        [Range(0f, 1f)]
        [SerializeField] private float slopeFollow = 0.7f;
        [Range(0f, 45f)]
        [SerializeField] private float maxSlopeTilt = 20f;

        [Header("Horse — steering")]
        [Tooltip("Fastest the horse may turn. Authored rather than derived: the default derivation " +
                 "sizes the pivot rate against the outermost foot, and on a machine whose feet are all " +
                 "close in under its body that comes out far faster than anything with this much mass " +
                 "would turn.")]
        [SerializeField] private float maxYawRate = 90f;

        protected override IStrideModel CreateStride()
            => new HipBudgetStride(hipHeightFraction, strideFraction);

        protected override IGaitPattern CreateGait()
            => new CanterGait(walkSwingDuty, runSwingDuty, leadRight, walkToTrot, trotToCanter);

        protected override IFootStyle CreateFeet() => new ArticulatedSole(toeOffAngle, swingToeAngle);

        protected override IBodyMotion CreateBody()
            => new BobbingBody(bobAmount, swayAmount, runPitch, turnRoll, attitudeSmooth,
                               slopeFollow, maxSlopeTilt);

        protected override float DeriveMaxYawRate() => maxYawRate;

        /// Lower than the walking station's 0.85, for the same reason the ostrich's is: this machine
        /// covers most of a leg-length while a hoof is in the air, so a foothold that was comfortable
        /// at lift-off is at full stretch on landing.
        protected override float FootholdReachFraction => footholdReachFraction;
    }
}
