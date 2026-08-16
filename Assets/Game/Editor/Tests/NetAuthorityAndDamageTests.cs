// Tests for the two things that decide who is allowed to act: authority discovery, and damage.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    public class NetAuthorityAndDamageTests
    {
        private readonly List<GameObject> spawned = new();

        private GameObject NewObject(string name = "entity")
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
        }

        // ─────────── Authority ───────────

        [Test]
        public void EverythingIsOursWithNoSession()
        {
            GameObject entity = NewObject();

            Assert.IsTrue(Network.Simulates(entity.transform),
                "With no NetworkManager the game is single-player and this machine runs everything.");
            Assert.IsTrue(Network.Owns(entity.transform));
            Assert.IsFalse(Network.IsNetworked);
        }

        [Test]
        public void AuthorityQuestionsAboutNothingAreSafe()
        {
            Assert.IsTrue(Network.Simulates(null));
            Assert.IsTrue(Network.Owns(null));
        }

        [Test]
        public void DiscoveryFindsTheThingsThatDriveAnEntity()
        {
            GameObject entity = NewObject("agent");
            entity.AddComponent<AgentController>();

            var body = new GameObject("body");
            body.transform.SetParent(entity.transform);
            NavMeshAgent navAgent = body.AddComponent<NavMeshAgent>();

            List<Behaviour> drivers = NetAuthority.Discover(entity);

            Assert.Contains(entity.GetComponent<AgentController>(), drivers,
                "The brain is the first thing a remote copy must stop running.");
            Assert.Contains(navAgent, drivers,
                "NavMeshAgent moves the transform on its own and has to be switched off too.");
        }

        [Test]
        public void DiscoveryLeavesPresentationAlone()
        {
            GameObject entity = NewObject("agent");
            entity.AddComponent<AgentController>();
            Animator animator = entity.AddComponent<Animator>();
            AudioSource audio = entity.AddComponent<AudioSource>();

            List<Behaviour> drivers = NetAuthority.Discover(entity);

            Assert.IsFalse(drivers.Contains(animator),
                "A remote copy still has to animate — something else is moving it, not nothing.");
            Assert.IsFalse(drivers.Contains(audio));
        }

        [Test]
        public void DiscoveryOnNothingIsEmptyRatherThanAnError()
        {
            Assert.IsEmpty(NetAuthority.Discover(null));
            Assert.IsEmpty(NetAuthority.Discover(NewObject("bare")));
        }

        // ─────────── Damage ───────────

        [Test]
        public void DamageLandsLocallyWhenThereIsNoOneToAskInstead()
        {
            GameObject victim = NewObject("victim");
            HealthComponent health = victim.AddComponent<HealthComponent>();
            int before = health.GetHealth;

            NetDamage.Apply(victim, 30);

            Assert.AreEqual(before - 30, health.GetHealth,
                "An entity nobody has networked must still be damageable — locally is the best " +
                "available answer, and refusing would make it invulnerable.");
        }

        [Test]
        public void DamageFindsHealthOnAParent()
        {
            GameObject victim = NewObject("victim");
            HealthComponent health = victim.AddComponent<HealthComponent>();

            var hitbox = new GameObject("hitbox");
            hitbox.transform.SetParent(victim.transform);

            NetDamage.Apply(hitbox, 10);

            Assert.AreEqual(health.GetMaxHealth - 10, health.GetHealth,
                "Bullets hit colliders, and a collider is rarely the object holding the health.");
        }

        [Test]
        public void DamageRecordsWhoDidIt()
        {
            GameObject victim = NewObject("victim");
            HealthComponent health = victim.AddComponent<HealthComponent>();
            GameObject attacker = NewObject("attacker");

            NetDamage.Apply(victim, 5, attacker.transform);

            Assert.AreSame(attacker.transform, health.LastDamageSource,
                "Retaliation, kill credit and damage feedback all read this.");
        }

        [Test]
        public void NonsenseDamageIsIgnoredRatherThanApplied()
        {
            GameObject victim = NewObject("victim");
            HealthComponent health = victim.AddComponent<HealthComponent>();
            int before = health.GetHealth;

            NetDamage.Apply(victim, 0);
            NetDamage.Apply(victim, -50);
            NetDamage.Apply((GameObject)null, 10);

            Assert.AreEqual(before, health.GetHealth);
        }

        [Test]
        public void DamagingSomethingWithNoHealthIsSafe()
        {
            Assert.DoesNotThrow(() => NetDamage.Apply(NewObject("rock"), 10));
        }
    }
}
