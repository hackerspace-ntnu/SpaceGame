using UnityEngine;

/// <summary>
/// Downward cone-of-light pulse for the Ruin Scanner. Spawned at the scanner,
/// points straight down, and reaches `radius` meters at its base. Drawn with
/// the RuinScannerPulse shader (additive, soft radial falloff) so it reads as
/// a beam scanning the ground rather than a fullscreen flash.
///
/// The cone is built procedurally — no mesh asset required.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class RuinScannerPulse : MonoBehaviour
{
    private float startTime;
    private float duration;
    private MaterialPropertyBlock mpb;
    private MeshRenderer mr;
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");

    /// <summary>
    /// Spawns the cone. `center` = cone tip (scanner muzzle).
    /// `direction` = forward axis (player aim). `right`/`up` = orthonormal
    /// basis perpendicular to `direction` matching the scanner's ray fan.
    /// `baseRadius` = full-length rim radius. `centerLength` = slant of the
    /// axial ray. `rimSlants` = per-segment slant length for the outer rim
    /// (length must match the segment count of the resulting mesh — we
    /// adopt rimSlants.Length as the radial resolution so visual and
    /// detection geometry stay in lockstep).
    /// </summary>
    public static RuinScannerPulse Spawn(Vector3 center, Vector3 direction, Vector3 right, Vector3 up,
        float baseRadius, float centerLength, float[] rimSlants, float duration, Material material)
    {
        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.down;
        direction.Normalize();

        var go = new GameObject("RuinScannerPulse");
        go.transform.position = center;
        // Mesh is baked in world-space offsets from origin, so identity rotation.
        go.transform.rotation = Quaternion.identity;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = BuildConeMesh(direction, right, up, baseRadius, centerLength, rimSlants);

        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        if (material != null) mr.sharedMaterial = material;

        var pulse = go.AddComponent<RuinScannerPulse>();
        pulse.duration = duration;
        return pulse;
    }

    /// <summary>
    /// Builds a hollow cone shell whose base ring follows the supplied
    /// per-segment slant lengths — i.e. the cone bulges out where the
    /// scanner's rays travelled further. Vertices are local-space offsets
    /// from the muzzle; the GameObject sits at the muzzle with identity
    /// rotation. UVs are (radial 0..1, depth 0=tip..1=base).
    /// </summary>
    private static Mesh BuildConeMesh(Vector3 forward, Vector3 right, Vector3 up,
        float baseRadius, float centerLength, float[] rimSlants)
    {
        int segs = rimSlants.Length;
        var verts = new Vector3[segs * 2];
        var uvs   = new Vector2[segs * 2];
        var tris  = new int[segs * 6];

        // Reference end-cap point for direction toward each rim sample.
        Vector3 axisEnd = forward * Mathf.Max(0.01f, centerLength + baseRadius);
        float maxRim = 0f;

        for (int i = 0; i < segs; i++)
        {
            float t = (float)i / segs;
            float a = t * Mathf.PI * 2f;
            float cx = Mathf.Cos(a);
            float cz = Mathf.Sin(a);

            // Direction toward this rim sample (matches the scanner's ray fan
            // for the outer ring — see RuinScannerArtifact.cs).
            Vector3 rimOffset = (right * cx + up * cz) * baseRadius;
            Vector3 dir = (axisEnd + rimOffset).normalized;
            float slant = Mathf.Max(0.01f, rimSlants[i]);
            Vector3 rimPos = dir * slant;
            if (slant > maxRim) maxRim = slant;

            // Tip ring (degenerate at origin) — duplicated so each lateral quad gets a unique tip vertex.
            verts[i * 2 + 0] = Vector3.zero;
            uvs  [i * 2 + 0] = new Vector2(t, 0f);

            // Base ring (per-segment slant — bulges where rays went further).
            verts[i * 2 + 1] = rimPos;
            uvs  [i * 2 + 1] = new Vector2(t, 1f);
        }

        for (int i = 0; i < segs; i++)
        {
            int next = (i + 1) % segs;
            int a = i * 2;        // tip i
            int b = i * 2 + 1;    // base i
            int c = next * 2;     // tip next
            int d = next * 2 + 1; // base next

            // Two triangles per segment (back-face culling is off in the shader, so winding doesn't matter).
            int o = i * 6;
            tris[o + 0] = a; tris[o + 1] = b; tris[o + 2] = d;
            tris[o + 3] = a; tris[o + 4] = d; tris[o + 5] = c;
        }

        var m = new Mesh { name = "RuinScannerPulseCone" };
        m.vertices  = verts;
        m.uv        = uvs;
        m.triangles = tris;
        m.RecalculateBounds();
        // Conservative bounds so it isn't culled when off-axis.
        var b2 = m.bounds; b2.Expand(Mathf.Max(baseRadius, maxRim) * 0.5f); m.bounds = b2;
        return m;
    }

    private void Awake()
    {
        mr = GetComponent<MeshRenderer>();
        mpb = new MaterialPropertyBlock();
        startTime = Time.time;
    }

    private void OnDestroy()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
    }

    private void Update()
    {
        float t = Mathf.Clamp01((Time.time - startTime) / Mathf.Max(0.01f, duration));

        mr.GetPropertyBlock(mpb);
        mpb.SetFloat(ProgressId, t);
        mr.SetPropertyBlock(mpb);

        if (t >= 1f) Destroy(gameObject);
    }
}
