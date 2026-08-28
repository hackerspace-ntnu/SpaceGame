// The lightning conjurer, as four choices.
//
// Everything that makes a legged machine walk lives in LeggedLocomotion. What makes THIS one a
// conjurer rather than the humanoid robot is scale, and scale changes almost every number:
//
//  1. STRIDE COMES FROM THE HIP. Same as the humanoid and the ostrich, and for the same reason --
//     the feet sit under the hips rather than splayed out sideways, so a coxa yaw arc would give
//     this a stride of nearly nothing. The coxa is left to do the job it is actually needed for:
//     holding a planted foot still while eighteen metres of body turns over it.
//  2. A BIPED IS A BIPED. AlternatingGait, no minimum-planted rule.
//  3. IT IS SIX TIMES THE PLAYER'S HEIGHT, AND THAT IS THE WHOLE CHARACTER. Step frequency in
//     nature falls off roughly as 1/sqrt(length), and the baked clip this replaces was
//     deliberately slowed to a 2.4-second cycle for exactly that reason -- see the WALK note in
//     _Source~/anim.py. stepDuration on the prefab carries that decision now. Everything else
//     here follows from it: the body motion is damped well below the humanoid's, because a mass
//     this size does not bounce.
//  4. THE FOOT IS A JOINT. walkerize.py gave the rig a sole-roll hinge pivoting on the contact
//     point precisely so this could be ArticulatedSole rather than a flat pad dragged over slopes.
//
// ─────────── why the rig had to change ───────────
//
// The legs were authored as Thigh/Shin/Foot with no azimuth joint, no sole roll and no hinge pins.
// WalkerRig discovers nothing from that, and its fallback axle measurement -- the rest pose's own
// plane normal -- is unusable on a leg this straight. _Source~/walkerize.py renames the chain to
// Coxa_/Hip_/Knee_/Ankle_/Foot_, inserts the two missing joints and models a pin at each one, and
// verifies the result classifies the way this component assumes before it will save. Re-run it
// after any change to the leg bones in rig.py.
//
// ─────────── deriving hipHeightFraction ───────────
//
// HipBudgetStride sizes a stride from what is left of the leg once the hip height is paid for.
// This rig's leg is modelled ALL BUT STRAIGHT -- 1.7 degrees of knee offset over an 11.5-unit
// thigh -- which is the same problem the ostrich has and is solved the same way: stand the machine
// down slightly, bend the knee, and buy back the reach the stride is made of. 0.88 is a little
// lower than the humanoid's 0.90 for that reason. Raising it toward 1 stands the conjurer up and
// it can barely step at all.
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Creatures.Conjurer
{
    public class ConjurerLocomotion : LeggedLocomotion
    {
        [Header("Conjurer — stride")]
        [Tooltip("Hip height while walking, as a fraction of its height in the model's rest pose.\n\n" +
                 "The single most important number here. The legs are modelled almost straight, and " +
                 "a straight leg can only reach the ground directly beneath it. Standing the machine " +
                 "down bends the knee and buys back the reach the stride is made of. Lower is a " +
                 "longer stride and a more crouched look; 1 leaves it unable to step.")]
        [Range(0.70f, 0.93f)]
        [SerializeField] private float hipHeightFraction = 0.88f;

        [Header("Conjurer — gait")]
        [Tooltip("Fraction of the cycle a foot is airborne at a walk. Below 0.5 both feet are down " +
                 "for part of the cycle, which is what makes it a walk.")]
        [Range(0.30f, 0.49f)]
        [SerializeField] private float walkSwingDuty = 0.40f;
        [Tooltip("Same at a run. Above 0.5 the swings overlap and the machine is briefly airborne " +
                 "with no feet down. Kept low for a machine this heavy: a flight phase on eighteen " +
                 "metres of robot reads as the model losing contact, not as a sprint.")]
        [Range(0.51f, 0.75f)]
        [SerializeField] private float runSwingDuty = 0.54f;

        [Header("Conjurer — foot")]
        [Tooltip("Degrees the sole rolls onto its toe at the ends of a stance: heel down to receive " +
                 "the weight, flat through the middle, toe down to push off.")]
        [SerializeField] private float toeOffAngle = 12f;
        [Tooltip("Degrees the toes lift through mid-swing to clear the ground being crossed.")]
        [SerializeField] private float swingToeAngle = 9f;

        [Header("Conjurer — body motion")]
        [Tooltip("Vertical bob, as a fraction of leg reach. Half the humanoid's: this body is a " +
                 "nine-metre sphere and a bob that reads as a jaunty step on a person reads as the " +
                 "whole machine hopping.")]
        [SerializeField] private float bobAmount = 0.009f;
        [Tooltip("How far the body leans over the foot carrying it.")]
        [SerializeField] private float swayAmount = 0.14f;
        [Tooltip("Degrees the body pitches forward at top speed.")]
        [SerializeField] private float runPitch = 3f;
        [Tooltip("Degrees the body rolls into a turn at top speed.")]
        [SerializeField] private float turnRoll = 2.5f;
        [Tooltip("Low: a heavy machine's attitude changes slowly. This is most of what sells the " +
                 "mass, and raising it makes eighteen metres of robot feel like a puppet.")]
        [SerializeField] private float attitudeSmooth = 5f;
        [Tooltip("How much of the ground's tilt the body takes on. A biped has fewer than three " +
                 "feet down, so the support plane rarely fits -- but a body pinned level still " +
                 "strands a downhill leg.")]
        [Range(0f, 1f)]
        [SerializeField] private float slopeFollow = 0.45f;
        [Range(0f, 45f)]
        [SerializeField] private float maxSlopeTilt = 14f;

        [Header("Conjurer — steering")]
        [Tooltip("Fastest the machine may turn. Authored rather than derived: a biped pivots about " +
                 "its own feet, and this one is deliberately ponderous.")]
        [SerializeField] private float maxYawRate = 45f;

        protected override IStrideModel CreateStride() => new HipBudgetStride(hipHeightFraction);
        protected override IGaitPattern CreateGait() => new AlternatingGait(walkSwingDuty, runSwingDuty);
        protected override IFootStyle CreateFeet() => new ArticulatedSole(toeOffAngle, swingToeAngle);

        protected override IBodyMotion CreateBody()
            => new BobbingBody(bobAmount, swayAmount, runPitch, turnRoll, attitudeSmooth,
                               slopeFollow, maxSlopeTilt);

        protected override float DeriveMaxYawRate() => maxYawRate;

        /// Tighter than the 0.72 the humanoid and the ostrich use. A foothold is chosen at lift-off
        /// and landed on a step later, and this machine covers a lot of ground in one 1.2-second
        /// swing -- so a margin that is comfortable on a person leaves this one arriving at full
        /// stretch, which is what reports as an unreachable leg and a foot that snaps.
        protected override float FootholdReachFraction => 0.66f;
    }
}
