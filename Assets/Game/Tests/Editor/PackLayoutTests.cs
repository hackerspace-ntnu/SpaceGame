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
    /// The surface here is exactly 10 x 8 cells with no hem, so every expected uv in this file is
    /// arithmetic anyone can check: cell <c>(i, j)</c> of a <c>w x h</c> item is centred at
    /// <c>((i + w/2) * cell, (j + h/2) * cell)</c>. The hem case is <c>PackSurfaceTests</c>'s.
    /// </para>
    /// <para>
    /// The figures are written in the cell the suite was authored against (0.09 m) and put through
    /// <see cref="M"/>, so they follow the cell rather than pinning it. See that method.
    /// </para>
    /// </summary>
    public class PackLayoutTests
    {
        /// <summary>
        /// A length that was authored against the pack's ORIGINAL 0.09 m cell, restated at
        /// whatever the cell is today.
        ///
        /// <para>
        /// Every metre figure in this file — the surface, the asked uv, the expected snap — is a
        /// position on a lattice, and the arithmetic in the comments beside them is written in
        /// units of that lattice. The 2026-09-01 enlargement multiplied the cell by
        /// <see cref="PackScale.Factor"/> and multiplied nothing else about the rules, so wrapping
        /// the numbers rather than re-typing them is what keeps the suite testing the ARITHMETIC
        /// instead of a particular scale — and what makes the next scale change a one-line edit to
        /// <see cref="PackScale"/> rather than a sweep through six test files.
        /// </para>
        /// </summary>
        private static float M(float metresAtTheOriginalCell) =>
            metresAtTheOriginalCell * (PackGrid.Cell / PackScale.LegacyCell);

        private static readonly Vector2 Surface = new(M(0.90f), M(0.72f));

        private const PackSurfaceId Left = PackSurfaceId.BackPanelLeft;
        private const PackSurfaceId Right = PackSurfaceId.BackPanelRight;

        /// <summary>
        /// A face the overhang rule leaves strict — which, since 2026-09-05, is every face but the
        /// rack. The rule keys on the face's IDENTITY, not its size — see
        /// <c>PackOverhang.Axes</c> — so the leaf at this suite's own 10 x 8 cells keeps every uv
        /// here checkable by the same arithmetic.
        /// </summary>
        private const PackSurfaceId Strict = PackSurfaceId.Leaf;

        /// <summary>
        /// The dead zone the hand snaps with: how far past a cell boundary, in cells, the cursor
        /// has to travel before the ghost moves over. The value is the hand's own business
        /// (<c>PackHandController.SnapDeadbandCells</c>); the arithmetic under test holds for any
        /// value under half a cell.
        /// </summary>
        private const float Deadband = 0.25f;

        private static readonly PackShape TwoByTwo = PackShape.Rect(2, 2);
        private static readonly PackShape FourByFour = PackShape.Rect(4, 4);

        [Test]
        public void APlacedItemSnapsToACellAndIsReadableBack()
        {
            var layout = new PackLayout();

            bool placed = layout.TryPlace("item-a", Left, Surface, TwoByTwo,
                                          new Vector2(M(0.2f), M(0.3f)), 0f);

            Assert.IsTrue(placed);
            Assert.AreEqual(1, layout.Placements.Count);

            PackPlacement p = layout.Placements[0];

            Assert.AreEqual("item-a", p.ItemId);
            Assert.AreEqual(Left, p.Surface);

            // (0.2, 0.3) is nearest the block whose lowest cell is (1, 2), centred at (0.18, 0.27).
            Assert.AreEqual(M(0.18f), p.Uv.x, 1e-4f, "the uv stored is the snapped one, not the asked one");
            Assert.AreEqual(M(0.27f), p.Uv.y, 1e-4f);
        }

        [Test]
        public void AnOverlappingItemIsRefusedAndChangesNothing()
        {
            var layout = new PackLayout();

            layout.TryPlace("item-a", Left, Surface, FourByFour, new Vector2(M(0.2f), M(0.3f)), 0f);

            bool second = layout.TryPlace("item-b", Left, Surface, FourByFour,
                                          new Vector2(M(0.2f), M(0.3f)), 0f);

            Assert.IsFalse(second);
            Assert.AreEqual(1, layout.Placements.Count);
            Assert.AreEqual("item-a", layout.Placements[0].ItemId);

            // The same clash on a different surface is not a clash at all.
            Assert.IsTrue(layout.TryPlace("item-b", Right, Surface, FourByFour,
                                          new Vector2(M(0.2f), M(0.3f)), 0f));
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

            layout.TryPlace("item-a", Left, Surface, FourByFour, new Vector2(M(0.2f), M(0.3f)), 0f);

            Assert.IsFalse(layout.TryPlace("item-b", Left, Surface, FourByFour,
                                           new Vector2(M(0.2f), M(0.3f)), 0f));

            Assert.IsTrue(layout.Remove("item-a"));
            Assert.IsFalse(layout.Remove("item-a"));

            Assert.IsTrue(layout.TryPlace("item-b", Left, Surface, FourByFour,
                                          new Vector2(M(0.2f), M(0.3f)), 0f));
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

            Assert.IsFalse(layout.CanPlace(Strict, Surface, rod, new Vector2(M(0.45f), M(0.36f)), 0f),
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

            layout.TryPlace("item-a", Left, Surface, TwoByTwo, new Vector2(M(0.27f), M(0.27f)), 0f);

            // One cell to the right, which overlaps its own current cells on the whole left column.
            bool moved = layout.TryMove("item-a", Left, Surface, TwoByTwo,
                                        new Vector2(M(0.36f), M(0.27f)), 0f);

            Assert.IsTrue(moved);
            Assert.AreEqual(1, layout.Placements.Count);
            Assert.AreEqual(M(0.36f), layout.Placements[0].Uv.x, 1e-4f);
        }

        [Test]
        public void AnItemHangingOffTheEdgeIsRefused()
        {
            var layout = new PackLayout();

            Assert.IsFalse(layout.TryPlace("item-a", Left, Surface, TwoByTwo,
                                           new Vector2(M(0.89f), M(0.36f)), 0f),
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
                                          new Vector2(M(0.3f), M(0.3f)), 39f));

            Assert.AreEqual(0f, layout.Placements[0].Yaw, 1e-4f);

            Assert.IsTrue(layout.TryPlace("b", Left, Surface, PackShape.Rect(3, 1),
                                          new Vector2(M(0.7f), M(0.3f)), 71f));

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

        // ── Overhang: the rack takes items longer than itself; nothing else does ──

        /// <summary>The rack as wired: 0.80 x 0.60 m -> 8 x 6 cells with a (0.04, 0.03) hem.</summary>
        private static readonly Vector2 RackSize = new(M(0.80f), M(0.60f));

        /// <summary>
        /// A side back panel as wired since 2026-09-05: 0.18 x 0.54 m -> exactly 2 x 6 cells,
        /// zero hem. One column narrower than the socket between them.
        /// </summary>
        private static readonly Vector2 BackSize = new(M(0.18f), M(0.54f));

        /// <summary>The wing pack's derived block: 6 cells wide, 14 long — longer than any face.</summary>
        private static readonly PackShape Oversized = PackShape.Rect(6, 14);

        [Test]
        public void AnOversizedRectangleOverhangsTheRackAndOccupiesItsFullSpan()
        {
            var layout = new PackLayout();

            // A quarter turn lays the long side along the rack's u axis, the one axis that
            // allows overhang; the block centre of the clamped 8 x 6 span is (0.40, 0.30).
            Assert.IsTrue(layout.TryPlace("craft", PackSurfaceId.Rack, RackSize, Oversized,
                                          new Vector2(M(0.40f), M(0.30f)), 90f));

            Assert.IsTrue(layout.TryOccupancy("craft", out _, out Vector2Int origin,
                                              out PackShape oriented));
            Assert.AreEqual(new Vector2Int(0, 0), origin);
            Assert.AreEqual(8, oriented.Width, "occupies the whole span it hangs past");
            Assert.AreEqual(6, oriented.Height);

            Assert.AreEqual(M(0.40f), layout.Placements[0].Uv.x, 1e-4f);
            Assert.AreEqual(M(0.30f), layout.Placements[0].Uv.y, 1e-4f);

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
            var leaf = new Vector2(M(0.78f), M(0.50f));   // 8 x 5 cells

            Assert.IsFalse(layout.TryPlace("craft", PackSurfaceId.Leaf, leaf, Oversized,
                                           new Vector2(M(0.39f), M(0.25f)), 90f));
            Assert.IsFalse(layout.TryFindSpot(PackSurfaceId.Leaf, leaf, Oversized, out _, out _));

            // Overhang belongs to the rack alone, and only along u; every other face refuses the
            // same way the leaf does — the back panels included, since 2026-09-05.
            Assert.IsFalse(layout.TryFindSpot(PackSurfaceId.WingLeft, new Vector2(M(0.36f), M(0.54f)),
                                              Oversized, out _, out _));
            Assert.IsFalse(layout.TryFindSpot(PackSurfaceId.LongGoods, new Vector2(M(1.62f), M(0.09f)),
                                              Oversized, out _, out _));
            Assert.IsFalse(layout.TryFindSpot(Left, BackSize, Oversized, out _, out _));
            Assert.IsFalse(layout.TryFindSpot(Right, BackSize, Oversized, out _, out _));
        }

        [Test]
        public void AClickOnTheOverhangingEndStillFindsTheItem()
        {
            var layout = new PackLayout();
            layout.TryPlace("craft", PackSurfaceId.Rack, RackSize, Oversized,
                            new Vector2(M(0.40f), M(0.30f)), 90f);

            // Past the panel's edge along u — where the craft's nose hangs.
            Assert.IsTrue(layout.TryFindAt(PackSurfaceId.Rack, RackSize, new Vector2(M(0.95f), M(0.30f)),
                                           out PackPlacement found));
            Assert.AreEqual("craft", found.ItemId);

            // The rack's v axis stays strict: off the side is off the pack.
            Assert.IsFalse(layout.TryFindAt(PackSurfaceId.Rack, RackSize,
                                            new Vector2(M(0.40f), M(0.70f)), out _));
        }

        /// <summary>
        /// The back panels used to allow overhang on BOTH axes, which meant every rectangle in the
        /// game clamped down to the panel's span and was accepted: a 1.3 m launcher lay across a
        /// 0.57 m strip hanging off both ends, and the panel read as having far more room than it
        /// has. Since 2026-09-05 they are strict like every face but the rack — an item fits a back
        /// panel only if its own cells do.
        /// </summary>
        [Test]
        public void TheBackPanelsRefuseWhatDoesNotFitInsideThem()
        {
            var layout = new PackLayout();

            // 5 x 8 on the 2 x 6 panel would once have clamped to the whole face. Now it is
            // simply too big, at either quarter turn, on either panel, by either path.
            Assert.IsFalse(layout.TryPlace("gear", Left, BackSize, PackShape.Rect(5, 8),
                                           new Vector2(M(0.09f), M(0.27f)), 0f));
            Assert.IsFalse(layout.TryPlace("gear", Left, BackSize, PackShape.Rect(5, 8),
                                           new Vector2(M(0.09f), M(0.27f)), 90f));
            Assert.IsFalse(layout.TryFindSpot(Left, BackSize, PackShape.Rect(5, 8), out _, out _));
            Assert.IsFalse(layout.TryFindSpot(Right, BackSize, PackShape.Rect(5, 8), out _, out _));

            // One cell too long is still too long: 2 x 7 on a 2 x 6 panel.
            Assert.IsFalse(layout.TryFindSpot(Left, BackSize, PackShape.Rect(2, 7), out _, out _));

            // And what does fit, fits exactly: 2 x 6 fills the panel edge to edge, block centre
            // (0.09, 0.27), and nothing else can then land on it.
            Assert.IsTrue(layout.TryPlace("bottle", Left, BackSize, PackShape.Rect(2, 6),
                                          new Vector2(M(0.09f), M(0.27f)), 0f));
            Assert.AreEqual(M(0.09f), layout.Placements[0].Uv.x, 1e-4f);
            Assert.AreEqual(M(0.27f), layout.Placements[0].Uv.y, 1e-4f);
            Assert.IsFalse(layout.TryPlace("mug", Left, BackSize, PackShape.Rect(1, 1),
                                           new Vector2(M(0.045f), M(0.045f)), 0f));
        }

        [Test]
        public void AClickPastTheBackPanelEdgeFindsNothing()
        {
            var layout = new PackLayout();

            // The panel's own column 0, all six rows.
            Assert.IsTrue(layout.TryPlace("rod", Left, BackSize, PackShape.Rect(1, 6),
                                          new Vector2(M(0.045f), M(0.27f)), 0f));

            // Over the item: found.
            Assert.IsTrue(layout.TryFindAt(Left, BackSize, new Vector2(M(0.05f), M(0.30f)),
                                           out PackPlacement found));
            Assert.AreEqual("rod", found.ItemId);

            // Past the top edge and past the left edge: with no overhang there is nothing
            // hanging there to click on, so off the panel is off the pack on every axis.
            Assert.IsFalse(layout.TryFindAt(Left, BackSize, new Vector2(M(0.05f), M(0.60f)), out _));
            Assert.IsFalse(layout.TryFindAt(Left, BackSize, new Vector2(-M(0.03f), M(0.27f)), out _));
        }

        // ── Snapping with a held cell: the dead zone at a cell boundary ──────────

        /// <summary>
        /// The uv a cursor sits at when its block's exact, unrounded origin is
        /// <paramref name="exact"/> cells — the inverse of <c>PackGrid.BlockOrigin</c> before its
        /// rounding, on this suite's hem-free surface.
        /// </summary>
        private static Vector2 CursorAt(Vector2 exact, Vector2Int size) =>
            new((exact.x + size.x * 0.5f) * PackGrid.Cell,
                (exact.y + size.y * 0.5f) * PackGrid.Cell);

        /// <summary>
        /// A cursor resting on the seam between two cells re-rounds every frame — the camera's
        /// cursor parallax alone moves the hit point by a hair — and the ghost flickered between
        /// the two. Holding the cell the ghost is already on until the cursor is a dead zone PAST
        /// the boundary is what stops it, and a fresh snap of the same uv shows what it stopped.
        /// </summary>
        [Test]
        public void ASnapHoldsItsCellJustPastTheBoundary()
        {
            var held = new Vector2Int(3, 2);
            Vector2 heldUv = PackGrid.BlockCentreUv(Surface, held, TwoByTwo.Size);

            // 0.1 of a cell past the seam toward cell 4 on u, and the same past the seam toward
            // cell 1 on v: a fresh snap rounds both across, a held snap stays put on both.
            Vector2 cursor = CursorAt(new Vector2(3.6f, 1.4f), TwoByTwo.Size);

            Vector2 fresh = PackLayout.Snap(Strict, Surface, TwoByTwo, cursor, 0f);
            Assert.AreEqual(new Vector2Int(4, 1), PackGrid.BlockOrigin(Surface, fresh, TwoByTwo.Size),
                            "the control: without a held cell this uv rounds across the seam");

            Vector2 stuck = PackLayout.Snap(Strict, Surface, TwoByTwo, cursor, 0f, heldUv, Deadband);
            Assert.AreEqual(heldUv.x, stuck.x, 1e-5f, "held on u");
            Assert.AreEqual(heldUv.y, stuck.y, 1e-5f, "held on v");
        }

        [Test]
        public void ASnapLetsGoOfItsCellPastTheDeadZone()
        {
            var held = new Vector2Int(3, 2);
            Vector2 heldUv = PackGrid.BlockCentreUv(Surface, held, TwoByTwo.Size);

            // 0.3 of a cell past the seam on u — beyond the 0.25 dead zone — and dead centre of
            // the held cell on v: u moves over by exactly one cell, v stays.
            Vector2 cursor = CursorAt(new Vector2(3.8f, 2.0f), TwoByTwo.Size);

            Vector2 snapped = PackLayout.Snap(Strict, Surface, TwoByTwo, cursor, 0f, heldUv, Deadband);
            Assert.AreEqual(new Vector2Int(4, 2), PackGrid.BlockOrigin(Surface, snapped, TwoByTwo.Size));

            // Back inside the dead zone on the far side of the seam, the NEW cell is now the held
            // one and holds in turn — the hysteresis works in both directions.
            Vector2 back = CursorAt(new Vector2(3.4f, 2.0f), TwoByTwo.Size);
            Vector2 stillNew = PackLayout.Snap(Strict, Surface, TwoByTwo, back, 0f, snapped, Deadband);
            Assert.AreEqual(new Vector2Int(4, 2), PackGrid.BlockOrigin(Surface, stillNew, TwoByTwo.Size));
        }

        [Test]
        public void ASnapWithAHeldCellFollowsAFlickAcrossTheFace()
        {
            var held = new Vector2Int(3, 2);
            Vector2 heldUv = PackGrid.BlockCentreUv(Surface, held, TwoByTwo.Size);

            // Four cells away in one frame: the hold is a dead zone, not a tether.
            Vector2 cursor = CursorAt(new Vector2(7.1f, 5.9f), TwoByTwo.Size);

            Vector2 snapped = PackLayout.Snap(Strict, Surface, TwoByTwo, cursor, 0f, heldUv, Deadband);
            Vector2 fresh = PackLayout.Snap(Strict, Surface, TwoByTwo, cursor, 0f);

            Assert.AreEqual(fresh.x, snapped.x, 1e-5f);
            Assert.AreEqual(fresh.y, snapped.y, 1e-5f);
            Assert.AreEqual(new Vector2Int(7, 6), PackGrid.BlockOrigin(Surface, snapped, TwoByTwo.Size));
        }

        /// <summary>
        /// The held uv is a stored placement's uv, so the hold must recover the placement's own
        /// block exactly — the same idempotence <c>PackGrid.Snap</c> promises — or an item lifted
        /// off the mat would appear one cell over on the first frame it was carried, from a click
        /// that landed a little off its centre.
        /// </summary>
        [Test]
        public void ASnapHeldOnAPlacementStartsOnThatPlacementsCells()
        {
            var layout = new PackLayout();
            Assert.IsTrue(layout.TryPlace("item", Strict, Surface, FourByFour,
                                          new Vector2(M(0.41f), M(0.28f)), 0f));

            PackPlacement placed = layout.Placements[0];

            // The cursor is wherever the click landed: inside the item, 0.56 of a cell right of
            // its centre — which a fresh snap would round across to the next column.
            Vector2 cursor = placed.Uv + new Vector2(M(0.05f), -M(0.04f));
            Vector2 fresh = PackLayout.Snap(Strict, Surface, FourByFour, cursor, placed.Yaw);
            Assert.AreNotEqual(placed.Uv.x, fresh.x, "the control: a fresh snap does move it");

            Vector2 snapped = PackLayout.Snap(Strict, Surface, FourByFour, cursor, placed.Yaw,
                                              placed.Uv, Deadband);
            Assert.AreEqual(placed.Uv.x, snapped.x, 1e-5f);
            Assert.AreEqual(placed.Uv.y, snapped.y, 1e-5f);
        }
    }
}
