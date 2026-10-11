using NUnit.Framework;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    /// <summary>The round the workshop's hoist makes by itself: the same place for the same second, and its two manners.</summary>
    public class WorkshopHoistRoundTests
    {
        private const float Travel = 2f;
        private const float Drop = 3f;

        // Two calls: one over open floor, one where something stands two stops under the hook.
        private static WorkshopHoistRound Round()
        {
            var limits = new int[16];
            for (int i = 0; i < limits.Length; i++) limits[i] = 4;
            limits[(3 * 4) + 1] = 2;

            return new WorkshopHoistRound(new Vector3Int(4, 4, 5), limits, new Vector3(Travel, Travel, Drop), 1f, 2f,
                                          new[] { new Vector2Int(1, 2), new Vector2Int(3, 1) });
        }

        [Test]
        public void TheRoundStartsParkedAndComesBackToWhereItStarted()
        {
            WorkshopHoistRound round = Round();

            Assert.Greater(round.Seconds, 0d);
            Assert.AreEqual(Vector3.zero, round.At(0d));
            Assert.Less(round.At(round.Seconds - 0.01d).magnitude, 0.05f, "home again at the end, hook up");

            for (double t = 0d; t < round.Seconds; t += 1.37d)
                Assert.Less((round.At(t) - round.At(t + (3d * round.Seconds))).magnitude, 1e-3f, $"same second of a later round, t={t}");
        }

        [Test]
        public void ItOnlyTravelsWithTheHookRightUp()
        {
            WorkshopHoistRound round = Round();

            for (double t = 0d; t < round.Seconds; t += 0.05d)
            {
                Vector3 at = round.At(t);
                bool between = !Whole(at.x / Travel) || !Whole(at.y / Travel);
                if (between) Assert.AreEqual(0f, at.z, 1e-4f, $"travelling at t={t} with the hook down");
            }
        }

        [Test]
        public void TheHookStopsOnWhatStandsUnderIt()
        {
            WorkshopHoistRound round = Round();
            float deepestOpen = 0f, deepestOverLoad = 0f;

            for (double t = 0d; t < round.Seconds; t += 0.05d)
            {
                Vector3 at = round.At(t);
                bool overLoad = Mathf.Approximately(at.x, 3f * Travel) && Mathf.Approximately(at.y, 1f * Travel);
                if (overLoad) deepestOverLoad = Mathf.Max(deepestOverLoad, at.z);
                else deepestOpen = Mathf.Max(deepestOpen, at.z);
            }

            Assert.AreEqual(2f * Drop, deepestOverLoad, 1e-3f, "two stops down and no further");
            Assert.AreEqual(4f * Drop, deepestOpen, 1e-3f, "all the way down over open floor");
        }

        private static bool Whole(float stops) => Mathf.Abs(stops - Mathf.Round(stops)) < 1e-4f;
    }
}
