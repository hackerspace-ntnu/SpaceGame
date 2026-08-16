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

        // --- climbing -------------------------------------------------------

        /// <summary>
        /// Gradient of the sand ahead: rise over run, positive uphill.
        ///
        /// Clamped, because the probe that measures the rise is a raycast and a raycast does not
        /// know the difference between a dune and the wall of a ruin. An unclamped grade off a
        /// vertical face is effectively infinite and stops the craft dead from one metre of
        /// scenery, which reads as a bug rather than as terrain.
        /// </summary>
        public const float MaxGrade = 4f;

        /// <summary>Rise over run, clamped to <see cref="MaxGrade"/>. Positive uphill.</summary>
        public static float ClimbGrade(float rise, float run)
        {
            if (run <= 1e-3f) return 0f;
            return Mathf.Clamp(rise / run, -MaxGrade, MaxGrade);
        }

        /// <summary>
        /// Speed the craft loses to climbing, as a deceleration in m/s².
        ///
        /// A sand yacht has no engine and very little mass to trade for height: it carries way up
        /// the windward face of a dune and runs out. So this is deliberately far harsher than the
        /// gravity term it is loosely modelled on — the rig makes around 1.4 m/s² flat out, and
        /// anything past <paramref name="stallGrade"/> has to dwarf that or the craft simply
        /// motors up cliffs at full speed, which is the behaviour being fixed.
        ///
        /// Downhill returns zero rather than a negative. Letting the craft accelerate down a face
        /// is a different feature with its own failure mode — a runaway that outruns its own
        /// steering — and the brief here is bleed-and-stop.
        ///
        /// Quadratic below the stall grade and cubic above it, matched at the join so there is no
        /// step: gentle rolling dunes cost a little, a real face costs everything, and neither
        /// edge feels like hitting a wall.
        /// </summary>
        /// <param name="grade">Rise over run along the heading, positive uphill.</param>
        /// <param name="climbCost">Master gain, m/s² at the stall grade.</param>
        /// <param name="stallGrade">Gradient the craft cannot carry way through. ~0.3 is 17°.</param>
        public static float SlopeDeceleration(float grade, float climbCost, float stallGrade)
        {
            if (grade <= 0f) return 0f;
            if (stallGrade <= 1e-4f) return climbCost;

            float t = grade / stallGrade;

            // Below the stall grade: quadratic, so the first few degrees are nearly free and the
            // craft still crosses ordinary rolling sand without feeling glued down.
            if (t <= 1f) return climbCost * t * t;

            // Above it: cubic off the same value and slope, so the cost runs away fast enough to
            // stop the craft within a few metres of hull travel.
            return climbCost * (1f + 2f * (t - 1f) + (t - 1f) * (t - 1f) * (t - 1f));
        }

        // --- coming to a stop -------------------------------------------------

        /// <summary>
        /// The deceleration that does not care how fast you are going: sand packing against the
        /// hull, bearings, the strut ploughing its furrow.
        ///
        /// This exists because the rest of the model cannot stop the craft. Every other resistance
        /// here goes as speed SQUARED, and a quadratic never reaches zero — furl every sail and the
        /// craft creeps on at a tenth of a metre a second for as long as anyone watches, which is
        /// exactly the "it still drifts with no drive" complaint. A constant term brings it to a
        /// genuine halt in a few seconds, and <see cref="StopThreshold"/> takes the last sliver.
        ///
        /// Blended out as the hull lifts clear, because a craft up on its foil is barely touching
        /// the sand and should coast a long way.
        /// </summary>
        /// <param name="rideHeight01">0 ploughing, 1 flying.</param>
        /// <param name="hullResistance">m/s² with the hull down.</param>
        /// <param name="foilResistance">m/s² up on the foil.</param>
        public static float RollingResistance(float rideHeight01, float hullResistance,
                                              float foilResistance)
        {
            return Mathf.Lerp(hullResistance, foilResistance, Mathf.Clamp01(rideHeight01));
        }

        /// <summary>
        /// Below this speed, a craft that is not being driven is simply stopped, m/s.
        ///
        /// Without it the last centimetre-per-second of way survives forever, and on a
        /// transform-driven hull that reads as the whole craft sliding imperceptibly across the
        /// pan — the single most common "it is still glitching" report.
        /// </summary>
        public const float StopThreshold = 0.15f;

        // --- steering ---------------------------------------------------------

        /// <summary>
        /// Turn rate the steered foil asks for, in degrees per second.
        ///
        /// This is the bicycle relation — a vehicle with a steered contact point travels a circle
        /// of radius <c>wheelbase / tan(steer)</c> — which is the right model here for one blunt
        /// reason: the craft has exactly one thing in the sand, and pointing it is what turns the
        /// hull. It also gives the two behaviours that make a steered vehicle feel like one: the
        /// radius is set by the wheel and not by the speed, and the rate goes to zero with the
        /// speed, so a stopped craft has no steerage at all.
        ///
        /// The wheelbase is NOT the foil's lever arm. That would make this craft turn inside its
        /// own length (4.65 m of lever puts an 18 m hull on a 10 m radius at half lock); the hull
        /// itself resists sliding sideways along its whole length, and the effective wheelbase is
        /// what that resistance adds up to. It is a tuning number measured in hull lengths.
        /// </summary>
        /// <param name="speed">Forward speed, m/s. Signed: making sternway reverses the helm.</param>
        /// <param name="steerAngle">Foil angle off the centreline, degrees, positive to starboard.</param>
        /// <param name="wheelbase">Effective distance between the steered foil and the hull's own
        /// centre of lateral resistance, metres.</param>
        public static float SteeredYawRate(float speed, float steerAngle, float wheelbase)
        {
            if (wheelbase <= 1e-3f) return 0f;
            float radians = speed * Mathf.Tan(steerAngle * Mathf.Deg2Rad) / wheelbase;
            return radians * Mathf.Rad2Deg;
        }

        /// <summary>
        /// The fastest the craft can be turned at this speed before the foil breaks out sideways,
        /// degrees per second.
        ///
        /// A turn of rate ω at speed v needs a lateral acceleration of v·ω, and the foil can only
        /// supply so much of that. This is what makes the craft understeer as it gets quick: the
        /// same handful of wheel that carves a tight circle at walking pace washes out into a long
        /// arc at forty metres a second, which is both what a sand yacht does and what stops the
        /// player spinning a seventeen-metre hull on the spot.
        /// </summary>
        /// <param name="speed">Speed, m/s.</param>
        /// <param name="maxLateralAcceleration">What the foil can hold sideways, m/s².</param>
        public static float GripLimitedYawRate(float speed, float maxLateralAcceleration)
        {
            float v = Mathf.Abs(speed);
            if (v < 0.05f) return float.PositiveInfinity;   // no lateral load to shed at a standstill
            return maxLateralAcceleration / v * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Turn rate the craft will actually settle at: what the foil asks for, limited by what it
        /// can hold, and never above the hull's own ceiling.
        /// </summary>
        public static float SteadyYawRate(float speed, float steerAngle, float wheelbase,
                                          float maxLateralAcceleration, float maxYawRate)
        {
            float asked = SteeredYawRate(speed, steerAngle, wheelbase);
            float grip = GripLimitedYawRate(speed, maxLateralAcceleration);
            float ceiling = Mathf.Min(grip, Mathf.Abs(maxYawRate));
            return Mathf.Clamp(asked, -ceiling, ceiling);
        }

        /// <summary>
        /// How much of the foil is still doing its job at this ride height, 0..1.
        ///
        /// The strut surfaces as the hull climbs it, so a craft flying at the top of its foil has
        /// far less blade in the sand to steer with. Floored well above zero rather than taken to
        /// the geometric truth: a foiler that becomes unsteerable at exactly the moment it is
        /// going fastest is not a design, it is a way to lose the craft.
        /// </summary>
        public static float SteeringAuthority(float rideHeight01, float flyingAuthority)
        {
            return Mathf.Lerp(1f, Mathf.Clamp01(flyingAuthority), Mathf.Clamp01(rideHeight01));
        }
    }
}
