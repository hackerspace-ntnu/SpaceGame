using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A serializable, editable CLOSED polygon footprint for AREA terrain features (dunes, mesas,
/// buttes, cliffs, flat pads). Vertices are stored in the feature's LOCAL XZ plane (Y is ignored),
/// so moving the spawner moves the whole footprint.
///
/// This replaces the old axis-aligned box as the area footprint: a feature no longer fades along a
/// perfect rectangle. It asks the polygon for the signed distance from any (x, z) to the polygon
/// boundary, feeds that into <see cref="TerrainNoiseHelper.OverlapWeight"/>, and so the feature's
/// outline follows whatever shape the designer dragged.
///
/// Sign convention of <see cref="SignedDistanceInside"/>: POSITIVE inside the polygon (metres in
/// from the nearest edge), NEGATIVE outside. That matches the "distance inside" value every area
/// feature already passes to <see cref="TerrainNoiseHelper.OverlapWeight"/>.
///
/// Linear features keep using <see cref="FeaturePath"/>; they ignore this polygon entirely.
/// </summary>
[System.Serializable]
public class FeaturePolygon
{
    [Tooltip("Ordered vertices of the closed footprint, in feature-local space. Y is ignored — only X and Z matter. The editor edits these with draggable handles. At least 3 are needed; fewer falls back to the bounding box.")]
    public List<Vector3> vertices = new List<Vector3>();

    /// <summary>True when the polygon has enough vertices to define an area.</summary>
    public bool IsValid => vertices != null && vertices.Count >= 3;

    /// <summary>Vertex count (0 when unused).</summary>
    public int Count => vertices != null ? vertices.Count : 0;

    /// <summary>
    /// Replaces the polygon with a rectangle matching the given local-space box half-extents, so a
    /// freshly created area feature starts from the familiar box and the designer reshapes from there.
    /// </summary>
    public void ResetToBox(Vector3 localBoxHalfExtents)
    {
        float hx = Mathf.Max(1f, localBoxHalfExtents.x);
        float hz = Mathf.Max(1f, localBoxHalfExtents.z);
        vertices = new List<Vector3>
        {
            new Vector3(-hx, 0f, -hz),
            new Vector3( hx, 0f, -hz),
            new Vector3( hx, 0f,  hz),
            new Vector3(-hx, 0f,  hz),
        };
    }

    /// <summary>
    /// Signed distance from the local-space (x, z) point to the polygon boundary. POSITIVE inside
    /// (metres in from the nearest edge), NEGATIVE outside. This is the single value an area feature
    /// feeds to <see cref="TerrainNoiseHelper.OverlapWeight"/> for its edge falloff — so the
    /// feature's silhouette is exactly the polygon the designer drew.
    /// </summary>
    public float SignedDistanceInside(float x, float z)
    {
        if (!IsValid) return 0f;

        Vector2 p = new Vector2(x, z);
        float minSqr = float.MaxValue;
        bool inside = false;

        // Distance to the nearest edge + even-odd point-in-polygon test, in one pass.
        for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
        {
            Vector2 a = new Vector2(vertices[i].x, vertices[i].z);
            Vector2 b = new Vector2(vertices[j].x, vertices[j].z);

            // Distance to segment a-b.
            Vector2 ab = b - a;
            float lenSqr = Vector2.Dot(ab, ab);
            float t = lenSqr > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSqr) : 0f;
            float dSqr = (p - (a + ab * t)).sqrMagnitude;
            if (dSqr < minSqr) minSqr = dSqr;

            // Ray-cast parity test for containment.
            if (((a.y > p.y) != (b.y > p.y)) &&
                (p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x))
                inside = !inside;
        }

        float dist = Mathf.Sqrt(minSqr);
        return inside ? dist : -dist;
    }

    /// <summary>True if the local-space (x, z) point lies inside the polygon.</summary>
    public bool Contains(float x, float z) => SignedDistanceInside(x, z) > 0f;

    /// <summary>
    /// Builds a deterministic, noise-warped outline that fills the given box half-extents — driven
    /// by a SINGLE <paramref name="complexity"/> dial running 1 → 10:
    ///
    ///   • complexity 1  — a near-perfect clean circle (or box-proportioned ellipse).
    ///   • complexity ~4 — a soft, gently rolling organic blob.
    ///   • complexity ~7 — pronounced lobes pushing in and out in different directions.
    ///   • complexity 10 — a wild, spiky, multi-armed silhouette with no two directions alike.
    ///
    /// As the dial rises three things scale together: the radius noise gets STRONGER (lobes pull
    /// further in/out), gains more OCTAVES (finer spikes ride on the big lobes), and the ring uses
    /// MORE VERTICES (so the sharper detail is actually resolved). A per-direction asymmetry term
    /// (also scaled by the dial) breaks the radial symmetry so arms point random ways instead of
    /// forming an even star.
    ///
    /// Same (box, seed, complexity) always yields the same polygon, so a Noise-mode footprint
    /// bakes identically every time.
    /// </summary>
    public void GenerateFromNoise(Vector3 boxHalfExtents, int seed, float complexity)
    {
        float hx = Mathf.Max(1f, boxHalfExtents.x);
        float hz = Mathf.Max(1f, boxHalfExtents.z);

        // Normalised dial: 0 at complexity 1 (clean circle) → 1 at complexity 10 (wild).
        float c = Mathf.Clamp01((Mathf.Clamp(complexity, 1f, 10f) - 1f) / 9f);

        // Everything scales off the one dial.
        int n          = Mathf.RoundToInt(Mathf.Lerp(20f, 64f, c));   // ring resolution
        float amp      = Mathf.Lerp(0.02f, 0.85f, c);                 // how far lobes pull in/out
        int octaves    = Mathf.RoundToInt(Mathf.Lerp(1f, 4f, c));     // detail layers
        float asymmetry= Mathf.Lerp(0f, 0.55f, c);                    // breaks radial symmetry
        // Base lobe count: a couple of broad lobes at low complexity, many at high.
        float baseFreq = Mathf.Lerp(1.5f, 5f, c);

        // Deterministic per-feature noise origin so different seeds give different outlines.
        float ox = (seed * 0.731f) % 1000f + 13.7f;
        float oz = (seed * 1.373f) % 1000f + 41.3f;
        // A second, unrelated origin for the symmetry-breaking asymmetry pass.
        float ax = (seed * 2.117f) % 1000f + 71.9f;
        float az = (seed * 0.529f) % 1000f + 5.1f;

        var pts = new List<Vector3>(n);
        for (int i = 0; i < n; i++)
        {
            float ang = (i / (float)n) * Mathf.PI * 2f;
            float cos = Mathf.Cos(ang);
            float sin = Mathf.Sin(ang);

            // Fractal Brownian motion sampled around a circle so it wraps seamlessly at the seam.
            // Each octave doubles the frequency and halves the weight.
            float noise = 0f, freq = baseFreq, weight = 1f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                float nx = ox + Mathf.Cos(ang) * freq;
                float nz = oz + Mathf.Sin(ang) * freq;
                noise += (Mathf.PerlinNoise(nx, nz) * 2f - 1f) * weight;
                norm  += weight;
                freq  *= 2f;
                weight *= 0.5f;
            }
            noise /= Mathf.Max(0.0001f, norm);   // back into [-1, 1]

            // Asymmetry: a slow, low-frequency warp that biases whole arcs of the ring inward or
            // outward, so lobes cluster and point in irregular directions instead of an even star.
            float asym = (Mathf.PerlinNoise(ax + cos * 0.9f, az + sin * 0.9f) * 2f - 1f) * asymmetry;

            // Radius multiplier: 1 at complexity 1 (no swing), wide swing at complexity 10.
            float radiusMul = Mathf.Max(0.12f, 1f + noise * amp + asym);
            pts.Add(new Vector3(cos * hx * radiusMul, 0f, sin * hz * radiusMul));
        }
        vertices = pts;
    }

    /// <summary>Axis-aligned local-space bounds enclosing every vertex (zero-size if empty).
    /// Y of the result is zero — area features derive their vertical band from the height function.</summary>
    public Bounds ComputeLocalBounds(float padding = 0f)
    {
        if (Count == 0) return new Bounds(Vector3.zero, Vector3.zero);
        Bounds b = new Bounds(new Vector3(vertices[0].x, 0f, vertices[0].z), Vector3.zero);
        for (int i = 1; i < vertices.Count; i++)
            b.Encapsulate(new Vector3(vertices[i].x, 0f, vertices[i].z));
        if (padding != 0f) b.Expand(padding * 2f);
        return b;
    }
}
