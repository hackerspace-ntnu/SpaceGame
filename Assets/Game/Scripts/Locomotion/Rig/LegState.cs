// Everything the gait and the IK need to know about one leg, while it is walking.
//
//   WalkerRig.Limb = rig chain + measured geometry
//   LegState       = a limb + gait slot + foothold + load share + swing bookkeeping
//
// Keeping gait state out of the limb type is the entire cost of supporting arms later: an arm is a
// limb driven by something that is not a gait, and it has no business carrying a phase offset.
//
// A class rather than a struct because it is mutated in place inside foreach loops all over the
// gait.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public class LegState
    {
        public WalkerRig.Limb Rig;

        /// What this leg is, in numbers. Measured once at Initialise.
        public LegMeasurement Measure;

        /// World contact point; fixed while planted. This is a contract with the ground -- if it
        /// moves while planted, the machine is skating.
        public Vector3 Foot;
        public Vector3 GroundNormal = Vector3.up;

        public Vector3 SwingFrom, SwingTo;

        /// This leg's slice of the gait cycle.
        public float PhaseOffset;

        public bool Swinging;
        /// 0..1 across the current swing.
        public float SwingT;
        /// Arc height for THIS step, sized to what it actually has to clear.
        public float SwingLift;

        /// Seconds this swing was given, FIXED AT LIFT-OFF. Recomputing it every frame from the
        /// current pace lets a step that began at speed stretch when the rider slows, so the foot
        /// is still in the air when its slice reopens, misses that slot and limps for a stride.
        public float SwingSpan;

        /// For spotting the moment the slice opens. Reading progress straight off the phase slice
        /// looks equivalent and is not: a leg stepped early is by definition outside its slice, so
        /// it would be planted again on the very next frame, teleporting the foot with no arc.
        public bool WasInSlice;

        /// The last solve could not reach this foot. Out of REACH, not merely clamped.
        public bool Unreachable;

        /// This foot is standing on something, whether or not its leg can still span the distance.
        /// Set by the support survey; read by the reach correction, which exists precisely for the
        /// legs that are grounded but over-stretched.
        public bool Grounded;

        /// Seconds this foot has been on the ground. Gates the step-early rule.
        public float StanceTime;

        /// Share of the body this foot is carrying, 0..1. Ramps in from touchdown and out again as
        /// lift-off comes up, so feet trade the weight rather than swapping it.
        public float Load;

        /// Reused across frames so the per-frame solve allocates nothing.
        public WalkerLimbSolver.Result Solved;
        public WalkerLimbSolver.Limits Limits;
    }
}
