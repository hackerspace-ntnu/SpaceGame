using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTests
{
    /// <summary>
    /// The backpack's save round trip, against a real <see cref="PackLayout"/> and real surfaces.
    ///
    /// <para>
    /// Two formats live here at once. v2 stores where every item was left — surface, uv and yaw —
    /// and has to give it back unchanged. v1 stored two positional lists of slot ids and nothing
    /// about position, because there was no position to store; those saves have to load and be
    /// arranged onto the surfaces. The v1 case is the one that matters: get it wrong and every
    /// world written before this change opens with an empty pack, silently.
    /// </para>
    /// </summary>
    public class BackpackSaveCodecTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<InventoryItem> created = new();
        private readonly List<GameObject> spawned = new();

        [SetUp]
        public void ClearMeasurementCache() => ItemFootprint.ClearCache();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            foreach (InventoryItem item in created)
                if (item != null) Object.DestroyImmediate(item);

            spawned.Clear();
            created.Clear();
        }

        /// <summary>
        /// A registered item. Registration is not incidental: the codec stores ids and resolves
        /// them back through <see cref="Registry{T}"/>, so an unregistered item is indistinguishable
        /// from one whose asset was deleted.
        /// </summary>
        private InventoryItem Item(string id)
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.name = id;
            item.itemName = id;
            item.ID = id;
            created.Add(item);

            Registry<InventoryItem>.Register(item);
            return item;
        }

        /// <summary>
        /// A face to lay things on. <see cref="PackSurface"/> authors its id and size through
        /// private serialized fields, so a test that is not loading a prefab writes them directly.
        /// </summary>
        private PackSurface Surface(PackSurfaceId id, float width = 0.86f, float depth = 0.72f)
        {
            var go = new GameObject("SURF_" + id);
            spawned.Add(go);

            var surface = go.AddComponent<PackSurface>();
            typeof(PackSurface).GetField("id", Hidden).SetValue(surface, id);
            typeof(PackSurface).GetField("size", Hidden).SetValue(surface, new Vector2(width, depth));

            return surface;
        }

        private PackSurface[] Rig() => new[]
        {
            Surface(PackSurfaceId.Leaf),
            Surface(PackSurfaceId.WingLeft),
            Surface(PackSurfaceId.WingRight),
        };

        private static JObject Payload(object state) =>
            JObject.FromObject(state, SaveSerializer.Serializer);

        private static PackPlacement Find(PackLayout layout, string itemId)
        {
            foreach (PackPlacement placement in layout.Placements)
                if (placement.ItemId == itemId) return placement;

            return default;
        }

        // ---------------------------------------------------------------- v2

        /// <summary>
        /// The whole point of the v2 record: an item comes back on the face it was left on, at the
        /// spot it was left at, turned the way it was left. A round trip that only preserved the
        /// item list would pass every "is my gear still there" check and still shuffle the pack
        /// every time the player reloaded.
        ///
        /// <para>
        /// The grid did not change the FORMAT — a uv is still a Vector2 in metres from the (0,0)
        /// corner and the yaw is still a float — it only narrowed which values ever get written.
        /// So a round trip has to be exact, and this is what proves the snapped uv survives the
        /// file unaltered.
        /// </para>
        /// </summary>
        [Test]
        public void RoundTrip_PreservesSurfaceUvAndYaw()
        {
            PackSurface[] rig = Rig();

            InventoryItem staff = Item("staff"), canister = Item("canister");

            var source = new PackLayout();
            Assert.IsTrue(source.TryPlace(staff.ID, PackSurfaceId.WingRight, rig[2].Size,
                                          PackShape.Rect(2, 2), new Vector2(0.43f, 0.36f), 90f));
            Assert.IsTrue(source.TryPlace(canister.ID, PackSurfaceId.Leaf, rig[0].Size,
                                          PackShape.Rect(1, 1), new Vector2(0.7f, 0.2f), 0f));

            PackPlacement wrote = Find(source, staff.ID);

            JObject payload = Payload(BackpackSaveCodec.Capture(source));

            var target = new PackLayout();
            BackpackSaveCodec.Restore(target, rig, null, payload);

            Assert.AreEqual(2, target.Placements.Count);

            PackPlacement back = Find(target, staff.ID);
            Assert.AreEqual(PackSurfaceId.WingRight, back.Surface);
            Assert.AreEqual(wrote.Uv.x, back.Uv.x, 1e-4f);
            Assert.AreEqual(wrote.Uv.y, back.Uv.y, 1e-4f);
            Assert.AreEqual(90f, back.Yaw, 1e-4f);

            PackPlacement other = Find(target, canister.ID);
            Assert.AreEqual(PackSurfaceId.Leaf, other.Surface);
            Assert.AreEqual(Find(source, canister.ID).Uv.x, other.Uv.x, 1e-4f);
        }

        /// <summary>
        /// <b>A free-placement save still loads.</b> Every world written before the grid holds uvs
        /// at arbitrary metres and yaws at arbitrary angles — 24-degree wheel notches and 45-degree
        /// first-fit diagonals. None of that is a legal placement any more, and refusing it would
        /// delete the player's gear on the first load after the update.
        ///
        /// <para>
        /// So it snaps: same face, nearest cell, nearest quarter turn. No new file version, no
        /// second migration on top of the v1 one, and the item moves by at most half a cell.
        /// </para>
        /// </summary>
        [Test]
        public void AFreePlacementSaveSnapsOntoTheGridWithoutLosingAnything()
        {
            PackSurface[] rig = Rig();

            InventoryItem staff = Item("staff"), canister = Item("canister");

            // Written by the free-placement codec: a uv on no lattice at all, and a diagonal yaw.
            var payload = Payload(new BackpackSaveCodec.State
            {
                placements = new List<BackpackSaveCodec.PackPlacementRecord>
                {
                    new() { itemId = staff.ID, surface = (byte)PackSurfaceId.WingRight,
                            // 47, not 45: exactly half way between two quarter turns is a tie, and
                            // Mathf.RoundToInt breaks ties to even, so 45 legitimately rounds DOWN.
                            u = 0.4137f, v = 0.3612f, yaw = 47f },
                    new() { itemId = canister.ID, surface = (byte)PackSurfaceId.Leaf,
                            u = 0.7031f, v = 0.2049f, yaw = 168f },
                },
            });

            var target = new PackLayout();
            BackpackSaveCodec.Restore(target, rig, null, payload);

            Assert.AreEqual(2, target.Placements.Count, "nothing may be dropped by the snap");

            PackPlacement back = Find(target, staff.ID);

            Assert.AreEqual(PackSurfaceId.WingRight, back.Surface, "it stays on the face it was left on");
            Assert.AreEqual(90f, back.Yaw, 1e-4f, "a diagonal rounds to the nearest quarter turn");
            Assert.Less(Vector2.Distance(back.Uv, new Vector2(0.4137f, 0.3612f)), PackGrid.Cell,
                        "and lands within a cell of where it was");

            // Snapped is a fixed point: capturing what was restored and restoring that again is
            // the same placement, so a world does not drift a little every time it is reloaded.
            var again = new PackLayout();
            BackpackSaveCodec.Restore(again, rig, null, Payload(BackpackSaveCodec.Capture(target)));

            PackPlacement twice = Find(again, staff.ID);

            Assert.AreEqual(back.Uv.x, twice.Uv.x, 1e-4f);
            Assert.AreEqual(back.Uv.y, twice.Uv.y, 1e-4f);
            Assert.AreEqual(back.Yaw, twice.Yaw, 1e-4f);
        }

        [Test]
        public void Restore_ClearsWhatTheSaveLeftEmpty()
        {
            PackSurface[] rig = Rig();
            InventoryItem stale = Item("stale");

            var target = new PackLayout();
            target.TryPlace(stale.ID, PackSurfaceId.Leaf, rig[0].Size,
                            PackShape.Rect(1, 1), new Vector2(0.3f, 0.3f), 0f);

            BackpackSaveCodec.Restore(target, rig, null, Payload(BackpackSaveCodec.Capture(new PackLayout())));

            Assert.AreEqual(0, target.Placements.Count,
                            "starting contents survived a load of an empty pack");
        }

        [Test]
        public void Restore_SkipsItemIdsThatAreNoLongerInTheRegistry()
        {
            PackSurface[] rig = Rig();
            InventoryItem known = Item("known");

            var payload = Payload(new BackpackSaveCodec.State
            {
                placements = new List<BackpackSaveCodec.PackPlacementRecord>
                {
                    new() { itemId = "deleted-from-the-project", surface = (byte)PackSurfaceId.Leaf, u = 0.2f, v = 0.2f },
                    new() { itemId = "known", surface = (byte)PackSurfaceId.Leaf, u = 0.6f, v = 0.4f },
                },
            });

            var target = new PackLayout();

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            BackpackSaveCodec.Restore(target, rig, null, payload);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(1, target.Placements.Count);
            Assert.AreEqual(known.ID, target.Placements[0].ItemId);
        }

        /// <summary>
        /// A payload that says nothing about contents must leave the pack alone rather than empty
        /// it — the difference between "it was stored empty" and "nothing was stored". A save from
        /// before the pack persisted anything still has a <c>deployed</c> flag in it.
        /// </summary>
        [Test]
        public void Restore_LeavesAPayloadWithNoContentsAtAllUntouched()
        {
            PackSurface[] rig = Rig();
            InventoryItem existing = Item("existing");

            var target = new PackLayout();
            target.TryPlace(existing.ID, PackSurfaceId.Leaf, rig[0].Size,
                            PackShape.Rect(1, 1), new Vector2(0.3f, 0.3f), 0f);

            BackpackSaveCodec.Restore(target, rig, null, JObject.Parse(@"{""deployed"":true}"));

            Assert.AreEqual(1, target.Placements.Count);
            Assert.AreEqual(existing.ID, target.Placements[0].ItemId);
        }

        [Test]
        public void Capture_OfAnEmptyPackIsAnEmptyList()
        {
            BackpackSaveCodec.State state = BackpackSaveCodec.Capture(new PackLayout());

            Assert.IsNotNull(state.placements);
            Assert.AreEqual(0, state.placements.Count);
            Assert.IsNull(state.strapItemIds, "v1 keys must not be written by a v2 capture");
            Assert.IsNull(state.mainItemIds);
        }

        // ---------------------------------------------------------------- v1 migration

        /// <summary>
        /// <b>The test that matters.</b> Every world saved before free placement holds two
        /// positional lists of slot ids and no placements key at all. Without this migration each
        /// of those loads a pack that is completely, silently empty — no error, no warning, just
        /// gear gone.
        /// </summary>
        [Test]
        public void V1Payload_LoadsAndIsArrangedOntoTheSurfaces()
        {
            PackSurface[] rig = Rig();

            InventoryItem bedroll = Item("bedroll"), cell = Item("water-cell"), rope = Item("rope");

            // Exactly the shape the old codec wrote: fixed-length lists with nulls for empty slots.
            var payload = JObject.Parse(@"{
                ""strapItemIds"": [""bedroll"", null, ""rope""],
                ""mainItemIds"": [null, ""water-cell"", null]
            }");

            var target = new PackLayout();
            BackpackSaveCodec.Restore(target, rig, null, payload);

            Assert.AreEqual(3, target.Placements.Count, "every item in an old save has to survive it");

            foreach (InventoryItem item in new[] { bedroll, cell, rope })
            {
                PackPlacement placement = Find(target, item.ID);
                Assert.AreEqual(item.ID, placement.ItemId, $"'{item.ID}' was dropped by the migration");
            }
        }

        /// <summary>
        /// The arranged placements must be real placements, not a pile at the origin: everything a
        /// v1 save carried lands on a surface it actually fits on, and no two things overlap.
        /// </summary>
        [Test]
        public void V1Payload_ArrangesItemsWithoutOverlappingThem()
        {
            PackSurface[] rig = Rig();

            for (int i = 0; i < 6; i++) Item("legacy-" + i);

            var ids = new List<string>();
            for (int i = 0; i < 6; i++) ids.Add("legacy-" + i);

            var payload = Payload(new BackpackSaveCodec.State { mainItemIds = ids });

            var target = new PackLayout();
            BackpackSaveCodec.Restore(target, rig, null, payload);

            Assert.AreEqual(6, target.Placements.Count);

            // The layout refuses an overlap outright, so the way to prove they do not overlap is to
            // ask it whether each spot is still free with its own occupant excluded.
            foreach (PackPlacement placement in target.Placements)
            {
                PackSurface surface = System.Array.Find(rig, s => s.Id == placement.Surface);
                Assert.IsNotNull(surface, "an item was arranged onto a surface the rig does not have");

                InventoryItem item = Registry<InventoryItem>.Get(placement.ItemId);

                Assert.IsTrue(surface.Accepts(PackShapes.For(item, null), placement.Uv, placement.Yaw),
                              $"'{placement.ItemId}' was arranged partly off the edge of {placement.Surface}");
            }
        }

        /// <summary>
        /// A v1 save could hold 22 items; the new pack is limited by area, so some of them may have
        /// nowhere to go. Dropping them is the honest answer, and it must not throw on the way.
        /// </summary>
        [Test]
        public void V1Payload_LongerThanThePackCanHoldDoesNotThrow()
        {
            PackSurface[] rig = { Surface(PackSurfaceId.Leaf, 0.2f, 0.2f) };

            var ids = new List<string>();
            for (int i = 0; i < 22; i++)
            {
                Item("crowd-" + i);
                ids.Add("crowd-" + i);
            }

            var payload = Payload(new BackpackSaveCodec.State { strapItemIds = ids });
            var target = new PackLayout();

            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Assert.DoesNotThrow(() => BackpackSaveCodec.Restore(target, rig, null, payload));
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

            Assert.Greater(target.Placements.Count, 0, "as much as fits still has to be kept");
            Assert.Less(target.Placements.Count, 22);
        }

        /// <summary>
        /// A v2 payload wins outright. A save written after the migration may still carry the old
        /// keys — from a hand-edited file, or a partial merge — and reading both would duplicate
        /// every item, which the layout refuses one by one and would show as gear quietly missing.
        /// </summary>
        [Test]
        public void APayloadCarryingBothFormatsReadsOnlyTheNewOne()
        {
            PackSurface[] rig = Rig();
            InventoryItem placed = Item("placed"), legacy = Item("legacy");

            var payload = Payload(new BackpackSaveCodec.State
            {
                placements = new List<BackpackSaveCodec.PackPlacementRecord>
                {
                    new() { itemId = placed.ID, surface = (byte)PackSurfaceId.Leaf, u = 0.4f, v = 0.4f },
                },
                strapItemIds = new List<string> { legacy.ID },
            });

            var target = new PackLayout();
            BackpackSaveCodec.Restore(target, rig, null, payload);

            Assert.AreEqual(1, target.Placements.Count);
            Assert.AreEqual(placed.ID, target.Placements[0].ItemId);
        }
    }
}
