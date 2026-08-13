using UnityEngine;

namespace SpaceGame.Vehicles.DuneFoil
{
    /// <summary>
    /// The foil's side of the craft, as pure functions: how high it rides and how hard the
    /// sand holds it back. Split out from <see cref="FoilLift"/> for the same reason
    /// <see cref="SailAerodynamics"/> is split out — so it can be reasoned about and tested
    /// without a scene.
    /// </summary>
    public static class FoilPhysics
    {
        /// <summary>
        /// Ride height in metres above the sand.
        ///
        /// Foil lift goes as speed squared and has to carry a fixed weight, so there is a
        /// take-off speed below which the craft simply sits on its hull. Above it the craft
        /// climbs the strut and tops out when the foil tip reaches the surface — any higher
        /// and the foil is out of the sand making no lift at all, which is the real limit
        /// rather than an arbitrary clamp.
        /// </summary>
        /// <param name="speed">Horizontal speed, m/s.</param>
        /// <param name="takeoffSpeed">Speed at which the hull first leaves the sand.</param>
        /// <param name="maxRideHeight">Strut length: height at which the foil tip surfaces.</param>
        public static float RideHeight(float speed, float takeoffSpeed, float maxRideHeight)
        {
            if (speed <= takeoffSpeed || takeoffSpeed <= 0f) return 0f;

            // lift/weight ratio beyond the point where they balance
            float excess = (speed * speed) / (takeoffSpeed * takeoffSpeed) - 1f;

            // Saturating rather than linear: the last metres come slowly, so the craft settles
            // onto its foil instead of shooting up the strut the moment it passes take-off.
            //
            // The constant sets how fast it climbs, and it matters more than it looks. Sand
            // drag falls as the hull lifts clear, so height and speed feed each other; make the
            // climb too gradual and the craft is trapped, ploughing at a third of its height
            // with drag it can never out-accelerate. At 0.4 the hull is most of the way up by
            // twice take-off speed, which breaks that loop while keeping the transition
            // something the player earns.
            const float climbRate = 0.4f;
            float t = excess / (excess + climbRate);
            return Mathf.Clamp(t * maxRideHeight, 0f, maxRideHeight);
        }

        /// <summary>
        /// Drag from the sand, as a deceleration in m/s².
        ///
        /// Two regimes, and the gap between them is the whole point of a foiler: dragging a
        /// whole hull through sand is enormously more expensive than slicing one strut through
        /// it. <paramref name="rideHeight01"/> blends between them, so getting up onto the foil
        /// is a real and rewarding transition rather than a cosmetic one.
        /// </summary>
        public static float SandDrag(float speed, float rideHeight01, float hullDrag, float foilDrag)
        {
            float coefficient = Mathf.Lerp(hullDrag, foilDrag, Mathf.Clamp01(rideHeight01));
            return coefficient * speed * speed;
        }

        /// <summary>
        /// How much of the craft's sideways motion the foil refuses. A deeply immersed foil
        /// grips hard; as the craft rises and less strut is buried, it slips more.
        ///
        /// Without this the craft would simply be blown sideways and could never sail upwind:
        /// resisting leeway is what turns a sideways force into forward motion.
        /// </summary>
        public static float LateralGrip(float rideHeight01, float gripSubmerged, float gripFlying)
        {
            return Mathf.Lerp(gripSubmerged, gripFlying, Mathf.Clamp01(rideHeight01));
        }
    }
}
