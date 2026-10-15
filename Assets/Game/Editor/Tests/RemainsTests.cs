// What an NPC leaves behind when it dies: its body, lying where it fell, and the loot it shed beside
// it. Both stay for a while (the user's call, 2026-10-05: "let the body lay there, and the loot,
// then let it despawn after a little while") and are then taken away by the server -- only once no
// player is close enough to watch them vanish. An item a PLAYER drops never expires, and loot a
// player picks up is theirs. The countdown is saved, so a save/quit/load pauses it.
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Tests
{
    public class RemainsTests
    {
        private const string ItemPath = "Assets/Game/Resources/Items/Artifacts/basicgun.asset";
        private const float CorpseLifetime = 120f;
        private const float LootLifetime = 90f;

        private IWorldService previousWorld;
        private IItemDropService previousDrops;
        private WorldSpy world;
        private readonly System.Collections.Generic.List<Object> junk = new();

        [SetUp]
        public void SetUp()
        {
            previousWorld = GameServices.World;
            previousDrops = GameServices.ItemDropService;
            world = new WorldSpy(junk);
            GameServices.World = world;
            GameServices.ItemDropService = new PlayerDropService();
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.World = previousWorld;
            GameServices.ItemDropService = previousDrops;
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        // ── Fixtures ─────────────────────────────────────────────────────────

        private static void Wake(Component c)
        {
            foreach (string message in new[] { "Awake", "OnEnable" })
                c.GetType().GetMethod(message, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(c, null);
        }

        private static void Set(Object target, string field, object value)
        {
            var so = new UnityEditor.SerializedObject(target);
            Find(so, field).floatValue = (float)value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static UnityEditor.SerializedProperty Find(UnityEditor.SerializedObject so, string field)
        {
            UnityEditor.SerializedProperty property = so.FindProperty(field);
            Assert.IsNotNull(property, $"no serialized field '{field}'");
            return property;
        }

        /// A creature with a body reaction and, optionally, a gun in its bag and a loot table.
        private GameObject Creature(float corpseLifetime = CorpseLifetime, bool armed = false)
        {
            var go = new GameObject("Creature");
            junk.Add(go);
            go.AddComponent<HealthComponent>();

            EntityInventoryComponent bag = null;
            if (armed) bag = go.AddComponent<EntityInventoryComponent>();

            var reaction = go.AddComponent<HealthReactionModule>();
            Set(reaction, "corpseLifetime", corpseLifetime);

            if (armed)
            {
                var loot = go.AddComponent<EntityLootTable>();
                Set(loot, "lootLifetime", LootLifetime);
                Wake(bag);
                bag.TryAddItem(UnityEditor.AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath));
                Wake(reaction);
                Wake(loot);
            }
            else
            {
                Wake(reaction);
            }

            return go;
        }

        private static void Kill(GameObject go) => go.GetComponent<HealthComponent>().Damage(999);

        // ── The body ────────────────────────────────────────────────────────

        [Test]
        public void ADeadBody_LiesWhereItFell_AndCountsDownItsLifetime()
        {
            GameObject body = Creature();
            Kill(body);

            Assert.IsTrue(body.activeSelf, "the body blinked out instead of lying there");
            var remains = body.GetComponent<Remains>();
            Assert.IsNotNull(remains, "nothing will ever take the body away");
            Assert.IsTrue(remains.Counting);
            Assert.AreEqual(CorpseLifetime, remains.Remaining, 1e-4f);
        }

        [Test]
        public void ABody_IsTakenAwayByTheWorld_OnlyOnceItsLifetimeIsUp()
        {
            GameObject body = Creature();
            Kill(body);
            var remains = body.GetComponent<Remains>();

            remains.Tick(CorpseLifetime - 1f);
            Assert.AreEqual(0, world.Despawned.Count, "taken away before its time");

            remains.Tick(2f);
            CollectionAssert.AreEqual(new[] { body }, world.Despawned,
                "the body must leave through the world service, which despawns it on every machine");
        }

        [TestCase(0f, false, ExpectedResult = true)]
        [TestCase(0f, true, ExpectedResult = false)]
        [TestCase(1f, false, ExpectedResult = false)]
        public bool TakenAway_OnlyWhenDue_AndNobodyIsWatching(float remaining, bool playerNear) =>
            UnseenRemoval.ShouldTakeAway(remaining, playerNear);

        [Test]
        public void ARevivedBody_StopsCountingDown()
        {
            GameObject body = Creature();
            Kill(body);

            body.GetComponent<HealthComponent>().RestoreHealth(50);

            Assert.IsFalse(body.GetComponent<Remains>().Counting, "a living creature would be taken away");
        }

        [Test]
        public void ABodyWithNoLifetime_StaysForGood()
        {
            // A Strider monowheel's wreck: AbandonedVehicle decides when it goes, not the corpse rule.
            GameObject wreck = Creature(corpseLifetime: 0f);
            Kill(wreck);

            Assert.IsTrue(wreck.activeSelf);
            Assert.IsNull(wreck.GetComponent<Remains>());
        }

        [Test]
        public void ADeadEntity_IsNobodysTarget_UntilItIsRevived()
        {
            GameObject body = Creature();
            var faction = body.AddComponent<EntityFaction>();
            Wake(faction);
            Assume.That(EntityTargetRegistry.All, Has.Member(faction));

            Kill(body);
            Assert.That(EntityTargetRegistry.All, Has.No.Member(faction),
                "a corpse lying for minutes would be counted by its town and raise alarms");

            body.GetComponent<HealthComponent>().RestoreHealth(50);
            Assert.That(EntityTargetRegistry.All, Has.Member(faction));

            typeof(EntityFaction).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(faction, null);
        }

        // ── Save and load ───────────────────────────────────────────────────

        [Test]
        public void ASavedBody_ComesBackWithTheLifetimeItHadLeft_WhicheverSaverLoadsFirst()
        {
            GameObject body = Creature();
            body.AddComponent<RemainsSaveable>();
            Kill(body);
            body.GetComponent<Remains>().Tick(CorpseLifetime - 30f);

            object captured = body.GetComponent<RemainsSaveable>().CaptureState();
            Assert.IsNotNull(captured, "a save mid-countdown loses the countdown");
            JObject json = JObject.FromObject(captured, SaveSerializer.Serializer);

            // Health first: the restored death starts a full countdown, the saver then corrects it.
            GameObject healthFirst = Creature();
            healthFirst.GetComponent<HealthComponent>().RestoreHealth(0);
            healthFirst.AddComponent<RemainsSaveable>().RestoreState(json);
            Assert.AreEqual(30f, healthFirst.GetComponent<Remains>().Remaining, 1e-3f);

            // Saver first: the restored death must not restart what the saver put back.
            GameObject saverFirst = Creature();
            saverFirst.AddComponent<RemainsSaveable>().RestoreState(json);
            saverFirst.GetComponent<HealthComponent>().RestoreHealth(0);
            Assert.AreEqual(30f, saverFirst.GetComponent<Remains>().Remaining, 1e-3f);
            Assert.IsTrue(saverFirst.activeSelf, "a restored corpse is hidden instead of lying there");
        }

        [Test]
        public void ABodyFromAnOlderSave_StillGoesAway()
        {
            GameObject body = Creature();
            body.GetComponent<HealthComponent>().RestoreHealth(0);

            Assert.IsTrue(body.GetComponent<Remains>().Counting, "a corpse with no saved countdown lies for ever");
        }

        [Test]
        public void NothingCounting_SavesNothing()
        {
            GameObject alive = Creature();
            Assert.IsNull(alive.AddComponent<RemainsSaveable>().CaptureState());
        }

        [Test]
        public void ThePolicy_GivesAnythingThatCanBecomeRemains_ItsSaver()
        {
            GameObject creature = Creature();
            SaveablePolicy.Ensure(creature, out _);

            Assert.IsNotNull(creature.GetComponent<RemainsSaveable>(),
                "a load is handed a countdown no saver exists to take");
        }

        // ── The loot ────────────────────────────────────────────────────────

        [Test]
        public void LootShedByADeadNpc_LiesBesideIt_AndCountsDown()
        {
            GameObject body = Creature(armed: true);
            Kill(body);

            Assert.AreEqual(1, world.Spawned.Count, "the gun was not dropped");
            var remains = world.Spawned[0].GetComponent<Remains>();
            Assert.IsNotNull(remains, "the dropped gun lies there for ever");
            Assert.IsTrue(remains.Counting);
            Assert.AreEqual(LootLifetime, remains.Remaining, 1e-4f);
        }

        [Test]
        public void ADeadNpcsBag_IsEmptiedByTheDrop()
        {
            // Otherwise the corpse lies there still holding the gun that is also on the sand beside it.
            GameObject body = Creature(armed: true);
            Kill(body);

            CollectionAssert.IsEmpty(body.GetComponent<EntityInventoryComponent>().GetAllItems());
        }

        [Test]
        public void AnItemAPlayerDrops_NeverExpires()
        {
            var hand = new GameObject("Hand");
            junk.Add(hand);
            hand.transform.position = Vector3.up * 2f;

            GameObject dropped = GameServices.ItemDropService.DropItem(
                hand.transform, UnityEditor.AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath));

            Assert.IsNotNull(dropped);
            Assert.IsNull(dropped.GetComponent<Remains>());
        }

        /// Spawns a bare object (no prefab: nothing here needs the item's own components) and records
        /// what it was asked to despawn instead of destroying it, which EditMode does not allow.
        private sealed class WorldSpy : IWorldService
        {
            public readonly System.Collections.Generic.List<GameObject> Spawned = new();
            public readonly System.Collections.Generic.List<GameObject> Despawned = new();
            private readonly System.Collections.Generic.List<Object> junk;

            public WorldSpy(System.Collections.Generic.List<Object> junk) => this.junk = junk;

            public void Despawn(GameObject gameObject) => Despawned.Add(gameObject);

            public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation,
                                    ulong ownerClientId = NetworkSpawn.NoOwner)
            {
                var go = new GameObject(prefab.name);
                go.transform.SetPositionAndRotation(position, rotation);
                junk.Add(go);
                Spawned.Add(go);
                return go;
            }
        }
    }
}
