using System;
using System.Collections.Generic;
using SpaceGame.Gameplay;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.World
{
    /// <summary>
    /// The world's NavMesh, baked once at author time.
    ///
    /// The world has no walkable geometry that is generated at runtime — settlements are generated
    /// from an editor context menu, and both cave and terrain-feature spawners instantiate
    /// pre-baked mesh assets. So the NavMesh the runtime needs is fully known before the game
    /// starts, and rebuilding it whenever a chunk streams in was reconstructing something that
    /// never changes. That rebuild cost 5.3 s of frame time per chunk event, measured; this asset
    /// exists so the runtime cost becomes a single <c>NavMesh.AddNavMeshData</c> call.
    ///
    /// Baked by <c>WorldNavMeshBaker</c>. Holds the chunk stamps it was baked from so a stale asset
    /// is detectable rather than silently wrong — see <see cref="Stamp"/>.
    /// </summary>
    public class WorldNavMeshAsset : ScriptableObject
    {
        [Tooltip("The world this mesh is baked for. Must be assigned: the project can hold more " +
                 "than one WorldStreamingConfig, and a bake attached to the wrong one is a NavMesh " +
                 "for a different world that nothing would report as broken.")]
        public WorldStreamingConfig config;

        [Tooltip("The baked NavMesh, stored as a sub-asset of this file. Added to the runtime " +
                 "NavMesh once by WorldNavMeshProvider and never rebuilt.")]
        public NavMeshData bakedData;

        [Tooltip("Settings the bake used. Kept here rather than read from a NavMeshSurface so the " +
                 "asset records exactly what produced it.")]
        public WorldNavMeshBakeSettings settings = new();

        [Tooltip("One entry per chunk scene the bake covered, with the dependency hash it had at " +
                 "bake time. Any difference means the asset no longer matches the world.")]
        public Stamp[] stamps = Array.Empty<Stamp>();

        [Tooltip("When the bake ran, for the humans reading this in the inspector.")]
        public string bakedAtUtc = "";

        [Tooltip("Total NavMesh sources the bake collected. A sudden drop is the signal that " +
                 "geometry went missing from a chunk scene.")]
        public int sourceCount;

        // Appended after the original fields on purpose: an asset baked before they existed still
        // loads, with no links, until the next bake fills them.
        [Tooltip("What the bake looks for when it links mesh edges across gaps and drops.")]
        public WorldNavMeshLinkSettings linkSettings = new();

        [Tooltip("Jump links derived from the baked mesh's boundary edges. Never edit by hand: " +
                 "every bake replaces the list. WorldNavMeshProvider adds them with the mesh.")]
        public AutoLink[] autoLinks = Array.Empty<AutoLink>();

        /// <summary>One off-mesh link the bake derived: an agent may cross start to end by jumping.</summary>
        [Serializable]
        public struct AutoLink
        {
            public Vector3 start;
            public Vector3 end;
            public bool bidirectional;
        }

        /// <summary>
        /// Adds <see cref="autoLinks"/> to the live NavMesh and records each instance in
        /// <paramref name="into"/> so the caller can remove them. Call after the mesh itself is added.
        /// The links are baked data, not save state: every machine adds the same list.
        /// </summary>
        public void AddAutoLinks(List<NavMeshLinkInstance> into)
        {
            if (autoLinks == null || autoLinks.Length == 0) return;

            int area = NavLinkAreas.Jump;
            if (area < 0)
            {
                Debug.LogError($"[WorldNavMesh] NavMesh area '{NavLinkAreas.JumpName}' does not " +
                               $"exist; {autoLinks.Length} auto links not added.", this);
                return;
            }

            foreach (var link in autoLinks)
            {
                var added = UnityEngine.AI.NavMesh.AddLink(new NavMeshLinkData
                {
                    startPosition = link.start,
                    endPosition = link.end,
                    bidirectional = link.bidirectional,
                    width = linkSettings.linkWidth,
                    costModifier = -1f,
                    area = area,
                    agentTypeID = settings.agentTypeID,
                });
                if (added.valid) into.Add(added);
            }
        }

        /// <summary>
        /// A chunk scene as it stood when the NavMesh was baked. The hash is
        /// <c>AssetDatabase.GetAssetDependencyHash</c>, so it moves when the scene changes OR when
        /// anything the scene references changes — a re-baked feature mesh, an edited TerrainData.
        /// A plain file timestamp would miss both.
        /// </summary>
        [Serializable]
        public struct Stamp
        {
            public string sceneGuid;
            public string sceneName;
            public string dependencyHash;
        }
    }

    /// <summary>
    /// What counts as a crossable gap or drop between two pieces of baked NavMesh. The links are made
    /// in the <see cref="NavLinkAreas.Jump"/> area, which NavMeshAgentMotor crosses as a leap. All distances are
    /// between mesh <i>edges</i>, which already sit one agent radius back from the real ledge, so a
    /// sheer drop is never closer than about two radii however narrow the geometry gap is.
    /// </summary>
    [Serializable]
    public class WorldNavMeshLinkSettings
    {
        [Tooltip("Shallowest fall that is a one-way drop. Anything shallower is a gap, crossed both ways.")]
        [Min(0f)] public float minDrop = 0.8f;

        [Tooltip("Deepest fall an agent may be sent over. Deeper edges get no link.")]
        [Min(0f)] public float maxDrop = 2f;

        [Tooltip("Widest empty gap between two mesh edges at about the same height.")]
        [Min(0f)] public float maxGap = 1.5f;

        [Tooltip("Widest horizontal reach of a drop, edge to landing. A sheer ledge leaves at " +
                 "least two agent radii between the edges, so this must exceed that.")]
        [Min(0f)] public float maxDropReach = 2f;

        [Tooltip("Highest the far side may be above the near edge and still be a gap.")]
        [Min(0f)] public float maxRise = 0.5f;

        [Tooltip("Distance between candidate points along a boundary edge.")]
        [Min(0.1f)] public float edgeSampleSpacing = 1f;

        [Tooltip("Two links between the same pair of mesh pieces are never closer than this.")]
        [Min(0f)] public float linkSpacing = 6f;

        [Tooltip("Mesh pieces smaller than this (square metres) get no links in or out.")]
        [Min(0f)] public float minIslandArea = 2f;

        [Tooltip("A one-way drop only lands on a piece at least this big: a smaller one is a trap " +
                 "the agent cannot leave.")]
        [Min(0f)] public float minDropLandingArea = 20f;

        [Tooltip("Width of each link, across the direction of travel.")]
        [Min(0f)] public float linkWidth = 1f;

        [Tooltip("Hard cap on the links one bake may emit; hitting it is reported.")]
        [Min(1)] public int maxLinks = 20000;

        [Tooltip("Mesh vertices closer than this are one vertex when the mesh is analysed.")]
        [Min(0.001f)] public float weldTolerance = 0.01f;

        [Tooltip("How far outside an edge must be empty before a gap past it is real. A neighbouring " +
                 "tile's mesh starts right at the edge, and is not a gap.")]
        [Min(0.01f)] public float voidProbeDistance = 0.15f;

        [Tooltip("Step of the outward search for the far side of a gap.")]
        [Min(0.02f)] public float probeStep = 0.1f;

        [Tooltip("A link is dropped when a solid collider stands between its two ends this many metres above them: " +
                 "that is a wall or a fence the agent cannot cross, not a gap it could jump.")]
        [Min(0f)] public float wallProbeHeight = 0.5f;
    }

    /// <summary>
    /// The bake's inputs, spelled out field by field.
    ///
    /// Deliberately not a serialized <see cref="NavMeshBuildSettings"/>: the fields that matter to
    /// a designer are a handful, and the ones that do not (tile size, ledge dropping) are worth
    /// pinning to a known value rather than inheriting from whatever a NavMeshSurface in some
    /// scene happens to say.
    /// </summary>
    [Serializable]
    public class WorldNavMeshBakeSettings
    {
        [Tooltip("Which NavMesh agent type this mesh is for. 0 is Humanoid — the default agent " +
                 "every NavMeshAgent in the project uses.")]
        public int agentTypeID;

        [Tooltip("Agent radius in metres. Walkable surface is eroded by this much at every edge.")]
        public float agentRadius = 0.5f;

        [Tooltip("Agent height in metres. Sets how low a ceiling still counts as passable.")]
        public float agentHeight = 2f;

        [Tooltip("Steepest ground the agent will walk on, in degrees.")]
        public float agentSlope = 60f;

        [Tooltip("Tallest step the agent will climb, in metres.")]
        public float agentClimb = 0.8f;

        [Tooltip("Voxel size in metres. This is the dominant cost knob: halving it roughly " +
                 "quadruples bake time and memory. 0.333 (radius/1.5) is ample for 0.5 m agents " +
                 "on desert terrain; Unity's default of radius/3 costs 4x for detail no agent " +
                 "of this size can use.")]
        public float voxelSize = 0.3333333f;

        [Tooltip("Voxels per NavMesh tile. 256 at 0.333 m gives ~85 m tiles.")]
        public int tileSize = 256;

        [Tooltip("Walkable islands smaller than this area are discarded, in square metres.")]
        public float minRegionArea = 2f;

        [Tooltip("Layers the bake collects collision from. Player, UI, Hologram and the other " +
                 "non-world layers are excluded: baking a character's capsule into a permanent " +
                 "NavMesh would carve a hole that never heals.")]
        public LayerMask layerMask = ~0;

        public NavMeshBuildSettings ToBuildSettings()
        {
            var s = UnityEngine.AI.NavMesh.GetSettingsByID(agentTypeID);
            s.agentRadius = agentRadius;
            s.agentHeight = agentHeight;
            s.agentSlope = agentSlope;
            s.agentClimb = agentClimb;
            s.minRegionArea = minRegionArea;
            s.overrideVoxelSize = true;
            s.voxelSize = voxelSize;
            s.overrideTileSize = true;
            s.tileSize = tileSize;
            return s;
        }
    }
}
