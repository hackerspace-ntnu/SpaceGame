// Drop-on component for any sustained sound tied to an object or a place: desert wind, an interior
// hum, a generator, a ship idling on its pad.
//
// FMOD ships StudioEventEmitter, which does much the same job. This exists because it goes through
// the catalog, so a loop placed in a scene is chosen by meaning ("interior hum") rather than by
// binding a specific event — the same indirection every one-shot in the game now gets.
using FMODUnity;
using UnityEngine;
using SpaceGame.Audio;

namespace SpaceGame.Presentation
{
    public class AudioLoop : MonoBehaviour
    {
        [Header("Sound")]
        [SerializeField] private SfxId loopId = SfxId.AmbInteriorHum;
        [Tooltip("Overrides the catalog outright when set.")]
        [SerializeField] private EventReference loopSound;

        [Header("Behaviour")]
        [Tooltip("Start as soon as the object is enabled. Turn off for loops driven by game logic, " +
                 "which start and stop them through Play() and Stop().")]
        [SerializeField] private bool playOnEnable = true;
        [Tooltip("Follow this object as it moves. Leave off for a fixed location — it saves FMOD " +
                 "updating 3D attributes every frame for something that never moves.")]
        [SerializeField] private bool followTransform;
        [Tooltip("Let the tail ring out instead of cutting hard when stopped.")]
        [SerializeField] private bool fadeOutOnStop = true;

        [Header("Parameter")]
        [Tooltip("Optional FMOD parameter driven by SetIntensity(). Leave empty if the event has none.")]
        [SerializeField] private string intensityParameter = "";

        private readonly LoopingEmitter emitter = new LoopingEmitter();

        public bool IsPlaying => emitter.IsPlaying;

        private void OnEnable()
        {
            if (playOnEnable) Play();
        }

        // Both, and not just one: a scene unload disables, a despawn destroys, and a loop that only
        // cleans up on the path it happened to be tested with leaks on the other.
        private void OnDisable() => Stop();

        private void OnDestroy() => emitter.Stop(false);

        public void Play()
        {
            emitter.Play(loopId, followTransform ? gameObject : null, loopSound);

            if (!followTransform) emitter.SetPosition(transform.position);
        }

        public void Stop() => emitter.Stop(fadeOutOnStop);

        /// <summary>Drives the configured FMOD parameter — wind strength, engine load, and so on.</summary>
        public void SetIntensity(float value)
        {
            if (string.IsNullOrEmpty(intensityParameter)) return;

            emitter.SetParameter(intensityParameter, value);
        }

        public void SetVolume(float volume) => emitter.SetVolume(volume);
    }
}
