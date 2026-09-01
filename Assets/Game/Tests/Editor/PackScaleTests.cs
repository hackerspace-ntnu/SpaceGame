using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The one property the 2026-09-01 enlargement had to have, and the one number it could have
    /// got wrong.
    ///
    /// <para>
    /// The enlargement is a <b>similarity transform</b>: every length in the physical inventory
    /// multiplied by <see cref="PackScale.Factor"/>, no count multiplied by anything. That is what
    /// let 255 cells stay 255 cells, every authored <see cref="PackShape"/> mask stay valid, and
    /// every save from before the change be migrated by multiplying two floats. If it ever stops
    /// being a similarity transform — a cell scaled without its faces, a face scaled without the
    /// cell — nothing throws: the pack simply holds a different amount of gear than it did, and
    /// the first person to notice is a player whose kit has rearranged itself.
    /// </para>
    /// </summary>
    public class PackScaleTests
    {
        /// <summary>
        /// Metres of hem that still count as none.
        ///
        /// <para>
        /// A face fills its rectangle when nothing is left over that a cell could have used, and
        /// that is a claim about millimetres, not about bit patterns: the same rectangle written
        /// as <c>30 * PackGrid.Cell</c> and as <c>4.05</c> is the same face and need not be the
        /// same float. A hem worth the name is a fraction of a 135 mm cell; a tenth of a
        /// millimetre is arithmetic.
        /// </para>
        /// </summary>
        private const float NoHem = 1e-4f;

        /// <summary>
        /// <see cref="PackGrid.Cell"/> is written as a literal rather than as
        /// <c>LegacyCell * Factor</c>, because it is a <c>const</c> read by eye off the model and
        /// because <c>0.09f * 1.5f</c> in float need not land on the same bit pattern as
        /// <c>0.135f</c>. This is the assertion that pays for that choice: change one of the three
        /// numbers and this is what says so.
        /// </summary>
        [Test]
        public void TheCellIsTheLegacyCellTimesTheFactor()
        {
            Assert.AreEqual(PackScale.LegacyCell * PackScale.Factor, PackGrid.Cell, 1e-6f,
                "PackGrid.Cell, PackScale.LegacyCell and PackScale.Factor have drifted apart. " +
                "Every save written before the enlargement is migrated by multiplying its uvs by " +
                "Factor, so a mismatch here silently misplaces every item in every old save.");
        }

        /// <summary>
        /// The property the migration depends on: scaling a surface and a uv by the same factor
        /// leaves the item on the cell it was already on. Asserted on the cell index rather than on
        /// the uv, because "the same cell" is the thing the player sees and the uv is only how it
        /// is written down.
        /// </summary>
        [Test]
        public void ScalingASurfaceAndItsUvTogetherKeepsTheCell()
        {
            // 8 x 8 cells at the ORIGINAL cell, which is what a pre-enlargement save was written
            // against. The leaf's own shape, so the arithmetic is the shipped one.
            var oldSurface = new Vector2(8f * PackScale.LegacyCell, 8f * PackScale.LegacyCell);
            var newSurface = oldSurface * PackScale.Factor;

            var block = new Vector2Int(2, 3);

            for (int y = 0; y + block.y <= 8; y++)
            {
                for (int x = 0; x + block.x <= 8; x++)
                {
                    var origin = new Vector2Int(x, y);

                    // What the old build would have stored, in the old frame.
                    Vector2 oldUv = BlockCentre(oldSurface, origin, block, PackScale.LegacyCell);

                    // What PackSaveCodec.Restore hands to PackLayout for a pre-v3 payload.
                    Vector2 migrated = oldUv * PackScale.Factor;

                    Assert.AreEqual(origin, PackGrid.BlockOrigin(newSurface, migrated, block),
                                    $"a {block.x}x{block.y} item saved on cell {origin} came back " +
                                    "on a different cell after the enlargement");
                }
            }
        }

        /// <summary>
        /// A face keeps its cell count through the enlargement. This is the whole claim that
        /// capacity did not move, made against every shipped face at once.
        /// </summary>
        [Test]
        public void EveryShippedFaceKeepsItsCellCount()
        {
            // The rig's seven faces and the wall, as CELL COUNTS — the authored quantity. The
            // metres in ExpeditionRigWiring.SurfaceTable and InventoryWallBuilder are these times
            // the cell, and that is the direction the dependency runs.
            (int across, int up)[] faces =
            {
                (3, 6),   // BackPanelLeft
                (3, 6),   // BackPanelRight
                (8, 8),   // Leaf
                (4, 7),   // WingLeft
                (4, 7),   // WingRight
                (18, 1),  // LongGoods
                (9, 9),   // Rack
                (30, 22), // WallGrid
            };

            foreach ((int across, int up) in faces)
            {
                // Both ways a face's metres are actually written down: derived from the cell, which
                // is how the wiring scripts author one, and typed as the decimal it comes to, which
                // is how a face resized by hand in the inspector arrives. They are not obliged to
                // be the same float — 3 x 0.135f is 0.40500003 and 0.405f is 0.40500000 — and a
                // face that fills its rectangle one way round has to fill it the other way too.
                Fills(across, up, new Vector2(across * PackGrid.Cell, up * PackGrid.Cell));
                Fills(across, up, new Vector2(Typed(across), Typed(up)));
            }
        }

        /// <summary>A face of this many cells across and up fills its rectangle edge to edge.</summary>
        private static void Fills(int across, int up, Vector2 size)
        {
            Vector2Int cells = PackGrid.CellsOn(size);

            Assert.AreEqual(across, cells.x, $"{across}x{up} face lost columns");
            Assert.AreEqual(up, cells.y, $"{across}x{up} face lost rows");

            // NOT Assert.AreEqual(Vector2.zero, hem). That comparison is bitwise, and it prints
            // both sides through Vector2.ToString, which is "F2" — so a hem of 1e-8 m fails it and
            // reports "(0.00, 0.00)" against "(0.00, 0.00)", naming neither the axis nor the
            // amount. Asserted in metres, with the amount in the message, instead.
            Vector2 hem = PackGrid.Hem(size);

            Assert.AreEqual(0f, Mathf.Max(hem.x, hem.y), NoHem,
                            $"{across}x{up} face no longer fills edge to edge: it is inset by " +
                            $"{hem.x * 1000f:0.###} x {hem.y * 1000f:0.###} mm of hem.");
        }

        /// <summary>
        /// The metres somebody reading this many cells off the model would type into a
        /// <c>PackSurface</c>: the exact decimal, rather than the float the multiply lands on.
        /// </summary>
        private static float Typed(int cells) =>
            (float)System.Math.Round(cells * (double)PackGrid.Cell, 6);

        /// <summary>
        /// The rig is still 255 cells. Stated as its own test because it is the number the docs
        /// quote, and a doc quoting a number nothing checks is how a doc goes stale.
        /// </summary>
        [Test]
        public void TheRigStillHolds255Cells()
        {
            int total = 3 * 6 + 3 * 6 + 8 * 8 + 4 * 7 + 4 * 7 + 18 * 1 + 9 * 9;

            Assert.AreEqual(255, total);
        }

        /// <summary>The middle of a block of cells, at an arbitrary cell size.</summary>
        private static Vector2 BlockCentre(Vector2 surface, Vector2Int origin, Vector2Int block,
                                           float cell)
        {
            // Deliberately NOT PackGrid.BlockCentreUv: that one uses today's cell, and the point of
            // this arithmetic is to produce a uv in the frame of a build that had a different one.
            var hem = new Vector2(
                Mathf.Max(0f, (surface.x - Mathf.Floor(surface.x / cell + 1e-4f) * cell) * 0.5f),
                Mathf.Max(0f, (surface.y - Mathf.Floor(surface.y / cell + 1e-4f) * cell) * 0.5f));

            return hem + new Vector2((origin.x + block.x * 0.5f) * cell,
                                     (origin.y + block.y * 0.5f) * cell);
        }
    }
}
