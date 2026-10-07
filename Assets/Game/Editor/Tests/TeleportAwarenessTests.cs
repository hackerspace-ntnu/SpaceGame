// Why a teleport has to TELL the thing it moved.
//
// Moving a transform is enough for a crate and not enough for anything that keeps a second copy of
// where it is in world space. A legged machine holds its path position and every planted foot that
// way, and re-writes the body's transform from them every LateUpdate — so a walker teleported
// through a portal was returned to where it started within a frame, silently. Its feet, meanwhile,
// stayed in the room it left, with the IK chains reaching for them.
//
// The seam is ITeleportAware, raised by SaveTeleport — the single function in this project that
// moves anything instantly. What these tests pin down is the part that is easy to get subtly wrong
// and impossible to see: that listeners are told, that they are told a TRANSFER rather than merely
// a destination, and that a resync is not mistaken for a move.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Teleporting;
using SpaceGame.Portals;

namespace SpaceGame.EditorTools
{
    public class TeleportAwarenessTests
    {
        private GameObject subject;

        [SetUp]
        public void SetUp() => subject = new GameObject("TeleportSubject");

        [TearDown]
        public void TearDown()
        {
            if (subject != null) Object.DestroyImmediate(subject);
        }

        /// <summary>
        /// A stand-in for the world-space state a real listener holds — one point it expects to be
        /// carried along, exactly as LeggedLocomotion carries a foothold.
        /// </summary>
        private sealed class Witness : MonoBehaviour, ITeleportAware
        {
            public int Calls;
            public Vector3 Held;

            public void OnTeleported(in TeleportMove move)
            {
                Calls++;
                Held = move.Point(Held);
            }
        }

        [Test]
        public void Move_TellsListenersUnderTheObject()
        {
            var child = new GameObject("Part");
            child.transform.SetParent(subject.transform);
            Witness witness = child.AddComponent<Witness>();

            SaveTeleport.Move(subject, new Vector3(50f, 0f, 0f), Quaternion.identity);

            Assert.AreEqual(1, witness.Calls,
                "Nothing under the object heard about the move, so anything holding world-space " +
                "state is now holding a position in the room it left.");
        }

        [Test]
        public void Move_CarriesWorldStateAsARigidTransform_NotAsAnOffset()
        {
            Witness witness = subject.AddComponent<Witness>();

            // A metre in front of the object, which is where it must still be afterwards — the
            // object turns a quarter turn on the way, so a listener that merely added the
            // translation would leave this point off to the side.
            witness.Held = new Vector3(0f, 0f, 1f);

            SaveTeleport.Move(subject, new Vector3(10f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));

            Assert.That(Vector3.Distance(witness.Held, new Vector3(11f, 0f, 0f)), Is.LessThan(0.001f),
                "The held point was translated but not turned. This is a walker arriving through a " +
                "portal with its feet pointing the way they pointed in the previous room.");
        }

        [Test]
        public void Move_SaysNothingForAResync()
        {
            subject.transform.position = new Vector3(3f, 4f, 5f);
            Witness witness = subject.AddComponent<Witness>();

            // NetAuthority and NetworkPlayerController both call Move with the pose the object
            // already has, purely to push it into PhysX.
            SaveTeleport.Move(subject, subject.transform.position, subject.transform.rotation);

            Assert.AreEqual(0, witness.Calls,
                "A resync woke every listener to rebase its world state by an identity transform — " +
                "cost for nothing, and float noise walking a foothold a millimetre at a time.");
        }

        [Test]
        public void Girth_IsTheNarrowestCrossSection_NotTheWorldBox()
        {
            // A long thin thing, standing at 45 degrees so its world AABB is inflated in two axes
            // at once — which is exactly the case that made a fit test flicker with facing.
            var thing = new GameObject("Long");
            thing.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            BoxCollider box = thing.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 2f, 12f);

            try
            {
                Vector2 girth = thing.AddComponent<PortalTraveller>().Girth;

                Assert.That(girth.x, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(girth.y, Is.EqualTo(1f).Within(0.001f),
                    "The 12 m length was counted as part of the cross-section, so a long thing " +
                    "is refused end-on by every aperture it would comfortably fit through.");
            }
            finally
            {
                Object.DestroyImmediate(thing);
            }
        }

        [Test]
        public void PortalTraveller_ResolvesToTheCarrier_NotThePassenger()
        {
            // A mount with a rider parented to it, which is what mounting does.
            var mount = new GameObject("Mount");
            PortalTraveller carrier = mount.AddComponent<PortalTraveller>();

            var rider = new GameObject("Rider");
            rider.transform.SetParent(mount.transform);
            rider.AddComponent<PortalTraveller>();
            SphereCollider riderCollider = rider.AddComponent<SphereCollider>();

            try
            {
                Assert.AreSame(carrier, PortalTraveller.For(riderCollider),
                    "The rider was going through the aperture under their own name as well as " +
                    "being carried by the mount, so the transfer is applied to them twice and " +
                    "they arrive as far past the exit as the two apertures are apart.");
            }
            finally
            {
                Object.DestroyImmediate(mount);
            }
        }
    }
}
