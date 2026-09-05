using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists a <see cref="SuitOxygen"/>, following <see cref="HealthSaveable"/> exactly.
    ///
    /// <para>
    /// Air is the one survival number that is not an item identity, so it is the one that needs a
    /// saver: a bottle's charge rides in the save on its own because a charged and a drained bottle
    /// are two different items, but a suit that is 41% full is a float and nothing else records it.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(SuitOxygen))]
    public class SuitOxygenSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "suitOxygen";

        private SuitOxygen suit;

        private SuitOxygen Suit => suit != null ? suit : suit = GetComponent<SuitOxygen>();

        public string SaveKey => Key;

        public struct State
        {
            public float current;

            /// Stored but never applied. The capacity belongs to the prefab, so a save is not
            /// allowed to raise or lower it; it is here so a support log can explain a clamp.
            public float max;
        }

        public object CaptureState() => Suit == null
            ? null
            : new State { current = Suit.Current, max = Suit.Max };

        public void RestoreState(JObject state)
        {
            if (Suit == null || state == null) return;

            // Float OR Integer: Newtonsoft parses a round number written as 100.0 back as an
            // integer token, so demanding Float alone silently drops every full tank.
            if (state["current"] is not { } current) return;
            if (current.Type is not (JTokenType.Float or JTokenType.Integer)) return;

            Suit.RestoreOxygen(current.Value<float>());
        }
    }
}
