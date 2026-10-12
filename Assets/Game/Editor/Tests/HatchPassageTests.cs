// Which way a hatch crawl goes. Getting the side wrong crawls a player standing outside back out
// into the sand (or one inside deeper into the hold) — the hatch "works" and goes nowhere.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class HatchPassageTests
    {
        private static readonly Vector3 Outer = new Vector3(5f, 4f, 2f);
        private static readonly Vector3 Sill = new Vector3(3.6f, 3.7f, 2f);
        private static readonly Vector3 Inner = new Vector3(1.5f, 1.1f, 2f);

        [Test]
        public void OnTheFenderCrawlsIn()
        {
            Vector3 player = new Vector3(6f, 4f, 1.5f);
            Assert.IsTrue(HatchPassage.IsOutside(player, Outer, Inner));
            CollectionAssert.AreEqual(new[] { Outer, Sill, Inner }, HatchPassage.Route(true, Outer, Sill, Inner));
        }

        [Test]
        public void InTheHoldCrawlsOut()
        {
            Vector3 player = new Vector3(1.2f, 1.1f, 2.5f);
            Assert.IsFalse(HatchPassage.IsOutside(player, Outer, Inner));
            CollectionAssert.AreEqual(new[] { Inner, Sill, Outer }, HatchPassage.Route(false, Outer, Sill, Inner));
        }

        [Test]
        public void EveryRoutePassesTheSillBetweenTheFloors()
        {
            foreach (bool fromOutside in new[] { true, false })
            {
                Vector3[] route = HatchPassage.Route(fromOutside, Outer, Sill, Inner);
                Assert.AreEqual(3, route.Length);
                Assert.AreEqual(Sill, route[1], "the sill is always the middle of the crawl");
                Assert.AreNotEqual(route[0], route[2]);
            }
        }
    }
}
