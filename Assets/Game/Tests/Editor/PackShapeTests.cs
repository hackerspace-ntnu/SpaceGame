using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The grid itself: cell masks, the snap, and the one behaviour the whole change exists for —
    /// two shapes interlocking in a space neither of their bounding boxes would fit in.
    ///
    /// <para>
    /// Every metre figure below is written in the cell the suite was authored against (0.09 m) and
    /// put through <see cref="M"/>, so it follows the cell rather than pinning it. See that method.
    /// </para>
    /// </summary>
    public class PackShapeTests
    {
        /// <summary>
        /// A length that was authored against the pack's ORIGINAL 0.09 m cell, restated at
        /// whatever the cell is today.
        ///
        /// <para>
        /// Every figure in this file is a length on a lattice, and the arithmetic in the comments
        /// beside them is written in units of that lattice. The 2026-09-01 enlargement multiplied
        /// the cell by <see cref="PackScale.Factor"/> and multiplied no COUNT by anything, so
        /// wrapping the numbers rather than re-typing them at 1.5x is what keeps this suite testing
        /// the division instead of a particular scale. The same helper, with the same reasoning,
        /// is in <c>PackLayoutTests</c>.
        /// </para>
        /// </summary>
        private static float M(float metresAtTheOriginalCell) =>
            metresAtTheOriginalCell * (PackGrid.Cell / PackScale.LegacyCell);

        /// <summary>2 x 3 cells exactly, so every cell index in this file is unambiguous.</summary>
        private static readonly Vector2 Narrow = new(M(0.18f), M(0.27f));

        /// <summary>10 x 8 cells exactly.</summary>
        private static readonly Vector2 Panel = new(M(0.90f), M(0.72f));

        private const PackSurfaceId Face = PackSurfaceId.Leaf;

        /// <summary>
        /// An L: the bottom row and the left of the row above it, with the top-right corner empty.
        /// Row-major, so the array reads bottom row first.
        /// </summary>
        private static PackShape LowerL() =>
            PackShape.FromMask(2, 2, new[] { true, true, true, false });

        /// <summary>The complement: the top row and the right of the row below it.</summary>
        private static PackShape UpperL() =>
            PackShape.FromMask(2, 2, new[] { false, true, true, true });

        private static Vector2 At(Vector2 surface, int x, int y, int w, int h) =>
            PackGrid.BlockCentreUv(surface, new Vector2Int(x, y), new Vector2Int(w, h));

        // ── The point of the exercise ────────────────────────────────────────

        /// <summary>
        /// <b>The test this feature exists for.</b> Two L-shaped items tile a 2 x 3 face perfectly,
        /// and their BOUNDING BOXES overlap by a whole row while doing it. Anything reasoning in
        /// rectangles — the separating-axis test this replaced included — refuses the second one.
        /// </summary>
        [Test]
        public void TwoLShapesInterlockWhereBoundingBoxesWouldNot()
        {
            var layout = new PackLayout();

            Assert.IsTrue(layout.TryPlace("lower", Face, Narrow, LowerL(), At(Narrow, 0, 0, 2, 2), 0f));

            // The proof that the boxes really do collide: a SOLID 2 x 2 in the same place is
            // refused, and the only difference between it and the L below is the empty corner.
            Assert.IsFalse(layout.CanPlace(Face, Narrow, PackShape.Rect(2, 2), At(Narrow, 0, 1, 2, 2), 0f),
                           "the bounding boxes overlap across the middle row");

            Assert.IsTrue(layout.TryPlace("upper", Face, Narrow, UpperL(), At(Narrow, 0, 1, 2, 2), 0f),
                          "the masks interlock: the lower L's empty corner is the upper L's foot");

            Assert.AreEqual(2, layout.Placements.Count);
        }

        /// <summary>
        /// The other half of the same rule: a hole is not a free pass. Where a filled cell of the
        /// incoming shape lands on a filled cell of something already down, it is refused however
        /// much empty space the two masks have between them.
        /// </summary>
        [Test]
        public void AMaskShapedItemIsRefusedWhenAnyOneOfItsCellsWouldClash()
        {
            var layout = new PackLayout();

            Assert.IsTrue(layout.TryPlace("lower", Face, Narrow, LowerL(), At(Narrow, 0, 0, 2, 2), 0f));

            // The upper L one row DOWN from where it interlocks. Its foot at (1,0) lands on the
            // lower L's own (1,0) — a single shared cell out of six, and enough.
            Assert.IsFalse(layout.CanPlace(Face, Narrow, UpperL(), At(Narrow, 0, 0, 2, 2), 0f));

            Assert.IsFalse(layout.TryPlace("upper", Face, Narrow, UpperL(), At(Narrow, 0, 0, 2, 2), 0f));
            Assert.AreEqual(1, layout.Placements.Count, "a refusal leaves the layout exactly as it was");
        }

        /// <summary>
        /// A HOLE, unlike a filled cell, may hang off the edge of the face. Demanding otherwise
        /// would refuse an L pushed into the corner it was drawn to fit.
        /// </summary>
        [Test]
        public void AnEmptyCellMayHangOffTheEdgeWhereAFilledOneMayNot()
        {
            var layout = new PackLayout();

            // Cell column 1 of this block is the top row of the face; column 2 would be off it.
            Vector2 topLeft = At(Narrow, 0, 2, 2, 2);

            Assert.IsFalse(layout.CanPlace(Face, Narrow, PackShape.Rect(2, 2), topLeft, 0f));

            // The same block with its whole top row empty fits, because nothing filled leaves the grid.
            PackShape footOnly = PackShape.FromMask(2, 2, new[] { true, true, false, false });

            Assert.IsTrue(layout.CanPlace(Face, Narrow, footOnly, topLeft, 0f));
        }

        /// <summary>
        /// Every request the pack sends names its item by a POINT, and the point has to be one the
        /// item is actually on. An L's block centre is the corner it does not fill, so sending the
        /// stored uv would make a mask-shaped item undraggable, undroppable and un-right-clickable
        /// with no error anywhere — the server would simply find nothing there.
        /// </summary>
        [Test]
        public void TheAnchorPointLandsOnACellTheItemActuallyFills()
        {
            var layout = new PackLayout();

            Vector2 centre = At(Narrow, 0, 0, 2, 2);

            Assert.IsTrue(layout.TryPlace("lower", Face, Narrow, LowerL(), centre, 0f));

            Assert.IsFalse(layout.TryFindAt(Face, Narrow, centre, out _),
                           "the block's own centre is the L's empty corner");

            Assert.IsTrue(layout.TryAnchorUv("lower", Narrow, out Vector2 anchor));
            Assert.IsTrue(layout.TryFindAt(Face, Narrow, anchor, out PackPlacement found));
            Assert.AreEqual("lower", found.ItemId);
        }

        // ── Snapping ─────────────────────────────────────────────────────────

        /// <summary>
        /// Snapping has to be a fixed point, because a placement is snapped on the way in, saved,
        /// read back and snapped again. If the second pass could move an item, an item would walk
        /// across the pack one reload at a time.
        /// </summary>
        [Test]
        public void SnappingIsIdempotent()
        {
            // A face with a hem, so the offset is exercised rather than a clean multiple of a cell.
            var hemmed = new Vector2(M(0.86f), M(0.72f));

            PackShape shape = PackShape.Rect(3, 2);

            var probes = new[]
            {
                new Vector2(M(0.213f), M(0.377f)), new Vector2(0f, 0f), new Vector2(M(0.5f), M(0.5f)),
                new Vector2(M(0.8599f), M(0.7199f)), new Vector2(M(0.1351f), M(0.0449f)),
            };

            foreach (Vector2 probe in probes)
            {
                Vector2 once = PackLayout.Snap(PackSurfaceId.Leaf, hemmed, shape, probe, 0f);
                Vector2 twice = PackLayout.Snap(PackSurfaceId.Leaf, hemmed, shape, once, 0f);

                Assert.AreEqual(once.x, twice.x, 1e-5f, $"snapping {probe} twice moved it");
                Assert.AreEqual(once.y, twice.y, 1e-5f, $"snapping {probe} twice moved it");
            }
        }

        /// <summary>And what it actually snaps TO, once, so the fixed point is the right one.</summary>
        [Test]
        public void SnappingLandsOnACellCentreOffsetByTheHem()
        {
            var hemmed = new Vector2(M(0.86f), M(0.72f));   // 9 x 8 cells, 0.28 of a cell of hem across

            Vector2 snapped = PackLayout.Snap(PackSurfaceId.Leaf, hemmed,
                                              PackShape.Rect(2, 2), new Vector2(M(0.2f), M(0.3f)), 0f);

            // Nearest block origin is (1, 2): 0.025 + 1 * 0.09 + 0.09 across, 0 + 2 * 0.09 + 0.09 up.
            Assert.AreEqual(M(0.205f), snapped.x, 1e-4f);
            Assert.AreEqual(M(0.27f), snapped.y, 1e-4f);
        }

        // ── Rotation ─────────────────────────────────────────────────────────

        /// <summary>
        /// A quarter turn maps <c>(x, y)</c> to <c>(Height - 1 - y, x)</c>, and four of them are
        /// the identity. Get the handedness wrong and every authored shape is mirrored, with
        /// nothing in the editor showing it.
        /// </summary>
        [Test]
        public void RotatingAMaskTurnsItTheWayAYawDoes()
        {
            PackShape l = LowerL();

            PackShape turned = l.Rotated(1);

            Assert.AreEqual(2, turned.Width);
            Assert.AreEqual(2, turned.Height);

            // (0,0) -> (1,0), (1,0) -> (1,1), (0,1) -> (0,0). The empty corner (1,1) goes to (0,1).
            Assert.IsTrue(turned[1, 0]);
            Assert.IsTrue(turned[1, 1]);
            Assert.IsTrue(turned[0, 0]);
            Assert.IsFalse(turned[0, 1]);

            PackShape back = l.Rotated(4);

            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    Assert.AreEqual(l[x, y], back[x, y], $"four turns changed cell {x},{y}");

            Assert.AreEqual(l.Rotated(2)[0, 0], l.Rotated(1).Rotated(1)[0, 0],
                            "two turns must be one turn twice");
        }

        [Test]
        public void ANonSquareRectangleSwapsItsAxesOnAQuarterTurn()
        {
            PackShape turned = PackShape.Rect(5, 2).Rotated(1);

            Assert.AreEqual(2, turned.Width);
            Assert.AreEqual(5, turned.Height);
            Assert.IsTrue(turned.IsRectangular);
        }

        // ── Derived shapes ───────────────────────────────────────────────────

        /// <summary>
        /// The default an unauthored item gets. It has to round UP — an item that overhangs the
        /// cells reserved for it lies through its neighbour — but not by a whole cell on a size
        /// that is already an exact multiple, which is what the tolerance is for.
        /// </summary>
        [Test]
        public void ADerivedShapeIsTheSmallestBlockTheFootprintFitsIn()
        {
            Assert.AreEqual(new Vector2Int(3, 2),
                            PackShape.ForFootprint(new Vector2(M(0.26f), M(0.12f))).Size,
                            "0.26 m is 2.89 cells, so three");

            Assert.AreEqual(new Vector2Int(2, 1),
                            PackShape.ForFootprint(new Vector2(M(0.18f), M(0.09f))).Size,
                            "an exact multiple must not gain a cell to float error");

            Assert.AreEqual(new Vector2Int(1, 1),
                            PackShape.ForFootprint(new Vector2(M(0.001f), M(0.001f))).Size,
                            "nothing occupies less than one cell");

            Assert.AreEqual(new Vector2Int(15, 1),
                            PackShape.ForFootprint(new Vector2(M(1.35f), M(0.04f))).Size,
                            "the LaserStaff at fifteen cells, which is why LongGoods is 18 long");
        }

        /// <summary>
        /// A blank mask is an authoring slip, not an item that occupies nothing. Honouring it
        /// literally would give the item a shape that clashes with nothing and stacks invisibly on
        /// top of everything else on the mat.
        /// </summary>
        [Test]
        public void AMaskWithNothingSwitchedOnFallsBackToASolidBlock()
        {
            PackShape blank = PackShape.FromMask(2, 2, new[] { false, false, false, false });

            Assert.IsTrue(blank.IsRectangular);
            Assert.AreEqual(4, blank.FilledCells);
        }

        // ── Cell arithmetic ──────────────────────────────────────────────────

        [Test]
        public void ARectangleDividesIntoTheCellCountTheGridClaims()
        {
            // Rectangles in the PROPORTIONS of the rig's faces rather than the shipped table, so
            // that every row here is a face that does NOT divide exactly and the rounding-down is
            // what is under test. The shipped rows, which do divide exactly, are pinned by
            // PackScaleTests.EveryShippedFaceKeepsItsCellCount.
            Assert.AreEqual(new Vector2Int(2, 5), PackGrid.CellsOn(new Vector2(M(0.26f), M(0.50f))), "back panel");
            Assert.AreEqual(new Vector2Int(8, 5), PackGrid.CellsOn(new Vector2(M(0.78f), M(0.50f))), "leaf");
            Assert.AreEqual(new Vector2Int(4, 4), PackGrid.CellsOn(new Vector2(M(0.38f), M(0.40f))), "wing");
            Assert.AreEqual(new Vector2Int(17, 1), PackGrid.CellsOn(new Vector2(M(1.60f), M(0.14f))), "long goods");
            Assert.AreEqual(new Vector2Int(8, 6), PackGrid.CellsOn(new Vector2(M(0.80f), M(0.60f))), "rack");
        }

        [Test]
        public void APointOnTheHemBelongsToNoCell()
        {
            var hemmed = new Vector2(M(0.86f), M(0.72f));   // 0.28 of a cell of hem at each end across

            Assert.IsFalse(PackGrid.OnGrid(hemmed, PackGrid.CellAt(hemmed, new Vector2(M(0.01f), M(0.3f)))));
            Assert.IsTrue(PackGrid.OnGrid(hemmed, PackGrid.CellAt(hemmed, new Vector2(M(0.03f), M(0.3f)))));
            Assert.IsFalse(PackGrid.OnGrid(hemmed, PackGrid.CellAt(hemmed, new Vector2(M(0.85f), M(0.3f)))));
        }

        /// <summary>A full face fills exactly, with nothing left over and no room for one more.</summary>
        [Test]
        public void APackedFaceRefusesTheNextItem()
        {
            var layout = new PackLayout();

            int placed = 0;

            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 5; x++)
                {
                    if (layout.TryPlace($"i{x}-{y}", Face, Panel, PackShape.Rect(2, 2),
                                        At(Panel, x * 2, y * 2, 2, 2), 0f)) placed++;
                }
            }

            Assert.AreEqual(20, placed, "10 x 8 cells takes exactly twenty 2 x 2 items");
            Assert.IsFalse(layout.TryFindSpot(Face, Panel, PackShape.Rect(1, 1), out _, out _));
        }
    }
}
