using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Library of analytic cross-section / silhouette profile functions shared by the terrain features.
    /// A feature picks a profile, scales it by its tuning height, and adds
    /// <see cref="TerrainNoiseHelper.SurfaceNoise"/> on top.
    ///
    /// All profiles take a normalised coordinate (usually in [-1, 1] or [0, 1]) and return a
    /// normalised height/depth in [0, 1] which the feature then multiplies by metres.
    /// </summary>
    public static class TerrainProfiles
    {
        /// <summary>
        /// Flat-topped plateau profile (mesa silhouette). <paramref name="edgeDistance"/> is
        /// the normalised distance from the footprint edge inward, [0, 1]. The result is 0 at the
        /// edge, rises steeply across <paramref name="wallFraction"/> of the span, then is a flat 1
        /// across the top. Gives steep-sided, flat-topped rock.
        /// </summary>
        public static float Plateau(float edgeDistance, float wallFraction)
        {
            float w = Mathf.Clamp(wallFraction, 0.01f, 1f);
            float u = Mathf.Clamp01(edgeDistance / w);
            return Mathf.SmoothStep(0f, 1f, u);
        }

        /// <summary>
        /// Smooth single-sided escarpment step (cliff profile). <paramref name="t"/> runs [-1, 1]
        /// across the footprint; the result eases from 0 on the low side to 1 on the high side, with
        /// the transition centred at <paramref name="edge"/> and spread over <paramref name="width"/>.
        /// </summary>
        public static float CliffStep(float t, float edge, float width)
        {
            float w = Mathf.Max(0.01f, width);
            float u = Mathf.Clamp01((t - edge) / w + 0.5f);
            return Mathf.SmoothStep(0f, 1f, u);
        }
    }
}
