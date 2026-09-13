// Why a mount's third-person camera carries a MountRuntimeCamera marker, and what the sweep may
// and may not destroy.
//
// The camera is spawned unparented and DontSaveInEditor, and an EditMode test that mounts leaves
// it behind: no OnDestroy reaches a plain MonoBehaviour in edit mode, so destroying the mount
// releases nothing, and the camera ends up enabled and belonging to no scene, rendering the next
// play session instead of the player's camera (MountRuntimeCamera.cs has the full story). The
// marker is the only handle on such an object. These pin the two halves: mounting tags the camera
// with its mount, and the sweep removes a camera whose mount is gone while leaving a live mount's
// camera alone.
//
// In Editor/ rather than beside the other EditMode tests because MountModule is an
// Assembly-CSharp type, which an asmdef cannot reference.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class MountRuntimeCameraSweepTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();

            // Whatever a test left behind, the next one -- and the next play session -- starts clean.
            MountRuntimeCamera.SweepOrphans();
        }

        [Test]
        public void MountingInThirdPerson_TagsTheSpawnedCameraWithItsMount()
        {
            MountModule mount = BuildMount();
            Interactor rider = BuildRider();

            Assert.IsTrue(mount.TryMount(rider, null), "the fixture's mount refused the rider");

            Camera orbit = mount.MountedThirdPersonCamera;
            Assert.IsNotNull(orbit, "no orbit camera was spawned");

            var marker = orbit.GetComponent<MountRuntimeCamera>();
            Assert.IsNotNull(marker, "the spawned camera carries no MountRuntimeCamera marker");
            Assert.AreSame(mount, marker.Owner);
            Assert.IsFalse(marker.IsOrphaned, "a camera whose mount is alive is not an orphan");
        }

        [Test]
        public void TheSweep_DestroysACameraWhoseMountIsGone_AndSparesOneWhoseMountIsAlive()
        {
            MountModule living = BuildMount();
            MountModule doomed = BuildMount();

            GameObject kept = Track(new GameObject("kept_MountThirdPersonCamera", typeof(Camera)));
            GameObject stray = Track(new GameObject("stray_MountThirdPersonCamera", typeof(Camera)));
            MountRuntimeCamera.Attach(kept, living);
            MountRuntimeCamera.Attach(stray, doomed);

            // The mount goes without ever having known about this camera, which is exactly the
            // shape of a reference lost to a domain reload: OnDestroy has nothing to release.
            Object.DestroyImmediate(doomed.gameObject);

            int swept = MountRuntimeCamera.SweepOrphans();

            Assert.AreEqual(1, swept, "exactly the orphan should have been swept");
            Assert.IsTrue(stray == null, "the orphaned camera survived the sweep");
            Assert.IsFalse(kept == null, "the sweep destroyed a camera whose mount is still alive");
        }

        // ─────────── Rig ───────────

        private MountModule BuildMount()
        {
            GameObject mountObject = Track(new GameObject("Mount"));
            var mount = mountObject.AddComponent<MountModule>();

            var seat = new GameObject("Seat");
            seat.transform.SetParent(mountObject.transform, false);

            // Edit mode never advances Time.time, so the authored cooldown reads as "0 >= 0.25" and
            // IsAvailableForMount refuses every mount. Same workaround as the other mount fixtures.
            var authored = new SerializedObject(mount);
            authored.FindProperty("mountCooldown").floatValue = 0f;
            authored.FindProperty("seatPoint").objectReferenceValue = seat.transform;
            authored.FindProperty("defaultPerspective").enumValueIndex =
                (int)MountModule.CameraPerspective.ThirdPerson;
            authored.ApplyModifiedPropertiesWithoutUndo();

            return mount;
        }

        private Interactor BuildRider()
        {
            GameObject riderObject = Track(new GameObject("Rider"));
            riderObject.AddComponent<PlayerMovement>();
            return riderObject.AddComponent<Interactor>();
        }

        private GameObject Track(GameObject go)
        {
            spawned.Add(go);
            return go;
        }
    }
}
