// The Sky City's NavMesh, read off disk and put into play the way StaticNavMeshData puts it:
//
//   a bake that never ran, or wrote an empty mesh;
//   a fleet prefab that does not carry the mesh, so nothing in the scene ever adds it;
//   a promenade the bake left out, so the city's people have nowhere to stand;
//   a mesh baked in world space, which only lines up while the city never moves.
using System.Text.RegularExpressions;
using NUnit.Framework;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace SpaceGame.EditorTools
{
    public class SkyCityNavMeshTests
    {
        // Where persistentScene places the fleet, and somewhere else entirely, turned.
        private static readonly Vector3 ScenePosition = new(3970.32f, 228f, 1025f);
        private static readonly Vector3 MovedPosition = new(-512f, 40f, 2300f);
        private const float MovedYaw = 70f;

        // The promenade lanes in the city's modelled metres (sky_city_BUILD.md): a clear lane at
        // x 4.2-7.0 each side of the keel, 104 m long, walking surface at 0.
        private const float LaneCentreX = 5.6f;
        private static readonly float[] AlongLane = { -40f, 0f, 40f };

        // Clear air beside the hull at deck height, well inside the escorts' station.
        private static readonly Vector3 BesideTheCity = new(40f, 0f, 0f);

        private const float SampleRadius = 2f;

        private NavMeshData data;
        private NavMeshDataInstance instance;

        [SetUp]
        public void SetUp()
        {
            data = AssetDatabase.LoadAssetAtPath<NavMeshData>(SkyCityNavMeshBaker.AssetPath);
            if (data == null)
                Assert.Ignore($"{SkyCityNavMeshBaker.AssetPath} has not been baked (World > Streaming > Bake Sky City NavMesh).");
        }

        [TearDown]
        public void TearDown()
        {
            if (instance.valid) instance.Remove();
            instance = default;
        }

        [Test]
        public void TheBakeIsNotEmpty()
        {
            Assert.Greater(data.sourceBounds.size.x, 0f);
            Assert.Greater(data.sourceBounds.size.z, 0f);
        }

        [Test]
        public void TheFleetPrefabAddsThisMesh()
        {
            var fleet = AssetDatabase.LoadAssetAtPath<GameObject>(SkyFleetBuilder.FleetPrefabPath);
            Assert.IsNotNull(fleet, SkyFleetBuilder.FleetPrefabPath);
            var provider = fleet.GetComponent<StaticNavMeshData>();
            Assert.IsNotNull(provider, "StaticNavMeshData on the fleet root");
            Assert.AreSame(data, provider.Data);
        }

        [Test]
        public void EveryPromenadeIsWalkableWhereTheSceneHoldsTheCity()
        {
            AssertPromenadesWalkable(ScenePosition, Quaternion.identity);
        }

        [Test]
        public void ThePromenadesMoveWithTheCity()
        {
            AssertPromenadesWalkable(MovedPosition, Quaternion.Euler(0f, MovedYaw, 0f));
        }

        [Test]
        public void TheAirBesideTheCityIsNotWalkable()
        {
            instance = NavMesh.AddNavMeshData(data, ScenePosition, Quaternion.identity);
            Vector3 point = ScenePosition + BesideTheCity * SkyCityBuilder.Scale;
            Assert.IsFalse(NavMesh.SamplePosition(point, out _, SampleRadius, NavMesh.AllAreas), $"{point}");
        }

        [Test]
        public void AScaledInstanceLogsALoudError()
        {
            // The component silently mis-sizes a scaled instance's mesh (AddNavMeshData takes no
            // scale) unless it says so out loud - see StaticNavMeshData.OnEnable.
            var go = new GameObject("scaled-static-navmesh-data");
            go.transform.localScale = new Vector3(2f, 1f, 1f);
            var provider = go.AddComponent<StaticNavMeshData>();
            provider.Configure(data);

            try
            {
                LogAssert.Expect(LogType.Error, new Regex("StaticNavMeshData.*scaled", RegexOptions.IgnoreCase));

                // OnEnable does not run in EditMode, and SendMessage("OnEnable") trips Unity's own
                // ShouldRunBehaviour assertion; call it directly instead (same pattern as AlertChainTests).
                typeof(StaticNavMeshData)
                    .GetMethod("OnEnable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(provider, null);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private void AssertPromenadesWalkable(Vector3 position, Quaternion rotation)
        {
            instance = NavMesh.AddNavMeshData(data, position, rotation);
            Assert.IsTrue(instance.valid, "AddNavMeshData");

            foreach (float side in new[] { -1f, 1f })
            foreach (float z in AlongLane)
            {
                Vector3 local = new Vector3(side * LaneCentreX, 0f, z) * SkyCityBuilder.Scale;
                Vector3 point = position + rotation * local;
                Assert.IsTrue(NavMesh.SamplePosition(point, out _, SampleRadius, NavMesh.AllAreas),
                    $"no NavMesh within {SampleRadius} m of the promenade at local {local} (world {point})");
            }
        }
    }
}
