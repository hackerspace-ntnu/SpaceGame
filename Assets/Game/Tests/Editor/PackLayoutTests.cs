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

        /// <summary>
        /// A face the overhang rule leaves strict. The rule keys on the face's IDENTITY, not its
        /// size — see <c>PackOverhang.Axes</c> — so the leaf at this suite's own 0.90 x 0.72 m
        /// keeps every uv here checkable by the same arithmetic while refusing the oversized
        /// shapes the back panels would clamp.
        /// </summary>
        private const PackSurfaceId Strict = PackSurfaceId.Leaf;

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

            // Wider than the surface either way round: no cell and no turn can seat it. Asked of
            // a strict face — on the back panel the overhang clamp would seat it over the whole
            // face instead of refusing it.
            Assert.IsFalse(layout.TryFindSpot(Strict, Surface, PackShape.Rect(16, 16), out _, out _));
        }

        /// <summary>
        /// An item longer than the face one way round and short enough the other has to be found by
        /// turning it. This is what the grid kept of the old diagonal search — four orientations,
        /// which for a rectangle is two. Asked of a strict face: on the back panel the upright rod
        /// would clamp to the column span and fit without turning at all.
        /// </summary>
        [Test]
        public void TryFindSpotTurnsAnItemThatOnlyFitsTheOtherWayRound()
        {
            var layout = new PackLayout();

            // 9 cells long on a face that is 10 across and 8 up: fits flat, not upright.
            var rod = PackShape.Rect(2, 9);

            Assert.IsFalse(layout.CanPlace(Strict, Surface, rod, new Vector2(0.45f, 0.36f), 0f),
                           "upright it runs off the top");

            Assert.IsTrue(layout.TryFindSpot(Strict, Surface, rod, out Vector2 uv, out float yaw));

            Assert.AreEqual(90f, yaw, 1e-4f, "the only orientation that fits");
            Assert.IsTrue(layout.TryPlace("rod", Strict, Surface, rod, uv, yaw));
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

            Assert.IsFalse(layout.TryFindSpot(Strict, Surface, rod, out _, out _,
                                              ignoreItemId: null, allowTurns: false));

            Assert.IsTrue(layout.TryFindSpot(Strict, Surface, rod, out _, out _,
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

        // ── Overhang: the rack and the back panels take items bigger than themselves ──

        /// <summary>The rack as wired: 0.80 x 0.60 m -> 8 x 6 cells with a (0.04, 0.03) hem.</summary>
        private static readonly Vector2 RackSize = new(0.80f, 0.60f);

        /// <summary>The back panel as wired: 0.27 x 0.54 m -> exactly 3 x 6 cells, zero hem.</summary>
        private static readonly Vector2 BackSize = new(0.27f, 0.54f);

        /// <summary>The wing pack's derived block: 6 cells wide, 14 long — longer than any face.</summary>
        private static readonly PackShape Oversized = PackShape.Rect(6, 14);

        [Test]
        public void AnOversizedRectangleOverhangsTheRackAndOccupiesItsFullSpan()
        {
            var layout = new PackLayout();

            // A quarter turn lays the long side along the rack's u axis, the one axis that
            // allows overhang; the block centre of the clamped 8 x 6 span is (0.40, 0.30).
            Assert.IsTrue(layout.TryPlace("craft", PackSurfaceId.Rack, RackSize, Oversized,
                                          new Vector2(0.40f, 0.30f), 90f));

            Assert.IsTrue(layout.TryOccupancy("craft", out _, out Vector2Int origin,
                                              out PackShape oriented));
            Assert.AreEqual(new Vector2Int(0, 0), origin);
            Assert.AreEqual(8, oriented.Width, "occupies the whole span it hangs past");
            Assert.AreEqual(6, oriented.Height);

            Assert.AreEqual(0.40f, layout.Placements[0].Uv.x, 1e-4f);
            Assert.AreEqual(0.30f, layout.Placements[0].Uv.y, 1e-4f);

            // First-fit — the world-pickup path — reaches the same answer on its own.
            var fresh = new PackLayout();
            Assert.IsTrue(fresh.TryFindSpot(PackSurfaceId.Rack, RackSize, Oversized,
                                            out _, out float yaw));
            Assert.AreEqual(90f, yaw, 1e-4f);
        }

        [Test]
        public void StrictFacesRefuseOversizedShapes()
        {
            var layout = new PackLayout();
            var leaf = new Vector2(0.78f, 0.50f);   // 8 x 5 cells

            Assert.IsFalse(layout.TryPlace("craft", PackSurfaceId.Leaf, leaf, Oversized,
                                           new Vector2(0.39f, 0.25f), 90f));
            Assert.IsFalse(layout.TryFindSpot(PackSurfaceId.Leaf, leaf, Oversized, out _, out _));

            // Overhang belongs to the rack (u only) and the back panels (both axes); every other
            // face refuses the same way the leaf does.
            Assert.IsFalse(layout.TryFindSpot(PackSurfaceId.WingLeft, new Vector2(0.36f, 0.54f),
                                              Oversized, out _, out _));
            Assert.IsFalse(layout.TryFindSpot(PackSurfaceId.LongGoods, new Vector2(1.62f, 0.09f),
                                              Oversized, out _, out _));
        }

        [Test]
        public void AClickOnTheOverhangingEndStillFindsTheItem()
        {
            var layout = new PackLayout();
            layout.TryPlace("craft", PackSurfaceId.Rack, RackSize, Oversized,
                            new Vector2(0.40f, 0.30f), 90f);

            // Past the panel's edge along u — where the craft's nose hangs.
            Assert.IsTrue(layout.TryFindAt(PackSurfaceId.Rack, RackSize, new Vector2(0.95f, 0.30f),
                                           out PackPlacement found));
            Assert.AreEqual("craft", found.ItemId);

            // The rack's v axis stays strict: off the side is off the pack.
            Assert.IsFalse(layout.TryFindAt(PackSurfaceId.Rack, RackSize,
                                            new Vector2(0.40f, 0.70f), out _));
        }

        [Test]
        public void AnOversizedRectangleOverhangsTheBackPanelOnBothAxes()
        {
            var layout = new PackLayout();

            // 5 x 8 on the 3 x 6 panel: both axes clamp, so the item occupies the WHOLE face and
            // hangs evenly past every edge. Block centre of the clamped span is (0.135, 0.27).
            Assert.IsTrue(layout.TryPlace("gear", Left, BackSize, PackShape.Rect(5, 8),
                                          new Vector2(0.135f, 0.27f), 0f));

            Assert.IsTrue(layout.TryOccupancy("gear", out _, out Vector2Int origin,
                                              out PackShape oriented));
            Assert.AreEqual(new Vector2Int(0, 0), origin);
            Assert.AreEqual(3, oriented.Width, "occupies the whole span it hangs past");
            Assert.AreEqual(6, oriented.Height, "on BOTH axes, unlike the rack");

            Assert.AreEqual(0.135f, layout.Placements[0].Uv.x, 1e-4f);
            Assert.AreEqual(0.27f, layout.Placements[0].Uv.y, 1e-4f);

            // First-fit — the world-pickup path — reaches the same answer on its own.
            var fresh = new PackLayout();
            Assert.IsTrue(fresh.TryFindSpot(Left, BackSize, PackShape.Rect(5, 8), out _, out _));
        }

        [Test]
        public void AVerticalOverhangOccupiesItsFullColumnButNotItsNeighbours()
        {
            var layout = new PackLayout();

            // 2 x 8 on the 3 x 6 panel: only v clamps. The block centre of a 2-wide block at
            // origin x = 1 is 0.09 + 0.18 / 2 = 0.18.
            Assert.IsTrue(layout.TryPlace("rod", Left, BackSize, PackShape.Rect(2, 8),
                                          new Vector2(0.18f, 0.27f), 0f));

            Assert.IsTrue(layout.TryOccupancy("rod", out _, out Vector2Int origin,
                                              out PackShape oriented));
            Assert.AreEqual(new Vector2Int(1, 0), origin);
            Assert.AreEqual(2, oriented.Width, "short enough along u to stay unclamped");
            Assert.AreEqual(6, oriented.Height);
            Assert.AreEqual(0.27f, layout.Placements[0].Uv.y, 1e-4f);

            // The clamp fills the overhung item's own columns and nothing else: the column it
            // does not cross is still usable.
            Assert.IsTrue(layout.TryPlace("mug", Left, BackSize, PackShape.Rect(1, 1),
                                          new Vector2(0.045f, 0.045f), 0f));
        }

        [Test]
        public void AClickPastTheBackPanelEdgeFindsOnlyTheItemUnderIt()
        {
            var layout = new PackLayout();

            // Columns 0-1, all six rows: 2 x 8 clamps to 2 x 6, block centre (0.09, 0.27).
            Assert.IsTrue(layout.TryPlace("rod", Left, BackSize, PackShape.Rect(2, 8),
                                          new Vector2(0.09f, 0.27f), 0f));

            // Past the top edge, over the item: cell (0, 6) clamps to (0, 5), which is filled.
            Assert.IsTrue(layout.TryFindAt(Left, BackSize, new Vector2(0.05f, 0.60f),
                                           out PackPlacement found));
            Assert.AreEqual("rod", found.ItemId);

            // Past the top edge but BESIDE the item: (2, 6) clamps to (2, 5), which nothing
            // fills — the clamp resolves the click, the occupancy still decides.
            Assert.IsFalse(layout.TryFindAt(Left, BackSize, new Vector2(0.25f, 0.60f), out _));

            // Past the left edge: u clamps too, and the left column is filled.
            Assert.IsTrue(layout.TryFindAt(Left, BackSize, new Vector2(-0.03f, 0.27f), out _));
        }
    }
}
