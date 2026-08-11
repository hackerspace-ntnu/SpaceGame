// How a heading error becomes a twist. Pure functions, no state, no components.
//
// This lives in the locomotion assembly rather than on the driver because a driver has to sit in
// Assembly-CSharp -- it implements IRiderControllable and IMovementMotor, which live there -- and an
// asmdef cannot reference the default assembly, so nothing in Assembly-CSharp is reachable from the
// EditMode tests. Keeping the decisions here is what makes them testable at all.
//
// Both legged machines turned out to be using the same two rules with the numbers written out
// twice, which is how they came to be shared: the bird's driver had them factored out for testing
// and the station's had them inline, and they agreed to the digit.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public static class WalkerSteering
    {
        /// Heading error, in degrees, over which the turn command saturates. A legged machine
        /// corrects hard and briefly rather than carving a long arc.
        private const float TurnSaturationAngle = 45f;

        /// Yaw command in -1..1 for a heading error in degrees. Positive turns toward the target.
        public static float Turn(float headingErrorDegrees)
        {
            return Mathf.Clamp(headingErrorDegrees / TurnSaturationAngle, -1f, 1f);
        }

        /// Forward throttle in 0..1.
        ///
        /// Past turnInPlaceAngle the machine pivots on the spot instead of driving on, which is what
        /// stops it carving wide arcs around every waypoint.
        ///
        /// speedMultiplier is the intent's, and is what separates a roam from a charge: without it
        /// the throttle is wide open for every MoveToPosition and a wandering machine crosses the
        /// map at a dead run. Clamped to 1 because it is a scale on the throttle, not a way to ask
        /// the legs for more than they have -- AgentController already multiplies a ±10% drift into
        /// every intent.
        public static float Throttle(float headingErrorDegrees, float turnInPlaceAngle,
                                     float speedMultiplier)
        {
            if (Mathf.Abs(headingErrorDegrees) > turnInPlaceAngle) return 0f;
            return Mathf.Clamp01(speedMultiplier);
        }
    }
}
