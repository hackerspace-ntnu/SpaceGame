using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Vehicles;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class CrewShiftTests
    {
        private readonly List<Object> junk = new();

        [TearDown]
        public void TearDown() { foreach (var o in junk) if (o != null) Object.DestroyImmediate(o); junk.Clear(); }

        private CrewShift House(int seats, int standingPosts = 0)
        {
            var go = new GameObject("House");
            junk.Add(go);
            var posts = new Transform[seats];
            for (int i = 0; i < seats; i++) { posts[i] = new GameObject($"Seat_{i}").transform; posts[i].SetParent(go.transform); }
            var vessel = go.AddComponent<VesselSeats>();
            var so = new SerializedObject(vessel);
            SerializedProperty p = so.FindProperty("seats");
            p.arraySize = posts.Length;
            for (int i = 0; i < posts.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = posts[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            var shift = go.AddComponent<CrewShift>();
            var shiftSo = new SerializedObject(shift);
            shiftSo.FindProperty("standingPosts").intValue = standingPosts;
            shiftSo.ApplyModifiedPropertiesWithoutUndo();
            return shift;
        }

        private GameObject Person(string name)
        {
            var go = new GameObject(name);
            junk.Add(go);
            return go;
        }

        private GameObject Elder(string name)
        {
            GameObject go = Person(name);
            go.AddComponent<StandingRider>();
            return go;
        }

        [Test]
        public void Take_Ashore_FillsTheRoster_AndMarksTheHouseAshore()
        {
            CrewShift house = House(2);
            var a = new GameObject("A"); junk.Add(a);
            house.Take(a, aboard: false);
            Assert.AreEqual(CrewState.Ashore, house.State);
            Assert.IsTrue(house.HasRoomFor(a));
            var b = new GameObject("B"); junk.Add(b);
            house.Take(b, aboard: false);
            Assert.IsFalse(house.HasRoomFor(b));
        }

        [Test]
        public void FirstWithRoom_SkipsFullHouses_AndNonHouses()
        {
            CrewShift full = House(1), free = House(1);
            var a = new GameObject("A"); junk.Add(a);
            full.Take(a, aboard: false);
            var plain = new GameObject("Crawler"); junk.Add(plain);

            Assert.AreSame(free, CrewShift.FirstWithRoom(new[] { plain, full.gameObject, free.gameObject }, a));
        }

        [Test]
        public void AnyAshore_ReadsEveryHouseInTheGroup()
        {
            CrewShift aboard = House(1), ashore = House(1);
            var a = new GameObject("A"); junk.Add(a);
            ashore.Take(a, aboard: false);
            Assert.IsTrue(CrewShift.AnyAshore(new[] { aboard.gameObject, ashore.gameObject }));
            Assert.IsFalse(CrewShift.AnyAshore(new[] { aboard.gameObject }));
        }
        [Test]
        public void Take_Ashore_ClosesTheLeadersGate_BeforeAnyUpdate()
        {
            // A city spawned (or reloaded) mid-stop: its leader starts in Choosing, and the gate must
            // already be up when the leader first ticks, or it picks its next stop and walks away.
            const string city = "crew-gate-test";
            CrewShift lead = House(1);
            FormationModule leadFormation = lead.gameObject.AddComponent<FormationModule>();
            leadFormation.SetFormation(city, true);
            Assume.That(FormationModule.LeaderOf(city), Is.SameAs(leadFormation), "edit mode must register formations");

            NpcTaskModule tasks = lead.gameObject.AddComponent<NpcTaskModule>();
            typeof(NpcTaskModule).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(tasks, null);
            tasks.SetTasks(new[] { new NpcTask { targetSite = SiteKind.Ruin } });

            var crew = new GameObject("Crew"); junk.Add(crew);
            lead.Take(crew, aboard: false);

            Assert.IsFalse(CrewShift.AllAboard(city), "the house is registered under its formation, crew ashore");
            tasks.Tick(default, 1f);
            Assert.AreEqual(NpcTaskModule.Phase.Choosing, tasks.CurrentPhase, "held by the gate Take installed");
            Assert.IsFalse(lead.GetComponent<AgentGoal>().HasGoal, "no destination while the crew is ashore");
        }

        [Test]
        public void AStandingRider_TakesOnlyAStandingPost()
        {
            CrewShift house = House(3, standingPosts: 1);
            GameObject elder = Elder("Elder");
            house.Take(elder, aboard: true);
            Assert.AreSame(elder, house.GetComponent<VesselSeats>().OccupantAt(2), "the last seat is the standing post");
            Assert.IsNull(house.GetComponent<VesselSeats>().OccupantAt(0));
        }

        [Test]
        public void ACrewPost_IsNeverTakenByAStandingRider_NorTheStandingPostByCrew()
        {
            CrewShift house = House(2, standingPosts: 1);
            VesselSeats seats = house.GetComponent<VesselSeats>();
            GameObject crew = Person("Crew");
            house.Take(crew, aboard: true);
            Assert.AreSame(crew, seats.OccupantAt(0));
            Assert.IsFalse(house.HasRoomFor(Person("Second crew")), "one crew post, already taken");
            Assert.IsNull(seats.OccupantAt(1), "the standing post stays free for an elder");

            GameObject elder = Elder("Elder");
            house.Take(elder, aboard: true);
            Assert.AreSame(elder, seats.OccupantAt(1));
            Assert.IsFalse(house.HasRoomFor(Elder("Second elder")), "one standing post, already taken");
        }

        [Test]
        public void HasRoomFor_CountsCrewAndStandingSeparately()
        {
            CrewShift house = House(3, standingPosts: 1);
            house.Take(Elder("Elder"), aboard: false);
            Assert.IsFalse(house.HasRoomFor(Elder("Other elder")));
            Assert.IsTrue(house.HasRoomFor(Person("Crew")));

            CrewShift plain = House(2);
            Assert.IsFalse(plain.HasRoomFor(Elder("Elder on a barge")), "a carrier with no standing post takes no elder");
        }

        [Test]
        public void FirstWithRoom_PassesOverAHouseWhoseStandingPostIsTaken()
        {
            CrewShift lead = House(2, standingPosts: 1), second = House(2, standingPosts: 1);
            lead.Take(Elder("First elder"), aboard: false);
            GameObject next = Elder("Second elder");
            Assert.AreSame(second, CrewShift.FirstWithRoom(new[] { lead.gameObject, second.gameObject }, next));
            Assert.AreSame(lead, CrewShift.FirstWithRoom(new[] { lead.gameObject, second.gameObject }, Person("Crew")));
        }

        [Test]
        public void AnElderAshore_HoldsTheGate()
        {
            const string city = "elder-gate-test";
            CrewShift lead = House(2, standingPosts: 1);
            lead.gameObject.AddComponent<FormationModule>().SetFormation(city, true);
            lead.Take(Elder("Elder"), aboard: false);
            Assert.IsFalse(CrewShift.AllAboard(city), "the column waits for its elder like any crew member");
        }
    }
}
