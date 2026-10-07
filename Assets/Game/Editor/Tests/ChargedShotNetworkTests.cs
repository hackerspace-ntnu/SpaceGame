// A charged shot seen from a machine that did not fire it.
//
// The bug: Weapon.Present() gave up on charging weapons outright — "peers therefore hear a charged
// shot but do not draw one" — so ball lightning was a noise with no orb on every machine except the
// shooter's. A charging weapon is a two-press state machine and a watcher sees two identical
// NetMsg.ItemUsed messages, so it cannot tell a charge press from a launch press without being
// told. It is told, in NetArg.B.
//
// Two halves are tested here and they are deliberately different kinds of test:
//
//   the alternation   pure, because the failure is an ORDER — the phase has to describe the press
//                     rather than the state after it, and a watcher that misreads one press leaves
//                     an orb stuck on the barrel for the rest of the session.
//   the assets        read off disk, because the fix rests on three facts about shipped prefabs
//                     that nothing in C# enforces: the gun in the hand really does charge, its
//                     projectile is NOT a network prefab (skill tier 2 — every machine makes its
//                     own and only the authority's hurts), and the held prefab IS one (tier 1).
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Weapons;

namespace SpaceGame.EditorTools
{
    public class ChargedShotNetworkTests
    {
        private const int Shot = 0;
        private const int ChargeStart = 1;
        private const int ChargeLaunch = 2;

        // ── The alternation the owner reports ──────────────────────────────────────

        [Test]
        public void AWeaponThatDoesNotChargeAlwaysReportsAPlainShot()
        {
            Assert.AreEqual(Shot, Weapon.PhaseForPress(charges: false, alreadyCharging: false));
            Assert.AreEqual(Shot, Weapon.PhaseForPress(charges: false, alreadyCharging: true),
                            "a non-charging weapon has no second press to describe");
        }

        [Test]
        public void TheFirstPressStartsTheChargeAndTheSecondLaunchesIt()
        {
            Assert.AreEqual(ChargeStart, Weapon.PhaseForPress(charges: true, alreadyCharging: false));
            Assert.AreEqual(ChargeLaunch, Weapon.PhaseForPress(charges: true, alreadyCharging: true),
                            "read BEFORE the press is applied, or every press describes the one before it");
        }

        // ── What a watching machine does with it ───────────────────────────────────

        [Test]
        public void AWatcherTrustsWhatTheOwnerReported()
        {
            // Even when its own state disagrees — a dropped message must not desync the two
            // machines permanently, so the owner's word wins and the watcher re-syncs.
            Assert.IsTrue(Weapon.IsLaunchPress(charges: true, ChargeLaunch, alreadyCharging: false),
                          "told it is a launch while holding nothing: believe it, then recover");
            Assert.IsFalse(Weapon.IsLaunchPress(charges: true, ChargeStart, alreadyCharging: true),
                           "told it is a charge while already charging: believe it, and restart");
        }

        [Test]
        public void AWatcherMirrorsItsOwnAlternationWhenNobodyReported()
        {
            // An NPC firing through EntityEquipmentController never runs OnRequestUse, so B is
            // unset. The watcher has seen the same presses the authority has, so alternating is
            // sound — and it is the only thing available.
            Assert.IsFalse(Weapon.IsLaunchPress(charges: true, Shot, alreadyCharging: false));
            Assert.IsTrue(Weapon.IsLaunchPress(charges: true, Shot, alreadyCharging: true));
        }

        [Test]
        public void APlainWeaponNeverLaunchesACharge()
        {
            foreach (int phase in new[] { Shot, ChargeStart, ChargeLaunch })
            {
                Assert.IsFalse(Weapon.IsLaunchPress(charges: false, phase, alreadyCharging: true),
                               $"phase {phase} on a weapon that does not charge");
            }
        }

        /// <summary>Two full shots, alternating, as the owner reports them and a watcher reads them.</summary>
        [Test]
        public void TwoShotsInARowStayInStep()
        {
            bool owner = false, watcher = false;

            for (int shot = 0; shot < 2; shot++)
            {
                int press = Weapon.PhaseForPress(true, owner);
                Assert.AreEqual(ChargeStart, press, $"shot {shot} press 1");
                Assert.IsFalse(Weapon.IsLaunchPress(true, press, watcher));
                owner = true; watcher = true;

                press = Weapon.PhaseForPress(true, owner);
                Assert.AreEqual(ChargeLaunch, press, $"shot {shot} press 2");
                Assert.IsTrue(Weapon.IsLaunchPress(true, press, watcher));
                owner = false; watcher = false;
            }
        }

        // ── The shipped assets the fix rests on ────────────────────────────────────

        private static GameObject HeldPrefab()
        {
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(
                "Assets/Game/Resources/Items/Artifacts/BallLightningWeapon.asset");
            Assert.IsNotNull(item, "BallLightningWeapon.asset is missing");

            var held = new SerializedObject(item).FindProperty("itemPrefab").objectReferenceValue as GameObject;
            Assert.IsNotNull(held, "BallLightningWeapon has no itemPrefab");
            return held;
        }

        [Test]
        public void TheBallLightningGunInTheHandReallyDoesCharge()
        {
            var weapon = HeldPrefab().GetComponentInChildren<BallLightningWeapon>(true);
            Assert.IsNotNull(weapon, "the held prefab carries no BallLightningWeapon");

            Assert.IsTrue(new SerializedObject(weapon).FindProperty("enableCharging").boolValue,
                "If this is ever turned off, the two-press path above stops being the one ball " +
                "lightning takes — and Fire() on this weapon refuses outright without a charged " +
                "projectile, so the shot would silently become an error in the console.");
        }

        /// <summary>
        /// Skill tier 2: projectiles must NOT be networked. Every machine instantiates its own and
        /// only the authority's deals damage (`Cosmetic`). A NetworkObject here would mean the
        /// server spawning one orb for everybody — and the watcher's local copy on top of it.
        /// </summary>
        [Test]
        public void TheOrbIsNotANetworkPrefab()
        {
            var weapon = HeldPrefab().GetComponentInChildren<BallLightningWeapon>(true);
            var projectile = new SerializedObject(weapon).FindProperty("projectilePrefab")
                             .objectReferenceValue as BallLightningProjectile;

            Assert.IsNotNull(projectile, "the weapon has no projectile prefab");
            Assert.IsNull(projectile.GetComponent<NetworkObject>(),
                          "a projectile is drawn per machine, not spawned once for everybody");
        }

        /// <summary>
        /// Skill tier 1: every `InventoryItem.itemPrefab` needs a root NetworkObject, because
        /// dropping a hotbar slot routes through `World.Spawn`. Without it the gun you drop exists
        /// only on the machine that dropped it.
        /// </summary>
        [Test]
        public void TheGunYouCanDropIsANetworkPrefab()
        {
            Assert.IsNotNull(HeldPrefab().GetComponent<NetworkObject>());
        }
    }
}
