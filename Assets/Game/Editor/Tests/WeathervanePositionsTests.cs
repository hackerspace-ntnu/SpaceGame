// The weathervane puzzle's rules. The one property everything else rests on is that a scramble is
// always solvable: the coupling (a crank turns its own vane and the next one) is not invertible on
// a ring of four, so an arrangement picked at random can be a puzzle with no answer.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Gameplay.Puzzles;

namespace SpaceGame.EditorTools
{
    public class WeathervanePositionsTests
    {
        [Test]
        public void ACrankTurnsItsOwnVaneAndTheNextOne()
        {
            WeathervanePositions ring = WeathervanePositions.Solved(4).Turn(1);

            Assert.AreEqual("0110", ring.ToString());
        }

        [Test]
        public void TheLastCrankWrapsRoundToTheFirstVane()
        {
            WeathervanePositions ring = WeathervanePositions.Solved(4).Turn(3);

            Assert.AreEqual("1001", ring.ToString());
        }

        [Test]
        public void FourTurnsOfOneCrankIsNoChange()
        {
            WeathervanePositions ring = WeathervanePositions.Solved(4).Turn(2);
            WeathervanePositions after = ring.Turn(0).Turn(0).Turn(0).Turn(0);

            Assert.AreEqual(ring, after);
        }

        [Test]
        public void SomeArrangementsCannotBeSolved()
        {
            // The reason a ring is scrambled by cranking rather than by picking positions: this
            // coupling reaches only a quarter of the 256 arrangements of four vanes.
            Assert.AreEqual(64, Reachable(4).Count);
        }

        [Test]
        public void EveryScrambleIsUnsolvedAndReachableFromSolved()
        {
            HashSet<int> reachable = Reachable(4);
            var random = new Random(1234);

            for (int i = 0; i < 500; i++)
            {
                WeathervanePositions ring = WeathervanePositions.Scramble(4, 6, random);
                Assert.IsFalse(ring.IsSolved);
                Assert.IsTrue(reachable.Contains(ring.Packed), ring.ToString());
            }
        }

        [Test]
        public void PackingRoundTripsThroughTheWire()
        {
            WeathervanePositions ring = WeathervanePositions.Solved(4).Turn(0).Turn(0).Turn(1);
            var copy = new WeathervanePositions(4, ring.Packed);

            Assert.AreEqual(ring, copy);
            Assert.AreEqual("2310", copy.ToString());
        }

        // Breadth-first over every crank from the solved pose.
        private static HashSet<int> Reachable(int count)
        {
            var seen = new HashSet<int> { 0 };
            var queue = new Queue<WeathervanePositions>();
            queue.Enqueue(WeathervanePositions.Solved(count));
            while (queue.Count > 0)
            {
                WeathervanePositions ring = queue.Dequeue();
                for (int crank = 0; crank < count; crank++)
                {
                    WeathervanePositions next = ring.Turn(crank);
                    if (seen.Add(next.Packed)) queue.Enqueue(next);
                }
            }
            return seen;
        }
    }
}
