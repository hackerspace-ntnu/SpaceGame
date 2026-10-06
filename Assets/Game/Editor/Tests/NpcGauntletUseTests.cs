// D9: an NPC fires a worn gauntlet only when the gauntlet's asset opts in, and fires it where it is
// aiming rather than along its own forearm.
using System.Collections.Generic;
using NUnit.Framework;
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
