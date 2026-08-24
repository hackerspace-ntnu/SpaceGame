using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class RepulsorBlastMathTests
    {
        /// <summary>
        /// The gauntlet's authored edge falloff. Passed explicitly at every call site because
        /// FlingVelocity deliberately has no default — the serialized field on each artifact is
        /// the only source of truth for it.
        /// </summary>
        private const float EdgeFalloff = 0.35f;

        [Test]
        public void Charge_ClampsToOne_AndHasFloor()
        {
            Assert.AreEqual(1f, RepulsorBlast.ChargeFrom(5f, 1.2f, 0.25f), 1e-4f);
            Assert.AreEqual(0.25f, RepulsorBlast.ChargeFrom(0f, 1.2f, 0.25f), 1e-4f);
            Assert.AreEqual(0.5f, RepulsorBlast.ChargeFrom(0.6f, 1.2f, 0.25f), 1e-4f);
        }

        [Test]
        public void InCone_AcceptsFront_RejectsBehind_RejectsFar_AcceptsPointBlank()
        {
            Vector3 origin = Vector3.zero, aim = Vector3.forward;
            Assert.IsTrue(RepulsorBlast.InCone(origin, aim, new Vector3(0, 0, 5f), 10f, 40f));
            Assert.IsFalse(RepulsorBlast.InCone(origin, aim, new Vector3(0, 0, -5f), 10f, 40f));
            Assert.IsFalse(RepulsorBlast.InCone(origin, aim, new Vector3(0, 0, 11f), 10f, 40f));
            Assert.IsTrue(RepulsorBlast.InCone(origin, aim, origin, 10f, 40f));
            // 45° off-axis at halfAngle 40° is outside the cone
            Assert.IsFalse(RepulsorBlast.InCone(origin, aim, new Vector3(5f, 0, 5f), 10f, 40f));
        }

        [Test]
        public void FlingVelocity_AlwaysHasUpwardComponent()
        {
            Vector3 v = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 0, 3f), 0.5f, 10f, 12f, 22f, 27f, EdgeFalloff);
            Assert.Greater(v.y, 0f);
        }

        [Test]
        public void FlingVelocity_PointBlankFullCharge_HitsMaxSpeed()
        {
            Vector3 v = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 0, 0.01f), 1f, 10f, 12f, 22f, 27f, EdgeFalloff);
            Assert.AreEqual(22f, v.magnitude, 0.05f);
        }

        [Test]
        public void FlingVelocity_EdgeHit_IsWeakerThanCloseHit()
        {
            Vector3 close = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 0, 1f), 1f, 10f, 12f, 22f, 27f, EdgeFalloff);
            Vector3 edge = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 0, 10f), 1f, 10f, 12f, 22f, 27f, EdgeFalloff);
            Assert.Greater(close.magnitude, edge.magnitude);
        }

        [Test]
        public void FlingVelocity_PushesHorizontallyAwayFromOrigin()
        {
            Vector3 v = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(3f, 0, 4f), 1f, 10f, 12f, 22f, 27f, EdgeFalloff);
            Vector3 flat = Vector3.ProjectOnPlane(v, Vector3.up);
            Assert.Greater(Vector3.Dot(flat.normalized, new Vector3(0.6f, 0, 0.8f)), 0.99f);
        }

        [Test]
        public void FlingVelocity_TargetDirectlyOverOrigin_FallsBackToAimDirection()
        {
            Vector3 v = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 2f, 0), 1f, 10f, 12f, 22f, 27f, EdgeFalloff);
            Vector3 flat = Vector3.ProjectOnPlane(v, Vector3.up);
            Assert.Greater(Vector3.Dot(flat.normalized, Vector3.forward), 0.99f);
        }

        // ── Launch ─────────────────────────────────────────────────────────────
        //
        // The shared tail of every knockback in the game: the repulsor's blast, the Sucker
        // Puncher's direct hit and its recoil all end here. Worth pinning on its own, because
        // "the punch launches nobody" and "the punch launches everybody straight up" are the
        // same one-line mistake and neither shows up in FlingVelocity's own tests.

        [Test]
        public void Launch_KeepsSpeed_RegardlessOfTilt()
        {
            foreach (float tilt in new[] { 0f, 22f, 45f, 89f })
                Assert.AreEqual(20f, RepulsorBlast.Launch(Vector3.forward, tilt, 20f).magnitude,
                                0.01f, $"tilt {tilt}");
        }

        [Test]
        public void Launch_TiltsUpward_ButKeepsTheHorizontalDirection()
        {
            Vector3 v = RepulsorBlast.Launch(new Vector3(3f, 0f, 4f), 30f, 20f);

            Assert.AreEqual(20f * Mathf.Sin(30f * Mathf.Deg2Rad), v.y, 0.01f);
            Vector3 flat = Vector3.ProjectOnPlane(v, Vector3.up);
            Assert.Greater(Vector3.Dot(flat.normalized, new Vector3(0.6f, 0f, 0.8f)), 0.999f);
        }

        [Test]
        public void Launch_IgnoresTheVerticalPartOfTheDirection()
        {
            // A punch aimed steeply down must still throw the victim ALONG the ground, not into
            // it — the tilt is the only source of vertical, by design (see FlungBody).
            Vector3 v = RepulsorBlast.Launch(new Vector3(0f, -9f, 1f), 25f, 18f);
            Assert.Greater(v.y, 0f);
            Assert.Greater(Vector3.Dot(Vector3.ProjectOnPlane(v, Vector3.up).normalized,
                                       Vector3.forward), 0.999f);
        }

        [Test]
        public void Launch_StraightDownDirection_GoesUpRatherThanNaN()
        {
            // ProjectOnPlane leaves nothing to normalise here. The guard matters because this is
            // reachable in play: punching straight down at your own feet.
            Vector3 v = RepulsorBlast.Launch(Vector3.down, 30f, 12f);
            Assert.IsFalse(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z));
            Assert.AreEqual(12f, v.magnitude, 0.01f);
            Assert.Greater(v.y, 0f);
        }
    }
}
