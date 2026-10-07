// Where an item goes when the player is not looking through their own eye.
//
// The bug these pin, reported from the ornithopter: the grappling hook hooked straight down. Every
// aimed item reads AimProvider, AimProvider read the player's own first-person camera, and mounting
// parents the rider under the seat marker with the seat's rotation. SEAT_Cradle on the ornithopter
// is rotated +90 about X — a prone pilot faces the floor — so that camera's forward IS world down,
// while the view the pilot is actually looking at, and the view the crosshair is drawn on, is the
// mount's orbit camera eleven metres behind the craft.
//
// The rule these hold in place: the aim ray leaves the player's eye and points at whatever the
// crosshair of the ACTIVE view covers, and the machine the player is strapped into is transparent
// to it — a query does not care that the solver was told to ignore rider/mount collisions.
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.EditorTools
{
    public class MountedAimTests
    {
        /// <summary>Seat rotation on DuneOrnithopter's SEAT_Cradle: a prone pilot, face down.</summary>
        private static readonly Quaternion ProneSeat = Quaternion.Euler(90f, 0f, 0f);

        private GameObject mount;
        private GameObject player;
        private GameObject orbit;
        private GameObject target;
        private GameObject hull;
        private AimProvider aim;
        private Camera eye;

        [SetUp]
        public void SetUp()
        {
            // The craft: nose along +Z, a rider parented under it wearing the seat's rotation.
            // ParentRiderToMount does exactly this — the rider hangs off the mount's root with the
            // seat marker's rotation folded into its local rotation.
            mount = new GameObject("mount");

            player = new GameObject("player", typeof(AimProvider));
            player.transform.SetParent(mount.transform, false);
            player.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            player.transform.localRotation = ProneSeat;

            var eyeObject = new GameObject("eye", typeof(Camera));
            eyeObject.transform.SetParent(player.transform, false);
            eye = eyeObject.GetComponent<Camera>();

            aim = player.GetComponent<AimProvider>();
            typeof(AimProvider)
                .GetField("playerCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(aim, eye);

            // The mount's orbit camera: behind and above the craft, unparented, looking forward.
            // MountModule spawns it exactly like this and writes its world pose every LateUpdate.
            orbit = new GameObject("orbit", typeof(Camera));
            orbit.transform.position = new Vector3(0f, 4f, -11f);

            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in new[] { target, hull, orbit, mount })
                if (go != null) Object.DestroyImmediate(go);
            target = hull = orbit = mount = null;
        }

        /// <summary>A 1 m cube 30 m ahead of the craft, near face at z = 29.5.</summary>
        private void PlaceTargetAhead()
        {
            target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "target";
            target.transform.position = new Vector3(0f, 0.5f, 30f);
            Physics.SyncTransforms();
        }

        /// <summary>
        /// The craft's own nose box. Sized and placed to cross BOTH sight lines — the pilot's eye
        /// looking forward out of the cradle, and the orbit camera's centre ray on its way down
        /// past the craft to the target — because those are the two ways a hull swallows an aim.
        /// </summary>
        private void PlaceHullAheadOfThePilot()
        {
            hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hull.name = "COL_Nose";
            hull.transform.SetParent(mount.transform, worldPositionStays: true);
            hull.transform.position = new Vector3(0f, 1.5f, 3f);
            hull.transform.localScale = new Vector3(3f, 4f, 4f);
            Physics.SyncTransforms();
        }

        /// <summary>Point the orbit camera at the target, as a rider lining up a shot would.</summary>
        private void LookAtTarget()
        {
            orbit.transform.LookAt(target.transform.position);
            aim.SetExternalView(orbit.GetComponent<Camera>(), mount.transform);
        }

        // ─────────── The reported defect ───────────

        [Test]
        public void AProneSeatDoesNotSendTheAimIntoTheGround()
        {
            PlaceTargetAhead();
            LookAtTarget();

            Ray ray = aim.GetAimRay();

            Assert.Greater(ray.direction.z, 0.9f,
                "The pilot's own camera is pitched 90 degrees with the cradle, so reading its " +
                "forward aims at the sand under the craft. The aim must follow the view the " +
                "player is actually looking through — this is the ornithopter defect verbatim.");
        }

        [Test]
        public void TheAimLandsOnWhatTheCrosshairCovers()
        {
            PlaceTargetAhead();
            LookAtTarget();

            Assert.IsTrue(aim.TryGetAimHit(60f, out RaycastHit hit),
                "The crosshair is over a cube 30 m away and the aim reported nothing at all.");
            Assert.AreSame(target, hit.collider.gameObject,
                "The crosshair is the promise the item has to keep: what it covers is what the " +
                "shot must land on.");
        }

        [Test]
        public void TheAimLeavesThePilotsEyeRatherThanTheCameraBehindTheCraft()
        {
            PlaceTargetAhead();
            LookAtTarget();

            Ray ray = aim.GetAimRay();

            Assert.AreEqual(eye.transform.position.z, ray.origin.z, 0.001f,
                "Firing from the orbit camera would spawn every dart, net and beam eleven metres " +
                "behind the craft. The view decides the DIRECTION; the eye is still the origin.");
        }

        // ─────────── The machine you are strapped into is not a target ───────────

        [Test]
        public void TheAimLooksPastTheHullTheRiderIsSittingIn()
        {
            PlaceTargetAhead();
            PlaceHullAheadOfThePilot();
            LookAtTarget();

            Assert.IsTrue(aim.TryGetAimHit(60f, out RaycastHit hit),
                "The nose box swallowed the aim outright.");
            Assert.AreSame(target, hit.collider.gameObject,
                "MountModule tells the solver to ignore rider/mount collisions, but a raycast is a " +
                "query and queries do not care. Without this the aim lands on the craft's own nose " +
                "a metre from the pilot's head, every single time.");
        }

        [Test]
        public void TheCrosshairsFocusIgnoresTheCraftItIsLookingStraightAt()
        {
            PlaceTargetAhead();
            PlaceHullAheadOfThePilot();
            LookAtTarget();

            Ray ray = aim.GetAimRay();

            Assert.Greater(ray.direction.z, 0.9f,
                "The orbit camera looks over the craft to frame it, so the craft is the first " +
                "thing its centre ray crosses. Converging on that would peg every aim to the hull " +
                "the player is riding.");
        }

        [Test]
        public void AFirstPersonSeatStillLooksPastItsOwnHull()
        {
            PlaceTargetAhead();
            PlaceHullAheadOfThePilot();

            // A first-person seat has no camera of its own: the view IS the rider's eye. The hull
            // between that eye and the world is still the rider's own machine.
            player.transform.localRotation = Quaternion.identity;
            Physics.SyncTransforms();
            aim.SetExternalView(null, mount.transform);

            Assert.IsTrue(aim.TryGetAimHit(60f, out RaycastHit hit));
            Assert.AreSame(target, hit.collider.gameObject,
                "The seat the player is in is transparent to their aim whichever camera they are " +
                "looking through — the lander's cockpit as much as the ornithopter's cradle.");
        }

        // ─────────── On foot, nothing changed ───────────

        [Test]
        public void OnFootTheAimIsStillTheEyesOwnRay()
        {
            PlaceTargetAhead();
            player.transform.SetParent(null, worldPositionStays: false);
            player.transform.localRotation = Quaternion.identity;
            Physics.SyncTransforms();

            Ray ray = aim.GetAimRay();

            Assert.AreEqual(eye.transform.forward.x, ray.direction.x, 0.0001f);
            Assert.AreEqual(eye.transform.forward.y, ray.direction.y, 0.0001f);
            Assert.AreEqual(eye.transform.forward.z, ray.direction.z, 0.0001f);
            Assert.AreEqual(eye.transform.position, ray.origin,
                "A walking player looks down their own camera. Converging there would move the ray " +
                "by whatever the focus cast happened to find and change every item in the game.");
        }

        [Test]
        public void AViewLeftBehindByATornDownMountIsNotBelieved()
        {
            PlaceTargetAhead();
            LookAtTarget();

            // Dismount clears the view, but the paths that abandon a rider destroy the camera
            // instead. A stale reference must not outlive the ride either way.
            Object.DestroyImmediate(orbit);
            orbit = null;
            player.transform.SetParent(null, worldPositionStays: false);
            player.transform.localRotation = Quaternion.identity;
            Physics.SyncTransforms();

            Ray ray = aim.GetAimRay();

            Assert.AreEqual(eye.transform.forward.z, ray.direction.z, 0.0001f,
                "A destroyed view camera must read as no view at all, or a rider who left their " +
                "mount by any path but a clean dismount aims through a camera that is gone.");
        }

        [Test]
        public void ADisabledViewCameraIsNotTheViewEither()
        {
            PlaceTargetAhead();
            LookAtTarget();

            orbit.GetComponent<Camera>().enabled = false;

            Ray ray = aim.GetAimRay();

            Assert.AreEqual(eye.transform.forward.y, ray.direction.y, 0.0001f,
                "MountModule switches the orbit camera off rather than destroying it on every " +
                "perspective change. A camera that is not drawing anything is not the view.");
        }
    }
}
