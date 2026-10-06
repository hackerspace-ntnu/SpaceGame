// D9: an NPC fires a worn gauntlet only when the gauntlet's asset opts in, and fires it where it is
// aiming rather than along its own forearm.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    public class NpcGauntletUseTests
    {
        /// <summary>
        /// A gauntlet that records how it was used. Aims like a real gadget — through HolderAimRay — so
        /// the test proves an NPC's aim reaches the item. Nested: a test MonoBehaviour in its own file
        /// cannot be added as a component.
        /// </summary>
        private sealed class TestGauntletItem : ToolItem
        {
            public int Uses;
            public Vector3 LastDirection;

            public override void OnRequestUse(ref NetArg arg)
            {
                Ray aim = HolderAimRay();
                arg.P = aim.origin;
                arg.R = Quaternion.LookRotation(aim.direction);
            }

            protected override void Use()
            {
                Uses++;
                LastDirection = UseArg.R * Vector3.forward;
            }
        }

        private readonly List<Object> junk = new();
        private GameObject npc;
        private EntityBodyEquipment body;

        [SetUp]
        public void SetUp()
        {
            npc = EntityBodyEquipmentTests.Npc(junk);
            body = npc.GetComponent<EntityBodyEquipment>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private InventoryItem Gauntlet(bool npcUsable)
        {
            var prefab = new GameObject("TestGauntlet");
            junk.Add(prefab);
            prefab.SetActive(false);
            prefab.AddComponent<GauntletFit>();
            prefab.AddComponent<TestGauntletItem>();
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            junk.Add(item);
            item.itemName = "Test Gauntlet";
            item.itemPrefab = prefab;
            item.equipKind = EquipKind.Gauntlet;
            item.npcUsable = npcUsable;
            return item;
        }

        [Test]
        public void AGauntletThatDidNotOptIn_IsNotReady()
        {
            Assert.IsTrue(body.TryWear(Gauntlet(npcUsable: false), BodySlot.RightGauntlet));
            Assert.IsFalse(new WornGauntletUser(body, BodySlot.RightGauntlet).IsReady);
        }

        [Test]
        public void AnOptedInGauntlet_FiresAtWhereTheNpcAims()
        {
            Assert.IsTrue(body.TryWear(Gauntlet(npcUsable: true), BodySlot.RightGauntlet));
            var user = new WornGauntletUser(body, BodySlot.RightGauntlet);
            Assert.IsTrue(user.IsReady);

            Vector3 target = user.FireOrigin + new Vector3(10f, 0f, 10f);
            Assert.IsTrue(user.TryUseAt(target));

            var item = body.InstanceIn(BodySlot.RightGauntlet).GetComponent<TestGauntletItem>();
            Assert.AreEqual(1, item.Uses);
            Assert.Greater(Vector3.Dot(item.LastDirection, (target - user.FireOrigin).normalized), 0.99f,
                           "the gauntlet fired along its forearm instead of at the NPC's aim");
        }

        [Test]
        public void TheGauntletModule_IsAnItemUseModule_ThatNeverTakesTheFeet()
        {
            var module = npc.AddComponent<NpcGauntletUseModule>();
            Assert.IsInstanceOf<ItemUseModuleBase>(module, "AgentTargeting and CombatCadenceSaveable read ItemUseModuleBase");
            Assert.IsFalse(module.ClaimsMovement, "a gauntlet trigger must never take the NPC's feet");
        }

        // EditMode AddComponent runs no Awake; invoke it the way Unity would.
        private NpcGauntletUseModule Module(ItemUseModuleBase.Trigger trigger)
        {
            var module = npc.AddComponent<NpcGauntletUseModule>();
            var so = new SerializedObject(module);
            so.FindProperty("trigger").enumValueIndex = (int)trigger;
            so.ApplyModifiedPropertiesWithoutUndo();
            typeof(NpcGauntletUseModule).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(module, null);
            return module;
        }

        private TestGauntletItem WornItem() => body.InstanceIn(BodySlot.RightGauntlet).GetComponent<TestGauntletItem>();

        [Test]
        public void TheModule_RefusesAWornGauntletThatDidNotOptIn()
        {
            Assert.IsTrue(body.TryWear(Gauntlet(npcUsable: false), BodySlot.RightGauntlet));
            NpcGauntletUseModule module = Module(ItemUseModuleBase.Trigger.OnInterval);

            module.Tick(default, 1f);

            Assert.AreEqual(0, WornItem().Uses, "an NPC fired a gauntlet whose asset never opted in");
            Assert.IsFalse(body.TryUseWornAt(BodySlot.RightGauntlet, Vector3.forward * 10f),
                           "TryUseWornAt is public and must check the opt-in itself");
        }

        [Test]
        public void TheModule_FiresAnOptedInWornGauntlet()
        {
            Assert.IsTrue(body.TryWear(Gauntlet(npcUsable: true), BodySlot.RightGauntlet));
            NpcGauntletUseModule module = Module(ItemUseModuleBase.Trigger.OnInterval);

            module.Tick(default, 1f);

            Assert.AreEqual(1, WornItem().Uses);
        }

        [Test]
        public void TheModulesReach_CountsOnlyWhileAnOptedInGauntletIsWorn()
        {
            NpcGauntletUseModule module = Module(ItemUseModuleBase.Trigger.TargetInRange);
            int changes = 0;
            module.ReachChanged += () => changes++;
            Assert.AreEqual(0f, module.MaxRange, "a bare forearm widened the NPC's acquisition range");

            Assert.IsTrue(body.TryWear(Gauntlet(npcUsable: true), BodySlot.RightGauntlet));
            Assert.Greater(module.MaxRange, 0f);
            Assert.AreEqual(1, changes, "wearing the gauntlet must tell AgentTargeting to re-read its reach");

            body.Remove(BodySlot.RightGauntlet);
            Assert.AreEqual(0f, module.MaxRange);
            Assert.AreEqual(2, changes);
        }

        [Test]
        public void TheGauntletModulesCadence_IsSavedWithTheHandModules()
        {
            npc.AddComponent<NpcGauntletUseModule>();
            var saver = npc.AddComponent<CombatCadenceSaveable>();
            var state = (CombatCadenceSaveable.State)saver.CaptureState();
            Assert.AreEqual(1, state.itemUse.Length, "a gauntlet saved mid-cooldown would reload ready to fire");
        }

        [Test]
        public void TheRepulsor_IsTheOptedInGauntlet()
        {
            Assert.IsTrue(EntityBodyEquipmentTests.Asset<InventoryItem>(EntityBodyEquipmentTests.RepulsorPath).npcUsable);
        }
    }
}
