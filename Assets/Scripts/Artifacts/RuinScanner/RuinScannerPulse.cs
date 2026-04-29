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
    private const int RadialSegments = 48;

    private float startTime;
    private float duration;
    private MaterialPropertyBlock mpb;
    private MeshRenderer mr;
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");

    /// <summary>
    /// Spawns the cone. `center` = scanner position (cone tip).
    /// `radius` = base radius in meters. `length` = how far down the cone extends.
    /// </summary>
    public static RuinScannerPulse Spawn(Vector3 center, float radius, float length, float duration, Material material)
    {
        var go = new GameObject("RuinScannerPulse");
        go.transform.position = center;
        // Mesh is built along local +Y. Rotate so +Y points world-down.
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, Vector3.down);

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = BuildConeMesh(radius, length);

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
    /// Builds a hollow cone mesh (no caps) along local +Y, from tip at origin
    /// down to a circular base of radius `baseRadius` at y=`length`. UVs are
    /// (radial 0..1 across the lateral surface, depth 0=tip..1=base).
    /// </summary>
    private static Mesh BuildConeMesh(float baseRadius, float length)
    {
        int segs = RadialSegments;
        var verts = new Vector3[segs * 2];
        var uvs   = new Vector2[segs * 2];
        var tris  = new int[segs * 6];

        for (int i = 0; i < segs; i++)
        {
            float t = (float)i / segs;
            float a = t * Mathf.PI * 2f;
            float cx = Mathf.Cos(a);
            float cz = Mathf.Sin(a);

            // Tip ring (degenerate at origin) — duplicated so each lateral quad gets a unique tip vertex.
            verts[i * 2 + 0] = Vector3.zero;
            uvs  [i * 2 + 0] = new Vector2(t, 0f);

            // Base ring.
            verts[i * 2 + 1] = new Vector3(cx * baseRadius, length, cz * baseRadius);
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
        var b2 = m.bounds; b2.Expand(baseRadius * 0.5f); m.bounds = b2;
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
