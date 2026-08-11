using NUnit.Framework;
using UnityEngine;
using SpaceGame.Vehicles.DuneFoil;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The sailing rules the craft is supposed to obey. These are the claims made in
    /// docs/superpowers/specs/2026-08-11-dune-foil-sailer-design.md, written as tests, because
    /// "it feels like sailing" is not something a screenshot can confirm.
    ///
    /// Convention throughout: heading is +Z, starboard is +X, and a wind vector points the way the
    /// air is *going*, so a north wind (blowing from ahead toward the stern) is (0,0,-1).
    /// </summary>
    public class SailAerodynamicsTests
    {
        private const float Area = 60f;
        private const float Rho = 1.2f;

        // A sail set at `angle` degrees off the centreline: its normal is the heading rotated by
        // that angle and then turned 90 degrees, because the normal is across the chord.
        private static Vector3 SailNormalAt(float angleOffCentreline)
        {
            Vector3 chord = Quaternion.Euler(0f, angleOffCentreline, 0f) * Vector3.forward;
            return Vector3.Cross(Vector3.up, chord).normalized;
        }

        private static float DriveAt(float sailAngle, Vector3 apparentWind)
        {
            Vector3 f = SailAerodynamics.SailForce(apparentWind, SailNormalAt(sailAngle), Area, Rho, out _);
            return Vector3.Dot(f, Vector3.forward);
        }

        // Best drive available at this wind, over every trim the player could choose.
        private static float BestDrive(Vector3 apparentWind)
        {
            float best = float.NegativeInfinity;
            for (float a = -90f; a <= 90f; a += 1f)
                best = Mathf.Max(best, DriveAt(a, apparentWind));
            return best;
        }

        /// <summary>
        /// Speed the craft settles at on a given point of sail, sailed at its best trim throughout.
        ///
        /// Points of sail have to be compared this way rather than by instantaneous force. At equal
        /// *apparent* wind a dead run actually makes the most force — a stalled sail is a parachute
        /// and its drag coefficient beats any lift coefficient. What makes a reach faster is that
        /// running away from the wind kills the apparent wind (you can never outrun it), while on a
        /// reach the apparent wind holds up and even builds as you accelerate. That is a terminal-
        /// speed effect, so terminal speed is what these tests measure.
        /// </summary>
        private static float TerminalSpeed(Vector3 trueWind, float headingDegrees)
        {
            const float mass = 2600f;
            const float drag = 0.02f;
            const float dt = 0.05f;

            Vector3 heading = Quaternion.Euler(0f, headingDegrees, 0f) * Vector3.forward;
            float v = 0f;

            for (int step = 0; step < 6000; step++)
            {
                Vector3 apparent = SailAerodynamics.ApparentWind(trueWind, heading * v);

                // best trim available this instant, measured along the heading
                float drive = float.NegativeInfinity;
                for (float a = -90f; a <= 90f; a += 2f)
                {
                    Vector3 chord = Quaternion.AngleAxis(a, Vector3.up) * heading;
                    Vector3 n = Vector3.Cross(Vector3.up, chord).normalized;
                    Vector3 f = SailAerodynamics.SailForce(apparent, n, Area, Rho, out _);
                    drive = Mathf.Max(drive, Vector3.Dot(f, heading));
                }

                v += (drive / mass - drag * v * v) * dt;
                if (v < 0f) v = 0f;
            }
            return v;
        }

        // --- the no-go zone ----------------------------------------------------

        [Test]
        public void HeadToWind_NoTrimProducesDrive()
        {
            // wind straight down the bow: dead into it
            Vector3 apparent = new Vector3(0f, 0f, -12f);
            Assert.Less(BestDrive(apparent), 0f,
                "Sailing straight into the wind must not be possible at any trim.");
        }

        [Test]
        public void InsideNoGoZone_IsReportedAsSuch()
        {
            Vector3 heading = Vector3.forward;
            Vector3 windFrom = Quaternion.Euler(0f, 30f, 0f) * Vector3.forward;
            Assert.IsTrue(SailAerodynamics.IsInNoGoZone(heading, windFrom),
                "30 degrees off the wind is inside the no-go zone.");
        }

        [Test]
        public void OutsideNoGoZone_IsSailable()
        {
            Vector3 heading = Vector3.forward;
            Vector3 windFrom = Quaternion.Euler(0f, 60f, 0f) * Vector3.forward;
            Assert.IsFalse(SailAerodynamics.IsInNoGoZone(heading, windFrom),
                "60 degrees off the wind is a close reach and must be sailable.");

            Vector3 apparent = -windFrom * 12f;
            Assert.Greater(BestDrive(apparent), 0f, "A close reach must produce forward drive.");
        }

        // --- points of sail ----------------------------------------------------

        [Test]
        public void BeamReach_IsFasterThanADeadRun()
        {
            // One wind, blowing toward -Z. Heading 90 degrees off it is a beam reach; heading
            // straight downwind with it is a dead run.
            Vector3 trueWind = new Vector3(0f, 0f, -12f);

            float beamReach = TerminalSpeed(trueWind, 90f);
            float deadRun = TerminalSpeed(trueWind, 180f);

            Assert.Greater(beamReach, deadRun,
                $"A beam reach is the fastest point of sail. Got reach {beamReach:F1} m/s " +
                $"against run {deadRun:F1} m/s.");
        }

        [Test]
        public void DeadRun_CannotOutrunTheWind()
        {
            Vector3 trueWind = new Vector3(0f, 0f, -12f);
            Assert.Less(TerminalSpeed(trueWind, 180f), 12f,
                "Running downwind, the craft can at best match the wind and never beat it.");
        }

        [Test]
        public void ThePolarPeaks_OnAReach()
        {
            Vector3 trueWind = new Vector3(0f, 0f, -12f);

            float best = 0f, bestHeading = 0f;
            for (float h = 10f; h <= 180f; h += 5f)
            {
                float v = TerminalSpeed(trueWind, h);
                if (v > best) { best = v; bestHeading = h; }
            }

            Assert.That(bestHeading, Is.InRange(60f, 120f),
                $"The fastest heading should be a reach, somewhere near beam on. Got {bestHeading:F0} " +
                $"degrees off the wind at {best:F1} m/s.");
        }

        [Test]
        public void PinchingIntoTheWind_StopsTheCraft()
        {
            // The no-go zone is not a switch — induced drag closes it smoothly, so speed collapses
            // as the craft points higher and is gone well before it is head to wind.
            Vector3 trueWind = new Vector3(0f, 0f, -12f);

            float pinching = TerminalSpeed(trueWind, 15f);
            float reaching = TerminalSpeed(trueWind, 60f);

            Assert.Less(pinching, 1.5f,
                $"15 degrees off the wind must leave the craft stopped. Got {pinching:F1} m/s.");
            Assert.Greater(reaching, pinching * 5f,
                $"Bearing away to a reach must transform it. Got {reaching:F1} m/s.");
        }

        [Test]
        public void SpeedFallsOffMonotonically_AsTheCraftPointsHigher()
        {
            Vector3 trueWind = new Vector3(0f, 0f, -12f);
            float previous = 0f;
            for (float h = 15f; h <= 85f; h += 10f)
            {
                float v = TerminalSpeed(trueWind, h);
                Assert.Greater(v, previous - 0.05f,
                    $"Bearing away from {h - 10:F0} to {h:F0} degrees lost speed; the polar should " +
                    "open up smoothly all the way to a reach.");
                previous = v;
            }
        }

        [Test]
        public void InducedDrag_IsWhatClosesTheNoGoZone()
        {
            // Guards the mechanism, not just the outcome. Total drag has to exceed profile drag
            // wherever the sail is making lift, or pointing high becomes free again.
            float aoa = SailAerodynamics.PeakLiftAngle;
            Assert.Greater(SailAerodynamics.TotalDragCoefficient(aoa),
                           SailAerodynamics.DragCoefficient(aoa) * 1.5f,
                "At peak lift, induced drag should dominate profile drag.");

            Assert.AreEqual(SailAerodynamics.DragCoefficient(0f),
                            SailAerodynamics.TotalDragCoefficient(0f), 1e-3f,
                "With no lift there is no induced drag.");
        }

        // --- the lift curve ----------------------------------------------------

        [Test]
        public void LiftCollapses_BothBelowLuffAngleAndPastStall()
        {
            float peak = SailAerodynamics.LiftCoefficient(SailAerodynamics.PeakLiftAngle);
            float luffing = SailAerodynamics.LiftCoefficient(SailAerodynamics.LuffAngle * 0.5f);
            float stalled = SailAerodynamics.LiftCoefficient(SailAerodynamics.StallAngle + 20f);

            Assert.Greater(peak, luffing * 4f, "A luffing sail must make far less lift than a full one.");
            Assert.Greater(peak, stalled * 2f, "A stalled sail must make far less lift than a full one.");
        }

        [Test]
        public void LiftPeaks_AtThePeakAngle()
        {
            float peak = SailAerodynamics.LiftCoefficient(SailAerodynamics.PeakLiftAngle);
            for (float a = 0f; a <= 90f; a += 1f)
            {
                Assert.LessOrEqual(SailAerodynamics.LiftCoefficient(a), peak + 1e-4f,
                    $"Lift at {a} deg exceeded the documented peak.");
            }
        }

        [Test]
        public void DragRises_MonotonicallyToBroadside()
        {
            float previous = -1f;
            for (float a = 0f; a <= 90f; a += 5f)
            {
                float cd = SailAerodynamics.DragCoefficient(a);
                Assert.GreaterOrEqual(cd, previous, $"Drag fell going from {a - 5} to {a} degrees.");
                previous = cd;
            }
        }

        // --- apparent wind -----------------------------------------------------

        [Test]
        public void ApparentWind_ShiftsForwardAsTheCraftAccelerates()
        {
            Vector3 trueWind = new Vector3(-10f, 0f, 0f);        // beam wind from starboard
            Vector3 still = SailAerodynamics.ApparentWind(trueWind, Vector3.zero);
            Vector3 moving = SailAerodynamics.ApparentWind(trueWind, new Vector3(0f, 0f, 10f));

            float angleStill = Mathf.Abs(SailAerodynamics.SignedAngle(Vector3.forward, -still));
            float angleMoving = Mathf.Abs(SailAerodynamics.SignedAngle(Vector3.forward, -moving));

            Assert.Less(angleMoving, angleStill,
                "Speed must draw the apparent wind forward, toward the bow.");
        }

        [Test]
        public void ApparentWind_IgnoresVerticalMotion()
        {
            Vector3 trueWind = new Vector3(-10f, 0f, 0f);
            Vector3 a = SailAerodynamics.ApparentWind(trueWind, new Vector3(0f, 8f, 0f));
            Assert.AreEqual(0f, a.y, 1e-5f, "The sailing model is planar; climb must not enter it.");
        }

        // --- steering by sail balance -----------------------------------------

        [Test]
        public void MainAndJib_YawTheCraftInOppositeDirections()
        {
            Vector3 heading = Vector3.forward;
            // the same sideways force on both sails
            Vector3 force = new Vector3(500f, 0f, 0f);

            float main = SailAerodynamics.YawTorque(force, heading, leverArm: -4f);  // aft of the foil
            float jib = SailAerodynamics.YawTorque(force, heading, leverArm: 6f);    // forward of it

            Assert.Less(main * jib, 0f,
                "A sail aft of the foil and one forward of it must yaw the craft opposite ways. " +
                "This is the entire steering mechanism.");
        }

        [Test]
        public void SailOnTheCentreOfResistance_ProducesNoYaw()
        {
            float torque = SailAerodynamics.YawTorque(new Vector3(500f, 0f, 0f), Vector3.forward, 0f);
            Assert.AreEqual(0f, torque, 1e-4f);
        }

        [Test]
        public void YawTorque_ScalesWithLeverArm()
        {
            Vector3 force = new Vector3(500f, 0f, 0f);
            float near = Mathf.Abs(SailAerodynamics.YawTorque(force, Vector3.forward, 2f));
            float far = Mathf.Abs(SailAerodynamics.YawTorque(force, Vector3.forward, 8f));
            Assert.Greater(far, near * 3f, "Torque must scale with the lever arm.");
        }

        // --- the sheet is the control -----------------------------------------

        [Test]
        public void FreeSail_WeathervanesDownwind()
        {
            Vector3 heading = Vector3.forward;
            Vector3 apparent = new Vector3(-10f, 0f, 0f);   // going to port
            float vane = SailAerodynamics.WeathervaneAngle(heading, apparent);
            Assert.AreEqual(-90f, vane, 1f, "A free sail trails the apparent wind.");
        }

        [Test]
        public void ShorteningTheSheet_StopsTheSailSwingingOut()
        {
            float vane = -90f;
            Assert.AreEqual(-90f, SailAerodynamics.TrimmedSailAngle(vane, 90f), 1e-4f,
                "A long sheet lets the sail all the way out.");
            Assert.AreEqual(-25f, SailAerodynamics.TrimmedSailAngle(vane, 25f), 1e-4f,
                "A short sheet must stop it short. Rope length is the control.");
        }

        // --- heel ---------------------------------------------------------------

        [Test]
        public void Heel_FollowsTheSideOfTheLateralForce()
        {
            Vector3 toStarboard = new Vector3(800f, 0f, 0f);
            Vector3 toPort = new Vector3(-800f, 0f, 0f);

            float a = SailAerodynamics.HeelAngle(toStarboard, Vector3.forward, 100f, 25f);
            float b = SailAerodynamics.HeelAngle(toPort, Vector3.forward, 100f, 25f);

            Assert.Greater(a, 0f);
            Assert.Less(b, 0f);
            Assert.AreEqual(-a, b, 1e-3f, "Heel must be symmetric between tacks.");
        }

        [Test]
        public void Heel_IsClampedToTheMaximum()
        {
            float heel = SailAerodynamics.HeelAngle(new Vector3(100000f, 0f, 0f), Vector3.forward, 10f, 22f);
            Assert.AreEqual(22f, heel, 1e-4f, "Heel must never exceed the configured limit.");
        }

        // --- shader drive -------------------------------------------------------

        [Test]
        public void LuffAmount_IsHighWhenShaking_ZeroWhenDriving()
        {
            Assert.Greater(SailAerodynamics.LuffAmount(1f), 0.5f, "A barely-filled sail is flogging.");
            Assert.AreEqual(0f, SailAerodynamics.LuffAmount(SailAerodynamics.PeakLiftAngle), 1e-4f,
                "A sail at peak lift is steady.");
        }

        [Test]
        public void BillowAmount_RisesWithWindAndFalls_WhenLuffing()
        {
            float driving = SailAerodynamics.BillowAmount(SailAerodynamics.PeakLiftAngle, 12f, 12f);
            float luffing = SailAerodynamics.BillowAmount(1f, 12f, 12f);
            float light = SailAerodynamics.BillowAmount(SailAerodynamics.PeakLiftAngle, 3f, 12f);

            Assert.Greater(driving, 0.9f);
            Assert.Less(luffing, 0.15f, "A luffing sail must go flat.");
            Assert.Less(light, driving, "Less wind must mean less belly.");
        }
    }
}
