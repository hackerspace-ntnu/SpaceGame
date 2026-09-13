// The rule of respawn: you come back inside your ship. In the story world that is the crew's one
// hull, found by its seat markers wherever it has been driven; in versus it is your TEAM's ship
// and never any other. These pin the resolver's three promises: it answers with the seat's
// authored standing pose, it spreads consecutive respawns across the seats, and in versus it
// refuses outright rather than hand back whichever hull a scene scan found first.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class ShipRespawnTests
    {
        private readonly System.Collections.Generic.List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            VersusSession.Clear();

            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
        }

        private GameObject NewPlayer()
        {
            var player = new GameObject("Player");
            spawned.Add(player);
            return player;
        }

        /// <summary>
        /// A hull with seat markers the way PlayerShipBuilder authors them: the marker itself on
        /// the chair's cushion, the dismount point on the deck beside it.
        /// </summary>
        private Transform BuildHull(params Vector3[] seatMarkers)
        {
            var hull = new GameObject("Hull");
            spawned.Add(hull);

            for (int i = 0; i < seatMarkers.Length; i++)
            {
                var marker = new GameObject("Seat" + i);
                marker.transform.SetParent(hull.transform, false);
                marker.transform.localPosition = seatMarkers[i];

                var dismount = new GameObject("DismountPoint");
                dismount.transform.SetParent(marker.transform, false);
                dismount.transform.localPosition = Vector3.back;   // off the cushion, onto the deck

                var seat = marker.AddComponent<ShipSeat>();
                var serialized = new SerializedObject(seat);
                serialized.FindProperty("order").intValue = i;
                serialized.FindProperty("dismountPoint").objectReferenceValue = dismount.transform;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return hull.transform;
        }

        [Test]
        public void StoryWorld_StandsYouUpAtASeatsDismountPoint()
        {
            Transform hull = BuildHull(new Vector3(0f, 1f, 2f), new Vector3(0f, 1f, 4f));

            Assert.IsTrue(ShipRespawn.TryGetPose(NewPlayer(), out Vector3 position, out _));

            bool atADismountPoint = false;
            foreach (ShipSeat seat in hull.GetComponentsInChildren<ShipSeat>())
                if (Vector3.Distance(position, seat.DismountPoint.position) < 0.001f)
                    atADismountPoint = true;

            Assert.IsTrue(atADismountPoint,
                "The answer must be a seat's authored standing pose — the marker itself is a " +
                "seated pivot on the cushion, and standing a body there puts it a metre up the chair.");
        }

        [Test]
        public void ConsecutiveRespawns_SpreadAcrossTheSeats()
        {
            BuildHull(new Vector3(0f, 1f, 2f), new Vector3(0f, 1f, 4f));

            Assert.IsTrue(ShipRespawn.TryGetPose(NewPlayer(), out Vector3 first, out _));
            Assert.IsTrue(ShipRespawn.TryGetPose(NewPlayer(), out Vector3 second, out _));

            Assert.Greater(Vector3.Distance(first, second), 0.001f,
                "Two players brought back in a row must not share one pose.");
        }

        [Test]
        public void ASeatWithNoDismountPoint_StillAnswersWithTheSeatItself()
        {
            Transform hull = BuildHull(new Vector3(0f, 1f, 2f));
            var seat = hull.GetComponentsInChildren<ShipSeat>()[0];

            Object.DestroyImmediate(seat.DismountPoint.gameObject);

            Assert.IsTrue(ShipRespawn.TryGetPose(NewPlayer(), out Vector3 position, out _));
            Assert.Less(Vector3.Distance(position, seat.transform.position), 0.001f,
                "A bare versus blockout hull has markers and nothing else — the marker is still " +
                "inside the ship, which is the rule being kept.");
        }

        [Test]
        public void NoShipAnywhere_IsARefusal()
        {
            Assert.IsFalse(ShipRespawn.TryGetPose(NewPlayer(), out _, out _),
                "With no hull in the scene the caller's spawn-point fallback must run instead — " +
                "an invented pose here would place a player relative to nothing.");
        }

        [Test]
        public void Versus_AnUnresolvedTeam_RefusesRatherThanTakeAnyShip()
        {
            BuildHull(new Vector3(0f, 1f, 2f), new Vector3(0f, 1f, 4f));
            VersusSession.Begin(teamCount: 2, teamSize: 2, localTeam: 0, teamColors: new[] { 0, 1 });

            Assert.IsFalse(ShipRespawn.TryGetPose(NewPlayer(), out _, out _),
                "In versus every hull on the ring carries seats, so a scene scan cannot tell your " +
                "ship from the enemy's. A player whose team cannot be resolved is refused — " +
                "respawning in ANY ship is the exact bug this rule exists to prevent.");
        }
    }
}
