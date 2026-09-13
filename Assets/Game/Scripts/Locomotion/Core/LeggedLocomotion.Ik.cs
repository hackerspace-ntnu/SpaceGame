// Posing the legs onto the footholds the gait chose.
//
// The inverse kinematics itself is WalkerLimbSolver, which is analytic for the four-joint leg both
// shipping machines use: yaw the limb's plane onto the target, solve the two free links inside it,
// aim the tip at the ground. There is no iteration and nothing to converge, so nothing here can lag
// -- given a target, the pose is exact this frame.
//
// So this file is only the two things the solver has to be TOLD: where the foot is going, and which
// way the sole should be facing when it gets there. The second is the IFootStyle's call, and it is
// where a machine stops looking like something on stilts.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public abstract partial class LeggedLocomotion
    {
        private void SolveLegs()
        {
            int unreachable = 0;
            float worst = 0f;

            foreach (LegState leg in legs)
            {
                WalkerLimbGeometry g = leg.Rig.Geometry;
                var frame = new WalkerLimbSolver.Frame
                {
                    Hip = leg.Rig.Anchor.position,
                    YawAxis = body.TransformDirection(g.YawAxisBody).normalized,
                    RestFwd = body.TransformDirection(g.RestFwdBody).normalized,
                };

                WalkerLimbSolver.Solve(frame, g, leg.Limits, leg.Foot,
                                       footStyle.SoleNormal(leg, frame), ref leg.Solved);
                WalkerLimbSolver.Apply(leg.Rig, leg.Solved);

                // Out of REACH, not merely clamped. Result.Clamped is raised whenever any single
                // joint is sitting on a limit, which on a bird is most of the time and says nothing
                // about whether the foot can be honoured -- reading it as unreachable had the
                // step-early rule firing on two thirds of all frames, which syncs the legs and
                // turns a walk into a hop.
                // Tripping this a few percent EARLY -- at 0.97, so the step is asked for before the
                // foot visibly separates -- was tried and measured, and it is a trap. The six-legged
                // machines gate step-early on planted count rather than on stance time, so an
                // earlier flag means far more early steps: the crawler went from reach 0.95 and no
                // unreachable frames to reach 1.65, 63 unreachable frames and 43 frames of actual
                // FALLING while turning. It stays at the real limit.
                leg.Unreachable = leg.Solved.ReachFraction > 1f && !leg.Swinging;

                if (leg.Unreachable) unreachable++;
                worst = Mathf.Max(worst, leg.Solved.ReachFraction);
            }

            diagnostics.UnreachableLegs = unreachable;
            diagnostics.WorstReachFraction = worst;
        }

        /// Poses every arm at whatever target it is currently carrying.
        ///
        /// Deliberately NOT part of `Step`. The base owns the frame order for the things that move
        /// the body, and an arm moves nothing -- it is posed onto a target the subclass wrote, so
        /// the subclass is what says when the targets are ready. Calling this before they are
        /// written poses the arms at their rest contact, which is inert rather than wrong.
        protected void SolveArms()
        {
            for (int i = 0; i < arms.Count; i++) arms[i].Solve();
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || !Application.isPlaying || !ready) return;

            foreach (LegState leg in legs)
            {
                float scale = Mathf.Max(leg.Measure.LegReach * 0.03f, 0.02f);

                Gizmos.color = leg.Swinging ? Color.yellow : (leg.Unreachable ? Color.red : Color.green);
                Gizmos.DrawSphere(leg.Foot, scale);

                Gizmos.color = new Color(1f, 0.4f, 0f, 0.6f);
                Gizmos.DrawLine(leg.Rig.Anchor.position, leg.Foot);

                // The arc this foot can sweep, which is the leg's whole stride.
                Gizmos.color = new Color(0f, 0.6f, 1f, 0.5f);
                Gizmos.DrawWireSphere(body.TransformPoint(leg.Measure.HomeLocal),
                                      leg.Measure.StrideLength * 0.5f);
            }
        }
    }
}
