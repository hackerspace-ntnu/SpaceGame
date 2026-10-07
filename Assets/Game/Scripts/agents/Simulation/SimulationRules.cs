// Simulation distance as pure maths: which cells around the players are awake, and whether one agent
// in one cell sleeps. No scene, no components — SimulationRange applies it, the tests run it bare.
//
// Cells are the ChunkGrid idea at a finer size (125 m against 500 m streaming chunks), but unbounded
// and UNCLAMPED: ChunkGrid.ToCoord clamps to the edge chunk, which would put the minigame arena
// 16.5 km east into the world's corner cell. Distances are horizontal, like NpcWorldSim's own.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    public readonly struct SimulationCells
    {
        public readonly Vector3 Origin;
        public readonly float CellSize;
        public readonly float WakeRadius;
        public readonly float SleepRadius;

        public SimulationCells(Vector3 origin, float cellSize, float wakeRadius, float sleepRadius)
        {
            Origin = origin;
            CellSize = cellSize;
            WakeRadius = wakeRadius;
            SleepRadius = sleepRadius;
        }
    }

    /// <summary>What one subject's sleep depends on, read by <see cref="DistanceDormant.Read"/>.</summary>
    public readonly struct DormancyInputs
    {
        public readonly bool CellAwake;
        public readonly bool InGroup;
        public readonly bool SeatedAloft;
        public readonly bool Flying;
        public readonly bool HuntsPlayer;
        public readonly float SecondsSinceHurt;

        public DormancyInputs(bool cellAwake, bool inGroup, bool seatedAloft, bool flying, bool huntsPlayer,
            float secondsSinceHurt)
        {
            CellAwake = cellAwake;
            InGroup = inGroup;
            SeatedAloft = seatedAloft;
            Flying = flying;
            HuntsPlayer = huntsPlayer;
            SecondsSinceHurt = secondsSinceHurt;
        }
    }

    public static class SimulationRules
    {
        public static Vector2Int CellOf(Vector3 position, Vector3 origin, float cellSize) =>
            new(Mathf.FloorToInt((position.x - origin.x) / cellSize),
                Mathf.FloorToInt((position.z - origin.z) / cellSize));

        /// <summary>Horizontal distance from <paramref name="point"/> to the nearest point of the cell; 0 inside it.</summary>
        public static float DistanceToCell(Vector2Int cell, Vector3 point, Vector3 origin, float cellSize)
        {
            float minX = origin.x + cell.x * cellSize;
            float minZ = origin.z + cell.y * cellSize;
            float dx = Mathf.Max(minX - point.x, 0f, point.x - (minX + cellSize));
            float dz = Mathf.Max(minZ - point.z, 0f, point.z - (minZ + cellSize));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Moves <paramref name="awake"/> on one tick: a cell whose nearest point is beyond SleepRadius of
        /// every source leaves, a cell within WakeRadius of any source joins. The gap between the two is
        /// what stops a cell flickering as somebody walks along its edge. <paramref name="scratch"/> is
        /// the caller's, so nothing is allocated.
        /// </summary>
        public static void Advance(HashSet<Vector2Int> awake, IReadOnlyList<Vector3> sources, in SimulationCells cells,
            List<Vector2Int> scratch)
        {
            scratch.Clear();
            foreach (Vector2Int cell in awake)
                if (!AnyWithin(cell, sources, cells, cells.SleepRadius))
                    scratch.Add(cell);

            for (int i = 0; i < scratch.Count; i++)
                awake.Remove(scratch[i]);

            int reach = Mathf.CeilToInt(cells.WakeRadius / cells.CellSize);
            for (int s = 0; s < sources.Count; s++)
            {
                Vector2Int centre = CellOf(sources[s], cells.Origin, cells.CellSize);
                for (int x = -reach; x <= reach; x++)
                for (int y = -reach; y <= reach; y++)
                {
                    var cell = new Vector2Int(centre.x + x, centre.y + y);
                    if (DistanceToCell(cell, sources[s], cells.Origin, cells.CellSize) <= cells.WakeRadius)
                        awake.Add(cell);
                }
            }
        }

        public static bool ShouldSleep(in DormancyInputs inputs, float woundedWakeSeconds) =>
            !inputs.CellAwake && !inputs.InGroup && !inputs.SeatedAloft && !inputs.Flying && !inputs.HuntsPlayer &&
            inputs.SecondsSinceHurt >= woundedWakeSeconds;

        private static bool AnyWithin(Vector2Int cell, IReadOnlyList<Vector3> sources, in SimulationCells cells, float radius)
        {
            for (int i = 0; i < sources.Count; i++)
                if (DistanceToCell(cell, sources[i], cells.Origin, cells.CellSize) <= radius)
                    return true;

            return false;
        }
    }
}
