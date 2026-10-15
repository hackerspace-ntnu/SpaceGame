// What the hand-tool roster promises about putting a tool away, read off the BUILT prefabs: a resident
// stows its tool on its belt whenever it is not working, and a tool with no place to hang stays in the
// hand and stoops the walk. So every tool is stowable except the explicit carry-only list, and a long
// tool never lands on a hip.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class HandToolBeltTests
    {
        /// <summary>The belt every Raxy resident wears offers the hips and the back; a backpack adds its two corners.</summary>
        private static readonly BeltSlot[] BeltOnly = { BeltSlot.HipRight, BeltSlot.HipLeft, BeltSlot.Back };

        private static readonly BeltSlot[] BeltAndPack =
            { BeltSlot.HipRight, BeltSlot.HipLeft, BeltSlot.Back, BeltSlot.PackLeft, BeltSlot.PackRight };

        private static readonly BeltSlot[] HipSlots = { BeltSlot.HipRight, BeltSlot.HipLeft };

        private static GameObject Prefab(HandToolSpec spec) =>
            AssetDatabase.LoadAssetAtPath<InventoryItem>(spec.ItemPath)?.itemPrefab;

        private static IEnumerable<HandToolSpec> Stowable() => HandToolRoster.All.Where(s => !s.CarryOnly);

        private static bool IsLong(HandToolSpec spec) => spec.Slung || spec.HoldSize > BeltHangs.HipMaxLength;

        [Test]
        public void OnlyCartsAreCarryOnly()
        {
            // Growing this list sends another resident about stooped with a tool in hand: a decision.
            var carts = new[] { "Carry_Cart_Hand", "Carry_Cart_Hover" };

            CollectionAssert.AreEquivalent(carts, HandToolRoster.All.Where(s => s.CarryOnly).Select(s => s.Id).ToArray());
            foreach (HandToolSpec spec in HandToolRoster.All.Where(s => s.CarryOnly))
                Assert.AreEqual(CarryStance.Push, spec.Stance, $"{spec.Id} is carry-only but is not a pushed cart.");
        }

        [Test]
        public void EveryBuiltTool_HasABeltMountUnlessCarryOnly()
        {
            foreach (HandToolSpec spec in HandToolRoster.All)
            {
                GameObject prefab = Prefab(spec);
                if (prefab == null) continue;

                Assert.AreEqual(!spec.CarryOnly, prefab.GetComponent<BeltMount>() != null,
                    $"{spec.Id}: the built prefab disagrees with the roster about hanging on a belt. Re-run Hand Tools > Build All.");
            }
        }

        [Test]
        public void EveryStowableTool_FindsASlotOnTheBeltAlone()
        {
            foreach (HandToolSpec spec in Stowable())
            {
                GameObject prefab = Prefab(spec);
                if (prefab == null) continue;

                Assert.IsTrue(BeltSeat.Plan(new[] { prefab.GetComponent<BeltMount>() }, BeltOnly).ContainsKey(0),
                    $"{spec.Id} has no slot on a standard belt, so a resident holding it never puts it away.");
            }
        }

        [Test]
        public void ALongTool_HangsOnlyFromTheBackAndPack()
        {
            foreach (HandToolSpec spec in Stowable())
            {
                GameObject prefab = Prefab(spec);
                if (prefab == null) continue;

                IReadOnlyList<BeltSlot> slots = prefab.GetComponent<BeltMount>().Slots;
                bool hangsFromAHip = slots.Any(HipSlots.Contains);
                Assert.AreEqual(!IsLong(spec), hangsFromAHip,
                    $"{spec.Id} ({spec.HoldSize:0.00} m) {(IsLong(spec) ? "is long but may hang from a hip" : "is short but may not hang from a hip")}.");
            }
        }

        [Test]
        public void TwoLongTools_NeverShareAnAnchor()
        {
            GameObject staff = Prefab(HandToolRoster.All.First(s => s.Id == "Tool_Spear_Stone"));
            GameObject spade = Prefab(HandToolRoster.All.First(s => s.Id == "Tool_Shovel"));
            if (staff == null || spade == null) return;

            var mounts = new[] { staff.GetComponent<BeltMount>(), spade.GetComponent<BeltMount>() };

            Dictionary<int, BeltSlot> belt = BeltSeat.Plan(mounts, BeltOnly);
            Assert.AreEqual(BeltSlot.Back, belt[0]);
            Assert.IsFalse(belt.ContainsKey(1), "the second long tool must be left off a belt with one back anchor, not put on a hip");

            Dictionary<int, BeltSlot> pack = BeltSeat.Plan(mounts, BeltAndPack);
            Assert.AreNotEqual(pack[0], pack[1]);
        }
    }
}
