// Assets/Game/Editor/Tests/MonowheelPresentationMathTests.cs
//
// The monowheel's presentation arithmetic (spec §2, §4). Pure, because every interesting case —
// a teleport, a reverse, a standstill, no camera — is a single frame that a MonoBehaviour test
// would need a scene, a camera and a physics step to reach.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Vehicles.Monowheel;
using UnityEngine;
using M = SpaceGame.Vehicles.Monowheel.MonowheelPresentationMath;

namespace SpaceGame.EditorTools
{
    public class MonowheelPresentationMathTests
    {
        private const float Dt = 1f / 60f;

        [Test]
        public void StepSpeed_UnsmoothedIsForwardDistanceOverTime()
        {
            float s = M.StepSpeed(0f, Vector3.zero, new Vector3(0f, 0f, 0.2f), Vector3.forward,
                                  Dt, 0f, 50f, out bool teleported);
            Assert.AreEqual(12f, s, 1e-3f);
            Assert.IsFalse(teleported);
        }

        [Test]
        public void StepSpeed_IgnoresSidewaysMotion()
        {
            float s = M.StepSpeed(0f, Vector3.zero, new Vector3(0.2f, 0f, 0f), Vector3.forward,
                                  Dt, 0f, 50f, out _);
            Assert.AreEqual(0f, s, 1e-4f);
        }

        [Test]
        public void StepSpeed_SmoothingMovesPartWayTowardRaw()
        {
            float s = M.StepSpeed(0f, Vector3.zero, new Vector3(0f, 0f, 0.2f), Vector3.forward,
                                  Dt, 0.15f, 50f, out _);
            Assert.Greater(s, 0f);
            Assert.Less(s, 12f);
        }

        [Test]
        public void StepSpeed_TeleportKeepsPreviousSpeed()
        {
            float s = M.StepSpeed(3f, Vector3.zero, new Vector3(0f, 0f, 400f), Vector3.forward,
                                  Dt, 0.15f, 50f, out bool teleported);
            Assert.IsTrue(teleported);
            Assert.AreEqual(3f, s, 1e-5f, "a snap must not read as 24 km/s");
        }

        [Test]
        public void StepSpeed_AHitchAtTopSpeedIsStillDriving()
        {
            // 5 m in a 250 ms hitch is 20 m/s — a single's top speed, not a teleport. A fixed
            // distance threshold would have called this a snap (Strider session's catch).
            M.StepSpeed(20f, Vector3.zero, new Vector3(0f, 0f, 5f), Vector3.forward,
                        0.25f, 0.15f, 50f, out bool teleported);
            Assert.IsFalse(teleported);
        }

        [Test]
        public void StepSpeed_ZeroDtIsNoChange()
        {
            Assert.AreEqual(4f, M.StepSpeed(4f, Vector3.zero, Vector3.forward, Vector3.forward,
                                            0f, 0.15f, 50f, out _));
        }

        [Test]
        public void SpinDegrees_IsSpeedOverRadius()
        {
            // 1.965 m paddle radius at 19.65 m/s is 10 rad/s.
            Assert.AreEqual(10f * Mathf.Rad2Deg * Dt, M.SpinDegrees(19.65f, 1.965f, Dt), 1e-3f);
        }

        [Test]
        public void SpinDegrees_IsSignedBySpeed()
        {
            Assert.Less(M.SpinDegrees(-5f, 1.965f, Dt), 0f);
        }

        [Test]
        public void SpinDegrees_ZeroRadiusIsZero()
        {
            Assert.AreEqual(0f, M.SpinDegrees(10f, 0f, Dt));
        }

        [Test]
        public void SpeedFraction_UsesMagnitudeAndClamps()
        {
            Assert.AreEqual(0.5f, M.SpeedFraction(-10f, 20f), 1e-5f);
            Assert.AreEqual(1f, M.SpeedFraction(50f, 20f));
            Assert.AreEqual(0f, M.SpeedFraction(5f, 0f));
        }

        [Test]
        public void Rate_AtRestIsIdle()
        {
            Assert.AreEqual(2f, M.Rate(2f, 12f, 0f));
            Assert.AreEqual(12f, M.Rate(2f, 12f, 1f));
        }

        [Test]
        public void LodFactor_FullNearZeroFarLinearBetween()
        {
            Assert.AreEqual(1f, M.LodFactor(10f, 60f, 150f));
            Assert.AreEqual(0.5f, M.LodFactor(105f, 60f, 150f), 1e-5f);
            Assert.AreEqual(0f, M.LodFactor(500f, 60f, 150f));
        }

        [Test]
        public void LodFactor_NoCameraIsFull()
        {
            Assert.AreEqual(1f, M.LodFactor(float.NaN, 60f, 150f));
        }

        [Test]
        public void PlaneNormal_OfATiltedRingIsItsAxle()
        {
            // A ring of radius 1.7 in the YZ plane, cambered 20 degrees about Z.
            Quaternion camber = Quaternion.AngleAxis(20f, Vector3.forward);
            var pts = new List<Vector3>();
            for (int i = 0; i < 72; i++)
            {
                float a = i * Mathf.PI * 2f / 72f;
                pts.Add(camber * new Vector3(0f, Mathf.Cos(a) * 1.7f, Mathf.Sin(a) * 1.7f));
            }
            Vector3 n = M.PlaneNormal(pts);
            Assert.Greater(Mathf.Abs(Vector3.Dot(n, camber * Vector3.right)), 0.999f);
            Assert.AreEqual(1f, n.magnitude, 1e-4f);
        }

        [Test]
        public void PlaneNormal_DegenerateThrows()
        {
            var line = new List<Vector3> { Vector3.zero, Vector3.up, Vector3.up * 2f, Vector3.up * 3f };
            Assert.Throws<ArgumentException>(() => M.PlaneNormal(line));
            Assert.Throws<ArgumentException>(() => M.PlaneNormal(new List<Vector3> { Vector3.zero }));
        }
    }
}
