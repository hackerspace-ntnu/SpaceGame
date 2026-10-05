using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keeps a creature as tame as the players made it. Trust is won one gift at a time over a long
    /// visit, and a creature that forgets it on load hands back every item it was given for nothing.
    /// Whether it is dead is not here: that is <see cref="HealthSaveable"/>'s.
    /// </summary>
    [RequireComponent(typeof(CreatureTrust))]
    public class CreatureTrustSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "creatureTrust";   // written into save files — NEVER rename

        private CreatureTrust creatureTrust;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake.
        private CreatureTrust Trust => creatureTrust != null ? creatureTrust : creatureTrust = GetComponent<CreatureTrust>();

        public string SaveKey => Key;

        public struct State
        {
            public float trust;
        }

        public object CaptureState() =>
            Trust != null && Trust.Trust > 0f ? new State { trust = Trust.Trust } : null;

        public void RestoreState(JObject state)
        {
            if (Trust == null) return;

            // No entry means wild, and it has to be said: a previous restore may have tamed it.
            Trust.Restore(state != null ? state.ToObject<State>(SaveSerializer.Serializer).trust : 0f);
        }
    }
}
