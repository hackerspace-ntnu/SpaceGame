using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime decoration pass.
///
/// For each enabled <see cref="DecorationRule"/> on a <see cref="CaveDecorationSettings"/>:
///   1. Walk the cave mesh's triangles, building a list of (worldPos, inwardNormal, surfaceType,
///      area, nearestRoomKind) sample sites that pass the rule's filters.
///   2. Compute a target instance count = density × eligibleArea, clamped to maxInstances.
///   3. Shuffle, then accept points greedily while maintaining minSpacing (Poisson-disk thinning
///      with a uniform grid).
///   4. Instantiate prefabs (or procedural fallbacks) under a parent transform.
///
/// All randomisation derives from <paramref name="seed"/> + the rule's <see cref="DecorationRule.seedSalt"/>
/// so two scatters with the same seed produce identical placement.
/// </summary>
public static class DecorationScatterer
{
    public class ScatterContext
    {
        public Mesh Mesh;
        public Transform Parent;
        public CaveGraph Graph;
        public List<LiquidPool> LiquidPools;   // may be null/empty
        public int Seed;
        public int CaveLayer;                  // -1 = leave layer alone
    }

    public static int Scatter(CaveDecorationSettings settings, ScatterContext ctx)
    {
        if (settings == null || !settings.enabled || settings.rules == null || settings.rules.Length == 0)
            return 0;
        if (ctx.Mesh == null || ctx.Parent == null) return 0;

        // -------------------------------------------------------------------------
        // 1) Bake the mesh into a triangle table (one entry per triangle) — done once,
        //    re-used across all rules. World-space positions and inward-facing normals.
        // -------------------------------------------------------------------------
        var triangles = BuildTriangleTable(ctx.Mesh, ctx.Parent, settings.floorCeilingThreshold, ctx.Graph);

        int placedTotal = 0;
        int cap = settings.globalInstanceCap > 0 ? settings.globalInstanceCap : int.MaxValue;
        var grid = new SpatialHashGrid(2f); // shared across rules so different rules also avoid each other a bit

        foreach (var rule in settings.rules)
        {
            if (placedTotal >= cap) break;
            if (rule == null || !rule.enabled) continue;
            int remaining = cap - placedTotal;
            placedTotal += ScatterRule(rule, triangles, ctx, grid, remaining);
        }

        return placedTotal;
    }

    // -------------------------------------------------------------------------
    // Per-rule scatter
    // -------------------------------------------------------------------------

    static int ScatterRule(DecorationRule rule, TriangleSite[] triangles, ScatterContext ctx, SpatialHashGrid grid, int globalRemaining)
    {
        var rng = new System.Random(ctx.Seed ^ unchecked(rule.seedSalt * (int)2654435761));

        // Filter triangles eligible for this rule.
        var eligible = new List<int>(triangles.Length / 4);
        float eligibleArea = 0f;
        for (int i = 0; i < triangles.Length; i++)
        {
            if (PassesRule(rule, triangles[i], ctx.LiquidPools))
            {
                eligible.Add(i);
                eligibleArea += triangles[i].Area;
            }
        }
        if (eligible.Count == 0) return 0;

        int target = Mathf.RoundToInt(eligibleArea * rule.densityPerSqM);
        if (rule.maxInstances > 0) target = Mathf.Min(target, rule.maxInstances);
        target = Mathf.Min(target, globalRemaining);
        if (target <= 0) return 0;

        // Shuffle eligible (Fisher-Yates) so we don't bias toward earlier triangles when minSpacing
        // saturates.
        for (int i = eligible.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (eligible[i], eligible[j]) = (eligible[j], eligible[i]);
        }

        var ruleRoot = new GameObject(rule.name);
        ruleRoot.transform.SetParent(ctx.Parent, worldPositionStays: false);

        int placed = 0;
        // When density-noise is active we reject a chunk of candidates → need extra attempts to
        // still hit the target count in dense regions. 6× was the old default; bump to 12× if
        // clumping is on so empty patches don't starve the placed total.
        bool useDensityNoise = rule.densityNoiseScale > 0.0001f;
        int attempts = Mathf.Min(eligible.Count, target * (useDensityNoise ? 12 : 6));

        // Per-rule deterministic offset for the density noise — different rules get different
        // clump patterns at the same seed so they don't all clump in the same places.
        float densityOffsetX = (rule.seedSalt * 13.37f) % 1000f;
        float densityOffsetZ = (rule.seedSalt * 27.91f) % 1000f;

        for (int i = 0; i < attempts && placed < target; i++)
        {
            var tri = triangles[eligible[i % eligible.Count]];
            float u = (float)rng.NextDouble();
            float v = (float)rng.NextDouble();
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
            Vector3 worldPos = tri.V0 + (tri.V1 - tri.V0) * u + (tri.V2 - tri.V0) * v;

            // Min-spacing rejection using the spatial grid.
            if (rule.minSpacing > 0f && grid.HasWithin(worldPos, rule.minSpacing)) continue;

            // Density-noise modulation: sample a low-frequency Perlin at this point's XZ. High
            // noise = accept; low noise = reject. Contrast shapes how sharp the clump boundaries
            // are — at contrast=1 anything below a threshold is fully rejected (hard patches);
            // at contrast=0 it's a soft gradient with no fully-empty zones.
            if (useDensityNoise)
            {
                float fx = worldPos.x * rule.densityNoiseScale + densityOffsetX;
                float fz = worldPos.z * rule.densityNoiseScale + densityOffsetZ;
                float n = Mathf.PerlinNoise(fx, fz);                   // [0,1]
                // Contrast remap: lerp between flat (contrast=0: prob = n) and sharp
                // (contrast=1: prob = step(0.5, n)).
                float sharpened = Mathf.Clamp01((n - 0.5f) * (1f + rule.densityNoiseContrast * 8f) + 0.5f);
                float pAccept = Mathf.Lerp(n, sharpened, rule.densityNoiseContrast);
                if (rng.NextDouble() > pAccept) continue;
            }

            // Liquid-proximity boost (acceptance probability bonus near waterline).
            if (rule.liquidProximityBoost > 0f && ctx.LiquidPools != null)
            {
                float dist = DistanceToAnyPool(worldPos, ctx.LiquidPools);
                float boost = Mathf.Clamp01(1f - dist / rule.liquidProximityBoost);
                float pAccept = Mathf.Lerp(0.4f, 1f, boost);
                if (rng.NextDouble() > pAccept) continue;
            }

            PlaceInstance(rule, ruleRoot.transform, worldPos, tri.InwardNormal, rng, ctx.CaveLayer);
            grid.Add(worldPos);
            placed++;
        }

        return placed;
    }

    static void PlaceInstance(DecorationRule rule, Transform parent, Vector3 worldPos, Vector3 inwardNormal, System.Random rng, int layer)
    {
        Vector3 pos = worldPos + inwardNormal * rule.surfaceEmbed;

        // Up direction: blend between world up and the inward normal by the alignment factor.
        Vector3 up = Vector3.Slerp(Vector3.up, inwardNormal, rule.normalAlignment).normalized;
        if (up.sqrMagnitude < 0.001f) up = Vector3.up;
        float yaw = Mathf.Lerp(rule.yawRange.x, rule.yawRange.y, (float)rng.NextDouble());

        GameObject go;
        if (rule.prefabs != null && rule.prefabs.Length > 0)
        {
            var prefab = rule.prefabs[rng.Next(rule.prefabs.Length)];
            if (prefab == null)
            {
                go = MakeFallbackPrimitive(rule);
            }
            else
            {
                go = Object.Instantiate(prefab);
            }
        }
        else
        {
            go = MakeFallbackPrimitive(rule);
        }

        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.position = pos;
        // Build a rotation aligning local up to `up`, then yaw around that up.
        Quaternion alignToNormal = Quaternion.FromToRotation(Vector3.up, up);
        go.transform.rotation = alignToNormal * Quaternion.Euler(0f, yaw, 0f);

        float s = Mathf.Lerp(rule.scaleRange.x, rule.scaleRange.y, (float)rng.NextDouble());
        go.transform.localScale = Vector3.one * s;

        if (layer >= 0) SetLayerRecursive(go, layer);

        if (rule.attachedLightRange > 0f)
        {
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(go.transform, worldPositionStays: false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = rule.fallbackEmission == Color.black ? Color.white : rule.fallbackEmission;
            light.intensity = rule.attachedLightIntensity;
            light.range = rule.attachedLightRange;
            light.shadows = LightShadows.None;
            if (layer >= 0) lightGo.layer = layer;
        }
    }

    static GameObject MakeFallbackPrimitive(DecorationRule rule)
    {
        var go = GameObject.CreatePrimitive(rule.fallbackPrimitive);
        go.name = rule.name + "_proc";
        var col = go.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Object.Destroy(col);
            else Object.DestroyImmediate(col);
        }

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { name = rule.name + "_mat" };
            m.SetColor("_BaseColor", rule.fallbackColor);
            m.color = rule.fallbackColor;
            if (rule.fallbackEmission.maxColorComponent > 0.01f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", rule.fallbackEmission);
            }
            mr.sharedMaterial = m;
        }
        return go;
    }

    // -------------------------------------------------------------------------
    // Filter checks
    // -------------------------------------------------------------------------

    static bool PassesRule(DecorationRule rule, TriangleSite tri, List<LiquidPool> pools)
    {
        // Surface type
        if (rule.surfaceType != DecorationSurfaceType.Any && tri.Surface != rule.surfaceType) return false;

        // Y range
        if (tri.Center.y < rule.minWorldY || tri.Center.y > rule.maxWorldY) return false;

        // Tilt: angle between inward normal and the surface's "ideal" up direction (floor=world up,
        // ceiling=world down, wall=anything sideways).
        float tilt = SurfaceTilt(tri);
        if (tilt > rule.maxSurfaceTilt) return false;

        // Room-kind filter
        if (rule.allowedRoomKinds != null && rule.allowedRoomKinds.Length > 0)
        {
            bool match = false;
            for (int i = 0; i < rule.allowedRoomKinds.Length; i++)
                if (rule.allowedRoomKinds[i] == tri.NearestRoomKind) { match = true; break; }
            if (!match) return false;
        }
        return true;
    }

    static float SurfaceTilt(TriangleSite tri)
    {
        switch (tri.Surface)
        {
            case DecorationSurfaceType.Floor:   return Mathf.Acos(Mathf.Clamp(tri.InwardNormal.y, -1f, 1f));
            case DecorationSurfaceType.Ceiling: return Mathf.Acos(Mathf.Clamp(-tri.InwardNormal.y, -1f, 1f));
            case DecorationSurfaceType.Wall:    return Mathf.Acos(Mathf.Clamp(new Vector2(tri.InwardNormal.x, tri.InwardNormal.z).magnitude, -1f, 1f));
            default:                            return 0f;
        }
    }

    static float DistanceToAnyPool(Vector3 p, List<LiquidPool> pools)
    {
        float best = float.MaxValue;
        for (int i = 0; i < pools.Count; i++)
        {
            float d = pools[i].DistanceTo(p);
            if (d < best) best = d;
        }
        return best;
    }

    // -------------------------------------------------------------------------
    // Triangle table
    // -------------------------------------------------------------------------

    struct TriangleSite
    {
        public Vector3 V0, V1, V2;
        public Vector3 Center;
        public Vector3 InwardNormal;
        public float Area;
        public DecorationSurfaceType Surface;
        public RoomKind NearestRoomKind;
    }

    static TriangleSite[] BuildTriangleTable(Mesh mesh, Transform parent, float floorCeilingThreshold, CaveGraph graph)
    {
        var verts = mesh.vertices;
        var tris  = mesh.triangles;
        var normals = mesh.normals;
        int triCount = tris.Length / 3;

        Vector3 caveCenter = mesh.bounds.center;
        caveCenter = parent.TransformPoint(caveCenter);

        var table = new TriangleSite[triCount];
        for (int t = 0; t < triCount; t++)
        {
            int i0 = tris[t * 3 + 0], i1 = tris[t * 3 + 1], i2 = tris[t * 3 + 2];
            Vector3 v0 = parent.TransformPoint(verts[i0]);
            Vector3 v1 = parent.TransformPoint(verts[i1]);
            Vector3 v2 = parent.TransformPoint(verts[i2]);
            Vector3 center = (v0 + v1 + v2) / 3f;

            // Cave mesh normals already face into the air volume (see MarchingCubesMesher reverse-
            // winding comment). But if normals are missing, derive from winding + center fallback.
            Vector3 normal;
            if (normals != null && normals.Length == verts.Length)
                normal = parent.TransformDirection(((normals[i0] + normals[i1] + normals[i2]) / 3f).normalized);
            else
            {
                Vector3 cross = Vector3.Cross(v1 - v0, v2 - v0);
                normal = cross.sqrMagnitude > 1e-6f ? cross.normalized : Vector3.up;
                // Flip toward caveCenter so it points inward.
                if (Vector3.Dot(normal, caveCenter - center) < 0f) normal = -normal;
            }
            normal = normal.normalized;

            float area = 0.5f * Vector3.Cross(v1 - v0, v2 - v0).magnitude;

            // Classify floor / ceiling / wall by the inward normal's Y component.
            DecorationSurfaceType st;
            if (normal.y >  floorCeilingThreshold) st = DecorationSurfaceType.Floor;
            else if (normal.y < -floorCeilingThreshold) st = DecorationSurfaceType.Ceiling;
            else st = DecorationSurfaceType.Wall;

            table[t] = new TriangleSite
            {
                V0 = v0, V1 = v1, V2 = v2,
                Center = center,
                InwardNormal = normal,
                Area = area,
                Surface = st,
                NearestRoomKind = NearestRoomKind(graph, center, parent),
            };
        }
        return table;
    }

    static RoomKind NearestRoomKind(CaveGraph graph, Vector3 worldCenter, Transform parent)
    {
        if (graph == null || graph.Rooms.Count == 0) return RoomKind.Normal;
        Vector3 local = parent.InverseTransformPoint(worldCenter);
        float best = float.MaxValue;
        RoomKind kind = RoomKind.Normal;
        for (int i = 0; i < graph.Rooms.Count; i++)
        {
            float d = (graph.Rooms[i].Center - local).sqrMagnitude;
            if (d < best) { best = d; kind = graph.Rooms[i].Kind; }
        }
        return kind;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        var t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursive(t.GetChild(i).gameObject, layer);
    }

    // -------------------------------------------------------------------------
    // Cheap uniform-grid spatial hash for Poisson-disk thinning
    // -------------------------------------------------------------------------

    class SpatialHashGrid
    {
        readonly float _cell;
        readonly Dictionary<long, List<Vector3>> _buckets = new();

        public SpatialHashGrid(float cell) { _cell = cell; }

        long Key(int x, int y, int z) => ((long)(x & 0xFFFF) << 32) | ((long)(y & 0xFFFF) << 16) | (long)(z & 0xFFFF);
        (int x, int y, int z) Cell(Vector3 p) => (Mathf.FloorToInt(p.x / _cell), Mathf.FloorToInt(p.y / _cell), Mathf.FloorToInt(p.z / _cell));

        public void Add(Vector3 p)
        {
            var c = Cell(p);
            long k = Key(c.x, c.y, c.z);
            if (!_buckets.TryGetValue(k, out var list)) { list = new List<Vector3>(); _buckets[k] = list; }
            list.Add(p);
        }

        public bool HasWithin(Vector3 p, float r)
        {
            float r2 = r * r;
            var c = Cell(p);
            int span = Mathf.Max(1, Mathf.CeilToInt(r / _cell));
            for (int dz = -span; dz <= span; dz++)
            for (int dy = -span; dy <= span; dy++)
            for (int dx = -span; dx <= span; dx++)
            {
                long k = Key(c.x + dx, c.y + dy, c.z + dz);
                if (!_buckets.TryGetValue(k, out var list)) continue;
                for (int i = 0; i < list.Count; i++)
                    if ((list[i] - p).sqrMagnitude < r2) return true;
            }
            return false;
        }
    }
}
