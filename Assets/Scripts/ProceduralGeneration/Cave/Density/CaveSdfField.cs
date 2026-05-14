using UnityEngine;

/// <summary>
/// Realises a <see cref="CaveGraph"/> as a continuous signed distance field. Every room becomes
/// a sphere, every corridor a capsule, all blended together with smooth-min. Then a 3D noise warps
/// the cave walls for organic shape, and per-primitive *tilted, roughened* floors are carved in so
/// every floor patch has its own gentle slope and surface bumps — no flat plane anywhere, but
/// walkable everywhere.
///
/// Per-primitive slopes are derived deterministically from the primitive index + seed so two
/// generations with the same seed are bit-identical.
///
/// Performance: <see cref="Sample"/> is O(rooms + corridors) per voxel. With ~15 rooms and ~30
/// corridors over a 96^3 voxel grid that is ~40M float ops — well under a second single-threaded.
/// </summary>
public class CaveSdfField : ICaveDensityField
{
    readonly CaveGraph _graph;
    readonly CaveGenerationSettings _settings;
    readonly Bounds _bounds;
    readonly Vector2[] _roomSlopes;
    readonly Vector2[] _corridorSlopes;

    public Bounds Bounds => _bounds;

    public CaveSdfField(CaveGraph graph, CaveGenerationSettings settings)
    {
        _graph = graph;
        _settings = settings;

        // Bounds = whatever the graph occupies, plus padding for noise displacement and walls.
        float pad = Mathf.Max(settings.boundsPadding, settings.noiseAmplitude + 1f);
        _bounds = graph.ComputeBounds(pad);

        // Clamp to footprint half-extents so we don't bake far outside what the user asked for.
        Vector3 min = Vector3.Max(_bounds.min, -settings.halfExtents);
        Vector3 max = Vector3.Min(_bounds.max,  settings.halfExtents);
        _bounds = new Bounds((min + max) * 0.5f, max - min);

        // Pre-compute a slope direction for each primitive (rise/run vector in XZ). Magnitude
        // capped to floorSlopeAmount. Cheap to do once vs every voxel sample.
        var rng = new System.Random(settings.seed * 31 + 7);
        _roomSlopes = new Vector2[graph.Rooms.Count];
        for (int i = 0; i < _roomSlopes.Length; i++) _roomSlopes[i] = RandomSlope(rng, settings.floorSlopeAmount);
        _corridorSlopes = new Vector2[graph.Corridors.Count];
        for (int i = 0; i < _corridorSlopes.Length; i++) _corridorSlopes[i] = RandomSlope(rng, settings.floorSlopeAmount);
    }

    static Vector2 RandomSlope(System.Random rng, float amount)
    {
        float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
        float mag = amount * (0.4f + (float)rng.NextDouble() * 0.6f); // 40–100% of allowed slope
        return new Vector2(Mathf.Cos(ang) * mag, Mathf.Sin(ang) * mag);
    }

    public float Sample(Vector3 p)
    {
        float d = 1e6f;
        float k = _settings.smoothUnionRadius;

        // Per-sample floor roughness — small-scale 2D noise offset added to the floor plane Y.
        // 2D (XZ) noise so floor bumps are coherent across the height of the room rather than
        // varying with Y (which would look fizzy).
        float roughness = 0f;
        if (_settings.flattenFloors && _settings.floorRoughness > 0f)
        {
            float fx = (p.x + _settings.seed * 0.7531f) * _settings.floorRoughnessFrequency;
            float fz = (p.z + _settings.seed * 2.1313f) * _settings.floorRoughnessFrequency;
            // Two-octave fbm in 2D for variety.
            float n = Mathf.PerlinNoise(fx, fz) * 2f - 1f;
            float n2 = (Mathf.PerlinNoise(fx * 2.17f + 13.7f, fz * 2.17f + 7.3f) * 2f - 1f) * 0.5f;
            roughness = (n + n2) / 1.5f * _settings.floorRoughness;
        }

        // Rooms (spheres)
        for (int i = 0; i < _graph.Rooms.Count; i++)
        {
            var r = _graph.Rooms[i];
            float s = SdfPrimitives.Sphere(p, r.Center, r.Radius);
            if (_settings.flattenFloors)
            {
                s = SdfPrimitives.ApplyFloorFlatten(
                    s, p, r.Center,
                    _settings.floorFlattenDepth,
                    _roomSlopes[i],
                    roughness,
                    _settings.floorFlattenStrength);
            }
            d = SdfPrimitives.SmoothMin(d, s, k);
        }

        // Corridors (capsules)
        for (int i = 0; i < _graph.Corridors.Count; i++)
        {
            var c = _graph.Corridors[i];
            Vector3 a = _graph.Rooms[c.FromRoomId].Center;
            Vector3 b = _graph.Rooms[c.ToRoomId].Center;
            float s = SdfPrimitives.Capsule(p, a, b, c.Radius);

            if (_settings.flattenFloors)
            {
                // Anchor the floor reference on the line a→b parametrised by horizontal position
                // of p. That way the corridor's floor plane *follows the corridor's slope* — a
                // corridor going from y=0 to y=6 over 20 m of horizontal travel has its floor
                // smoothly rising from 0 to 6 along its length, rather than cutting a flat step.
                Vector3 floorAnchor = ClosestPointOnSegmentXZ(p, a, b);
                // Slight extra dip — corridor floors sit a bit lower than rooms so transitions feel
                // like dropping into a room rather than stepping out of one.
                Vector3 anchorWithLift = new Vector3(floorAnchor.x, floorAnchor.y, floorAnchor.z);

                // Reduce flatten strength on steeper corridors so the slope through marching cubes
                // stays smooth instead of stair-stepping.
                float slopeAlong = Mathf.Abs(b.y - a.y) / Mathf.Max(0.001f, new Vector2(b.x - a.x, b.z - a.z).magnitude);
                float strength = _settings.floorFlattenStrength * Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(slopeAlong / _settings.maxCorridorSlope));

                s = SdfPrimitives.ApplyFloorFlatten(
                    s, p, anchorWithLift,
                    _settings.floorFlattenDepth * 0.6f,
                    _corridorSlopes[i],
                    roughness * 0.7f,
                    strength);
            }
            d = SdfPrimitives.SmoothMin(d, s, k);
        }

        // Surface noise — apply only near the iso-band so deep solid areas can't pop pockets and
        // deep air areas can't have stalactite shards floating in mid-room.
        if (_settings.noiseAmplitude > 0f)
        {
            float falloffRadius = _settings.noiseAmplitude * 2f;
            float falloff = 1f - Mathf.Clamp01(Mathf.Abs(d) / Mathf.Max(0.01f, falloffRadius));
            if (falloff > 0f)
            {
                float n = NoiseDistortion.Fbm(p, _settings.noiseFrequency, _settings.seed);
                d += n * _settings.noiseAmplitude * falloff;
            }
        }

        // Hard floor clamp: nothing below floorClampY ever becomes air.
        if (p.y < _settings.floorClampY)
        {
            d = Mathf.Max(d, _settings.floorClampY - p.y);
        }

        return d;
    }

    /// <summary>
    /// Project p onto the segment a→b parametrised by horizontal (XZ) distance only. The returned
    /// point has the same X,Z as the closest segment point and the *interpolated* Y. This is what
    /// makes corridor floors track the corridor's slope smoothly.
    /// </summary>
    static Vector3 ClosestPointOnSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector2 a2 = new Vector2(a.x, a.z);
        Vector2 b2 = new Vector2(b.x, b.z);
        Vector2 p2 = new Vector2(p.x, p.z);
        Vector2 ab = b2 - a2;
        float len2 = ab.sqrMagnitude;
        float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p2 - a2, ab) / len2) : 0f;
        return new Vector3(
            a.x + (b.x - a.x) * t,
            a.y + (b.y - a.y) * t,
            a.z + (b.z - a.z) * t);
    }
}
