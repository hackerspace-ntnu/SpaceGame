// Assets/Game/Editor/Tests/EntityBodyEquipmentPersistenceTests.cs
// What an NPC wears survives a save, and comes off with the bag when it dies — once.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Ragdoll;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Tests
{
    public class EntityBodyEquipmentPersistenceTests
    {
        // Far from the open scene, so its terrain is never the ground under these bodies.
        private static readonly Vector3 Far = new(150000f, 6000f, 150000f);
        private const float KillHeight = 200f;

        // A stand-in for the NPC craft. NESTED: a top-level MonoBehaviour in an editor assembly cannot be added.
        private sealed class TestAirborneCarrier : MonoBehaviour, IAirborneCarrier { }

        private readonly List<Object> junk = new();
        private IWorldService previousWorld;
        private IItemDropService previousDrops;
        private InstantiatingWorld world;

        [SetUp]
        public void SetUp()
        {
            previousWorld = GameServices.World;
            previousDrops = GameServices.ItemDropService;
            world = new InstantiatingWorld(junk);
            GameServices.World = world;
            GameServices.ItemDropService = new PlayerDropService();
            // A restore reads ids back through the item registry, which RegistryLoader fills only in play.
            Registry<InventoryItem>.Register(Pack);
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.World = previousWorld;
            GameServices.ItemDropService = previousDrops;
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private static InventoryItem Pack => EntityBodyEquipmentTests.Asset<InventoryItem>(EntityBodyEquipmentTests.WingPackPath);

        [Test]
        public void WornGear_RoundTripsThroughTheSave()
        {
            GameObject a = EntityBodyEquipmentTests.Npc(junk);
            a.GetComponent<EntityBodyEquipment>().TryWear(Pack);
            var saverA = a.AddComponent<EntityBodyEquipmentSaveable>();
            JObject saved = JObject.FromObject(saverA.CaptureState(), SaveSerializer.Serializer);

            GameObject b = EntityBodyEquipmentTests.Npc(junk);
            b.AddComponent<EntityBodyEquipmentSaveable>().RestoreState(saved);

            Assert.AreEqual(EntityBodyEquipmentSaveable.Key, saverA.SaveKey);
            Assert.AreEqual(new JArray(Pack.ID, string.Empty, string.Empty).ToString(), saved["items"]?.ToString(),
                            "the save JSON does not carry the worn ids, positional by BodySlot");
            Assert.AreEqual(Pack, b.GetComponent<EntityBodyEquipment>().ItemIn(BodySlot.Torso));
        }

        [Test]
        public void AnEmptySave_StripsTheBody()
        {
            GameObject a = EntityBodyEquipmentTests.Npc(junk);
            JObject saved = JObject.FromObject(a.AddComponent<EntityBodyEquipmentSaveable>().CaptureState(), SaveSerializer.Serializer);

            GameObject b = EntityBodyEquipmentTests.Npc(junk);
            b.GetComponent<EntityBodyEquipment>().TryWear(Pack);
            b.AddComponent<EntityBodyEquipmentSaveable>().RestoreState(saved);

            Assert.IsNull(b.GetComponent<EntityBodyEquipment>().ItemIn(BodySlot.Torso));
        }

        [Test]
        public void ARecordWithNoItems_WarnsAndLeavesTheBodyDressed()
        {
            GameObject npc = EntityBodyEquipmentTests.Npc(junk);
            npc.GetComponent<EntityBodyEquipment>().TryWear(Pack);

            LogAssert.Expect(LogType.Warning, new Regex("no items array"));
            npc.AddComponent<EntityBodyEquipmentSaveable>().RestoreState(new JObject());

            Assert.AreEqual(Pack, npc.GetComponent<EntityBodyEquipment>().ItemIn(BodySlot.Torso));
        }

        [Test]
        public void ThePolicy_GivesAWearerItsSaver()
        {
            GameObject npc = EntityBodyEquipmentTests.Npc(junk);
            npc.AddComponent<HealthComponent>();
            SaveablePolicy.Ensure(npc, out _);
            Assert.IsNotNull(npc.GetComponent<EntityBodyEquipmentSaveable>());
        }

        [Test]
        public void ADeadWearer_DropsItsPackOnce_AndARestoredCorpseDropsNothing()
        {
            GameObject npc = EntityBodyEquipmentTests.Npc(junk);
            var health = npc.AddComponent<HealthComponent>();
            var loot = npc.AddComponent<EntityLootTable>();
            foreach (string m in new[] { "Awake", "OnEnable" })
                typeof(EntityLootTable).GetMethod(m, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(loot, null);
            var body = npc.GetComponent<EntityBodyEquipment>();
            body.TryWear(Pack);

            health.Damage(999);
            int drops = world.Spawned.Count(go => go.name.StartsWith(Pack.itemPrefab.name));
            Assert.AreEqual(1, drops, "the wing pack did not drop exactly once");
            Assert.IsNull(body.ItemIn(BodySlot.Torso), "the corpse still wears the pack it dropped");

            body.TryWear(Pack);                // as a restore would put it back on the corpse
            health.RestoreHealth(0);           // a load restoring the body at zero health raises OnDeath
            Assert.AreEqual(1, world.Spawned.Count(go => go.name.StartsWith(Pack.itemPrefab.name)),
                            "a reload dropped the pack a second time");
        }

        /// <summary>
        /// What a dead wearer sheds is a pickup like any other: a player's interact takes it into a hotbar.
        /// The wing pack once lay beside every dead Sky nomad answering "RMB: pick up" and then refusing,
        /// because its prefab's PickupableItem named no item — TryAddItem(null) — with a clean console.
        /// </summary>
        [Test]
        public void ADeadWearer_DropsGearAPlayerCanPickUp()
        {
            InventoryItem repulsor = EntityBodyEquipmentTests.Asset<InventoryItem>(EntityBodyEquipmentTests.RepulsorPath);
            GameObject npc = EntityBodyEquipmentTests.Npc(junk);
            var health = npc.AddComponent<HealthComponent>();
            var loot = npc.AddComponent<EntityLootTable>();
            Invoke(loot, "Awake");
            Invoke(loot, "OnEnable");
            var body = npc.GetComponent<EntityBodyEquipment>();
            Assert.IsTrue(body.TryWear(Pack));
            Assert.IsTrue(body.TryWear(repulsor));

            health.Damage(999);

            foreach (InventoryItem worn in new[] { Pack, repulsor })
            {
                GameObject dropped = world.Spawned.Single(go => go.name.StartsWith(worn.itemPrefab.name));
                var pickup = dropped.GetComponent<PickupableItem>();
                Assert.IsNotNull(pickup, $"the dropped {worn.itemName} has no PickupableItem");
                Assert.IsTrue(pickup.isActiveAndEnabled, $"the dropped {worn.itemName} is not an active pickup");
                Assert.AreEqual(worn, pickup.Item, $"the dropped {worn.itemName} would hand a player the wrong item");
                Assert.IsTrue(dropped.GetComponentsInChildren<Collider>().Any(c => c.enabled && !c.isTrigger),
                              $"the dropped {worn.itemName} has no solid collider for the interact ray to hit");
            }
        }

        private int PacksDropped => world.Spawned.Count(go => go.name.StartsWith(Pack.itemPrefab.name));

        private static void Invoke(Component c, string method) =>
            c.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(c, null);

        /// <summary>A pack-wearing pilot seated in a craft KillHeight above a ground slab at Far.</summary>
        /// <param name="seatLetsGoFirst">
        /// The seat's death handler runs before the loot table's and unparents the body, as it would if
        /// VesselSeats had subscribed first.
        /// </param>
        private (GameObject npc, HealthComponent health) PilotAloft(bool seatLetsGoFirst = false)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            junk.Add(ground);
            ground.transform.position = Far + Vector3.down * 0.5f;
            ground.transform.localScale = new Vector3(100f, 1f, 100f);

            var craft = new GameObject("Craft", typeof(TestAirborneCarrier));
            junk.Add(craft);
            craft.transform.position = Far + Vector3.up * KillHeight;

            GameObject npc = EntityBodyEquipmentTests.Npc(junk);
            npc.transform.SetParent(craft.transform, false);
            var health = npc.AddComponent<HealthComponent>();
            var loot = npc.AddComponent<EntityLootTable>();
            Invoke(loot, "Awake");
            if (seatLetsGoFirst)
            {
                health.OnDeath += () =>
                {
                    npc.transform.SetParent(null, true);
                    Invoke(loot, "OnTransformParentChanged");   // EditMode sends no transform messages
                };
            }
            Invoke(loot, "OnEnable");
            npc.GetComponent<EntityBodyEquipment>().TryWear(Pack);
            Physics.SyncTransforms();
            return (npc, health);
        }

        [Test]
        public void APilotKilledAloft_StillWaits_WhenItsSeatLetsGoBeforeTheLootTableHears()
        {
            (GameObject npc, HealthComponent health) = PilotAloft(seatLetsGoFirst: true);

            health.Damage(999);

            Assert.AreEqual(0, PacksDropped, "a body already let go by its seat dropped its pack at the kill point");
            Assert.IsNotNull(npc.GetComponent<LootAwaitingGround>(), "the drop is not waiting for the ground");
        }

        [Test]
        public void APilotSetDownAlive_DropsAtOnceWhereItLaterDies()
        {
            (GameObject npc, HealthComponent health) = PilotAloft();
            npc.transform.SetParent(null, true);
            Invoke(npc.GetComponent<EntityLootTable>(), "OnTransformParentChanged");   // EditMode sends no transform messages

            health.Damage(999);

            Assert.AreEqual(1, PacksDropped, "a pilot that had landed alive held its pack as if killed aloft");
            Assert.IsNull(npc.GetComponent<LootAwaitingGround>());
        }

        [Test]
        public void ACorpseThatNeverStopsMoving_StillDropsOnceItIsDown()
        {
            (GameObject npc, HealthComponent health) = PilotAloft();
            // A limp corpse past its limp ceiling, whose bones never read slow: the drifting Sky City deck, a dune slope.
            var rig = npc.AddComponent<RagdollRig>();
            typeof(RagdollRig).GetProperty(nameof(RagdollRig.IsLimp)).GetSetMethod(true).Invoke(rig, new object[] { true });
            rig.IsCorpse = true;
            typeof(RagdollRig).GetField("limpSeconds", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(rig, 1000f);

            health.Damage(999);
            FallTo(npc, KillHeight * 0.5f);
            Assert.AreEqual(0, PacksDropped, "a settled ragdoll dropped its pack while still in the air");

            FallTo(npc, 0.1f);
            Assert.AreEqual(1, PacksDropped, "a corpse that never stops moving held its pack after it was down");
        }

        /// <summary>VesselSeats lets the body go; it falls to <paramref name="height"/> above the slab.</summary>
        private static void FallTo(GameObject npc, float height)
        {
            npc.transform.SetParent(null, true);
            npc.transform.position = Far + Vector3.up * height;
            Physics.SyncTransforms();
            if (npc.TryGetComponent(out LootAwaitingGround waiting)) Invoke(waiting, "Update");
        }

        [Test]
        public void APilotKilledAloft_DropsOnlyOnceItsBodyIsDown_AtTheBody()
        {
            (GameObject npc, HealthComponent health) = PilotAloft();

            health.Damage(999);
            Assert.AreEqual(0, PacksDropped, "the pack was dropped in mid-air, at the kill point");

            FallTo(npc, KillHeight * 0.5f);
            Assert.AreEqual(0, PacksDropped, "the pack was dropped while the body was still falling");

            FallTo(npc, 0.1f);
            Assert.AreEqual(1, PacksDropped, "the pack was not dropped when the body came down");
            GameObject pack = world.Spawned.First(go => go.name.StartsWith(Pack.itemPrefab.name));
            Assert.Less(pack.transform.position.y - Far.y, 3f, "the pack was dropped above the ground");
            Assert.Less(Vector2.Distance(new Vector2(pack.transform.position.x, pack.transform.position.z),
                                         new Vector2(Far.x, Far.z)), 3f, "the pack is not beside the body");
            Assert.IsNull(npc.GetComponent<EntityBodyEquipment>().ItemIn(BodySlot.Torso));

            FallTo(npc, 0.1f);
            Assert.AreEqual(1, PacksDropped, "the pack was dropped a second time");
        }

        [Test]
        public void APilotDespawnedBeforeItLands_DropsOnTheGroundBelow_NeverAloft()
        {
            (GameObject npc, HealthComponent health) = PilotAloft();
            health.Damage(999);
            FallTo(npc, KillHeight * 0.5f);

            Invoke(npc.GetComponent<LootAwaitingGround>(), "OnDisable");

            Assert.AreEqual(1, PacksDropped, "a body despawned in the fall took its pack with it");
            GameObject pack = world.Spawned.First(go => go.name.StartsWith(Pack.itemPrefab.name));
            Assert.Less(pack.transform.position.y - Far.y, 3f, "the pack was dropped at altitude");
        }

        [Test]
        public void AWaiterDisabledBeforeItsDropWasHandedOver_StillAnswersWhoeverWaits()
        {
            // WhenDropped made the waiter ahead of the loot table's Begin, and the body went first.
            var body = new GameObject("Corpse");
            junk.Add(body);
            var waiting = body.AddComponent<LootAwaitingGround>();
            bool answered = false;
            LootAwaitingGround.WhenDropped(body, () => answered = true);
            Assume.That(answered, Is.False);

            Invoke(waiting, "OnDisable");

            Assert.IsTrue(answered, "a save hold waiting on this corpse would never be released");
        }
    }
}
