using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Status;
using UnityEngine;

namespace SpaceGame.EditorTests
{
    /// <summary>
    /// Stasis holds a body for its own time and no longer, the witch's herb-bound life gives up one
    /// herb per stasis at most and keeps the last for the finish, and poison does not floor a keeper.
    ///
    /// <para>
    /// Both rules are what keep the sprite hamlet's boss fight a fight: a stasis that a touching
    /// dome or a second bolt could refresh is a lock nobody escapes, and a stasis in which the
    /// keepers could burn every herb at once is a fight that ends on the first freeze.
    /// </para>
    /// </summary>
    public class StasisTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
        }

        [Test]
        public void StasisHoldsTheBodyAndIsNotExtended()
        {
            StatusReceiver body = StatusReceiver.Ensure(New("creature", typeof(HealthComponent)));

            body.Apply(StatusKind.Stasis, seconds: 5f);
            Assert.IsTrue(body.Has(StatusKind.Stasis), "Stasis did not take offline, where every machine decides.");
            Assert.IsTrue(body.Suppressed, "A body in stasis could still act.");

            body.Apply(StatusKind.Stasis, seconds: 60f);
            Assert.LessOrEqual(body.SecondsLeft(StatusKind.Stasis), 5f,
                               "A second application pushed the stasis out. A creature against the dome " +
                               "would be held for as long as it stood there.");
        }

        [Test]
        public void OnlyRueInStasisHurtsTheWitch()
        {
            GameObject witch = New("witch", typeof(HealthComponent));
            HealthComponent health = witch.GetComponent<HealthComponent>();
            health.RestoreHealth(health.GetMaxHealth);
            HerbBoundLife life = witch.AddComponent<HerbBoundLife>();

            // Unity raises neither for an AddComponent outside play mode.
            Invoke(life, "Awake");
            Invoke(life, "OnEnable");
            StatusReceiver body = StatusReceiver.Of(witch);
            int full = health.GetHealth;

            health.Damage(50, null);
            health.Damage(50, null, DamageKind.Rue);
            Assert.AreEqual(full, health.GetHealth, "She was hurt while free.");

            body.Apply(StatusKind.Stasis, seconds: 5f);
            health.Damage(50, null);
            Assert.AreEqual(full, health.GetHealth, "An ordinary hit hurt her in stasis.");

            health.Damage(full * 10, null, DamageKind.Rue);
            Assert.AreEqual(1, life.HerbsBurned, "One stasis burned more or less than one herb.");
            Assert.IsTrue(health.Alive, "The first stasis killed her.");
        }

        /// <summary>
        /// Rue takes her down to a sliver and no further: the last herb is the finish's to pluck,
        /// and plucking it is the one blow that ends her.
        /// </summary>
        [Test]
        public void TheLastHerbIsLeftForTheKeepersToPluck()
        {
            GameObject witch = New("witch", typeof(HealthComponent));
            HealthComponent health = witch.GetComponent<HealthComponent>();
            health.RestoreHealth(health.GetMaxHealth);
            HerbBoundLife life = witch.AddComponent<HerbBoundLife>();
            Invoke(life, "Awake");
            Invoke(life, "OnEnable");
            StatusReceiver body = StatusReceiver.Of(witch);

            for (int stasis = 0; stasis < 3; stasis++)
            {
                body.Clear(StatusKind.Stasis);
                body.Apply(StatusKind.Stasis, seconds: 5f);
                health.Damage(health.GetMaxHealth * 10, null, DamageKind.Rue);
            }

            Assert.IsTrue(health.Alive, "Rue burned the last herb; the finish had nothing left to pluck.");
            Assert.IsTrue(life.IsBroken, "Three freezes' worth of rue did not leave her broken.");

            life.PluckLastHerb(null);
            Assert.IsFalse(health.Alive, "Plucking the last herb did not end her.");
        }

        /// <summary>
        /// A poison ball is counted against a keeper (PoisonTally), not felt as a blow: a keeper
        /// floored by every ball would stop throwing, and the spiral would win on its own.
        /// </summary>
        [Test]
        public void PoisonDoesNotFloorAKeeper()
        {
            GameObject keeper = New("keeper", typeof(HealthComponent));
            HealthComponent health = keeper.GetComponent<HealthComponent>();
            health.RestoreHealth(health.GetMaxHealth);
            StunOnHitModule stun = keeper.AddComponent<StunOnHitModule>();
            Invoke(stun, "OnEnable");

            health.Damage(12, null, DamageKind.Poison);
            Assert.IsFalse(stun.IsStunned, "A poison ball floored the keeper.");

            health.Damage(12, null);
            Assert.IsTrue(stun.IsStunned, "An ordinary blow no longer floors a keeper.");
            Assert.AreEqual(health.GetMaxHealth, health.GetHealth, "A keeper lost health.");
        }

        private GameObject New(string name, params System.Type[] components)
        {
            var go = new GameObject(name, components);
            spawned.Add(go);
            return go;
        }

        private static void Invoke(Component target, string method)
        {
            MethodInfo info = target.GetType().GetMethod(
                method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

            Assert.IsNotNull(info, $"No '{method}' on {target.GetType().Name}.");
            info.Invoke(target, null);
        }
    }
}
