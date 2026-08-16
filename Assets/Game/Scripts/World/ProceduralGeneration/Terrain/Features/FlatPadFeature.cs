using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// ================ REFERENCE STUB FEATURE — copy this as your template ================
    ///
    /// FlatPadFeature is the simplest possible terrain feature: it raises a flat, gently noisy pad
    /// inside the designer-drawn polygon footprint, sitting on top of the underlying terrain. It exists
    /// to demonstrate the exact <see cref="TerrainFeature"/> contract a feature-implementing agent must
    /// fill — every one of the nine real features follows this same three-member shape.
    ///
    /// What it shows:
    ///   • Declaring <see cref="FeatureType"/> + <see cref="DensityKind"/>.
    ///   • Building a <see cref="HeightfieldDensity"/> from a height lambda (the common case).
    ///   • Reading the AABB from <see cref="FeatureContext.LocalBounds"/> for the meshing band.
    ///   • Querying the polygon footprint edge via <see cref="FeatureContext.FootprintDistanceInside"/>.
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

            // -------------------------------------------------------------------------
            // The height lambda: ground + pad surface. This is the ONE thing a real
            // area feature customises — swap the constant for a TerrainProfiles call.
            // -------------------------------------------------------------------------
            System.Func<float, float, float> heightFn = (x, z) =>
            {
                float groundY = context.LocalGroundHeight(x, z);

                // Signed distance INTO the polygon footprint boundary (negative = outside).
                // Use context.FootprintDistanceInside — never recompute the edge yourself.
                float distInside = context.FootprintDistanceInside(x, z);

                // Overlap falloff: 0 at/outside the polygon edge, 1 deep inside — fades the pad into terrain.
                float weight = TerrainNoiseHelper.OverlapWeight(distInside, tuning);
                if (weight <= 0f) return groundY;

                // Surface noise gives the otherwise-flat pad a little organic life.
                float noise = TerrainNoiseHelper.SurfaceNoise(new Vector3(x, 0f, z), tuning, context.Seed);

                return groundY + (padHeight + noise) * weight;
            };

            // Coverage mask: the pad only occupies the columns inside its polygon footprint (plus the
            // overlap band). Columns outside it are just plain ground — without this mask the mesher
            // would build a flat ground-following apron all around the pad. Returning false there
            // makes those columns mesh as pure air, so only the pad itself is generated. Every real
            // area feature follows this same pattern: the coverage predicate mirrors whatever the
            // height lambda uses to decide weight > 0.
            System.Func<float, float, bool> coverageFn = (x, z) =>
                TerrainNoiseHelper.OverlapWeight(context.FootprintDistanceInside(x, z), tuning) > 0f;

            // Vertical extent the meshing band must cover: from a touch below ground to the pad top.
            float maxY = box.max.y + padHeight + tuning.noiseAmount + 2f;
            float minY = context.LocalGroundHeight(box.center.x, box.center.z) - 4f;
            float bandPadding = context.VoxelSize * 2f;

            return new HeightfieldDensity(heightFn, box, minY, maxY, bandPadding, coverageFn);
        }
    }
}
