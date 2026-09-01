using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The one place an <see cref="InventoryItem"/> becomes a <see cref="PackShape"/>.
    ///
    /// <para>
    /// Every path that touches the layout has to agree about how big an item is — a world pickup,
    /// a drag, a save restore, a client adopting the server's list — and the same reason
    /// <see cref="ItemFootprint.FootprintOf(InventoryItem)"/> exists applies twice over here:
    /// two machines that disagree about one item's mask disagree about what fits, and the pack
    /// desynchronises with nothing in the console.
    /// </para>
    /// <para>
    /// <b>The library is passed in rather than read from a static.</b> It arrives from
    /// <see cref="BackpackObject.Shapes"/>, threaded through the codec and the first-fit helper.
    /// A static would have been shorter and is the trap this project has hit before: a per-instance
    /// field assigned into a global means the last pack to wake up decides what every other pack
    /// thinks its gear is shaped like.
    /// </para>
    /// </summary>
    public static class PackShapes
    {
        /// <summary>
        /// The shape an item occupies, authored if anyone has drawn one and derived from the item's
        /// true footprint otherwise. Never empty for a non-null item, for the same reason
        /// <see cref="ItemFootprint.FootprintOf(InventoryItem)"/> has a floor: an item that
        /// occupies nothing is refused by <see cref="PackLayout"/> and would be uncarryable.
        /// </summary>
        public static PackShape For(InventoryItem item, PackShapeLibrary library)
        {
            if (item == null) return PackShape.None;

            PackShapeLibrary.Entry authored = library != null ? library.Find(item.ID) : null;

            if (authored == null) return PackShape.ForFootprint(ItemFootprint.FootprintOf(item));

            PackShape shape = PackShape.FromMask(authored.width, authored.height, authored.cells);

            if (shape.IsEmpty) return PackShape.ForFootprint(ItemFootprint.FootprintOf(item));

            WarnIfOversized(item, shape);

            return shape;
        }

        /// <summary>
        /// May this item be turned? Authored per item; anything undrawn may, because a derived
        /// block is a rectangle and turning one cannot look wrong.
        /// </summary>
        public static bool AllowsRotation(InventoryItem item, PackShapeLibrary library)
        {
            if (item == null) return false;

            PackShapeLibrary.Entry authored = library != null ? library.Find(item.ID) : null;

            return authored == null || authored.allowRotation;
        }

        /// <summary>
        /// The yaw a placement of this item may actually use: a quarter turn, and zero for an item
        /// whose row forbids rotation.
        /// </summary>
        public static float SnapYaw(InventoryItem item, PackShapeLibrary library, float yaw) =>
            AllowsRotation(item, library) ? PackGrid.SnapYaw(yaw) : 0f;

        /// <summary>Forget which items have already been complained about. For tests.</summary>
        public static void ClearWarnings() => warned.Clear();

        private static readonly HashSet<string> warned = new();

        /// <summary>
        /// Say so when an authored shape is smaller than the item really is.
        ///
        /// <para>
        /// Items are still drawn at true size — the grid governs where a thing snaps, never how big
        /// it renders — so a mask two cells long under a 0.26 m item does not shrink the item, it
        /// makes the item visibly overhang the cells the layout reserved for it and lie through
        /// whatever is in the next cell along. Silently growing the mask would be worse: the
        /// authored shape is somebody's decision and the tool has no business overruling it. So it
        /// is reported, once per item per session, naming both numbers.
        /// </para>
        /// <para>
        /// Only an AUTHORED shape can trigger this. A derived one is <see cref="Mathf.Ceil"/>ed
        /// from the same footprint it would be compared against.
        /// </para>
        /// </summary>
        private static void WarnIfOversized(InventoryItem item, PackShape shape)
        {
            const float slack = 1e-3f;

            Vector2 footprint = ItemFootprint.FootprintOf(item);
            Vector2 block = shape.Metres;

            bool tooWide = footprint.x > block.x + slack;
            bool tooDeep = footprint.y > block.y + slack;

            if (!tooWide && !tooDeep) return;

            string id = item.ID ?? item.itemName;
            if (!warned.Add(id)) return;

            Debug.LogWarning(
                $"PackShapes: '{item.itemName}' measures {footprint.x:F3} x {footprint.y:F3} m but " +
                $"its authored grid shape is only {shape.Width} x {shape.Height} cells " +
                $"({block.x:F3} x {block.y:F3} m at a {PackGrid.Cell:F3} m cell). The item is drawn " +
                "at true size, so it will overhang the cells the layout reserved for it. Widen the " +
                "shape in PackShapes.asset, or lower the item's ItemGrip packSize.", item);
        }
    }
}
