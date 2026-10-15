// Where a view camera that is not the player's own head comes from: a mount's orbit camera, the workshop crane's.
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>
    /// Spawns a runtime view camera from the project's authored third-person camera prefab, so every such view gets the
    /// same render settings (post-processing on, the right volume mask, the FMOD listener) instead of whatever a clone of
    /// <c>Camera.main</c> happened to carry.
    /// </summary>
    public static class ThirdPersonCameras
    {
        // Resources.Load paths are relative to the Resources folder, so this has to track the prefab's subfolder.
        private const string DefaultResourcePath = "Cameras/Mount Third Person Camera";
        private static Camera defaultPrefab;

        /// <summary>The project's authored third-person camera, loaded once from Resources.</summary>
        public static Camera DefaultPrefab =>
            defaultPrefab != null ? defaultPrefab : defaultPrefab = Resources.Load<Camera>(DefaultResourcePath);

        /// <summary>
        /// A new, unparented camera named <paramref name="name"/>: from <paramref name="prefab"/>, else the project
        /// default, else a clone of <c>Camera.main</c> stripped of its children, else a bare camera. Untagged, so
        /// <c>Camera.main</c> keeps resolving to the player's own; with an AudioListener; never saved into a scene.
        /// </summary>
        public static Camera Spawn(Camera prefab, string name)
        {
            Camera source = prefab != null ? prefab : DefaultPrefab;
            GameObject cameraObject;
            Camera spawned;
            if (source != null)
            {
                cameraObject = Object.Instantiate(source.gameObject);
                spawned = cameraObject.GetComponent<Camera>();
                spawned.targetTexture = null;
            }
            else if (Camera.main != null)
            {
                cameraObject = Object.Instantiate(Camera.main.gameObject);
                foreach (Transform child in cameraObject.transform)
                    Object.Destroy(child.gameObject);
                spawned = cameraObject.GetComponent<Camera>();
                spawned.targetTexture = null;
            }
            else
            {
                cameraObject = new GameObject();
                spawned = cameraObject.AddComponent<Camera>();
            }

            cameraObject.name = name;
            cameraObject.tag = "Untagged";
            if (!cameraObject.GetComponent<AudioListener>())
                cameraObject.AddComponent<AudioListener>();

            // Runtime state that must never reach a scene file: spawned unparented into whatever scene is open, it is an
            // ordinary root object Ctrl+S would write to disk. DontSaveInEditor rather than DontSave, so a scene load in
            // play mode still destroys it (see MountRuntimeCamera for what an edit-mode leftover costs).
            cameraObject.hideFlags = HideFlags.DontSaveInEditor;
            return spawned;
        }
    }
}
