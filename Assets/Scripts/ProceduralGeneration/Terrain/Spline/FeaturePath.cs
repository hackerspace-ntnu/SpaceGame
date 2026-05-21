using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A serializable, editable poly-line path used by LINEAR terrain features (canyons, canyon paths,
/// ridges, natural bridges, stone arches, cave entrances). Points are stored in the feature's
/// LOCAL space (relative to the spawner transform), so moving the spawner moves the whole path.
///
/// The path is a Catmull-Rom spline through its control points: <see cref="FeatureSpline"/> samples
/// it as a smooth curve. Linear features sweep their cross-section profile along this curve.
///
/// Area features ignore the path entirely and use the box footprint; this object simply stays
/// empty for them. Keeping path data on every <see cref="FeatureContext"/> (rather than splitting
/// the base class) is what makes all nine features interchangeable — the #1 design priority.
/// </summary>
[System.Serializable]
public class FeaturePath
{
    [Tooltip("Ordered control points in feature-local space. The editor edits these with draggable handles. A linear feature needs at least 2; an area feature leaves this empty.")]
    public List<Vector3> controlPoints = new List<Vector3>();

    [Tooltip("Half-width (metres) of the feature along the path — e.g. canyon half-width, bridge half-span. Linear features read this as their cross-section extent.")]
    public float halfWidth = 12f;

    /// <summary>True when this path can be swept (has at least two control points).</summary>
    public bool IsValid => controlPoints != null && controlPoints.Count >= 2;

    /// <summary>Number of control points (0 for an unused / area-feature path).</summary>
    public int Count => controlPoints != null ? controlPoints.Count : 0;

    /// <summary>
    /// Replaces the path with a straight two-point default spanning the given local-space box,
    /// so a newly-created linear feature starts with an editable path instead of nothing.
    /// </summary>
    public void ResetToBoxDiagonal(Vector3 localBoxHalfExtents)
    {
        controlPoints = new List<Vector3>
        {
            new Vector3(-localBoxHalfExtents.x * 0.8f, 0f, 0f),
            new Vector3( localBoxHalfExtents.x * 0.8f, 0f, 0f),
        };
    }

    /// <summary>Axis-aligned local-space bounds enclosing every control point (zero-size if empty).</summary>
    public Bounds ComputeLocalBounds(float padding = 0f)
    {
        if (Count == 0) return new Bounds(Vector3.zero, Vector3.zero);
        Bounds b = new Bounds(controlPoints[0], Vector3.zero);
        for (int i = 1; i < controlPoints.Count; i++) b.Encapsulate(controlPoints[i]);
        b.Expand((padding + halfWidth) * 2f);
        return b;
    }
}
