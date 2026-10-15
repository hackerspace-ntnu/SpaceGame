using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists how long a dead body or the loot it shed (<see cref="Remains"/>) has left before it is
    /// taken away, so a save/quit/load -- or its chunk unloading -- pauses the countdown instead of
    /// restarting it or leaving the thing lying there for good.
    ///
    /// Carried by everything that can become remains (anything with health, every pickup), not only by
    /// what already is: a <see cref="Remains"/> is added at runtime, so a freshly loaded item has none
    /// until this saver puts one back, and a saver added after the record is read is handed nothing.
    /// Writes nothing while nothing is counting, which is every living creature and every item a
    /// player dropped.
    /// </summary>
    public class RemainsSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "remains";     // written into save files — NEVER rename

        public string SaveKey => Key;

        public struct State
        {
            public float remaining;
        }

        public object CaptureState()
        {
            if (!TryGetComponent(out Remains remains) || !remains.Counting) return null;

            return new State { remaining = remains.Remaining };
        }

        public void RestoreState(JObject state)
        {
            if (state == null) return;

            Remains.On(gameObject).Begin(state.ToObject<State>(SaveSerializer.Serializer).remaining);
        }
    }
}
