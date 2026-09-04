using NUnit.Framework;
using SpaceGame.Items;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Where the folded ornithopter goes when it is not being flown.
    ///
    /// <para>
    /// The wing pack is the one item sized by the surface rather than by the hand: it is the whole
    /// aircraft, folded, and it is meant to read that way — filling the rack edge to edge, hanging
    /// off the top and bottom, storable on the ship's gear wall at true size. Both of those are
    /// decided by a single authored number, <c>ItemGrip.packSize</c>, which
    /// <c>WingPackBuilder.PackSizeForRack</c> derives from the rack's own width and the folded
    /// mesh's proportions.
    /// </para>
    /// <para>
    /// Neither end of that derivation is under this test's control. The mesh is re-exported from
    /// Blender, the rack is re-cut in <c>ExpeditionRigWiring</c>, <c>PackScale.Factor</c> has moved
    /// once already — and the builder that reconciles them is a menu item nobody runs on a whim, so
    /// the number in the prefab goes stale silently. It has before: the 1.26 it carried was
    /// computed against the pre-enlargement rack and left the craft at six of nine columns. The
    /// failure has no symptom beyond "that looks small", which is exactly the kind nobody files.
    /// </para>
    /// <para>
    /// In <c>Tests/Editor/</c> with the rest of the backpack suite: the code under test is in the
    /// predefined <c>Assembly-CSharp</c>, which an asmdef cannot reference.
    /// </para>
    /// </summary>
    public class WingPackStowTests
    {
        private const string ItemPath = "Assets/Game/Resources/Items/Artifacts/WingPack.asset";
        private const string RigPath = "Assets/Game/Prefabs/Items/Equipment/ExpeditionRig.prefab";
        private const string WallPath = "Assets/Game/Prefabs/Items/Equipment/InventoryWall.prefab";

        [SetUp]
        public void ClearMeasurementCache() => ItemFootprint.ClearCache();

        /// <summary>The wing pack's footprint in whole cells, as the pack derives it.</summary>
        private static Vector2Int Shape()
        {
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            Assert.That(item, Is.Not.Null, $"No Wing Pack item at {ItemPath}.");

            PackShape shape = PackShape.ForFootprint(ItemFootprint.FootprintOf(item));

            return new Vector2Int(shape.Width, shape.Height);
        }

        /// <summary>The cell grid of one shipped surface, read off the prefab that carries it.</summary>
        private static Vector2Int CellsOf(string prefabPath, PackSurfaceId id)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"No prefab at {prefabPath}.");

            foreach (PackSurface surface in prefab.GetComponentsInChildren<PackSurface>(true))
                if (surface.Id == id)
                    return surface.Cells;

            Assert.Fail($"{prefabPath} has no {id} surface wired.");
            return default;
        }

        [Test]
        public void TheFoldedCraftFillsTheRackAcrossTheStrictAxis()
        {
            Vector2Int shape = Shape();
            Vector2Int rack = CellsOf(RigPath, PackSurfaceId.Rack);

            // The rack overhangs along its long axis only (PackOverhang) — the other axis is what
            // the lashing reaches around, so it is the one the craft has to match exactly. Equal,
            // not "at least": a column short and it reads as a toy on the pack's back; a column
            // over and the rack refuses it outright, with red cells and no explanation.
            Assert.That(shape.x, Is.EqualTo(rack.x),
                        $"The wing pack is {shape.x} cells across a {rack.x}-column rack. Its " +
                        "packSize needs re-deriving from WingPackBuilder.PackSizeForRack — but do " +
                        "NOT re-run the builder to do it, because it rebuilds the prefab from " +
                        "scratch and drops the networking and persistence added since.");
        }

        [Test]
        public void TheFoldedCraftStandsOnTheShipsGearWallAtTrueSize()
        {
            Vector2Int shape = Shape();
            Vector2Int wall = CellsOf(WallPath, PackSurfaceId.WallGrid);

            // The wall is strict on both axes: no overhang, so the craft has to fit inside it whole,
            // stood on end. Its height is the binding one — the wall is far wider than the craft and
            // barely taller than it.
            Assert.That(shape.y, Is.LessThanOrEqualTo(wall.y),
                        $"The wing pack is {shape.y} cells tall and the gear wall is {wall.y}. " +
                        "Nothing on the ship can store it.");
            Assert.That(shape.x, Is.LessThanOrEqualTo(wall.x),
                        $"The wing pack is {shape.x} cells wide and the gear wall is {wall.x}.");
        }

        /// <summary>
        /// The rack and the gear wall are the only two places the folded craft is meant to live —
        /// the two this file's other tests size it against. Nothing gates it to just those, though:
        /// <see cref="PackOverhang"/> lets a back panel take an item far wider than its own 3x6
        /// span by clamping it down to fit, which is right for a bedroll and wrong for a whole
        /// aircraft — the craft would strap onto a panel the leaf covers the instant the pack
        /// closes, "storing" it somewhere it is invisible and cannot be flown from.
        /// </summary>
        [Test]
        public void OnlyTheRackAndTheGearWallTakeTheFoldedCraft()
        {
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            Assert.That(item, Is.Not.Null, $"No Wing Pack item at {ItemPath}.");

            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            Assert.That(rig, Is.Not.Null, $"No prefab at {RigPath}.");

            var wall = AssetDatabase.LoadAssetAtPath<GameObject>(WallPath);
            Assert.That(wall, Is.Not.Null, $"No prefab at {WallPath}.");

            Assert.That(SurfaceOn(rig, PackSurfaceId.Rack).AcceptsItem(item), Is.True,
                        "The rack refuses the folded craft it is sized to fill.");
            Assert.That(SurfaceOn(wall, PackSurfaceId.WallGrid).AcceptsItem(item), Is.True,
                        "The gear wall refuses the folded craft.");

            Assert.That(SurfaceOn(rig, PackSurfaceId.BackPanelLeft).AcceptsItem(item), Is.False,
                        "The left back panel accepts the folded craft — it is covered the instant " +
                        "the pack closes and should refuse everything but the rack and the wall.");
            Assert.That(SurfaceOn(rig, PackSurfaceId.BackPanelRight).AcceptsItem(item), Is.False,
                        "The right back panel accepts the folded craft.");
            Assert.That(SurfaceOn(rig, PackSurfaceId.Leaf).AcceptsItem(item), Is.False,
                        "The leaf accepts the folded craft.");
        }

        /// <summary>The face of one prefab that carries a given id, else the test fails by name.</summary>
        private static PackSurface SurfaceOn(GameObject prefab, PackSurfaceId id)
        {
            foreach (PackSurface surface in prefab.GetComponentsInChildren<PackSurface>(true))
                if (surface.Id == id)
                    return surface;

            Assert.Fail($"{prefab.name} has no {id} surface wired.");
            return null;
        }
    }
}
