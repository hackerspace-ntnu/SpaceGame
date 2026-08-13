// The four things a legged machine can differ in, and nothing else.
//
// LeggedLocomotion owns the order of a frame and every piece of arithmetic that is the same on
// every machine -- rig discovery, the gait clock, foothold resolution, ground probing, gravity, the
// IK call. What is genuinely different between a six-legged station and a running bird is only
// this: how far a leg can step, when each leg swings, where the body goes, and what the foot does.
//
// All four are plain C# classes. No MonoBehaviour, no scene, no rig, so the expensive bugs on
// record -- the collapsed stride budget, the step-early rule firing on two thirds of frames, the
// bob arriving late through a filter -- become one-line assertions.
//
// ─────────── invariant I1 ───────────
//
// NO POLICY MAY RETURN A STATE THAT STOPS THE MACHINE. The clock advances by distance travelled, so
// speed 0 means the gait never turns, no phase slice ever opens, and nothing can un-stick it. That
// is exactly the latch that broke the crawler rewrite and cost a session. A policy may step a leg
// early, refuse a foothold, or ask for a shorter stride. It may not gate motion on its own output.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Locomotion
{
    /// How far a leg can step, and how tall the machine stands to do it.
    public interface IStrideModel
    {
        /// How far this foot travels through the body frame per cycle.
        float StrideLength(in LegMeasurement m);

        /// Hip height while walking. A bird stands DOWN to buy back the reach its stride is made
        /// of; a station rides at the height it was modelled at and returns the rest height.
        float WorkingHipHeight(in LegMeasurement m);
    }

    /// When each leg swings.
    public interface IGaitPattern
    {
        /// Once, at Initialise. The layout is how a pattern works out which legs are diagonal,
        /// lateral, front or rear -- from where the feet actually are, not from leg indices, so it
        /// stays correct if the rig is re-authored.
        void Bind(IReadOnlyList<LegMeasurement> legs);

        /// This leg's slice of the cycle, 0..1.
        ///
        /// A CONTINUOUS FUNCTION of runBlend, never a table that gets reassigned at a threshold.
        /// Swapping tables mid-stride teleports a foot that is in the air; blending walks it over.
        float PhaseOffset(int leg, float runBlend);

        /// Fraction of the cycle a leg spends airborne.
        float Duty(float runBlend);

        /// May this leg step before its slice opens? The only adaptive rule in the gait, and so the
        /// only thing that can pull legs into step with each other -- which is why every
        /// implementation gates it on something.
        bool MayStepEarly(in StepEarlyRequest r);
    }

    /// Where the body goes.
    public interface IBodyMotion
    {
        /// Writes the target height into `pose.PathPos.y`, the body's rotation into
        /// `pose.Attitude`, and any bob or sway into `pose.DisplayOffset`.
        ///
        /// The display offset is added on top of the path and NEVER written back to it, or the
        /// lean integrates and walks the machine sideways off its own course. Keeping it out of the
        /// path is also what keeps it out of the fall test.
        void Pose(in BodyFrame f, in SupportState s, float dt, ref BodyPose pose);
    }

    /// What the foot does.
    public interface IFootStyle
    {
        /// Where a swinging foot is, `t` running 0 to 1 across the swing.
        Vector3 SwingPoint(Vector3 from, Vector3 to, float t, float lift);

        /// The normal the sole is laid against.
        ///
        /// Foot articulation is done by PITCHING THE NORMAL handed to the solver, never by driving
        /// the ankle. The solver places the CONTACT POINT and derives the ankle from it, so the
        /// foot pivots about the point it is standing on and a planted foot cannot slip.
        Vector3 SoleNormal(LegState leg, in WalkerLimbSolver.Frame frame);
    }

    // ─────────── what the policies are handed ───────────

    /// The body's pose for this frame, before gravity and smoothing have had their say.
    public struct BodyPose
    {
        /// In: where the path has got to. Out: the height the body motion is asking for. The base
        /// smooths toward it, or overrides it entirely when gravity has taken over.
        public Vector3 PathPos;

        /// Heading, in degrees. Owned by the base; a body motion reads it to build its attitude.
        public float Yaw;

        /// Bob and sway. Added on top of the path each frame and never accumulated.
        public Vector3 DisplayOffset;

        /// The body's full world rotation.
        public Quaternion Attitude;
    }

    /// What the feet are doing for the body this frame.
    public struct SupportState
    {
        /// How high the planted feet are holding the body. Already floored at the ground, so a
        /// machine whose feet are stranded in the air does not ride up on them.
        public float SupportHeight;

        /// The plane the grounded feet span, in the machine's own yaw frame -- so its normal reads
        /// directly as the pitch and roll of the ground under the body rather than as a world
        /// direction. Invalid on a machine with fewer than three feet down, or with them in a line.
        ///
        /// Fitted over every GROUNDED foot rather than only the carrying ones, and that distinction
        /// is the point of it: a leg stretched past its reach stops carrying, so a survey of the
        /// carriers alone drops exactly the leg whose plight the body needs to answer.
        public WalkerSupportPlane Plane;

        /// Planted feet that are actually carrying weight. Zero is what lets gravity take over.
        public int CarryingCount;

        /// Planted feet standing on something, whether or not their leg can still span the
        /// distance. What the support plane is fitted through.
        public int GroundedCount;

        /// Total load across those feet, 0..legCount.
        public float Load;

        /// Load-weighted mean of the carrying feet across the body's X. Taken from where the feet
        /// ARE rather than from a hand-signed sine, so it cannot lean the wrong way if the rig's
        /// left and right come in swapped -- and from the LOAD rather than a headcount, which is
        /// what makes it a curve that can be used unfiltered.
        public float LeanX;

        public float GroundY;
        public bool HasGround;
    }

    /// The machine's state this frame, as a body motion sees it.
    public struct BodyFrame
    {
        public Transform Body;
        public float CommandedSpeed;
        public float CommandedYawRate;
        public float MaxYawRate;
        /// How far into "running" the current speed is, 0..1.
        public float RunBlend;
        /// Fraction of top speed actually being worked at. Scales bob and lean, so a standing
        /// machine is still rather than idling up and down on the spot.
        public float Effort;
        public float Phase;
        public float Duty;
        public float RideHeight;
        /// The shortest leg's reach. Bob and lean are sized against it rather than against ride
        /// height, which on a model whose origin sits at foot level is ~0.
        public float LegReach;
    }

    /// A leg asking to step before its slice opens.
    public struct StepEarlyRequest
    {
        public int LegIndex;
        public int PlantedCount;
        public int LegCount;
        public float StanceTime;
        public float SwingDuration;
        /// Out of REACH, not merely clamped -- see WalkerLimbSolver.Result.Clamped.
        public bool Unreachable;
    }
}
