using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the walk a wanderer was already on.
    ///
    /// Small, and worth it for a reason that is easy to miss: <c>WanderModule</c> is the fallback on
    /// most creatures in the world, so "the wander state resets" is what a player actually sees on
    /// load — every animal in sight stands still for a beat and then sets off somewhere new, all at
    /// the same moment. Restoring the destination and the pause makes a reload look like the world
    /// carried on rather than like everything in it was just switched on.
    ///
    /// <c>WanderModule</c> has no anchor: it picks from wherever the creature is standing, so there
    /// is no territory here to drift. That is <see cref="AirWanderSaveable"/>'s problem.
    /// </summary>
    [RequireComponent(typeof(WanderModule))]
    public class WanderSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "wander";     // written into save files — NEVER rename

        private WanderModule wander;

        private WanderModule Wander => wander != null ? wander : wander = GetComponent<WanderModule>();

        public string SaveKey => Key;

        public struct State
        {
            public bool hasDestination;

            /// <summary>Meaningless unless <see cref="hasDestination"/>.</summary>
            public Vector3 destination;

            /// <summary>Seconds left of the idle pause after reaching a point.</summary>
            public float waitTimer;
        }

        public object CaptureState()
        {
            if (Wander == null) return null;

            return new State
            {
                hasDestination = Wander.HasDestination,
                destination = Wander.CurrentDestination,
                waitTimer = Wander.WaitTimer,
            };
        }

        public void RestoreState(JObject state)
        {
            if (Wander == null) return;

            if (state == null)
            {
                Wander.RestoreWander(false, Vector3.zero, 0f);
                return;
            }

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            Wander.RestoreWander(restored.hasDestination, restored.destination, restored.waitTimer);
        }
    }
}
