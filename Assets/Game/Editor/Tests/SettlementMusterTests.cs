// The muster spot's rule, as a pure function of street polylines: the street whose trail reaches farthest from the
// centre is the road out, whatever order the streets come in; the spot stands a few metres back inside the end of that
// street's PAVING (not its stepping-stone trail), measured along the street; it faces out along the paving; and the
// streets of a settlement generated earlier are traced from the pieces laid along them, out from the centre, through
// every piece of a row, leaving out pieces no street reaches.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.World;
using UnityEngine;
using StreetPoint = SpaceGame.World.SettlementMuster.StreetPoint;

namespace SpaceGame.EditorTools
{
    public class SettlementMusterTests
    {
        private const float Inset = 5f, Tolerance = 1e-3f;
        private static readonly Vector3 Centre = new Vector3(100f, 20f, -40f);

        [Test]
        public void FarthestStreetEnd_Wins()
        {
            var streets = new List<StreetPoint[]>
            {
                Paved(Vector3.zero, new Vector3(0f, 0f, 40f)),
                Paved(Vector3.zero, new Vector3(60f, 0f, 0f)),
                Paved(Vector3.zero, new Vector3(0f, 0f, -50f)),
            };

            Assert.IsTrue(SettlementMuster.TryChoose(Centre, streets, Inset, out Pose pose));
            AssertNear(Centre + new Vector3(60f - Inset, 0f, 0f), pose.position, "on the east street, inset from its end");
        }

        [Test]
        public void Spot_IsInsetFromThePavedEnd_NotTheTrailEnd()
        {
            var north = new[]
            {
                At(Vector3.zero, true), At(new Vector3(0f, 0f, 20f), true), At(new Vector3(0f, 0f, 40f), true),
                At(new Vector3(0f, 0f, 60f), false), At(new Vector3(0f, 0f, 100f), false),
            };
            StreetPoint[] east = Paved(Vector3.zero, new Vector3(70f, 0f, 0f));

            Assert.IsTrue(SettlementMuster.TryChoose(Centre, new List<StreetPoint[]> { east, north }, Inset, out Pose pose));
            AssertNear(Centre + new Vector3(0f, 0f, 40f - Inset), pose.position,
                       "the road out is the one whose TRAIL reaches farthest; the spot is at the edge of its paving");
            AssertNear(Vector3.forward, pose.forward, "facing out along the paving");
        }

        [Test]
        public void StreetPavedNowhere_MustersAtItsEnd()
        {
            var trail = new[] { At(Vector3.zero, true), At(new Vector3(30f, 0f, 0f), false), At(new Vector3(60f, 0f, 0f), false) };

            Assert.IsTrue(SettlementMuster.TryChoose(Centre, new List<StreetPoint[]> { trail }, Inset, out Pose pose));
            AssertNear(Centre + new Vector3(60f - Inset, 0f, 0f), pose.position, "paved only at the centre: the trail is the whole street");
        }

        [Test]
        public void Spot_IsInsetAlongTheStreet_RoundABend()
        {
            StreetPoint[] bent = Paved(Vector3.zero, new Vector3(30f, 0f, 0f), new Vector3(30f, 0f, 30f));

            Assert.IsTrue(SettlementMuster.TryChoose(Centre, new List<StreetPoint[]> { bent }, Inset, out Pose near));
            AssertNear(Centre + new Vector3(30f, 0f, 30f - Inset), near.position, "back along the last leg");

            Assert.IsTrue(SettlementMuster.TryChoose(Centre, new List<StreetPoint[]> { bent }, 35f, out Pose round));
            AssertNear(Centre + new Vector3(25f, 0f, 0f), round.position, "measured along the street, round its bend");
        }

        [Test]
        public void Spot_FacesOutAlongTheStreet_Level()
        {
            StreetPoint[] downhill = Paved(Vector3.zero, new Vector3(0f, -2f, -20f), new Vector3(0f, -6f, -40f));

            Assert.IsTrue(SettlementMuster.TryChoose(Centre, new List<StreetPoint[]> { downhill }, Inset, out Pose pose));
            AssertNear(Vector3.back, pose.forward, "+Z points out along the street, never down the slope");
            Assert.AreEqual(Centre.y - 5f, pose.position.y, Tolerance, "the height follows the street");
        }

        [Test]
        public void Tie_IsBrokenByBearing_WhateverTheOrder()
        {
            StreetPoint[] east = Paved(Vector3.zero, new Vector3(50f, 0f, 0f));
            StreetPoint[] west = Paved(Vector3.zero, new Vector3(-50f, 0f, 0f));

            Assert.IsTrue(SettlementMuster.TryChoose(Centre, new List<StreetPoint[]> { west, east }, Inset, out Pose first));
            Assert.IsTrue(SettlementMuster.TryChoose(Centre, new List<StreetPoint[]> { east, west }, Inset, out Pose second));
            AssertNear(first.position, second.position, "the same street either way round");
            AssertNear(Vector3.right, first.forward, "the smaller bearing from north (east, 90°) wins the tie");
        }

        [Test]
        public void StreetShorterThanTheInset_StandsAtItsStart()
        {
            Assert.IsTrue(SettlementMuster.TryChoose(Centre, new List<StreetPoint[]> { Paved(Vector3.zero, new Vector3(3f, 0f, 0f)) }, Inset, out Pose pose));
            AssertNear(Centre, pose.position, "clamped to the start");
            AssertNear(Vector3.right, pose.forward, "still facing out");
        }

        [Test]
        public void NoStreet_ChoosesNothing()
        {
            Assert.IsFalse(SettlementMuster.TryChoose(Centre, new List<StreetPoint[]>(), Inset, out _));
            Assert.IsFalse(SettlementMuster.TryChoose(Centre, new List<StreetPoint[]> { new[] { At(Vector3.zero, true) } }, Inset, out _),
                           "a single point has no direction to face");
        }

        [Test]
        public void TracedStreets_RunFromTheCentreOut_ThroughEveryPieceOfARow()
        {
            const float Link = 4f;
            var pieces = new List<StreetPoint> { At(new Vector3(80f, 0f, 0f), true) };   // a stray piece, out of reach
            for (int i = 0; i <= 12; i++) pieces.Add(At(new Vector3(i * 1.5f, 0f, 0f), true));          // east, paved to 18 m
            for (int i = 1; i <= 8; i++) pieces.Add(At(new Vector3(18f + i * 1.5f, 0f, 0f), false));    // then stones to 30 m
            for (int i = 1; i <= 10; i++) pieces.Add(At(new Vector3(0f, 0f, i * 1.5f), true));          // north, 15 m

            List<StreetPoint[]> streets = SettlementMuster.TraceStreets(Centre, pieces, Link);

            Assert.AreEqual(2, streets.Count, "one street per end");
            foreach (StreetPoint[] street in streets)
            {
                AssertNear(Centre, street[0].position, "every street starts at the piece nearest the centre");
                for (int i = 1; i < street.Length; i++)
                    Assert.LessOrEqual(Vector3.Distance(street[i - 1].position, street[i].position), 1.5f + Tolerance,
                                       "a street steps to the next piece of its row, never leaps over one (or it could miss the last slab)");
            }

            Assert.IsTrue(SettlementMuster.TryChoose(Centre, streets, Inset, out Pose pose));
            AssertNear(Centre + new Vector3(18f - Inset, 0f, 0f), pose.position, "inset from the last paved piece; the stray is no street end");
            AssertNear(Vector3.right, pose.forward, "out along the east street");
        }

        private static StreetPoint At(Vector3 fromCentre, bool paved) => new StreetPoint(Centre + fromCentre, paved);

        private static StreetPoint[] Paved(params Vector3[] fromCentre)
        {
            var points = new StreetPoint[fromCentre.Length];
            for (int i = 0; i < points.Length; i++) points[i] = At(fromCentre[i], true);
            return points;
        }

        private static void AssertNear(Vector3 expected, Vector3 actual, string message) =>
            Assert.LessOrEqual(Vector3.Distance(expected, actual), Tolerance, $"{message}: expected {expected}, got {actual}");
    }
}
