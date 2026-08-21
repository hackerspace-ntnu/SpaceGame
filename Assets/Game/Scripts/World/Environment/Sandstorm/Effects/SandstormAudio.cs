// The sound of being in it.
//
// A plain Unity AudioSource rather than an FMOD event, on purpose: the storm's roar is one
// continuous loop whose volume and filter track a single number, and routing that through the
// SfxId catalog would mean editing an FMOD project this repository no longer has. Sfx stays the
// right tool for one-shots; this is not one.
//
// Put it anywhere — the loop is 2D, so it does not matter where the object sits. What matters is
// the listener's position, which is where the storm is sampled.
using UnityEngine;

namespace SpaceGame.World.Weather
{
    [RequireComponent(typeof(AudioSource), typeof(AudioLowPassFilter))]
    public class SandstormAudio : MonoBehaviour
    {
        [Tooltip("Where the storm is heard from. Left empty, the main camera is used and " +
                 "re-found if it is ever replaced.")]
        [SerializeField] private Transform listener;

        [Tooltip("Seconds for the volume and filter to catch up. Fast enough to feel like the " +
                 "door closing caused it, slow enough not to click.")]
        [SerializeField, Min(0.01f)] private float responseTime = 0.35f;

        private AudioSource source;
        private AudioLowPassFilter lowPass;
        private SandstormProfile playing;
        private float volume;
        private float cutoff = OpenCutoff;

        // 22 kHz is "filter off" as far as anything audible is concerned, and interpolating toward
        // it means the open-air case needs no special branch.
        private const float OpenCutoff = 22000f;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            lowPass = GetComponent<AudioLowPassFilter>();

            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
        }

        private void Update()
        {
            Transform ear = ResolveListener();
            if (ear == null)
                return;

            float targetVolume = 0f;
            float targetCutoff = OpenCutoff;

            if (Sandstorms.TrySample(ear.position, out StormSample sample))
            {
                SandstormProfile profile = sample.Profile;
                targetVolume = profile.maxVolume * sample.Density *
                               Mathf.Lerp(1f, profile.shelteredVolume, sample.Shelter);
                targetCutoff = Mathf.Lerp(OpenCutoff, profile.shelteredCutoff, sample.Shelter);

                SwitchTo(profile);
            }

            // Frame-rate independent approach to the target: the same easing whether the game is
            // running at 30 or 240, which a raw Lerp with deltaTime is not.
            float catchUp = 1f - Mathf.Exp(-Time.deltaTime / responseTime);
            volume = Mathf.Lerp(volume, targetVolume, catchUp);
            cutoff = Mathf.Lerp(cutoff, targetCutoff, catchUp);

            source.volume = volume;
            lowPass.cutoffFrequency = cutoff;

            // Below audibility there is nothing to play. Stopping rather than looping silence
            // keeps a clear-weather scene from holding a voice open for the whole session.
            if (volume < 0.001f && source.isPlaying)
            {
                source.Stop();
                playing = null;
            }
        }

        private void SwitchTo(SandstormProfile profile)
        {
            if (playing == profile && source.isPlaying)
                return;

            if (profile.loop == null)
                return;

            playing = profile;
            source.clip = profile.loop;
            source.Play();
        }

        private Transform ResolveListener()
        {
            // Re-resolved rather than cached once: the player prefab is respawned on death, and a
            // listener captured at Awake would leave the storm silent from then on.
            if (listener == null && Camera.main != null)
                listener = Camera.main.transform;

            return listener;
        }
    }
}
