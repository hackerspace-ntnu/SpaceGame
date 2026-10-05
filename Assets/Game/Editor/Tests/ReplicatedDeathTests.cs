// Why an NPC killed on the host stays on a client's screen, and dies there the way it did on the host.
//
// The bug these pin: a client learns of every death through the health NetworkVariable, which
// NetworkedHealthComponent applies with HealthComponent.RestoreHealth — the same call a save load
// makes. Read as a load, a death on a client was not shown at all. RestoreHealth now says which
// kind of restore it is (IsReplicating), and the death listeners that care tell the two apart.
// The corpse itself is never taken away by the death: it lies there for the server's Remains
// countdown (RemainsTests), and a client's copy goes when the server's network despawn reaches it.
//
// Lifecycle methods are called by hand: edit mode does not deliver Awake/OnEnable to a plain
// MonoBehaviour added with AddComponent.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using SpaceGame.Agents;
using SpaceGame.Audio;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    public class ReplicatedDeathTests
    {
        private const float CorpseLifetime = 5f;

        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        // ─────────── HealthComponent: which restore is this ───────────

        [Test]
        public void AReplicatedDeathIsARestoreThatSaysItIsReplicated()
        {
            HealthComponent health = NewObject("npc").AddComponent<HealthComponent>();
            bool restoring = false, replicating = false;
            health.OnDeath += () => { restoring = health.IsRestoring; replicating = health.IsReplicating; };

            health.RestoreHealth(0, replicated: true);

            Assert.IsTrue(restoring,
                "A replicated death must still read as a restore, or every client drops its own loot " +
                "and charges the killer in the ledger a second time.");
            Assert.IsTrue(replicating);
            Assert.IsFalse(health.IsRestoring, "The flag must not outlive the call.");
            Assert.IsFalse(health.IsReplicating, "The flag must not outlive the call.");
        }

        [Test]
        public void ASavedDeathIsNotReplicated()
        {
            HealthComponent health = NewObject("npc").AddComponent<HealthComponent>();
            bool replicating = true;
            health.OnDeath += () => replicating = health.IsReplicating;

            health.RestoreHealth(0);

            Assert.IsFalse(replicating,
                "A save load (and a late joiner's spawn snapshot) meets a death that is already over.");
        }

        // ─────────── HealthReactionModule: the corpse ───────────

        [Test]
        public void AReplicatedDeath_KeepsTheCorpse()
        {
            (GameObject npc, HealthComponent health, AgentController brain, _) = NewNpc();

            health.RestoreHealth(0, replicated: true);

            Assert.IsTrue(npc.activeSelf,
                "A client must not switch the corpse off the frame it died; it goes when the " +
                "server's network despawn reaches it.");
            Assert.IsFalse(brain.enabled, "The dead STATE still applies: a corpse does not think.");
        }

        [Test]
        public void ASavedDeath_LeavesTheCorpseLying()
        {
            (GameObject npc, HealthComponent health, AgentController brain, _) = NewNpc();

            health.RestoreHealth(0);

            Assert.IsTrue(npc.activeSelf,
                "A corpse loaded from a save lies where it fell for the rest of its Remains countdown.");
            Assert.IsFalse(brain.enabled);
        }

        [Test]
        public void NeitherRestoredDeath_FiresTheOneOffConsequences()
        {
            (_, HealthComponent replicated, _, Counter replicatedEvents) = NewNpc();
            (_, HealthComponent saved, _, Counter savedEvents) = NewNpc();

            replicated.RestoreHealth(0, replicated: true);
            saved.RestoreHealth(0);

            Assert.AreEqual(0, replicatedEvents.Count,
                "The death UnityEvent (and the noise and ledger report beside it) belong to the " +
                "machine that decided the death; a client firing them repeats them once per peer.");
            Assert.AreEqual(0, savedEvents.Count, "A load must not replay the death's announcement.");
        }

        [Test]
        public void AKillDecidedHere_FiresTheConsequencesOnce()
        {
            (GameObject npc, HealthComponent health, _, Counter events) = NewNpc();

            health.Damage(health.GetMaxHealth);

            Assert.AreEqual(1, events.Count,
                "Control: the counter is wired, so the zeros above are a refusal and not a dead probe.");
            Assert.IsTrue(npc.activeSelf, "A kill leaves the corpse lying for its Remains countdown.");
        }

        // ─────────── HidePartsOnDeath: the dropped staff ───────────

        [Test]
        public void AReplicatedDeath_HidesThePartThatDroppedAsLoot()
        {
            (HealthComponent health, Renderer staff) = NewStaffBearer();

            health.RestoreHealth(0, replicated: true);

            Assert.IsFalse(staff.enabled,
                "The server dropped the staff as loot; a client that keeps one in the corpse's fist " +
                "shows two staffs, one of which is a lie.");
        }

        [Test]
        public void ASavedDeath_LeavesThePartAlone()
        {
            (HealthComponent health, Renderer staff) = NewStaffBearer();

            health.RestoreHealth(0);

            Assert.IsTrue(staff.enabled, "A save load is not a kill; nothing was dropped this session.");
        }

        // ─────────── fixture ───────────

        /// <summary>The smallest NPC whose death HealthReactionModule handles, wired by hand.</summary>
        private (GameObject npc, HealthComponent health, AgentController brain, Counter events) NewNpc()
        {
            GameObject npc = NewObject("npc");
            var health = npc.AddComponent<HealthComponent>();
            var brain = npc.AddComponent<AgentController>();
            var reaction = npc.AddComponent<HealthReactionModule>();

            Set(reaction, "corpseLifetime", CorpseLifetime);
            // Silent, so the test does not depend on the audio catalog having an event assigned.
            Set(reaction, "deathId", SfxId.None);
            Set(reaction, "hurtId", SfxId.None);

            var counter = new Counter();
            var onDeath = new UnityEvent();
            onDeath.AddListener(counter.Increment);
            Set(reaction, "onDeath", onDeath);

            Call(reaction, "Awake");
            Call(reaction, "OnEnable");
            return (npc, health, brain, counter);
        }

        private (HealthComponent health, Renderer staff) NewStaffBearer()
        {
            GameObject npc = NewObject("conjurer");
            var health = npc.AddComponent<HealthComponent>();

            var staff = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            staff.name = "Staff";
            staff.transform.SetParent(npc.transform);

            var hide = npc.AddComponent<HidePartsOnDeath>();
            Set(hide, "partNames", new[] { "Staff" });
            Call(hide, "Awake");
            Call(hide, "OnEnable");

            return (health, staff.GetComponent<Renderer>());
        }

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        private sealed class Counter
        {
            public int Count { get; private set; }
            public void Increment() => Count++;
        }

        private static void Set(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, $"{target.GetType().Name} has no field '{field}'. Renamed?");
            info.SetValue(target, value);
        }

        private static void Call(MonoBehaviour behaviour, string method)
        {
            MethodInfo info = behaviour.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, $"{behaviour.GetType().Name} has no method '{method}'. Renamed?");
            info.Invoke(behaviour, null);
        }
    }
}
