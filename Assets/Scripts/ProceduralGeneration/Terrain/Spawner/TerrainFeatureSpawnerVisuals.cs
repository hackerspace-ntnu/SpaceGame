using UnityEngine;

/// <summary>
/// Presentation helpers for <see cref="TerrainFeatureSpawner"/> — the default feature material and
/// the passive footprint gizmo. Split out of the spawner MonoBehaviour purely to keep that file
/// small; none of this is gameplay logic.
/// </summary>
public static class TerrainFeatureSpawnerVisuals
{
    /// <summary>
    /// Returns the explicit feature material if one was assigned, otherwise builds a neutral
    /// desert-rock URP/Lit material so a feature is always visible even before art is hooked up.
    /// </summary>
    public static Material ResolveMaterial(Material assigned)
    {
        if (assigned != null) return assigned;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(shader) { name = "DefaultTerrainFeatureMaterial" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.78f, 0.66f, 0.46f));
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
        return m;
    }

    /// <summary>
    /// Draws the passive footprint gizmo for a spawner — the spline curve for linear features, the
    /// editable polygon outline for area features (or the seeding box when no polygon exists yet).
    /// The interactive handles are drawn separately by the editor; this is the always-on outline.
    /// </summary>
    public static void DrawFootprintGizmo(TerrainFeatureSpawner spawner)
    {
        if (spawner == null) return;
        Gizmos.matrix = spawner.transform.localToWorldMatrix;

        if (spawner.UsesPath)
        {
            FeaturePath path = spawner.Path;
            Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.9f);
            if (path != null && path.IsValid)
            {
                var spline = new FeatureSpline(path);
                Vector3 prev = spline.Evaluate(0f);
                for (int i = 1; i <= 48; i++)
                {
                    Vector3 cur = spline.Evaluate(i / 48f);
                    Gizmos.DrawLine(prev, cur);
                    prev = cur;
                }
            }
            return;
        }

        Gizmos.color = new Color(0.5f, 0.85f, 1f, 0.6f);
        FeaturePolygon footprint = spawner.Footprint;
        if (footprint != null && footprint.IsValid)
        {
            var v = footprint.vertices;
            for (int i = 0; i < v.Count; i++)
            {
                Vector3 a = new Vector3(v[i].x, 0f, v[i].z);
                int j = (i + 1) % v.Count;
                Vector3 b = new Vector3(v[j].x, 0f, v[j].z);
                Gizmos.DrawLine(a, b);
            }
        }
        else
        {
            // No polygon drawn yet — show the seeding box instead.
            Gizmos.DrawWireCube(Vector3.zero, spawner.BoxHalfExtents * 2f);
        }
    }
}
