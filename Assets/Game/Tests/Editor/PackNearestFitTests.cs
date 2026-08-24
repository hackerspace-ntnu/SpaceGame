using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The magnet-snap search: given a cursor point, the nearest legal placement on the face.
    /// Same hemless 10 x 8 surface as <c>PackLayoutTests</c>, so every expected uv is
    /// arithmetic anyone can check: cell (i, j) of a w x h block is centred at
    /// ((i + w/2) * 0.09, (j + h/2) * 0.09).
    /// </summary>
    public class PackNearestFitTests
    {
        private static readonly Vector2 Surface = new(0.90f, 0.72f);

        /// <summary>Two whole cells wide — a 2 x 8 face, where a 3 x 1 item can only stand up.</summary>
        private static readonly Vector2 Narrow = new(0.18f, 0.72f);

        /// <summary>The rack's own size: 8 x 6 whole cells, with a 40 x 30 mm hem left over.</summary>
        private static readonly Vector2 RackSize = new(0.80f, 0.60f);

        private const PackSurfaceId Left = PackSurfaceId.BackPanelLeft;

        /// <summary>The one face <see cref="PackOverhang"/> lets an item hang off.</summary>
        private const PackSurfaceId Rack = PackSurfaceId.Rack;

        private static readonly PackShape TwoByTwo = PackShape.Rect(2, 2);
        private static readonly PackShape ThreeByOne = PackShape.Rect(3, 1);

        [Test]
        public void OnAnEmptyFaceTheNearestSpotIsTheCursorsOwnCell()
        {
            var layout = new PackLayout();

            bool found = layout.TryFindNearest(Left, Surface, TwoByTwo, new Vector2(0.2f, 0.3f),
                                               preferredYaw: 0f, allowTurns: true,
                                               out Vector2 uv, out float yaw);

            Assert.IsTrue(found);
            Assert.AreEqual(0f, yaw);
            // Where PackLayout.Snap would have put it: the same answer as the old strict snap.
            Vector2 snapped = PackLayout.Snap(Left, Surface, TwoByTwo, new Vector2(0.2f, 0.3f), 0f);
            Assert.AreEqual(snapped, uv);
        }

        [Test]
        public void ACursorOverAPlacedItemSnapsToTheNearestFreeBlock()
        {
            var layout = new PackLayout();

            // Obstacle: 2 x 2 at origin (2, 2), centre (0.27, 0.27).
            Assert.IsTrue(layout.TryPlace("obstacle", Left, Surface, TwoByTwo,
                                          new Vector2(0.27f, 0.27f), 0f));

            // Cursor just below the obstacle's centre: the free block at origin (2, 0),
            // centre (0.27, 0.09), is the unique nearest legal spot.
            bool found = layout.TryFindNearest(Left, Surface, TwoByTwo, new Vector2(0.27f, 0.20f),
                                               preferredYaw: 0f, allowTurns: true,
                                               out Vector2 uv, out float yaw);

            Assert.IsTrue(found);
            Assert.AreEqual(0f, yaw);
            Assert.AreEqual(0.27f, uv.x, 1e-4f);
            Assert.AreEqual(0.09f, uv.y, 1e-4f);
        }

        [Test]
        public void ThePreferredYawWinsWheneverItFitsAtAll()
        {
            var layout = new PackLayout();

            // Empty face: both orientations of a 3 x 1 fit everywhere. The search must not
            // "improve" on the player's chosen 90.
            bool found = layout.TryFindNearest(Left, Surface, ThreeByOne, new Vector2(0.3f, 0.3f),
                                               preferredYaw: 90f, allowTurns: true,
                                               out _, out float yaw);

            Assert.IsTrue(found);
            Assert.AreEqual(90f, yaw);
        }

        [Test]
        public void AShapeThatOnlyFitsTurnedIsTurned()
        {
            var layout = new PackLayout();

            // 2 x 8 face: a 3 x 1 cannot lie down, but stands up at 90.
            bool found = layout.TryFindNearest(Left, Narrow, ThreeByOne, new Vector2(0.09f, 0.3f),
                                               preferredYaw: 0f, allowTurns: true,
                                               out _, out float yaw);

            Assert.IsTrue(found);
            Assert.AreEqual(90f, yaw);
        }

        [Test]
        public void ForbiddenFromTurningItReportsNoRoomInstead()
        {
            var layout = new PackLayout();

            bool found = layout.TryFindNearest(Left, Narrow, ThreeByOne, new Vector2(0.09f, 0.3f),
                                               preferredYaw: 0f, allowTurns: false,
                                               out _, out _);

            Assert.IsFalse(found);
        }

        [Test]
        public void AFullFaceReportsNoRoom()
        {
            var layout = new PackLayout();

            Assert.IsTrue(layout.TryPlace("slab", Left, Surface, PackShape.Rect(10, 8),
                                          new Vector2(0.45f, 0.36f), 0f));

            bool found = layout.TryFindNearest(Left, Surface, TwoByTwo, new Vector2(0.2f, 0.2f),
                                               preferredYaw: 0f, allowTurns: true,
                                               out _, out _);

            Assert.IsFalse(found);
        }

        [Test]
        public void TheItemInTheAirIsNotAnObstacleToItself()
        {
            var layout = new PackLayout();

            // The dragged item is still recorded at origin (2, 2). With its own id excluded,
            // the nearest spot under a cursor over its own cells is exactly where it lies.
            Assert.IsTrue(layout.TryPlace("held", Left, Surface, TwoByTwo,
                                          new Vector2(0.27f, 0.27f), 0f));

            bool found = layout.TryFindNearest(Left, Surface, TwoByTwo, new Vector2(0.27f, 0.27f),
                                               preferredYaw: 0f, allowTurns: true,
                                               out Vector2 uv, out _, ignoreItemId: "held");

            Assert.IsTrue(found);
            Assert.AreEqual(0.27f, uv.x, 1e-4f);
            Assert.AreEqual(0.27f, uv.y, 1e-4f);
        }

        [Test]
        public void AMaskOnlyClashesWhereItIsFilled()
        {
            var layout = new PackLayout();

            // A single filled cell at (1, 1) (a 1 x 1 block, centre (0.135, 0.135)).
            Assert.IsTrue(layout.TryPlace("peg", Left, Surface, PackShape.Rect(1, 1),
                                          new Vector2(0.135f, 0.135f), 0f));

            // An L missing its (1, 1) corner: filled (0,0), (1,0), (0,1). Its bounding block
            // CAN sit at origin (0, 0) because the hole lands exactly on the peg.
            var l = PackShape.FromMask(2, 2, new[] { true, true, true, false });

            bool found = layout.TryFindNearest(Left, Surface, l, new Vector2(0.09f, 0.09f),
                                               preferredYaw: 0f, allowTurns: false,
                                               out Vector2 uv, out _);

            Assert.IsTrue(found);
            // Block centre of a 2 x 2 at origin (0, 0).
            Assert.AreEqual(0.09f, uv.x, 1e-4f);
            Assert.AreEqual(0.09f, uv.y, 1e-4f);
        }

        /// <summary>
        /// The overhang rule reaches the magnet the same way it reaches the fit test and the
        /// snap. It has to: the rack is where the long gear goes, so an item the search declined
        /// to clamp would be one the player could never magnet onto the only face built to take
        /// it — and a search that clamped differently from <see cref="PackLayout.TryPlace"/>
        /// would name a spot the placement then refused.
        /// </summary>
        [Test]
        public void AnOversizedItemMagnetsOntoTheRacksFullSpan()
        {
            var layout = new PackLayout();

            // Ten cells wide on an eight-cell face. PackOverhang clamps it to the full span, so
            // there is exactly one column it can sit in and the answer may only vary in v.
            PackShape longGoods = PackShape.Rect(10, 1);

            bool found = layout.TryFindNearest(Rack, RackSize, longGoods, new Vector2(0.40f, 0.26f),
                                               preferredYaw: 0f, allowTurns: false,
                                               out Vector2 uv, out float yaw);

            Assert.IsTrue(found);
            Assert.AreEqual(0f, yaw);

            // Block centre of the clamped 8 x 1 at origin (0, 2): hem (0.04, 0.03), plus half
            // the span across and two and a half cells up.
            Assert.AreEqual(0.40f, uv.x, 1e-4f);
            Assert.AreEqual(0.255f, uv.y, 1e-4f);

            // And the spot the search named is one the layout really takes — the two agree
            // about the clamp, which is the whole point of asking.
            Assert.IsTrue(layout.TryPlace("skis", Rack, RackSize, longGoods, uv, yaw));
        }

        [Test]
        public void AFaceThatForbidsOverhangRefusesTheSameOversizedItem()
        {
            var layout = new PackLayout();

            // The identical shape and the identical grid, differing only in which face it is:
            // ten cells will not go on eight anywhere but the rack.
            bool found = layout.TryFindNearest(Left, RackSize, PackShape.Rect(10, 1),
                                               new Vector2(0.40f, 0.26f),
                                               preferredYaw: 0f, allowTurns: false,
                                               out _, out _);

            Assert.IsFalse(found);
        }
    }
}
