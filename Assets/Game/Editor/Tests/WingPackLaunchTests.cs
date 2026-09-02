// Where a wing-pack ornithopter is spawned, and where its pilot is put down again.
//
// Both were producing the same class of failure online, and for the same reason: a pose worked out
// AFTER the fact, on one machine, where the fact is something every machine has to agree about.
//
//   • The craft used to be spawned at the pilot's position and moved afterwards so the cradle
//     landed on them. That move reached the server's copy alone — it is not in the spawn message —
//     and because the craft is owner-authoritative the pilot's machine then published its own
//     un-moved pose straight back over it. Its copy had never been anywhere but the prefab's
//     origin, so the craft, the rider parented into the seat, and the chunk streamer following
//     them all ended up at the world origin. From the pilot's chair: flying, frozen, in the dark.
//
//   • A dismount used to travel as a bare event, and each machine picked its own spot from its own
//     copy of the mount. That is the same answer only while the copies agree — and the case where
//     it matters most is the one where they do not, because a crash landing puts the pilot on
//     ground the SERVER probed for. So the position is recorded and sent.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types,
// and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class WingPackLaunchTests
    {
        private GameObject craftPrefab;
        private GameObject riderObject;

        private const float Lift = 1.2f;

        [SetUp]
        public void SetUp()
        {
            // A stand-in for DuneOrnithopter.prefab: a root with a mount whose seat marker sits
            // away from the origin, which is the whole reason the correction exists.
            craftPrefab = new GameObject("CraftPrefab");
            var mount = craftPrefab.AddComponent<MountModule>();

            var seat = new GameObject("SEAT").transform;
            seat.SetParent(craftPrefab.transform, false);
            seat.localPosition = new Vector3(0f, -0.3f, 1.5f);

            var dismount = new GameObject("DISMOUNT").transform;
            dismount.SetParent(craftPrefab.transform, false);
            dismount.localPosition = new Vector3(0f, -1.2f, -1.5f);

            var serialized = new UnityEditor.SerializedObject(mount);
            serialized.FindProperty("seatPoint").objectReferenceValue = seat;
            serialized.FindProperty("dismountPoint").objectReferenceValue = dismount;
            // Edit mode never advances Time.time, so the authored 0.25 s cooldown reads as
            // "0 >= 0.25" and refuses every mount. Same workaround as MountSeatAddressingTests.Fit.
            serialized.FindProperty("mountCooldown").floatValue = 0f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            riderObject = new GameObject("Rider");
            riderObject.AddComponent<PlayerMovement>();
            riderObject.AddComponent<Interactor>();
        }

        [TearDown]
        public void TearDown()
        {
            if (craftPrefab != null) Object.DestroyImmediate(craftPrefab);
            if (riderObject != null) Object.DestroyImmediate(riderObject);
        }

        /// Where the seat ends up if the craft is spawned at the answer under test.
        private static Vector3 SeatAfterSpawningAt(GameObject prefab, Vector3 craftPosition, Quaternion facing)
        {
            var mount = prefab.GetComponent<MountModule>();
            Transform seat = mount.ActiveSeatPoint;

            // The seat marker carried through the same rigid transform the spawn applies.
            Vector3 seatLocal = prefab.transform.InverseTransformPoint(seat.TransformPoint(mount.SeatOffset));
            return craftPosition + facing * seatLocal;
        }

        [Test]
        public void TheCraftIsSpawnedSoItsSeatLandsOnThePilot()
        {
            Vector3 pilot = new Vector3(3766f, 118f, 1597f);
            Quaternion facing = Quaternion.identity;

            Vector3 craft = WingPackItem.LaunchPosition(craftPrefab, pilot, facing, Lift);

            Assert.That(SeatAfterSpawningAt(craftPrefab, craft, facing),
                        Is.EqualTo(pilot + Vector3.up * Lift).Using(Vector3Within(1e-3f)),
                        "The pilot is teleported by however far the cradle sits from the craft's " +
                        "origin the moment they are seated.");
        }

        [Test]
        public void TheSeatCorrectionTurnsWithTheLaunchHeading()
        {
            // The heading is whatever the pilot was facing when they used the pack, and the seat
            // offset is measured in the craft's own frame — so an offset applied unrotated is
            // correct due north and wrong everywhere else, by twice the cradle's offset at worst.
            Vector3 pilot = new Vector3(100f, 50f, -200f);
            Quaternion facing = Quaternion.Euler(0f, 137f, 0f);

            Vector3 craft = WingPackItem.LaunchPosition(craftPrefab, pilot, facing, Lift);

            Assert.That(SeatAfterSpawningAt(craftPrefab, craft, facing),
                        Is.EqualTo(pilot + Vector3.up * Lift).Using(Vector3Within(1e-3f)));
        }

        [Test]
        public void ACraftWithNoSeatMarkerIsStillSpawnedOnThePilot()
        {
            // Nothing to correct by. The craft's origin lands on the pilot, which is visibly wrong
            // and is not a teleport to the other side of the map.
            var bare = new GameObject("Bare");
            try
            {
                Vector3 pilot = new Vector3(5f, 6f, 7f);

                Assert.That(WingPackItem.LaunchPosition(bare, pilot, Quaternion.identity, Lift),
                            Is.EqualTo(pilot + Vector3.up * Lift).Using(Vector3Within(1e-3f)));
            }
            finally
            {
                Object.DestroyImmediate(bare);
            }
        }

        [Test]
        public void ADismountRecordsWhereItActuallyPutTheRider()
        {
            // What MountNetworkSync sends to the peers. A crash landing resolves ground against the
            // world on the server; without this the peers fall back on their own dismount marker
            // and put the pilot under the wreck — and on the pilot's own machine, where their body
            // is owner-authoritative, that wrong answer is the one that sticks.
            var mount = craftPrefab.GetComponent<MountModule>();
            var interactor = riderObject.GetComponent<Interactor>();
            Vector3 ground = new Vector3(3766f, 112.5f, 1597f);

            Assert.IsTrue(mount.TryMount(interactor, null), "The rider should have been seated.");
            mount.DismountAt(ground);

            Assert.IsTrue(mount.HasLastDismountPosition);
            Assert.That(mount.LastDismountPosition, Is.EqualTo(ground).Using(Vector3Within(1e-3f)));
        }

        [Test]
        public void AnOrdinaryDismountRecordsTheMountsOwnDismountPoint()
        {
            var mount = craftPrefab.GetComponent<MountModule>();
            var interactor = riderObject.GetComponent<Interactor>();

            Assert.IsTrue(mount.TryMount(interactor, null), "The rider should have been seated.");
            mount.Dismount();

            Assert.IsTrue(mount.HasLastDismountPosition);
            Assert.That(mount.LastDismountPosition,
                        Is.EqualTo(craftPrefab.transform.Find("DISMOUNT").position).Using(Vector3Within(1e-3f)),
                        "An unremarkable dismount must still announce a place, or every peer is " +
                        "back to guessing.");
        }

        private static System.Collections.IComparer Vector3Within(float tolerance) =>
            new Vector3Comparer(tolerance);

        private class Vector3Comparer : System.Collections.IComparer
        {
            private readonly float tolerance;

            public Vector3Comparer(float tolerance) => this.tolerance = tolerance;

            public int Compare(object x, object y) =>
                x is Vector3 a && y is Vector3 b && Vector3.Distance(a, b) <= tolerance ? 0 : 1;
        }
    }
}
