// Why stepping out of a PlayerShip chair used to leave the player walking around with gravity off.
//
// NetChannel is keyed by the ENTITY — the NetworkObject root — and dispatches a message to every
// handler registered for its id under that root. PlayerShipBuilder gives every chair its own
// MountModule + MountNetworkSync (NetMsg.cs says so where SeatRequest/SeatRelease were retired), so
// one ship carries four of them on one NetworkObject and a single NetMsg.Mount reached all four.
// One press seated the same player in all four chairs.
//
// The physics is where that stopped being merely wasteful. MountModule.EnterMountedRigidbodyState
// snapshots the rider's body so the dismount can hand it back exactly as it found it. The first
// chair snapshotted the truth — dynamic, gravity on — and then froze the body; the other three
// snapshotted the frozen body, so THEIR idea of "as we found it" was kinematic with useGravity off.
// On dismount the three bad snapshots were applied after the good one and won.
// PlayerMovement.EnsureMovableBody rescues a kinematic player, which is why the body could still be
// walked around, but nothing puts gravity back — so the player kept walking, and stopped falling.
//
// The session that reported it left the fingerprints in Editor.log: three
// "Setting linear velocity of a kinematic body is not supported" from MountModule.Mounting.cs:412
// per mount (EnterMountedRigidbodyState clears the velocity BEFORE it goes kinematic, so it only
// warns when somebody else got there first — once per surplus chair), the mirror three from :430 on
// the dismount, and then "[PlayerMovement] ... was driving a kinematic body ... Released it."
//
// The fix is the addressing every other multi-instance system on this channel already uses —
// ArticulatedPartInteraction's switchIndex, VehicleStation.StationIndex, NetLatch.Index: the sender
// says which one of us it means in NetArg.A, and the others drop the message.
//
// Driven with no NetworkManager, which is what an EditMode test, a scene opened straight from the
// editor and a torn-down session all look like: with no wire every send runs the handlers locally,
// so the whole round trip is observable inside the call that made it. That is the same degradation
// contract VehicleStationTests leans on.
//
// In Editor/ rather than beside the other EditMode tests because MountModule and PlayerMovement are
// Assembly-CSharp types, which an asmdef cannot reference.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class MountSeatAddressingTests
    {
        private readonly List<GameObject> spawned = new();

        private MountNetworkSync[] chairs;
        private Interactor rider;
        private Rigidbody riderBody;

        [SetUp]
        public void SetUp()
        {
            chairs = BuildShip(seats: 3);
            rider = BuildRider(out riderBody);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
        }

        // ─────────── Rig ───────────

        /// <summary>
        /// A hull shaped like PlayerShip: the helm's mount on the root and <paramref name="seats"/>
        /// passenger mounts under it, all sharing the hull's one channel.
        /// </summary>
        private MountNetworkSync[] BuildShip(int seats)
        {
            var hull = new GameObject("Hull");
            spawned.Add(hull);

            var syncs = new List<MountNetworkSync> { Fit(hull) };

            for (int i = 0; i < seats; i++)
            {
                var chair = new GameObject($"PassengerSeat{i + 1}");
                chair.transform.SetParent(hull.transform, false);
                syncs.Add(Fit(chair));
            }

            // In hierarchy order, which is the order GetComponentsInChildren enumerates and so the
            // order a real ship registers its handlers in.
            foreach (MountNetworkSync sync in syncs)
                Boot(sync);

            return syncs.ToArray();
        }

        /// <summary>One chair: a mount and the networked half that speaks for it.</summary>
        private static MountNetworkSync Fit(GameObject chair)
        {
            MountModule mount = chair.AddComponent<MountModule>();

            // Edit mode never advances Time.time, so the authored 0.25 s cooldown reads as
            // "0 >= 0.25" and refuses every mount. Zeroing it is the documented way round that
            // gate — it is what leaves MountRiderComponentRestoreTests intermittently red.
            var so = new SerializedObject(mount);
            so.FindProperty("mountCooldown").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();

            return chair.AddComponent<MountNetworkSync>();
        }

        /// <summary>A player body: what CacheMountedPlayerReferences reaches for, and no more.</summary>
        private Interactor BuildRider(out Rigidbody body)
        {
            var go = new GameObject("Rider");
            spawned.Add(go);

            body = go.AddComponent<Rigidbody>();
            body.useGravity = true;
            body.isKinematic = false;

            go.AddComponent<PlayerMovement>();
            return go.AddComponent<Interactor>();
        }

        /// <summary>
        /// Runs Awake and OnEnable by hand — Unity calls neither for a component created in edit
        /// mode, and the handler registration under test lives in OnEnable. Same helper, and the
        /// same reason, as HostileDialogTests.
        /// </summary>
        private static void Boot(MonoBehaviour behaviour)
        {
            Call(behaviour, "Awake");
            Call(behaviour, "OnEnable");
        }

        private static void Call(MonoBehaviour behaviour, string method)
        {
            MethodInfo info = behaviour.GetType().GetMethod(
                method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            info?.Invoke(behaviour, null);
        }

        private static MountModule MountOf(MountNetworkSync sync) => sync.GetComponent<MountModule>();

        // ─────────── The addressing ───────────

        [Test]
        public void EveryChairOnAHullHasItsOwnAddress()
        {
            int[] addresses = chairs.Select(c => c.MountIndex).ToArray();

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, addresses,
                "Chairs on one entity share a channel, so each has to be able to say which one it " +
                "is. Positional over the hull, the way VehicleStation.StationIndex is.");
        }

        [Test]
        public void MountingOneChair_LeavesTheOtherChairsEmpty()
        {
            chairs[0].RequestMount(rider);

            Assert.IsTrue(MountOf(chairs[0]).IsMounted, "The chair that was asked must seat them.");

            foreach (MountNetworkSync other in chairs.Skip(1))
            {
                Assert.IsFalse(MountOf(other).IsMounted,
                    other.name + " seated a rider nobody put there: NetMsg.Mount reaches every " +
                    "MountNetworkSync on the hull's channel, so one press mounted all four chairs.");
            }
        }

        [Test]
        public void MountingOneChair_DoesNotDisturbTheChairSomebodyElseIsIn()
        {
            chairs[1].RequestMount(rider);
            Interactor second = BuildRider(out Rigidbody _);

            chairs[2].RequestMount(second);

            Assert.AreSame(rider.transform, MountOf(chairs[1]).MountedPlayerTransform,
                "Seating a second player must not turf the first out of their own chair.");
        }

        // ─────────── What the player felt ───────────

        [Test]
        public void SteppingOutOfAChair_GivesTheRiderTheirOwnPhysicsBack()
        {
            chairs[0].RequestMount(rider);
            chairs[0].RequestDismount();

            Assert.IsTrue(riderBody.useGravity,
                "The player walked out of the ship with gravity switched off. Surplus chairs " +
                "snapshotted the body AFTER the first chair had frozen it, and handed that back.");
            Assert.IsFalse(riderBody.isKinematic,
                "The player walked out of the ship with a kinematic body — only PlayerMovement's " +
                "failsafe rescued that, and it says so in the log every time.");
        }

        [Test]
        public void DismountingOneChair_LeavesAnotherRiderSeated()
        {
            chairs[1].RequestMount(rider);
            Interactor second = BuildRider(out Rigidbody _);
            chairs[2].RequestMount(second);

            chairs[2].RequestDismount();

            Assert.IsTrue(MountOf(chairs[1]).IsMounted,
                "A dismount reaches every chair on the hull's channel, so one player standing up " +
                "threw everybody else out of their seats too.");
        }
    }
}
