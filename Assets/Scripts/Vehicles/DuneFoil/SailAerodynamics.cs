using UnityEngine;

namespace SpaceGame.Vehicles.DuneFoil
{
    /// <summary>
    /// The sailing rules, as pure functions. Nothing here touches a transform, a component or
    /// a frame, so the whole model is readable in one file and testable without a scene.
    ///
    /// Everything works in the craft's horizontal plane. A sail is a wing: it makes lift
    /// perpendicular to the flow and drag along it, and what drives the craft forward is the
    /// component of that total force along the heading. Sailing upwind works because lift is
    /// large and roughly sideways, and the foil refuses to let the craft slide sideways.
    /// </summary>
    public static class SailAerodynamics
    {
        /// <summary>Below this angle of attack the sail is not filled — it luffs and flaps.</summary>
        public const float LuffAngle = 5f;

        /// <summary>Angle of attack of peak lift. Past this, flow starts to separate.</summary>
        public const float PeakLiftAngle = 18f;

        /// <summary>Past this the sail is stalled: drag dominates and lift collapses.</summary>
        public const float StallAngle = 35f;

        /// <summary>
        /// Half-width of the no-go zone, for warnings and UI.
        ///
        /// Advisory only — nothing here switches off inside it. The force model produces the
        /// no-go zone by itself, smoothly, through induced drag: at 45 degrees off the wind the
        /// craft makes about two thirds of its best speed, by 25 degrees about a third, and by
        /// 15 degrees it is stopped. That is how a real polar behaves; a hard cutoff at a fixed
        /// angle is not. Use this to tell the player they are pinching, not to decide physics.
        /// </summary>
        public const float NoGoHalfAngle = 45f;

        /// <summary>
        /// Apparent wind: what the craft actually feels, true wind minus its own velocity.
        /// Using this rather than true wind everywhere downstream is what makes speed shift
        /// the felt wind forward and makes tacking behave the way sailors expect.
        /// </summary>
        public static Vector3 ApparentWind(Vector3 trueWind, Vector3 craftVelocity)
        {
            return Flatten(trueWind - craftVelocity);
        }

        /// <summary>Drops the vertical component; all of the sailing model is planar.</summary>
        public static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>
        /// Signed angle in degrees from <paramref name="from"/> to <paramref name="to"/> about
        /// world up. Positive is clockwise seen from above (i.e. to starboard).
        /// </summary>
        public static float SignedAngle(Vector3 from, Vector3 to)
        {
            from = Flatten(from);
            to = Flatten(to);
            if (from.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-8f) return 0f;
            return Vector3.SignedAngle(from, to, Vector3.up);
        }

        /// <summary>
        /// Lift coefficient against angle of attack, in degrees, unsigned.
        ///
        /// Three regimes, which are the three things a sailor feels: nothing below the luffing
        /// threshold, a ramp to peak lift, then a collapse through the stall into a flat plate.
        /// </summary>
        public static float LiftCoefficient(float angleOfAttack)
        {
            float a = Mathf.Abs(angleOfAttack);
            if (a <= LuffAngle)
            {
                // shaking, not driving: a little residual as it fills
                return Mathf.Lerp(0f, 0.15f, a / LuffAngle);
            }
            if (a <= PeakLiftAngle)
            {
                float t = Mathf.InverseLerp(LuffAngle, PeakLiftAngle, a);
                return Mathf.Lerp(0.15f, 1.35f, Mathf.Sin(t * Mathf.PI * 0.5f));
            }
            if (a <= StallAngle)
            {
                float t = Mathf.InverseLerp(PeakLiftAngle, StallAngle, a);
                return Mathf.Lerp(1.35f, 0.55f, t * t);
            }
            // fully separated: a stalled sail is a bag, and past 90 degrees it is a parachute
            float post = Mathf.InverseLerp(StallAngle, 90f, a);
            return Mathf.Lerp(0.55f, 0f, post);
        }

        /// <summary>
        /// How much drag comes with making lift. Higher means a rig that points worse.
        /// A sail is a very low aspect ratio wing, so this is large by aircraft standards.
        /// </summary>
        public const float InducedDragFactor = 0.16f;

        /// <summary>
        /// Profile drag against angle of attack — the drag the sail has from its shape alone.
        /// Low when the flow is attached, and by 90 degrees the sail is a flat plate held across
        /// the wind, which is exactly how a run downwind works.
        /// </summary>
        public static float DragCoefficient(float angleOfAttack)
        {
            float a = Mathf.Abs(angleOfAttack);
            float t = Mathf.Clamp01(a / 90f);
            return Mathf.Lerp(0.08f, 1.6f, t * t);
        }

        /// <summary>
        /// Total drag: profile drag plus induced drag, which grows as the square of the lift.
        ///
        /// This term is what creates the no-go zone. Close to the wind, drive is the small
        /// difference between a large forward lift component and a large rearward drag one;
        /// making lift costs drag, so as the craft points higher that difference goes negative
        /// and no trim can save it. Without an induced term, pointing high is free and the
        /// craft would happily sail at 20 degrees off the wind, which no sailing craft can do.
        /// </summary>
        public static float TotalDragCoefficient(float angleOfAttack)
        {
            float cl = LiftCoefficient(angleOfAttack);
            return DragCoefficient(angleOfAttack) + InducedDragFactor * cl * cl;
        }

        /// <summary>
        /// Force a single sail makes, in world space.
        /// </summary>
        /// <param name="apparentWind">Apparent wind vector, world space, horizontal.</param>
        /// <param name="sailNormal">
        /// Unit normal of the sail's chord plane, world space, horizontal. The sail's own facing.
        /// </param>
        /// <param name="area">Sail area in square metres.</param>
        /// <param name="airDensity">Air density. Tuning knob as much as a physical constant.</param>
        /// <param name="angleOfAttack">Reported back for the caller's shader and UI.</param>
        public static Vector3 SailForce(Vector3 apparentWind, Vector3 sailNormal, float area,
                                        float airDensity, out float angleOfAttack)
        {
            angleOfAttack = 0f;
            apparentWind = Flatten(apparentWind);
            sailNormal = Flatten(sailNormal);

            float windSpeed = apparentWind.magnitude;
            if (windSpeed < 0.05f || sailNormal.sqrMagnitude < 1e-6f) return Vector3.zero;

            Vector3 windDir = apparentWind / windSpeed;
            sailNormal.Normalize();

            // Chord lies perpendicular to the normal. The angle of attack is between the chord
            // and the flow; measuring off the normal and folding to +/-90 gets there directly
            // and stays continuous as the sail passes head to wind.
            float normalToWind = Vector3.Angle(sailNormal, windDir);
            angleOfAttack = 90f - Mathf.Abs(90f - normalToWind);

            // Which side is the wind hitting? That decides which way lift points.
            float side = Mathf.Sign(Vector3.Dot(sailNormal, windDir));
            if (Mathf.Approximately(side, 0f)) side = 1f;

            float q = 0.5f * airDensity * windSpeed * windSpeed * area;
            float cl = LiftCoefficient(angleOfAttack);
            float cd = TotalDragCoefficient(angleOfAttack);

            // Lift acts perpendicular to the flow, in the plane, on the low-pressure side.
            Vector3 liftDir = Vector3.Cross(Vector3.up, windDir).normalized * -side;
            return liftDir * (cl * q) + windDir * (cd * q);
        }

        /// <summary>
        /// True if the craft's heading is inside the no-go zone and cannot be sailed.
        /// <paramref name="windFrom"/> is the direction the wind blows *from*.
        /// </summary>
        public static bool IsInNoGoZone(Vector3 heading, Vector3 windFrom)
        {
            return Mathf.Abs(SignedAngle(heading, windFrom)) < NoGoHalfAngle;
        }

        /// <summary>
        /// The angle the sail wants to sit at if left free: it weathervanes to trail the wind.
        /// Returned as a signed angle off the craft's heading, so a sheet limit can clamp it.
        ///
        /// This is why a rope length is a real control. The sail is never positioned directly;
        /// it is let out until the sheet stops it, exactly as on a boat.
        /// </summary>
        public static float WeathervaneAngle(Vector3 heading, Vector3 apparentWind)
        {
            Vector3 downwind = Flatten(apparentWind);
            if (downwind.sqrMagnitude < 1e-6f) return 0f;
            return SignedAngle(heading, downwind);
        }

        /// <summary>
        /// Where a sail actually ends up: it weathervanes, but the sheet will not let it out
        /// past <paramref name="maxSheetAngle"/> off the centreline.
        /// </summary>
        public static float TrimmedSailAngle(float weathervaneAngle, float maxSheetAngle)
        {
            return Mathf.Clamp(weathervaneAngle, -maxSheetAngle, maxSheetAngle);
        }

        /// <summary>
        /// Yaw torque one sail contributes, about the craft's vertical axis.
        ///
        /// This is the entire steering mechanism. <paramref name="leverArm"/> is measured from
        /// the centre of lateral resistance — the foil — along the heading, positive forward.
        /// A sail forward of the foil bears the bow away from the wind; a sail aft of it luffs
        /// the bow up into the wind. Trimming main against jib is therefore the helm.
        /// </summary>
        public static float YawTorque(Vector3 sailForce, Vector3 heading, float leverArm)
        {
            Vector3 right = Vector3.Cross(Vector3.up, Flatten(heading).normalized);
            float lateral = Vector3.Dot(Flatten(sailForce), right);
            return -lateral * leverArm;
        }

        /// <summary>
        /// Heel angle in degrees from the lateral force, damped by the righting moment.
        /// Signed: positive heels to starboard.
        /// </summary>
        public static float HeelAngle(Vector3 sailForce, Vector3 heading, float rightingMoment,
                                      float maxHeel)
        {
            if (rightingMoment <= 1e-4f) return 0f;
            Vector3 right = Vector3.Cross(Vector3.up, Flatten(heading).normalized);
            float lateral = Vector3.Dot(Flatten(sailForce), right);
            return Mathf.Clamp(lateral / rightingMoment, -maxHeel, maxHeel);
        }

        /// <summary>
        /// How much a sail is flogging, 0..1, for the shader's flutter term. A sail below the
        /// luffing angle is shaking; a stalled one is unsteady but not flogging.
        /// </summary>
        public static float LuffAmount(float angleOfAttack)
        {
            float a = Mathf.Abs(angleOfAttack);
            if (a < LuffAngle) return 1f - Mathf.InverseLerp(0f, LuffAngle, a) * 0.4f;
            if (a > StallAngle) return Mathf.InverseLerp(StallAngle, 90f, a) * 0.5f;
            return 0f;
        }

        /// <summary>
        /// How full the sail is, 0..1, for the shader's billow term. Peaks with the lift it is
        /// actually making, so a luffing sail goes flat and a driving one bellies out.
        /// </summary>
        public static float BillowAmount(float angleOfAttack, float windSpeed, float refWindSpeed)
        {
            float shape = Mathf.Clamp01(LiftCoefficient(angleOfAttack) / 1.35f);
            float pressure = Mathf.Clamp01(windSpeed / Mathf.Max(refWindSpeed, 0.01f));
            return shape * pressure;
        }
    }
}
