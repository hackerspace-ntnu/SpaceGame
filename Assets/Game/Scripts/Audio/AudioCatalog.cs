using System;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

namespace SpaceGame.Audio
{
    /// <summary>
    /// The single asset that decides which FMOD event every <see cref="SfxId"/> plays, and how loud,
    /// how often and how far away it may be heard.
    ///
    /// <para>
    /// Before this existed the only way to give a sound to a component was to drag an EventReference
    /// onto it in the inspector. That works, and every one of those assignments still wins over this
    /// catalog — but it does not scale to seventy sounds across forty-odd call sites, and an
    /// unassigned field is silent with no way to tell "nobody got to it yet" from "meant to be
    /// quiet". Defaults live here so a new call site makes noise the moment it is written.
    /// </para>
    ///
    /// <para>
    /// Loaded by path from a Resources folder rather than wired to a scene object on purpose: audio
    /// is asked for from prefabs, from items that exist before any manager does, and from menus that
    /// can be entered without ever passing through Bootstrap.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "AudioCatalog", menuName = "SpaceGame/Audio/Audio Catalog")]
    public class AudioCatalog : ScriptableObject
    {
        /// <summary>Where <see cref="Default"/> looks. Must sit under some <c>Resources</c> folder.</summary>
        public const string ResourcePath = "AudioCatalog";

        [Serializable]
        public class Entry
        {
            public SfxId id;

            [Tooltip("The FMOD event this sound resolves to. Leave empty to make the sound silent.")]
            public EventReference eventRef;

            [Tooltip("Seconds before this sound may be triggered again by the same source. 0 disables " +
                     "the limit. Exists to stop chatter and footsteps from stacking into mush.")]
            public float cooldown;

            [Tooltip("Past this many metres from the nearest listener the sound is dropped before FMOD " +
                     "ever sees it. 0 means never cull. Cheap insurance against a crowd of distant NPCs.")]
            public float maxDistance;

            [Range(0f, 1f)]
            [Tooltip("Trim applied on top of whatever the event itself does. 1 plays the event as authored.")]
            public float volume = 1f;

            [Tooltip("Free text — used here to mark which mappings are stand-ins awaiting real audio.")]
            public string note;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        private Dictionary<SfxId, Entry> lookup;

        private static AudioCatalog cached;
        private static bool loadAttempted;

        /// <summary>
        /// The catalog every <see cref="Sfx"/> call goes through, or null if the asset is missing.
        /// <para>
        /// A missing catalog is reported once and then tolerated forever. Audio is the one system
        /// that must never take the game down with it.
        /// </para>
        /// </summary>
        public static AudioCatalog Default
        {
            get
            {
                if (cached != null) return cached;
                if (loadAttempted) return null;

                loadAttempted = true;
                cached = Resources.Load<AudioCatalog>(ResourcePath);

                if (cached == null)
                {
                    Debug.LogWarning(
                        $"[Audio] No AudioCatalog found at Resources/{ResourcePath}. Sounds asked for " +
                        "by SfxId will be silent; EventReference fields assigned in the inspector still play.");
                }

                return cached;
            }
        }

        /// <summary>Drops the cached asset so the next lookup reloads. Used by the editor tooling.</summary>
        public static void ClearCache()
        {
            cached = null;
            loadAttempted = false;
        }

        public IReadOnlyList<Entry> Entries => entries;

        public bool TryGet(SfxId id, out Entry entry)
        {
            if (lookup == null) BuildLookup();

            return lookup.TryGetValue(id, out entry);
        }

        private void BuildLookup()
        {
            lookup = new Dictionary<SfxId, Entry>(entries.Count);

            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (e == null || e.id == SfxId.None) continue;

                // A duplicate id is a content mistake rather than a crash — first one wins, and say so
                // loudly enough that whoever added the second sees it.
                if (lookup.ContainsKey(e.id))
                {
                    Debug.LogWarning($"[Audio] AudioCatalog lists {e.id} more than once. Using the first entry.");
                    continue;
                }

                lookup[e.id] = e;
            }
        }

        private void OnValidate()
        {
            // The lookup is built from this list, so it has to go stale whenever the list is edited.
            lookup = null;

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null) continue;

                entries[i].cooldown = Mathf.Max(0f, entries[i].cooldown);
                entries[i].maxDistance = Mathf.Max(0f, entries[i].maxDistance);
                entries[i].volume = Mathf.Clamp01(entries[i].volume);
            }
        }
    }
}
