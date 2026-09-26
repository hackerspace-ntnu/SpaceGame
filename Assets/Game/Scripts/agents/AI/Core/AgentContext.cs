// Snapshot of runtime data passed from AgentController to behaviour modules each frame.
using UnityEngine;

namespace SpaceGame.Agents
{
    public struct AgentContext
    {
        public Transform Self;
        public Vector3 Position;
        public Vector3 Velocity;
        public bool HasReachedDestination;
        public bool IsImmobile;

        // The agent's shared target decision, or null on agents without an AgentTargeting
        // component. Combat modules should read this instead of querying EntityTargetRegistry
        // themselves — that is what keeps chase, melee and ranged committed to the same entity.
        public AgentTargeting Targeting;

        // Where the agent is trying to get to, or null on agents without an AgentGoal component.
        // The counterpart of Targeting for travel: task modules write a destination here and
        // movement modules read it, so nothing that decides WHERE also decides HOW.
        public AgentGoal Goal;

        public bool IsMoving => Velocity.sqrMagnitude > 0.01f;
    }
}
