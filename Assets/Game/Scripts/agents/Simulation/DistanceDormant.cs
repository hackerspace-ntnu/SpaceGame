// The opt-in that lets simulation distance put this NPC or animal to sleep. Only people and animals
// carry it (<c>DistanceDormancyWiring</c> puts it on every prefab under Agents/Characters, Robots and
// creatures); a machine never does, so the sky fleet, the Strider houses and the craft keep moving.
//
// The decision is <c>SimulationRange</c>'s and the rule SimulationRules'; this component only reads its own
// body for the inputs, and stamps when it was last hurt so a long-range shot wakes it.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AgentController))]
    public class DistanceDormant : MonoBehaviour
    {
        private static readonly List<DistanceDormant> registered = new();

        public static IReadOnlyList<DistanceDormant> All => registered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => registered.Clear();

        public AgentController Agent { get; private set; }

        private HealthComponent health;
        private NpcFlightModule flight;
        private AgentTargeting targeting;
        private GroupMembership membership;
        private float lastHurtTime = float.NegativeInfinity;

        private void OnEnable()
        {
            Agent = GetComponent<AgentController>();
            health = GetComponent<HealthComponent>();
            flight = GetComponentInChildren<NpcFlightModule>(true);
            targeting = GetComponent<AgentTargeting>();

            if (!registered.Contains(this)) registered.Add(this);
            if (health) health.OnDamage += OnDamaged;
        }

        // Whoever switches the marker itself off must not leave the agent parked with nobody left to
        // unpark it. A body going away takes the marker down with enabled still true, and is left
        // parked: unparking re-enables its NavMeshAgent, and on a Play Mode stop Netcode destroys
        // the sleepers after the NavMesh is gone, one "not close enough to the NavMesh" each. A body
        // merely deactivated re-registers on activation and the next SimulationRange tick re-derives it.
        private void OnDisable()
        {
            registered.Remove(this);
            if (health) health.OnDamage -= OnDamaged;
            if (Agent && !enabled) Agent.Dormant = false;
        }

        private void OnDamaged(int amount) => lastHurtTime = Time.time;

        public DormancyInputs Read(bool cellAwake, HashSet<Transform> players, float now)
        {
            // Stamped by NpcWorldSim after this object woke (NpcSpawn's beforeSpawn), so looked up late.
            if (!membership) TryGetComponent(out membership);
            if (!targeting) TryGetComponent(out targeting);

            return new DormancyInputs(
                cellAwake,
                membership && membership.Group != null,
                AirborneSeat.IsSeatedAloft(transform),
                flight && (flight.InFlight || flight.IsAirborne || flight.OnSortie),
                targeting && IsPlayer(targeting.Target, players),
                now - lastHurtTime);
        }

        // A seated player is a child of its seat, and a target may be a child collider: walk up.
        private static bool IsPlayer(Transform target, HashSet<Transform> players)
        {
            for (Transform t = target; t != null; t = t.parent)
                if (players.Contains(t))
                    return true;

            return false;
        }
    }
}
