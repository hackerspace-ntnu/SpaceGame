// Assets/Game/Editor/Tests/MonowheelPoseMathTests.cs
//
// How far a monowheel's chassis tips about its hub so the ski rests on the sand. Side-view
// numbers: x is forward along the heading, y is up; a positive pitch tips the nose down and a
// positive slope means the ground rises ahead. The Runner's own measurements are used where a
// realistic shape matters: ski low point 3.74 m ahead of the hub and 1.60 m below it, hub 1.96 m
// over flat ground.
using NUnit.Framework;
using UnityEngine;
using M = SpaceGame.Vehicles.Monowheel.MonowheelPoseMath;

namespace SpaceGame.EditorTools
{
    public class MonowheelPoseMathTests
    {
        private static readonly Vector2 RunnerSki = new Vector2(3.74f, -1.60f);
        private const float RunnerClearance = 1.96f;

        // Depth of the ski point below the hub, measured square to a ground line of the given slope,
        // after the chassis tips nose-down by pitch degrees.
        private static float DepthBelowHub(Vector2 hubToSki, float pitchDeg, float slopeDeg)
        {
            float p = pitchDeg * Mathf.Deg2Rad, a = slopeDeg * Mathf.Deg2Rad;
            var tipped = new Vector2(hubToSki.x * Mathf.Cos(p) + hubToSki.y * Mathf.Sin(p),
                                     -hubToSki.x * Mathf.Sin(p) + hubToSki.y * Mathf.Cos(p));
            var groundNormal = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
            return -Vector2.Dot(tipped, groundNormal);
        }

        [Test]
        public void SkiPitch_OnFlatGround_TipsTheNoseDownUntilTheSkiTouches()
        {
            float pitch = M.SkiPitch(RunnerSki, RunnerClearance, 0f);
            Assert.Greater(pitch, 0f, "the Runner's ski hangs in the air at rest, so the nose must come down");
            Assert.AreEqual(RunnerClearance, DepthBelowHub(RunnerSki, pitch, 0f), 1e-3f);
        }

        [Test]
        public void SkiPitch_WhenTheSkiAlreadyTouches_IsZero()
        {
            float touching = -RunnerSki.y;
            Assert.AreEqual(0f, M.SkiPitch(RunnerSki, touching, 0f), 1e-3f);
        }

        [Test]
        public void SkiPitch_OnGroundRisingAhead_PitchesNoseUpByTheSlope()
        {
            float flat = M.SkiPitch(RunnerSki, RunnerClearance, 0f);
            float uphill = M.SkiPitch(RunnerSki, RunnerClearance, 10f);
            Assert.AreEqual(flat - 10f, uphill, 1e-3f);
            Assert.AreEqual(RunnerClearance, DepthBelowHub(RunnerSki, uphill, 10f), 1e-3f);
        }

        [Test]
        public void SkiPitch_WithTheGroundOutOfTheSkisReach_IsFiniteAndTipsAllTheWay()
        {
            float pitch = M.SkiPitch(RunnerSki, 50f, 0f);
            Assert.IsFalse(float.IsNaN(pitch));
            Assert.AreEqual(90f - Mathf.Atan2(-RunnerSki.y, RunnerSki.x) * Mathf.Rad2Deg, pitch, 1e-3f,
                            "points the ski straight down; the caller clamps it to a sane limit");
        }

        [Test]
        public void GroundUnderHub_OnFlatGround_IsLevelAndClearanceIsTheHubHeight()
        {
            M.GroundUnderHub(contactForward: 0f, groundAtContact: 10f, skiForward: 3.7f, groundAtSki: 10f,
                             hubForward: 0f, hubHeight: 12f, out float slope, out float clearance);
            Assert.AreEqual(0f, slope, 1e-4f);
            Assert.AreEqual(2f, clearance, 1e-4f);
        }

        [Test]
        public void GroundUnderHub_OnASlope_MeasuresClearanceSquareToTheGround()
        {
            // A 45° rise: the ground line passes 1 m under the hub's foot, so the hub is 2 m above
            // the line vertically and sqrt(2) square to it.
            M.GroundUnderHub(contactForward: 0f, groundAtContact: 0f, skiForward: 4f, groundAtSki: 4f,
                             hubForward: 1f, hubHeight: 3f, out float slope, out float clearance);
            Assert.AreEqual(45f, slope, 1e-3f);
            Assert.AreEqual(Mathf.Sqrt(2f), clearance, 1e-3f);
        }
    }
}
