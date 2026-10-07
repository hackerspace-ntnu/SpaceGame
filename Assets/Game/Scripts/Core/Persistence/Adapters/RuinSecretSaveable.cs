using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keeps a scanned ruin secret exposed.
    ///
    /// <para>
    /// A secret is more than a visual: while it is revealed its interactable is ENABLED, which is
    /// how a hidden door becomes a door you can actually open. <c>hideWhenDormant</c> switches the
    /// renderers back off on <c>Awake</c>, so a player who scanned a wall open, saved, and loaded
    /// found the wall solid again and the scanner pulse wasted.
    /// </para>
    /// <para>
    /// What is stored is the remaining reveal, not a flag. A reveal is a countdown — the pulse buys
    /// a few seconds — so "it was revealed" alone would either expire instantly or last forever.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(RuinSecret))]
    public class RuinSecretSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "ruinSecret";       // written into save files — NEVER rename

        private RuinSecret secret;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private RuinSecret Secret => secret != null ? secret : secret = GetComponent<RuinSecret>();

        public string SaveKey => Key;

        public struct State
        {
            /// <summary>Seconds of reveal left when the save was written.</summary>
            public float revealRemaining;
        }

        public object CaptureState()
        {
            if (Secret == null || !Secret.IsRevealed) return null;

            float remaining = Secret.RevealRemaining;
            return remaining > 0f ? new State { revealRemaining = remaining } : (object)null;
        }

        public void RestoreState(JObject state)
        {
            if (Secret == null) return;

            // Nothing stored means dormant, and it has to be applied rather than assumed: a secret
            // revealed earlier in this session is looking at the same component.
            if (state == null) { Secret.RestoreReveal(0f); return; }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Secret.RestoreReveal(restored.revealRemaining);
        }
    }
}
