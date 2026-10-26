using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Every loaded terrain a painted scatter (<see cref="GrassField"/>, <see cref="RockField"/>)
    /// grows on. Streamed world chunks bring and take their terrains with them, so the set is
    /// re-read each render tick. Each slot is one <see cref="GroundPaint"/> channel. Painting a
    /// splat layer or sculpting is reported by Unity's terrain callbacks; detail paint has no
    /// callback, so in the editor a terrain whose dirty count moved is re-read once it has been
    /// still for <see cref="DetailSettleSeconds"/> and diffed against the copy kept here. Each
    /// change is reported as the ground area it covers, so only the scatter over it is redone.
    ///
    /// Main thread only. What a worker thread needs is copied out with <see cref="Capture"/>.
    /// </summary>
    public sealed class PaintedGround : IDisposable
    {
        // Paint below this weight grows nothing (the scatters fade to zero at half a profile's
        // minimum weight, and no profile goes that low).
        private const float PaintedWeight = 0.01f;

        // A detail brush stroke dirties the terrain many times a second; it is re-read once the
        // stroke has ended, the way a splat stroke reports one synched change at its end.
        private const float DetailSettleSeconds = 0.3f;

        // A changed cell reaches scatter this far away: one for blending between cells, two for
        // the furthest a profile's edge warp can carry the outline.
        private const int ChangeMarginTexels = 3;

        private readonly IReadOnlyList<GroundPaint> paints;
        private readonly float[] growsAbove;
        private readonly bool anyDetail;
        private readonly Dictionary<Terrain, TerrainEntry> terrains = new Dictionary<Terrain, TerrainEntry>();
        private readonly List<Terrain> active = new List<Terrain>();
        private readonly List<Terrain> gone = new List<Terrain>();
        private readonly List<Rect> pending = new List<Rect>();
        private readonly List<Terrain> repainted = new List<Terrain>();
        private readonly List<PaintedGroundPatch> captured = new List<PaintedGroundPatch>();

        private sealed class TerrainEntry
        {
            public Rect Area;
            public int[] Indices;        // per slot; -1 = the terrain does not carry it
            public int[] Resolutions;    // per slot, cells along one side of its map
            public bool[][] Painted;     // per slot, per cell [z * res + x]; null = absent or everywhere
            public bool[] AnyPainted;    // per slot
            public byte[][] DetailCopy;  // per detail slot, the weights last read, to diff a stroke against
            public int DirtyCount;
            public float DetailChangedAt = -1f;
        }

        /// <param name="paints">Each slot's paint channel; <see cref="GroundPaint.Everywhere"/> grows it everywhere.</param>
        /// <param name="growsAbove">Optional, per slot: the paint weight below which that scatter
        /// grows nothing. A faint brush wash under it then costs no scattering at all — without
        /// it, a 1–30 % wash over hundreds of chunks starves the chunks in front of the camera.
        /// Null treats any paint above 1 % as painted.</param>
        public PaintedGround(IReadOnlyList<GroundPaint> paints, IReadOnlyList<float> growsAbove = null)
        {
            this.paints = paints;
            this.growsAbove = new float[paints.Count];
            for (int slot = 0; slot < paints.Count; slot++)
                this.growsAbove[slot] = growsAbove == null ? PaintedWeight : Mathf.Max(PaintedWeight, growsAbove[slot]);
            foreach (GroundPaint paint in paints)
                anyDetail |= paint.IsDetail;
            TerrainCallbacks.textureChanged += OnTextureChanged;
            TerrainCallbacks.heightmapChanged += OnHeightmapChanged;
        }

        public void Dispose()
        {
            TerrainCallbacks.textureChanged -= OnTextureChanged;
            TerrainCallbacks.heightmapChanged -= OnHeightmapChanged;
        }

        /// <summary>
        /// Picks up terrains that loaded or unloaded and detail strokes that ended, then moves
        /// every area changed since the last call into <paramref name="into"/>.
        /// </summary>
        public void TakeChanges(List<Rect> into)
        {
            Terrain.GetActiveTerrains(active);
            gone.Clear();
            foreach (KeyValuePair<Terrain, TerrainEntry> pair in terrains)
                if (pair.Key == null || !active.Contains(pair.Key))
                    gone.Add(pair.Key);
            foreach (Terrain terrain in gone)
            {
                pending.Add(terrains[terrain].Area);
                terrains.Remove(terrain);
            }
            foreach (Terrain terrain in active)
            {
                if (terrains.ContainsKey(terrain) || terrain.terrainData == null)
                    continue;
                var entry = new TerrainEntry();
                Describe(terrain, entry);
                terrains.Add(terrain, entry);
                pending.Add(entry.Area);
                repainted.Add(terrain);
            }
            if (anyDetail)
                WatchDetailPaint();

            into.AddRange(pending);
            pending.Clear();
        }

        /// <summary>Moves every terrain that loaded or was repainted since the last call into <paramref name="into"/>.</summary>
        public void TakeRepainted(List<Terrain> into)
        {
            into.AddRange(repainted);
            repainted.Clear();
        }

        public bool Contains(Terrain terrain) => terrains.ContainsKey(terrain);

        /// <summary>Each slot's index on the terrain (its layer or detail prototype); -1 where it does not carry it.</summary>
        public IReadOnlyList<int> PaintIndices(Terrain terrain) => terrains[terrain].Indices;

        /// <summary>The loaded terrains, for checks that need the Terrain itself.</summary>
        public IEnumerable<Terrain> Terrains => terrains.Keys;

        /// <summary>
        /// True when any loaded terrain can grow the slot. Most slots are painted nowhere near
        /// the player, and this lets them skip streaming altogether.
        /// </summary>
        public bool CarriesAnywhere(int slot)
        {
            foreach (TerrainEntry entry in terrains.Values)
                if (entry.AnyPainted[slot])
                    return true;
            return false;
        }

        /// <summary>
        /// True when the slot is painted somewhere under <paramref name="area"/> (or within the
        /// reach of a warped edge). Chunks with no paint are settled without being scattered,
        /// which is what keeps a long draw distance over a mostly bare terrain cheap.
        /// </summary>
        public bool Carries(int slot, Rect area)
        {
            foreach (TerrainEntry entry in terrains.Values)
                if (entry.Area.Overlaps(area) && Grows(entry, slot) && PaintedUnder(entry, slot, area))
                    return true;
            return false;
        }

        /// <summary>The ground under <paramref name="area"/> for one slot, safe to hand to a worker thread.</summary>
        public PaintedGroundPatch[] Capture(int slot, Rect area, float edgeWarp)
        {
            captured.Clear();
            foreach (KeyValuePair<Terrain, TerrainEntry> pair in terrains)
                if (pair.Value.Area.Overlaps(area) && Grows(pair.Value, slot))
                    captured.Add(new PaintedGroundPatch(pair.Key, paints[slot], pair.Value.Indices[slot], area, edgeWarp));
            return captured.ToArray();
        }

        private static bool PaintedUnder(TerrainEntry entry, int slot, Rect area)
        {
            bool[] painted = entry.Painted[slot];
            if (painted == null)
                return true;
            int res = entry.Resolutions[slot];
            // Paint cell i's centre is at u = (i + 0.5) / res.
            int xMin = Mathf.Max(0, Mathf.FloorToInt((area.xMin - entry.Area.xMin) / entry.Area.width * res) - ChangeMarginTexels);
            int zMin = Mathf.Max(0, Mathf.FloorToInt((area.yMin - entry.Area.yMin) / entry.Area.height * res) - ChangeMarginTexels);
            int xMax = Mathf.Min(res - 1, Mathf.CeilToInt((area.xMax - entry.Area.xMin) / entry.Area.width * res) + ChangeMarginTexels);
            int zMax = Mathf.Min(res - 1, Mathf.CeilToInt((area.yMax - entry.Area.yMin) / entry.Area.height * res) + ChangeMarginTexels);
            for (int z = zMin; z <= zMax; z++)
                for (int x = xMin; x <= xMax; x++)
                    if (painted[z * res + x])
                        return true;
            return false;
        }

        private bool Grows(TerrainEntry entry, int slot) =>
            paints[slot].Everywhere || entry.Indices[slot] >= 0;

        private void Describe(Terrain terrain, TerrainEntry entry)
        {
            Vector3 position = terrain.transform.position;
            TerrainData data = terrain.terrainData;
            Vector3 size = data.size;
            entry.Area = new Rect(position.x, position.z, size.x, size.z);
            int slots = paints.Count;
            entry.Indices = new int[slots];
            entry.Resolutions = new int[slots];
            entry.Painted = new bool[slots][];
            entry.AnyPainted = new bool[slots];
            entry.DetailCopy = new byte[slots][];
            entry.DirtyCount = DirtyCount(data);
            // The whole alphamap, read once for every splat slot (once per slot cost 170 ms on a
            // 13-layer, 512² terrain).
            float[,,] splats = null;
            for (int slot = 0; slot < slots; slot++)
            {
                GroundPaint paint = paints[slot];
                int index = paint.IndexOn(data);
                entry.Indices[slot] = index;
                entry.AnyPainted[slot] = paint.Everywhere;
                if (index < 0)
                    continue;
                int res = paint.Resolution(data);
                entry.Resolutions[slot] = res;
                if (!paint.IsDetail)
                    splats ??= data.GetAlphamaps(0, 0, res, res);
                float[,] weights = paint.Read(data, index, new RectInt(0, 0, res, res), paint.IsDetail ? null : splats);
                var painted = new bool[res * res];
                byte[] copy = paint.IsDetail ? new byte[res * res] : null;
                for (int z = 0; z < res; z++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        painted[z * res + x] = weights[z, x] > growsAbove[slot];
                        entry.AnyPainted[slot] |= painted[z * res + x];
                        if (copy != null)
                            copy[z * res + x] = (byte)Mathf.RoundToInt(weights[z, x] * byte.MaxValue);
                    }
                }
                entry.Painted[slot] = painted;
                entry.DetailCopy[slot] = copy;
            }
        }

        /// <summary>
        /// Detail paint has no terrain callback. A terrain whose dirty count moved is re-read once
        /// it has settled, and the cells that differ from the kept copy become a changed area.
        /// </summary>
        private void WatchDetailPaint()
        {
            float now = Time.realtimeSinceStartup;
            foreach (KeyValuePair<Terrain, TerrainEntry> pair in terrains)
            {
                TerrainEntry entry = pair.Value;
                int dirty = DirtyCount(pair.Key.terrainData);
                if (dirty != entry.DirtyCount)
                {
                    entry.DirtyCount = dirty;
                    entry.DetailChangedAt = now;
                    continue;
                }
                if (entry.DetailChangedAt < 0f || now - entry.DetailChangedAt < DetailSettleSeconds)
                    continue;
                entry.DetailChangedAt = -1f;
                byte[][] before = entry.DetailCopy;
                int[] indicesBefore = entry.Indices;
                Describe(pair.Key, entry);
                if (TryDetailChange(pair.Key, entry, before, indicesBefore, out Rect area))
                    pending.Add(area);
            }
        }

        /// <summary>The ground whose detail paint differs from <paramref name="before"/>, over every detail slot.</summary>
        private bool TryDetailChange(Terrain terrain, TerrainEntry entry, byte[][] before, int[] indicesBefore, out Rect area)
        {
            area = entry.Area;
            int xMin = int.MaxValue, zMin = int.MaxValue, xMax = -1, zMax = -1;
            int resolution = 1;
            for (int slot = 0; slot < paints.Count; slot++)
            {
                if (!paints[slot].IsDetail)
                    continue;
                byte[] now = entry.DetailCopy[slot];
                byte[] was = before[slot];
                if (now == null && was == null)
                    continue;
                // A prototype added, removed or reordered, or the map resized: all of it changed.
                if (now == null || was == null || was.Length != now.Length || indicesBefore[slot] != entry.Indices[slot])
                    return true;
                resolution = entry.Resolutions[slot];
                for (int i = 0; i < now.Length; i++)
                {
                    if (now[i] == was[i])
                        continue;
                    int x = i % resolution;
                    int z = i / resolution;
                    xMin = Mathf.Min(xMin, x);
                    xMax = Mathf.Max(xMax, x);
                    zMin = Mathf.Min(zMin, z);
                    zMax = Mathf.Max(zMax, z);
                }
            }
            if (xMax < 0)
                return false;
            area = CellsToWorld(terrain, new RectInt(xMin, zMin, xMax - xMin + 1, zMax - zMin + 1), resolution);
            return true;
        }

        // Brush strokes report unsynched changes as they paint and one synched change when the
        // stroke ends; the CPU copy a patch reads is only current after that one. Adding or
        // removing a terrain layer comes through here too, so the indices are re-read.
        private void OnTextureChanged(Terrain terrain, string textureName, RectInt texels, bool synched)
        {
            if (!synched || textureName != TerrainData.AlphamapTextureName)
                return;
            if (!terrains.TryGetValue(terrain, out TerrainEntry entry))
                return;
            Describe(terrain, entry);
            pending.Add(CellsToWorld(terrain, texels, terrain.terrainData.alphamapResolution));
            if (!repainted.Contains(terrain))
                repainted.Add(terrain);
        }

        private void OnHeightmapChanged(Terrain terrain, RectInt texels, bool synched)
        {
            if (synched && terrains.ContainsKey(terrain))
                pending.Add(SamplesToWorld(terrain, texels, terrain.terrainData.heightmapResolution));
        }

        /// <summary>The world rect paint cells cover (cell i spans u = i / res .. (i + 1) / res), widened by the change margin.</summary>
        private static Rect CellsToWorld(Terrain terrain, RectInt cells, int resolution) =>
            ToWorld(terrain, cells, Mathf.Max(1, resolution));

        /// <summary>The world rect heightmap samples span (sample i is at u = i / (res - 1)), widened by the change margin.</summary>
        private static Rect SamplesToWorld(Terrain terrain, RectInt samples, int resolution) =>
            ToWorld(terrain, samples, Mathf.Max(1, resolution - 1));

        private static Rect ToWorld(Terrain terrain, RectInt texels, int divisions)
        {
            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            float texelX = size.x / divisions;
            float texelZ = size.z / divisions;
            return new Rect(origin.x + (texels.xMin - ChangeMarginTexels) * texelX,
                            origin.z + (texels.yMin - ChangeMarginTexels) * texelZ,
                            (texels.width + 2 * ChangeMarginTexels) * texelX,
                            (texels.height + 2 * ChangeMarginTexels) * texelZ);
        }

        private static int DirtyCount(TerrainData data)
        {
#if UNITY_EDITOR
            return UnityEditor.EditorUtility.GetDirtyCount(data);
#else
            return 0;
#endif
        }
    }
}
