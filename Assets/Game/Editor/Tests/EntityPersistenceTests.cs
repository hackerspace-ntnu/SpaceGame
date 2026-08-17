// The rules that decide whether a moving thing is saved at all, and whether dying twice costs twice.
//
// Both were real, shipped bugs. SaveablePolicy inferred "this moves" from having a non-kinematic
// Rigidbody or a NavMeshAgent, and every legged machine and mount in the game has neither — so the
// Ostrich a player rides was never captured. And HealthComponent.RestoreHealth fires OnDeath, which
// EntityLootTable listens to, so every reload of a world containing one dead creature dropped its loot
// again.
//
// Neither is catchable by reading the code: the first looks like a save system that works, and the
// second looks like generous loot.
using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Locomotion;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTools
{
    public class EntityPersistenceTests
    {
        private readonly List<GameObject> spawned = new();

        private GameObject New(string name)
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

        // ─────────────────────────────────────────────
        //  Opting in
        // ─────────────────────────────────────────────

        [Test]
        public void NeedsSaving_IsTrueForAKinematicBodyThatIsAPersistentEntity()
        {
            // The exact shape of every mount and legged machine in this project: a kinematic Rigidbody,
            // no NavMeshAgent, no HealthComponent. Before IPersistentEntity this returned false.
            GameObject mount = New("Mount");
            Rigidbody body = mount.AddComponent<Rigidbody>();
            body.isKinematic = true;
            mount.AddComponent<AgentController>();

            Assert.IsTrue(SaveablePolicy.NeedsSaving(mount, out string why));
            Assert.IsTrue(why.Contains("entity"), $"Expected the reason to name the marker, got '{why}'.");
        }

        [Test]
        public void NeedsSaving_IsFalseForAKinematicBodyThatIsNothingElse()
        {
            // The counter-case, so the clause above is not simply saving everything. A kinematic body
            // with no behaviour is scenery.
            GameObject prop = New("Prop");
            prop.AddComponent<Rigidbody>().isKinematic = true;

            Assert.IsFalse(SaveablePolicy.NeedsSaving(prop, out _));
        }

        [Test]
        public void MountModuleIsAPersistentEntity()
        {
            // Declared on the type rather than discovered per prefab, so this is a compile-time fact
            // being pinned: if someone removes the interface, the mount silently stops being saved.
            Assert.IsTrue(typeof(IPersistentEntity).IsAssignableFrom(typeof(MountModule)));
            Assert.IsTrue(typeof(IPersistentEntity).IsAssignableFrom(typeof(AgentController)));
            Assert.IsTrue(typeof(IPersistentEntity).IsAssignableFrom(typeof(SpaceGame.World.SceneTracked)));
        }

        [Test]
        public void LeggedLocomotionIsAPersistentEntityForEverySubclass()
        {
            // The whole reason the marker is an interface on the base: name matching cannot see base
            // classes, so a per-type list would need every machine spelled out.
            Assert.IsTrue(typeof(IPersistentEntity).IsAssignableFrom(typeof(LeggedLocomotion)));
        }

        [Test]
        public void Ensure_AddsAMountSaverToAMount()
        {
            GameObject mount = New("Mount");
            mount.AddComponent<MountModule>();

            SaveablePolicy.Ensure(mount, out string added);

            Assert.IsNotNull(mount.GetComponent<SaveableEntity>());
            Assert.IsNotNull(mount.GetComponent<MountSaveable>(), $"Ensure added: {added}");
        }

        [Test]
        public void Ensure_AddsAnAgentStateSaverToSomethingThatCanFight()
        {
            GameObject agent = New("Agent");
            agent.AddComponent<AgentTargeting>();

            SaveablePolicy.Ensure(agent, out _);

            Assert.IsNotNull(agent.GetComponent<AgentStateSaveable>());
        }

        [Test]
        public void Ensure_IsIdempotent()
        {
            // Both the editor pass and the per-hydrate runtime pass call this, so a second run adding a
            // second copy of every saver would double every entity's state bag.
            GameObject mount = New("Mount");
            mount.AddComponent<MountModule>();

            SaveablePolicy.Ensure(mount, out _);
            bool changedAgain = SaveablePolicy.Ensure(mount, out _);

            Assert.IsFalse(changedAgain);
            Assert.AreEqual(1, mount.GetComponents<MountSaveable>().Length);
        }

        // ─────────────────────────────────────────────
        //  Death that stays dead, and pays out once
        // ─────────────────────────────────────────────

        [Test]
        public void RestoreHealth_ToZero_AnnouncesDeathAndFlagsItAsARestore()
        {
            GameObject creature = New("Creature");
            HealthComponent health = creature.AddComponent<HealthComponent>();

            bool died = false;
            bool restoringDuringDeath = false;

            health.OnDeath += () =>
            {
                died = true;
                restoringDuringDeath = health.IsRestoring;
            };

            health.RestoreHealth(0);

            Assert.IsTrue(died, "A restored corpse must announce its death or nothing applies the state.");
            Assert.IsTrue(restoringDuringDeath, "Listeners must be able to tell a load from a kill.");
            Assert.IsFalse(health.IsRestoring, "The flag must not outlive the call.");
        }

        [Test]
        public void RestoreHealth_ToZero_WhenAlreadyZero_StillAnnouncesDeath()
        {
            // The case the old crossing test missed. An entity whose live value is already 0 — or
            // negative from overkill — got no event, so nothing applied the dead state and the corpse
            // came back standing up.
            GameObject creature = New("Creature");
            HealthComponent health = creature.AddComponent<HealthComponent>();

            health.RestoreHealth(0);

            int deaths = 0;
            health.OnDeath += () => deaths++;

            health.RestoreHealth(0);

            Assert.AreEqual(1, deaths);
        }

        [Test]
        public void RestoreHealth_Damage_StillReportsARealDeathAsReal()
        {
            // The guard must be narrow. If IsRestoring leaked past the restore call, no creature killed
            // later in the session would ever drop loot.
            GameObject creature = New("Creature");
            HealthComponent health = creature.AddComponent<HealthComponent>();

            health.RestoreHealth(10);

            bool restoringDuringDeath = true;
            health.OnDeath += () => restoringDuringDeath = health.IsRestoring;

            health.Damage(999);

            Assert.IsFalse(restoringDuringDeath);
        }

        [Test]
        public void RestoreHealth_AboveZero_RevivesSomethingThatWasDead()
        {
            GameObject creature = New("Creature");
            HealthComponent health = creature.AddComponent<HealthComponent>();

            health.Damage(999);
            Assert.IsFalse(health.Alive);

            bool revived = false;
            health.OnRevive += () => revived = true;

            health.RestoreHealth(50);

            Assert.IsTrue(revived);
            Assert.IsTrue(health.Alive);
            Assert.AreEqual(50, health.GetHealth);
        }

        // ─────────────────────────────────────────────
        //  Patrol progress
        // ─────────────────────────────────────────────

        [Test]
        public void RestorePatrolProgress_ClampsToTheRouteThatExistsNow()
        {
            // The route is authored data and may have lost waypoints since the save. An out-of-range
            // index would throw inside the patrol tick, on a frame nobody can attribute to a load.
            GameObject robot = New("Robot");
            PatrolModule patrol = robot.AddComponent<PatrolModule>();

            patrol.RestorePatrolProgress(99, -1);

            Assert.AreEqual(0, patrol.WaypointIndex, "With no waypoints authored, index 0 is the only valid answer.");
            Assert.AreEqual(-1, patrol.WaypointDirection);
        }

        [Test]
        public void RestorePatrolProgress_NormalisesDirection()
        {
            GameObject robot = New("Robot");
            PatrolModule patrol = robot.AddComponent<PatrolModule>();

            // 0 is what an old save with no direction field deserialises to, and a direction of 0 would
            // leave a ping-pong patroller standing on one waypoint forever.
            patrol.RestorePatrolProgress(0, 0);

            Assert.AreEqual(1, patrol.WaypointDirection);
        }

        // ─────────────────────────────────────────────
        //  Savers round-tripping
        // ─────────────────────────────────────────────

        [Test]
        public void AgentStateSaveable_RoundTripsMemoryThroughTheSaveSerializer()
        {
            GameObject agent = New("Agent");
            AgentTargeting targeting = agent.AddComponent<AgentTargeting>();
            var saver = agent.AddComponent<AgentStateSaveable>();

            targeting.RestoreMemory(null, new Vector3(3f, 4f, 5f), true, 2.5f, null);

            // Through StateBag, so the Vector3 converters are exercised — the same path a real save
            // takes. Read without them, a Vector3 recurses through its own properties.
            var bag = new StateBag();
            bag.Set(saver.SaveKey, saver.CaptureState());

            Assert.IsTrue(bag.TryGetRaw(saver.SaveKey, out JObject payload));

            targeting.RestoreMemory(null, Vector3.zero, false, 0f, null);
            saver.RestoreState(payload);
            saver.OnLoadComplete();

            Assert.IsTrue(targeting.HasLastKnownPosition);
            Assert.AreEqual(new Vector3(3f, 4f, 5f), targeting.LastKnownPosition);
            Assert.AreEqual(2.5f, targeting.TimeSinceSeen, 0.001f);
        }

        [Test]
        public void AgentStateSaveable_ToleratesAPayloadFromBeforeTheseFieldsExisted()
        {
            // Every saver must read defensively: an empty object is what a save written by an older
            // build looks like, and it must leave the component alone rather than throw.
            GameObject agent = New("Agent");
            agent.AddComponent<AgentTargeting>();
            var saver = agent.AddComponent<AgentStateSaveable>();

            Assert.DoesNotThrow(() =>
            {
                saver.RestoreState(new JObject());
                saver.OnLoadComplete();
            });
        }

        // ─────────────────────────────────────────────
        //  Runtime spawns
        // ─────────────────────────────────────────────

        [Test]
        public void EnsureSpawned_GivesARuntimeVehicleTheSaversItsPrefabLacks()
        {
            // The ornithopter case. The prefab carries a SaveableEntity and a RigidbodySaveable, so it
            // saved its pose and its velocity — and not one thing about somebody flying it, because
            // MountSaveable is added by the policy and the policy had never been run over a spawn.
            GameObject craft = New("Craft");
            craft.AddComponent<Rigidbody>();
            craft.AddComponent<MountModule>();

            Assert.IsTrue(SaveablePolicy.EnsureSpawned(craft));

            Assert.IsNotNull(craft.GetComponent<SaveableEntity>());
            Assert.IsNotNull(craft.GetComponent<MountSaveable>(),
                "A runtime-spawned mount that cannot save its rider comes back empty.");
        }

        [Test]
        public void EnsureSpawned_LeavesUnqualifiedSpawnsAlone()
        {
            // Every effect, every piece of debris and every projectile goes through the same spawn
            // service. Opting them in would put them in the save file and re-spawn them on every load.
            GameObject effect = New("Effect");

            Assert.IsFalse(SaveablePolicy.EnsureSpawned(effect));
            Assert.IsNull(effect.GetComponent<SaveableEntity>());
        }

        // ─────────────────────────────────────────────
        //  Player binding order
        // ─────────────────────────────────────────────

        [Test]
        public void Bind_GivesThePlayerAMomentumSaver()
        {
            // The player is SaveScope.External, so SaveablePolicy deliberately steps over it — which
            // also meant the "a moving body has momentum worth keeping" rule was never applied to the
            // one object most likely to be moving fast when somebody quits.
            GameObject player = New("Player");
            player.AddComponent<Rigidbody>();
            player.AddComponent<SaveableEntity>();

            new PlayerSaveService().Bind("profile-a", player, applyPosition: false);

            Assert.IsNotNull(player.GetComponent<RigidbodySaveable>(),
                "Without this the player reloads at the right coordinates and completely still.");
        }

        [Test]
        public void Bind_AnnouncesThePlayerOnlyAfterRestoringIt()
        {
            // Ordering, and it is load-bearing. PlayerBound is what fires the world's deferred pass,
            // whose first act is to re-seat riders. Raised before the restore, a mount would put the
            // player in its seat and the SaveTeleport inside the restore would drag them back out.
            GameObject player = New("Player");
            player.AddComponent<SaveableEntity>();

            var service = new PlayerSaveService(new[]
            {
                new PlayerRecord { ProfileId = "profile-a", Position = new Vector3(5f, 0f, 5f) },
            });

            bool announcedBeforeMove = false;
            service.PlayerBound += (_, bound) =>
                announcedBeforeMove = bound.transform.position != new Vector3(5f, 0f, 5f);

            service.Bind("profile-a", player, applyPosition: true);

            Assert.IsFalse(announcedBeforeMove,
                "PlayerBound fired while the player was still at its spawn position, so anything " +
                "reacting to it sees a world that is about to be moved out from under it.");
        }

        [Test]
        public void Bind_AnnouncesAPlayerThatHasNoSavedRecord()
        {
            // A client joining a saved world for the first time. Keying the world's deferred pass off
            // a successful RESTORE rather than off a player EXISTING would leave every mount in that
            // world holding a rider reference nothing ever tries to resolve.
            GameObject player = New("Player");
            player.AddComponent<SaveableEntity>();

            var service = new PlayerSaveService();
            bool announced = false;
            service.PlayerBound += (_, _) => announced = true;

            bool restored = service.Bind("newcomer", player, applyPosition: false);

            Assert.IsFalse(restored, "A profile seen for the first time has nothing to restore.");
            Assert.IsTrue(announced, "...but it is still a player, and the world needs to know.");
        }

        [Test]
        public void SaveKeysAreUniqueAcrossTheNewSavers()
        {
            // Two savers on one entity sharing a key means the second silently overwrites the first in
            // the state bag, and only one of them ever restores.
            var keys = new List<string>
            {
                MountSaveable.Key,
                AgentStateSaveable.Key,
                EntityInventorySaveable.Key,
                ArticulatedPartsSaveable.Key,
                DuneFoilSaveable.Key,
                OrnithopterSaveable.Key,
                HealthSaveable.Key,
                RigidbodySaveable.Key,
            };

            CollectionAssert.AllItemsAreUnique(keys);
        }
    }
}
