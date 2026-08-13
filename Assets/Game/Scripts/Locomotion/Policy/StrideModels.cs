// How far a leg can step.
//
// Two machines, two entirely different answers, and getting them confused is not a tuning error --
// it is a machine that cannot walk. The station's feet stick out sideways on long coxae, so its
// stride is the ARC THE COXA SWEEPS. The bird's foot sits almost directly under its hip, a few
// centimetres of radius against a metre of leg, so a yaw arc would give it a stride of nearly
// nothing; its stride is what the HIP PITCH can reach on the ground, which is bounded by how much
// of the leg's length the hip height has already spent.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    /// The chord the foot sweeps when the coxa turns. For machines whose legs stick out sideways.
    public class YawArcStride : IStrideModel
    {
        private readonly float yawRange;
        private readonly float fraction;

        /// `fraction` is how much of the yaw travel the stride is sized against, leaving the joint
        /// some margin rather than running it onto its stop at the end of every step.
        public YawArcStride(float yawRange, float fraction = 0.85f)
        {
            this.yawRange = yawRange;
            this.fraction = fraction;
        }

        public float StrideLength(in LegMeasurement m)
            => 2f * m.RestFootRadius * Mathf.Sin(yawRange * fraction * Mathf.Deg2Rad);

        /// A station rides at the height it was modelled at. It has reach to spare, because its
        /// legs are splayed rather than straight.
        public float WorkingHipHeight(in LegMeasurement m) => m.RestHipHeight;
    }

    /// What the hip pitch can reach on the ground, after the hip height has been paid for.
    ///
    /// A leg carrying the body at height h out of a total linkage length L can only put its foot on
    /// the ground within sqrt(L^2 - h^2) of the hip; everything else is spent reaching DOWN. The
    /// bird's legs are modelled nearly straight -- h is 91% of L -- so at the modelled height that
    /// budget is almost nothing, and a stride derived from the hip's angular travel instead would
    /// ask for steps the leg physically cannot place. It would get them, too: the solver would
    /// clamp, the foot would hang off the end of a straight leg, and the bird would skate.
    ///
    /// Standing it down by `hipHeightFraction` is what buys the stride back. It is the single most
    /// important number on the machine.
    public class HipBudgetStride : IStrideModel
    {
        private readonly float hipHeightFraction;
        private readonly float strideFraction;

        /// `strideFraction` is how much of the available horizontal reach the stride is sized
        /// against. The trailing leg is at its most extended in the instant before it lifts, so
        /// this decides how close to dead straight the leg gets at the end of every stance. At 0.85
        /// it reaches 94% of the linkage and the smallest bump tips it past what the solver can
        /// honour; at 0.72 it tops out around 90% and keeps a visible bend in the hock.
        public HipBudgetStride(float hipHeightFraction = 0.86f, float strideFraction = 0.72f)
        {
            this.hipHeightFraction = hipHeightFraction;
            this.strideFraction = strideFraction;
        }

        public float WorkingHipHeight(in LegMeasurement m) => m.RestHipHeight * hipHeightFraction;

        public float StrideLength(in LegMeasurement m)
        {
            float linkage = m.MaxReach;
            float height = m.WorkingHipHeight;

            // The floor is a floor, not a fallback: it keeps the root real when the hip is riding
            // higher than the linkage can spare. Reaching it means the machine is standing too tall
            // to step at all, which is a rig problem rather than something to paper over here.
            float budget = Mathf.Sqrt(
                Mathf.Max(linkage * 0.15f, linkage * linkage - height * height));

            return 2f * budget * strideFraction;
        }
    }
}
