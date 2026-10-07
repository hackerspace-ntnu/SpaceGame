using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Monowheel;

namespace SpaceGame.EditorTools
{
    public class MountedGunnersTests
    {
        private readonly List<Object> junk = new();

        [TearDown]
        public void TearDown() { foreach (var o in junk) if (o != null) Object.DestroyImmediate(o); junk.Clear(); }

        [Test]
        public void EachGunnerSeat_GetsADistinctMemberIndex_SoEachRollsItsOwnLoadout()
        {
            var group = new NpcGroup { Id = "convoy" };
            var mount = Make("Double"); var a = Make("A"); var b = Make("B");

            GroupMembership.Stamp(mount, group, 3, null);
            GroupMembership.StampRider(mount, a, 1);
            GroupMembership.StampRider(mount, b, 2);

            Assert.AreNotEqual(a.GetComponent<GroupMembership>().MemberIndex,
                               b.GetComponent<GroupMembership>().MemberIndex);
        }

        [Test]
        public void TheDriverStamp_IsUnchanged()
        {
            var group = new NpcGroup { Id = "convoy" };
            var mount = Make("Double"); var driver = Make("Driver");

            GroupMembership.Stamp(mount, group, 3, null);
            GroupMembership.StampRider(mount, driver);

            Assert.AreEqual(3 + GroupMembership.RiderIndexOffset, driver.GetComponent<GroupMembership>().MemberIndex);
        }

        [Test]
        public void SpawnGunners_SeatsOneStampedGunnerPerSeat_ApartFromTheDriver()
        {
            var group = new NpcGroup { Id = "convoy" };
            var gunnerPrefab = Make("Gunner");
            gunnerPrefab.AddComponent<HealthComponent>();
            MountedGunners gunners = Double(seatCount: 3, gunnerPrefab);
            GroupMembership.Stamp(gunners.gameObject, group, 3, null);

            gunners.SpawnGunners();

            var seats = gunners.GetComponent<VesselSeats>();
            var indices = new HashSet<int> { 3 + GroupMembership.RiderIndexOffset };   // the driver's
            Assert.AreEqual(3, gunners.Gunners.Count);
            for (int seat = 0; seat < seats.Capacity; seat++)
            {
                GameObject gunner = seats.OccupantAt(seat);
                Assert.IsNotNull(gunner, $"seat {seat} is empty");
                Assert.IsTrue(gunner.transform.IsChildOf(gunners.transform), "a gunner rides the mount");
                Assert.IsTrue(indices.Add(gunner.GetComponent<GroupMembership>().MemberIndex),
                              $"seat {seat} shares a loadout roll");
                CollectionAssert.Contains(group.Fighters, gunner, "a gunner fights for the group");
            }
        }

        [Test]
        public void SpawnGunners_SkipsAnOccupiedSeat()
        {
            MountedGunners gunners = Double(seatCount: 2, Make("Gunner"));
            var seats = gunners.GetComponent<VesselSeats>();
            GameObject passenger = Make("Passenger");
            Assume.That(seats.Seat(0, passenger));

            gunners.SpawnGunners();

            Assert.AreSame(passenger, seats.OccupantAt(0));
            Assert.AreEqual(1, gunners.Gunners.Count);
            Assert.AreSame(gunners.Gunners[0], seats.OccupantAt(1));
        }

        [Test]
        public void AKilledDouble_StandsItsGunnersBesideIt_AndNoLongerOwnsThem()
        {
            MountedGunners gunners = Double(seatCount: 2, Make("Gunner"));
            GameObject mount = gunners.gameObject;
            var seats = mount.GetComponent<VesselSeats>();
            Transform sideSeat = mount.transform.Find("Seat_1");
            sideSeat.localPosition = new Vector3(SideSeatX, 1.5f, 0f);
            mount.AddComponent<Rigidbody>();
            mount.AddComponent<MonowheelMotor>();
            mount.AddComponent<HealthComponent>();
            var wreck = mount.AddComponent<MonowheelWreck>();

            gunners.SpawnGunners();
            var aboard = new List<GameObject>(gunners.Gunners);
            junk.AddRange(aboard);   // on foot they are no longer children of the mount
            Assume.That(aboard.Count, Is.EqualTo(2));

            wreck.Wreck();

            Assert.AreEqual(0, seats.Occupied, "nobody rides a wreck");
            foreach (GameObject gunner in aboard)
                Assert.IsFalse(gunner.transform.IsChildOf(mount.transform), $"{gunner.name} is still under the wreck");
            CollectionAssert.IsEmpty(gunners.Gunners, "a gunner on foot is the group's to take down, not the wreck's");
            Assert.Greater(aboard[1].transform.position.x, SideSeatX, "a side gunner steps out past its wheel");
        }

        [Test]
        public void AKilledWheel_StandsItsLivingDriverBesideIt()
        {
            GameObject mount = Make("Wheel");
            mount.AddComponent<Rigidbody>();
            mount.AddComponent<MonowheelMotor>();
            mount.AddComponent<HealthComponent>();
            var passenger = mount.AddComponent<NpcPassenger>();
            var wreck = mount.AddComponent<MonowheelWreck>();
            GameObject driver = Make("Driver");
            passenger.Seat(driver);
            Assume.That(passenger.Rider, Is.SameAs(driver));

            wreck.Wreck();

            Assert.IsFalse(passenger.HasRider, "nobody drives a wreck");
            Assert.IsFalse(driver.transform.IsChildOf(mount.transform),
                "a driver left in the saddle vanishes with the corpse and is never counted dead");
        }

        [Test]
        public void ReleasedGunners_OutliveTheWrecksDespawn_AsTheGroupsFighters()
        {
            var group = new NpcGroup { Id = "convoy" };
            GameObject gunnerPrefab = Make("Gunner");
            gunnerPrefab.AddComponent<HealthComponent>();   // only a member that can fall is a fighter
            MountedGunners gunners = Double(seatCount: 2, gunnerPrefab);
            GameObject mount = gunners.gameObject;
            mount.AddComponent<Rigidbody>();
            mount.AddComponent<MonowheelMotor>();
            mount.AddComponent<HealthComponent>();
            var wreck = mount.AddComponent<MonowheelWreck>();
            GroupMembership.Stamp(mount, group, 3, null);

            gunners.SpawnGunners();
            var aboard = new List<GameObject>(gunners.Gunners);
            junk.AddRange(aboard);
            Assume.That(aboard.Count, Is.EqualTo(2));

            wreck.Wreck();
            gunners.DespawnGunners();   // what the corpse's OnNetworkDespawn / OnDestroy does

            foreach (GameObject gunner in aboard)
            {
                Assert.IsTrue(gunner != null, "the wreck's despawn took a gunner fighting on foot with it");
                CollectionAssert.Contains(group.Fighters, gunner, "on foot a gunner is still one of the party's fighters");
            }
        }

        private const float SideSeatX = 1.2f;

        private GameObject Make(string name)
        {
            var go = new GameObject(name);
            junk.Add(go);
            return go;
        }

        private MountedGunners Double(int seatCount, GameObject gunnerPrefab)
        {
            GameObject mount = Make("Double");
            mount.AddComponent<NetworkObject>();

            var posts = new Transform[seatCount];
            for (int i = 0; i < seatCount; i++)
            {
                posts[i] = new GameObject($"Seat_{i}").transform;
                posts[i].SetParent(mount.transform);
            }

            var seats = mount.AddComponent<VesselSeats>();
            var seatsSo = new SerializedObject(seats);
            SerializedProperty list = seatsSo.FindProperty("seats");
            list.arraySize = posts.Length;
            for (int i = 0; i < posts.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = posts[i];
            seatsSo.ApplyModifiedPropertiesWithoutUndo();

            var gunners = mount.AddComponent<MountedGunners>();
            var gunnersSo = new SerializedObject(gunners);
            gunnersSo.FindProperty("gunnerPrefab").objectReferenceValue = gunnerPrefab;
            gunnersSo.ApplyModifiedPropertiesWithoutUndo();
            return gunners;
        }
    }
}
