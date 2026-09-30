// ScoutRota on real components: only the house the column follows sends scouts, an outing turns the
// scout's formation off through the runtime `active` switch (never `enabled`, which
// MonowheelDriverGate and MountModule own) and drives it by AgentGoal round a closed loop, a scout
// riding back still counts as away, and every way an outing ends gives the scout back.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Monowheel;

namespace SpaceGame.EditorTools
{
    public class ScoutRotaTests
    {
        private const string City = "scout-rota-test";
        private static readonly Vector3 FarOut = new Vector3(1000f, 0f, 0f);
        private readonly List<Object> junk = new();

        [TearDown]
        public void TearDown() { foreach (var o in junk) if (o != null) Object.DestroyImmediate(o); junk.Clear(); }

        private ScoutRota House(bool leader)
        {
            var go = new GameObject(leader ? "LeadHouse" : "House");
            junk.Add(go);
            go.AddComponent<FormationModule>().SetFormation(City, leader);
            var rota = go.AddComponent<ScoutRota>();
            rota.SetGroundCheck(_ => true);   // no world streamer here: all ground is in
            return rota;
        }

        private (FormationModule formation, GameObject rider) Scout(string name)
        {
            var go = new GameObject(name);
            junk.Add(go);
            go.AddComponent<MonowheelMotor>();
            var formation = go.AddComponent<FormationModule>();
            formation.SetFormation(City, false);
            var rider = new GameObject(name + "Rider");
            junk.Add(rider);
            go.AddComponent<NpcPassenger>().Seat(rider);
            Assume.That(go.GetComponent<NpcPassenger>().HasRider, "edit mode must seat a rider");
            return (formation, rider);
        }

        private static AgentGoal Goal(FormationModule scout) => scout.GetComponent<AgentGoal>();

        private static bool ActiveBit(FormationModule module) => new SerializedObject(module).FindProperty("active").boolValue;

        private static bool OnSweep(FormationModule scout) =>
            !ActiveBit(scout) && Goal(scout) != null && Goal(scout).HasGoal && Goal(scout).Reason == ScoutRota.SweepReason;

        private static void AssertFormationBack(FormationModule scout)
        {
            Assert.IsTrue(ActiveBit(scout), $"{scout.name}'s formation switch is back on");
            Assert.IsFalse(Goal(scout) != null && Goal(scout).HasGoal, $"{scout.name}'s sweep goal is cleared");
        }

        private static void AssertHome(FormationModule scout)
        {
            AssertFormationBack(scout);
            Assert.IsTrue(scout.enabled, "the rota never touches `enabled`");
        }

        [Test]
        public void TheLeader_SendsTwo_OffTheirSlots_OntoASweep_AtTheWheelsOwnGoalSpeed()
        {
            ScoutRota rota = House(leader: true);
            var (a, _) = Scout("A"); var (b, _) = Scout("B"); var (c, _) = Scout("C");

            rota.Tick(0f);

            Assert.AreEqual(2, rota.OutCount);
            Assert.IsTrue(OnSweep(a) && OnSweep(b), "the first two listed go when nobody has been home longer");
            Assert.IsTrue(a.enabled && b.enabled, "the rota switches the module off, never the component");
            AssertHome(c);

            Assert.AreEqual(1f, Goal(a).SpeedMultiplier, 0.001f,
                "the wheel's GoalTravelModule already rides goals at cruise; a second multiplier would crawl");
        }

        [Test]
        public void ASweep_KeepsToStreamedGround_PullingItsWaypointsIn()
        {
            ScoutRota rota = House(leader: true);
            rota.SetGroundCheck(p => FlatFrom(rota, p) <= 250f);
            var (a, _) = Scout("A");

            rota.Tick(0f);

            Assume.That(OnSweep(a));
            Assert.LessOrEqual(FlatFrom(rota, Goal(a).Position), 250f,
                "a waypoint past the loaded chunks drops the wheel through unloaded ground");
        }

        [Test]
        public void WithNoStreamedGroundOnTheRing_NobodyIsSent_AndASweepEnds()
        {
            ScoutRota rota = House(leader: true);
            var (a, _) = Scout("A");
            rota.Tick(0f);
            Assume.That(OnSweep(a));

            rota.SetGroundCheck(_ => false);
            Goal(a).Set(a.transform.position, 1f, ScoutRota.SweepReason);   // arrived at its waypoint
            rota.Tick(0.5f);

            Assert.IsFalse(rota.IsSweeping(a), "with no waypoint left to reach the sweep ends and the scout rides home");
            AssertFormationBack(a);

            var (b, _) = Scout("B");
            rota.Tick(5f);
            Assert.IsFalse(rota.IsOut(b), "a scout is not sent where no ground has streamed in");
        }

        private static float FlatFrom(ScoutRota rota, Vector3 point)
        {
            Vector3 offset = point - rota.transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        [Test]
        public void AHouseThatDoesNotLead_SendsNobody()
        {
            House(leader: true);
            ScoutRota follower = House(leader: false);
            var (a, _) = Scout("A");

            follower.Tick(0f);

            Assert.AreEqual(0, follower.OutCount);
            AssertHome(a);
        }

        [Test]
        public void LosingTheLead_CallsEveryoneHome()
        {
            ScoutRota first = House(leader: true);
            var (a, _) = Scout("A"); var (b, _) = Scout("B");
            first.Tick(0f);
            Assume.That(first.OutCount, Is.EqualTo(2));

            House(leader: true);
            first.GetComponent<FormationModule>().SetFormation(City, false);
            first.Tick(1f);

            Assert.AreEqual(0, first.OutCount);
            AssertHome(a);
            AssertHome(b);
        }

        [Test]
        public void SwitchingTheHouseOff_CallsEveryoneHome()
        {
            ScoutRota rota = House(leader: true);
            var (a, _) = Scout("A"); var (b, _) = Scout("B");
            rota.Tick(0f);
            Assume.That(rota.OutCount, Is.EqualTo(2));

            // Edit mode does not run OnDisable for a plain MonoBehaviour, so raise it as Unity would.
            rota.enabled = false;
            typeof(ScoutRota).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(rota, null);

            Assert.AreEqual(0, rota.OutCount);
            AssertHome(a);
            AssertHome(b);
        }

        [Test]
        public void AScoutThatLosesItsRider_ComesHome_AndIsNotSentAgain()
        {
            ScoutRota rota = House(leader: true);
            var (a, riderA) = Scout("A");
            rota.Tick(0f);
            Assume.That(rota.IsOut(a));

            Object.DestroyImmediate(riderA);
            rota.Tick(5f);

            Assert.IsFalse(rota.IsOut(a));
            AssertHome(a);
            Assert.AreEqual(0, rota.OutCount, "a riderless wheel is nobody's scout");
        }

        [Test]
        public void AScoutParkedMidSweep_IsGivenBack_WithoutTouchingEnabled()
        {
            ScoutRota rota = House(leader: true);
            var (a, _) = Scout("A");
            rota.Tick(0f);
            Assume.That(OnSweep(a));

            a.enabled = false;   // as MonowheelDriverGate or MountModule would
            rota.Tick(0.5f);

            Assert.AreEqual(0, rota.OutCount);
            AssertFormationBack(a);
            Assert.IsFalse(a.enabled, "the suppressor's `enabled` is left to the suppressor");
        }

        [Test]
        public void ReachingAWaypoint_HeadsForTheNext()
        {
            ScoutRota rota = House(leader: true);
            var (a, _) = Scout("A");
            rota.Tick(0f);
            Vector3 first = Goal(a).Position;

            a.transform.position = first;
            rota.Tick(0.5f);

            Assert.IsTrue(OnSweep(a));
            Assert.Greater(Vector3.Distance(first, Goal(a).Position), 1f, "on to the next waypoint");
        }

        [Test]
        public void TheLoop_ClosesOnItsFirstWaypoint_ThenTheScoutRidesHome()
        {
            ScoutRota rota = House(leader: true);
            var (a, _) = Scout("A");
            rota.Tick(0f);
            Vector3 first = Goal(a).Position;
            int points = new SerializedObject(rota).FindProperty("sweepPoints").intValue;

            float now = 0f;
            for (int i = 0; i < points; i++)
            {
                a.transform.position = Goal(a).Position;
                rota.Tick(now += 0.1f);
            }
            Assert.IsTrue(OnSweep(a));
            Assert.AreEqual(first, Goal(a).Position, "the last leg rides back to the first waypoint");

            a.transform.position = Goal(a).Position;
            rota.Tick(now + 0.1f);

            Assert.IsFalse(rota.IsSweeping(a));
            Assert.IsTrue(rota.IsOut(a), "600 m out, still away while it rides back");
            AssertFormationBack(a);
        }

        [Test]
        public void AReturningScout_HoldsItsPlace_UntilItIsBackWithTheColumn()
        {
            ScoutRota rota = House(leader: true);
            var (a, _) = Scout("A"); var (b, _) = Scout("B"); var (c, _) = Scout("C");
            rota.Tick(0f);
            Assume.That(rota.IsOut(a) && rota.IsOut(b));
            a.transform.position = FarOut;
            b.transform.position = FarOut;
            float t = rota.SweepTimeout;

            rota.Tick(t + 1f);
            rota.Tick(t + 2f);

            Assert.IsFalse(rota.IsSweeping(a) || rota.IsSweeping(b), "the timeout turned both for home");
            AssertFormationBack(a);
            Assert.AreEqual(2, rota.OutCount, "riding back still counts as away");
            Assert.IsFalse(rota.IsOut(c), "nobody new goes while two are away");

            a.transform.position = Vector3.zero;
            rota.Tick(t + 3f);

            Assert.IsFalse(rota.IsOut(a), "back within regroup distance: home");
            Assert.IsTrue(rota.IsSweeping(c), "C, home longest, takes the free place");
            Assert.IsTrue(rota.IsOut(b));
            Assert.AreEqual(2, rota.OutCount);
        }
    }
}
