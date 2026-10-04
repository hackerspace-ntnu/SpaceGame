using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Vehicles;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>The colony rovers' loop, with no scene: a closed, seeded, smooth curve driven by distance.</summary>
    public class RoverRouteTests
    {
        private static readonly Vector2 Centre = new(100f, -40f);

        private static RoverRoute Ring(int seed)
        {
            var points = new List<Vector2>();
            for (int slot = 0; slot < 8; slot++) points.Add(RoverRoute.Candidate(seed, slot, 8, 0, Centre, 40f, 90f));
            return new RoverRoute(points);
        }

        [Test]
        public void ACandidateIsTheSameOnEveryMachineAndStaysInItsRing()
        {
            for (int slot = 0; slot < 8; slot++)
            {
                Vector2 a = RoverRoute.Candidate(7, slot, 8, 0, Centre, 40f, 90f);
                Vector2 b = RoverRoute.Candidate(7, slot, 8, 0, Centre, 40f, 90f);
                Assert.AreEqual(a, b);
                Assert.That(Vector2.Distance(a, Centre), Is.InRange(40f - 0.01f, 90f + 0.01f));
            }
        }

        [Test]
        public void AnotherAlternativeOrSeedProposesAnotherPoint()
        {
            Assert.AreNotEqual(RoverRoute.Candidate(7, 2, 8, 0, Centre, 40f, 90f), RoverRoute.Candidate(7, 2, 8, 1, Centre, 40f, 90f));
            Assert.AreNotEqual(RoverRoute.Candidate(7, 2, 8, 0, Centre, 40f, 90f), RoverRoute.Candidate(8, 2, 8, 0, Centre, 40f, 90f));
        }

        [Test]
        public void TheLoopClosesOnItselfAndWrapsInBothDirections()
        {
            RoverRoute route = Ring(3);
            Assert.That(Vector2.Distance(route.PointAt(0f), route.PointAt(route.Length)), Is.LessThan(0.01f));
            Assert.That(Vector2.Distance(route.PointAt(12.5f), route.PointAt(12.5f + route.Length)), Is.LessThan(0.01f));
            Assert.That(Vector2.Distance(route.PointAt(-5f), route.PointAt(route.Length - 5f)), Is.LessThan(0.01f));
        }

        [Test]
        public void DrivingAMetreMovesAboutAMetreAlongTheHeading()
        {
            RoverRoute route = Ring(3);
            for (float at = 0f; at < route.Length; at += route.Length / 40f)
            {
                Vector2 step = route.PointAt(at + 1f) - route.PointAt(at);
                Assert.That(step.magnitude, Is.InRange(0.8f, 1.01f), $"1 m of driving moved {step.magnitude} m at {at}");
                Assert.That(Vector2.Dot(step.normalized, route.HeadingAt(at + 0.5f)), Is.GreaterThan(0.95f), "the heading disagrees with the motion");
            }
        }

        [Test]
        public void TheLoopIsSmoothWithNoSharpCorner()
        {
            RoverRoute route = Ring(3);
            IReadOnlyList<Vector2> samples = route.Samples;
            for (int i = 0; i < samples.Count; i++)
            {
                Vector2 before = samples[i] - samples[(i + samples.Count - 1) % samples.Count];
                Vector2 after = samples[(i + 1) % samples.Count] - samples[i];
                Assert.That(Vector2.Dot(before.normalized, after.normalized), Is.GreaterThan(0.7f), $"the loop turns sharply at sample {i}");
            }
        }

        [Test]
        public void TooFewPointsCannotMakeALoop()
        {
            Assert.Throws<System.ArgumentException>(() => new RoverRoute(new List<Vector2> { Vector2.zero, Vector2.one, Vector2.right }));
        }
    }
}
