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

        private CrewShift House(int seats)
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
            return go.AddComponent<CrewShift>();
        }

        [Test]
        public void Take_Ashore_FillsTheRoster_AndMarksTheHouseAshore()
        {
            CrewShift house = House(2);
            var a = new GameObject("A"); junk.Add(a);
            house.Take(a, aboard: false);
            Assert.AreEqual(CrewState.Ashore, house.State);
            Assert.IsTrue(house.HasRoom);
            var b = new GameObject("B"); junk.Add(b);
            house.Take(b, aboard: false);
            Assert.IsFalse(house.HasRoom);
        }

        [Test]
        public void FirstWithRoom_SkipsFullHouses_AndNonHouses()
        {
            CrewShift full = House(1), free = House(1);
            var a = new GameObject("A"); junk.Add(a);
            full.Take(a, aboard: false);
            var plain = new GameObject("Crawler"); junk.Add(plain);

            Assert.AreSame(free, CrewShift.FirstWithRoom(new[] { plain, full.gameObject, free.gameObject }));
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
    }
}
