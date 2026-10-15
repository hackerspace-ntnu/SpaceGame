using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>
    /// Switches the lights under this GameObject off while this machine's view camera (<see cref="ViewCamera"/>)
    /// is far from them, and back on as it comes near. A LODGroup culls renderers by distance but never a light,
    /// and a lamp inside a mountain still costs the frame wherever it is in view.
    ///
    /// <para>
    /// Presentation only, each machine by its own camera: nothing is sent or saved. A few lights are checked a
    /// frame, so a camera that jumps far is caught up within a handful of frames. With no camera at all the
    /// lights are left as they are.
    /// </para>
    /// </summary>
    public sealed class DistanceCulledLights : MonoBehaviour
    {
        [Tooltip("A light is switched off while the view camera is further than this from it (m).")]
        [SerializeField, Min(1f)] private float lightsOutBeyond = 170f;

        [Tooltip("A switched-off light comes back this much nearer than it went out (m), so it does not flicker " +
                 "at the edge.")]
        [SerializeField, Min(0f)] private float hysteresis = 10f;

        [Tooltip("Lights tested each frame, in turn.")]
        [SerializeField, Min(1)] private int checksPerFrame = 16;

        private Light[] lights;
        private int next;

        /// <summary>Builder only: the distance the lights go out beyond.</summary>
        public void Configure(float beyond) => lightsOutBeyond = beyond;

        // Only the lights authored on: one authored off stays off.
        private void Awake() => lights = System.Array.FindAll(GetComponentsInChildren<Light>(true), light => light.enabled);

        private void Update()
        {
            Camera view = ViewCamera.Current;
            if (view == null || lights.Length == 0) return;

            Vector3 eye = view.transform.position;
            float outSquared = lightsOutBeyond * lightsOutBeyond;
            float backSquared = Mathf.Max(0f, lightsOutBeyond - hysteresis) * Mathf.Max(0f, lightsOutBeyond - hysteresis);
            for (int i = 0; i < Mathf.Min(checksPerFrame, lights.Length); i++)
            {
                next = (next + 1) % lights.Length;
                Light light = lights[next];
                float distanceSquared = (light.transform.position - eye).sqrMagnitude;
                light.enabled = light.enabled ? distanceSquared <= outSquared : distanceSquared <= backSquared;
            }
        }

        // Disabled with the building (a chunk unloading): put every light back on for whoever enables it next.
        private void OnDisable()
        {
            if (lights == null) return;
            foreach (Light light in lights) light.enabled = true;
        }
    }
}
