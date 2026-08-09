// How a heading error becomes a twist. Pure functions, no state, no components.
//
// This lives in the Ostrich assembly rather than on OstrichDriver because the driver has to sit in
// Assembly-CSharp -- it implements IRiderControllable and IMovementMotor, which live there -- and
// an asmdef cannot reference the default assembly, so nothing in Assembly-CSharp is reachable from
// the EditMode tests. Keeping the decisions here is what makes them testable at all.
using UnityEngine;

public static class OstrichSteering
{
    /// Heading error, in degrees, over which the turn command saturates. A bird this leggy corrects
    /// hard and briefly rather than carving a long arc.
    private const float TurnSaturationAngle = 45f;

    /// Yaw command in -1..1 for a heading error in degrees. Positive turns toward the target.
    public static float Turn(float headingErrorDegrees)
    {
        return Mathf.Clamp(headingErrorDegrees / TurnSaturationAngle, -1f, 1f);
    }

    /// Forward throttle in 0..1.
    ///
    /// Past turnInPlaceAngle the bird pivots on the spot instead of running on, which is what stops
    /// it from carving wide arcs around every waypoint.
    ///
    /// speedMultiplier is the intent's, and is what separates a roam from a charge: without it the
    /// throttle is wide open for every MoveToPosition and a wandering bird crosses the map at a
    /// dead run. Clamped to 1 because it is a scale on the throttle, not a way to ask the legs for
    /// more than they have -- AgentController already multiplies a ±10% drift into every intent.
    public static float Throttle(float headingErrorDegrees, float turnInPlaceAngle,
                                 float speedMultiplier)
    {
        if (Mathf.Abs(headingErrorDegrees) > turnInPlaceAngle) return 0f;
        return Mathf.Clamp01(speedMultiplier);
    }
}
