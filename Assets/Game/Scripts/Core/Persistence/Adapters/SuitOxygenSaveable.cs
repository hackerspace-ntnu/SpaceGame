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
    /// <b>The SUIT's reserve only.</b> The tank's charge is not recorded here and must not be: it
    /// belongs to the tank, and travels in the pack's own record like every other thing the pack is
    /// carrying (see <c>PackSaveCodec</c>). Storing it in two files is how the two come to
    /// disagree, and the tank is the half that would lose.
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
            /// Seconds of air left in the suit's own reserve.
            public float current;

            /// Stored but never applied. The capacity belongs to the prefab, so a save is not
            /// allowed to raise or lower it; it is here so a support log can explain a clamp.
            public float max;
        }

        public object CaptureState() => Suit == null
            ? null
            : new State { current = Suit.SuitSeconds, max = Suit.SuitCapacity };

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
