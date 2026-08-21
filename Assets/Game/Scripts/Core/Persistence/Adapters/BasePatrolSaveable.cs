using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists a base patroller's territory and the leg it was walking.
    ///
    /// <b>Same anchor bug as <see cref="PatrolSaveable"/>, same fix.</b>
    /// <c>BasePatrolModule.EnsureAnchor</c> latches the centre of the patrol area from
    /// <c>transform.position</c> on the first tick and never revisits it. Because the record's pose
    /// is applied before this restore runs, an unsaved anchor is re-latched wherever the creature
    /// was when the game was saved — so the area it guards drifts across the world one reload at a
    /// time, and nothing in the game says so.
    ///
    /// The phase and destination are here for the ordinary reason: a creature halfway to a
    /// destination should keep walking to it, not stop, wait out a fresh pause and pick somewhere
    /// else.
    /// </summary>
    [RequireComponent(typeof(BasePatrolModule))]
    public class BasePatrolSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "basePatrol";     // written into save files — NEVER rename

        private BasePatrolModule patrol;

        private BasePatrolModule Patrol =>
            patrol != null ? patrol : patrol = GetComponent<BasePatrolModule>();

        public string SaveKey => Key;

        public struct State
        {
            public bool hasSpawnAnchor;
            public Vector3 spawnAnchor;

            /// <summary>True for Moving, false for Waiting. The module has exactly two states.</summary>
            public bool moving;

            public Vector3 destination;

            /// <summary>Seconds left of the pause between destinations.</summary>
            public float waitTimer;
        }

        public object CaptureState()
        {
            if (Patrol == null) return null;

            return new State
            {
                hasSpawnAnchor = Patrol.HasSpawnAnchor,
                spawnAnchor = Patrol.SpawnAnchor,
                moving = Patrol.IsMoving,
                destination = Patrol.Destination,
                waitTimer = Patrol.WaitTimer,
            };
        }

        public void RestoreState(JObject state)
        {
            if (Patrol == null) return;

            if (state == null)
            {
                // Defaults are "waiting, nowhere to go". The anchor is left alone on purpose — see
                // the class summary; clearing it is the drift.
                Patrol.RestoreBasePatrol(false, Vector3.zero, false, Vector3.zero, 0f);
                return;
            }

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            Patrol.RestoreBasePatrol(restored.hasSpawnAnchor, restored.spawnAnchor,
                                     restored.moving, restored.destination, restored.waitTimer);
        }
    }
}
