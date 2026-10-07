using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Where the body screen's six tiles sit: a pyramid down the left edge, read top-down the way
    /// the body reads — the torso on top, the two gauntlets under it, the hands along the bottom.
    ///
    /// <para>
    /// Pure arithmetic, deliberately outside <see cref="BodyInventoryUI"/>. The rail is the one
    /// part of that screen with a shape worth asserting — that the rows are centred on each other,
    /// that Q is left of E, that the block clears the screen edge — and a layout expressed as
    /// offsets inside a builder can only be checked by looking at it.
    /// </para>
    /// <para>
    /// Positions are canvas pixels against an anchor on the LEFT edge at half height
    /// (<c>anchorMin = anchorMax = (0, 0.5)</c>), which is why every y is signed and every x is a
    /// distance from the left. See <see cref="UIScale"/> for what a canvas pixel is.
    /// </para>
    /// </summary>
    public static class GearRailLayout
    {
        /// <summary>
        /// The middle of the pyramid, measured from the left edge. Wide enough that the three-tile
        /// bottom row clears the edge with a margin, near enough that the whole block stays in the
        /// left third and out of the way of the figure framed down the centre.
        /// </summary>
        public const float CentreFromLeft = 210f;

        /// <summary>Gap between a row of tiles and the caption bracketing it.</summary>
        public const float CaptionGap = 12f;

        /// <summary>Centre-to-centre spacing along a row.</summary>
        public static float ColumnPitch => HotbarStyle.SlotWidth + HotbarStyle.SlotSpacing;

        /// <summary>Centre-to-centre spacing between rows.</summary>
        public static float RowPitch => HotbarStyle.SlotHeight + HotbarStyle.SlotSpacing;

        /// <summary>The three bands, top to bottom. The hand row's width comes from the inventory.</summary>
        public const int RowCount = 3;

        /// <summary>One tile: which slot it names and where its centre goes.</summary>
        public readonly struct Placement
        {
            public readonly GearRef Slot;
            public readonly Vector2 At;

            /// <summary>The tile's corner key label: Q, E, SPACE ×2, or the hotbar number.</summary>
            public readonly string Key;

            /// <summary>The tile's object name, for the hierarchy.</summary>
            public readonly string Name;

            public Placement(GearRef slot, Vector2 at, string key, string name)
            {
                Slot = slot;
                At = at;
                Key = key;
                Name = name;
            }
        }

        /// <summary>
        /// The six tiles, in draw order: torso, left gauntlet, right gauntlet, then the hand slots
        /// left to right.
        ///
        /// <para>
        /// The gauntlets are laid out Q-left, E-right, which MIRRORS the figure behind them — that
        /// character is seen from the front, so the player's left arm is on the right of the
        /// screen. The keys win over the anatomy because the key is what the tile is labelled with
        /// and what the player presses; a Q sitting to the right of an E would be wrong in the
        /// place it is actually read.
        /// </para>
        /// </summary>
        public static IReadOnlyList<Placement> Build(int hotbarSize)
        {
            var placements = new List<Placement>(RowCount + hotbarSize);

            Add(placements, 0, 0, 1, GearRef.Body(BodySlot.Torso), "SPACE ×2", "Torso");
            Add(placements, 1, 0, 2, GearRef.Body(BodySlot.LeftGauntlet), "Q", "Left gauntlet");
            Add(placements, 1, 1, 2, GearRef.Body(BodySlot.RightGauntlet), "E", "Right gauntlet");

            for (int i = 0; i < hotbarSize; i++)
                Add(placements, 2, i, hotbarSize, GearRef.Hotbar(i), (i + 1).ToString(), $"Slot {i + 1}");

            return placements;
        }

        private static void Add(List<Placement> into, int row, int column, int columns,
                                GearRef slot, string key, string name)
            => into.Add(new Placement(slot, new Vector2(ColumnX(column, columns), RowY(row)), key, name));

        /// <summary>The centre of <paramref name="row"/>, counted from the top, with the block centred on the anchor.</summary>
        public static float RowY(int row) => (RowCount - 1) * 0.5f * RowPitch - row * RowPitch;

        /// <summary>The centre of one tile in a row of <paramref name="columns"/>, counted from the left.</summary>
        public static float ColumnX(int column, int columns)
            => CentreFromLeft + (column - (columns - 1) * 0.5f) * ColumnPitch;

        /// <summary>Where a caption above the pyramid sits.</summary>
        public static float CaptionAboveY => RowY(0) + HotbarStyle.SlotHeight * 0.5f + CaptionGap;

        /// <summary>Where a caption below the pyramid sits.</summary>
        public static float CaptionBelowY => RowY(RowCount - 1) - HotbarStyle.SlotHeight * 0.5f - CaptionGap;
    }
}
