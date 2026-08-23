using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists a patroller's route: where it had got to, where it was walking, and — the part that
    /// matters most — where its territory is centred.
    ///
    /// <b>Why this is its own saver and not part of the agent's.</b> Patrol progress used to ride on
    /// <see cref="AgentStateSaveable"/>, which is keyed off <c>AgentTargeting</c>. The patrol robots
    /// and the deathmatch bots have a <see cref="PatrolModule"/> and no <c>AgentTargeting</c> at all,
    /// so the one population whose entire identity is a route was the one population saving nothing
    /// about it. Keying off the module that owns the state is the fix, and it is the same rule every
    /// other saver here follows.
    ///
    /// <b>The anchor is the important field.</b> In <see cref="PatrolMode.RadiusBased"/> with no
    /// explicit centre, <c>EnsureAnchor</c> latches the patrol circle from <c>transform.position</c>
    /// the first time it ticks. The record's pose is applied before this restore runs, so without a
    /// saved anchor the guard re-latches at wherever it was standing when the game was saved — and
    /// its territory migrates a little further on every single save/load cycle. Saving the anchor
    /// and marking it latched is what pins the patrol area to the place it was authored for.
    /// </summary>
    [RequireComponent(typeof(PatrolModule))]
    public class PatrolSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "patrol";     // written into save files — NEVER rename

        private PatrolModule patrol;

        // Lazy, not cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private PatrolModule Patrol => patrol != null ? patrol : patrol = GetComponent<PatrolModule>();

        public string SaveKey => Key;

        public struct State
        {
            public int waypointIndex;

            /// <summary>+1 or -1 on a ping-pong route.</summary>
            public int waypointDirection;

            public bool hasDestination;

            /// <summary>Meaningless unless <see cref="hasDestination"/>. A nullable would not survive JSON as cleanly.</summary>
            public Vector3 destination;

            /// <summary>Seconds left of the pause at the current post.</summary>
            public float waitTimer;

            public bool hasSpawnAnchor;
            public Vector3 spawnAnchor;
        }

        public object CaptureState()
        {
            if (Patrol == null) return null;

            return new State
            {
                waypointIndex = Patrol.WaypointIndex,
                waypointDirection = Patrol.WaypointDirection,
                hasDestination = Patrol.HasDestination,
                destination = Patrol.Destination,
                waitTimer = Patrol.WaitTimer,
                hasSpawnAnchor = Patrol.HasSpawnAnchor,
                spawnAnchor = Patrol.SpawnAnchor,
            };
        }

        public void RestoreState(JObject state)
        {
            if (Patrol == null) return;

            if (state == null)
            {
                // No record: the route is at its defaults. The ANCHOR is deliberately not touched —
                // "nothing was recorded" is not the same claim as "re-centre your territory on
                // wherever you are standing", and the second one is the drift bug.
                Patrol.RestorePatrolProgress(0, 1);
                Patrol.RestorePatrolLeg(false, Vector3.zero, 0f);
                return;
            }

            // Through the shared serializer, always: it is the only thing that knows how to read a
            // Vector3 back, and one read without the registered converters recurses through the
            // struct's own properties into a stack overflow.
            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            Patrol.RestoreSpawnAnchor(restored.hasSpawnAnchor, restored.spawnAnchor);
            Patrol.RestorePatrolProgress(restored.waypointIndex, restored.waypointDirection);
            Patrol.RestorePatrolLeg(restored.hasDestination, restored.destination, restored.waitTimer);
        }
    }
}
