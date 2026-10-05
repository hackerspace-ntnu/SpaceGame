// A group whose people were all killed stays gone in that world: it is marked WipedOut (saved in
// its record) instead of folding back into a record that re-spawns it at full strength, with fresh
// guns, the next tick a player is in range. Dead bodies still lying in the group's Live list are
// not "members left": they are corpses, and since corpses lie for minutes the old count of Live
// objects never reached zero while anyone was there to see it.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class WipedOutGroupTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<Object> junk = new();
        private NpcWorldSim sim;
        private NpcGroup caravan;

        [SetUp]
        public void SetUp()
        {
            var tribe = ScriptableObject.CreateInstance<FactionDefinition>();
            junk.Add(tribe);
            var template = new NpcGroupTemplate { id = "caravan", tribe = tribe, useStartPosition = true };

            var go = new GameObject("Sim");
            junk.Add(go);
            sim = go.AddComponent<NpcWorldSim>();
            typeof(NpcWorldSim).GetField("templates", Private).SetValue(sim, new[] { template });
            Call("Awake");
            Call("Start");

            caravan = sim.FindGroup("caravan");
            caravan.Spawned = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private object Call(string method, params object[] args) =>
            typeof(NpcWorldSim).GetMethod(method, Private).Invoke(sim, args);

        private GameObject Member(bool dead)
        {
            var member = new GameObject("Member");
            junk.Add(member);
            HealthComponent health = member.AddComponent<HealthComponent>();
            if (dead) health.Damage(999);
            caravan.Live.Add(member);
            caravan.Fighters.Add(member);
            return member;
        }

        [Test]
        public void ACaravanWhosePeopleAllLieDead_IsWipedOut()
        {
            Member(dead: true);
            Member(dead: true);

            Call("TickGroup", caravan, 0.1f);

            Assert.IsTrue(caravan.WipedOut, "every member is a corpse: the caravan is gone");
            Assert.IsFalse(caravan.Spawned);
        }

        [Test]
        public void AWipedOutCaravan_StaysGoneThroughItsSaveRecord()
        {
            Member(dead: true);
            Call("TickGroup", caravan, 0.1f);

            var restored = new NpcGroup { Id = caravan.Id, TemplateId = caravan.TemplateId };
            NpcGroup.Record record = caravan.ToRecord();
            restored.ApplyRecord(in record);

            Assert.IsTrue(restored.WipedOut, "a reload must not bring the caravan back at full strength");
        }

        [Test]
        public void AWipedOutCaravan_IsNotSpawnedAgain()
        {
            Member(dead: true);
            Call("TickGroup", caravan, 0.1f);
            caravan.Live.Clear();
            caravan.Fighters.Clear();

            Call("TickGroup", caravan, 0.1f);

            Assert.IsFalse(caravan.Spawned);
            Assert.IsTrue(caravan.WipedOut);
        }
    }
}
