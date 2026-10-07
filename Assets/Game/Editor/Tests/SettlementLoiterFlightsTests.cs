// Assets/Game/Editor/Tests/SettlementLoiterFlightsTests.cs
// The Sky City's swarm: who may go up and when, where its orbit sits (clear of every escort hull), how a flier
// comes back in to land (straight in, never a spiral among the houses), and how deck pads are picked.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.EditorTools;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Ornithopter;

namespace SpaceGame.Tests
{
    public class SettlementLoiterFlightsTests
    {
        private const float Tolerance = 0.01f;

        // Room an orbit leaves round an escort's station: its wander (FleetEscortModule ±14 m) plus a hull's
        // half-length and a craft's footprint.
        private const float EscortClearance = 40f;

        private readonly List<Object> junk = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private SerializedObject Defaults()
        {
            var go = new GameObject("Loiter");
            junk.Add(go);
            return new SerializedObject(go.AddComponent<SettlementLoiterFlights>());
        }

        [Test]
        public void ALaunch_NeedsAMooredCityWithTimeInHand_RoomAloft_AndSomeoneWatching()
        {
            Assert.IsTrue(LoiterRules.MayLaunch(false, 240f, 210f, 2, 3, true));
            Assert.IsFalse(LoiterRules.MayLaunch(true, 240f, 210f, 0, 3, true), "a launch under way");
            Assert.IsFalse(LoiterRules.MayLaunch(false, 100f, 210f, 0, 3, true), "a launch with no time left to land before departure");
            Assert.IsFalse(LoiterRules.MayLaunch(false, 240f, 210f, 3, 3, true), "a fourth flier");
            Assert.IsFalse(LoiterRules.MayLaunch(false, 240f, 210f, 0, 3, false), "a launch with nobody near");
        }

        [Test]
        public void AnyWithin_IsFlat_AndSkipsMissingPlayers()
        {
            var near = new GameObject("Near");
            junk.Add(near);
            near.transform.position = new Vector3(0f, 500f, 800f);
            var players = new List<Transform> { null, near.transform };

            Assert.IsTrue(LoiterRules.AnyWithin(Vector3.zero, players, 900f));
            Assert.IsFalse(LoiterRules.AnyWithin(Vector3.zero, players, 700f));
        }

        [Test]
        public void TheOrbitRate_CarriesAFlierRoundAtItsSpeed()
        {
            float degreesPerSecond = LoiterRules.OrbitDegreesPerSecond(10f, 100f);
            Assert.AreEqual(0.1f * Mathf.Rad2Deg, degreesPerSecond, Tolerance);
            Assert.AreEqual(0f, LoiterRules.OrbitDegreesPerSecond(10f, 0f));
        }

        [Test]
        public void TheApproach_StartsOutsideAndAboveThePad_SteepEnoughToGoStraightIn()
        {
            SerializedObject so = Defaults();
            float distance = so.FindProperty("approachFixDistance").floatValue;
            float height = so.FindProperty("approachFixHeight").floatValue;
            var flight = new NpcFlightSettings();

            Vector3 pad = new Vector3(30f, 2f, 10f);
            Vector3 fix = LoiterRules.ApproachFix(pad, Vector3.zero, distance, height);
            Vector3 flat = fix - pad;
            flat.y = 0f;

            Assert.AreEqual(distance, flat.magnitude, Tolerance);
            Assert.Greater(Vector3.Dot(flat, new Vector3(pad.x, 0f, pad.z)), 0f, "the approach starts over the city, not outside it");
            float slope = Mathf.Atan2(height, distance) * Mathf.Rad2Deg;
            Assert.Greater(slope, flight.ApproachSlope, "the craft would cruise on toward the pad before turning in, sinking over the deck");
            Assert.Less(slope, flight.MaxApproachSlope, "the craft would spiral down onto the deck among the houses");
        }

        [Test]
        public void TheOrbit_StaysClearOfEveryEscortHull()
        {
            SerializedObject so = Defaults();
            Vector2 radius = so.FindProperty("orbitRadius").vector2Value;
            float height = so.FindProperty("orbitHeight").floatValue;
            float jitter = so.FindProperty("orbitHeightJitter").floatValue;

            foreach (SkyFleetPlacement.EscortSlot escort in SkyFleetPlacement.Escorts)
            {
                float outward = new Vector2(escort.Station.x, escort.Station.z).magnitude;
                foreach (float r in new[] { radius.x, radius.y })
                foreach (float h in new[] { height - jitter, height + jitter })
                {
                    float gap = new Vector2(outward - r, escort.Station.y - h).magnitude;
                    Assert.Greater(gap, EscortClearance,
                                   $"an orbit of {r} m at {h} m passes {gap:0} m from the {escort.Vessel} at {escort.Station}");
                }
            }
        }

        [Test]
        public void Pads_AreThoseTheRuleAccepts_SpacedApart_UpToTheCount()
        {
            var candidates = new List<Vector3>
            {
                new(0f, 0f, 0f), new(5f, 0f, 0f), new(40f, 0f, 0f), new(80f, 0f, 0f), new(120f, 0f, 0f), new(160f, 0f, 0f),
            };

            List<Vector3> pads = LoiterRules.PickPads(candidates, 3, 25f, point => point.x != 80f);

            CollectionAssert.AreEqual(new[] { new Vector3(0f, 0f, 0f), new Vector3(40f, 0f, 0f), new Vector3(120f, 0f, 0f) }, pads);
        }
    }
}
