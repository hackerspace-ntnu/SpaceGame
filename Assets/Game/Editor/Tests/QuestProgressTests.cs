// A hand-in moves an item out of the player's bag and sometimes puts another one back, so the
// failures worth pinning are the ones that lose something or that refuse a swap the player can
// obviously make: a full hotbar completing a one-for-one step, a reward with nowhere to go, and a
// step that could never complete at all.
//
// QuestProgress is pure, so none of this needs a scene, a popup, a player or a network — which is
// the whole reason the rules were split out of QuestGiver.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Gameplay.Quests;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class QuestProgressTests
    {
        private readonly List<ScriptableObject> assets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (ScriptableObject asset in assets)
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);

            assets.Clear();
        }

        private InventoryItem Item(string itemName)
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.itemName = itemName;
            assets.Add(item);
            return item;
        }

        private Questline Line(params QuestStep[] steps)
        {
            var line = ScriptableObject.CreateInstance<Questline>();
            line.title = "Test errand";
            line.steps = new List<QuestStep>(steps);
            assets.Add(line);
            return line;
        }

        private static QuestStep Step(InventoryItem required, InventoryItem reward = null) =>
            new QuestStep { text = "Bring me that.", required = required, reward = reward };

        /// <summary>A slot inventory with no stacking and no networking — the real one minus the wire.</summary>
        private class FakeInventory : IPlayerInventory
        {
            private readonly InventorySlot[] slots;

            public FakeInventory(int size)
            {
                slots = new InventorySlot[size];
                for (int i = 0; i < size; i++) slots[i] = new InventorySlot(i);
            }

            public int SelectedSlotIndex { get; private set; } = -1;

            public event Action<InventorySlot> OnSlotSelected;
            public event Action<int, InventorySlot> OnSlotChanged;
            public event Action<InventoryItem, ItemState> OnItemDropped;

            public bool TryAddItem(InventoryItem item)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if (!slots[i].IsEmpty) continue;

                    slots[i].Item = item;
                    OnSlotChanged?.Invoke(i, slots[i]);
                    return true;
                }

                return false;
            }

            public bool TryRemoveItem(int index)
            {
                if (index < 0 || index >= slots.Length || slots[index].IsEmpty) return false;

                slots[index].Item = null;
                OnSlotChanged?.Invoke(index, slots[index]);
                return true;
            }

            public void SelectSlot(int slotIndex)
            {
                SelectedSlotIndex = slotIndex;
                OnSlotSelected?.Invoke(GetSelectedSlot());
            }

            public bool TrySetSlot(int index, InventoryItem item)
            {
                if (index < 0 || index >= slots.Length) return false;

                slots[index].Item = item;
                OnSlotChanged?.Invoke(index, slots[index]);
                return true;
            }

            public void RestoreSlots(IReadOnlyList<InventoryItem> items, int selectedSlot)
            {
                for (int i = 0; i < slots.Length; i++)
                    slots[i].Item = items != null && i < items.Count ? items[i] : null;

                SelectedSlotIndex = selectedSlot;
            }

            public int GetInventorySize() => slots.Length;
            public InventorySlot GetSlot(int index) => index >= 0 && index < slots.Length ? slots[index] : null;
            public InventorySlot GetSelectedSlot() => GetSlot(SelectedSlotIndex);
            public InventoryItem GetSelectedItem() => GetSelectedSlot()?.Item;

            public void Drop(InventoryItem item) => OnItemDropped?.Invoke(item, null);
        }

        // ── Holding the item ─────────────────────────────────────────────────────

        [Test]
        public void WithoutTheItemThereIsNothingToHandOver()
        {
            InventoryItem cell = Item("Power Cell");
            var bag = new FakeInventory(6);

            Assert.IsFalse(QuestProgress.CanHandIn(bag, Step(cell), out string why));
            StringAssert.Contains("Power Cell", why);
        }

        [Test]
        public void WithTheItemTheHandInIsAllowed()
        {
            InventoryItem cell = Item("Power Cell");
            var bag = new FakeInventory(6);
            bag.TryAddItem(cell);

            Assert.IsTrue(QuestProgress.CanHandIn(bag, Step(cell), out _));
        }

        [Test]
        public void ADifferentItemIsNotTheOneAskedFor()
        {
            InventoryItem cell = Item("Power Cell");
            InventoryItem scrap = Item("Scrap Plate");

            var bag = new FakeInventory(6);
            bag.TryAddItem(scrap);

            Assert.IsFalse(QuestProgress.CanHandIn(bag, Step(cell), out _));
        }

        // ── Room for the reward ──────────────────────────────────────────────────

        [Test]
        public void AFullBagCanStillCompleteAOneForOneStep()
        {
            // The slot the payment frees is the slot the reward lands in. Refusing this would make
            // a quest un-completable for the commonest possible reason — a full hotbar — and the
            // player would have no idea why.
            InventoryItem cell = Item("Power Cell");
            InventoryItem charm = Item("Charm");

            var bag = new FakeInventory(3);
            bag.TryAddItem(cell);
            bag.TryAddItem(Item("Junk A"));
            bag.TryAddItem(Item("Junk B"));

            Assert.AreEqual(0, InventoryQuery.CountFree(bag), "the bag really is full");
            Assert.IsTrue(QuestProgress.CanHandIn(bag, Step(cell, charm), out _),
                          "the freed slot counts toward room for the reward");
        }

        [Test]
        public void AFullBagCompletesARewardlessStepToo()
        {
            InventoryItem cell = Item("Power Cell");

            var bag = new FakeInventory(2);
            bag.TryAddItem(cell);
            bag.TryAddItem(Item("Junk"));

            Assert.IsTrue(QuestProgress.CanHandIn(bag, Step(cell), out _));
        }

        // ── Malformed steps ──────────────────────────────────────────────────────

        [Test]
        public void AStepThatAsksForNothingCanNeverComplete()
        {
            var bag = new FakeInventory(6);

            Assert.IsFalse(QuestProgress.CanHandIn(bag, Step(null), out string why));
            StringAssert.Contains("no item", why);
        }

        [Test]
        public void ANullStepIsRefusedRatherThanThrowing()
        {
            Assert.IsFalse(QuestProgress.CanHandIn(new FakeInventory(6), null, out _));
        }

        [Test]
        public void ANullInventoryIsRefusedRatherThanThrowing()
        {
            Assert.IsFalse(QuestProgress.CanHandIn(null, Step(Item("Power Cell")), out _));
        }

        // ── Walking the steps ────────────────────────────────────────────────────

        [Test]
        public void TheIndexAdvancesOneStepAtATimeAndStopsAtTheEnd()
        {
            Questline line = Line(Step(Item("A")), Step(Item("B")));

            Assert.AreEqual(1, QuestProgress.NextStep(0, line));
            Assert.AreEqual(2, QuestProgress.NextStep(1, line));
            Assert.AreEqual(2, QuestProgress.NextStep(2, line), "past the last step it stays put");
        }

        [Test]
        public void TheLastStepFinishesTheQuestline()
        {
            Questline line = Line(Step(Item("A")), Step(Item("B")));

            Assert.IsFalse(QuestProgress.IsDone(0, line));
            Assert.IsFalse(QuestProgress.IsDone(1, line));
            Assert.IsTrue(QuestProgress.IsDone(2, line), "both steps handed in");
        }

        [Test]
        public void AFinishedGiverHasNoCurrentStepToAskAbout()
        {
            Questline line = Line(Step(Item("A")));

            Assert.IsNotNull(QuestProgress.Current(0, line));
            Assert.IsNull(QuestProgress.Current(1, line), "a finished errand asks for nothing");
        }

        [Test]
        public void ANullQuestlineIsDoneRatherThanCrashing()
        {
            Assert.IsTrue(QuestProgress.IsDone(0, null));
            Assert.IsNull(QuestProgress.Current(0, null));
            Assert.AreEqual(0, QuestProgress.NextStep(0, null));
        }

        // ── Authoring mistakes the generator should catch ────────────────────────

        [Test]
        public void AQuestlineReportsTheStepsThatCouldNeverComplete()
        {
            InventoryItem cell = Item("Power Cell");

            Questline line = Line(
                Step(cell),
                new QuestStep { text = "", required = cell },
                Step(null));

            var problems = new List<string>(line.Problems());

            Assert.IsTrue(problems.Exists(p => p.Contains("step 1") && p.Contains("no line")),
                          "a step with nothing to say is reported");
            Assert.IsTrue(problems.Exists(p => p.Contains("step 2") && p.Contains("never complete")),
                          "a step with no item is reported");
        }

        [Test]
        public void AnEmptyQuestlineIsReported()
        {
            var problems = new List<string>(Line().Problems());

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("no steps", problems[0]);
        }
    }
}
