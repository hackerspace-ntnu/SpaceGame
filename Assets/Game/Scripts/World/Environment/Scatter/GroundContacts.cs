using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>Where a plant painted on a terrain meets the ground: its base's centre and radius, in metres.</summary>
    public readonly struct GroundContact
    {
        public readonly float X;
        public readonly float Z;
        public readonly float Radius;

        public GroundContact(float x, float z, float radius)
        {
            X = x;
            Z = z;
            Radius = radius;
        }
    }

    /// <summary>
    /// The base of every tree and bush painted on the loaded terrains (Paint Trees), so a scatter
    /// can root itself against them: <see cref="GrassScatter"/> keeps a plant's base clear, shades
    /// the grass around it and lets it grow taller there.
    ///
    /// A plant's base is measured once per prefab from its LOD0 mesh: the part below
    /// <c>footprintHeight</c> (trunk, stems, root flare), not the crown. Terrain trees have no
    /// callback, so a terrain whose plant count changes is re-read once the count has been still
    /// for <see cref="SettleSeconds"/> — a Paint Trees stroke — and the ground whose plants
    /// differ is reported. Moving a plant without adding or removing one is not seen.
    ///
    /// Main thread only. <see cref="Under"/> copies the contacts a worker needs.
    /// </summary>
    public sealed class GroundContacts
    {
        // Contacts are bucketed by where they stand, so a chunk only looks at its neighbours.
        private const float CellSize = 16f;
        private const float SettleSeconds = 0.3f;

        // The base's radius is this percentile of its low vertices' distances from the root, so a
        // single drooping leaf or root tip does not stretch it.
        private const float FootprintPercentile = 0.8f;

        private readonly float footprintHeight;
        private readonly float reach;
        private readonly Dictionary<Terrain, Entry> terrains = new Dictionary<Terrain, Entry>();
        private readonly Dictionary<GameObject, float> footprints = new Dictionary<GameObject, float>();
        private readonly List<Terrain> gone = new List<Terrain>();
        private readonly List<GroundContact> gathered = new List<GroundContact>();
        private readonly List<float> radii = new List<float>();
        private readonly List<Vector3> vertices = new List<Vector3>();

        private sealed class Entry
        {
            public int PlantCount;
            public float CountChangedAt = -1f;
            public float LargestRadius;
            public List<GroundContact> All = new List<GroundContact>();
            public readonly Dictionary<Vector2Int, List<GroundContact>> Cells = new Dictionary<Vector2Int, List<GroundContact>>();
        }

        /// <param name="footprintHeight">A plant's mesh below this height, in metres, is its base.</param>
        /// <param name="reach">How far beyond its base a plant's contact reaches, in metres.</param>
        public GroundContacts(float footprintHeight, float reach)
        {
            this.footprintHeight = footprintHeight;
            this.reach = reach;
        }

        /// <summary>
        /// Reads the plants of terrains that loaded, forgets terrains that unloaded, and re-reads a
        /// terrain whose plants were edited, adding the ground that edit covers to <paramref name="changed"/>.
        /// </summary>
        public void Refresh(List<Terrain> active, List<Rect> changed)
        {
            gone.Clear();
            foreach (Terrain terrain in terrains.Keys)
                if (terrain == null || !active.Contains(terrain))
                    gone.Add(terrain);
            foreach (Terrain terrain in gone)
                terrains.Remove(terrain);

            float now = Time.realtimeSinceStartup;
            foreach (Terrain terrain in active)
            {
                if (terrain.terrainData == null)
                    continue;
                if (!terrains.TryGetValue(terrain, out Entry entry))
                {
                    entry = new Entry();
                    Read(terrain, entry);
                    terrains.Add(terrain, entry);
                    continue;
                }
                int count = terrain.terrainData.treeInstanceCount;
                if (count != entry.PlantCount)
                {
                    entry.PlantCount = count;
                    entry.CountChangedAt = now;
                    continue;
                }
                if (entry.CountChangedAt < 0f || now - entry.CountChangedAt < SettleSeconds)
                    continue;
                entry.CountChangedAt = -1f;
                List<GroundContact> before = entry.All;
                Read(terrain, entry);
                if (TryDifference(before, entry.All, out Rect area))
                    changed.Add(area);
            }
        }

        /// <summary>The contacts whose reach overlaps <paramref name="area"/> on <paramref name="terrain"/>.</summary>
        public GroundContact[] Under(Terrain terrain, Rect area)
        {
            if (!terrains.TryGetValue(terrain, out Entry entry) || entry.All.Count == 0)
                return System.Array.Empty<GroundContact>();
            float margin = entry.LargestRadius + reach;
            Vector2Int min = CellOf(area.xMin - margin, area.yMin - margin);
            Vector2Int max = CellOf(area.xMax + margin, area.yMax + margin);
            gathered.Clear();
            for (int z = min.y; z <= max.y; z++)
                for (int x = min.x; x <= max.x; x++)
                    if (entry.Cells.TryGetValue(new Vector2Int(x, z), out List<GroundContact> cell))
                        gathered.AddRange(cell);
            return gathered.ToArray();
        }

        private void Read(Terrain terrain, Entry entry)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;
            TreePrototype[] prototypes = data.treePrototypes;
            var bases = new float[prototypes.Length];
            for (int i = 0; i < prototypes.Length; i++)
                bases[i] = Footprint(prototypes[i].prefab);

            entry.PlantCount = data.treeInstanceCount;
            entry.All = new List<GroundContact>(entry.PlantCount);
            entry.Cells.Clear();
            entry.LargestRadius = 0f;
            foreach (TreeInstance plant in data.treeInstances)
            {
                float radius = bases[plant.prototypeIndex] * plant.widthScale;
                var contact = new GroundContact(origin.x + plant.position.x * size.x,
                                                origin.z + plant.position.z * size.z, radius);
                entry.All.Add(contact);
                Vector2Int cell = CellOf(contact.X, contact.Z);
                if (!entry.Cells.TryGetValue(cell, out List<GroundContact> list))
                    entry.Cells.Add(cell, list = new List<GroundContact>());
                list.Add(contact);
                entry.LargestRadius = Mathf.Max(entry.LargestRadius, radius);
            }
        }

        /// <summary>The radius of a prefab's base, measured from its LOD0 meshes in its root's space.</summary>
        private float Footprint(GameObject prefab)
        {
            if (prefab == null)
                return 0f;
            if (footprints.TryGetValue(prefab, out float known))
                return known;

            radii.Clear();
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            LODGroup lods = prefab.GetComponent<LODGroup>();
            Renderer[] renderers = lods != null && lods.lodCount > 0
                ? lods.GetLODs()[0].renderers
                : prefab.GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                    continue;
                Mesh mesh = filter.sharedMesh;
                if (!mesh.isReadable)
                {
                    Debug.LogWarning($"[GroundContacts] '{prefab.name}' mesh '{mesh.name}' is not Read/Write " +
                                     "enabled, so the grass cannot find its base and grows through it. Enable " +
                                     "Read/Write on the model's import settings.", prefab);
                    continue;
                }
                Matrix4x4 meshToRoot = toRoot * filter.transform.localToWorldMatrix;
                mesh.GetVertices(vertices);
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 p = meshToRoot.MultiplyPoint3x4(vertex);
                    if (p.y < footprintHeight)
                        radii.Add(new Vector2(p.x, p.z).magnitude);
                }
            }

            float radius = 0f;
            if (radii.Count > 0)
            {
                radii.Sort();
                radius = radii[Mathf.Min(radii.Count - 1, (int)(radii.Count * FootprintPercentile))];
            }
            footprints.Add(prefab, radius);
            return radius;
        }

        /// <summary>The ground covered by contacts in one list but not the other, widened by their reach.</summary>
        private bool TryDifference(List<GroundContact> before, List<GroundContact> after, out Rect area)
        {
            var kept = new HashSet<GroundContact>(after);
            var was = new HashSet<GroundContact>(before);
            float xMin = float.MaxValue, zMin = float.MaxValue, xMax = float.MinValue, zMax = float.MinValue;
            bool any = false;
            foreach (GroundContact contact in before)
                if (!kept.Contains(contact))
                    Grow(contact, ref xMin, ref zMin, ref xMax, ref zMax, ref any);
            foreach (GroundContact contact in after)
                if (!was.Contains(contact))
                    Grow(contact, ref xMin, ref zMin, ref xMax, ref zMax, ref any);
            area = any ? Rect.MinMaxRect(xMin, zMin, xMax, zMax) : default;
            return any;
        }

        private void Grow(GroundContact contact, ref float xMin, ref float zMin, ref float xMax, ref float zMax,
                          ref bool any)
        {
            float extent = contact.Radius + reach;
            xMin = Mathf.Min(xMin, contact.X - extent);
            zMin = Mathf.Min(zMin, contact.Z - extent);
            xMax = Mathf.Max(xMax, contact.X + extent);
            zMax = Mathf.Max(zMax, contact.Z + extent);
            any = true;
        }

        private static Vector2Int CellOf(float x, float z) =>
            new Vector2Int(Mathf.FloorToInt(x / CellSize), Mathf.FloorToInt(z / CellSize));
    }
}
