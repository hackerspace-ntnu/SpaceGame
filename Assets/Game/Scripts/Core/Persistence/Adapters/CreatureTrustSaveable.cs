using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists how far an animal has come to trust people (<see cref="CreatureTrust"/>). It cannot be recomputed:
    /// nothing in the world records the pets that won it over, and without this a trusting animal reloads wary and
    /// refuses the saddle it was ready for.
    /// </summary>
    [RequireComponent(typeof(CreatureTrust))]
    public class CreatureTrustSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "trust";          // written into save files — NEVER rename

        private CreatureTrust trust;

        private CreatureTrust Trust => trust != null ? trust : trust = GetComponent<CreatureTrust>();

        public string SaveKey => Key;

        public struct State
        {
            public int pets;
        }

        public object CaptureState()
        {
            // Null for an animal nobody has petted, so the common case costs no bytes.
            if (Trust == null || Trust.Pets == 0) return null;
            return new State { pets = Trust.Pets };
        }

        public void RestoreState(JObject state)
        {
            if (Trust == null) return;
            Trust.Restore(state == null ? 0 : state.ToObject<State>(SaveSerializer.Serializer).pets);
        }
    }
}
