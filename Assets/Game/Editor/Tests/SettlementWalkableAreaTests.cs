using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// A part of a settlement's walkable area picked by a rule (<c>SettlementWalkableArea.PatchWhere</c>): what
    /// <c>Settlement.PlaceCharacters</c> falls back on when the ground its heart can walk to is too small a share of
    /// everything walkable for chance to find. Two floors far from anything else stand in for "the heart's ground" and
    /// "the rest".
    /// </summary>
    public class SettlementWalkableAreaTests
    {
        // Far from the origin, where an open scene keeps its own colliders.
        private static readonly Vector3 Centre = new(5000f, 3000f, 5000f);
        private const float FloorSide = 20f;
        private const float FloorOffset = 20f;
        private const float BakeRadius = 45f;
        private const int Samples = 200;

        private readonly List<GameObject> floors = new();
        private SettlementWalkableArea area;

        [SetUp]
        public void SetUp()
        {
            floors.Add(Floor(-FloorOffset));
            floors.Add(Floor(FloorOffset));
            area = SettlementWalkableArea.Bake(Centre, BakeRadius, null);
            Assert.IsNotNull(area, "two box floors must bake");
            Assert.IsFalse(area.IsEmpty, "two box floors must bake to something walkable");
        }

        [TearDown]
        public void TearDown()
        {
            area?.Dispose();
            foreach (GameObject floor in floors) Object.DestroyImmediate(floor);
            floors.Clear();
        }

        private static GameObject Floor(float alongX)
        {
            var floor = new GameObject("WalkableAreaTestFloor") { hideFlags = HideFlags.HideAndDontSave };
            floor.transform.position = Centre + new Vector3(alongX, 0f, 0f);
            floor.AddComponent<BoxCollider>().size = new Vector3(FloorSide, 1f, FloorSide);
            return floor;
        }

        private static bool OnEastFloor(Vector3 point) => point.x > Centre.x;

        [Test]
        public void APatchHoldsOnlyTheGroundItsRuleAccepts()
        {
            SettlementWalkableArea.Patch east = area.PatchWhere(OnEastFloor);
            Assert.IsFalse(east.IsEmpty);

            var rng = new SettlementPlacementUtil.SeededRng(1);
            for (int i = 0; i < Samples; i++)
                Assert.IsTrue(OnEastFloor(east.Sample(ref rng)), "a point sampled off the east floor's patch lies on the west floor");
        }

        [Test]
        public void ARuleThatAcceptsNothingGivesAnEmptyPatch()
        {
            Assert.IsTrue(area.PatchWhere(_ => false).IsEmpty);
        }

        [Test]
        public void ARuleThatAcceptsEverythingKeepsBothFloors()
        {
            SettlementWalkableArea.Patch all = area.PatchWhere(_ => true);
            var rng = new SettlementPlacementUtil.SeededRng(1);
            int east = 0;
            for (int i = 0; i < Samples; i++)
                if (OnEastFloor(all.Sample(ref rng))) east++;

            // Two equal floors: each gets about half. A patch that dropped one would give 0 or all of them.
            Assert.Greater(east, Samples / 4);
            Assert.Less(east, Samples * 3 / 4);
        }
    }
}
