// Assets/Game/Editor/Tests/NpcFlightModuleTests.cs
// When a Sky nomad flies: a goal too far to walk and a wing pack on its back (D2), never in a fight (D2),
// alone to the shared goal (D5); a nomad that finds itself falling deploys, fight or not; a flier is kept
// out of the save (D4) without ever pulling a group member into it, and a corpse stays out until its loot
// is down. "Out of the save" is checked on what the real WorldSaveStore captures.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Vehicles;
using SpaceGame.World;

namespace SpaceGame.Tests
{
    public class NpcFlightModuleTests
    {
        private static readonly Vector3 FarAway = new Vector3(150000f, 6000f, 150000f);
        private readonly List<Object> junk = new();
        private readonly List<string> sites = new();
        private IWorldService previousWorld;
        private IItemDropService previousDrops;
        private InstantiatingWorld world;
        private GameObject nomad;
        private NpcFlightModule flight;
        private AgentGoal goal;
        private WorldSaveStore store;

        [SetUp]
        public void SetUp()
        {
            previousWorld = GameServices.World;
            world = new InstantiatingWorld(junk);
            GameServices.World = world;
            previousDrops = GameServices.ItemDropService;
            GameServices.ItemDropService = new PlayerDropService();

            GameObject craftPrefab = NpcAviatorTests.Craft(junk);
            craftPrefab.transform.position = FarAway + Vector3.down * 3000f;   // out of the way; only copied

            nomad = EntityBodyEquipmentTests.Npc(junk);
            nomad.transform.position = FarAway;
            nomad.AddComponent<HealthComponent>();
            nomad.AddComponent<AgentController>();
            goal = nomad.AddComponent<AgentGoal>();
            nomad.GetComponent<EntityBodyEquipment>().TryWear(EntityBodyEquipmentTests.Asset<InventoryItem>(EntityBodyEquipmentTests.WingPackPath));
            flight = nomad.AddComponent<NpcFlightModule>();
            var so = new SerializedObject(flight);
            so.FindProperty("craftPrefab").objectReferenceValue = craftPrefab;
            so.FindProperty("sortieChance").floatValue = 1f;
            so.ApplyModifiedPropertiesWithoutUndo();

            // The hold's store is SaveManager's in play; a test hands it its own.
            store = new WorldSaveStore();
            typeof(NpcFlightModule).GetField("saveHold", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(flight, new SaveScopeHold(() => store));
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.World = previousWorld;
            GameServices.ItemDropService = previousDrops;
            foreach (string id in sites) WorldSiteRegistry.Unregister(id);
            sites.Clear();
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private AgentContext Context(AgentTargeting targeting = null) => new AgentContext
        {
            Self = nomad.transform, Position = nomad.transform.position, Goal = goal, Targeting = targeting,
        };

        private void Ground()
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            junk.Add(block);
            block.transform.position = FarAway + Vector3.down * 0.5f;
            block.transform.localScale = new Vector3(40f, 1f, 40f);
            Physics.SyncTransforms();
        }

        private static void Invoke(Component c, string method) =>
            c.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(c, null);

        private bool KeepsNomadOutOfSave =>
            ((SaveScopeHold)typeof(NpcFlightModule).GetField("saveHold", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(flight)).Held;

        private void FlyFar()
        {
            goal.Set(FarAway + Vector3.right * 2000f, 10f);
            flight.Tick(Context(), 0.02f);
            Assume.That(flight.InFlight, "the fixture's nomad never took off");
        }

        private void Land()
        {
            NpcAviator aviator = flight.Aviator;
            typeof(NpcAviator).GetMethod("Touchdown", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(aviator, new object[] { aviator.transform.position, 0f });
        }

        private AgentTargeting InAFight()
        {
            var enemy = new GameObject("Enemy");
            junk.Add(enemy);
            enemy.transform.position = FarAway + Vector3.forward * 10f;
            enemy.AddComponent<HealthComponent>();
            var targeting = nomad.AddComponent<AgentTargeting>();
            targeting.ForceTarget(enemy.transform);
            Assume.That(targeting.Target, Is.Not.Null, "the fixture could not give the nomad a target");
            return targeting;
        }

        private SaveableEntity SavedNomad(SaveScope scope)
        {
            var entity = nomad.AddComponent<SaveableEntity>();
            var so = new SerializedObject(entity);
            so.FindProperty("scope").enumValueIndex = (int)scope;
            so.ApplyModifiedPropertiesWithoutUndo();
            entity.AdoptIdentity("npc-flight-test-nomad", "npc-flight-test-" + scope);
            return entity;
        }

        /// <summary>
        /// Whether a save taken now holds a record for <paramref name="entity"/>: its root captured by
        /// <paramref name="into"/>'s real Dehydrate from a preview scene, then put back where it was.
        /// </summary>
        internal static bool InSave(WorldSaveStore into, SaveableEntity entity)
        {
            GameObject root = entity.transform.root.gameObject;
            Scene home = root.scene;
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                SceneManager.MoveGameObjectToScene(root, preview);
                into.Dehydrate("npc-flight-test", preview);
            }
            finally
            {
                SceneManager.MoveGameObjectToScene(root, home);
                EditorSceneManager.ClosePreviewScene(preview);
            }
            return into.Record.Entities.ContainsKey(entity.InstanceId);
        }

        [Test]
        public void AGoalTooFarToWalk_DeploysTheCraft_AndSeatsTheNomad()
        {
            goal.Set(FarAway + Vector3.right * 2000f, 10f);

            MoveIntent? intent = flight.Tick(Context(), 0.02f);

            Assert.IsTrue(intent.HasValue, "the deploy frame should be claimed");
            Assert.AreEqual(1, world.Spawned.Count, "no craft was deployed");
            Assert.AreEqual(nomad, world.Spawned[0].GetComponent<NpcAviator>().Pilot);
            Assert.IsTrue(flight.InFlight);
        }

        [Test]
        public void AGoalWithinWalkingDistance_IsWalked()
        {
            Ground();
            goal.Set(FarAway + Vector3.right * 50f, 5f);

            Assert.IsFalse(flight.Tick(Context(), 0.02f).HasValue);
            Assert.IsEmpty(world.Spawned);
        }

        [Test]
        public void WithoutAWingPack_ItWalks()
        {
            nomad.GetComponent<EntityBodyEquipment>().Remove(BodySlot.Torso);
            goal.Set(FarAway + Vector3.right * 2000f, 10f);

            Assert.IsFalse(flight.Tick(Context(), 0.02f).HasValue);
            Assert.IsEmpty(world.Spawned);
        }

        [Test]
        public void InAFight_ItDoesNotTakeOff()
        {
            Ground();
            AgentTargeting targeting = InAFight();
            goal.Set(FarAway + Vector3.right * 2000f, 10f);

            Assert.IsFalse(flight.Tick(Context(targeting), 0.02f).HasValue, "took off when threatened (D2 says it fights)");
            Assert.IsEmpty(world.Spawned);
        }

        [Test]
        public void ANomadInMidAir_WithNowhereToGo_DeploysToLand()
        {
            Assert.IsTrue(flight.Tick(Context(), 1f).HasValue);
            Assert.AreEqual(1, world.Spawned.Count);
        }

        [Test]
        public void AMomentOffTheGround_IsNotAFall()
        {
            Assert.IsFalse(flight.Tick(Context(), 0.02f).HasValue, "a hop or a step off a ledge deployed the craft");
            Assert.IsEmpty(world.Spawned);
        }

        [Test]
        public void FallingDuringAFight_StillDeploys()
        {
            AgentTargeting targeting = InAFight();

            flight.Tick(Context(targeting), 0.02f);
            Assert.IsTrue(flight.Tick(Context(targeting), 1f).HasValue, "a nomad knocked off the city in a fight fell to its death");
            Assert.AreEqual(1, world.Spawned.Count);
        }

        [Test]
        public void AFollower_FliesAloneToItsLeadersGoal()
        {
            GameObject leader = EntityBodyEquipmentTests.Npc(junk);
            var leaderFormation = leader.AddComponent<FormationModule>();
            var leaderGoal = leader.AddComponent<AgentGoal>();
            leaderFormation.SetFormation("sky-wing-test", true);
            leaderGoal.Set(FarAway + Vector3.right * 2000f, 10f);
            var followerFormation = nomad.AddComponent<FormationModule>();
            followerFormation.SetFormation("sky-wing-test", false);
            // The formation registry is filled in OnEnable, which EditMode never runs.
            foreach (FormationModule f in new[] { leaderFormation, followerFormation })
                Invoke(f, "OnEnable");
            Assume.That(FormationModule.LeaderOf("sky-wing-test"), Is.EqualTo(leaderFormation),
                        "the formation registry did not take the fixture's leader in EditMode");
            Ground();

            flight.Tick(Context(), 0.02f);

            Assert.AreEqual(1, world.Spawned.Count, "the follower walked a 2 km leg");
            Assert.AreEqual(leaderGoal.Position, world.Spawned[0].GetComponent<NpcAviator>().Goal);
        }

        [Test]
        public void AfterLanding_ItWaitsBeforeFlyingAgain()
        {
            FlyFar();
            Land();

            Assert.IsFalse(flight.InFlight);
            Assert.IsFalse(flight.Tick(Context(), 0.02f).HasValue, "re-launched straight after landing");
            Assert.AreEqual(1, world.Spawned.Count);
        }

        [Test]
        public void AFlier_IsOutOfTheSave_AndBackInOnceLanded()
        {
            SaveableEntity flier = SavedNomad(SaveScope.World);
            Assume.That(InSave(store, flier), "the fixture's nomad was not saved before its flight");

            FlyFar();
            Assert.IsFalse(InSave(store, flier),
                           "a save taken mid-flight still holds the flier: the reload puts it back where it took off (D4)");

            Land();
            Assert.IsFalse(KeepsNomadOutOfSave);
            Assert.IsTrue(InSave(store, flier), "a landed flier is missing from the save");
        }

        [Test]
        public void AGroupMembersFlight_NeverPutsItInTheWorldSave()
        {
            SaveableEntity member = SavedNomad(SaveScope.External);
            FlyFar();
            Assert.IsFalse(KeepsNomadOutOfSave, "a group member's flight touched the world save");

            Land();

            Assert.IsFalse(InSave(store, member), "landing put a group member in the world save: its group record brings it back too");
        }

        [Test]
        public void APilotKilledInFlight_IsSavedAgainAsACorpse()
        {
            SaveableEntity entity = SavedNomad(SaveScope.World);
            FlyFar();

            nomad.GetComponent<HealthComponent>().Damage(999);

            Assert.IsFalse(flight.InFlight);
            Assert.IsFalse(KeepsNomadOutOfSave, "a corpse with no loot to wait for was kept out of the save");
            Assert.IsTrue(InSave(store, entity), "the corpse was left out of the save");
        }

        /// <summary>A world-saved pilot whose loot table holds its drop for the ground; flying, killed aloft.</summary>
        /// <param name="lootTableHearsFirst">The loot table subscribed to OnDeath at spawn, before the flight did.</param>
        private SaveableEntity PilotKilledAloft(bool lootTableHearsFirst)
        {
            SaveableEntity entity = SavedNomad(SaveScope.World);
            var loot = nomad.AddComponent<EntityLootTable>();
            Invoke(loot, "Awake");
            if (lootTableHearsFirst) Invoke(loot, "OnEnable");
            Ground();
            FlyFar();
            if (!lootTableHearsFirst) Invoke(loot, "OnEnable");
            Invoke(loot, "OnTransformParentChanged");        // EditMode sends no transform messages

            nomad.GetComponent<HealthComponent>().Damage(999);
            return entity;
        }

        private void CorpseComesDown()
        {
            nomad.transform.SetParent(null, true);
            nomad.transform.position = FarAway + Vector3.up * 0.1f;
            Physics.SyncTransforms();
            Invoke(nomad.GetComponent<LootAwaitingGround>(), "Update");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void APilotKilledInFlight_StaysOutOfTheSave_UntilItsLootIsDown(bool lootTableHearsFirst)
        {
            SaveableEntity entity = PilotKilledAloft(lootTableHearsFirst);
            Assume.That(nomad.GetComponent<LootAwaitingGround>(), Is.Not.Null, "the loot is not waiting for the ground");

            Assert.IsTrue(KeepsNomadOutOfSave,
                          "a corpse whose pack is still falling was given back to the save: a reload keeps the pack on it for good");
            Assert.IsFalse(InSave(store, entity), "a save taken during the fall holds the corpse");

            CorpseComesDown();

            Assert.IsFalse(KeepsNomadOutOfSave, "the corpse was not given back once its loot was down");
            Assert.IsTrue(InSave(store, entity), "the corpse is missing from the save");
        }

        private void SkyCityWithARuin(out Vector3 ruin)
        {
            sites.Add(WorldSiteRegistry.Register(SiteKind.Home, FarAway, 100f, WorldSite.SkyCityName, airborne: true));
            ruin = FarAway + new Vector3(1200f, -6000f, 0f);
            sites.Add(WorldSiteRegistry.Register(SiteKind.Ruin, ruin, 20f, "Test Ruin"));
            var so = new SerializedObject(flight);
            so.FindProperty("sortieTask").FindPropertyRelative("targetSite").enumValueIndex = (int)SiteKind.Ruin;
            so.FindProperty("sortieTask").FindPropertyRelative("searchRadius").floatValue = 10000f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void OnTheSkyCity_ASortiePicksAGroundSite_AndFlies()
        {
            SkyCityWithARuin(out Vector3 ruin);

            flight.Tick(Context(), 0.02f);

            Assert.IsTrue(flight.OnSortie);
            Assert.Less(Vector2.Distance(new Vector2(goal.Position.x, goal.Position.z), new Vector2(ruin.x, ruin.z)), 25f);
            Assert.AreEqual(1, world.Spawned.Count);
        }

        [Test]
        public void OnTheSkyCity_AResidentWithSomewhereToBe_IsNotSentOnASortie()
        {
            SkyCityWithARuin(out _);
            Ground();
            Vector3 errand = FarAway + Vector3.right * 20f;
            goal.Set(errand, 2f);

            flight.Tick(Context(), 0.02f);

            Assert.IsFalse(flight.OnSortie);
            Assert.AreEqual(errand, goal.Position, "a sortie overwrote the resident's own errand");
            Assert.IsEmpty(world.Spawned);
        }

        [Test]
        public void ASortieWithNoRoomToLaunch_LeavesTheResidentHome()
        {
            SkyCityWithARuin(out _);
            Ground();
            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            junk.Add(roof);
            roof.transform.position = FarAway + Vector3.up * 8f;
            roof.transform.localScale = new Vector3(6f, 1f, 6f);
            Physics.SyncTransforms();

            flight.Tick(Context(), 0.02f);

            Assert.IsEmpty(world.Spawned, "launched through a roof");
            Assert.IsFalse(flight.OnSortie, "counted as on a sortie without ever taking off");
            Assert.IsFalse(goal.HasGoal, "sent toward a ground site it cannot walk to");
        }
    }

    public class SaveScopeHoldTests
    {
        private readonly List<Object> junk = new();
        private WorldSaveStore store;

        [SetUp]
        public void SetUp() => store = new WorldSaveStore();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private SaveableEntity Entity(SaveScope scope)
        {
            var go = new GameObject("Flier");
            junk.Add(go);
            var entity = go.AddComponent<SaveableEntity>();
            var so = new SerializedObject(entity);
            so.FindProperty("scope").enumValueIndex = (int)scope;
            so.ApplyModifiedPropertiesWithoutUndo();
            entity.AdoptIdentity("save-scope-hold-test", "save-scope-hold-test-" + scope);
            return entity;
        }

        [Test]
        public void AGroupMember_IsNeverReclaimedIntoTheWorldSave()
        {
            SaveableEntity member = Entity(SaveScope.External);
            var hold = new SaveScopeHold(() => store);

            hold.Hold(member.gameObject);
            hold.Release();

            Assert.IsFalse(hold.Held);
            Assert.IsFalse(member.BelongsToWorld);
            Assert.IsFalse(NpcFlightModuleTests.InSave(store, member), "landing put a group member into the world save: it would load twice");
        }

        [Test]
        public void AWorldFlier_LeavesTheSave_AndComesBackOnRelease()
        {
            SaveableEntity flier = Entity(SaveScope.World);
            var hold = new SaveScopeHold(() => store);
            Assume.That(NpcFlightModuleTests.InSave(store, flier), "the fixture was not saved before the hold");

            hold.Hold(flier.gameObject);
            Assert.IsTrue(hold.Held);
            Assert.IsFalse(NpcFlightModuleTests.InSave(store, flier), "the record written before the hold survived it");

            hold.Release();
            Assert.IsFalse(hold.Held);
            Assert.IsTrue(NpcFlightModuleTests.InSave(store, flier));
        }

        [Test]
        public void AnAuthoredFlier_IsTombstoned_WhileHeld()
        {
            SaveableEntity flier = Entity(SaveScope.World);
            flier.AdoptAuthoredIdentity("save-scope-hold-test-authored");
            var hold = new SaveScopeHold(() => store);
            Assume.That(NpcFlightModuleTests.InSave(store, flier), "the fixture was not saved before the hold");

            hold.Hold(flier.gameObject);
            Assert.IsFalse(NpcFlightModuleTests.InSave(store, flier));
            Assert.IsTrue(store.Record.IsDestroyed(flier.InstanceId), "a load would put an authored flier back at its authored spot");

            hold.Release();
            Assert.IsFalse(store.Record.IsDestroyed(flier.InstanceId), "a landed authored flier is deleted on the next load");
            Assert.IsTrue(NpcFlightModuleTests.InSave(store, flier));
        }
    }
}
