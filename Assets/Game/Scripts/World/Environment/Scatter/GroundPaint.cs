using System;
using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// What a scatter grows on: one paint channel of a terrain. Grass reads a splat
    /// <see cref="TerrainLayer"/>, so painting it also colours the ground; rocks read a detail
    /// layer (the terrain's Paint Details brush), which leaves the ground texture untouched. Both
    /// are matched by asset reference, never by index or name, and neither set means everywhere.
    /// </summary>
    public readonly struct GroundPaint
    {
        // Instance-count detail maps hold up to this many per cell when the terrain does not say.
        private const int DefaultInstanceCountMax = 16;
        private const float CoverageMax = 255f;

        public readonly TerrainLayer Layer;
        public readonly GameObject DetailPrototype;

        private GroundPaint(TerrainLayer layer, GameObject detailPrototype)
        {
            Layer = layer;
            DetailPrototype = detailPrototype;
        }

        /// <summary>A splat layer; null grows everywhere on every terrain.</summary>
        public static GroundPaint Splat(TerrainLayer layer) => new GroundPaint(layer, null);

        /// <summary>A detail layer, found by the prefab its detail prototype draws.</summary>
        public static GroundPaint Detail(GameObject prototype) => new GroundPaint(null, prototype);

        public bool IsDetail => DetailPrototype != null;
        public bool Everywhere => Layer == null && DetailPrototype == null;

        /// <summary>The channel's index on <paramref name="data"/>; -1 when the terrain does not carry it.</summary>
        public int IndexOn(TerrainData data)
        {
            if (Layer != null)
                return Array.IndexOf(data.terrainLayers, Layer);
            if (DetailPrototype == null)
                return -1;
            DetailPrototype[] prototypes = data.detailPrototypes;
            for (int i = 0; i < prototypes.Length; i++)
                if (prototypes[i].usePrototypeMesh && prototypes[i].prototype == DetailPrototype)
                    return i;
            return -1;
        }

        /// <summary>Cells along one side of the channel's map. Its cell i is centred at u = (i + 0.5) / resolution.</summary>
        public int Resolution(TerrainData data) => IsDetail ? data.detailResolution : data.alphamapResolution;

        /// <summary>
        /// The paint, 0..1, under <paramref name="cells"/> as [z, x]. Main thread only.
        /// <paramref name="splats"/>, when given, is the terrain's alphamap already read over the
        /// same cells: a splat channel is then copied out of it instead of read again, which is what
        /// keeps describing a terrain with many layers from reading the whole map once per layer.
        /// </summary>
        public float[,] Read(TerrainData data, int index, RectInt cells, float[,,] splats = null)
        {
            var weights = new float[cells.height, cells.width];
            if (IsDetail)
            {
                int[,] density = data.GetDetailLayer(cells.x, cells.y, cells.width, cells.height, index);
                float scale = 1f / DetailMax(data);
                for (int z = 0; z < cells.height; z++)
                    for (int x = 0; x < cells.width; x++)
                        weights[z, x] = Mathf.Min(1f, density[z, x] * scale);
                return weights;
            }
            float[,,] maps = splats ?? data.GetAlphamaps(cells.x, cells.y, cells.width, cells.height);
            for (int z = 0; z < cells.height; z++)
                for (int x = 0; x < cells.width; x++)
                    weights[z, x] = maps[z, x, index];
            return weights;
        }

        /// <summary>What a fully painted detail cell holds: 255 in coverage mode, else the instance cap.</summary>
        private static float DetailMax(TerrainData data)
        {
            if (data.detailScatterMode == DetailScatterMode.CoverageMode)
                return CoverageMax;
            return data.maxDetailScatterPerRes > 0 ? data.maxDetailScatterPerRes : DefaultInstanceCountMax;
        }
    }
}
