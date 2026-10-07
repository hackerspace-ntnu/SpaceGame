// What one leg is, in numbers, measured once from the rest pose.
//
// PER LEG, not averaged across the machine. Both components this replaced averaged legReach,
// restHipHeight and RestFootRadius into a single stride for the whole machine, which is only right
// when every leg is the same. A quadruped with shorter front legs is an entirely natural rig and
// got a stride that fitted neither pair.
//
// Cycle DISTANCE stays global -- the body only travels one distance per cycle -- and the base
// derives it from the shortest leg's stride, since that is the one that runs out first.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public struct LegMeasurement
    {
        /// This leg's place in the machine's leg list.
        public int Index;

        /// Rest foothold in body space. The gait aims from it, and the gait policies work out
        /// which legs are diagonal, lateral, front or rear from it rather than from hard-coded
        /// indices.
        public Vector3 HomeLocal;

        /// The hip's position in body space. A constant: the hip sits ON the coxa's yaw axis, so
        /// yawing the leg cannot move it, and the coxa is rigid to the body. That is what lets the
        /// body work out where its hips WILL be under a pose it is still deciding on.
        public Vector3 HipLocal;

        /// Hip to contact point at rest: the radius the hip pitch sweeps the foot through.
        /// Deliberately not RestFootRadius, which is the horizontal offset from the yaw axis and is
        /// near zero on a bird -- using that here is what would give a three-centimetre stride.
        public float LegReach;

        /// Hip above the contact point in the model's rest pose.
        public float RestHipHeight;

        /// Hip above the contact point while walking, after the stride model has stood the machine
        /// down. Written by IStrideModel.WorkingHipHeight.
        public float WorkingHipHeight;

        /// How far this foot travels through the body frame per cycle. Written by
        /// IStrideModel.StrideLength.
        public float StrideLength;

        /// Arc height for a step on level ground.
        public float StepHeight;

        /// How far the sole spreads around its contact point, measured off the foot's own meshes so
        /// ground sampling covers the real footprint rather than a guessed radius.
        public float FootprintRadius;

        /// Horizontal distance from the body's centre to this leg's rest foothold. The outermost
        /// one bounds how fast the machine may pivot.
        public float FootRadiusFromCentre;

        /// Furthest the contact point can get from the hip. The linkage's own limit.
        public float MaxReach;

        /// Horizontal distance from the yaw axis to the rest foothold: the radius of the arc the
        /// foot sweeps when the coxa turns, and so what sets a splayed-leg machine's stride.
        public float RestFootRadius;
    }
}
