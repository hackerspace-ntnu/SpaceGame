using NUnit.Framework;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// How many bands a lashing is allowed to be.
    ///
    /// <para>
    /// The band count used to be "one per interior cell boundary", which strapped a 1.35 m staff
    /// down with nine ribbons and read as a net rather than a lashing. The rule is now a bracket on
    /// the item's length, and a bracket is exactly what a later tweak to the geometry can widen
    /// without anyone noticing: the failure is visible only on the mat, in a focus view nothing
    /// else tests. Hence a test on the count alone.
    /// </para>
    /// </summary>
    public class PackStrapVisualTests
    {
        [Test]
        public void SmallItemsGetOneBand()
        {
            Assert.AreEqual(1, PackStrapVisual.BandCount(1), "a one-cell item");
            Assert.AreEqual(1, PackStrapVisual.BandCount(2), "a two-cell item, about 0.27 m");
        }

        [Test]
        public void MidSizedItemsGetTwo()
        {
            Assert.AreEqual(2, PackStrapVisual.BandCount(3));
            Assert.AreEqual(2, PackStrapVisual.BandCount(5));
        }

        [Test]
        public void TheLongestGearGetsThreeAndNeverMore()
        {
            Assert.AreEqual(3, PackStrapVisual.BandCount(6));

            // Ten cells is the LaserStaff, the longest thing the rack takes.
            Assert.AreEqual(3, PackStrapVisual.BandCount(10));
            Assert.AreEqual(3, PackStrapVisual.BandCount(40));
        }
    }
}
