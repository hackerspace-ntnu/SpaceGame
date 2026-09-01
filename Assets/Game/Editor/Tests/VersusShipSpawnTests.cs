// Where the team ships start, asked without a scene.
//
// The three questions worth asking here are the ones whose wrong answers are quiet. A yaw with the
// wrong sign ships every hull facing out of the arena instead of into it, and looks like a level
// design opinion rather than a bug. An explicit layout missing a team leaves one side of a versus
// match with no ship, which nobody notices until that side tries to spawn. And a runtime override
// that loses to the asset means the whole "definable at runtime" promise silently does nothing.
//
// Grounding and ship spawning are deliberately absent: both are Unity-API-bound, and the question
// worth asking about them — does the ship land on the sand — is answered by playing the mode.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class VersusShipSpawnTests
    {
        [SetUp]
        public void SetUp() => VersusShipSpawns.Clear();

        [TearDown]
        public void TearDown()
        {
            VersusShipSpawns.Clear();
            VersusTeamRoster.Clear();
        }

        // ─────────────────────────────────────────────
        //  ShipSpawnLayout
        // ─────────────────────────────────────────────

        /// <summary>
        /// The ring is only worth having if it is actually symmetric — that symmetry is what makes
        /// it fair without anybody playtesting it. Both halves are checked: every team the same
        /// distance out, and every neighbour the same distance apart.
        /// </summary>
        [Test]
        public void Ring_SpacesTeamsEvenlyAroundTheCentre()
        {
            var center = new Vector2(40f, -15f);
            ShipSpawnPoint[] points = ShipSpawnLayout.Ring(center, 100f, 4);

            Assert.AreEqual(4, points.Length);

            foreach (ShipSpawnPoint point in points)
                Assert.AreEqual(100f, Vector2.Distance(center, point.GroundXZ), 0.001f,
                                "every team should start the same distance from the centre.");

            float firstGap = Vector2.Distance(points[0].GroundXZ, points[1].GroundXZ);

            for (int team = 1; team < points.Length; team++)
            {
                float gap = Vector2.Distance(points[team].GroundXZ,
                                             points[(team + 1) % points.Length].GroundXZ);

                Assert.AreEqual(firstGap, gap, 0.001f, "neighbouring teams should be equally spaced.");
            }
        }

        /// <summary>
        /// The one that catches a sign error in the yaw. Ships pointing out of the arena is a
        /// mistake that reads as an aesthetic choice, so it is asserted rather than eyeballed.
        /// </summary>
        [Test]
        public void Ring_PointsEveryShipAtTheCentre()
        {
            var center = new Vector2(-20f, 60f);
            ShipSpawnPoint[] points = ShipSpawnLayout.Ring(center, 75f, 3);

            foreach (ShipSpawnPoint point in points)
            {
                float radians = point.Yaw * Mathf.Deg2Rad;

                // Unity's yaw of zero looks down +Z, so the heading is (sin, cos) — the same
                // convention the layout builds the offsets on.
                var facing = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
                Vector2 toCentre = (center - point.GroundXZ).normalized;

                Assert.AreEqual(1f, Vector2.Dot(facing, toCentre), 0.001f,
                                $"{VersusRules.TeamName(point.Team)} should face the centre.");
            }
        }

        /// <summary>
        /// A team with no point, and a team with two. Both are silent in a way that only shows up
        /// at spawn time — one side with no ship, or a row in the asset that looks authoritative
        /// and is quietly losing to another.
        /// </summary>
        [Test]
        public void ValidateExplicit_RefusesMissingAndDuplicateTeams()
        {
            var missing = new List<ShipSpawnPoint> { new(0, Vector2.zero, 0f) };

            Assert.IsFalse(ShipSpawnLayout.TryValidateExplicit(missing, 2, out _, out string refusal));
            StringAssert.Contains(VersusRules.TeamName(1), refusal,
                                  "the refusal should name the team that has no point.");

            var duplicated = new List<ShipSpawnPoint>
            {
                new(0, Vector2.zero, 0f),
                new(0, Vector2.one, 90f),
                new(1, Vector2.one * 2f, 180f),
            };

            Assert.IsFalse(ShipSpawnLayout.TryValidateExplicit(duplicated, 2, out _, out refusal));
            StringAssert.Contains(VersusRules.TeamName(0), refusal,
                                  "the refusal should name the team that has two points.");
        }

        /// <summary>
        /// An asset authored for the biggest match the arena supports has to still run a smaller
        /// one, or every arena needs an asset per team count.
        /// </summary>
        [Test]
        public void ValidateExplicit_IgnoresRowsForTeamsTheMatchDoesNotHave()
        {
            var points = new List<ShipSpawnPoint>
            {
                new(0, new Vector2(10f, 0f), 0f),
                new(1, new Vector2(-10f, 0f), 180f),
                new(2, new Vector2(0f, 10f), 270f),
            };

            Assert.IsTrue(ShipSpawnLayout.TryValidateExplicit(points, 2,
                                                              out ShipSpawnPoint[] ordered, out _));
            Assert.AreEqual(2, ordered.Length);
            Assert.AreEqual(new Vector2(10f, 0f), ordered[0].GroundXZ);
            Assert.AreEqual(new Vector2(-10f, 0f), ordered[1].GroundXZ);
        }

        /// <summary>Seats have to sit inside the hull they are meant to be seats in.</summary>
        [Test]
        public void SeatRing_KeepsEverySeatOnTheAuthoredRadius()
        {
            var interior = new Vector3(0f, 1.2f, 0f);
            Vector3[] seats = ShipSpawnLayout.SeatRing(4, 1.6f, interior);

            Assert.AreEqual(4, seats.Length);

            foreach (Vector3 seat in seats)
            {
                Assert.AreEqual(1.6f, Vector2.Distance(new Vector2(interior.x, interior.z),
                                                       new Vector2(seat.x, seat.z)), 0.001f);
                Assert.AreEqual(interior.y, seat.y, 0.001f, "seats should share the deck height.");
            }

            // A ring of one is an arbitrary shove off centre, so a lone occupant goes on the offset.
            Assert.AreEqual(new[] { interior }, ShipSpawnLayout.SeatRing(1, 1.6f, interior));
        }

        // ─────────────────────────────────────────────
        //  VersusShipSpawns
        // ─────────────────────────────────────────────

        /// <summary>
        /// The whole point of the override layer. If the asset wins, "definable at runtime" is a
        /// promise the system does not keep, and nothing throws to say so.
        /// </summary>
        [Test]
        public void Override_WinsOverTheAssetAndClearRestoresIt()
        {
            // Left at its authored defaults: Ring layout, 120 m radius, centred on the origin.
            var config = ScriptableObject.CreateInstance<VersusShipSpawnConfig>();

            try
            {
                Assert.IsTrue(VersusShipSpawns.TryResolve(config, 2,
                                                          out IReadOnlyList<ShipSpawnPoint> authored, out _));
                Assert.AreEqual(120f, authored[0].GroundXZ.magnitude, 0.001f);

                VersusShipSpawns.UseRing(Vector2.zero, 40f);

                Assert.IsTrue(VersusShipSpawns.HasOverride);
                Assert.IsTrue(VersusShipSpawns.TryResolve(config, 2,
                                                          out IReadOnlyList<ShipSpawnPoint> overridden, out _));
                Assert.AreEqual(40f, overridden[0].GroundXZ.magnitude, 0.001f,
                                "the runtime ring should win over the asset's.");

                VersusShipSpawns.Clear();

                Assert.IsFalse(VersusShipSpawns.HasOverride);
                Assert.IsTrue(VersusShipSpawns.TryResolve(config, 2,
                                                          out IReadOnlyList<ShipSpawnPoint> restored, out _));
                Assert.AreEqual(120f, restored[0].GroundXZ.magnitude, 0.001f,
                                "clearing the override should hand the asset back.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        /// <summary>
        /// A copy, not an alias. The caller is tooling that keeps editing its own list, and a later
        /// edit reaching back into a match already loaded is the bug VersusSession.Begin documents.
        /// </summary>
        [Test]
        public void UseExplicit_CopiesThePointsItIsGiven()
        {
            var points = new List<ShipSpawnPoint>
            {
                new(0, new Vector2(5f, 0f), 0f),
                new(1, new Vector2(-5f, 0f), 180f),
            };

            VersusShipSpawns.UseExplicit(points);
            points[0] = new ShipSpawnPoint(0, new Vector2(999f, 999f), 0f);

            Assert.IsTrue(VersusShipSpawns.TryResolve(null, 2,
                                                      out IReadOnlyList<ShipSpawnPoint> resolved, out _));
            Assert.AreEqual(new Vector2(5f, 0f), resolved[0].GroundXZ,
                            "editing the caller's list should not change the layout already set.");
        }

        // ─────────────────────────────────────────────
        //  VersusTeamRoster
        // ─────────────────────────────────────────────

        /// <summary>
        /// Even sides, and the same answer every time for a given client. The second half is what
        /// stops a reconnecting player — who comes back on the same client id, since Netcode hands
        /// out the lowest free one — from being spawned inside the enemy's ship.
        /// </summary>
        [Test]
        public void Roster_FillsTeamsEvenlyAndKeepsAClientOnItsTeam()
        {
            Assert.AreEqual(0, VersusTeamRoster.Assign(10ul, 2));
            Assert.AreEqual(1, VersusTeamRoster.Assign(11ul, 2));
            Assert.AreEqual(0, VersusTeamRoster.Assign(12ul, 2));
            Assert.AreEqual(1, VersusTeamRoster.Assign(13ul, 2));

            Assert.AreEqual(0, VersusTeamRoster.Assign(10ul, 2),
                            "asking again should not move a player to the other side.");
        }

        /// <summary>
        /// The rule that makes this a versus match rather than a shuffle: a side someone picked in
        /// the lobby outranks the balancer. Without it a party that queued together gets split up,
        /// silently, and lands in opposing ships.
        /// </summary>
        [Test]
        public void Roster_AClaimedTeamBeatsTheBalancer()
        {
            // Left to itself the balancer would fill 0, 1, 0 — so every one of these is a team the
            // balancer would NOT have chosen, which is what makes the assertion mean something.
            VersusTeamRoster.Claim(30ul, 1, 2);
            VersusTeamRoster.Claim(31ul, 1, 2);
            VersusTeamRoster.Claim(32ul, 1, 2);

            Assert.AreEqual(1, VersusTeamRoster.Assign(30ul, 2));
            Assert.AreEqual(1, VersusTeamRoster.Assign(31ul, 2));
            Assert.AreEqual(1, VersusTeamRoster.Assign(32ul, 2),
                            "three players who all chose team 1 should all be on team 1.");
        }

        /// <summary>
        /// A team index the match does not have is dropped rather than stored — it would index a
        /// real per-team array later. The client is balanced instead of refused, because a player
        /// with no spawn is worse than a player on the wrong side.
        /// </summary>
        [Test]
        public void Roster_RefusesAClaimOutsideTheMatchesTeams()
        {
            VersusTeamRoster.Claim(40ul, 7, 2);
            VersusTeamRoster.Claim(41ul, -1, 2);

            Assert.IsFalse(VersusTeamRoster.TryGet(40ul, out _), "team 7 does not exist in a 2-team match.");
            Assert.IsFalse(VersusTeamRoster.TryGet(41ul, out _), "a negative team is not a team.");

            Assert.AreEqual(0, VersusTeamRoster.Assign(40ul, 2), "they should still be given a side.");
        }

        /// <summary>
        /// Filling the emptiest team rather than counting arrivals is what keeps the sides even
        /// across a session people leave. Counting would put this player on team 1 as well.
        /// </summary>
        [Test]
        public void Roster_ReleasingAClientFreesItsPlace()
        {
            VersusTeamRoster.Assign(20ul, 2);   // team 0
            VersusTeamRoster.Assign(21ul, 2);   // team 1
            VersusTeamRoster.Release(20ul);

            Assert.IsFalse(VersusTeamRoster.TryGet(20ul, out _));
            Assert.AreEqual(0, VersusTeamRoster.Assign(22ul, 2),
                            "the freed place on team 0 should be the emptiest one.");
        }
    }
}
