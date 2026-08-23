// Four players arriving at one spawn point, which is the only arrangement this game has.
//
// The world's single SpawnPoint is a child of ShipRV, scattering inside half a metre on the cargo
// bay floor, and every client and every respawn was handed the same call on it. The scatter had no
// idea who it had already placed, and it could not learn from the geometry either: the clearance
// test deliberately ignores player bodies, or the first player standing in a bay that small would
// block every candidate and nobody else could spawn indoors at all (see IndoorSpawnPointTests for
// the shape of that bay, and SpawnClearance for why the exclusion is there).
//
// So these build the same room out of primitives and ask the two questions that matter: does a
// second player get placed away from the first, and — the one that decides whether this is a fix or
// a new bug — does a room too full to satisfy that still place them somewhere. "Not yet" is
// reserved for ground that has not loaded, and a caller that hears it waits.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class CrowdedSpawnTests
    {
        /// <summary>The separation SpawnManager asks for, mirrored so the assertions can name it.</summary>
        private const float Separation = 2.5f;

        private readonly List<GameObject> spawned = new();

        [SetUp]
        public void SetUp()
        {
            // The scatter is random, and a test that fails one run in a thousand teaches nobody
            // anything. Seeded so a failure here is a real failure.
            Random.InitState(20260821);
        }

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

        /// <summary>Floor top at y=0, ceiling 5 m up, and a spawn point on the floor.</summary>
        private SpawnPoint BuildBay(float floorSize, float scatterRadius)
        {
            Slab("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(floorSize, 1f, floorSize));
            Slab("Ceiling", new Vector3(0f, 5.5f, 0f), new Vector3(floorSize, 1f, floorSize));

            var host = new GameObject("SpawnPoint");
            host.transform.position = new Vector3(0f, 0.01f, 0f);
            spawned.Add(host);

            var point = host.AddComponent<SpawnPoint>();
            var serialized = new SerializedObject(point);

            // A metre, not the default fifty: the probe takes the first collider it meets, so a ray
            // starting above the roof lands the player on the roof.
            serialized.FindProperty("probeHeight").floatValue = 1f;
            serialized.FindProperty("spawnRadius").floatValue = scatterRadius;
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

        /// <summary>Where a body standing on the bay floor would be, for use as an occupant.</summary>
        private static Vector3 StandingAt(float x, float z) => new(x, 1.2f, z);

        // ── The spawn point on its own ────────────────────────────────────────────

        [Test]
        public void WithNobodyThere_TheAnswerIsWhatItAlwaysWas()
        {
            SpawnPoint point = BuildBay(floorSize: 30f, scatterRadius: 10f);

            Assert.IsTrue(point.TryGetSpawnPoint(null, 0f, out Vector3 position, out float clearance));
            Assert.AreEqual(1.2f, position.y, 0.01f, "One ground clearance above the floor it found.");
            Assert.IsTrue(float.IsPositiveInfinity(clearance),
                "An empty room is as clear as it is possible to be, so every separation test passes " +
                "without the caller needing a special case for the first player in.");
        }

        [Test]
        public void ARoomyBay_PutsTheSecondPlayerAwayFromTheFirst()
        {
            SpawnPoint point = BuildBay(floorSize: 30f, scatterRadius: 10f);
            var occupied = new List<Vector3> { StandingAt(0f, 0f) };

            Assert.IsTrue(point.TryGetSpawnPoint(occupied, Separation, out Vector3 position,
                                                 out float clearance));

            Assert.GreaterOrEqual(clearance, Separation,
                "Twenty scattered probes over a bay this size must find somewhere clear.");
            Assert.GreaterOrEqual(Vector3.Distance(position, occupied[0]), Separation,
                "The reported clearance has to be the distance actually achieved, since the caller " +
                "chooses between spawn points on it.");
        }

        [Test]
        public void AFullBay_StillPlacesThePlayerSomewhere()
        {
            // Half a metre of scatter and a separation nothing in the room can satisfy: the real
            // cargo bay, asked for the impossible.
            SpawnPoint point = BuildBay(floorSize: 4f, scatterRadius: 0.5f);
            var occupied = new List<Vector3> { StandingAt(0f, 0f) };

            Assert.IsTrue(point.TryGetSpawnPoint(occupied, separation: 8f, out Vector3 position,
                                                 out float clearance),
                "False means 'the ground here has not loaded' and the caller waits for it. A bay " +
                "that is merely crowded must never say that — the player would sit out the whole " +
                "spawn timeout and be dropped on somebody's head anyway.");

            Assert.Less(clearance, 8f, "It should report honestly that it could not get clear.");
            Assert.AreEqual(1.2f, position.y, 0.01f, "And it is still a position on the floor.");
        }

        [Test]
        public void NoGroundAtAll_IsStillAnswered_NotYet()
        {
            // No floor, no ceiling, no terrain: the streamed world before its chunk arrives. The
            // separation machinery must not have turned that into a confident wrong answer.
            var host = new GameObject("SpawnPoint");
            host.transform.position = new Vector3(0f, 400f, 0f);
            spawned.Add(host);

            var point = host.AddComponent<SpawnPoint>();
            Physics.SyncTransforms();

            Assert.IsFalse(point.TryGetSpawnPoint(new List<Vector3> { Vector3.zero }, Separation,
                                                  out _, out _));
        }

        // ── The manager, which is what actually knows who is where ────────────────

        [Test]
        public void FourPlayersInARow_AreNotStacked()
        {
            BuildBay(floorSize: 30f, scatterRadius: 10f);
            SpawnManager manager = NewSpawnManager();

            var placed = new List<Vector3>();

            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(manager.TryGetSpawnPoint(out Vector3 position),
                    $"Player {i + 1} of a full lobby got no position at all.");
                placed.Add(position);
            }

            // Nothing has a body yet — these four resolved back to back, which is exactly what
            // happens when a lobby starts and every client's spawn coroutine reaches this on the
            // same frame. Only the manager's own record of what it has handed out keeps them apart.
            for (int a = 0; a < placed.Count; a++)
            {
                for (int b = a + 1; b < placed.Count; b++)
                {
                    Assert.GreaterOrEqual(Vector3.Distance(placed[a], placed[b]), Separation,
                        $"Players {a + 1} and {b + 1} were handed positions {placed[a]} and " +
                        $"{placed[b]}, which is inside one another.");
                }
            }
        }

        [Test]
        public void FourPlayersInACupboard_AllStillGetAPosition()
        {
            // The bay too small to seat them. Crowding is a preference and must never cost anybody
            // a body — four players in a heap sort themselves out on the next physics step, four
            // players with no spawn do not.
            BuildBay(floorSize: 4f, scatterRadius: 0.5f);
            SpawnManager manager = NewSpawnManager();

            for (int i = 0; i < 4; i++)
                Assert.IsTrue(manager.TryGetSpawnPoint(out _), $"Player {i + 1} was left unspawned.");
        }

        [Test]
        public void ALivingPlayer_IsAvoidedTheSameAsAClaimedPosition()
        {
            BuildBay(floorSize: 30f, scatterRadius: 10f);

            // A body standing on the spawn point: the respawn case, where everybody who did not die
            // is already in the room.
            GameObject body = Slab("Player", StandingAt(0f, 0f), new Vector3(1f, 3f, 1f));
            body.tag = "Player";
            Physics.SyncTransforms();

            SpawnManager manager = NewSpawnManager();

            Assert.IsTrue(manager.TryGetSpawnPoint(out Vector3 position));
            Assert.GreaterOrEqual(Vector3.Distance(position, body.transform.position), Separation,
                "Found by tag, the same way SpawnClearance identifies the bodies it excludes from " +
                "the clearance test — that exclusion is precisely why somebody has to track them.");
        }
    }
}
