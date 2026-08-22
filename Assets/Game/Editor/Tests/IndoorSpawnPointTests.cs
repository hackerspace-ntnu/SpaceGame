// Spawning inside a hull, without ending up inside its floor.
//
// The game's only spawn point is a child of ShipRV standing on the CargoBayFloor box. Measured in
// this editor: floor at y=100.91, sand under the ship at y=100.00, ceiling at y=105.31. That 0.91 m
// is the entire margin protecting an indoor spawn from rules that all measure the terrain — and the
// ship is a drivable gravity Rigidbody in a world whose ground runs from 100 m to 167 m.
//
// These tests build the same shape out of primitives: a floor, a roof over it, and a spawn point
// between them. No Terrain, deliberately — a scene with no heightmap is the case where every
// terrain rule silently answers "I have nothing to say", so what is left under test is the geometry
// the position is actually measured against.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class IndoorSpawnPointTests
    {
        private readonly System.Collections.Generic.List<GameObject> spawned = new();

        /// <summary>
        /// Pins the RNG so scattering is reproducible.
        ///
        /// <c>SpawnPoint.GetRandomPoint</c> draws from <c>Random.insideUnitCircle</c>, and the
        /// scattering test asserts which half of a bay the samples land in — so with an unseeded
        /// generator it passed or failed depending on the draw, alternating between runs. A test
        /// that is right most of the time is worse than one that is wrong all of the time: it
        /// trains everyone to re-run the suite instead of reading the failure.
        ///
        /// The value is arbitrary; what matters is that it is the same one every run.
        /// </summary>
        [SetUp]
        public void SeedTheGenerator() => Random.InitState(20260822);

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
            Physics.SyncTransforms();
        }

        /// <summary>
        /// A solid slab. Physics.SyncTransforms is not optional here: autoSyncTransforms is false in
        /// this project, so a collider moved after it was created is still at the origin as far as
        /// every query is concerned — the same fact that made teleporting the player fail.
        /// </summary>
        private GameObject Slab(string name, Vector3 center, Vector3 size)
        {
            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            slab.transform.position = center;
            slab.transform.localScale = size;
            spawned.Add(slab);
            Physics.SyncTransforms();
            return slab;
        }

        private SpawnPoint Point(Vector3 position, float probeHeight = 1f, float radius = 0.5f)
        {
            var host = new GameObject("SpawnPoint");
            host.transform.position = position;
            spawned.Add(host);

            var point = host.AddComponent<SpawnPoint>();
            var serialized = new SerializedObject(point);
            serialized.FindProperty("probeHeight").floatValue = probeHeight;
            serialized.FindProperty("spawnRadius").floatValue = radius;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Physics.SyncTransforms();
            return point;
        }

        /// <summary>Floor top at y=0, ceiling 5 m above it — the cargo bay, roughly to scale.</summary>
        private SpawnPoint BuildCargoBay(float ceilingHeight = 5f)
        {
            Slab("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(8f, 1f, 8f));
            Slab("Ceiling", new Vector3(0f, ceilingHeight + 0.5f, 0f), new Vector3(8f, 1f, 8f));
            return Point(new Vector3(0f, 0.01f, 0f));
        }

        [Test]
        public void ASpawnPointOnAFloor_PutsThePlayerOnThatFloor()
        {
            SpawnPoint point = BuildCargoBay();

            Assert.IsTrue(point.TryGetSpawnPoint(out Vector3 position),
                "There is a floor under it and headroom over it. There is nothing to wait for.");
            Assert.AreEqual(1.2f, position.y, 0.01f,
                "The pivot sits one ground-clearance above the floor it found — not above the " +
                "ground outside, which is where the terrain fallback would have put it.");
        }

        [Test]
        public void NoHeadroom_IsRefused_NotSquashed()
        {
            // A crawlspace: floor and ceiling 1 m apart, and a 3 m player.
            SpawnPoint point = BuildCargoBay(ceilingHeight: 1f);

            Assert.IsFalse(point.TryGetSpawnPoint(out _),
                "Every position here would put the player's head through the deck above. " +
                "Answering 'not yet' is the only honest response — the caller waits and asks again.");
        }

        [Test]
        public void AFloorFilledWithGeometry_IsRefused()
        {
            SpawnPoint point = BuildCargoBay();

            // Cargo stacked exactly where the spawn point stands.
            Slab("Crates", new Vector3(0f, 1.5f, 0f), new Vector3(4f, 3f, 4f));

            Assert.IsFalse(point.TryGetSpawnPoint(out _),
                "The ground probe still finds a surface — the top of the crates is a surface. " +
                "What must not happen is a body placed inside them.");
        }

        [Test]
        public void ScatteringFindsTheClearHalfOfTheBay()
        {
            SpawnPoint point = BuildCargoBay();

            // Half the bay is blocked, and the spawn point itself stands in the blocked half.
            Slab("Crates", new Vector3(-1.5f, 1.5f, 0f), new Vector3(3f, 3f, 4f));
            SerializedObject serialized = new(point);
            serialized.FindProperty("spawnRadius").floatValue = 3f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.IsTrue(point.TryGetSpawnPoint(out Vector3 position),
                "Twenty scattered probes over a bay that is half clear must find the clear half.");
            Assert.Greater(position.x, 0f, "It has to be the side without the crates in it.");
        }

        // ── The two questions the spawn path asks about geometry ──────────────────

        [Test]
        public void Sheltered_IsTrueUnderARoofAndFalseUnderTheSky()
        {
            Slab("Roof", new Vector3(0f, 6f, 0f), new Vector3(8f, 1f, 8f));

            Assert.IsTrue(SpawnClearance.IsSheltered(Vector3.zero),
                "A ceiling overhead is what says this position was authored against a floor, and " +
                "that measuring it against the terrain answers a question nobody asked.");
            Assert.IsFalse(SpawnClearance.IsSheltered(new Vector3(50f, 0f, 50f)),
                "Out in the open the terrain rules are the right ones and must keep applying.");
        }

        [Test]
        public void StandsOnStructure_SeesAFloorButNotOpenAir()
        {
            Slab("Deck", new Vector3(0f, -0.5f, 0f), new Vector3(8f, 1f, 8f));

            Assert.IsTrue(SpawnClearance.StandsOnStructure(new Vector3(0f, 1.2f, 0f), 1.7f),
                "This is what exempts a position inside a hull from being lifted to the height of " +
                "the sand outside — which is below the floor, not above it.");
            Assert.IsFalse(SpawnClearance.StandsOnStructure(new Vector3(0f, 40f, 0f), 1.7f),
                "Nothing underneath means nothing is holding it up, and the clamp should fire.");
        }
    }
}
