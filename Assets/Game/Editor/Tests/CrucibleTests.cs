// The rail's geometry, checked without a scene.
//
// The bend point is the one piece of this room that MUST agree between two machines to the bit:
// nothing about a rope's shape is ever sent, so both machines solve it independently from the same
// replicated positions. A closed form has no iteration count to disagree about — and the first test
// here is what proves the closed form is actually the minimum rather than merely plausible, by
// checking it against brute force at a far finer resolution than the answer needs.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types,
// and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class CrucibleTests
    {
        private static float RopeLength(Vector3 bend, Vector3 from, Vector3 to) =>
            Vector3.Distance(from, bend) + Vector3.Distance(bend, to);

        private static float BestSampled(Vector3 a, Vector3 b, Vector3 from, Vector3 to)
        {
            float best = float.MaxValue;

            for (int i = 0; i <= 20000; i++)
                best = Mathf.Min(best, RopeLength(Vector3.Lerp(a, b, i / 20000f), from, to));

            return best;
        }

        [Test]
        public void ClosestBend_IsTheShortestRopeOverTheRail()
        {
            Random.InitState(20260905);

            for (int trial = 0; trial < 200; trial++)
            {
                Vector3 a = Random.insideUnitSphere * 10f;
                Vector3 b = a + Random.onUnitSphere * Random.Range(2f, 20f);
                Vector3 from = Random.insideUnitSphere * 15f;
                Vector3 to = Random.insideUnitSphere * 15f;

                Vector3 bend = LeashRail.ClosestBend(a, b, from, to);

                Assert.AreEqual(BestSampled(a, b, from, to), RopeLength(bend, from, to), 0.005f,
                                $"trial {trial}: the closed form is not the minimum");
            }
        }

        [Test]
        public void ClosestBend_ClampsToTheSegment()
        {
            Vector3 a = new(0f, 0f, 0f);
            Vector3 b = new(10f, 0f, 0f);

            // Both points well off the near end: the shortest bend available is the mouth itself.
            Vector3 bend = LeashRail.ClosestBend(a, b, new Vector3(-20f, 3f, 0f), new Vector3(-20f, -3f, 0f));

            Assert.AreEqual(0f, bend.x, 0.0001f);
        }

        /// <summary>
        /// Both ends on the rail's own line: every point between them is equally short, so there is
        /// no unique answer. The midpoint is chosen because it is stable — a tie-break that lands at
        /// an end makes the bend jump as the degenerate case is entered and left, which reads in play
        /// as the rope snagging on nothing.
        /// </summary>
        [Test]
        public void ClosestBend_WithBothPointsOnTheRailsOwnLine_FallsBackToTheMidpoint()
        {
            Vector3 bend = LeashRail.ClosestBend(
                new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f),
                new Vector3(2f, 0f, 0f), new Vector3(6f, 0f, 0f));

            Assert.AreEqual(4f, bend.x, 0.0001f);
        }

        /// <summary>
        /// The whole control scheme in one assertion: back away from your rail and the far end of the
        /// rope is drawn toward it, because rope spent outside is rope the inside no longer has.
        /// </summary>
        [Test]
        public void WalkingAwayFromTheRail_SpendsMoreRope()
        {
            Vector3 a = new(-5f, 0f, 0f);
            Vector3 b = new(5f, 0f, 0f);
            Vector3 cell = new(0f, -6f, 0f);

            Vector3 near = new(0f, 0f, 2f);
            Vector3 far = new(0f, 0f, 8f);

            float spentNear = RopeLength(LeashRail.ClosestBend(a, b, near, cell), near, cell);
            float spentFar = RopeLength(LeashRail.ClosestBend(a, b, far, cell), far, cell);

            Assert.Greater(spentFar, spentNear,
                           "walking away from the slot has to cost rope, or there is no winch");
        }

        /// <summary>
        /// Walking ALONG the rail sweeps the load sideways — the second of the two axes one player
        /// gets while moving.
        /// </summary>
        [Test]
        public void WalkingAlongTheRail_MovesTheBendTheSameWay()
        {
            Vector3 a = new(-5f, 0f, 0f);
            Vector3 b = new(5f, 0f, 0f);
            Vector3 cell = new(0f, -6f, 0f);

            float left = LeashRail.ClosestBend(a, b, new Vector3(-3f, 0f, 4f), cell).x;
            float right = LeashRail.ClosestBend(a, b, new Vector3(3f, 0f, 4f), cell).x;

            Assert.Less(left, right);
        }

        [Test]
        public void TheFloorIsLava_OnlyWhenThereIsSomeoneToCoordinateWith()
        {
            Assert.IsFalse(CruciblePit.HazardFor(0));
            Assert.IsFalse(CruciblePit.HazardFor(1), "one rope cannot hold the cell up at all");
            Assert.IsTrue(CruciblePit.HazardFor(2));
            Assert.IsTrue(CruciblePit.HazardFor(4));
        }
    }
}
