using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class GravelBlastMathTests
    {
        [Test]
        public void Backfires_ExactlyOneSeedInChance_AcrossAContiguousRange()
        {
            // The uint modulo makes every run of `chance` consecutive seeds contain exactly one
            // backfire — the "1 in 10" on the tin is exact, not approximate.
            int hits = 0;
            for (int seed = -1000; seed < 1000; seed++)
                if (GravelBlastMath.Backfires(seed, 10)) hits++;
            Assert.AreEqual(200, hits);
        }

        [Test]
        public void Backfires_ChanceZero_NeverFires()
        {
            for (int seed = -50; seed < 50; seed++)
                Assert.IsFalse(GravelBlastMath.Backfires(seed, 0));
        }

        [Test]
        public void Backfires_IsDeterministicInTheSeed()
        {
            for (int seed = -50; seed < 50; seed++)
                Assert.AreEqual(GravelBlastMath.Backfires(seed, 10),
                                GravelBlastMath.Backfires(seed, 10));
        }

        [Test]
        public void PelletDirections_SameSeed_SameShot()
        {
            // The whole authority scheme rests on this: the server's damage trace and every
            // machine's cosmetic spray are derived independently from the seed and must agree.
            Vector3[] a = GravelBlastMath.PelletDirections(1234, Quaternion.identity, 14, 7f);
            Vector3[] b = GravelBlastMath.PelletDirections(1234, Quaternion.identity, 14, 7f);
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++) Assert.AreEqual(a[i], b[i]);
        }

        [Test]
        public void PelletDirections_DifferentSeeds_DifferentShots()
        {
            Vector3[] a = GravelBlastMath.PelletDirections(1, Quaternion.identity, 14, 7f);
            Vector3[] b = GravelBlastMath.PelletDirections(2, Quaternion.identity, 14, 7f);
            Assert.AreNotEqual(a[0], b[0]);
        }

        [Test]
        public void PelletDirections_StayInsideTheCone_AndAreUnitLength()
        {
            var aim = Quaternion.LookRotation(new Vector3(1f, 0.3f, 0.5f));
            foreach (Vector3 dir in GravelBlastMath.PelletDirections(99, aim, 200, 7f))
            {
                Assert.AreEqual(1f, dir.magnitude, 1e-4f);
                Assert.LessOrEqual(Vector3.Angle(aim * Vector3.forward, dir), 7f + 1e-3f);
            }
        }

        [Test]
        public void PelletDirections_NonPositiveCount_IsEmptyNotAnError()
        {
            Assert.AreEqual(0, GravelBlastMath.PelletDirections(1, Quaternion.identity, 0, 7f).Length);
            Assert.AreEqual(0, GravelBlastMath.PelletDirections(1, Quaternion.identity, -3, 7f).Length);
        }

        [Test]
        public void BackfireVelocity_PushesOppositeTheAim_WithLift()
        {
            Vector3 v = GravelBlastMath.BackfireVelocity(Vector3.forward, 9f, 35f);
            Assert.Less(v.z, 0f);
            Assert.Greater(v.y, 0f);
            Assert.AreEqual(9f, v.magnitude, 1e-3f);
        }

        [Test]
        public void BackfireVelocity_AimedStraightDown_ResolvesToStraightUp()
        {
            // ProjectOnPlane degenerates when the aim is vertical; the kick must not vanish there.
            Vector3 v = GravelBlastMath.BackfireVelocity(Vector3.down, 9f, 35f);
            Assert.AreEqual(9f, v.y, 1e-3f);
        }
    }
}
