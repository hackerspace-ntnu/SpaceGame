// Pressing Generate twice must move the town, not replace it.
//
// The cost of getting this wrong is invisible at the moment it happens and permanent afterwards: a
// destroyed object takes its SaveableEntity identity with it, and every saved record about it is
// orphaned in every existing save file forever. So the rule that decides what gets destroyed is
// pinned here rather than left to be noticed in a playtest three weeks later.
//
// TownReuse is pure, so this needs no terrain, no prefabs and no instantiation — plain GameObjects
// standing in for both the prefabs and the placed instances.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.World.Towns;

namespace SpaceGame.EditorTools
{
    public class TownReuseTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
        }

        private GameObject New(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        private GameObject Root() => New("Generated");

        /// <summary>An already-placed object, stamped as a generator would stamp it.</summary>
        private static GameObject Placed(Transform root, TownSection section, int group, GameObject source)
        {
            var go = new GameObject(source.name + " (placed)");
            go.transform.SetParent(root);

            TownInstance stamp = go.AddComponent<TownInstance>();
            stamp.section = section;
            stamp.groupIndex = group;
            stamp.source = source;

            return go;
        }

        private static TownSlot Slot(TownSection section, int group, int prefabIndex, float x = 0f) =>
            new TownSlot
            {
                Section = section,
                GroupIndex = group,
                PrefabIndex = prefabIndex,
                LocalXZ = new Vector2(x, 0f),
                Scale = 1f,
            };

        private static System.Func<TownSlot, GameObject> From(params GameObject[] prefabs) =>
            slot => slot.PrefabIndex >= 0 && slot.PrefabIndex < prefabs.Length
                ? prefabs[slot.PrefabIndex]
                : null;

        // ── The case that matters ────────────────────────────────────────────────

        [Test]
        public void ChangingTheSpacingDestroysNothing()
        {
            // The whole reason this class exists. The slots are at completely different positions;
            // the town is the same town, so every object must be moved rather than replaced.
            GameObject hut = New("Hut");
            Transform root = Root().transform;
            for (int i = 0; i < 4; i++) Placed(root, TownSection.Building, 0, hut);

            var slots = new List<TownSlot>
            {
                Slot(TownSection.Building, 0, 0, 10f),
                Slot(TownSection.Building, 0, 0, 40f),
                Slot(TownSection.Building, 0, 0, 70f),
                Slot(TownSection.Building, 0, 0, 100f),
            };

            TownReusePlan plan = TownReuse.Match(slots, From(hut), root);

            Assert.AreEqual(4, plan.ReuseCount, "every hut should be moved, not remade");
            Assert.IsEmpty(plan.Surplus, "nothing should be destroyed for moving the town");
        }

        [Test]
        public void AnEmptyTownMakesEverythingNew()
        {
            GameObject hut = New("Hut");
            Transform root = Root().transform;

            var slots = new List<TownSlot> { Slot(TownSection.Building, 0, 0) };
            TownReusePlan plan = TownReuse.Match(slots, From(hut), root);

            Assert.AreEqual(0, plan.ReuseCount);
            Assert.IsNull(plan.Reused[0]);
            Assert.IsEmpty(plan.Surplus);
        }

        // ── Counts ───────────────────────────────────────────────────────────────

        [Test]
        public void AskingForFewerLeavesTheRestSurplus()
        {
            GameObject hut = New("Hut");
            Transform root = Root().transform;
            for (int i = 0; i < 5; i++) Placed(root, TownSection.Building, 0, hut);

            var slots = new List<TownSlot>
            {
                Slot(TownSection.Building, 0, 0),
                Slot(TownSection.Building, 0, 0),
            };

            TownReusePlan plan = TownReuse.Match(slots, From(hut), root);

            Assert.AreEqual(2, plan.ReuseCount);
            Assert.AreEqual(3, plan.Surplus.Count, "the three nobody asked for");
        }

        [Test]
        public void AskingForMoreOnlyMakesTheDifference()
        {
            GameObject hut = New("Hut");
            Transform root = Root().transform;
            for (int i = 0; i < 2; i++) Placed(root, TownSection.Building, 0, hut);

            var slots = new List<TownSlot>
            {
                Slot(TownSection.Building, 0, 0),
                Slot(TownSection.Building, 0, 0),
                Slot(TownSection.Building, 0, 0),
            };

            TownReusePlan plan = TownReuse.Match(slots, From(hut), root);

            Assert.AreEqual(2, plan.ReuseCount, "the two already standing");
            Assert.IsNull(plan.Reused[2], "and one new one");
            Assert.IsEmpty(plan.Surplus);
        }

        // ── Identity ─────────────────────────────────────────────────────────────

        [Test]
        public void ADifferentPrefabIsNotReused()
        {
            GameObject hut = New("Hut");
            GameObject tower = New("Tower");
            Transform root = Root().transform;
            Placed(root, TownSection.Building, 0, hut);

            var slots = new List<TownSlot> { Slot(TownSection.Building, 0, 1) };
            TownReusePlan plan = TownReuse.Match(slots, From(hut, tower), root);

            Assert.IsNull(plan.Reused[0], "a tower is not a hut");
            Assert.AreEqual(1, plan.Surplus.Count, "the hut is no longer wanted");
        }

        [Test]
        public void ReorderingAGroupsPrefabArrayStillReusesTheSameObjects()
        {
            // The reuse key holds the prefab by reference, not by its index in the array. An index
            // key would decide every hut had become a tower the moment the array was reordered —
            // which is an ordinary thing to do in the inspector, and would silently rebuild the town.
            GameObject hut = New("Hut");
            GameObject tower = New("Tower");
            Transform root = Root().transform;
            Placed(root, TownSection.Building, 0, hut);
            Placed(root, TownSection.Building, 0, tower);

            // Same two prefabs, swapped in the array, so the indices on the slots are inverted.
            var slots = new List<TownSlot>
            {
                Slot(TownSection.Building, 0, 1),
                Slot(TownSection.Building, 0, 0),
            };

            TownReusePlan plan = TownReuse.Match(slots, From(tower, hut), root);

            Assert.AreEqual(2, plan.ReuseCount);
            Assert.IsEmpty(plan.Surplus);
        }

        [Test]
        public void TheSamePrefabInTwoGroupsIsNotCrossMatched()
        {
            // Two groups of the same prefab are two different things — an inner ring and an outer
            // one, say. Letting one group consume the other's objects would make which group loses
            // its buildings depend on list order.
            GameObject hut = New("Hut");
            Transform root = Root().transform;
            Placed(root, TownSection.Building, 0, hut);

            var slots = new List<TownSlot> { Slot(TownSection.Building, 1, 0) };
            TownReusePlan plan = TownReuse.Match(slots, From(hut), root);

            Assert.IsNull(plan.Reused[0]);
            Assert.AreEqual(1, plan.Surplus.Count);
        }

        [Test]
        public void TheSamePrefabInTwoSectionsIsNotCrossMatched()
        {
            GameObject crate = New("Crate");
            Transform root = Root().transform;
            Placed(root, TownSection.Prop, 0, crate);

            var slots = new List<TownSlot> { Slot(TownSection.Scatter, 0, 0) };
            TownReusePlan plan = TownReuse.Match(slots, From(crate), root);

            Assert.IsNull(plan.Reused[0]);
            Assert.AreEqual(1, plan.Surplus.Count);
        }

        // ── Strays ───────────────────────────────────────────────────────────────

        [Test]
        public void AnUnstampedChildIsSurplus()
        {
            // Foundation pads carry no stamp — they are re-measured against the new ground every
            // time — and neither does a town generated before the stamp existed, nor anything
            // dropped under the Generated root by hand.
            GameObject hut = New("Hut");
            Transform root = Root().transform;

            var pad = new GameObject("FoundationPad_Hut");
            pad.transform.SetParent(root);

            TownReusePlan plan = TownReuse.Match(new List<TownSlot> { Slot(TownSection.Building, 0, 0) },
                                                From(hut), root);

            Assert.IsNull(plan.Reused[0]);
            Assert.AreEqual(1, plan.Surplus.Count);
            Assert.AreSame(pad, plan.Surplus[0]);
        }

        [Test]
        public void AStampWhoseSourcePrefabWasDeletedIsSurplus()
        {
            // A missing source is a dangling reference, not a match for anything. Left in the pool
            // it would be neither reused nor destroyed, and would quietly accumulate.
            Transform root = Root().transform;
            var orphan = new GameObject("Was a hut");
            orphan.transform.SetParent(root);
            orphan.AddComponent<TownInstance>();   // source left null

            TownReusePlan plan = TownReuse.Match(new List<TownSlot>(), _ => null, root);

            Assert.AreEqual(1, plan.Surplus.Count);
        }

        // ── Null safety ──────────────────────────────────────────────────────────

        [Test]
        public void NoRootMeansEverythingIsNewAndNothingIsSurplus()
        {
            GameObject hut = New("Hut");
            var slots = new List<TownSlot> { Slot(TownSection.Building, 0, 0) };

            TownReusePlan plan = TownReuse.Match(slots, From(hut), null);

            Assert.AreEqual(1, plan.Reused.Length);
            Assert.IsNull(plan.Reused[0]);
            Assert.IsEmpty(plan.Surplus);
        }

        [Test]
        public void ASlotWithNoPrefabIsSkippedRatherThanThrowing()
        {
            Transform root = Root().transform;
            var slots = new List<TownSlot> { Slot(TownSection.Building, 0, 99) };

            TownReusePlan plan = TownReuse.Match(slots, From(), root);

            Assert.IsNull(plan.Reused[0]);
        }
    }
}
