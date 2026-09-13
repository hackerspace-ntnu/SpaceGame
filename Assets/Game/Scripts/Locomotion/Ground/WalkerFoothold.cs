// Where a leg plants next.
//
// This is core rather than policy, deliberately. It is the single most bug-prone piece in the whole
// system, and the two machines that had drifted copies of it had drifted into two different bugs.
// A stride model contributes a LENGTH and a stand-down HEIGHT; nothing else gets an opinion about
// where a foot goes.
//
// Two things here are load-bearing and both were paid for:
//
// 1. CLAMP AGAINST WHERE THE HIP WILL BE WHEN THE FOOT LANDS, not where it is now. The foothold is
//    aimed a swing's worth of travel ahead, which on a machine covering more ground per swing than
//    its own leg is long puts it far out of reach of the hip at lift-off. Clamping against the
//    current hip drags every step short; the leg then lands already over-extended, reports
//    unreachable, and gets stepped again on the next frame -- which is a thrash, not a gait.
//
// 2. BOUND THE STEP IN THE HORIZONTAL PLANE and only then look for the ground under it. Clamping
//    the 3D vector instead drags an out-of-range foothold along the line back to the hip, which
//    LIFTS it off the ground; the foot then plants in mid-air, the body rides up to meet it, and
//    the machine levitates.
//
// And the arithmetic that used to be wrong: THE MARGIN BELONGS ON THE HORIZONTAL RESULT, NOT ON THE
// LEG'S LENGTH. Spending it first and then taking the hip height out of what is left is a different
// sum entirely, and on a bird a catastrophic one -- the hip rides at 1.40 m out of a 1.79 m leg, so
// 72% of the leg is 1.29 m, less than the height, the square root goes imaginary, and the whole
// budget collapses onto its degenerate floor. Every step then landed a quarter of a metre ahead of
// the hip instead of eight tenths, and spent the entire stance dragging back to 1.6 m behind, which
// no 1.79 m leg can hold. That is the trailing, over-stretched leg, and it was arithmetic rather
// than tuning. The six-legged machine had the same expression and was not bitten only because its
// legs stick out sideways, so the rise stayed well under the limit.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public static class WalkerFoothold
    {
        /// How far a leg may reach horizontally from `hipAtTouchdown`, given how much of its length
        /// standing at that height has already spent.
        public static float HorizontalBudget(float maxReach, float hipHeight, float probeHeight,
                                             float reachFraction)
        {
            float rise = Mathf.Max(0f, hipHeight - probeHeight);
            return Mathf.Sqrt(
                Mathf.Max(maxReach * maxReach * 0.04f, maxReach * maxReach - rise * rise))
                * reachFraction;
        }

        /// Pulls `probe` inside what the leg can hold, in the horizontal plane, about the hip's
        /// position at touchdown. The height is left exactly as the probe had it -- the caller
        /// samples the ground AFTER this, which is the whole point.
        public static Vector3 Clamp(Vector3 probe, Vector3 hipAtTouchdown, float maxReach,
                                    float reachFraction)
        {
            float horizontal = HorizontalBudget(maxReach, hipAtTouchdown.y, probe.y, reachFraction);

            Vector3 flat = probe - hipAtTouchdown;
            flat.y = 0f;
            if (flat.magnitude > horizontal) flat = flat.normalized * horizontal;

            return new Vector3(hipAtTouchdown.x + flat.x, probe.y, hipAtTouchdown.z + flat.z);
        }

        /// Where the hip will be when this swing lands. The leg is aimed at that, not at where the
        /// hip is at lift-off.
        ///
        /// `travel` is the machine's commanded WORLD velocity, not a speed along its nose. A crab
        /// walks sideways with its body square to the direction it is going, so a heading plus a
        /// scalar cannot express where its hips will be -- and a foothold aimed down the nose while
        /// the body slides sideways is a foothold the leg spends its whole stance dragging away
        /// from. Everything that reads the commanded twist has to read the same vector or the gait
        /// and the footholds disagree.
        public static Vector3 HipAtTouchdown(Vector3 hipNow, Vector3 travel, float swing)
            => hipNow + travel * swing;

        /// Where the hip will be when this swing lands, for a body that TURNS as well as travels.
        ///
        /// The overload above adds a displacement, which is exact for travel and, for rotation, the
        /// small-angle approximation carried in `drift`. That approximation has a hard limit and a
        /// pivoting biped walks straight past it: its feet cover a whole stride on an arc a fraction
        /// of the machine's own width, so ONE step can span hundreds of degrees, and a hip predicted
        /// by adding a tangent is then nowhere near where the hip actually goes. Everything
        /// downstream is measured against that prediction -- the reach clamp above, and the rest
        /// foothold the step is aimed from -- so both are carried by the same rotation instead.
        ///
        /// Exact at any angle, and identical to the overload above when `turn` is the identity.
        ///
        /// `pivot` is the body's origin, `travel` the whole displacement over the swing (not a
        /// velocity), `turn` the yaw the swing will cover.
        public static Vector3 HipAtTouchdown(Vector3 hipNow, Vector3 pivot, Vector3 travel,
                                             Quaternion turn)
            => pivot + travel + turn * (hipNow - pivot);
    }
}
