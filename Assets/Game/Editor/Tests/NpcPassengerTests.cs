// Tests for putting an NPC in a saddle.
//
// Same constraint as AgentAuthorityTests: there is no session here, so what is provable is the
// arithmetic and the bookkeeping rather than a live reparent. That covers the two failures this
// class has actually had — a seat offset folded into the wrong space, and a dismount that handed
// back drivers it never switched off — and the netcode rule it exists under is asserted where it
// can be: an unspawned NetworkObject must never be reparented, because Netcode refuses and puts
// the parent straight back, which is what left a caravan's riders standing in the empty desert.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.Tests
{
    public class NpcPassengerTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);

            spawned.Clear();
        }

        // ─────────── The fold that the netcode path depends on ───────────

        [Test]
        public void TheSeatIsTheSamePlaceInEitherSpace()
        {
            (Transform root, Transform seat) = NewMountRig();

            (Vector3 local, Quaternion localRotation) =
                NpcPassenger.SeatPoseIn(root, seat, new Vector3(0f, -0.85f, 0f), new Vector3(0f, 15f, 0f));
            (Vector3 world, Quaternion worldRotation) =
                NpcPassenger.SeatPoseIn(null, seat, new Vector3(0f, -0.85f, 0f), new Vector3(0f, 15f, 0f));

            Assert.That(Vector3.Distance(root.TransformPoint(local), world), Is.LessThan(1e-4f),
                "Netcode will not parent a rider to the seat marker itself, so the marker's offset " +
                "is folded into the mount root's local space instead. If the fold is wrong the " +
                "rider rides somewhere other than the saddle.");
            Assert.That(Quaternion.Angle(root.rotation * localRotation, worldRotation), Is.LessThan(0.01f));
        }

        [Test]
        public void AMountThatIsNotAtTheOriginFoldsTheSameWay()
        {
            (Transform root, Transform seat) = NewMountRig();
            root.SetPositionAndRotation(new Vector3(913f, 27f, -455f), Quaternion.Euler(0f, 214f, 0f));

            (Vector3 local, _) = NpcPassenger.SeatPoseIn(root, seat, Vector3.down, Vector3.zero);
            (Vector3 world, _) = NpcPassenger.SeatPoseIn(null, seat, Vector3.down, Vector3.zero);

            Assert.That(Vector3.Distance(root.TransformPoint(local), world), Is.LessThan(1e-3f),
                "Caravans live kilometres from the origin — a fold that only holds at the origin " +
                "holds nowhere the player will ever see one.");
        }

        // ─────────── Seating an unnetworked rider ───────────

        [Test]
        public void TheRiderEndsUpOnTheSeat()
        {
            (NpcPassenger passenger, Transform seat) = NewPassenger(new Vector3(0f, -0.85f, 0f));
            GameObject rider = NewObject("rider");

            passenger.Seat(rider);

            Assert.AreSame(seat, rider.transform.parent);
            Assert.That(Vector3.Distance(rider.transform.position, seat.TransformPoint(new Vector3(0f, -0.85f, 0f))),
                        Is.LessThan(1e-4f),
                "The offset exists because a character's origin is between its feet: seat them at " +
                "the saddle's origin and they stand on it instead of sitting in it.");
        }

        [Test]
        public void SeatingRefusesASecondRider()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            GameObject first = NewObject("first");
            GameObject second = NewObject("second");

            passenger.Seat(first);
            passenger.Seat(second);

            Assert.AreSame(first, passenger.Rider);
            Assert.IsNull(second.transform.parent, "One saddle, one rider.");
        }

        // ─────────── What dismounting is allowed to switch back on ───────────

        [Test]
        public void DismountingGivesBackOnlyWhatSeatingTookAway()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            GameObject rider = NewObject("rider");

            var controller = rider.AddComponent<AgentController>();
            controller.enabled = false;

            passenger.Seat(rider);
            passenger.Dismount();

            Assert.IsFalse(controller.enabled,
                "This rider's brain was already switched off by something else. Dismounting must " +
                "not hand them a working one it never took.");
        }

        [Test]
        public void DismountingUnparentsAndRestores()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            GameObject rider = NewObject("rider");
            var controller = rider.AddComponent<AgentController>();

            passenger.Seat(rider);
            Assert.IsFalse(controller.enabled, "A passenger must not walk out from under its mount.");

            GameObject dismounted = passenger.Dismount();

            Assert.AreSame(rider, dismounted);
            Assert.IsNull(rider.transform.parent);
            Assert.IsTrue(controller.enabled);
            Assert.IsFalse(passenger.HasRider);
        }

        [Test]
        public void ADismountDuringTeardownIsRefused()
        {
            (NpcPassenger passenger, Transform seat) = NewPassenger(Vector3.zero);
            GameObject rider = NewObject("rider");

            passenger.Seat(rider);
            passenger.gameObject.SetActive(false);

            Assert.IsNull(passenger.Dismount(),
                "Unity will not reparent out of an inactive hierarchy, so a teardown-time dismount " +
                "would leave the rider parented to something about to be destroyed.");
            Assert.AreSame(seat, rider.transform.parent);
            Assert.IsTrue(passenger.HasRider);
        }

        // ─────────── Fixtures ───────────

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        /// <summary>A mount root with a seat marker on its back, posed so no axis is trivially zero.</summary>
        private (Transform root, Transform seat) NewMountRig()
        {
            GameObject mount = NewObject("mount");
            mount.transform.SetPositionAndRotation(new Vector3(4f, 1f, -7f), Quaternion.Euler(0f, 63f, 0f));

            var seat = new GameObject("seat").transform;
            seat.SetParent(mount.transform, worldPositionStays: false);
            seat.SetLocalPositionAndRotation(new Vector3(0f, 2.3f, -0.15f), Quaternion.identity);

            return (mount.transform, seat);
        }

        // AddComponent runs no Awake outside play mode, so the serialized fields are planted by
        // hand — the same ones the Inspector fills in on the prefab.
        private (NpcPassenger passenger, Transform seat) NewPassenger(Vector3 seatOffset)
        {
            (Transform root, Transform seat) = NewMountRig();

            var passenger = root.gameObject.AddComponent<NpcPassenger>();
            Plant(passenger, "seatPoint", seat);
            Plant(passenger, "seatOffset", seatOffset);
            Plant(passenger, "dismountSideOffset", 1.6f);
            Plant(passenger, "dismountSampleDistance", 6f);

            return (passenger, seat);
        }

        private static void Plant(NpcPassenger passenger, string field, object value)
        {
            FieldInfo info = typeof(NpcPassenger)
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(info, $"NpcPassenger.{field} was renamed; this test plants it directly.");
            info.SetValue(passenger, value);
        }
    }
}
