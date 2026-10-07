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
