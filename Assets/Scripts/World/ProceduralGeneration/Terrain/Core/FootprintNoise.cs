using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// The explicit, serializable knob block that drives a <see cref="FootprintMode.Noise"/> footprint.
    ///
    /// The noise footprint is a closed ring of vertices placed around the centre of the feature's
    /// Width × Breadth box. Each vertex's radius is the base ellipse radius for its angle, multiplied
    /// by a deterministic noise field. Tuning the knobs sweeps the result continuously from a clean
    /// rounded shape to a wild, messy, multi-armed silhouette:
    ///
    ///   • ALL knobs minimal  → a near-perfect rounded blob (the box-proportioned ellipse).
    ///   • mid settings       → a soft, gently rolling organic outline.
    ///   • ALL knobs maximal  → a spiky, asymmetric, many-lobed mess.
    ///
    /// Determinism: the same (box, seed, knobs) always yields the same polygon, so a Noise footprint
    /// bakes identically every time. Nothing here reads <c>Time</c> or <c>Random</c>.
    /// </summary>
    [System.Serializable]
    public class FootprintNoise
    {
        [Tooltip("Number of vertices in the generated ring. Higher resolves finer detail but costs more " +
                 "polygon-distance work. 24-48 is plenty for most shapes; raise it only for very spiky outlines.")]
        [Range(8, 96)] public int resolution = 40;

        [Tooltip("How many big lobes run around the outline. ~1.5 = a couple of broad bulges. " +
                 "5+ = many fingers. This is the base frequency of the radius noise.")]
        [Range(0.5f, 8f)] public float lobeFrequency = 2.5f;

        [Tooltip("How far the lobes pull the outline in and out, as a fraction of the base radius. " +
                 "0 = a perfectly clean ellipse. 0.8 = lobes nearly double / halve the radius.")]
        [Range(0f, 0.9f)] public float lobeAmplitude = 0.3f;

        [Tooltip("Layers of finer noise riding on top of the big lobes. 1 = only the broad lobes " +
                 "(smooth). 4 = fine spikes and crinkles on every lobe.")]
        [Range(1, 5)] public int detailOctaves = 2;

        [Tooltip("How strongly each finer octave contributes relative to the one below it. " +
                 "0.2 = detail barely visible. 0.7 = harsh, busy, broken edge.")]
        [Range(0f, 0.8f)] public float detailGain = 0.5f;

        [Tooltip("Breaks the radial symmetry: biases whole arcs of the outline inward or outward so " +
                 "lobes cluster and point in irregular directions instead of forming an even star. " +
                 "0 = symmetric, 1 = wildly lopsided.")]
        [Range(0f, 1f)] public float irregularity = 0.35f;

        [Tooltip("Pushes the lobes toward sharp points. 0 = soft rounded bulges. 1 = pinched, " +
                 "spiky corners — a jagged, shattered outline.")]
        [Range(0f, 1f)] public float cornerSharpness = 0.2f;

        /// <summary>
        /// Generates the closed outline ring into <paramref name="target"/>, sized to the box
        /// half-extents (Width on X, Breadth on Z) and shaped by the knobs above. The result is a
        /// list of feature-local XZ vertices (Y always 0).
        /// </summary>
        public void Generate(List<Vector3> target, Vector3 boxHalfExtents, int seed)
        {
            target.Clear();

            float hx = Mathf.Max(1f, boxHalfExtents.x);   // half-Width
            float hz = Mathf.Max(1f, boxHalfExtents.z);   // half-Breadth
            int n = Mathf.Clamp(resolution, 8, 96);

            // Deterministic per-seed noise origins. Two unrelated origins keep the lobe field and the
            // asymmetry field independent so they do not visually correlate.
            float lobeOx = Frac(seed * 0.731f) * 256f + 13.7f;
            float lobeOz = Frac(seed * 1.373f) * 256f + 41.3f;
            float asymOx = Frac(seed * 2.117f) * 256f + 71.9f;
            float asymOz = Frac(seed * 0.529f) * 256f + 5.1f;

            for (int i = 0; i < n; i++)
            {
                float ang = (i / (float)n) * Mathf.PI * 2f;
                float cos = Mathf.Cos(ang);
                float sin = Mathf.Sin(ang);

                // --- Multi-octave lobe noise, sampled AROUND a circle so it wraps at the seam. ------
                float noise = 0f, freq = lobeFrequency, weight = 1f, norm = 0f;
                for (int o = 0; o < Mathf.Clamp(detailOctaves, 1, 5); o++)
                {
                    float nx = lobeOx + Mathf.Cos(ang) * freq;
                    float nz = lobeOz + Mathf.Sin(ang) * freq;
                    noise += (Mathf.PerlinNoise(nx, nz) * 2f - 1f) * weight;
                    norm += weight;
                    freq *= 2f;
                    weight *= Mathf.Clamp01(detailGain);
                }
                noise = norm > 1e-5f ? noise / norm : 0f;   // back into ~[-1, 1]

                // --- Corner sharpness: bend the noise toward pinched points. -----------------------
                // Raising |noise| to a power < 1 sharpens crests; we lerp by the knob so 0 = untouched.
                if (cornerSharpness > 0f)
                {
                    float sign = Mathf.Sign(noise);
                    float sharp = sign * Mathf.Pow(Mathf.Abs(noise), Mathf.Lerp(1f, 0.35f, cornerSharpness));
                    noise = Mathf.Lerp(noise, sharp, cornerSharpness);
                }

                // --- Asymmetry: a slow low-frequency warp biasing whole arcs in or out. ------------
                float asym = (Mathf.PerlinNoise(asymOx + cos * 0.9f, asymOz + sin * 0.9f) * 2f - 1f)
                             * irregularity * 0.6f;

                // Radius multiplier — clamped so the outline can never collapse through the centre.
                float radiusMul = Mathf.Max(0.12f, 1f + noise * lobeAmplitude + asym);
                target.Add(new Vector3(cos * hx * radiusMul, 0f, sin * hz * radiusMul));
            }
        }

        /// <summary>Deterministic fractional part, kept positive — used to derive per-seed origins.</summary>
        static float Frac(float v)
        {
            float f = v - Mathf.Floor(v);
            return f < 0f ? f + 1f : f;
        }
    }
}
