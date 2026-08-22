// Coming back to life when the spawn point will not have you.
//
// Reported from a normal multiplayer session: a player died and the respawn button did nothing but
// print "[Respawn] No valid spawn position — the player stays down". The world has exactly one
// spawn point, it is a child of ShipRV standing on the cargo bay floor, and every reason that point
// has to refuse — cargo stacked on it, the ship parked somewhere awkward, the chunk under it not
// loaded — ended the respawn outright and left the player face down with no button left to press.
//
// Refusing is the right answer for a JOINING client, which can wait and ask again. It is the wrong
// answer for someone already in the session, and nothing about standing up requires a cargo bay. So
// a refused respawn now looks for open ground outside instead, and these pin that down: it must
// still prefer the spawn point, it must land under open sky rather than back under the hull, and it
// must still refuse when there is genuinely no world loaded to stand on.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class RespawnFallbackTests
    {
        private readonly System.Collections.Generic.List<GameObject> spawned = new();

        /// <summary>Distance the fallback starts looking at — mirrored so assertions can name it.</summary>
        private const float MinOpenGroundRadius = 7f;

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
        /// A solid slab. Physics.SyncTransforms is not optional: autoSyncTransforms is false in this
        /// project, so a collider moved after it was created is still at the origin as far as every
        /// query is concerned.
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

        /// <summary>Open ground with its surface at y=0, standing in for the sand outside.</summary>
        private void BuildOutside() => Slab("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(200f, 1f, 200f));

        /// <summary>
        /// A hull sitting on that ground with a spawn point inside it. The ceiling height decides
        /// whether the point can answer: 5 m is the cargo bay, 1 m is a bay the player cannot stand
        /// up in, which is how every "the point refuses" case looks from the geometry's side.
        /// </summary>
        private SpawnPoint BuildShip(float ceilingHeight)
        {
            Slab("BayFloor", new Vector3(0f, 0.5f, 0f), new Vector3(8f, 1f, 8f));
            Slab("Hull", new Vector3(0f, 1f + ceilingHeight + 0.5f, 0f), new Vector3(8f, 1f, 8f));

            var host = new GameObject("SpawnPoint");
            host.transform.position = new Vector3(0f, 1.01f, 0f);
            spawned.Add(host);

            var point = host.AddComponent<SpawnPoint>();
            var serialized = new SerializedObject(point);

            // A metre, not the default fifty: the probe takes the first collider it meets, so a ray
            // starting above the roof lands the player on the roof.
            serialized.FindProperty("probeHeight").floatValue = 1f;
            serialized.FindProperty("spawnRadius").floatValue = 0.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Physics.SyncTransforms();
            return point;
        }

        private SpawnManager NewSpawnManager()
        {
            var host = new GameObject("SpawnManager");
            spawned.Add(host);
            return host.AddComponent<SpawnManager>();
        }

        [Test]
        public void AWorkingSpawnPoint_IsStillWhereYouComeBack()
        {
            BuildOutside();
            BuildShip(ceilingHeight: 5f);
            SpawnManager manager = NewSpawnManager();

            Assert.IsTrue(manager.TryGetRespawnPosition(new Vector3(40f, 1.2f, 40f), out Vector3 position));

            Assert.AreEqual(2.2f, position.y, 0.01f,
                "One ground clearance above the bay floor, which is a metre above the sand.");
            Assert.Less(new Vector2(position.x, position.z).magnitude, MinOpenGroundRadius,
                "The fallback only exists for a refusal. A bay that can seat them must still be " +
                "where they come back, or every death moves the player outside for no reason.");
        }

        [Test]
        public void ABayTheyCannotStandUpIn_PutsThemOutsideInsteadOfLeavingThemDown()
        {
            BuildOutside();
            BuildShip(ceilingHeight: 1f);   // a crawlspace, and a 3 m player
            SpawnManager manager = NewSpawnManager();

            Assert.IsTrue(manager.TryGetRespawnPosition(new Vector3(40f, 1.2f, 40f), out Vector3 position),
                "The spawn point cannot vouch for anything here — which is the exact case that used " +
                "to print '[Respawn] No valid spawn position' and end the respawn.");

            Assert.AreEqual(1.2f, position.y, 0.01f, "Standing on the sand, one clearance above it.");
            Assert.GreaterOrEqual(new Vector2(position.x, position.z).magnitude, MinOpenGroundRadius,
                "Clear of the hull rather than tucked against it.");
            Assert.IsFalse(SpawnClearance.IsSheltered(position - Vector3.up * 1.2f),
                "Open sky overhead is the whole test: a roof means the search wandered back under " +
                "the hull it was sent out of.");
        }

        [Test]
        public void NoSpawnPointAtAll_PutsThemDownWhereTheyFell()
        {
            BuildOutside();
            SpawnManager manager = NewSpawnManager();

            // The refusal the manager still shouts about — nothing about the fallback makes a world
            // with no spawn point in it normal.
            LogAssert.Expect(LogType.Error, "No SpawnPoint found in scene!");

            var died = new Vector3(40f, 1.2f, 40f);

            Assert.IsTrue(manager.TryGetRespawnPosition(died, out Vector3 position),
                "A dead player is standing on ground that exists, by definition. That is the one " +
                "anchor that cannot be waiting for a chunk.");

            Assert.AreEqual(1.2f, position.y, 0.01f);
            Assert.Less(Vector3.Distance(position, died), 40f,
                "Near where they fell, not back at the origin.");
        }

        [Test]
        public void NoWorldLoadedAtAll_IsStillRefused()
        {
            // No ground, no ship, no terrain: the streamed world before its chunks arrive. The
            // fallback must not turn that into a confident wrong answer — dropping a body into
            // nothing is worse than making them wait and press the button again.
            BuildShip(ceilingHeight: 1f);

            foreach (GameObject go in spawned)
                if (go != null) go.transform.position += Vector3.up * 400f;

            Physics.SyncTransforms();

            SpawnManager manager = NewSpawnManager();

            Assert.IsFalse(manager.TryGetRespawnPosition(new Vector3(40f, 401.2f, 40f), out _),
                "Nothing to stand on anywhere: the honest answer is still 'not yet'.");
        }
    }
}
