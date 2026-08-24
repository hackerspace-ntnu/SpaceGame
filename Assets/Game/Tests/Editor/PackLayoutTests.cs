using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The pack's placement bookkeeping. Plain C# — no GameObjects, no surfaces, just the rules
    /// about what may sit where.
    ///
    /// <para>
    /// The surface here is 0.90 x 0.72 m, which is exactly 10 x 8 cells with no hem, so every
    /// expected uv in this file is arithmetic anyone can check: cell <c>(i, j)</c> of a
    /// <c>w x h</c> item is centred at <c>((i + w/2) * 0.09, (j + h/2) * 0.09)</c>. The hem case is
    /// <c>PackSurfaceTests</c>'s.
    /// </para>
    /// </summary>
    public class PackLayoutTests
    {
        private static readonly Vector2 Surface = new(0.90f, 0.72f);

        private const PackSurfaceId Left = PackSurfaceId.BackPanelLeft;
        private const PackSurfaceId Right = PackSurfaceId.BackPanelRight;

        private static readonly PackShape TwoByTwo = PackShape.Rect(2, 2);
        private static readonly PackShape FourByFour = PackShape.Rect(4, 4);

        [Test]
        public void APlacedItemSnapsToACellAndIsReadableBack()
        {
            var layout = new PackLayout();

            bool placed = layout.TryPlace("item-a", Left, Surface, TwoByTwo,
                                          new Vector2(0.2f, 0.3f), 0f);

            Assert.IsTrue(placed);
            Assert.AreEqual(1, layout.Placements.Count);

            PackPlacement p = layout.Placements[0];

            Assert.AreEqual("item-a", p.ItemId);
            Assert.AreEqual(Left, p.Surface);

            // (0.2, 0.3) is nearest the block whose lowest cell is (1, 2), centred at (0.18, 0.27).
            Assert.AreEqual(0.18f, p.Uv.x, 1e-4f, "the uv stored is the snapped one, not the asked one");
            Assert.AreEqual(0.27f, p.Uv.y, 1e-4f);
        }

        [Test]
        public void AnOverlappingItemIsRefusedAndChangesNothing()
        {
            var layout = new PackLayout();

            layout.TryPlace("item-a", Left, Surface, FourByFour, new Vector2(0.2f, 0.3f), 0f);

            bool second = layout.TryPlace("item-b", Left, Surface, FourByFour,
                                          new Vector2(0.2f, 0.3f), 0f);

            Assert.IsFalse(second);
            Assert.AreEqual(1, layout.Placements.Count);
            Assert.AreEqual("item-a", layout.Placements[0].ItemId);

            // The same clash on a different surface is not a clash at all.
            Assert.IsTrue(layout.TryPlace("item-b", Right, Surface, FourByFour,
                                          new Vector2(0.2f, 0.3f), 0f));
        }

        /// <summary>
        /// Touching is legal, overlapping is not, and the boundary between them is now exactly one
        /// cell wide rather than a floating-point tolerance.
        /// </summary>
        [Test]
        public void ItemsMayButtUpAgainstEachOtherButNotShareACell()
        {
            var layout = new PackLayout();

            Assert.IsTrue(layout.TryPlace("a", Left, Surface, TwoByTwo,
                                          PackGrid.BlockCentreUv(Surface, new Vector2Int(0, 0), new Vector2Int(2, 2)), 0f));

            Assert.IsTrue(layout.TryPlace("b", Left, Surface, TwoByTwo,
                                          PackGrid.BlockCentreUv(Surface, new Vector2Int(2, 0), new Vector2Int(2, 2)), 0f),
                          "the very next column is free");

            Assert.IsFalse(layout.TryPlace("c", Left, Surface, TwoByTwo,
                                           PackGrid.BlockCentreUv(Surface, new Vector2Int(1, 0), new Vector2Int(2, 2)), 0f),
                           "one cell of overlap is still an overlap");
        }

        [Test]
        public void RemovingAnItemFreesItsSpace()
        {
            var layout = new PackLayout();

            layout.TryPlace("item-a", Left, Surface, FourByFour, new Vector2(0.2f, 0.3f), 0f);

            Assert.IsFalse(layout.TryPlace("item-b", Left, Surface, FourByFour,
                                           new Vector2(0.2f, 0.3f), 0f));

            Assert.IsTrue(layout.Remove("item-a"));
            Assert.IsFalse(layout.Remove("item-a"));

            Assert.IsTrue(layout.TryPlace("item-b", Left, Surface, FourByFour,
                                          new Vector2(0.2f, 0.3f), 0f));
        }

        [Test]
        public void TryFindSpotFindsRoomAndAdmitsWhenThereIsNone()
        {
            var layout = new PackLayout();

            Assert.IsTrue(layout.TryFindSpot(Left, Surface, TwoByTwo, out Vector2 uv, out float yaw));
            Assert.IsTrue(layout.TryPlace("item-a", Left, Surface, TwoByTwo, uv, yaw));

            // Wider than the surface either way round: no cell and no turn can seat it.
            Assert.IsFalse(layout.TryFindSpot(Left, Surface, PackShape.Rect(16, 16), out _, out _));
        }

        /// <summary>
        /// An item longer than the face one way round and short enough the other has to be found by
        /// turning it. This is what the grid kept of the old diagonal search — four orientations,
        /// which for a rectangle is two.
        /// </summary>
        [Test]
        public void TryFindSpotTurnsAnItemThatOnlyFitsTheOtherWayRound()
        {
            var layout = new PackLayout();

            // 9 cells long on a face that is 10 across and 8 up: fits flat, not upright.
            var rod = PackShape.Rect(2, 9);

            Assert.IsFalse(layout.CanPlace(Left, Surface, rod, new Vector2(0.45f, 0.36f), 0f),
                           "upright it runs off the top");

            Assert.IsTrue(layout.TryFindSpot(Left, Surface, rod, out Vector2 uv, out float yaw));

            Assert.AreEqual(90f, yaw, 1e-4f, "the only orientation that fits");
            Assert.IsTrue(layout.TryPlace("rod", Left, Surface, rod, uv, yaw));
        }

        /// <summary>
        /// An item that may not turn is offered one orientation only, and the search must not hand
        /// back a spot that only works turned — that would find room and then lose it again inside
        /// <c>TryPlace</c>.
        /// </summary>
        [Test]
        public void TryFindSpotHonoursAnItemThatMayNotTurn()
        {
            var layout = new PackLayout();
            var rod = PackShape.Rect(2, 9);

            Assert.IsFalse(layout.TryFindSpot(Left, Surface, rod, out _, out _,
                                              ignoreItemId: null, allowTurns: false));

            Assert.IsTrue(layout.TryFindSpot(Left, Surface, rod, out _, out _,
                                             ignoreItemId: null, allowTurns: true));
        }

        /// The bug this suite exists for: nudging an item must not collide with the item's own
        /// current placement.
        [Test]
        public void AnItemCanBeMovedOntoItsOwnFootprint()
        {
            var layout = new PackLayout();

            layout.TryPlace("item-a", Left, Surface, TwoByTwo, new Vector2(0.27f, 0.27f), 0f);

            // One cell to the right, which overlaps its own current cells on the whole left column.
            bool moved = layout.TryMove("item-a", Left, Surface, TwoByTwo,
                                        new Vector2(0.36f, 0.27f), 0f);

            Assert.IsTrue(moved);
            Assert.AreEqual(1, layout.Placements.Count);
            Assert.AreEqual(0.36f, layout.Placements[0].Uv.x, 1e-4f);
        }

        [Test]
        public void AnItemHangingOffTheEdgeIsRefused()
        {
            var layout = new PackLayout();

            Assert.IsFalse(layout.TryPlace("item-a", Left, Surface, TwoByTwo,
                                           new Vector2(0.89f, 0.36f), 0f),
                           "snapped hard against the right edge, its outer column is off the grid");

            Assert.AreEqual(0, layout.Placements.Count);
        }

        /// <summary>
        /// A yaw off the quarter turns is not refused, it is rounded. Old saves and the old
        /// 24-degree wheel both produced angles like 39 or 168, and a restore that refused them
        /// would lose gear.
        /// </summary>
        [Test]
        public void AYawOffTheQuarterTurnsIsRoundedRatherThanRefused()
        {
            var layout = new PackLayout();

            Assert.IsTrue(layout.TryPlace("a", Left, Surface, PackShape.Rect(3, 1),
                                          new Vector2(0.3f, 0.3f), 39f));

            Assert.AreEqual(0f, layout.Placements[0].Yaw, 1e-4f);

            Assert.IsTrue(layout.TryPlace("b", Left, Surface, PackShape.Rect(3, 1),
                                          new Vector2(0.7f, 0.3f), 71f));

            Assert.AreEqual(90f, layout.Placements[1].Yaw, 1e-4f);
        }

        /// <summary>A click on a cell an item is on names that item; a click beside it names nothing.</summary>
        [Test]
        public void TryFindAtNamesWhateverCoversTheCell()
        {
            var layout = new PackLayout();

            layout.TryPlace("item-a", Left, Surface, TwoByTwo,
                            PackGrid.BlockCentreUv(Surface, new Vector2Int(2, 2), new Vector2Int(2, 2)), 0f);

            Assert.IsTrue(layout.TryFindAt(Left, Surface,
                                           PackGrid.CentreUv(Surface, new Vector2Int(3, 3)),
                                           out PackPlacement hit));
            Assert.AreEqual("item-a", hit.ItemId);

            Assert.IsFalse(layout.TryFindAt(Left, Surface,
                                            PackGrid.CentreUv(Surface, new Vector2Int(5, 3)), out _));
        }
    }
}
