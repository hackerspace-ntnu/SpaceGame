// The one flight every focus camera takes. Angles are blended as numbers about world axes, never
// as a rotation, because a slerp between an eyeline and a shot round the far side of a pack rolls
// through 19° at the halfway point — the camera cartwheels. This pins that the horizon stays level.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public class FocusFlightTests
    {
        private static readonly FlightPose From = new(new Vector3(0f, 1.7f, 0f), 350f, 10f, 70f);
        private static readonly FlightPose To = new(new Vector3(2f, 1.5f, 3f), 10f, 38f, 40f);

        [Test]
        public void AtZeroIsTheFromPose()
        {
            FlightPose p = FocusFlight.Blend(From, To, 0f);
            Assert.AreEqual(From.Position, p.Position);
            Assert.AreEqual(From.Yaw, p.Yaw, 1e-4f);
            Assert.AreEqual(From.Pitch, p.Pitch, 1e-4f);
            Assert.AreEqual(From.Fov, p.Fov, 1e-4f);
        }

        [Test]
        public void AtOneIsTheTargetPose()
        {
            // The two angles are compared AS ANGLES. Mathf.LerpAngle returns from + delta and never
            // wraps the sum back into [0, 360), so the short 20° hop from 350° to 10° lands on
            // 370°: the target heading exactly, and indistinguishable from it to Quaternion.Euler,
            // which is the only thing that ever reads Yaw — but not the same float.
            FlightPose p = FocusFlight.Blend(From, To, 1f);
            Assert.AreEqual(To.Position, p.Position);
            Assert.AreEqual(0f, Mathf.DeltaAngle(To.Yaw, p.Yaw), 1e-4f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(To.Pitch, p.Pitch), 1e-4f);
            Assert.AreEqual(To.Fov, p.Fov, 1e-4f);
        }

        [Test]
        public void TimeOutsideTheFlightIsClamped()
        {
            // Not hypothetical: FlyIn adds a whole unscaled frame before it re-tests the bound, so
            // the last LateUpdate of a flight routinely asks for a t a little past 1.
            FlightPose past = FocusFlight.Blend(From, To, 1.4f);
            Assert.AreEqual(To.Position, past.Position);
            Assert.AreEqual(0f, Mathf.DeltaAngle(To.Yaw, past.Yaw), 1e-4f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(To.Pitch, past.Pitch), 1e-4f);
            Assert.AreEqual(To.Fov, past.Fov, 1e-4f);

            FlightPose before = FocusFlight.Blend(From, To, -0.2f);
            Assert.AreEqual(From.Position, before.Position);
            Assert.AreEqual(0f, Mathf.DeltaAngle(From.Yaw, before.Yaw), 1e-4f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(From.Pitch, before.Pitch), 1e-4f);
            Assert.AreEqual(From.Fov, before.Fov, 1e-4f);
        }

        [Test]
        public void TheFlightIsEasedNotLinear()
        {
            // Taken at a quarter of the way, because SmoothStep(0.5) IS 0.5: every assertion in
            // this file measured at the midpoint passes just as happily with the ease deleted.
            // SmoothStep(0.25) = 3(0.25²) - 2(0.25³) = 0.15625, so the expected values below are
            // the eased ones — the linear position would be (0.5, 1.65, 0.75).
            FlightPose p = FocusFlight.Blend(From, To, 0.25f);

            Assert.AreEqual(0.3125f, p.Position.x, 1e-4f);
            Assert.AreEqual(1.66875f, p.Position.y, 1e-4f);
            Assert.AreEqual(0.46875f, p.Position.z, 1e-4f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(353.125f, p.Yaw), 1e-3f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(14.375f, p.Pitch), 1e-3f);
            Assert.AreEqual(65.3125f, p.Fov, 1e-4f);
        }

        [Test]
        public void YawWrapsTheShortWayRound()
        {
            // 350° to 10° is a 20° turn through north, not a 340° turn the other way.
            FlightPose p = FocusFlight.Blend(From, To, 0.5f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, p.Yaw), 1e-3f);
        }

        [Test]
        public void RollIsZeroAllTheWay()
        {
            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                float roll = FocusFlight.Blend(From, To, t).Rotation.eulerAngles.z;
                Assert.AreEqual(0f, Mathf.DeltaAngle(0f, roll), 1e-3f, "roll at t=" + t);
            }

            // The loop above cannot actually fail: Rotation is Euler(pitch, yaw, 0), so its z is
            // zero by construction. This is the half with teeth — the design it rejects. A slerp
            // between the same two poses takes the geodesic between them and tips the horizon on
            // the way across: these fixtures are a mere 20° of yaw apart and it still rolls ~1.3°,
            // while the 180° crossing a pack focus camera really flies rolls through ~20°.
            Quaternion blended = FocusFlight.Blend(From, To, 0.5f).Rotation;
            Quaternion slerped = Quaternion.Slerp(From.Rotation, To.Rotation, 0.5f);

            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(0f, slerped.eulerAngles.z)), 0.5f,
                "a slerp between these poses should be visibly rolled, or this test guards nothing");
            Assert.Greater(Quaternion.Angle(blended, slerped), 0.5f,
                "the angle blend must not agree with the slerp it exists to avoid");
        }

        [Test]
        public void OfReadsATransformAsYawAndPitch()
        {
            var go = new GameObject("Eye");
            try
            {
                go.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(25f, 200f, 0f));
                FlightPose p = FlightPose.Of(go.transform, 60f);
                Assert.AreEqual(new Vector3(1f, 2f, 3f), p.Position);
                Assert.AreEqual(200f, p.Yaw, 1e-3f);
                Assert.AreEqual(25f, p.Pitch, 1e-3f);
                Assert.AreEqual(60f, p.Fov);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
