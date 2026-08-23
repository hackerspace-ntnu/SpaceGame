using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists where an agent was trying to get to.
    ///
    /// <b>Why it is not folded into <see cref="NpcTaskSaveable"/>.</b> <see cref="AgentGoal"/> is the
    /// one place a destination is ever written down, and <c>NpcTaskModule</c> is only one of the
    /// things that writes to it — <c>AgentController</c> attaches a goal to every agent, and anything
    /// with a reason to send one somewhere uses it. A saver keyed off the task module would leave
    /// every other traveller's destination unsaved, which is the same mistake that left the patrol
    /// robots' routes riding on a component they do not have.
    ///
    /// <b>Restored, not re-issued.</b> The goal is put back verbatim rather than through
    /// <c>Set</c>/<c>TrySetSampled</c>: the NavMesh under the destination belongs to a chunk that may
    /// still be streaming, so re-sampling it at restore time would either fail or snap the
    /// destination to whatever mesh happens to be loaded.
    /// </summary>
    [RequireComponent(typeof(AgentGoal))]
    public class AgentGoalSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "agentGoal";     // written into save files — NEVER rename

        private AgentGoal goal;

        private AgentGoal Goal => goal != null ? goal : goal = GetComponent<AgentGoal>();

        public string SaveKey => Key;

        public struct State
        {
            /// <summary>False is an ordinary state — an agent with nowhere to be.</summary>
            public bool hasGoal;

            public Vector3 position;

            /// <summary>How close counts as there. Usually the destination site's radius.</summary>
            public float arriveRadius;

            /// <summary>Human-readable, and used by chatter and dialog. "picking over the Vela wreck".</summary>
            public string reason;

            /// <summary>The WorldSite this goal came from, if any.</summary>
            public string siteId;

            /// <summary>A property of the errand, not of the agent — an amble and a hurry differ here.</summary>
            public float speedMultiplier;
        }

        public object CaptureState()
        {
            if (Goal == null) return null;

            // An agent with no goal is at its default, and the key is dropped rather than storing a
            // row of zeroes on every idle creature in the world.
            if (!Goal.HasGoal) return null;

            return new State
            {
                hasGoal = true,
                position = Goal.Position,
                arriveRadius = Goal.ArriveRadius,
                reason = Goal.Reason,
                siteId = Goal.SiteId,
                speedMultiplier = Goal.SpeedMultiplier,
            };
        }

        public void RestoreState(JObject state)
        {
            if (Goal == null) return;

            if (state == null)
            {
                Goal.RestoreGoal(false, Vector3.zero, 2f, null, null, 1f);
                return;
            }

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            Goal.RestoreGoal(restored.hasGoal, restored.position, restored.arriveRadius,
                             restored.reason, restored.siteId, restored.speedMultiplier);
        }
    }
}
