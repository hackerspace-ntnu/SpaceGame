// The tag on a mount's spawned third-person camera saying whose it is, and the sweep that removes
// one whose mount is gone.
//
// The camera is spawned UNPARENTED (MountModule.Camera.cs says why) and flagged DontSaveInEditor so
// it can never be written into a scene file again. Both are right, and together they produce an
// object nothing else can find. Every EditMode test that mounts a rider in third person spawns one
// (there is no NetworkManager, so the rider counts as local), and the fixture's TearDown does not
// take it down: Unity delivers no OnDestroy to a plain MonoBehaviour in edit mode, so destroying
// the mount never runs ReleaseRuntimeThirdPersonCamera, and the camera hangs off no hierarchy that
// could take it along. Nor does closing the scene: a DontSaveInEditor object is detached rather
// than destroyed, leaving an enabled Camera that belongs to NO scene. The four found this way
// (Hull, PassengerSeat1 x2, PassengerSeat2 -- the MountSeatAddressingTests rig) outlived the
// tests, the scene, every domain reload and every play session for days: enabled, depth -1, tied
// with the player's Main Camera, so the game rendered through a camera parked at the prefab's
// authored pose and the player saw nothing of their own view. MountModule's own reference is a
// private field that does not survive a reload, so nothing could reach them.
//
// This marker is what CAN reach them. The owner is serialized, so it survives a domain reload, and
// the sweep destroys every marked camera whose owner is gone or whose scene is invalid. The editor
// runs the sweep after every test run, after every domain reload and before entering play mode
// (see MountRuntimeCameraSweep).
using UnityEngine;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public sealed class MountRuntimeCamera : MonoBehaviour
    {
        [SerializeField, HideInInspector] private MountModule owner;

        /// <summary>The mount that spawned this camera. Reads null once that mount is destroyed.</summary>
        public MountModule Owner => owner;

        /// <summary>
        /// True when nothing can legitimately drive or destroy this camera any more: its mount is
        /// gone, or it belongs to no loaded scene (the scene it was made in was closed under it).
        /// </summary>
        public bool IsOrphaned => owner == null || !gameObject.scene.IsValid();

        /// <summary>Tags <paramref name="cameraObject"/> as <paramref name="mount"/>'s runtime camera.</summary>
        public static MountRuntimeCamera Attach(GameObject cameraObject, MountModule mount)
        {
            MountRuntimeCamera marker = cameraObject.GetComponent<MountRuntimeCamera>();
            if (marker == null) marker = cameraObject.AddComponent<MountRuntimeCamera>();
            marker.owner = mount;
            return marker;
        }

        /// <summary>
        /// Destroys every runtime mount camera that is orphaned, wherever it is -- including the
        /// scene-less ones a plain scene search never returns. Returns how many it removed.
        /// </summary>
        public static int SweepOrphans()
        {
            int swept = 0;
            foreach (MountRuntimeCamera marker in Resources.FindObjectsOfTypeAll<MountRuntimeCamera>())
            {
                if (marker == null || !marker.IsOrphaned) continue;
#if UNITY_EDITOR
                // Never an asset: nothing authors this marker, but FindObjectsOfTypeAll would hand
                // one back if something ever did, and destroying it would edit the asset on disk.
                if (UnityEditor.EditorUtility.IsPersistent(marker)) continue;
#endif
                if (Application.isPlaying)
                    Destroy(marker.gameObject);
                else
                    DestroyImmediate(marker.gameObject);
                swept++;
            }

            return swept;
        }
    }
}
