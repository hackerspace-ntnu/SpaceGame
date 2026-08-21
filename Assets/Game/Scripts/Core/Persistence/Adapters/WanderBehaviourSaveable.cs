using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the older brain-driven wander helper: its current leg, its place in a patrol route,
    /// and its leash anchor.
    ///
    /// <b>Why an obsolete-looking component still gets a saver.</b> <c>WanderBehaviour</c> predates
    /// the module system and is only used by <c>EnemyBrain</c> and <c>NpcBrain</c> — but it is placed
    /// in live chunk scenes, and it carries the same latched <c>spawnAnchor</c> as every other
    /// roaming component here. An anchored leash that re-latches on load walks the creature's
    /// territory across the map one save at a time, and a component nobody is looking at is exactly
    /// where that goes unnoticed longest.
    /// </summary>
    [RequireComponent(typeof(WanderBehaviour))]
    public class WanderBehaviourSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "wanderBehaviour";     // written into save files — NEVER rename

        private WanderBehaviour behaviour;

        private WanderBehaviour Behaviour =>
            behaviour != null ? behaviour : behaviour = GetComponent<WanderBehaviour>();

        public string SaveKey => Key;

        public struct State
        {
            public bool hasDestination;

            /// <summary>Meaningless unless <see cref="hasDestination"/>.</summary>
            public Vector3 destination;

            public float waitTimer;
            public int patrolIndex;

            /// <summary>+1 or -1 on a ping-pong route.</summary>
            public int patrolDirection;

            public bool hasSpawnAnchor;

            /// <summary>Centre of the leash. Unused when an explicit anchor transform is assigned.</summary>
            public Vector3 spawnAnchor;
        }

        public object CaptureState()
        {
            if (Behaviour == null) return null;

            return new State
            {
                hasDestination = Behaviour.HasDestination,
                destination = Behaviour.CurrentDestination,
                waitTimer = Behaviour.WaitTimer,
                patrolIndex = Behaviour.PatrolIndex,
                patrolDirection = Behaviour.PatrolDirection,
                hasSpawnAnchor = Behaviour.HasSpawnAnchor,
                spawnAnchor = Behaviour.SpawnAnchor,
            };
        }

        public void RestoreState(JObject state)
        {
            if (Behaviour == null) return;

            if (state == null)
            {
                // Matches the component's own ResetState, minus the anchor — which stays, because
                // clearing it is the drift this saver exists to stop.
                Behaviour.RestoreWanderState(false, Vector3.zero, 0f, 0, 1, false, Vector3.zero);
                return;
            }

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            Behaviour.RestoreWanderState(restored.hasDestination, restored.destination,
                                         restored.waitTimer, restored.patrolIndex,
                                         restored.patrolDirection,
                                         restored.hasSpawnAnchor, restored.spawnAnchor);
        }
    }
}
