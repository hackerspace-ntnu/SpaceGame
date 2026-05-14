using UnityEngine;

/// <summary>
/// Signed distance primitives (cave-air convention: negative = inside cave / empty).
///
/// All distances are *un-padded* SDFs — the caller is responsible for shifting by a wall thickness
/// if needed. For caves we don't shift; the wall thickness comes from voxel resolution itself.
/// </summary>
public static class SdfPrimitives
{
    /// <summary>Negative inside the sphere, positive outside. r = sphere radius.</summary>
    public static float Sphere(Vector3 p, Vector3 center, float r)
    {
        return (p - center).magnitude - r;
    }

    /// <summary>Capsule with hemispherical caps between a and b, radius r. Cheaper than two spheres + a cylinder.</summary>
    public static float Capsule(Vector3 p, Vector3 a, Vector3 b, float r)
    {
        Vector3 pa = p - a;
        Vector3 ba = b - a;
        float baLen2 = Vector3.Dot(ba, ba);
        // Avoid div-by-zero if a == b (degenerate corridor).
        float h = baLen2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(pa, ba) / baLen2) : 0f;
        return (pa - ba * h).magnitude - r;
    }

    /// <summary>
    /// Polynomial smooth-min — softly blends two SDFs so corridors flow into rooms without seams.
    /// k = blend radius (set to 0 for a hard union).
    /// </summary>
    public static float SmoothMin(float a, float b, float k)
    {
        if (k <= 0f) return Mathf.Min(a, b);
        float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
        return Mathf.Lerp(b, a, h) - k * h * (1f - h);
    }

    /// <summary>
    /// Carve a floor into a primitive's SDF. The floor is a tilted, slightly noisy plane sitting at
    /// roughly Y = center.y - depth. Tilt direction <paramref name="slopeXZ"/> (length = rise/run) lets
    /// callers give every primitive its own gentle slope, and <paramref name="roughness"/> adds noise
    /// for a bumpy organic feel. <paramref name="strength"/> blends between the primitive's natural
    /// curved floor (0) and the carved plane (1).
    /// </summary>
    public static float ApplyFloorFlatten(
        float sdf,
        Vector3 p,
        Vector3 center,
        float depth,
        Vector2 slopeXZ,
        float roughnessOffset,
        float strength)
    {
        if (depth <= 0f || strength <= 0f) return sdf;
        // Per-primitive tilted plane: floorY varies linearly across XZ from the centre point.
        float dx = p.x - center.x;
        float dz = p.z - center.z;
        float floorY = center.y - depth + dx * slopeXZ.x + dz * slopeXZ.y + roughnessOffset;
        float halfSpace = floorY - p.y;
        // Lerp the SDF intersection: at strength=1 we get a true SDF-max (hard flat plane); at
        // strength<1 we soft-blend toward the natural primitive SDF, leaving organic curvature.
        float hardCut = Mathf.Max(sdf, halfSpace);
        return Mathf.Lerp(sdf, hardCut, strength);
    }
}
