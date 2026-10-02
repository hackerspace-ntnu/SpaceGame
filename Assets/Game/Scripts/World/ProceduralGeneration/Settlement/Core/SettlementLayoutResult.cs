// What either of a settlement's layouts (the cluster, or the planned streets) hands back to
// Settlement.Generate for the decorations, characters and the summary line that follow.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public sealed class SettlementLayoutResult
    {
        public readonly List<Transform> buildings = new();
        /// <summary>World XZ, one per entry of <see cref="buildings"/>.</summary>
        public readonly List<SettlementFootprint> buildingFootprints = new();
        /// <summary>How far the settlement reaches from its centre, outskirts included.</summary>
        public float extent;
        public List<SettlementTerrainSculptor.TerrainPatchBackup> terrainBackup = new();
        /// <summary>World footprint of every street slab, stair and wall; decorations keep off them.</summary>
        public IReadOnlyList<SettlementFootprint> paving = Array.Empty<SettlementFootprint>();
        /// <summary>Parent of the stairs and walls, whose colliders decorations must not stand in. Null for a cluster.</summary>
        public Transform pavingRoot;
        /// <summary>Appended to the summary line.</summary>
        public string summary = "";
        /// <summary>Streets that run into another on a different terrace, so they end at a wall instead of stairs.</summary>
        public int streetsEndingAtWalls;
        /// <summary>Terrace walls whose drop needs more courses than the style stacks, so they hang short of the ground.</summary>
        public int wallsTooTall;
    }
}
