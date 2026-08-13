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
        /// <param name="angleOfAttack">
        /// Reported back for the caller's shader and UI. Zero is a sail edge-on to the flow and
        /// flogging; ninety is one held broadside to it, stalled, working as a parachute.
        /// </param>
        /// <remarks>
        /// This was wrong in two ways at once, and between them the craft could not sail.
        ///
        /// The angle of attack was the COMPLEMENT of the real one — a sail held broadside to the
        /// breeze reported 0 degrees and was treated as luffing, while one lying edge-on to it
        /// reported 90 and was treated as stalled. Every coefficient below was then read off the
        /// curve backwards. Nothing caught it because the tests search across all trims for the
        /// best one, and a model that is inverted end to end still has a best trim; it is just in
        /// the wrong place.
        ///
        /// And lift was pushed square across the flow with its side chosen by which face the wind
        /// was on, rather than along the sail's own normal resolved across the flow. That is right
        /// for a plate at small incidence and wrong everywhere else, and it is what made a
        /// close-hauled sail push the craft backwards.
        ///
        /// The coefficient curves themselves were always right. They just had to be given the
        /// angle they were drawn for.
        /// </remarks>
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

            // Face the normal downwind. `sailNormal` names a plane, and a plane has two normals;
            // the one pointing away from the wind is the sail's low-pressure side, which is the
            // side lift pulls toward.
            float alongWind = Vector3.Dot(sailNormal, windDir);
            if (alongWind < 0f)
            {
                sailNormal = -sailNormal;
                alongWind = -alongWind;
            }

            // With the normal faced downwind, `alongWind` is the cosine of the angle between the
            // normal and the flow — and the chord is square to the normal, so the sail's own angle
            // of attack is the complement of that. Broadside is 90, edge-on is 0.
            angleOfAttack = 90f - Mathf.Acos(Mathf.Clamp01(alongWind)) * Mathf.Rad2Deg;

            float q = 0.5f * airDensity * windSpeed * windSpeed * area;
            float cl = LiftCoefficient(angleOfAttack);
            float cd = TotalDragCoefficient(angleOfAttack);

            // Lift is square to the flow, drag runs with it. The lift direction is what is left of
            // the sail's normal once the along-flow part is taken out — which puts it on the
            // correct side automatically, on either tack, without a sign to get wrong.
            Vector3 across = sailNormal - windDir * alongWind;
            Vector3 lift = across.sqrMagnitude > 1e-8f ? across.normalized * (cl * q) : Vector3.zero;

            return lift + windDir * (cd * q);
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
        ///
        /// Returned as a signed angle of the BOOM off the craft's stern — 0 is a boom lying down
        /// the centreline, +90 is one right out to port, -90 right out to starboard — so a sheet
        /// limit can clamp it. This is why a rope length is a real control: the sail is never
        /// positioned directly, it is let out until the sheet stops it, exactly as on a boat.
        ///
        /// Measured off the stern rather than the bow, which is a 180 degree correction and was
        /// the second half of why this craft would not sail. A boom points aft; the free one
        /// trails away downwind. Measured off the bow instead, the rig chose a trim 90 degrees
        /// wrong on every point of sail — hard out on a beam reach, where it made no drive at all,
        /// and hard in on a run, where it made none either. The clamp then hid the worst of it by
        /// pinning the sail at its sheet limit, so the craft looked trimmed and went nowhere.
        /// </summary>
        public static float WeathervaneAngle(Vector3 heading, Vector3 apparentWind)
        {
            Vector3 downwind = Flatten(apparentWind);
            if (downwind.sqrMagnitude < 1e-6f) return 0f;
            return SignedAngle(-Flatten(heading), downwind);
        }

        /// <summary>
        /// The chord-plane normal of a sail whose boom sits <paramref name="boomAngle"/> off the
        /// centreline. One place, so the trim the player sets and the force it makes cannot drift
        /// apart.
        /// </summary>
        public static Vector3 NormalFromBoomAngle(float boomAngle, Vector3 heading)
        {
            Vector3 flat = Flatten(heading);
            if (flat.sqrMagnitude < 1e-8f) return Vector3.right;
            Vector3 chord = Quaternion.AngleAxis(boomAngle, Vector3.up) * flat.normalized;
            return Vector3.Cross(Vector3.up, chord).normalized;
        }

        /// <summary>
        /// Signed angle of a boom that physically points <paramref name="boomDirection"/>, off the
        /// craft's stern. The inverse of <see cref="NormalFromBoomAngle"/>'s input, used to read a
        /// trim back off the model's own geometry instead of trusting a number to describe it.
        /// </summary>
        public static float BoomAngle(Vector3 boomDirection, Vector3 heading)
        {
            Vector3 boom = Flatten(boomDirection);
            Vector3 flat = Flatten(heading);
            if (boom.sqrMagnitude < 1e-8f || flat.sqrMagnitude < 1e-8f) return 0f;
            return SignedAngle(-flat.normalized, boom.normalized);
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
        /// Yaw torque one sail contributes, about the craft's vertical axis. Positive turns the
        /// bow to starboard, which is the same sense as a positive rotation about world up.
        ///
        /// <paramref name="leverArm"/> is measured from the centre of lateral resistance — the
        /// foil — along the heading, positive forward. A sail forward of the foil bears the bow
        /// away from the wind; a sail aft of it luffs the bow up into the wind. Trimming main
        /// against jib is therefore a helm, and it works alongside the wheel rather than instead
        /// of it.
        ///
        /// The sign used to be inverted, and it made both of those sentences false on screen: a
        /// jib forward of the resistance rounded the craft UP into the wind and a main aft of it
        /// bore AWAY, so every trim did the opposite of what the rig is shaped to do and of what
        /// the readouts said. Nothing caught it because the tests only pinned that main and jib
        /// disagree with each other, which an inverted pair does perfectly well.
        ///
        /// Two ways to talk yourself back into the minus sign, and why neither survives: a force
        /// to starboard applied ahead of the pivot pushes the BOW to starboard, and a positive
        /// rotation about <c>Vector3.up</c> swings +Z toward +X — forward toward starboard. Both
        /// are positive, so the product is.
        /// </summary>
        public static float YawTorque(Vector3 sailForce, Vector3 heading, float leverArm)
        {
            return YawTorque(sailForce, heading, leverArm, 0f);
        }

        /// <summary>
        /// The same, for a sail whose centre of effort has been swung off the centreline by
        /// canting its post.
        ///
        /// <paramref name="lateralOffset"/> is how far the centre of effort now sits to
        /// starboard. Drive applied off the centreline yaws the craft: push the starboard side
        /// forward and the bow swings to port. That is real, it is how anything with a canting
        /// rig steers, and it is what makes leaning the post a control rather than a decoration.
        /// </summary>
        public static float YawTorque(Vector3 sailForce, Vector3 heading, float leverArm,
                                      float lateralOffset)
        {
            Vector3 flatHeading = Flatten(heading);
            if (flatHeading.sqrMagnitude < 1e-8f) return 0f;
            flatHeading.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, flatHeading);
            Vector3 force = Flatten(sailForce);

            float lateral = Vector3.Dot(force, right);
            float drive = Vector3.Dot(force, flatHeading);

            return lateral * leverArm - drive * lateralOffset;
        }

        /// <summary>
        /// Heel angle in degrees from the lateral force, damped by the righting moment.
        /// Signed: positive heels to starboard.
        /// </summary>
        public static float HeelAngle(Vector3 sailForce, Vector3 heading, float rightingMoment,
                                      float maxHeel)
        {
            return HeelAngle(sailForce, heading, 0f, 0f, rightingMoment, maxHeel);
        }

        /// <summary>
        /// Heel with the post canted over.
        ///
        /// Two effects, and they pull in opposite directions, which is the whole point of being
        /// able to lean the mast:
        ///
        /// - the sail's sideways push acts on a shorter arm as the post lays down, by cos(cant);
        /// - the weight of the rig itself now hangs out to one side, by sin(cant).
        ///
        /// Lean the post INTO the wind and the second term cancels the first: the craft stands up,
        /// carries its full sail plan in a breeze that would otherwise have it on its ear, and
        /// keeps driving. Lean it to leeward and it lies down. That trade — depower by leaning to
        /// windward, or accept the heel — is what a canting rig is for.
        /// </summary>
        /// <param name="cantDegrees">Post lean, positive to starboard.</param>
        /// <param name="rigWeightMoment">Moment the rig's own mass makes at full lay-down.</param>
        public static float HeelAngle(Vector3 sailForce, Vector3 heading, float cantDegrees,
                                      float rigWeightMoment, float rightingMoment, float maxHeel)
        {
            if (rightingMoment <= 1e-4f) return 0f;

            Vector3 flatHeading = Flatten(heading);
            if (flatHeading.sqrMagnitude < 1e-8f) return 0f;

            Vector3 right = Vector3.Cross(Vector3.up, flatHeading.normalized);
            float lateral = Vector3.Dot(Flatten(sailForce), right);

            float cant = cantDegrees * Mathf.Deg2Rad;
            float moment = lateral * Mathf.Cos(cant) + rigWeightMoment * Mathf.Sin(cant);

            return Mathf.Clamp(moment / rightingMoment, -maxHeel, maxHeel);
        }

        // --- canting the post -------------------------------------------------

        /// <summary>
        /// What a canted post costs in drive, 0..1.
        ///
        /// A sail leaned over presents less of itself to a horizontal wind — the cloth is still
        /// all there, but the part of it standing across the breeze goes as the cosine of the
        /// lean. So laying the post down to stand the craft up is paid for in speed, which is
        /// exactly the bargain the control is meant to offer.
        /// </summary>
        public static float CantDriveFactor(float cantDegrees)
        {
            return Mathf.Max(0f, Mathf.Cos(cantDegrees * Mathf.Deg2Rad));
        }

        /// <summary>
        /// How far a canted sail's centre of effort swings off the centreline, metres to starboard.
        /// Feed to <see cref="YawTorque(Vector3,Vector3,float,float)"/>.
        /// </summary>
        /// <param name="cantDegrees">Post lean, positive to starboard.</param>
        /// <param name="centreOfEffortHeight">How far the centre of effort sits up the post.</param>
        public static float CantLateralOffset(float cantDegrees, float centreOfEffortHeight)
        {
            return centreOfEffortHeight * Mathf.Sin(cantDegrees * Mathf.Deg2Rad);
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
