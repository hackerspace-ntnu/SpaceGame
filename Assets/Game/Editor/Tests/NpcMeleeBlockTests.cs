// Why an NPC's sword blow can be caught on another NPC's guard, and why a caught blow does not shove.
//
// The bug these pin: CloseCombatModule.LandBlow sent its damage with no DamageKind, so it arrived
// as Unspecified — and MeleeDefense lifts its guard only against DamageKind.Melee. Every NPC-on-NPC
// melee blow was unblockable, on ~35 prefabs that carry a guard. The fix passes Melee and leaves
// the knockback out when the blow was blocked or dodged.
//
// The guard here is a stand-in filter with MeleeDefense's one rule that matters to this seam —
// answer a Melee blow, ignore anything else — rather than MeleeDefense itself, because a real
// guard only counts when BodyLanguage can show it, which needs a reaction table, a CharacterActions
// and an animator that edit mode cannot drive. MeleeDefense's own odds are MeleeDefenseTests'.
// LandBlow is private and reached by reflection; offline, NetDamage resolves the hit in place, as
// it does on the server.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    public class NpcMeleeBlockTests
    {
        private const float KnockbackSpeed = 6f;

        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        [Test]
        public void AGuardedVictim_TakesNothingFromAnNpcBlow()
        {
            (HealthComponent victim, _) = NewVictim(guarded: true);
            CloseCombatModule attacker = NewAttacker();

            LandBlow(attacker, victim.transform);

            Assert.AreEqual(victim.GetMaxHealth, victim.GetHealth,
                "The blow landed through the guard: LandBlow is not sending DamageKind.Melee.");
            Assert.AreEqual(DamageDefense.Blocked, victim.LastDefense);
        }

        [Test]
        public void ABlockedBlow_DoesNotShove()
        {
            (HealthComponent victim, Rigidbody body) = NewVictim(guarded: true);
            CloseCombatModule attacker = NewAttacker();

            LandBlow(attacker, victim.transform);

            Assert.AreEqual(Vector3.zero, body.GetAccumulatedForce(),
                "A body that caught the blow on its guard was knocked back as though it took it.");
        }

        [Test]
        public void AnUnguardedBlow_HurtsAndShoves()
        {
            (HealthComponent victim, Rigidbody body) = NewVictim(guarded: false);
            CloseCombatModule attacker = NewAttacker();

            LandBlow(attacker, victim.transform);

            Assert.Less(victim.GetHealth, victim.GetMaxHealth);
            Assert.AreNotEqual(Vector3.zero, body.GetAccumulatedForce(),
                "Control: the shove is observable, so the zero above is a refusal and not a dead probe.");
        }

        // ─────────── fixture ───────────

        private (HealthComponent health, Rigidbody body) NewVictim(bool guarded)
        {
            GameObject victim = NewObject("victim");
            victim.transform.position = Vector3.forward * 2f;
            var health = victim.AddComponent<HealthComponent>();
            victim.AddComponent<BoxCollider>();
            var body = victim.AddComponent<Rigidbody>();

            if (guarded) health.AddFilter(new MeleeGuard());
            return (health, body);
        }

        private CloseCombatModule NewAttacker()
        {
            var melee = NewObject("attacker").AddComponent<CloseCombatModule>();
            FieldInfo knockback = typeof(CloseCombatModule).GetField(
                "knockbackSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(knockback, "CloseCombatModule has no field 'knockbackSpeed'. Renamed?");
            knockback.SetValue(melee, KnockbackSpeed);
            return melee;
        }

        private static void LandBlow(CloseCombatModule attacker, Transform target)
        {
            MethodInfo land = typeof(CloseCombatModule).GetMethod(
                "LandBlow", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(land, "CloseCombatModule has no method 'LandBlow'. Renamed?");
            land.Invoke(attacker, new object[] { target });
        }

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        /// <summary>MeleeDefense's seam rule without its odds: stop every Melee blow whole, ignore the rest.</summary>
        private sealed class MeleeGuard : IDamageFilter
        {
            public void Filter(HealthComponent victim, ref DamageHit hit)
            {
                if (hit.Kind != DamageKind.Melee) return;

                hit.Defense = DamageDefense.Blocked;
                hit.Amount = 0;
            }
        }
    }
}
