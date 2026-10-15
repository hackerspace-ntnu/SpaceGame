// The crane operator's own camera: while they work the console, the view leaves their head for a fixed place a little
// behind and high over them, looking down on the whole of the main room's floor, so the hook, the load, the ship under
// it and the mount it goes on are all in sight at once. It does not move; the console runs the hook by its keys.
using System;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.World
{
    /// <summary>
    /// A still camera over the crane console for its local operator. Plain class held by
    /// <see cref="WorkshopCraneConsole"/>: <see cref="Begin"/> when this machine's player takes the console,
    /// <see cref="End"/> when they let go. Local only: nothing here is sent or saved.
    /// </summary>
    [Serializable]
    public sealed class WorkshopCraneCamera
    {
        [Tooltip("Metres behind the operator's stance, away from the pit, the camera stands.")]
        [SerializeField, Min(0f)] private float back = 2f;

        [Tooltip("Metres above the operator's stance the camera stands.")]
        [SerializeField, Min(0f)] private float rise = 10f;

        [Tooltip("Vertical field of view, degrees: wide enough for the whole floor from there.")]
        [SerializeField, Range(20f, 100f)] private float fieldOfView = 60f;

        private Camera view;
        private Camera head;
        private AudioListener headListener;

        /// <summary>The way the view faces across the floor, or zero while there is no view.</summary>
        public Vector3 Facing => view != null ? Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized : Vector3.zero;

        public bool IsShowing => view != null;

        /// <summary>
        /// This machine's player took the console: their head camera off, and the still view on, over
        /// <paramref name="stance"/> and looking down at <paramref name="floorCentre"/>.
        /// </summary>
        public void Begin(GameObject player, Transform stance, Vector3 floorCentre)
        {
            End();

            head = player != null ? player.GetComponentInChildren<Camera>(true) : null;
            headListener = head != null ? head.GetComponent<AudioListener>() : null;
            if (head != null) head.enabled = false;
            if (headListener != null) headListener.enabled = false;

            view = ThirdPersonCameras.Spawn(null, "WorkshopCraneCamera");
            view.fieldOfView = fieldOfView;
            // No helmet glass on a view from outside the helmet.
            GlassDistortionRenderFeature.RuntimeEnabled = false;

            Vector3 facing = Vector3.ProjectOnPlane(stance.forward, Vector3.up).normalized;
            Vector3 position = stance.position - facing * back + Vector3.up * rise;
            view.transform.SetPositionAndRotation(position, Quaternion.LookRotation(floorCentre - position, Vector3.up));
        }

        /// <summary>The player's own head again. Safe when nothing is showing.</summary>
        public void End()
        {
            if (view != null)
            {
                UnityEngine.Object.Destroy(view.gameObject);
                GlassDistortionRenderFeature.RuntimeEnabled = true;
            }
            view = null;

            if (head != null) head.enabled = true;
            if (headListener != null) headListener.enabled = true;
            head = null;
            headListener = null;
        }
    }
}
