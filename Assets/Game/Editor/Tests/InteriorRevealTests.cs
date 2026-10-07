// When a hull's interior is drawn: inside it, or at an opening looking in — never from out on the
// sand. The bug these catch is the one that makes the feature pointless or broken: an interior that
// stays hidden while you stand in it, or one drawn for everybody everywhere.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class InteriorRevealTests
    {
        private static readonly Bounds[] Hold = { new Bounds(new Vector3(0f, 2f, 5f), new Vector3(4f, 3f, 9f)) };
        private static readonly List<Vector3> Hatch = new List<Vector3> { new Vector3(3f, 3f, 1f) };
        private const float Radius = 4f;

        [Test]
        public void StandingInTheHoldShowsIt()
        {
            Assert.IsTrue(InteriorReveal.ShouldReveal(new Vector3(0.5f, 2f, 6f), Hold, Hatch, Radius));
        }

        [Test]
        public void OutsideButAtTheHatchShowsIt()
        {
            Assert.IsTrue(InteriorReveal.ShouldReveal(new Vector3(5.5f, 3f, 1f), Hold, Hatch, Radius));
        }

        [Test]
        public void OutOnTheSandItStaysHidden()
        {
            Assert.IsFalse(InteriorReveal.ShouldReveal(new Vector3(20f, 1f, 5f), Hold, Hatch, Radius));
            Assert.IsFalse(InteriorReveal.ShouldReveal(Hatch[0] + Vector3.right * Radius, Hold, Hatch, Radius),
                           "exactly at the radius is outside it");
        }
    }
}
