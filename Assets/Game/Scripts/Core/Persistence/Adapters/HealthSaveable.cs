using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists a <see cref="HealthComponent"/>. Works for anything that has one — the player,
    /// agents, destructible props — so nothing needs a health saver of its own.
    /// </summary>
    [RequireComponent(typeof(HealthComponent))]
    public class HealthSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "health";

        private HealthComponent health;

        private HealthComponent Health => health != null ? health : health = GetComponent<HealthComponent>();

        public string SaveKey => Key;

        public struct State
        {
            public int current;

            /// Stored but never applied. maxHealth belongs to the prefab, so a save is not allowed
            /// to raise or lower it; it is here so a support log can explain a clamped restore.
            public int max;
        }

        public object CaptureState() => Health == null
            ? null
            : new State { current = Health.GetHealth, max = Health.GetMaxHealth };

        public void RestoreState(JObject state)
        {
            if (Health == null || state == null) return;
            if (state["current"] is not { Type: JTokenType.Integer } current) return;

            Health.RestoreHealth(current.Value<int>());
        }
    }
}
