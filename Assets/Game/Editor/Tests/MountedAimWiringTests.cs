// That a real mount actually hands its rider's aim the view it put them in.
//
// MountedAimTests pins what AimProvider does once it has been told; this pins the telling, which is
// the half that silently stops happening. MountModule is where a rider's eye is taken away from
// them — parented under the seat marker, wearing the seat's rotation, with their own camera
// switched off for the whole ride — so it is the only thing that can say which camera has replaced
// it. Nothing throws when it does not: every item goes on aiming down a disabled camera pointed
// wherever the seat happens to face, which on the ornithopter is straight into the sand.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types,
// and an asmdef cannot reference Assembly-CSharp.
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class MountedAimWiringTests
    {
        private GameObject mountObject;
        private GameObject riderObject;
        private GameObject target;
        private GameObject hull;

        private MountModule mount;
        private Interactor interactor;
        private AimProvider aim;
        private Camera eye;

        [SetUp]
        public void SetUp()
        {
            mountObject = new GameObject("Mount");
            mount = mountObject.AddComponent<MountModule>();

            // The ornithopter's cradle: a prone pilot, face down, so the rider's own camera points
            // at the ground the moment they are seated.
            var seat = new GameObject("SEAT_Cradle");
            seat.transform.SetParent(mountObject.transform, false);
            seat.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Edit mode never advances Time.time, so the authored cooldown reads as "0 >= 0.25" and
            // IsAvailableForMount refuses every mount. Same workaround as the other mount fixtures.
            var authored = new SerializedObject(mount);
            authored.FindProperty("mountCooldown").floatValue = 0f;
            authored.FindProperty("seatPoint").objectReferenceValue = seat.transform;
            authored.FindProperty("defaultPerspective").enumValueIndex =
                (int)MountModule.CameraPerspective.ThirdPerson;
            authored.ApplyModifiedPropertiesWithoutUndo();

            riderObject = new GameObject("Rider");
            riderObject.AddComponent<PlayerMovement>();
            riderObject.AddComponent<PlayerLook>();
            interactor = riderObject.AddComponent<Interactor>();
            aim = riderObject.AddComponent<AimProvider>();

            var eyeObject = new GameObject("eye", typeof(Camera));
            eyeObject.transform.SetParent(riderObject.transform, false);
            eye = eyeObject.GetComponent<Camera>();
            typeof(AimProvider)
                .GetField("playerCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(aim, eye);

            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in new[] { target, hull, riderObject, mountObject })
                if (go != null) Object.DestroyImmediate(go);
            target = hull = riderObject = mountObject = null;
        }

        [Test]
        public void MountingHandsTheRidersAimTheViewItPutThemIn()
        {
            Assert.IsTrue(mount.TryMount(interactor, null), "the rider was never seated");

            Assert.IsNotNull(mount.MountedThirdPersonCamera, "no orbit camera was spawned");
            Assert.AreSame(mount.MountedThirdPersonCamera, aim.ViewCamera,
                "The rider is watching the orbit camera and every item is reading their own eye, " +
                "which mounting has just pitched face down with the cradle. This is the whole " +
                "ornithopter defect: the hook fires at the sand under the craft.");

            mount.Dismount();
        }

        [Test]
        public void AMountedAimReachesPastTheCraftToWhatTheOrbitCameraIsOver()
        {
            target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "target";
            target.transform.position = new Vector3(0f, 0.5f, 30f);

            // The craft's own hull, between the pilot and everything else.
            hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hull.name = "COL_Nose";
            hull.transform.SetParent(mountObject.transform, worldPositionStays: true);
            hull.transform.position = new Vector3(0f, 1.5f, 3f);
            hull.transform.localScale = new Vector3(3f, 4f, 4f);

            Assert.IsTrue(mount.TryMount(interactor, null), "the rider was never seated");

            // LateUpdate is what poses the orbit camera and edit mode never runs it, so put it
            // where a ride would: behind and above, framing the craft and the ground ahead.
            Transform orbit = mount.MountedThirdPersonCamera.transform;
            orbit.position = new Vector3(0f, 4f, -11f);
            orbit.LookAt(target.transform.position);
            Physics.SyncTransforms();

            bool found = aim.TryGetAimHit(60f, out RaycastHit hit);

            mount.Dismount();

            Assert.IsTrue(found, "the aim reported nothing at all from the saddle");
            Assert.AreSame(target, hit.collider.gameObject,
                "Either the aim is still following the seat's rotation into the ground, or it " +
                "stopped at the craft the rider is strapped inside — a raycast is a query, and " +
                "the rider/mount collision suspension does nothing to a query.");
        }

        [Test]
        public void DismountingGivesTheAimBackToTheRidersOwnEye()
        {
            Assert.IsTrue(mount.TryMount(interactor, null), "the rider was never seated");
            mount.Dismount();

            Assert.AreSame(eye, aim.ViewCamera,
                "A view left standing after the ride would aim every item down a camera that is " +
                "no longer drawing anything, for the rest of the session.");
        }
    }
}
