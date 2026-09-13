// What NetAuthority switches off on a remote copy: the layer that DRIVES motion, found by type.
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Locomotion;

namespace SpaceGame.Core
{
    /// <summary>
    /// Finds the behaviours that move an entity of its own accord, and decides which components
    /// are that entity's own rather than a separate entity parented under it.
    /// </summary>
    public static class SimulationDrivers
    {
        /// <summary>
        /// Everything on <paramref name="root"/> that moves it of its own accord: the agent brain,
        /// its motor, and the NavMeshAgent underneath both.
        ///
        /// Matched by interface and base type, never by name, so a new motor or a renamed
        /// controller is covered the day it is written.
        /// </summary>
        public static List<Behaviour> Discover(GameObject root)
        {
            var found = new List<Behaviour>();
            if (root == null) return found;

            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || !BelongsTo(root, behaviour)) continue;

                if (behaviour is AgentController or IMovementMotor)
                    found.Add(behaviour);
            }

            foreach (NavMeshAgent agent in root.GetComponentsInChildren<NavMeshAgent>(true))
                if (BelongsTo(root, agent)) found.Add(agent);

            return found;
        }

        /// <summary>
        /// Is <paramref name="candidate"/> part of <paramref name="root"/>'s own entity, rather
        /// than a separate one that happens to be parented under it?
        ///
        /// A rider is parented into their mount's hierarchy while mounted, so without this a
        /// mount's remote copy would reach down and switch off the player sitting on it — and,
        /// worse, switch them back on again when the rider dismounted, handing a remote player's
        /// controls to the wrong machine. The boundary between two entities is a NetworkObject.
        /// </summary>
        internal static bool BelongsTo(GameObject root, Component candidate)
        {
            NetworkObject owner = candidate.GetComponentInParent<NetworkObject>();
            NetworkObject mine = root.GetComponentInParent<NetworkObject>();

            // Neither is networked: the old whole-hierarchy behaviour, which is right for a plain
            // prefab whose parts are all one thing.
            return owner == null || mine == null || owner == mine;
        }
    }
}
