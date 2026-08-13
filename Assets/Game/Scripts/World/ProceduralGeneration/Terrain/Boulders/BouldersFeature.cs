using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Terrain feature that scatters a field of natural, eroded rock BOULDERS across its footprint
    /// polygon. ONE feature with many tunable knobs (<see cref="BouldersSettings"/>) — scatter density,
    /// clustering, size distribution and skew, per-boulder erosion / lumpiness / flattening, and how
    /// deep boulders sink into the ground are all independently customisable.
    ///
    /// It is an AREA feature: it uses the designer-drawn <see cref="FeatureContext.Footprint"/>
    /// polygon as the scatter region and is deliberately NOT in <c>TerrainFeatureSpawner.UsesPath</c>.
    ///
    /// Density model — VOXEL SDF. A boulder is a rounded 3D rock that bulges and rests on the ground,
    /// so it genuinely needs a folded surface a heightfield cannot represent. The feature builds a
    /// <see cref="BouldersSdf"/>: the SmoothMin union of every scattered <see cref="BoulderInstance"/>,
    /// each an irregular domain-warp-eroded blob, with a uniform XZ bucket grid so each
    /// <see cref="ITerrainDensity.Sample"/> stays cheap regardless of the boulder count.
    ///
    /// Pipeline split:
    /// <list type="bullet">
    ///   <item><see cref="BouldersScatter"/> — noise-driven non-uniform placement off the seed.</item>
    ///   <item><see cref="BouldersSdf"/> — per-boulder shape + the spatial-acceleration density.</item>
    ///   <item><see cref="BouldersSettings"/> — the designer knobs.</item>
    /// </list>
    ///
    /// Determinism: every boulder's position, size, shape, rotation and noise phase is derived from
    /// <see cref="FeatureContext.Seed"/> via <see cref="TerrainNoiseHelper.Hash01"/> and seeded noise —
    /// no Time, no UnityEngine.Random. Same seed ⇒ identical field; different seed ⇒ a wholly
    /// different boulder field.
    /// </summary>
    public sealed class BouldersFeature : TerrainFeature
    {
        /// <summary>Optional per-instance settings. When null the class-default values are used.
        /// The spawner injects a designer-tuned instance via <see cref="ApplySettings"/>.</summary>
        public BouldersSettings Settings = new BouldersSettings();

        /// <inheritdoc/>
        public override TerrainFeatureType FeatureType => TerrainFeatureType.Boulders;

        /// <summary>A boulder is a rounded rock that bulges over its contact point — a genuine
        /// overhang — so the voxel SDF path is required.</summary>
        public override TerrainDensityKind DensityKind => TerrainDensityKind.Voxel;

        /// <inheritdoc/>
        public override object CreateDefaultSettings() => new BouldersSettings();

        /// <inheritdoc/>
        public override void ApplySettings(object settings) => Settings = settings as BouldersSettings;

        /// <inheritdoc/>
        public override ITerrainDensity BuildDensity(FeatureContext context)
        {
            BouldersSettings s = Settings ?? new BouldersSettings();
            TerrainFeatureTuning tuning = context.Tuning;
            Bounds box = context.LocalBounds;

            // The shared 'height' tuning scales the whole field's boulder size (heightVariation jitter
            // included), so the designer can grow / shrink every boulder with the common slider.
            float baseHeight = TerrainNoiseHelper.VariedHeight(tuning != null ? tuning.height : 1f,
                                                               tuning, context.Seed);
            // height is in metres; normalise it into a gentle multiplier around 1.
            float sizeScale = Mathf.Clamp(baseHeight <= 0.01f ? 1f : baseHeight / 6f, 0.25f, 4f);

            // --- Scatter the field -----------------------------------------------------------
            List<BoulderInstance> boulders = BouldersScatter.Build(context, s, sizeScale);

            // --- Volume bounds: boulders are not tall, so keep the Y extent modest -----------
            // Find the ground span and the tallest boulder so the volume just encloses the field.
            float groundLo = float.MaxValue, groundHi = float.MinValue;
            float topMost = float.MinValue, botMost = float.MaxValue;
            foreach (var b in boulders)
            {
                float halfY = b.Radius * b.AxisScale.y + b.Radius * s.irregularity;
                topMost = Mathf.Max(topMost, b.Centre.y + halfY);
                botMost = Mathf.Min(botMost, b.Centre.y - halfY);
            }
            // Sample ground at the box corners + centre for a sane fallback when the field is empty.
            foreach (var c in BoxSampleXZ(box))
            {
                float g = context.LocalGroundHeight(c.x, c.y);
                groundLo = Mathf.Min(groundLo, g);
                groundHi = Mathf.Max(groundHi, g);
            }
            if (boulders.Count == 0) { topMost = groundHi + 2f; botMost = groundLo - 2f; }

            float pad = context.VoxelSize * 2f + 1f;
            float vMinY = Mathf.Min(botMost, groundLo) - pad;
            float vMaxY = Mathf.Max(topMost, groundHi) + pad;
            Vector3 volCentre = new Vector3(box.center.x, (vMinY + vMaxY) * 0.5f, box.center.z);
            Vector3 volSize = new Vector3(box.size.x + pad * 2f, vMaxY - vMinY, box.size.z + pad * 2f);
            Bounds volume = new Bounds(volCentre, volSize);

            return new BouldersSdf(boulders, s, volume, context.LocalGroundHeight, context.Seed)
                .WithTuning(tuning);
        }

        /// <summary>Five representative XZ probe points (corners + centre) of the footprint box, used
        /// to bound the meshing volume's vertical extent against the underlying ground.</summary>
        static IEnumerable<Vector2> BoxSampleXZ(Bounds box)
        {
            yield return new Vector2(box.min.x, box.min.z);
            yield return new Vector2(box.max.x, box.min.z);
            yield return new Vector2(box.min.x, box.max.z);
            yield return new Vector2(box.max.x, box.max.z);
            yield return new Vector2(box.center.x, box.center.z);
        }
    }
}
