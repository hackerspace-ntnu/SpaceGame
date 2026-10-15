using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class PelletShotMathTests
    {
        [Test]
        public void PelletDirections_SameSeed_SameShot()
        {
            // The whole authority scheme rests on this: the server's damage trace and every
            // machine's cosmetic spray are derived independently from the seed and must agree.
            Vector3[] a = PelletShotMath.PelletDirections(1234, Quaternion.identity, 14, 7f);
            Vector3[] b = PelletShotMath.PelletDirections(1234, Quaternion.identity, 14, 7f);
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++) Assert.AreEqual(a[i], b[i]);
        }

        [Test]
        public void PelletDirections_DifferentSeeds_DifferentShots()
        {
            Vector3[] a = PelletShotMath.PelletDirections(1, Quaternion.identity, 14, 7f);
            Vector3[] b = PelletShotMath.PelletDirections(2, Quaternion.identity, 14, 7f);
            Assert.AreNotEqual(a[0], b[0]);
        }

        [Test]
        public void PelletDirections_StayInsideTheCone_AndAreUnitLength()
        {
            var aim = Quaternion.LookRotation(new Vector3(1f, 0.3f, 0.5f));
            foreach (Vector3 dir in PelletShotMath.PelletDirections(99, aim, 200, 7f))
            {
                Assert.AreEqual(1f, dir.magnitude, 1e-4f);
                Assert.LessOrEqual(Vector3.Angle(aim * Vector3.forward, dir), 7f + 1e-3f);
            }
        }

        [Test]
        public void PelletDirections_NonPositiveCount_IsEmptyNotAnError()
        {
            Assert.AreEqual(0, PelletShotMath.PelletDirections(1, Quaternion.identity, 0, 7f).Length);
            Assert.AreEqual(0, PelletShotMath.PelletDirections(1, Quaternion.identity, -3, 7f).Length);
        }

        [Test]
        public void DamageFalloff_InsideFullDamageRange_IsUndiminished()
        {
            Assert.AreEqual(1f, PelletShotMath.DamageFalloff(0f, 15f, 70f, 0.25f), 1e-4f);
            Assert.AreEqual(1f, PelletShotMath.DamageFalloff(15f, 15f, 70f, 0.25f), 1e-4f);
        }

        [Test]
        public void DamageFalloff_TapersToTheFarFraction_AndNoFurther()
        {
            // The whole point of the taper: a seventy-metre shot must not be a point-blank shot,
            // and it must not be nothing either.
            Assert.AreEqual(0.625f, PelletShotMath.DamageFalloff(42.5f, 15f, 70f, 0.25f), 1e-3f);
            Assert.AreEqual(0.25f, PelletShotMath.DamageFalloff(70f, 15f, 70f, 0.25f), 1e-4f);
            Assert.AreEqual(0.25f, PelletShotMath.DamageFalloff(500f, 15f, 70f, 0.25f), 1e-4f);
        }

        [Test]
        public void DamageFalloff_TaperWithNoRoomToRun_IsFullDamage_NotADivideByZero()
        {
            // Reachable from the Inspector: fullDamageRange is clamped to range, so the two can be
            // equal. That says "no falloff", so the whole reach is worth full damage — and nothing
            // divides by the zero-metre taper on the way there.
            Assert.AreEqual(1f, PelletShotMath.DamageFalloff(70f, 70f, 70f, 0.25f), 1e-4f);
            Assert.AreEqual(1f, PelletShotMath.DamageFalloff(69f, 70f, 70f, 0.25f), 1e-4f);
        }

        [Test]
        public void PelletDirections_ZeroSpread_IsExactlyTheAim()
        {
            // The basic gun: one pellet, no cone. Its round must go where the crosshair is, not
            // somewhere a rounding error away from it.
            var aim = Quaternion.LookRotation(new Vector3(0.2f, -0.1f, 1f));
            Vector3 dir = PelletShotMath.PelletDirections(7, aim, 1, 0f)[0];
            Assert.Less(Vector3.Angle(aim * Vector3.forward, dir), 1e-3f);
        }
    }
}
