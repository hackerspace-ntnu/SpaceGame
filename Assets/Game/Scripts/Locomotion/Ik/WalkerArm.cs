// A limb that is posed at a point rather than walked on.
//
//   WalkerRig.Limb = rig chain + measured geometry
//   WalkerArm      = a limb + a world target + a tip direction
//   LegState       = a limb + gait slot + foothold + load share + swing bookkeeping
//
// That is the whole of it, and the smallness is the point. `LegState` is the larger half of the
// same seam: everything in it that is not in here is gait state, which an arm has no business
// carrying. Until this existed `LeggedLocomotion.Initialise` turned EVERY limb the rig returned
// into a `LegState`, so a machine with arms walked on them.
//
// This is deliberately not a manipulation system. There is no reach planning, no collision, no
// pose blending and no notion of grasping -- a caller says where the hand goes and which way it
// points, and the existing IK answers. Whatever drives the target (a gait clock's phase, a gaze,
// an idle sway) lives in the robot's own component, exactly as `IBodyMotion` does for the body.
//
// A plain class, no MonoBehaviour, so it is testable with a procedural rig and no scene.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public class WalkerArm
    {
        /// The bones, and what they were measured as. Shared with nothing.
        public readonly WalkerRig.Limb Rig;

        /// The transform the limb's geometry was measured against, and therefore the one its yaw
        /// axis and rest plane are expressed in. The same body the legs are attached to.
        public readonly Transform Body;

        /// Where the tip's contact point should be, in WORLD space. The solver places the contact
        /// point -- not the last hinge -- so this is the palm of the hand, not the wrist.
        public Vector3 Target;

        /// Which way the last segment points at the target: from the last hinge toward the contact.
        /// On a leg this is straight down, and the sole's normal is its reverse; on an arm it is
        /// whichever way the hand is meant to be facing.
        ///
        /// A zero vector is read as "straight down" rather than producing a NaN heading.
        public Vector3 TipDirection = Vector3.down;

        /// Joint travel. Public because an arm's travel has nothing to do with a leg's -- a
        /// shoulder reaches behind itself and a hip does not -- and the machine that owns the arm
        /// is the only thing that knows.
        public WalkerLimbSolver.Limits Limits;

        /// Last solve. Allocated once, here, and written in place every frame after: invariant I3,
        /// nothing in the per-frame path allocates.
        public WalkerLimbSolver.Result Solved;

        /// Forward-kinematics scratch for `Tip`, sized once for the same reason.
        private readonly Vector3[] poseBuffer;

        /// The frame the last solve was taken in. Kept so `Tip` reports where THAT solve put the
        /// hand, rather than re-deriving it from a body that may have moved since.
        private WalkerLimbSolver.Frame lastFrame;

        public WalkerArm(WalkerRig.Limb rig, Transform body, in WalkerLimbSolver.Limits limits)
        {
            Rig = rig;
            Body = body;
            Limits = limits;

            int count = Mathf.Max(1, rig.Geometry.PitchCount);
            Solved = new WalkerLimbSolver.Result { Pitch = new float[count] };
            poseBuffer = new Vector3[count + 1];

            // Rest pose, so an arm nobody drives holds the pose it was modelled in rather than
            // being flung at the world origin on the first solve.
            Target = rig.ContactPoint;
            lastFrame = CurrentFrame();
        }

        /// How far the target is as a fraction of what the linkage can span. Above 1 the hand is
        /// visibly detached from the target, which is the arm's version of `LegState.Unreachable`.
        public float ReachFraction => Solved.ReachFraction;
        public bool Unreachable => Solved.ReachFraction > 1f;

        /// Where the last solve actually put the contact point. Walked out through the same
        /// forward kinematics the gizmos and the solver's own `SoleFromResult` use, so "did the arm
        /// arrive" is answered by the pose rather than by the angles it was asked for.
        public Vector3 Tip
            => WalkerLimbPose.Resolve(lastFrame, Rig.Geometry, Solved, poseBuffer).Sole;

        /// Poses the limb at `Target`. One analytic solve and one write of local rotations; there
        /// is nothing to converge and nothing to lag.
        public void Solve()
        {
            lastFrame = CurrentFrame();

            // The solver is written in terms of a GROUND NORMAL because its first consumer was a
            // foot, and the last segment is laid along the reverse of it. An arm thinks in terms of
            // where the hand points, which is that reverse -- so the conversion belongs here, once,
            // rather than in every caller.
            Vector3 dir = TipDirection.sqrMagnitude > 1e-8f ? TipDirection.normalized : Vector3.down;

            WalkerLimbSolver.Solve(lastFrame, Rig.Geometry, Limits, Target, -dir, ref Solved);
            WalkerLimbSolver.Apply(Rig, Solved);
        }

        /// Where the limb hangs off the body this frame. Identical in construction to the one
        /// `SolveLegs` builds -- a limb is a limb, and only what drives the target differs.
        private WalkerLimbSolver.Frame CurrentFrame()
        {
            WalkerLimbGeometry g = Rig.Geometry;
            return new WalkerLimbSolver.Frame
            {
                Hip = Rig.Anchor.position,
                YawAxis = Body.TransformDirection(g.YawAxisBody).normalized,
                RestFwd = Body.TransformDirection(g.RestFwdBody).normalized,
            };
        }
    }
}
