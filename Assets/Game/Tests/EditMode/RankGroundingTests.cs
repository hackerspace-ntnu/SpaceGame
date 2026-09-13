using NUnit.Framework;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Standing the rank on the sand rather than on the anchor's plane.
    ///
    /// The probe arrives as a delegate so all of this runs with no scene, no colliders and no
    /// physics — the same trick <c>LobbyJoinRecovery</c> uses to test a service call without the
    /// service.
    /// </summary>
    public class RankGroundingTests
    {
        private static Vector3[] Line(int count)
        {
            var seats = new Vector3[count];
            for (int i = 0; i < count; i++) seats[i] = new Vector3(i, 0f, 0f);
            return seats;
        }

        [Test]
        public void EverySeatIsPutOnTheGroundTheProbeFound()
        {
            GroundedRank rank = RankGrounding.Solve(Line(3), fallbackY: 0f,
                                                    (Vector3 seat, out float y) =>
                                                    {
                                                        y = seat.x * 0.5f;
                                                        return true;
                                                    });

            Assert.AreEqual(0f, rank.Positions[0].y, 0.0001f);
            Assert.AreEqual(0.5f, rank.Positions[1].y, 0.0001f);
            Assert.AreEqual(1f, rank.Positions[2].y, 0.0001f);
        }

        /// <summary>
        /// A scene with no ground under a seat must look exactly like the rank did before it was
        /// ever grounded, rather than dropping that astronaut to zero.
        /// </summary>
        [Test]
        public void ASeatWithNoGroundUnderItFallsBackToTheAnchorPlane()
        {
            GroundedRank rank = RankGrounding.Solve(Line(2), fallbackY: 3.196f,
                                                    (Vector3 seat, out float y) =>
                                                    {
                                                        y = 999f;
                                                        return false;
                                                    });

            Assert.AreEqual(3.196f, rank.Positions[0].y, 0.0001f);
            Assert.AreEqual(3.196f, rank.Positions[1].y, 0.0001f);
        }

        [Test]
        public void TheHeightSpreadCoversEverySeat()
        {
            GroundedRank rank = RankGrounding.Solve(Line(3), fallbackY: 0f,
                                                    (Vector3 seat, out float y) =>
                                                    {
                                                        y = Mathf.Approximately(seat.x, 1f) ? 5f : 1f;
                                                        return true;
                                                    });

            Assert.AreEqual(1f, rank.MinY, 0.0001f);
            Assert.AreEqual(5f, rank.MaxY, 0.0001f);
            Assert.AreEqual(4f, rank.HeightSpread, 0.0001f);
        }

        [Test]
        public void TheSeatsOwnXAndZAreLeftAlone()
        {
            GroundedRank rank = RankGrounding.Solve(new[] { new Vector3(2f, 0f, 7f) }, fallbackY: 0f,
                                                    (Vector3 seat, out float y) =>
                                                    {
                                                        y = 1f;
                                                        return true;
                                                    });

            Assert.AreEqual(2f, rank.Positions[0].x, 0.0001f);
            Assert.AreEqual(7f, rank.Positions[0].z, 0.0001f);
        }

        [Test]
        public void AnEmptyRankReportsNoSpreadRatherThanThrowing()
        {
            GroundedRank rank = RankGrounding.Solve(System.Array.Empty<Vector3>(), fallbackY: 2f,
                                                    (Vector3 seat, out float y) =>
                                                    {
                                                        y = 0f;
                                                        return true;
                                                    });

            Assert.AreEqual(0, rank.Positions.Length);
            Assert.AreEqual(0f, rank.HeightSpread, 0.0001f);
            Assert.AreEqual(2f, rank.MinY, 0.0001f);
        }

        /// <summary>
        /// A caller with no probe to offer gets the flat rank rather than an exception — the same
        /// degrade-do-not-throw rule the rest of the lobby follows.
        /// </summary>
        [Test]
        public void ARankWithNoProbeAtAllStillAnswers()
        {
            GroundedRank rank = RankGrounding.Solve(Line(2), fallbackY: 4f, probe: null);

            Assert.AreEqual(0, rank.Positions.Length);
            Assert.AreEqual(4f, rank.MinY, 0.0001f);
        }
    }
}
