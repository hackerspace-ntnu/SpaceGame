using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace SpaceGame.Audio
{
    /// <summary>
    /// How the game asks for a sound. One call, no setup, safe to call from anywhere at any time.
    ///
    /// <para>
    /// Every overload takes an optional <c>overrideRef</c>. When a component already carries an
    /// EventReference assigned in the inspector, pass it — it wins over the catalog. That is what
    /// lets the thirty-nine assignments already sitting in prefabs keep working untouched while
    /// everything that was never assigned starts making noise.
    /// </para>
    ///
    /// <para>
    /// Nothing in here throws. FMOD raises on an event that is not in a loaded bank, and a sound
    /// failing is never worth losing the frame it was called from, so lookups that miss are logged
    /// once per id and then silently skipped.
    /// </para>
    /// </summary>
    public static class Sfx
    {
        // Cooldown bookkeeping. Keyed on the pair (sound, whoever asked) so that one chatty NPC
        // rate-limits itself without muting the NPC standing next to it.
        private static readonly Dictionary<long, float> LastPlayed = new Dictionary<long, float>(128);

        // Missing events are a content problem, not a runtime one. Say it once per id, then drop it.
        private static readonly HashSet<SfxId> Complained = new HashSet<SfxId>();

        private const int PruneThreshold = 512;

        /// <summary>Plays a positioned one-shot. The everyday call.</summary>
        public static void Play(SfxId id, Vector3 position, int sourceKey = 0)
        {
            PlayInternal(id, position, null, sourceKey, default, false);
        }

        /// <summary>
        /// Plays a positioned one-shot, preferring an inspector-assigned event when there is one.
        /// </summary>
        public static void Play(SfxId id, Vector3 position, EventReference overrideRef, int sourceKey = 0)
        {
            PlayInternal(id, position, null, sourceKey, overrideRef, true);
        }

        /// <summary>Plays at a transform's position, using it as the rate-limiting source.</summary>
        public static void Play(SfxId id, Transform source)
        {
            if (source == null) return;

            PlayInternal(id, source.position, null, source.GetInstanceID(), default, false);
        }

        /// <summary>Plays at a transform's position, preferring an inspector-assigned event.</summary>
        public static void Play(SfxId id, Transform source, EventReference overrideRef)
        {
            if (source == null) return;

            PlayInternal(id, source.position, null, source.GetInstanceID(), overrideRef, true);
        }

        /// <summary>
        /// Plays a one-shot that follows a moving object. Worth the extra cost only for sounds long
        /// enough that the object will have moved before they finish.
        /// </summary>
        public static void PlayAttached(SfxId id, GameObject follow, EventReference overrideRef = default)
        {
            if (follow == null) return;

            PlayInternal(id, follow.transform.position, follow, follow.GetInstanceID(),
                         overrideRef, !overrideRef.IsNull);
        }

        /// <summary>
        /// Plays without a position — menus, HUD, anything that should not pan or fall off with
        /// distance. Skips the distance cull for the same reason.
        /// </summary>
        public static void Play2D(SfxId id, EventReference overrideRef = default)
        {
            PlayInternal(id, Vector3.zero, null, 0, overrideRef, !overrideRef.IsNull, ignoreDistance: true);
        }

        private static void PlayInternal(SfxId id, Vector3 position, GameObject attachTo, int sourceKey,
                                         EventReference overrideRef, bool hasOverride,
                                         bool ignoreDistance = false)
        {
            if (id == SfxId.None && !hasOverride) return;

            AudioCatalog.Entry entry = null;
            AudioCatalog catalog = AudioCatalog.Default;
            if (catalog != null) catalog.TryGet(id, out entry);

            // An inspector assignment beats the catalog, but the catalog still supplies the tuning
            // (cooldown, range, trim) for that slot — those describe the situation, not the asset.
            EventReference chosen = hasOverride && !overrideRef.IsNull
                ? overrideRef
                : entry != null ? entry.eventRef : default;

            if (chosen.IsNull)
            {
                if (id != SfxId.None && Complained.Add(id))
                {
                    Debug.LogWarning($"[Audio] {id} has no event assigned in the AudioCatalog and no " +
                                     "inspector override. It will stay silent.");
                }
                return;
            }

            float cooldown = entry?.cooldown ?? 0f;
            if (cooldown > 0f && IsOnCooldown(id, sourceKey, cooldown)) return;

            float maxDistance = entry?.maxDistance ?? 0f;
            if (!ignoreDistance && maxDistance > 0f && StudioListener.ListenerCount > 0)
            {
                // Squared comparison — this runs on every footstep of every entity in the level.
                if (StudioListener.DistanceSquaredToNearestListener(position) > maxDistance * maxDistance)
                    return;
            }

            float volume = entry?.volume ?? 1f;

            try
            {
                // PlayOneShot cannot take a volume, so anything trimmed has to go the long way round.
                if (volume >= 0.999f && attachTo == null)
                {
                    RuntimeManager.PlayOneShot(chosen, position);
                }
                else if (volume >= 0.999f)
                {
                    RuntimeManager.PlayOneShotAttached(chosen, attachTo);
                }
                else
                {
                    EventInstance instance = RuntimeManager.CreateInstance(chosen);
                    instance.setVolume(volume);

                    if (attachTo != null)
                        RuntimeManager.AttachInstanceToGameObject(instance, attachTo);
                    else
                        instance.set3DAttributes(RuntimeUtils.To3DAttributes(position));

                    instance.start();

                    // Released immediately: FMOD keeps it alive until it finishes, then reclaims it.
                    // Skipping this is the classic way to leak every one-shot the game ever plays.
                    instance.release();
                }
            }
            catch (EventNotFoundException)
            {
                if (Complained.Add(id))
                {
                    Debug.LogWarning($"[Audio] {id} points at '{chosen}', which is not in any loaded bank. " +
                                     "Check the bank list and the event path.");
                }
            }
        }

        private static bool IsOnCooldown(SfxId id, int sourceKey, float cooldown)
        {
            long key = ((long)(int)id << 32) ^ (uint)sourceKey;
            float now = Time.unscaledTime;

            if (LastPlayed.TryGetValue(key, out float last) && now - last < cooldown)
                return true;

            if (LastPlayed.Count > PruneThreshold) Prune(now);

            LastPlayed[key] = now;
            return false;
        }

        /// <summary>
        /// Drops stale cooldown entries. Sources are GameObjects that get destroyed, so without this
        /// the table grows for the whole session.
        /// </summary>
        private static void Prune(float now)
        {
            var stale = new List<long>();

            foreach (var kvp in LastPlayed)
            {
                // 30s outlives every cooldown the catalog can sensibly hold, so anything older than
                // that cannot be gating anything.
                if (now - kvp.Value > 30f) stale.Add(kvp.Key);
            }

            for (int i = 0; i < stale.Count; i++) LastPlayed.Remove(stale[i]);
        }

        /// <summary>Forgets all cooldowns and warnings. For entering play mode with domain reload off.</summary>
        public static void Reset()
        {
            LastPlayed.Clear();
            Complained.Clear();
            AudioCatalog.ClearCache();
        }

#if UNITY_EDITOR
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Reset();
#endif
    }
}
