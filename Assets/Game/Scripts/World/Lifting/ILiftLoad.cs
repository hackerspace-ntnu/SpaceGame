// Anything a body takes hold of with both hands and hauls: a heavy prop lifted by one end (Liftable), or a tied-up
// person heaved onto the shoulder (CaptiveHaul).
//
// LiftCarrier is the body's half and is the same for all of them: the phases (take hold, haul, let go), the timing, the
// arms reaching for the grips, where in front of the body the hands are carried. What the load does with the hands is the
// load's own business, and differs: a prop is a rigid thing posed from the hands every frame, a captive is a body laid over
// the carrier's shoulder with the hands wrapped round its legs.
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>A load a <see cref="LiftCarrier"/> can hold. Implemented by a component on the load.</summary>
    public interface ILiftLoad
    {
        // ReSharper disable once InconsistentNaming — implemented by Component.transform.
        Transform transform { get; }

        // ReSharper disable once InconsistentNaming — implemented by Component.gameObject.
        GameObject gameObject { get; }

        /// <summary>Whether there is anything to take hold of: a prop with measured grips, a captive that is still tied.</summary>
        bool CanBeHeld { get; }

        /// <summary>How quickly the load's far end swings round behind a turn, per second.</summary>
        float SwingRate { get; }

        /// <summary>The furthest the load may trail behind the way the carrier faces, in degrees.</summary>
        float MaxSwingLag { get; }

        /// <summary>How far into the take-hold clip the hands close on the grips, 0-1.</summary>
        float GripFraction { get; }

        /// <summary>The way the load lies, near end to far end, in the world: where a swing starts from.</summary>
        Vector3 Axis { get; }

        /// <summary>Where the hands close on a load lying at <paramref name="pose"/> — for a load that does not move as a rigid thing, where they close now.</summary>
        Vector3 GripOf(Pose pose);

        /// <summary>The two points the hands close on this frame, after <see cref="Hold"/>.</summary>
        void Grips(Transform carrier, Vector3 grip, out Vector3 left, out Vector3 right);

        /// <summary>The load is in <paramref name="by"/>'s hands now, or (null) in nobody's.</summary>
        void SetHeldBy(LiftCarrier by);

        /// <summary>Follow the hands for this frame. Every machine, from the carrier's LateUpdate.</summary>
        void Hold(in LiftFrame frame);

        /// <summary>Where the load would come to rest if it were let go of here.</summary>
        Pose RestFromHere();
    }

    /// <summary>One frame of a hold: where the hands want the near end, and how far through taking hold or letting go.</summary>
    public readonly struct LiftFrame
    {
        /// <summary>Where the hands hold the near end this frame.</summary>
        public readonly Vector3 Grip;

        /// <summary>The flat heading the load trails along.</summary>
        public readonly Vector3 Heading;

        /// <summary>The fixed carry point in front of the body, whatever phase the hold is in.</summary>
        public readonly Vector3 CarryPoint;

        /// <summary>The load's pose when the hands took hold.</summary>
        public readonly Pose From;

        /// <summary>The pose it is being let go onto.</summary>
        public readonly Pose Rest;

        /// <summary>0 while the hands are still closing on the load, 1 once it moves with them.</summary>
        public readonly float FromStart;

        /// <summary>0 while held, rising to 1 as it settles onto <see cref="Rest"/>.</summary>
        public readonly float ToRest;

        /// <summary>
        /// How far the hands have it up off the ground, 0 to 1: rising from the moment they close on it to the end of the
        /// take-hold, 1 while carried, falling as it is set down.
        /// </summary>
        public readonly float Raised;

        public LiftFrame(Vector3 grip, Vector3 heading, Vector3 carryPoint, Pose from, Pose rest, float fromStart, float toRest,
                         float raised)
        {
            Grip = grip;
            Heading = heading;
            CarryPoint = carryPoint;
            From = from;
            Rest = rest;
            FromStart = fromStart;
            ToRest = toRest;
            Raised = raised;
        }
    }
}
