using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The boulder-field signed distance function — a voxel <see cref="ITerrainDensity"/> whose surface
/// is the SmoothMin union of every scattered <see cref="BoulderInstance"/>.
///
/// SIGN CONVENTION (matches the whole terrain system): negative = solid rock, positive = air,
/// zero = surface. A point at a boulder's centre is deep inside ⇒ strongly negative; a point in
/// open air far from every boulder ⇒ strongly positive.
///
/// PER-BOULDER SHAPE — a boulder is NOT a sphere. It is built as:
///   1. a lumpy core: <c>lumpiness</c> SmoothMin-blended sub-spheres in a unit-sized boulder space;
///   2. per-axis squash / stretch (flatten + shape variety) and a yaw rotation, applied by
///      transforming the sample point into the boulder's local space before the SDF is evaluated;
///   3. domain-warp / fbm surface displacement (<see cref="NoiseDistortion"/> + the shared
///      <see cref="TerrainNoiseHelper"/>) added to the distance, so the surface is eroded,
///      faceted-but-rounded weathered rock rather than a smooth ovoid.
/// All boulders are SmoothMin-unioned so touching rocks fuse softly, and the whole field is
/// SmoothMin-unioned with a ground-fill half-space so each boulder grounds into the terrain.
///
/// PERFORMANCE — a naive union loops every boulder per Sample (O(N)). Instead the constructor
/// buckets boulders into a uniform XZ grid; <see cref="Sample"/> only tests boulders in the cells
/// within one boulder-reach of the query point, keeping it ~O(boulders in the neighbourhood).
/// </summary>
public sealed class BouldersSdf : ITerrainDensity
{
    readonly List<BoulderInstance> _boulders;
    readonly BouldersSettings _settings;
    readonly Bounds _bounds;
    readonly int _seed;
    readonly System.Func<float, float, float> _groundFn;

    // --- Uniform XZ bucket grid (spatial acceleration) -----------------------------------
    readonly float _cellSize;
    readonly int _gridCols, _gridRows;
    readonly float _gridMinX, _gridMinZ;
    readonly List<int>[] _cells;     // each cell holds indices into _boulders

    public Bounds Bounds => _bounds;
    public bool IsHeightfield => false;

    /// <param name="boulders">The scattered boulder field (from <see cref="BouldersScatter"/>).</param>
    /// <param name="settings">Resolved boulder settings (never null).</param>
    /// <param name="volumeBounds">Local-space volume the mesher walks — must enclose every
    /// boulder plus padding.</param>
    /// <param name="groundFn">Underlying ground height, for the ground-fill half-space.</param>
    /// <param name="seed">Deterministic seed.</param>
    public BouldersSdf(
        List<BoulderInstance> boulders, BouldersSettings settings,
        Bounds volumeBounds, System.Func<float, float, float> groundFn, int seed)
    {
        _boulders = boulders ?? new List<BoulderInstance>();
        _settings = settings ?? new BouldersSettings();
        _bounds = volumeBounds;
        _seed = seed;
        _groundFn = groundFn ?? ((x, z) => volumeBounds.min.y);

        // --- Build the bucket grid -------------------------------------------------------
        float maxReach = 1f;
        for (int i = 0; i < _boulders.Count; i++)
            if (_boulders[i].Reach > maxReach) maxReach = _boulders[i].Reach;

        // Cell ≈ the largest reach so any boulder spans at most the 3×3 neighbourhood of cells.
        _cellSize = Mathf.Max(1f, maxReach);
        _gridMinX = volumeBounds.min.x;
        _gridMinZ = volumeBounds.min.z;
        _gridCols = Mathf.Max(1, Mathf.CeilToInt(volumeBounds.size.x / _cellSize) + 1);
        _gridRows = Mathf.Max(1, Mathf.CeilToInt(volumeBounds.size.z / _cellSize) + 1);
        _cells = new List<int>[_gridCols * _gridRows];

        for (int i = 0; i < _boulders.Count; i++)
        {
            Vector3 c = _boulders[i].Centre;
            int gx = CellX(c.x);
            int gz = CellZ(c.z);
            int idx = gx + gz * _gridCols;
            (_cells[idx] ??= new List<int>(4)).Add(i);
        }
    }

    int CellX(float x) => Mathf.Clamp(Mathf.FloorToInt((x - _gridMinX) / _cellSize), 0, _gridCols - 1);
    int CellZ(float z) => Mathf.Clamp(Mathf.FloorToInt((z - _gridMinZ) / _cellSize), 0, _gridRows - 1);

    /// <summary>Signed density: negative inside boulder rock, positive in air, zero at the surface.</summary>
    public float Sample(Vector3 p)
    {
        // Start as open air — boulders carve solidity IN via SmoothMin (a union of solids).
        float field = 1e6f;

        // Only the 3×3 cell neighbourhood can hold a boulder whose reach covers p.
        int gx = CellX(p.x);
        int gz = CellZ(p.z);
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int cx = gx + dx, cz = gz + dz;
            if (cx < 0 || cz < 0 || cx >= _gridCols || cz >= _gridRows) continue;
            var bucket = _cells[cx + cz * _gridCols];
            if (bucket == null) continue;

            for (int k = 0; k < bucket.Count; k++)
            {
                var b = _boulders[bucket[k]];
                // Cheap reject: if p is beyond this boulder's reach it cannot lower the field.
                float ddx = p.x - b.Centre.x, ddz = p.z - b.Centre.z;
                if (ddx * ddx + ddz * ddz > b.Reach * b.Reach) continue;

                float d = BoulderSdf(p, b);
                field = SdfPrimitives.SmoothMin(field, d, _settings.blendRadius);
            }
        }

        // --- Ground fill: keep rock solid under each boulder so it grounds into the terrain.
        // Only fills BELOW ground AND only where a boulder is overhead (the field is already
        // near/under solid), so no flat apron is meshed across the empty footprint.
        if (field < _settings.blendRadius + 1f)
        {
            float groundY = _groundFn(p.x, p.z);
            float belowGround = groundY - p.y;          // >0 below ground ⇒ should be solid
            field = SdfPrimitives.SmoothMin(field, -belowGround, _settings.blendRadius);
        }

        return field;
    }

    /// <summary>
    /// Signed distance from <paramref name="p"/> to a single boulder. Negative inside the rock.
    /// Builds the lumpy core in the boulder's own squashed / rotated space, then adds domain-warp
    /// erosion so the surface reads as weathered rock.
    /// </summary>
    float BoulderSdf(Vector3 p, BoulderInstance b)
    {
        // --- Into boulder-local space: translate, un-yaw, un-squash ----------------------
        Vector3 rel = p - b.Centre;
        float cs = Mathf.Cos(-b.Yaw), sn = Mathf.Sin(-b.Yaw);
        Vector3 local = new Vector3(
            rel.x * cs - rel.z * sn,
            rel.y,
            rel.x * sn + rel.z * cs);
        // Divide out the per-axis scale so the core is evaluated as a near-unit blob. Dividing
        // distorts the metric slightly; multiplying the result back by the smallest scale keeps
        // the SDF a safe under-estimate (good enough for marching cubes).
        Vector3 scaled = new Vector3(
            local.x / b.AxisScale.x,
            local.y / b.AxisScale.y,
            local.z / b.AxisScale.z);
        float minScale = Mathf.Min(b.AxisScale.x, Mathf.Min(b.AxisScale.y, b.AxisScale.z));

        // --- Lumpy core: SmoothMin of a few offset sub-spheres ---------------------------
        float core = SdfPrimitives.Sphere(scaled, Vector3.zero, b.Radius);
        int lumps = Mathf.Clamp(_settings.lumpiness, 1, 4);
        for (int i = 1; i < lumps; i++)
        {
            // Deterministic sub-sphere offset/size from the boulder's noise phase.
            float a = b.NoisePhase.x + i * 2.3994f;            // golden-angle-ish spread
            Vector3 off = new Vector3(Mathf.Cos(a), Mathf.Sin(a * 1.7f) * 0.5f, Mathf.Sin(a))
                          * b.Radius * 0.45f;
            float subR = b.Radius * Mathf.Lerp(0.55f, 0.85f,
                TerrainNoiseHelper.Hash01(_seed, (int)(b.NoisePhase.z * 131f) + i));
            float sub = SdfPrimitives.Sphere(scaled, off, subR);
            core = SdfPrimitives.SmoothMin(core, sub, b.Radius * 0.5f);
        }
        core *= minScale;     // back into roughly true-distance metres

        // --- Surface erosion: displace the surface so it is faceted weathered rock --------
        // Routed through the SHARED TerrainNoiseHelper detail layer so the central "Surface
        // detail" dials reach boulders. The amplitude is the designer's detailStrength scaled by
        // the per-boulder 'irregularity', then CLAMPED to a safe fraction of the boulder radius so
        // even a violently jagged setting can never punch a hole clean through a small boulder.
        if (_settings.irregularity > 0f && _tuning != null && _tuning.detailStrength > 0f)
        {
            // Sample the unit field in the boulder's own (squashed) space so the erosion rides
            // along with the rock; the boulder's noise phase de-correlates neighbouring rocks.
            float warpUnit = TerrainNoiseHelper.DetailUnit(scaled + b.NoisePhase, _tuning, _seed + 5527);
            float amp = _tuning.detailStrength * _settings.irregularity;
            amp = Mathf.Min(amp, b.Radius * 0.6f);          // safety cap — never breach the core
            // Positive warp pushes the surface inward (an eroded notch), negative bulges it out.
            core += warpUnit * amp;
        }

        return core;
    }

    /// <summary>Shared feature tuning, captured so boulder erosion uses the central noise dials.</summary>
    TerrainFeatureTuning _tuning;

    /// <summary>Captures the shared <see cref="TerrainFeatureTuning"/> so the per-boulder erosion
    /// runs through <see cref="TerrainNoiseHelper.DetailedNoise"/> — the same central bumpiness /
    /// jaggedness dials every other terrain feature obeys. Called once by the feature before
    /// meshing; kept separate so the constructor signature stays small.</summary>
    public BouldersSdf WithTuning(TerrainFeatureTuning tuning)
    {
        _tuning = tuning;
        return this;
    }

    /// <summary>Folded SDF — no single surface height. Returns the bounds centre Y as a sane stub.</summary>
    public float SurfaceHeight(float localX, float localZ) => _bounds.center.y;
}
