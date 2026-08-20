// The plane a machine's feet span, and the tilt a body takes from it.
//
// This is the arithmetic behind the cross-slope fault: a body that holds itself dead level puts
// every hip at one height while its feet are at several, so the downhill legs must span the ride
// height plus the whole drop of the slope. Measured on the six-legged machine, a level deck reached
// 2.1x its own leg length at a 30-degree cross-slope and ended up standing on two feet with four
// detached. Following even part of the slope moves each hip toward its own foot.
using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Tests
{
    public class WalkerSupportPlaneTests
    {
        /// Four feet on level ground, at the corners of a square about the origin.
        private static Vector3[] Square(float y0, float y1, float y2, float y3) => new[]
        {
            new Vector3(-1f, y0, 1f),
            new Vector3(1f, y1, 1f),
            new Vector3(-1f, y2, -1f),
            new Vector3(1f, y3, -1f),
        };

        [Test]
        public void LevelFeetGiveALevelPlane()
        {
            Assert.IsTrue(WalkerSupportPlane.TryFit(Square(2f, 2f, 2f, 2f), 4, out WalkerSupportPlane p));

            Assert.IsTrue(p.Valid);
            Assert.AreEqual(1f, Vector3.Dot(p.Normal, Vector3.up), 1e-4f);
            Assert.AreEqual(2f, p.Height, 1e-4f, "the plane sits at the feet's own height");
        }

        /// The fault's own geometry: the right side high, the left side low.
        [Test]
        public void ACrossSlopeComesBackAsRoll()
        {
            // +x side is 1 unit higher than -x side, across 2 units: a 26.6-degree roll.
            Assert.IsTrue(WalkerSupportPlane.TryFit(Square(0f, 1f, 0f, 1f), 4, out WalkerSupportPlane p));

            Assert.IsTrue(p.Valid);
            Assert.Less(p.Normal.x, 0f, "a plane rising toward +x leans its normal toward -x");
            Assert.AreEqual(0f, p.Normal.z, 1e-4f, "there is no pitch in a pure cross-slope");
            Assert.AreEqual(Mathf.Atan2(1f, 2f) * Mathf.Rad2Deg,
                            Vector3.Angle(p.Normal, Vector3.up), 0.1f);
            Assert.AreEqual(0.5f, p.Height, 1e-4f, "under the body, halfway up the slope");
        }

        [Test]
        public void AForeAftSlopeComesBackAsPitch()
        {
            Assert.IsTrue(WalkerSupportPlane.TryFit(Square(1f, 1f, 0f, 0f), 4, out WalkerSupportPlane p));

            Assert.Less(p.Normal.z, 0f, "a plane rising toward +z leans its normal toward -z");
            Assert.AreEqual(0f, p.Normal.x, 1e-4f, "there is no roll in a pure fore-aft slope");
        }

        [Test]
        public void HeightIsThePlaneUnderTheBodyNotTheMeanOfTheFeet()
        {
            // Three feet, deliberately lopsided: two low on one side, one high on the other. The mean
            // of the heights is 1/3; the plane under the origin is 1/2.
            var feet = new[]
            {
                new Vector3(-1f, 0f, 1f),
                new Vector3(-1f, 0f, -1f),
                new Vector3(1f, 1f, 0f),
            };

            Assert.IsTrue(WalkerSupportPlane.TryFit(feet, 3, out WalkerSupportPlane p));
            Assert.AreEqual(0.5f, p.Height, 1e-4f);
        }

        // ─────────── when there is no plane to have ───────────

        [Test]
        public void FewerThanThreeFeetSpanNoPlane()
        {
            Assert.IsFalse(WalkerSupportPlane.TryFit(Square(0f, 1f, 0f, 1f), 2, out WalkerSupportPlane p));
            Assert.IsFalse(p.Valid);
            Assert.AreEqual(Vector3.up, p.Normal, "an unfittable plane must still read as level");
        }

        /// A biped. Two feet have no roll to measure and inventing one would be noise -- which is why
        /// the ostrich is untouched by any of this.
        [Test]
        public void CollinearFeetSpanNoPlane()
        {
            var inARow = new[]
            {
                new Vector3(-1f, 0f, 0f),
                new Vector3(0f, 0.5f, 0f),
                new Vector3(1f, 1f, 0f),
            };

            Assert.IsFalse(WalkerSupportPlane.TryFit(inARow, 3, out WalkerSupportPlane p));
            Assert.IsFalse(p.Valid);
        }

        [Test]
        public void NullOrOverlongCountsAreRefused()
        {
            Assert.IsFalse(WalkerSupportPlane.TryFit(null, 4, out _));
            Assert.IsFalse(WalkerSupportPlane.TryFit(Square(0f, 0f, 0f, 0f), 9, out _));
        }

        // ─────────── the tilt a body takes from it ───────────

        [Test]
        public void FollowScalesTheTilt()
        {
            WalkerSupportPlane.TryFit(Square(0f, 1f, 0f, 1f), 4, out WalkerSupportPlane p);
            float slope = Vector3.Angle(p.Normal, Vector3.up);

            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, p.Tilt(0f, 90f)), 1e-3f,
                            "follow 0 is a deck pinned level -- the behaviour that stranded the legs");
            Assert.AreEqual(slope, Quaternion.Angle(Quaternion.identity, p.Tilt(1f, 90f)), 0.1f,
                            "follow 1 lies flat on the slope");
            Assert.AreEqual(slope * 0.5f, Quaternion.Angle(Quaternion.identity, p.Tilt(0.5f, 90f)), 0.5f);
        }

        [Test]
        public void MaxTiltCapsIt()
        {
            WalkerSupportPlane.TryFit(Square(0f, 4f, 0f, 4f), 4, out WalkerSupportPlane p);

            Assert.Greater(Vector3.Angle(p.Normal, Vector3.up), 30f, "the test slope should be steep");
            Assert.AreEqual(12f, Quaternion.Angle(Quaternion.identity, p.Tilt(1f, 12f)), 0.1f,
                            "however steep the ground, the deck stays walkable");
        }

        [Test]
        public void AnInvalidPlaneNeverTilts()
        {
            WalkerSupportPlane level = WalkerSupportPlane.Level;
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, level.Tilt(1f, 45f)), 1e-4f);
        }

        [Test]
        public void TiltTurnsTheBodyTowardThePlanesNormal()
        {
            WalkerSupportPlane.TryFit(Square(0f, 1f, 0f, 1f), 4, out WalkerSupportPlane p);

            Vector3 up = p.Tilt(1f, 90f) * Vector3.up;
            Assert.AreEqual(1f, Vector3.Dot(up, p.Normal), 1e-3f,
                            "at full follow the body's up should be the plane's normal");
        }
    }
}
