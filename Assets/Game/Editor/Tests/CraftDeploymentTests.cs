// The two halves of deploying a craft that the player's wing pack and an NPC's flight share: where to
// spawn it so its seat lands on the pilot, and how to retire it on whichever machine this is.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;
using SpaceGame.Vehicles.Ornithopter;

namespace SpaceGame.Tests
{
    public class CraftDeploymentTests
    {
        private static readonly Vector3 FarAway = new Vector3(150000f, 6000f, 150000f);
        private readonly List<Object> junk = new();
        private IWorldService previousWorld;
        private DespawnSpy world;

        [SetUp]
        public void SetUp()
        {
            previousWorld = GameServices.World;
            world = new DespawnSpy();
            GameServices.World = world;
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.World = previousWorld;
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        [Test]
        public void LaunchPosition_PutsTheSeatOnTheLiftedPilot_ForEveryHeading()
        {
            var root = new GameObject("Craft");
            junk.Add(root);
            var seat = new GameObject("SEAT").transform;
            seat.SetParent(root.transform, false);
            seat.localPosition = new Vector3(0f, -0.3f, 1.5f);
            Vector3 pilot = new Vector3(10f, 2f, -4f);

            foreach (float yaw in new[] { 0f, 90f, 217f })
            {
                Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
                Vector3 craftAt = CraftDeployment.LaunchPosition(root.transform, seat.position, pilot, facing, 1.2f);
                Vector3 seatAfter = craftAt + facing * root.transform.InverseTransformPoint(seat.position);

                Assert.Less(Vector3.Distance(seatAfter, pilot + Vector3.up * 1.2f), 1e-4f, $"yaw {yaw}");
            }
        }

        [Test]
        public void Retire_Offline_DespawnsThroughTheWorldService()
        {
            var craft = new GameObject("Craft");
            junk.Add(craft);
            CraftDeployment.Retire(craft);
            CollectionAssert.Contains(world.Despawned, craft);
        }

        [Test]
        public void LaunchRoom_StandingOnGround_IsNone_ButAtAnEdge_ItIs()
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            junk.Add(block);
            block.transform.position = FarAway;
            block.transform.localScale = new Vector3(10f, 1f, 10f);
            Physics.SyncTransforms();
            Vector3 middle = FarAway + Vector3.up * 0.5f;
            Vector3 edge = FarAway + new Vector3(0f, 0.5f, 4.9f);

            Assert.IsFalse(FlightLaunch.HasLaunchRoom(middle, Vector3.forward, 0.6f, 6f, 1.5f, ~0), "mid-block is not a launch");
            Assert.IsTrue(FlightLaunch.HasLaunchRoom(edge, Vector3.forward, 0.6f, 6f, 1.5f, ~0), "facing off the edge is a launch");
            Assert.IsTrue(FlightLaunch.IsAirborne(FarAway + Vector3.up * 20f, 0.6f, ~0), "20 m up is airborne");
        }

        private sealed class DespawnSpy : IWorldService
        {
            public readonly List<GameObject> Despawned = new();
            public void Despawn(GameObject gameObject) => Despawned.Add(gameObject);
            public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation,
                                    ulong ownerClientId = NetworkSpawn.NoOwner) => null;
        }
    }
}
