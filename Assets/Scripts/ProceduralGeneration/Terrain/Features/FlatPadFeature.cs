using UnityEngine;

/// <summary>
/// ================ REFERENCE STUB FEATURE — copy this as your template ================
///
/// FlatPadFeature is the simplest possible terrain feature: it raises a flat, gently noisy pad
/// inside the box footprint, sitting on top of the underlying terrain. It exists to demonstrate
/// the exact <see cref="TerrainFeature"/> contract a feature-implementing agent must fill — every
/// one of the nine real features follows this same three-member shape.
///
/// What it shows:
///   • Declaring <see cref="FeatureType"/> + <see cref="DensityKind"/>.
///   • Building a <see cref="HeightfieldDensity"/> from a height lambda (the common case).
///   • Reading the box footprint from <see cref="FeatureContext.LocalBounds"/>.
///   • Sampling the underlying ground via <see cref="FeatureContext.LocalGroundHeight"/>.
///   • Using the shared helpers: <see cref="TerrainNoiseHelper.SurfaceNoise"/>,
///     <see cref="TerrainNoiseHelper.VariedHeight"/>, <see cref="TerrainNoiseHelper.OverlapWeight"/>.
///
/// A real area feature (dunes, mesa, butte, cliff) differs only in the body of the height lambda —
/// it calls a <see cref="TerrainProfiles"/> function instead of using a constant. A linear feature
/// reads <see cref="FeatureContext.Path"/> via a <see cref="FeatureSpline"/> instead of the box.
///
/// The feature does NOT mesh, smooth, skirt-blend or save anything — the shared
/// <see cref="TerrainFeatureGenerator"/> pipeline does all of that. Keep features this small.
/// </summary>
public sealed class FlatPadFeature : TerrainFeature
{
    public override TerrainFeatureType FeatureType => TerrainFeatureType.FlatPad;

    // A flat pad has no overhang, so the cheap heightfield density is correct.
    public override TerrainDensityKind DensityKind => TerrainDensityKind.Heightfield;

    public override ITerrainDensity BuildDensity(FeatureContext context)
    {
        Bounds box = context.LocalBounds;
        TerrainFeatureTuning tuning = context.Tuning;

        // The pad's flat height, with per-feature deterministic variation applied.
        float padHeight = TerrainNoiseHelper.VariedHeight(tuning.height, tuning, context.Seed);

        // Pre-compute the footprint edge geometry so the height lambda can do the overlap falloff.
        Vector2 halfXZ = new Vector2(box.extents.x, box.extents.z);
        Vector2 centreXZ = new Vector2(box.center.x, box.center.z);

        // -------------------------------------------------------------------------
        // The height lambda: ground + pad surface. This is the ONE thing a real
        // area feature customises — swap the constant for a TerrainProfiles call.
        // -------------------------------------------------------------------------
        System.Func<float, float, float> heightFn = (x, z) =>
        {
            float groundY = context.LocalGroundHeight(x, z);

            // Signed distance INTO the footprint from its nearest edge (negative = outside).
            float dx = halfXZ.x - Mathf.Abs(x - centreXZ.x);
            float dz = halfXZ.y - Mathf.Abs(z - centreXZ.y);
            float distInside = Mathf.Min(dx, dz);

            // Overlap falloff: 0 at/outside the edge, 1 deep inside — fades the pad into terrain.
            float weight = TerrainNoiseHelper.OverlapWeight(distInside, tuning);
            if (weight <= 0f) return groundY;

            // Surface noise gives the otherwise-flat pad a little organic life.
            float noise = TerrainNoiseHelper.SurfaceNoise(new Vector3(x, 0f, z), tuning, context.Seed);

            return groundY + (padHeight + noise) * weight;
        };

        // Vertical extent the meshing band must cover: from a touch below ground to the pad top.
        float maxY = box.max.y + padHeight + tuning.noiseAmount + 2f;
        float minY = context.LocalGroundHeight(box.center.x, box.center.z) - 4f;
        float bandPadding = context.VoxelSize * 2f;

        return new HeightfieldDensity(heightFn, box, minY, maxY, bandPadding);
    }
}
