using System;
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
