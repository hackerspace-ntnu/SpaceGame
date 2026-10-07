// The emote wheel's geometry and paging, which decide what a flick of the mouse plays.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class EmoteWheelLayoutTests
    {
        private const int Wedges = 8;
        private const float DeadZone = 10f;

        [Test]
        public void DirectionPicksTheWedgeItPointsIntoClockwiseFromTheTop()
        {
            Assert.AreEqual(0, EmoteWheelLayout.WedgeAt(Vector2.up * 100f, Wedges, DeadZone));
            Assert.AreEqual(2, EmoteWheelLayout.WedgeAt(Vector2.right * 100f, Wedges, DeadZone));
            Assert.AreEqual(4, EmoteWheelLayout.WedgeAt(Vector2.down * 100f, Wedges, DeadZone));
            Assert.AreEqual(6, EmoteWheelLayout.WedgeAt(Vector2.left * 100f, Wedges, DeadZone));

            // Boundaries sit half a wedge (22.5 degrees) either side of each centre.
            Assert.AreEqual(0, EmoteWheelLayout.WedgeAt(Rotated(22f), Wedges, DeadZone));
            Assert.AreEqual(1, EmoteWheelLayout.WedgeAt(Rotated(23f), Wedges, DeadZone));
            Assert.AreEqual(0, EmoteWheelLayout.WedgeAt(Rotated(-22f), Wedges, DeadZone));
            Assert.AreEqual(7, EmoteWheelLayout.WedgeAt(Rotated(-23f), Wedges, DeadZone));

            for (int wedge = 0; wedge < Wedges; wedge++)
            {
                Vector2 centre = EmoteWheelLayout.WedgeDirection(wedge, Wedges) * 100f;
                Assert.AreEqual(wedge, EmoteWheelLayout.WedgeAt(centre, Wedges, DeadZone),
                                "a wedge's own centre must select it, or the highlight and the pick disagree");
            }
        }

        [Test]
        public void TheDeadCentreSelectsNothing()
        {
            Assert.AreEqual(-1, EmoteWheelLayout.WedgeAt(Vector2.zero, Wedges, 0f));
            Assert.AreEqual(-1, EmoteWheelLayout.WedgeAt(Vector2.right * (DeadZone - 1f), Wedges, DeadZone));
            Assert.AreEqual(2, EmoteWheelLayout.WedgeAt(Vector2.right * (DeadZone + 1f), Wedges, DeadZone));
        }

        [Test]
        public void PagesGroupByCategoryInFirstAppearanceOrderAndSpillPastAWheel()
        {
            var categories = new List<string> { "Greet", "Greet", "Dance", "Greet", null, "Greet" };
            List<EmoteWheelLayout.Page> pages = EmoteWheelLayout.Paginate(categories, perPage: 3);

            Assert.AreEqual(4, pages.Count);
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, pages[0].Entries);
            CollectionAssert.AreEqual(new[] { 5 }, pages[1].Entries);
            Assert.AreEqual(("Greet", 0, 2), (pages[0].Category, pages[0].Part, pages[0].PartCount));
            Assert.AreEqual(("Greet", 1, 2), (pages[1].Category, pages[1].Part, pages[1].PartCount));
            Assert.AreEqual("Dance", pages[2].Category);
            Assert.AreEqual(string.Empty, pages[3].Category, "an uncategorised emote still gets a page");
        }

        [Test]
        public void PagingWrapsRoundBothEnds()
        {
            Assert.AreEqual(2, EmoteWheelLayout.Wrap(-1, 3));
            Assert.AreEqual(0, EmoteWheelLayout.Wrap(3, 3));
            Assert.AreEqual(1, EmoteWheelLayout.Wrap(7, 3));
            Assert.AreEqual(0, EmoteWheelLayout.Wrap(5, 0), "no pages must not divide by zero");
        }

        [Test]
        public void AShortPageSpreadsRoundTheWheelWithoutSharingAWedge()
        {
            CollectionAssert.AreEqual(new[] { 0, 2, 4, 6 }, Slots(4));

            for (int count = 1; count <= Wedges; count++)
            {
                int[] slots = Slots(count);
                Assert.AreEqual(0, slots[0], "the first emote on a page always sits at the top");
                CollectionAssert.AllItemsAreUnique(slots);
                foreach (int slot in slots) Assert.That(slot, Is.InRange(0, Wedges - 1));
            }
        }

        private static int[] Slots(int count)
        {
            var slots = new int[count];
            for (int i = 0; i < count; i++) slots[i] = EmoteWheelLayout.SlotOf(i, count, Wedges);
            return slots;
        }

        /// <summary>A point 100 px out, <paramref name="degrees"/> clockwise from straight up.</summary>
        private static Vector2 Rotated(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * 100f;
        }
    }
}
