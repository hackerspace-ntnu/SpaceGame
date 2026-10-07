// A realtime reflection probe that re-renders on an interval, so what a polished surface reflects
// follows the day/night cycle.
//
// Nothing else in the project updates reflections when the sky changes: a probe rendered once on load
// would hold the hour it loaded at, and a night-time load would leave mirrors showing a black sky all
// day. Rendering every frame keeps up but costs a scene render per frame for a sky that moves a few
// degrees a minute. An interval sits between the two, and with the probe time-sliced, each render is
// spread over several frames.
//
// Presentation only: every machine renders its own probe from the sky it already agrees on, and
// there is nothing to save.
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.World
{
    [AddComponentMenu("SpaceGame/Environment/Timed Reflection Probe")]
    [RequireComponent(typeof(ReflectionProbe))]
    public class TimedReflectionProbe : MonoBehaviour
    {
        [Tooltip("Seconds between re-renders. The sky turns slowly; shorter only costs frames.")]
        [SerializeField, Min(0.5f)] private float refreshSeconds = 10f;

        private ReflectionProbe probe;
        private float nextRefresh;

        private void Awake()
        {
            probe = GetComponent<ReflectionProbe>();
            if (probe.mode != ReflectionProbeMode.Realtime || probe.refreshMode != ReflectionProbeRefreshMode.ViaScripting)
                Debug.LogError($"[TimedReflectionProbe] '{name}' needs a Realtime probe refreshed Via Scripting.", this);
        }

        private void Update()
        {
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + refreshSeconds;
            probe.RenderProbe();
        }
    }
}
