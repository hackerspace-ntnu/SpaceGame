// Simulation distance: NPCs and animals no player is near stop doing anything, so tribes, Clanker
// towns and wildlife do not fight where nobody can see (SimulationDistance.md).
//
// Lives beside NpcWorldSim and borrows its distances — wake at SpawnRadius, sleep beyond
// DespawnRadius — so a chunk-resident NPC and a caravan member fall asleep and wake at the same line.
// Server-or-offline, like NpcWorldSim; the clients run no agent modules anyway. Nothing is saved:
// every sleeper is re-derived on the first tick after a load.
using System;
using System.Collections.Generic;
using SpaceGame.Core;
using SpaceGame.World;
using UnityEngine;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(NpcWorldSim))]
    public class SimulationRange : MonoBehaviour
    {
        [Tooltip("Side of one simulation cell, metres. Four to a 500 m streaming chunk.")]
        [SerializeField, Min(10f)] private float cellSize = 125f;

        [Tooltip("Seconds between decisions.")]
        [SerializeField, Min(0.05f)] private float tickInterval = 0.5f;

        [Tooltip("A sleeper that is hurt stays awake this long after the last hit, so a long-range shot " +
                 "is answered instead of landing on a statue.")]
        [SerializeField, Min(0f)] private float woundedWakeSeconds = 30f;

        /// <summary>
        /// The door a player inside an interior came in by, or null outside one. Public only as a test
        /// seam: the tests live in Assembly-CSharp-Editor, which has no InternalsVisibleTo into
        /// Assembly-CSharp. Nothing else assigns it.
        /// </summary>
        public Func<Transform, Vector3?> InteriorReturnPosition = ReturnPositionOfVisit;

        private readonly HashSet<Vector2Int> awake = new();
        private readonly List<Vector2Int> scratch = new();
        private readonly List<Transform> players = new();
        private readonly HashSet<Transform> playerSet = new();
        private readonly List<Vector3> sources = new();

        private NpcWorldSim sim;
        private Vector3 origin;
        private float nextTick;

        private void Start()
        {
            WorldStreamer streamer = FindFirstObjectByType<WorldStreamer>();
            origin = streamer != null && streamer.Config != null ? streamer.Config.worldOrigin : Vector3.zero;
        }

        private void Update()
        {
            if (!Network.Decides || Time.time < nextTick)
                return;

            nextTick = Time.time + tickInterval;
            SessionPlayers.Collect(players);
            Tick(players, Time.time);
        }

        /// <summary>One decision for every subject. Called by Update; public for the tests.</summary>
        public void Tick(IReadOnlyList<Transform> playerBodies, float now)
        {
            if (!sim) sim = GetComponent<NpcWorldSim>();

            playerSet.Clear();
            sources.Clear();
            for (int i = 0; i < playerBodies.Count; i++)
            {
                if (playerBodies[i] == null) continue;
                playerSet.Add(playerBodies[i]);
                AddWakeSources(playerBodies[i]);
            }

            var cells = new SimulationCells(origin, cellSize, sim.SpawnRadius, sim.DespawnRadius);
            SimulationRules.Advance(awake, sources, cells, scratch);

            IReadOnlyList<DistanceDormant> subjects = DistanceDormant.All;
            for (int i = 0; i < subjects.Count; i++)
            {
                DistanceDormant subject = subjects[i];

                // Left as it is while its controller is off. NavMeshAgentMotor's suspend is one latch
                // shared with the ragdoll, the lasso and the seating, and counts no holders: waking a dead
                // or knocked-down sleeper would resume its NavMeshAgent under the ragdoll. A passenger is
                // decided like anyone else; the controller keeps its motor out of it until it is put down.
                if (!subject.Agent.enabled)
                    continue;

                bool cellAwake = awake.Contains(SimulationRules.CellOf(subject.transform.position, origin, cellSize));
                subject.Agent.Dormant = SimulationRules.ShouldSleep(subject.Read(cellAwake, playerSet, now), woundedWakeSeconds);
            }
        }

        // Interiors load at world origin: a player inside one counts at the door they came in by too, so
        // the settlement whose house they are visiting stays awake for the visit. Where they stand still
        // counts, or a mount ridden in with them would sleep under its rider.
        private void AddWakeSources(Transform player)
        {
            sources.Add(player.position);
            Vector3? door = InteriorReturnPosition(player);
            if (door.HasValue) sources.Add(door.Value);
        }

        private static Vector3? ReturnPositionOfVisit(Transform player)
        {
            InteriorManager interiors = InteriorManager.Instance;
            return interiors != null && interiors.TryGetVisit(player.gameObject, out InteriorManager.InteriorVisit visit)
                ? visit.ReturnPosition
                : null;
        }
    }
}
