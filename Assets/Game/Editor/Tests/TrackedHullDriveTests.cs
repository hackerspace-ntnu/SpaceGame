using NUnit.Framework;
using UnityEngine;
using SpaceGame.Vehicles.Motors;

namespace SpaceGame.EditorTools
{
    public class TrackedHullDriveTests
    {
        private static readonly TrackedHullSettings S = new TrackedHullSettings
        {
            turnRate = 10f, cruiseSpeed = 4f, acceleration = 0.5f, braking = 1f, alignAngle = 45f,
        };

        private const float Dt = 0.1f;

        // --- Heading -------------------------------------------------------------------------------

        [Test] public void Bearing_IsClockwiseFromPlusZ()
        {
            Assert.AreEqual(0f, TrackedHullDrive.Bearing(Vector3.zero, Vector3.forward, 99f), 1e-4f);
            Assert.AreEqual(90f, TrackedHullDrive.Bearing(Vector3.zero, Vector3.right, 99f), 1e-4f);
            Assert.AreEqual(-90f, TrackedHullDrive.Bearing(Vector3.zero, Vector3.left, 99f), 1e-4f);
        }

        [Test] public void Bearing_OfAPointInTheSameColumn_IsTheFallback()
        {
            Assert.AreEqual(33f, TrackedHullDrive.Bearing(Vector3.zero, Vector3.up * 5f, 33f));
        }

        [Test] public void NextHeading_TurnsAtMostTurnRatePerSecond()
        {
            Assert.AreEqual(1f, TrackedHullDrive.NextHeading(0f, 90f, Dt, S), 1e-4f);
            Assert.AreEqual(-1f, TrackedHullDrive.NextHeading(0f, -90f, Dt, S), 1e-4f);
        }

        [Test] public void NextHeading_TakesTheShortWayRound()
        {
            float next = TrackedHullDrive.NextHeading(170f, -170f, Dt, S);
            Assert.AreEqual(0f, Mathf.DeltaAngle(171f, next), 1e-3f, "170 -> -170 is 20 degrees clockwise, not 340 back");
        }

        [Test] public void NextHeading_NeverOvershoots()
        {
            Assert.AreEqual(0.5f, TrackedHullDrive.NextHeading(0f, 0.5f, Dt, S), 1e-4f);
        }

        // --- Wanted speed --------------------------------------------------------------------------

        [Test] public void WantedSpeed_FarAndAligned_IsCruise()
        {
            Assert.AreEqual(S.cruiseSpeed, TrackedHullDrive.WantedSpeed(500f, 0f, 1f, S), 1e-4f);
        }

        [Test] public void WantedSpeed_TargetBeyondAlignAngle_IsZero_SoTheHullTurnsOnTheSpotFirst()
        {
            Assert.AreEqual(0f, TrackedHullDrive.WantedSpeed(500f, 45f, 1f, S));
            Assert.AreEqual(0f, TrackedHullDrive.WantedSpeed(500f, -170f, 1f, S));
        }

        [Test] public void WantedSpeed_FallsOffLinearlyWithHeadingError()
        {
            Assert.AreEqual(S.cruiseSpeed * 0.5f, TrackedHullDrive.WantedSpeed(500f, 22.5f, 1f, S), 1e-4f);
        }

        [Test] public void WantedSpeed_NearTheStopPoint_IsWhatItCanStillBrakeFrom()
        {
            // v = sqrt(2 * braking * d): 2 m left at 1 m/s^2 braking is 2 m/s.
            Assert.AreEqual(2f, TrackedHullDrive.WantedSpeed(2f, 0f, 1f, S), 1e-4f);
        }

        [Test] public void WantedSpeed_AtOrPastTheStopPoint_IsZero()
        {
            Assert.AreEqual(0f, TrackedHullDrive.WantedSpeed(0f, 0f, 1f, S));
            Assert.AreEqual(0f, TrackedHullDrive.WantedSpeed(-3f, 0f, 1f, S));
        }

        [Test] public void WantedSpeed_ScalesByTheMultiplier_AndAnUnsetOneIsFullSpeed()
        {
            Assert.AreEqual(S.cruiseSpeed * 0.5f, TrackedHullDrive.WantedSpeed(500f, 0f, 0.5f, S), 1e-4f);
            Assert.AreEqual(S.cruiseSpeed, TrackedHullDrive.WantedSpeed(500f, 0f, 0f, S), 1e-4f);
            Assert.AreEqual(S.cruiseSpeed, TrackedHullDrive.WantedSpeed(500f, 0f, 3f, S), 1e-4f, "never past cruise");
        }

        // --- Speed ---------------------------------------------------------------------------------

        [Test] public void NextSpeed_AcceleratesAtAcceleration()
        {
            Assert.AreEqual(0.05f, TrackedHullDrive.NextSpeed(0f, 4f, Dt, S), 1e-5f);
        }

        [Test] public void NextSpeed_BrakesAtBraking()
        {
            Assert.AreEqual(3.9f, TrackedHullDrive.NextSpeed(4f, 0f, Dt, S), 1e-5f);
        }

        [Test] public void NextSpeed_NeverPassesCruise_OrGoesBackward()
        {
            Assert.AreEqual(S.cruiseSpeed, TrackedHullDrive.NextSpeed(S.cruiseSpeed, 50f, 10f, S), 1e-5f);
            Assert.AreEqual(0f, TrackedHullDrive.NextSpeed(0.01f, -5f, 10f, S), 1e-5f);
        }

        // --- Footing -------------------------------------------------------------------------------

        private static Vector3[] Grid(System.Func<float, float, float> height)
        {
            var points = new Vector3[9];
            int i = 0;
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                    points[i++] = new Vector3(x * 6f, height(x * 6f, z * 15f), z * 15f);
            return points;
        }

        [Test] public void Footing_OnFlatGround_IsLevel_AtTheGroundHeight()
        {
            Assert.IsTrue(TrackedHullDrive.TryFooting(Grid((x, z) => 7f), 9, true, 12f, out HullFooting f));
            Assert.AreEqual(7f, f.Height, 1e-4f);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, f.Tilt), 1e-3f);
        }

        [Test] public void Footing_OnAnUphill_PitchesTheNoseUp()
        {
            // Rises 1 m per 10 m forward: 5.71 degrees.
            Assert.IsTrue(TrackedHullDrive.TryFooting(Grid((x, z) => 10f + z * 0.1f), 9, true, 12f, out HullFooting f));
            Vector3 nose = f.Tilt * Vector3.forward;
            Assert.Greater(nose.y, 0f, "nose up the slope");
            Assert.AreEqual(Mathf.Atan(0.1f) * Mathf.Rad2Deg, Quaternion.Angle(Quaternion.identity, f.Tilt), 0.05f);
            Assert.AreEqual(10f, f.Height, 1e-3f, "the plane's height under the pivot");
        }

        [Test] public void Footing_OnACrossSlope_RollsTheHighSideUp()
        {
            Assert.IsTrue(TrackedHullDrive.TryFooting(Grid((x, z) => x * 0.1f), 9, true, 12f, out HullFooting f));
            Vector3 right = f.Tilt * Vector3.right;
            Assert.Greater(right.y, 0f, "ground rises to +x, so the right side is lifted");
        }

        [Test] public void Footing_TiltIsCappedAtMaxTilt()
        {
            Assert.IsTrue(TrackedHullDrive.TryFooting(Grid((x, z) => z), 9, true, 12f, out HullFooting f));
            Assert.AreEqual(12f, Quaternion.Angle(Quaternion.identity, f.Tilt), 0.05f);
        }

        [Test] public void Footing_HeldLevel_RestsOnTheHighestSample()
        {
            Assert.IsTrue(TrackedHullDrive.TryFooting(Grid((x, z) => z * 0.1f), 9, false, 12f, out HullFooting f));
            Assert.AreEqual(1.5f, f.Height, 1e-4f, "a rigid level hull sits on the crest, not buried in it");
            Assert.AreEqual(Quaternion.identity, f.Tilt);
        }

        [Test] public void Footing_WithTooFewSamplesForAPlane_FallsBackToLevelOnTheHighest()
        {
            var two = new[] { new Vector3(0f, 3f, -10f), new Vector3(0f, 4f, 10f) };
            Assert.IsTrue(TrackedHullDrive.TryFooting(two, 2, true, 12f, out HullFooting f));
            Assert.AreEqual(4f, f.Height, 1e-4f);
            Assert.AreEqual(Quaternion.identity, f.Tilt);
        }

        [Test] public void Footing_WithNoSamples_IsNotAnAnswer()
        {
            Assert.IsFalse(TrackedHullDrive.TryFooting(new Vector3[4], 0, true, 12f, out _));
        }

        // --- Smoothing -----------------------------------------------------------------------------

        [Test] public void SmoothFactor_ZeroSharpnessSnaps_AndItIsFrameRateIndependent()
        {
            Assert.AreEqual(1f, TrackedHullDrive.SmoothFactor(0f, Dt));
            float one = TrackedHullDrive.SmoothFactor(4f, 0.2f);
            float twoHalves = 1f - (1f - TrackedHullDrive.SmoothFactor(4f, 0.1f)) * (1f - TrackedHullDrive.SmoothFactor(4f, 0.1f));
            Assert.AreEqual(one, twoHalves, 1e-5f);
        }
    }
}
