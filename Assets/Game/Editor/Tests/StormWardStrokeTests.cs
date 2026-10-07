// The storm ward's stroke, as numbers: at rest between pulses, up before the impact, below rest AT the
// impact, and back to rest after. The shockwave fires at the impact, so a stroke whose low point
// drifted off it would strike the air a moment after the dust had already left.
using NUnit.Framework;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class StormWardStrokeTests
    {
        private const float Tolerance = 1e-4f;
        private readonly StormWardStroke stroke = new StormWardStroke();

        [Test]
        public void AtRestBetweenPulses()
        {
            Assert.AreEqual(0f, stroke.Offset(untilImpact: stroke.Lead + 1f, sinceImpact: stroke.Length), Tolerance);
        }

        [Test]
        public void RisesBeforeTheImpactAndIsLowestAtIt()
        {
            float lowest = stroke.Offset(0f, 0f);
            Assert.Less(lowest, 0f, "the ring drives below its rest at the impact");

            float highest = float.MinValue;
            for (float u = stroke.Lead; u > 0f; u -= 0.01f)
            {
                float offset = stroke.Offset(u, stroke.Length);
                highest = UnityEngine.Mathf.Max(highest, offset);
                Assert.GreaterOrEqual(offset, lowest - Tolerance, $"lower than the impact {u:F2} s before it");
            }
            Assert.Greater(highest, 0f, "the ring rises before it slams");
        }

        [Test]
        public void SettlesBackToRestAfterTheImpact()
        {
            float halfway = stroke.Offset(stroke.Lead + 1f, 0.1f);
            Assert.Less(halfway, 0f);
            Assert.Greater(halfway, stroke.Offset(0f, 0f));
            Assert.AreEqual(0f, stroke.Offset(stroke.Lead + 1f, stroke.Length), Tolerance);
        }
    }
}
