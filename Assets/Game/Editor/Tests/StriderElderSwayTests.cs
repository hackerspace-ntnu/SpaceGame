// The elder's upper body has no clips: its chest rolls and leans with the legs' gait, and its head
// holds against the chest so the gaze stays steady.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Creatures;

namespace SpaceGame.EditorTools
{
    public class StriderElderSwayTests
    {
        private const float Roll = 3f, Lean = 4f, HeadSteady = 0.6f;

        [Test]
        public void StandingStill_PosesNothing()
        {
            for (float phase = 0f; phase < 1f; phase += 0.05f)
            {
                Assert.AreEqual(Vector3.zero, StriderElderSwayMath.Chest(phase, 0f, Roll, Lean));
                Assert.AreEqual(Vector3.zero, StriderElderSwayMath.Head(StriderElderSwayMath.Chest(phase, 0f, Roll, Lean), HeadSteady));
            }
        }

        [Test]
        public void Walking_StaysWithinItsAmplitudes_AndRollsBothWays()
        {
            float min = 0f, max = 0f;
            for (float phase = 0f; phase < 1f; phase += 0.01f)
            {
                Vector3 chest = StriderElderSwayMath.Chest(phase, 1f, Roll, Lean);
                Assert.LessOrEqual(Mathf.Abs(chest.z), Roll + 1e-4f);
                Assert.AreEqual(Lean, chest.x, 1e-4f, "at full speed it leans into the walk");
                min = Mathf.Min(min, chest.z);
                max = Mathf.Max(max, chest.z);
            }
            Assert.Less(min, -Roll * 0.9f);
            Assert.Greater(max, Roll * 0.9f);
        }

        [Test]
        public void TheHead_CountersTheChestsRoll()
        {
            Vector3 chest = StriderElderSwayMath.Chest(0.125f, 1f, Roll, Lean);
            Vector3 head = StriderElderSwayMath.Head(chest, HeadSteady);
            Assert.AreEqual(-chest.z * HeadSteady, head.z, 1e-4f);
            Assert.AreEqual(-chest.x * HeadSteady, head.x, 1e-4f);
        }

        [Test]
        public void SpeedIsClamped()
        {
            Assert.AreEqual(StriderElderSwayMath.Chest(0.3f, 1f, Roll, Lean), StriderElderSwayMath.Chest(0.3f, 5f, Roll, Lean));
        }
    }
}
