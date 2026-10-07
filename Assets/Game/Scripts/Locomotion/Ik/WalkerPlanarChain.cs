// Solving a chain of N links inside the limb's own plane.
//
// Stage 1 of the IK yaws that plane onto the target; everything here happens inside it, in 2D, and
// does not care how many joints follow. What varies is only how many links the solve is FREE to
// choose. The last segment's direction is prescribed -- a foot is laid along the ground normal --
// so an N-segment limb is an (N-1)-link problem, which is the classic reduction.
//
//   free links | case                            | method
//   -----------+---------------------------------+-------------------------------------------
//       2      | today's leg: ostrich, crawler   | the existing analytic two-link solve
//       1      | a stubby two-joint leg          | direct aim; exact when reachable
//      >=3     | a five-joint insect leg, an arm | CCD seeded from the rest pose
//
// The two-link path is the load-bearing row. Both shipping machines take it, and it is the SAME
// arithmetic they took before this file existed -- not an equivalent reimplementation. Changing it
// is a regression, not a refactor.
//
// The general path is CCD with a FIXED iteration budget rather than a convergence test. A gait
// cannot afford a solver whose cost depends on how awkward the target is, and a fixed budget makes
// the result deterministic: the same target gives the same pose every time, on every machine.
//
// Nothing here allocates. Lengths and rest angles are read straight off the measured geometry, and
// the angles are written into the caller's buffer -- which the locomotion allocates once per limb.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public static class WalkerPlanarChain
    {
        /// Polishing passes after the seed. The seed is already exact where the limits allow it, so
        /// these only repair joints the limits moved -- which is a small correction, and eight of
        /// them is comfortably enough for it.
        public const int CcdIterations = 8;

        /// Bisection steps used to fit the seed arc. Twenty-four halvings resolve the curl far below
        /// float precision, and each one is a single walk of a chain that is at most a handful of
        /// joints -- far cheaper than the CCD passes it saves.
        public const int SeedBisections = 24;

        /// Below this the target is treated as reached and the sweep stops early.
        private const float Tolerance = 1e-4f;

        /// Places the end of the first `count` links on `target`, measured from the first joint in
        /// the plane's (fwd, up) coordinates. `angles` receives absolute plane angles for the same
        /// range and must be at least `count` long.
        public static void Solve(in WalkerLimbGeometry g, in WalkerLimbSolver.Limits limits,
                                 int count, Vector2 target, float[] angles, out bool clamped)
        {
            clamped = false;
            if (count <= 0) return;

            if (count == 1)
            {
                angles[0] = target.sqrMagnitude > 1e-12f ? Mathf.Atan2(target.y, target.x) : 0f;
                clamped = Mathf.Abs(target.magnitude - g.Pitch[0].Length) > 1e-3f;
                return;
            }

            if (count == 2)
            {
                // Bit-for-bit the solve both shipping machines have always taken.
                WalkerLimbSolver.SolveTwoLink(g.Pitch[0].Length, g.Pitch[1].Length, target,
                                              g.BendSign, out angles[0], out angles[1], out clamped);
                return;
            }

            SolveCcd(g, limits, count, target, angles, out clamped);
        }

        /// An arc seed, then cyclic coordinate descent to polish it.
        ///
        /// CCD alone converges badly here, and measurement is the reason this is not just CCD: from
        /// a rest pose seed it was still 0.47 units out on an 18-unit chain after 24 passes, because
        /// its weakness is precisely a start far from the answer. Iterating harder buys very little
        /// -- 64 passes to get under a centimetre -- so the budget is spent on a better START.
        ///
        /// The seed bends the chain into a SYMMETRIC ARC and solves that arc exactly, which it can
        /// because an arc has one free parameter and the tip's distance from the root falls
        /// monotonically as the arc curls: bisect the curl to match the target's range, then rotate
        /// the whole chain rigidly to match its bearing. Both are then exact, before any joint limit
        /// has had a say. CCD's remaining job is only to repair what the limits broke, which is a
        /// small correction and what it is good at.
        ///
        /// Which WAY the chain curls comes from the rest pose, which is why this needs no stored
        /// bend sign: the modelled fan already says which way the limb folds.
        private static void SolveCcd(in WalkerLimbGeometry g, in WalkerLimbSolver.Limits limits,
                                     int count, Vector2 target, float[] angles, out bool clamped)
        {
            float total = 0f;
            for (int i = 0; i < count; i++) total += g.Pitch[i].Length;
            clamped = target.magnitude > total;

            SeedArc(g, count, target, angles);
            ApplyLimits(g, limits, count, angles);

            for (int pass = 0; pass < CcdIterations; pass++)
            {
                // Sweep from the joint nearest the tip back to the root. The far joints make the
                // small corrections and the near ones the large ones, so this order converges in
                // far fewer passes than root-to-tip on the same budget.
                for (int j = count - 1; j >= 0; j--)
                {
                    Vector2 pivot = JointPosition(g, angles, j);
                    Vector2 end = JointPosition(g, angles, count);

                    Vector2 have = end - pivot;
                    Vector2 want = target - pivot;
                    if (have.sqrMagnitude < 1e-12f || want.sqrMagnitude < 1e-12f) continue;

                    float turn = Mathf.Atan2(want.y, want.x) - Mathf.Atan2(have.y, have.x);

                    // Turning a joint carries every joint below it, because these are absolute
                    // plane angles rather than relative ones.
                    for (int k = j; k < count; k++) angles[k] += turn;
                }

                // Once per pass, not once per joint. Clamping inside the sweep undoes the alignment
                // the joint just made -- the clamp moves that joint's ANCESTORS too -- so every
                // subsequent joint in the same sweep is correcting against a pose that has already
                // moved under it, and the chain converges an order of magnitude more slowly.
                ApplyLimits(g, limits, count, angles);

                if (Vector2.Distance(JointPosition(g, angles, count), target) < Tolerance) break;
            }

            if (Vector2.Distance(JointPosition(g, angles, count), target) > 1e-3f) clamped = true;
        }

        /// Bends the chain into the symmetric arc whose tip lands on `target`.
        ///
        /// Exact whenever the target is inside the chain's reach, and it needs no derivative and no
        /// matrix: curl the arc harder and the tip comes closer to the root, monotonically, from
        /// the chain's full length down to nothing. So the range is a bisection, and the bearing is
        /// a rigid rotation afterwards -- which cannot disturb the range it just solved.
        private static void SeedArc(in WalkerLimbGeometry g, int count, Vector2 target, float[] angles)
        {
            // Which way the limb folds, taken from the rest pose's own curvature.
            float curvature = g.Pitch[count - 1].RestAngle - g.Pitch[0].RestAngle;
            float sign = curvature < 0f ? -1f : 1f;

            float wanted = target.magnitude;
            float middle = (count - 1) * 0.5f;

            // Curling by a full turn spread across the chain closes it into a polygon, so the tip
            // is back at the root: that is the far end of the bracket, whatever the lengths are.
            float low = 0f;
            float high = 2f * Mathf.PI / count;

            float curl = 0f;
            for (int step = 0; step < SeedBisections; step++)
            {
                curl = (low + high) * 0.5f;
                if (ArcRadius(g, count, curl * sign, middle) > wanted) low = curl;
                else high = curl;
            }
            curl = (low + high) * 0.5f;

            for (int i = 0; i < count; i++) angles[i] = curl * sign * (i - middle);

            // The arc is built about an arbitrary bearing; turn the whole thing to face the target.
            // A rigid rotation leaves every segment's length and every joint's relative angle
            // alone, so the range solved above survives it exactly.
            Vector2 tip = JointPosition(g, angles, count);
            if (tip.sqrMagnitude < 1e-12f || target.sqrMagnitude < 1e-12f) return;

            float turn = Mathf.Atan2(target.y, target.x) - Mathf.Atan2(tip.y, tip.x);
            for (int i = 0; i < count; i++) angles[i] += turn;
        }

        /// How far the tip of a `curl`-per-joint arc sits from the root.
        private static float ArcRadius(in WalkerLimbGeometry g, int count, float curl, float middle)
        {
            float x = 0f, y = 0f;
            for (int i = 0; i < count; i++)
            {
                float a = curl * (i - middle);
                x += Mathf.Cos(a) * g.Pitch[i].Length;
                y += Mathf.Sin(a) * g.Pitch[i].Length;
            }
            return Mathf.Sqrt(x * x + y * y);
        }

        /// Hold every joint inside its travel, measured the way a real hinge constrains it: the
        /// child's angle relative to its parent, about the rest pose.
        ///
        /// Run after every pass rather than once at the end. A chain allowed to wander outside its
        /// limits and then folded back at the end lands somewhere that is neither on the target nor
        /// a pose the linkage holds; correcting as it goes keeps every pass working from a legal
        /// configuration.
        private static void ApplyLimits(in WalkerLimbGeometry g, in WalkerLimbSolver.Limits limits,
                                        int count, float[] angles)
        {
            for (int i = 0; i < count; i++)
            {
                float parent = i == 0 ? 0f : angles[i - 1];
                float restParent = i == 0 ? 0f : g.Pitch[i - 1].RestAngle;
                float restRelative = g.Pitch[i].RestAngle - restParent;
                float limit = limits.PitchAt(i);

                float relative = Mathf.DeltaAngle(
                    0f, (angles[i] - parent - restRelative) * Mathf.Rad2Deg);
                angles[i] = parent + restRelative +
                            Mathf.Clamp(relative, -limit, limit) * Mathf.Deg2Rad;
            }
        }

        /// Where joint `index` sits, walking the chain out from the first joint. `index == count`
        /// is the end of the last link.
        public static Vector2 JointPosition(in WalkerLimbGeometry g, float[] angles, int index)
        {
            Vector2 p = Vector2.zero;
            for (int i = 0; i < index; i++)
                p += new Vector2(Mathf.Cos(angles[i]), Mathf.Sin(angles[i])) * g.Pitch[i].Length;
            return p;
        }
    }
}
