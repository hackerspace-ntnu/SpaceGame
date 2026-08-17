using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace SpaceGame.Audio
{
    /// <summary>
    /// Owns one sustained FMOD event — wind over a wing, a ship's engine, a weapon spinning up.
    ///
    /// <para>
    /// One-shots can be fired and forgotten; a loop cannot. It holds a voice until something stops
    /// it, and the thing that started it is usually a component that can be disabled, pooled,
    /// despawned or torn down mid-frame by netcode. Every one of those paths has to end in
    /// <see cref="Stop"/>, which is why this exists as a small object a component can own rather
    /// than as loose EventInstance handling repeated at each call site.
    /// </para>
    ///
    /// <para>
    /// Safe to Stop when never started, to Play twice, and to Stop twice.
    /// </para>
    /// </summary>
    public class LoopingEmitter
    {
        private EventInstance instance;
        private bool started;

        public bool IsPlaying => started && instance.isValid();

        /// <summary>
        /// Starts the loop, or does nothing if it is already running. Attaching to a GameObject lets
        /// FMOD track it as it moves; pass null for a loop that stays where it started.
        /// </summary>
        public void Play(EventReference reference, GameObject attachTo = null, Vector3 position = default)
        {
            if (reference.IsNull) return;
            if (IsPlaying) return;

            // A stale invalid handle from a previous life would otherwise be leaked here.
            if (started) Stop(false);

            try
            {
                instance = RuntimeManager.CreateInstance(reference);
            }
            catch (EventNotFoundException)
            {
                Debug.LogWarning($"[Audio] Looping event '{reference}' is not in any loaded bank.");
                return;
            }

            if (!instance.isValid()) return;

            if (attachTo != null)
                RuntimeManager.AttachInstanceToGameObject(instance, attachTo);
            else
                instance.set3DAttributes(RuntimeUtils.To3DAttributes(position));

            instance.start();
            started = true;
        }

        /// <summary>Starts the loop from a catalog entry, so loops get the same indirection one-shots do.</summary>
        public void Play(SfxId id, GameObject attachTo = null, EventReference overrideRef = default)
        {
            EventReference chosen = overrideRef;

            if (chosen.IsNull)
            {
                AudioCatalog catalog = AudioCatalog.Default;
                if (catalog != null && catalog.TryGet(id, out AudioCatalog.Entry entry))
                    chosen = entry.eventRef;
            }

            if (chosen.IsNull) return;

            Play(chosen, attachTo, attachTo != null ? attachTo.transform.position : Vector3.zero);
        }

        /// <summary>
        /// Stops and frees the loop. Must be reachable from OnDisable and OnDestroy both — a component
        /// that only cleans up in one of them leaks whenever the game exits through the other.
        /// </summary>
        public void Stop(bool allowFadeOut = true)
        {
            if (!instance.isValid())
            {
                started = false;
                return;
            }

            // Detaching first stops FMOD from following a Transform that is about to be destroyed.
            RuntimeManager.DetachInstanceFromGameObject(instance);

            // Qualified because FMOD.Studio and FMODUnity both export a STOP_MODE and both are
            // imported above; EventInstance.stop takes the FMOD.Studio one.
            instance.stop(allowFadeOut
                ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT
                : FMOD.Studio.STOP_MODE.IMMEDIATE);
            instance.release();
            instance.clearHandle();

            started = false;
        }

        /// <summary>Drives an FMOD parameter on the running loop — speed, intensity, charge and so on.</summary>
        public void SetParameter(string name, float value)
        {
            if (!IsPlaying || string.IsNullOrEmpty(name)) return;

            instance.setParameterByName(name, value);
        }

        public void SetVolume(float volume)
        {
            if (!IsPlaying) return;

            instance.setVolume(Mathf.Clamp01(volume));
        }

        /// <summary>Moves an unattached loop. No-op for loops following a GameObject.</summary>
        public void SetPosition(Vector3 position)
        {
            if (!IsPlaying) return;

            instance.set3DAttributes(RuntimeUtils.To3DAttributes(position));
        }
    }
}
