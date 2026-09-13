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
        private const float EdgeFalloff = 0.5f;

        /// <summary>The gauntlet's authored core — the fraction of the radius that takes full force.</summary>
        private const float CoreFraction = 0.4f;

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
                new Vector3(0, 0, 3f), 0.5f, 10f, 12f, 22f, 27f, CoreFraction, EdgeFalloff);
            Assert.Greater(v.y, 0f);
        }

        [Test]
        public void FlingVelocity_PointBlankFullCharge_HitsMaxSpeed()
        {
            Vector3 v = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 0, 0.01f), 1f, 10f, 12f, 22f, 27f, CoreFraction, EdgeFalloff);
            Assert.AreEqual(22f, v.magnitude, 0.05f);
        }

        [Test]
        public void FlingVelocity_EdgeHit_IsWeakerThanCloseHit()
        {
            Vector3 close = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 0, 1f), 1f, 10f, 12f, 22f, 27f, CoreFraction, EdgeFalloff);
            Vector3 edge = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 0, 10f), 1f, 10f, 12f, 22f, 27f, CoreFraction, EdgeFalloff);
            Assert.Greater(close.magnitude, edge.magnitude);
        }

        [Test]
        public void FlingVelocity_InsideTheCore_TakesTheFullSpeed()
        {
            // The point of the core: a body 3 m out of a 10 m blast is an ORDINARY hit, and it must
            // get the authored number rather than a discount for not standing in the caster.
            Vector3 v = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 0, 3f), 1f, 10f, 12f, 22f, 27f, CoreFraction, EdgeFalloff);
            Assert.AreEqual(22f, v.magnitude, 0.05f);
        }

        [Test]
        public void FlingVelocity_MidCone_StaysWellClearOfSprintSpeed()
        {
            // A player victim keeps a fling only while it beats their own top speed
            // (PlayerMovement.ShouldEndCarry, 9 m/s sprinting). A mid-cone hit that does not clear
            // that by a margin is the blast being confiscated on the tick it lands, which is
            // exactly what "way too weak" looked like. Numbers are the gauntlet's authored range,
            // flingSpeed and upwardTilt — the thundergun has no charge, so min == max and the
            // charge argument is always 1.
            Vector3 v = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 0, 10f), 1f, 20f, 48f, 48f, 30f, CoreFraction, EdgeFalloff);
            Assert.Greater(Vector3.ProjectOnPlane(v, Vector3.up).magnitude, 30f);
        }

        [Test]
        public void FlingVelocity_ConeEdge_IsStillALaunchAtTheGauntletsNumbers()
        {
            // The other half of the "way too weak" report. An edge hit is meant to be the weakest
            // hit in the cone, but at the old numbers it landed UNDER the CarryMomentum floor and
            // was deleted on the tick it was applied, so half the cone did nothing at all. At the
            // thundergun's numbers the worst hit available — full range, full falloff — still
            // clears a 9 m/s sprint by a wide margin.
            Vector3 v = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 0, 20f), 1f, 20f, 48f, 48f, 30f, CoreFraction, EdgeFalloff);
            Assert.Greater(Vector3.ProjectOnPlane(v, Vector3.up).magnitude, 15f);
        }

        [Test]
        public void FlingVelocity_ZeroCore_FallsOffFromTheOrigin()
        {
            // The Sucker Puncher's authored value. Its wave is centred on the point of contact, so
            // it must keep the no-core behaviour: falloff begins immediately.
            Vector3 v = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 0, 5f), 1f, 10f, 12f, 22f, 27f, 0f, 0.3f);
            Assert.AreEqual(22f * Mathf.Lerp(1f, 0.3f, 0.5f), v.magnitude, 0.05f);
        }

        [Test]
        public void DistanceFalloff_IsFullAtTheCoreEdge_AndEdgeFalloffAtTheRim()
        {
            Assert.AreEqual(1f, RepulsorBlast.DistanceFalloff(4f, 10f, 0.4f, 0.5f), 1e-4f);
            Assert.AreEqual(0.75f, RepulsorBlast.DistanceFalloff(7f, 10f, 0.4f, 0.5f), 1e-4f);
            Assert.AreEqual(0.5f, RepulsorBlast.DistanceFalloff(10f, 10f, 0.4f, 0.5f), 1e-4f);
            // Past the rim is clamped, not extrapolated. InCone rejects it anyway, but a negative
            // multiplier here would fling the victim INTO the blast.
            Assert.AreEqual(0.5f, RepulsorBlast.DistanceFalloff(30f, 10f, 0.4f, 0.5f), 1e-4f);
        }

        [Test]
        public void DistanceFalloff_FullCore_NeverDiminishes()
        {
            Assert.AreEqual(1f, RepulsorBlast.DistanceFalloff(10f, 10f, 1f, 0.1f), 1e-4f);
        }

        [Test]
        public void FlingVelocity_PushesHorizontallyAwayFromOrigin()
        {
            Vector3 v = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(3f, 0, 4f), 1f, 10f, 12f, 22f, 27f, CoreFraction, EdgeFalloff);
            Vector3 flat = Vector3.ProjectOnPlane(v, Vector3.up);
            Assert.Greater(Vector3.Dot(flat.normalized, new Vector3(0.6f, 0, 0.8f)), 0.99f);
        }

        [Test]
        public void FlingVelocity_TargetDirectlyOverOrigin_FallsBackToAimDirection()
        {
            Vector3 v = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 2f, 0), 1f, 10f, 12f, 22f, 27f, CoreFraction, EdgeFalloff);
            Vector3 flat = Vector3.ProjectOnPlane(v, Vector3.up);
            Assert.Greater(Vector3.Dot(flat.normalized, Vector3.forward), 0.99f);
        }

        // ── PushDirection / DirectedFling ──────────────────────────────────────
        //
        // The dial between a detonation and a directed blast. Worth pinning because both ends of it
        // are load-bearing and both fail silently: at 0 the gauntlet goes back to throwing bodies
        // in every direction it spans, and at 1 the rocket and the punch would stop being
        // explosions at all.

        [Test]
        public void PushDirection_AtGauntletBias_ThrowsASideOnBodyForward()
        {
            // A body standing 60 degrees off the aim — well inside a 70 degree cone, and exactly
            // the case that made the old radial blast read as going everywhere. It must leave
            // mostly DOWN THE AIM, not out to the side it happened to be standing on.
            var target = new Vector3(Mathf.Sin(60f * Mathf.Deg2Rad), 0f, Mathf.Cos(60f * Mathf.Deg2Rad)) * 8f;

            Vector3 dir = RepulsorBlast.PushDirection(Vector3.zero, Vector3.forward, target, 0.8f);

            Assert.Greater(Vector3.Dot(dir, Vector3.forward), 0.9f);
            Assert.Greater(dir.x, 0f, "the radial minority must survive, or the crowd stacks");
        }

        [Test]
        public void PushDirection_AtZeroBias_IsPurelyRadial_AndMatchesFlingVelocity()
        {
            var target = new Vector3(3f, 0f, 4f);

            Vector3 dir = RepulsorBlast.PushDirection(Vector3.zero, Vector3.forward, target, 0f);
            Assert.Greater(Vector3.Dot(dir, new Vector3(0.6f, 0f, 0.8f)), 0.999f);

            // The rocket and the punch reach this through FlingVelocity, which is DirectedFling
            // pinned to bias 0. If the two ever disagree, those two artifacts changed shape without
            // anybody editing them.
            Vector3 flung = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward, target,
                1f, 10f, 22f, 22f, 27f, CoreFraction, EdgeFalloff);
            Vector3 directed = RepulsorBlast.DirectedFling(Vector3.zero, Vector3.forward, target,
                10f, 22f, 27f, CoreFraction, EdgeFalloff, aimBias: 0f);
            Assert.AreEqual(0f, (flung - directed).magnitude, 1e-4f);
        }

        [Test]
        public void PushDirection_BodyBehindTheCaster_StillGetsADirection()
        {
            // The one degenerate case in the blend: radial and aim are exactly opposed, so at
            // bias 0.5 the lerp lands on the zero vector and a naive normalize returns NaN.
            Vector3 dir = RepulsorBlast.PushDirection(Vector3.zero, Vector3.forward,
                                                      new Vector3(0f, 0f, -6f), 0.5f);

            Assert.AreEqual(1f, dir.magnitude, 1e-3f);
            Assert.IsFalse(float.IsNaN(dir.x) || float.IsNaN(dir.z));
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
