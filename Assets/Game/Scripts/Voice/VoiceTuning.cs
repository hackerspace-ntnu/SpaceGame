// How far a voice carries — the tunables of proximity, in one asset a designer can edit.
//
// Lives at Resources/VoiceTuning, the same pattern AudioCatalog uses: code-bootstrapped systems
// (VoiceSession is created from a static) have no scene object to hang serialized fields on, and a
// Resources asset is the one place such a system can read Inspector-tuned values from. If the asset
// is ever missing the defaults below apply, so voice still works — it just stops being tunable.
//
// The host routes with ITS copy and every listener fades with its own, so these values must match
// across a session. They do, because the asset ships inside the build.
using UnityEngine;

namespace SpaceGame.Voice
{
    [CreateAssetMenu(fileName = "VoiceTuning", menuName = "SpaceGame/Audio/Voice Tuning")]
    public sealed class VoiceTuning : ScriptableObject
    {
        public const string ResourcePath = "VoiceTuning";

        [Tooltip("Metres within which a voice plays at full volume, before it starts to fade.")]
        [SerializeField, Min(0.1f)] private float fullVolumeDistance = 2.5f;

        [Tooltip("Metres at which a voice has faded to nothing. Nobody farther away is sent it at all.")]
        [SerializeField, Min(1f)] private float silentDistance = 30f;

        [Tooltip("How far past the silent distance the host still sends a voice, as a multiple of it. " +
                 "Positions lag a little over the network; without the margin a listener walking in would " +
                 "hear the fade start mid-word instead of from its edge.")]
        [SerializeField, Range(1f, 2f)] private float routeMargin = 1.15f;

        [Tooltip("Metres above a player's origin that their voice comes from.")]
        [SerializeField, Min(0f)] private float mouthHeight = 1.6f;

        private static VoiceTuning active;

        /// <summary>Metres to the far edge of the fade: FMOD's max distance, and the audibility limit.</summary>
        public float SilentDistance => Mathf.Max(1f, silentDistance);

        /// <summary>Metres of full volume: FMOD's min distance. Always short of the silent distance.</summary>
        public float FullVolumeDistance => Mathf.Clamp(fullVolumeDistance, 0.1f, SilentDistance * 0.95f);

        /// <summary>Metres within which the host forwards a voice at all.</summary>
        public float RouteDistance => SilentDistance * Mathf.Clamp(routeMargin, 1f, 2f);

        public float MouthHeight => Mathf.Max(0f, mouthHeight);

        /// <summary>The shipped asset, or the defaults above when it is missing.</summary>
        public static VoiceTuning Active
        {
            get
            {
                if (active != null) return active;

                active = Resources.Load<VoiceTuning>(ResourcePath);
                if (active != null) return active;

                Debug.LogWarning($"[Voice] No VoiceTuning at Resources/{ResourcePath}; using the built-in " +
                                 "distances. Create one from SpaceGame/Audio/Voice Tuning to tune them.");

                active = CreateInstance<VoiceTuning>();
                active.hideFlags = HideFlags.DontSave;
                return active;
            }
        }
    }
}
