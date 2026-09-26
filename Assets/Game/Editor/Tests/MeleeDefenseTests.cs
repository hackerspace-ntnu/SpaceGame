// When a humanoid NPC may block or dodge a melee blow, and what a defended hit does to health:
// the pure decision (facing cone, cooldown, chance boundaries, the counterplay windows) and the
// damage filter seam it plugs into.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using UnityEngine;

namespace SpaceGame.Tests
{
    public class MeleeDefenseTests
    {
        private const float Cone = 120f;
        private const float Block = 0.2f;
        private const float Dodge = 0.1f;

        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        /// <summary>A defender able to answer: alive, on its feet, idle, off cooldown, attacker dead ahead.</summary>
        private static MeleeDefense.Circumstances Ready() => new MeleeDefense.Circumstances
        {
            Melee = true,
            Alive = true,
            Facing = Vector3.forward,
            ToAttacker = Vector3.forward * 2f,
            Now = 10f,
            ReadyAt = 0f,
        };

        private static DamageDefense Decide(in MeleeDefense.Circumstances c, float roll) =>
            MeleeDefense.Decide(c, Cone, Block, Dodge, roll);

        // ─────────── Chances ───────────

        [Test]
        public void OneRollSplitsIntoBlockThenDodgeThenTaken()
        {
            MeleeDefense.Circumstances c = Ready();

            Assert.AreEqual(DamageDefense.Blocked, Decide(c, 0f));
            Assert.AreEqual(DamageDefense.Blocked, Decide(c, 0.1999f));
            Assert.AreEqual(DamageDefense.Dodged, Decide(c, 0.2f), "the block band ends exactly at blockChance");
            Assert.AreEqual(DamageDefense.Dodged, Decide(c, 0.2999f));
            Assert.AreEqual(DamageDefense.None, Decide(c, 0.3f), "block + dodge is the whole share of blows answered");
            Assert.AreEqual(DamageDefense.None, Decide(c, 0.9999f));
        }

        [Test]
        public void ZeroChancesNeverDefendAndCertaintyAlwaysBlocks()
        {
            MeleeDefense.Circumstances c = Ready();

            Assert.AreEqual(DamageDefense.None, MeleeDefense.Decide(c, Cone, 0f, 0f, 0f));
            Assert.AreEqual(DamageDefense.Blocked, MeleeDefense.Decide(c, Cone, 1f, 0f, 0.9999f));
            Assert.AreEqual(DamageDefense.Dodged, MeleeDefense.Decide(c, Cone, 0f, 1f, 0.9999f));
        }

        // ─────────── Who may defend ───────────

        [Test]
        public void OnlyAMeleeBlowIsDefended()
        {
            MeleeDefense.Circumstances c = Ready();
            c.Melee = false;

            Assert.AreEqual(DamageDefense.None, Decide(c, 0f), "a bullet, a blast or a fall is never blocked");
        }

        [Test]
        public void NotWhileCommittedToItsOwnSwing()
        {
            MeleeDefense.Circumstances c = Ready();
            c.Committed = true;

            Assert.AreEqual(DamageDefense.None, Decide(c, 0f),
                            "punishing the NPC's swing is the counterplay; its arms are busy");
        }

        [Test]
        public void NotWhileDownOrDead()
        {
            MeleeDefense.Circumstances down = Ready();
            down.Down = true;
            MeleeDefense.Circumstances dead = Ready();
            dead.Alive = false;

            Assert.AreEqual(DamageDefense.None, Decide(down, 0f), "a body on the ground cannot raise a guard");
            Assert.AreEqual(DamageDefense.None, Decide(dead, 0f));
        }

        [Test]
        public void CooldownHoldsUntilItsEndAndNotAfter()
        {
            MeleeDefense.Circumstances c = Ready();
            c.ReadyAt = 12f;

            c.Now = 11.99f;
            Assert.AreEqual(DamageDefense.None, Decide(c, 0f), "a defence is never offered twice running");
            c.Now = 12f;
            Assert.AreEqual(DamageDefense.Blocked, Decide(c, 0f));
        }

        // ─────────── Facing ───────────

        [Test]
        public void TheAttackerMustBeInsideTheFacingCone()
        {
            MeleeDefense.Circumstances c = Ready();

            c.ToAttacker = Quaternion.Euler(0f, 59f, 0f) * Vector3.forward;
            Assert.AreEqual(DamageDefense.Blocked, Decide(c, 0f), "59° off is inside a 120° cone");

            c.ToAttacker = Quaternion.Euler(0f, -61f, 0f) * Vector3.forward;
            Assert.AreEqual(DamageDefense.None, Decide(c, 0f), "a blow from the flank always lands");

            c.ToAttacker = Vector3.back;
            Assert.AreEqual(DamageDefense.None, Decide(c, 0f), "nor from behind");
        }

        [Test]
        public void HeightDoesNotTakeAnAttackerOutOfSight()
        {
            Assert.IsTrue(MeleeDefense.Faces(Vector3.forward, new Vector3(0f, 3f, 0.5f), Cone),
                          "a blow from a ledge above is still in front");
        }

        [Test]
        public void NoAttackerIsNotSeen()
        {
            Assert.IsFalse(MeleeDefense.Faces(Vector3.forward, Vector3.zero, Cone));
            Assert.IsFalse(MeleeDefense.Faces(Vector3.forward, Vector3.up, Cone),
                           "an attacker standing inside the body gives no direction to face");
        }

        // ─────────── The filter seam ───────────

        [Test]
        public void AHitStoppedWholeChangesNoHealthAndRaisesNoDamage()
        {
            HealthComponent health = NewHealth();
            health.AddFilter(new FixedFilter(DamageDefense.Dodged, 0));
            int damaged = 0;
            DamageHit defended = default;
            health.OnDamage += _ => damaged++;
            health.OnDefended += hit => defended = hit;

            DamageDefense met = health.Damage(30, NewObject("attacker").transform, DamageKind.Melee);

            Assert.AreEqual(DamageDefense.Dodged, met);
            Assert.AreEqual(health.GetMaxHealth, health.GetHealth);
            Assert.AreEqual(0, damaged, "OnDamage drives the flinch — a dodged blow must not also flinch");
            Assert.AreEqual(DamageDefense.Dodged, defended.Defense, "the attacker still has to be told");
        }

        [Test]
        public void APartialBlockLandsTheRestAndSaysSoDuringOnDamage()
        {
            HealthComponent health = NewHealth();
            health.AddFilter(new FixedFilter(DamageDefense.Blocked, 5));
            DamageDefense seenInOnDamage = DamageDefense.None;
            health.OnDamage += _ => seenInOnDamage = health.LastDefense;

            health.Damage(30, null, DamageKind.Melee);

            Assert.AreEqual(health.GetMaxHealth - 5, health.GetHealth);
            Assert.AreEqual(DamageDefense.Blocked, seenInOnDamage,
                            "HurtReaction reads this to leave the guard animation alone");
        }

        [Test]
        public void AnUnfilteredHitIsTakenInFullAndResetsTheLastDefence()
        {
            HealthComponent health = NewHealth();
            var filter = new FixedFilter(DamageDefense.Blocked, 0);
            health.AddFilter(filter);
            health.Damage(10, null, DamageKind.Melee);

            health.RemoveFilter(filter);
            DamageDefense met = health.Damage(10, null);

            Assert.AreEqual(DamageDefense.None, met);
            Assert.AreEqual(DamageDefense.None, health.LastDefense);
            Assert.AreEqual(health.GetMaxHealth - 10, health.GetHealth);
        }

        [Test]
        public void TheKindReachesTheFilterThroughNetDamage()
        {
            HealthComponent health = NewHealth();
            var filter = new FixedFilter(DamageDefense.None, 0);
            health.AddFilter(filter);

            NetDamage.Apply(health.gameObject, 10, null, DamageKind.Melee);

            Assert.AreEqual(DamageKind.Melee, filter.LastKind);
        }

        private HealthComponent NewHealth() => NewObject("victim").AddComponent<HealthComponent>();

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        /// <summary>A filter that answers every hit the same way, to test the seam without the odds.</summary>
        private sealed class FixedFilter : IDamageFilter
        {
            private readonly DamageDefense defense;
            private readonly int letThrough;

            public DamageKind LastKind { get; private set; }

            public FixedFilter(DamageDefense defense, int letThrough)
            {
                this.defense = defense;
                this.letThrough = letThrough;
            }

            public void Filter(HealthComponent victim, ref DamageHit hit)
            {
                LastKind = hit.Kind;
                if (defense == DamageDefense.None) return;

                hit.Defense = defense;
                hit.Amount = letThrough;
            }
        }
    }
}
