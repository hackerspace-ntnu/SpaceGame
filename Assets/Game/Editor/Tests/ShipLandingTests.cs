// What "the ship landed" has to keep meaning.
//
// Every failure in this area is a silent one. A hull grounded against the single point under its
// own origin still lands, still looks landed in the editor from the front, and leaves a 23-metre
// ship hanging two metres over the low side of a slope — measured in the shipped world, terrain
// under the arrival hull's centre read 100.0 m while the terrain 11 m to starboard read 104.9 m.
// A body captured twice by two different carriers still gets "restored", to a state it was never
// in, and the player who walks away from that ship is kinematic and weightless with a clean
// console. Neither shows up as an error, and the arrival persists the wreck exactly where the
// descent left it, so neither ever fixes itself either.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types,
// and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Arrival;

namespace SpaceGame.EditorTools
{
    public class HullFootprintTests
    {
        private const float Tolerance = 0.001f;

        /// <summary>A hull the shape of PlayerShip's own hover footprint, rounded.</summary>
        private static readonly Vector2 Extents = new(11f, 15f);

        [Test]
        public void SamplesTheCentreEveryEdgeAndEveryCorner()
        {
            var points = new Vector2[HullFootprint.SampleCount];

            HullFootprint.Samples(Vector2.zero, 0f, Extents, points);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(11f, 0f), new Vector2(-11f, 0f),
                    new Vector2(0f, 15f), new Vector2(0f, -15f),
                    new Vector2(11f, 15f), new Vector2(11f, -15f),
                    new Vector2(-11f, 15f), new Vector2(-11f, -15f),
                },
                points,
                "The corners are not decoration: a hull turned across a slope touches it at a " +
                "corner first, and an edge-only ring measures the two sides that happen to be low.");
        }

        [Test]
        public void TurnsTheFootprintWithTheHull()
        {
            var points = new Vector2[HullFootprint.SampleCount];

            HullFootprint.Samples(Vector2.zero, 90f, Extents, points);

            // Yaw 90 swings the long axis onto +X, so the fore sample lands 15 m east.
            Assert.AreEqual(15f, points[3].x, Tolerance);
            Assert.AreEqual(0f, points[3].y, Tolerance);
        }

        [Test]
        public void ReportsTheHighestAndLowestGroundItSpans()
        {
            // A plane sloping 0.2 m per metre eastward — roughly what the shipped world does under
            // the arrival site.
            bool Slope(Vector2 at, out float y) { y = at.x * 0.2f; return true; }

            HullFootprint.Ground ground = HullFootprint.Measure(Vector2.zero, 0f, Extents, Slope);

            Assert.IsTrue(ground.Complete);
            Assert.AreEqual(2.2f, ground.Highest, Tolerance);
            Assert.AreEqual(-2.2f, ground.Lowest, Tolerance);
            Assert.AreEqual(4.4f, ground.Spread, Tolerance,
                            "A level hull rests on its high corner, so the spread IS how far the " +
                            "rest of it hangs.");
        }

        [Test]
        public void IsIncompleteWhenAnyOfTheFootprintCannotBeMeasured()
        {
            // The east half is an unstreamed chunk.
            bool HalfMissing(Vector2 at, out float y)
            {
                y = 0f;
                return at.x <= 0f;
            }

            HullFootprint.Ground ground = HullFootprint.Measure(Vector2.zero, 0f, Extents, HalfMissing);

            Assert.IsTrue(ground.Any);
            Assert.IsFalse(ground.Complete,
                           "Half a footprint is 'ask again', never 'land here' — the missing half " +
                           "is exactly where the ground might be higher than everything measured.");
        }
    }

    public class LevelGroundSearchTests
    {
        private static readonly Vector2 Extents = new(10f, 10f);

        [Test]
        public void StaysOnTheAuthoredPointWhenItIsFlatEnough()
        {
            bool Flat(Vector2 at, out float y) { y = 42f; return true; }

            bool found = LevelGroundSearch.TryFind(new Vector2(100f, 200f), 0f, Extents,
                                                   maxSpread: 1f, searchRadius: 60f, ringStep: 12f,
                                                   Flat, out Vector2 xz, out float y);

            Assert.IsTrue(found);
            Assert.AreEqual(new Vector2(100f, 200f), xz,
                            "The ring layout is where the arena wants its teams; a search that " +
                            "wandered off flat ground would move them for nothing.");
            Assert.AreEqual(42f, y, 0.001f);
        }

        [Test]
        public void MovesOffASlopeOntoTheShelfBesideIt()
        {
            // Everything west of x = 40 is a 1-in-2 slope; east of it is a flat shelf.
            bool Terrain(Vector2 at, out float y)
            {
                y = at.x < 40f ? at.x * 0.5f : 20f;
                return true;
            }

            bool found = LevelGroundSearch.TryFind(Vector2.zero, 0f, Extents,
                                                   maxSpread: 0.5f, searchRadius: 120f, ringStep: 20f,
                                                   Terrain, out Vector2 xz, out float y);

            Assert.IsTrue(found);
            Assert.GreaterOrEqual(xz.x, 50f, "It had to reach the shelf to find level ground.");
            Assert.AreEqual(20f, y, 0.001f);
        }

        [Test]
        public void SettlesForTheFlattestSpotWhenNowhereIsLevelEnough()
        {
            // A spire: steep everywhere, but flattening with distance. Nothing meets the tolerance.
            bool Spire(Vector2 at, out float y)
            {
                y = 100f / (1f + at.magnitude);
                return true;
            }

            bool found = LevelGroundSearch.TryFind(Vector2.zero, 0f, Extents,
                                                   maxSpread: 0.01f, searchRadius: 60f, ringStep: 20f,
                                                   Spire, out Vector2 xz, out float _);

            Assert.IsTrue(found, "A world of nothing but slope still has to open.");
            Assert.GreaterOrEqual(xz.magnitude, 40f,
                                  "Settling for the least bad ground beats a match that never starts.");
        }

        [Test]
        public void RefusesOnlyWhenNothingAnywhereCanBeMeasured()
        {
            bool Nothing(Vector2 at, out float y) { y = 0f; return false; }

            bool found = LevelGroundSearch.TryFind(Vector2.zero, 0f, Extents,
                                                   maxSpread: 1f, searchRadius: 60f, ringStep: 20f,
                                                   Nothing, out Vector2 _, out float _);

            Assert.IsFalse(found,
                           "In a streamed world this is 'the chunks have not arrived', which the " +
                           "caller retries. Answering with a guess is how ships end up buried.");
        }

        [Test]
        public void ReturnsTheSameSpotEveryTime()
        {
            // Two rings tie on spread, so only a fixed walk order decides which wins.
            bool Steps(Vector2 at, out float y)
            {
                y = Mathf.Round(at.x / 20f);
                return true;
            }

            LevelGroundSearch.TryFind(Vector2.zero, 0f, Extents, 0.01f, 80f, 20f,
                                      Steps, out Vector2 first, out float _);
            LevelGroundSearch.TryFind(Vector2.zero, 0f, Extents, 0.01f, 80f, 20f,
                                      Steps, out Vector2 second, out float _);

            Assert.AreEqual(first, second,
                            "The formation is rebuilt whenever a client asks again, and a search " +
                            "that answered differently would move a ship peers were already told " +
                            "about.");
        }
    }

    public class ArrivalLandingYawTests
    {
        [Test]
        public void LandingYawIsTheInverseOfTheBearingThatProducesIt()
        {
            foreach (float wantedYaw in new[] { 0f, 37f, 180f, 271f, 359f })
            foreach (float sweep in new[] { 110f, -110f, 0f })
            {
                float bearing = ArrivalFormation.BearingForLandingYaw(wantedYaw, sweep);
                float landed = ArrivalFormation.LandingYawForBearing(bearing, sweep);

                Assert.AreEqual(wantedYaw, landed, 0.001f,
                                $"yaw {wantedYaw} at sweep {sweep}: the wreck's footprint is " +
                                "measured at the attitude it comes to rest in, so a heading that " +
                                "does not round-trip grounds the hull against the wrong terrain.");
            }
        }
    }

    public class CarriedBodyTests
    {
        private GameObject body;
        private Rigidbody rb;

        /// <summary>Stand-ins for the two carriers — the identity is all CarriedBody uses.</summary>
        private readonly object seat = new();
        private readonly object mount = new();

        [SetUp]
        public void SetUp()
        {
            body = new GameObject("Body");
            rb = body.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        [TearDown]
        public void TearDown()
        {
            CarriedBody.Abandon(seat);
            CarriedBody.Abandon(mount);
            Object.DestroyImmediate(body);
        }

        [Test]
        public void FreezesTheBodyWhileItIsHeld()
        {
            CarriedBody.Hold(body, seat);

            Assert.IsTrue(rb.isKinematic);
            Assert.IsFalse(rb.useGravity);
            Assert.AreEqual(RigidbodyInterpolation.None, rb.interpolation,
                            "Interpolation renders a body from where physics had it a step ago, " +
                            "which is a long way back on a hull flying a descent.");
            Assert.IsTrue(CarriedBody.IsHeld(body));
        }

        [Test]
        public void HandsBackWhatTheBodyStartedWith()
        {
            CarriedBody.Hold(body, seat);
            CarriedBody.Release(body, seat);

            Assert.IsFalse(rb.isKinematic);
            Assert.IsTrue(rb.useGravity);
            Assert.AreEqual(RigidbodyInterpolation.Interpolate, rb.interpolation);
            Assert.IsFalse(CarriedBody.IsHeld(body));
        }

        [Test]
        public void ASecondCarrierDoesNotBankTheFirstOnesStateAsTheTruth()
        {
            // The bug, exactly: ride the arrival down in a seat, then take the helm of the same ship.
            CarriedBody.Hold(body, seat);
            CarriedBody.Hold(body, mount);

            // Get up out of the chair, then later dismount from the helm.
            CarriedBody.Release(body, seat);
            CarriedBody.Release(body, mount);

            Assert.IsFalse(rb.isKinematic,
                           "A player handed back a kinematic body cannot move, and nothing but a " +
                           "warning ever tells them why.");
            Assert.IsTrue(rb.useGravity,
                          "And a player handed back a weightless one has 'weird gravity' forever.");
        }

        [Test]
        public void StaysHeldWhileAnyCarrierStillHasIt()
        {
            CarriedBody.Hold(body, seat);
            CarriedBody.Hold(body, mount);

            CarriedBody.Release(body, mount);

            Assert.IsTrue(rb.isKinematic, "The seat has not let go.");
            Assert.IsTrue(CarriedBody.IsHeld(body));
        }

        [Test]
        public void RepeatedHoldsByOneCarrierAreOneHold()
        {
            CarriedBody.Hold(body, seat);
            CarriedBody.Hold(body, seat);

            CarriedBody.Release(body, seat);

            Assert.IsFalse(rb.isKinematic,
                           "A carrier that re-asserts its hold every frame — which the seat repair " +
                           "pass does — must not deepen it into one it can never undo.");
        }

        [Test]
        public void AbandoningClearsTheClaimWithoutTouchingTheBody()
        {
            CarriedBody.Hold(body, mount);

            CarriedBody.Abandon(mount);

            Assert.IsFalse(CarriedBody.IsHeld(body),
                           "A claim left standing after teardown means the body can never be " +
                           "handed back by anyone.");
            Assert.IsTrue(rb.isKinematic,
                          "Deliberately NOT restored: the holder gave up precisely because " +
                          "touching the body is unsafe.");
        }
    }
}
