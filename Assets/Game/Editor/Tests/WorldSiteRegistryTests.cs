// The registry is where every NPC destination comes from, so its two failure modes are worth
// pinning: a site that duplicates itself each time its chunk streams back in, and a query that
// keeps handing back the same answer so a group walks the same route forever.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class WorldSiteRegistryTests
    {
        [SetUp]
        public void Setup() => WorldSiteRegistry.Clear();

        [TearDown]
        public void TearDown() => WorldSiteRegistry.Clear();

        [Test]
        public void RegisteringTwiceUnderOneIdUpdatesRatherThanDuplicates()
        {
            // The real sequence this stands in for: a marker's chunk streams in, out, and in again.
            // Each enable offers the site afresh, and a registry that appended would give a caravan
            // three copies of one well to choose between.
            string id = WorldSiteRegistry.Register(SiteKind.WaterHole, Vector3.zero, 10f, "Well");
            WorldSiteRegistry.Register(SiteKind.WaterHole, new Vector3(5f, 0f, 0f), 12f, "Well", id);

            Assert.AreEqual(1, WorldSiteRegistry.Count, "re-registering an id must update, not append");

            Assert.IsTrue(WorldSiteRegistry.TryGet(id, out WorldSite site));
            Assert.AreEqual(5f, site.Position.x, 0.001f, "the update's position should win");
            Assert.AreEqual(12f, site.Radius, 0.001f);
        }

        [Test]
        public void UnregisterRepairsTheIndexOfTheMovedEntry()
        {
            // Removal is a swap-remove, so the entry that gets moved into the hole must have its
            // index repaired. Getting this wrong makes an unrelated site unfindable by id — and
            // only for the site that happened to be last.
            string first = WorldSiteRegistry.Register(SiteKind.Ruin, Vector3.zero, 5f, "A");
            string second = WorldSiteRegistry.Register(SiteKind.Ruin, Vector3.one * 10f, 5f, "B");
            string third = WorldSiteRegistry.Register(SiteKind.Ruin, Vector3.one * 20f, 5f, "C");

            WorldSiteRegistry.Unregister(first);

            Assert.AreEqual(2, WorldSiteRegistry.Count);
            Assert.IsFalse(WorldSiteRegistry.Contains(first));
            Assert.IsTrue(WorldSiteRegistry.TryGet(second, out _), "B should still be findable");
            Assert.IsTrue(WorldSiteRegistry.TryGet(third, out WorldSite moved), "C was swapped into the hole");
            Assert.AreEqual("C", moved.Name);
        }

        [Test]
        public void QueriesAreFilteredByKindAndFlatDistance()
        {
            WorldSiteRegistry.Register(SiteKind.ScrapField, new Vector3(50f, 0f, 0f), 5f, "near scrap");
            WorldSiteRegistry.Register(SiteKind.ScrapField, new Vector3(500f, 0f, 0f), 5f, "far scrap");
            WorldSiteRegistry.Register(SiteKind.Ruin, new Vector3(60f, 0f, 0f), 5f, "near ruin");

            Assert.IsTrue(WorldSiteRegistry.TryFindNearest(SiteKind.ScrapField, Vector3.zero, 100f,
                                                           out WorldSite found));
            Assert.AreEqual("near scrap", found.Name);

            Assert.IsFalse(WorldSiteRegistry.TryFindNearest(SiteKind.TradePost, Vector3.zero, 10000f, out _),
                "a kind with no sites must report none rather than the nearest of something else");
        }

        [Test]
        public void HeightIsIgnoredWhenMeasuringDistance()
        {
            // The world is a heightmap. A site on top of a mesa is not further away than one at its
            // foot in any sense a walking NPC agrees with, and measuring in 3D would put half the
            // sites in the world out of range of a search that should reach them.
            WorldSiteRegistry.Register(SiteKind.Ruin, new Vector3(30f, 400f, 0f), 5f, "clifftop");

            Assert.IsTrue(WorldSiteRegistry.TryFindNearest(SiteKind.Ruin, Vector3.zero, 50f, out WorldSite site));
            Assert.AreEqual(30f, site.FlatDistanceTo(Vector3.zero), 0.001f);
        }

        [Test]
        public void ExcludingTheLastSiteIsWhatStopsAnNpcWorkingOneHeapForever()
        {
            string a = WorldSiteRegistry.Register(SiteKind.ScrapField, new Vector3(10f, 0f, 0f), 5f, "A");
            WorldSiteRegistry.Register(SiteKind.ScrapField, new Vector3(400f, 0f, 0f), 5f, "B");

            Assert.IsTrue(WorldSiteRegistry.TryFindNearest(SiteKind.ScrapField, Vector3.zero, 1000f,
                                                           out WorldSite next, excludeId: a));
            Assert.AreEqual("B", next.Name, "the excluded site must not be returned even when nearest");
        }

        [Test]
        public void ClearEmptiesEverything()
        {
            WorldSiteRegistry.Register(SiteKind.Home, Vector3.zero, 5f, "camp");
            WorldSiteRegistry.Clear();

            Assert.AreEqual(0, WorldSiteRegistry.Count);
            Assert.IsFalse(WorldSiteRegistry.TryFindNearest(SiteKind.Home, Vector3.zero, 1000f, out _));
        }
    }
}
