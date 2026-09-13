// How close you have to be standing before a mount offers you its seat.
//
// Interactor resolves an interactable by walking UP from whatever collider the look ray hit, which
// is what makes a creature mountable from any angle instead of only the one side its seat marker
// happens to be on. The cost of that reach is that every collider answers for the seat, so on a
// large entity the offer arrives from the full length of the ray against a body that is metres
// across — on the conjurer, from any side of a 4.8 m column, and pressing it fired the rider
// sixteen metres up onto a shoulder they were nowhere near.
//
// So the gate is per-interactor, through IContextualInteractable, and it is measured HORIZONTALLY.
// That is the part worth pinning: the seat on this machine is 15.75 m above every place a player
// can stand, so a true 3D distance could only ever be satisfied by someone already sitting in it.
// Dropping the vertical leaves the question that means something — are they standing under it.
//
// In Editor/ rather than beside the other EditMode tests because MountModule lives in the default
// assembly, and an asmdef cannot reference Assembly-CSharp.
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class MountRangeTests
    {
        private const string ConjurerPath = "Assets/Game/Prefabs/Agents/creatures/LightningConjurer.prefab";

        private GameObject mountObject;
        private GameObject player;

        [TearDown]
        public void TearDown()
        {
            if (mountObject != null) Object.DestroyImmediate(mountObject);
            if (player != null) Object.DestroyImmediate(player);
            mountObject = null;
            player = null;
        }

        /// A mount with its seat somewhere other than its own origin — the case the range exists
        /// for. Height is a parameter because a seat overhead and a seat at knee level have to give
        /// the same answer to a player standing in the same spot.
        private MountModule BuildMount(Vector3 seatLocalPosition, float maxMountDistance)
        {
            mountObject = new GameObject("Mount");
            mountObject.transform.position = Vector3.zero;

            var seat = new GameObject("Seat");
            seat.transform.SetParent(mountObject.transform, false);
            seat.transform.localPosition = seatLocalPosition;

            MountModule mount = mountObject.AddComponent<MountModule>();
            var so = new SerializedObject(mount);
            so.FindProperty("seatPoint").objectReferenceValue = seat.transform;
            so.FindProperty("maxMountDistance").floatValue = maxMountDistance;
            so.ApplyModifiedPropertiesWithoutUndo();
            return mount;
        }

        /// A body with an Interactor on it, which is how the real player is put together.
        private Interactor BuildPlayer(Vector3 position)
        {
            player = new GameObject("Player");
            player.transform.position = position;
            return player.AddComponent<Interactor>();
        }

        /// Awake by hand. Unity runs none of the messages for a component created in edit mode, and
        /// Awake is what resolves seatBone into seatPoint — without it a prefab whose seat is a bone
        /// answers from its own origin and the test measures the wrong thing. Same helper, same
        /// reason, as PassengerSeatTests.Boot, but aimed at one component: booting the conjurer
        /// whole would wake its whole module stack for a question about geometry.
        private static void Boot(MonoBehaviour mb)
        {
            MethodInfo awake = mb.GetType().GetMethod(
                "Awake", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            awake?.Invoke(mb, null);
        }

        private static bool Offers(MountModule mount, Interactor interactor) =>
            ((IContextualInteractable)mount).CanInteract(interactor);

        // ─────────────────────────────────────────────
        //  The rule
        // ─────────────────────────────────────────────

        [Test]
        public void StandingAtTheSeat_IsOfferedIt()
        {
            MountModule mount = BuildMount(new Vector3(3f, 0f, 0f), 3f);
            Assert.IsTrue(Offers(mount, BuildPlayer(new Vector3(4f, 0f, 0f))),
                "One metre from the seat is as close as anybody is going to get.");
        }

        [Test]
        public void StandingBeyondTheRange_IsNotOfferedIt()
        {
            MountModule mount = BuildMount(new Vector3(3f, 0f, 0f), 3f);
            Assert.IsFalse(Offers(mount, BuildPlayer(new Vector3(-3f, 0f, 0f))),
                "Six metres away, on the opposite side of the body. The look ray still reaches a " +
                "collider on a mount this size, which is exactly why the ray is not the gate.");
        }

        [Test]
        public void TheSeatBeingOverhead_DoesNotRefuseSomeoneStandingUnderIt()
        {
            // The conjurer's case in miniature: a seat sixteen metres up, and a player at its feet.
            MountModule mount = BuildMount(new Vector3(0f, 16f, 0f), 3f);

            Assert.IsTrue(Offers(mount, BuildPlayer(new Vector3(1f, 0f, 0f))),
                "Measured in 3D this rider is sixteen metres from the seat and no radius a player " +
                "could ever satisfy would admit them. Height is dropped on purpose.");
        }

        [Test]
        public void HeightAlone_DoesNotBuyRange()
        {
            MountModule mount = BuildMount(new Vector3(0f, 16f, 0f), 3f);

            Assert.IsFalse(Offers(mount, BuildPlayer(new Vector3(8f, 15f, 0f))),
                "Level with the seat but eight metres out across it. Ignoring height must not " +
                "turn into ignoring distance.");
        }

        [Test]
        public void ARangeOfZero_RefusesNobody()
        {
            MountModule mount = BuildMount(new Vector3(0f, 0f, 0f), 0f);

            Assert.IsTrue(Offers(mount, BuildPlayer(new Vector3(40f, 0f, 0f))),
                "Zero is the default and it means unlimited — every mount authored before this " +
                "field existed, the ostrich included, is gated by the interaction ray alone and " +
                "must keep behaving exactly as it did.");
        }

        [Test]
        public void AnInteractorlessQuery_IsNotRefused()
        {
            MountModule mount = BuildMount(new Vector3(3f, 0f, 0f), 3f);

            Assert.IsTrue(Offers(mount, null),
                "With nobody to ask about, there is no one to be too far away. A restore or a " +
                "script seating a rider must not trip over a rule written for a crosshair.");
        }

        // ─────────────────────────────────────────────
        //  The machine it was written for
        // ─────────────────────────────────────────────

        [Test]
        public void Conjurer_OffersTheShoulderFromTheArmSideOnly()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ConjurerPath);
            Assert.IsNotNull(prefab, $"Missing prefab: {ConjurerPath}. " +
                                     "Run Tools > Creatures > Build Lightning Conjurer.");

            mountObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            mountObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            MountModule mount = mountObject.GetComponent<MountModule>();
            Assert.IsNotNull(mount, "The conjurer has no MountModule, so there is nothing to sit on.");
            Boot(mount);

            Vector3 seat = mount.SeatWorldPosition;
            Assert.Greater(seat.y, 10f,
                "The seat is meant to be the shoulder bone. If it resolved to the machine's own " +
                "origin instead, everything below measures from its feet and proves nothing.");

            // Where the seat is, brought down to the sand: the spot a rider has to walk to.
            Vector3 underTheArm = new Vector3(seat.x, 0f, seat.z);
            Vector3 outward = underTheArm.normalized;

            Assert.IsTrue(Offers(mount, BuildPlayer(underTheArm)),
                "Standing directly under the shoulder is the one place mounting has to work from.");

            TearDownPlayerOnly();
            Assert.IsFalse(Offers(mount, BuildPlayer(-outward * 2.4f)),
                "Pressed against the OPPOSITE face of the body column — the closest a player can " +
                "physically stand on the wrong side. Before the range gate this offered the seat, " +
                "because the ray hit the same column either way.");
        }

        private void TearDownPlayerOnly()
        {
            if (player != null) Object.DestroyImmediate(player);
            player = null;
        }
    }
}
