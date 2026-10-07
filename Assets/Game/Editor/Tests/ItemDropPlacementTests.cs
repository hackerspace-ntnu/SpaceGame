// Where PlayerDropService puts a dropped item: clear of the ground the dropper stands on.
//
// An item whose pivot is born at or below a TerrainCollider or MeshCollider surface does not get
// pushed back out — it falls straight through and keeps falling (measured 2026-10-05: a basicgun
// born with its pivot 0.15 m up lands, one born at 0.0 m is 80 m down two seconds later). A dead
// NPC's loot was born exactly there: EntityLootTable drops from the body's root, which is its feet
// when it dies standing and its pelvis, tilted however the body lies, when it dies knocked down.
// 40 of the 57 weapons in one save were falling, and the load put every one of them back on the
// sand at its X/Z (WorldSaveStore.LandAwaitingGround) — weapons that appeared out of nowhere.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    public class ItemDropPlacementTests
    {
        private const string ItemPath = "Assets/Game/Resources/Items/Artifacts/basicgun.asset";

        private IWorldService previousWorld;
        private SpawnSpy world;
        private GameObject ground;
        private GameObject origin;
        private InventoryItem item;
        private float reach;

        [SetUp]
        public void SetUp()
        {
            previousWorld = GameServices.World;
            world = new SpawnSpy();
            GameServices.World = world;

            // A slab whose top face is y = 0.
            ground = new GameObject("DropTestGround");
            var box = ground.AddComponent<BoxCollider>();
            box.size = new Vector3(40f, 2f, 40f);
            box.center = new Vector3(0f, -1f, 0f);

            origin = new GameObject("DropTestOrigin");
            Physics.SyncTransforms();

            item = UnityEditor.AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            Assert.That(item, Is.Not.Null, ItemPath);
            reach = 0.5f * ItemWorldScale.SizeOf(item.itemPrefab);
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.World = previousWorld;
            Object.DestroyImmediate(ground);
            Object.DestroyImmediate(origin);
        }

        private Vector3 DropFrom(Vector3 position, Quaternion rotation)
        {
            origin.transform.SetPositionAndRotation(position, rotation);
            Physics.SyncTransforms();

            new PlayerDropService().DropItem(origin.transform, item);

            Assert.That(world.Spawns, Is.EqualTo(1), "the drop spawned nothing");
            return world.LastPosition;
        }

        [Test]
        public void LootFromABodyStandingOnTheGroundIsBornClearOfIt()
        {
            // A body's root is its feet: on flat ground the old spawn point was exactly y = 0.
            Vector3 at = DropFrom(Vector3.zero, Quaternion.identity);

            Assert.That(at.y, Is.GreaterThanOrEqualTo(reach - 1e-3f),
                $"pivot born at y={at.y:F2}, inside the ground's surface; it falls through");
        }

        [Test]
        public void LootFromABodyLyingFaceDownIsNotBornUnderTheGround()
        {
            // A knocked-down body's root rides its pelvis, a hand's width up, turned with the body:
            // face down, its forward points into the sand and the old spawn point was a reach below.
            Vector3 at = DropFrom(new Vector3(0f, 0.15f, 0f), Quaternion.Euler(90f, 0f, 0f));

            Assert.That(at.y, Is.GreaterThanOrEqualTo(reach - 1e-3f),
                $"pivot born at y={at.y:F2}, under the ground; it falls out of the world");
        }

        [Test]
        public void AnItemDroppedFromAHandWellAboveTheGroundIsStillBornAheadOfIt()
        {
            // The player's own drop: a hand two metres up, level. Nothing about it needs lifting.
            Vector3 hand = new Vector3(0f, 2f, 0f);
            Vector3 at = DropFrom(hand, Quaternion.identity);

            Vector3 expected = hand + Vector3.forward * reach;
            Assert.That(Vector3.Distance(at, expected), Is.LessThan(1e-3f),
                $"born at {at}, expected {expected}");
        }

        [Test]
        public void ADropOffALedgeIsLeftToFall()
        {
            // Looking down over an edge: nothing is under the hand within the item's reach, so there
            // is nothing to be clear of and the item is born where the hand put it.
            ground.transform.position = new Vector3(0f, -30f, 0f);
            Vector3 hand = new Vector3(0f, 2f, 0f);
            Quaternion lookingDown = Quaternion.Euler(60f, 0f, 0f);
            Vector3 at = DropFrom(hand, lookingDown);

            Vector3 expected = hand + lookingDown * Vector3.forward * reach;
            Assert.That(Vector3.Distance(at, expected), Is.LessThan(1e-3f),
                $"born at {at}, expected {expected}");
        }

        [Test]
        public void ACeilingOverThePlayersHeadIsNotTakenForTheFloor()
        {
            // The player is 3 m tall and holds things about 2 m up; a deck ceiling sits over the
            // head. The ground probe must start below it, or the item is born on the roof.
            var ceiling = new GameObject("DropTestCeiling");
            try
            {
                var slab = ceiling.AddComponent<BoxCollider>();
                slab.size = new Vector3(40f, 0.2f, 40f);
                ceiling.transform.position = new Vector3(0f, 3.3f, 0f);

                Vector3 hand = new Vector3(0f, 2f, 0f);
                Vector3 at = DropFrom(hand, Quaternion.identity);

                Assert.That(at.y, Is.LessThan(3.2f), $"born at y={at.y:F2}, above the ceiling");
            }
            finally
            {
                Object.DestroyImmediate(ceiling);
            }
        }

        private sealed class SpawnSpy : IWorldService
        {
            public int Spawns;
            public Vector3 LastPosition;

            public void Despawn(GameObject gameObject) { }

            public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation,
                                    ulong ownerClientId = NetworkSpawn.NoOwner)
            {
                Spawns++;
                LastPosition = position;
                return null;
            }
        }
    }
}
