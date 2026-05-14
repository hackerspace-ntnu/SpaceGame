using UnityEngine;

/// <summary>
/// Visual for the Ruin Scanner pulse. Two pieces share one material:
///
///   * Beam strip — a grid of quads built by casting forward from the muzzle
///     through every grid sample inside the cone. Each vertex sits on
///     whatever surface the beam hit (terrain, building wall, prop), so the
///     scan drapes over real geometry instead of only the ground.
///   * Ray plane — a triangle fan from the muzzle out to the strip's far
///     edge. The shader masks it so only the portion between the muzzle and
///     the current sweep line is lit, making the plane appear to extend from
///     the gun out to the moving line.
///
/// Cells where any of their four corners missed are dropped, so the visual
/// only covers actual surfaces — no floating ghost rectangle over voids.
///
/// If a muzzle <see cref="Transform"/> is supplied, the pulse re-anchors to
/// it each frame and rebuilds the geometry when the player has moved or
/// re-aimed enough — so walking and turning while scanning translates the
/// scan area smoothly. Detection (which secrets get revealed) is still a
/// one-shot at fire time, owned by the artifact.
/// </summary>
public class RuinScannerPulse : MonoBehaviour
{
    private float startTime;
    private float duration;
    private MaterialPropertyBlock mpb;
    private MeshRenderer stripRenderer;
    private MeshRenderer rayRenderer;
    private MeshFilter   stripFilter;
    private MeshFilter   rayFilter;
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");


    // Tracker state. When muzzleTracker is null the pulse stays where it
    // spawned (legacy behaviour).
    private Transform muzzleTracker;
    private Vector3   lastBuildOrigin;
    private Vector3   lastBuildAim;
    // Cone shape captured at spawn — the strip stays in the original cone
    // even if the player re-aims while the pulse plays.
    private float tanHalfAngle;
    private float maxRange;

    // Strip resolution. More slices = better surface hugging but more raycasts.
    private const int LengthSlices = 48;
    private const int WidthSlices  = 6;
    // Lift the strip slightly off the surface to avoid z-fighting.
    private const float SurfaceOffset = 0.05f;
    // Layers the forward cast hits.
    private const int CastMask = ~0;
    // Rebuild thresholds — translation in metres, rotation in cosine of angle.
    // Skipping rebuilds keeps cost bounded when the player is mostly still.
    private const float RebuildMoveDistSqr = 0.01f;       // 0.1 m
    private const float RebuildAimCosThresh = 0.9994f;    // ~2°

    public static RuinScannerPulse Spawn(Vector3 center, Vector3 direction, Vector3 right, Vector3 up,
        float baseRadius, float centerLength, float[] rimSlants, float duration, Material material,
        Transform muzzleTracker = null)
    {
        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
        direction.Normalize();

        // The artifact's longest detection slant is the real reach of the
        // beam; we cast at most that far so the visual stops where the beam
        // stopped (a wall, the terrain, an object).
        float slant = Mathf.Max(centerLength, 0.5f);
        if (rimSlants != null)
        {
            for (int i = 0; i < rimSlants.Length; i++)
                if (rimSlants[i] > slant) slant = rimSlants[i];
        }
        float tanHalfAngle = baseRadius / Mathf.Max(0.01f, slant);

        var go = new GameObject("RuinScannerPulse");
        go.transform.position = center;
        go.transform.rotation = Quaternion.identity;

        var pulse = go.AddComponent<RuinScannerPulse>();
        pulse.duration       = duration;
        pulse.tanHalfAngle   = tanHalfAngle;
        pulse.maxRange       = slant;
        pulse.muzzleTracker  = muzzleTracker;
        pulse.lastBuildOrigin = center;
        pulse.lastBuildAim    = direction;

        var stripGo = new GameObject("Strip");
        stripGo.transform.SetParent(go.transform, false);
        pulse.stripFilter = stripGo.AddComponent<MeshFilter>();
        pulse.stripRenderer = stripGo.AddComponent<MeshRenderer>();
        ConfigureRenderer(pulse.stripRenderer, material);

        var rayGo = new GameObject("Rays");
        rayGo.transform.SetParent(go.transform, false);
        pulse.rayFilter = rayGo.AddComponent<MeshFilter>();
        pulse.rayRenderer = rayGo.AddComponent<MeshRenderer>();
        ConfigureRenderer(pulse.rayRenderer, material);

        pulse.RebuildMeshes(center, direction);

        return pulse;
    }

    private static void ConfigureRenderer(MeshRenderer mr, Material material)
    {
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        if (material != null) mr.sharedMaterial = material;
    }

    /// <summary>
    /// Replaces both child meshes with new ones built from the given muzzle
    /// pose. The strip is forward-projected onto whatever the beam hits
    /// (terrain, walls, props), so it has to be rebuilt whenever the muzzle
    /// has moved or re-aimed appreciably.
    /// </summary>
    private void RebuildMeshes(Vector3 origin, Vector3 aim)
    {
        // Build a basis perpendicular to the aim. Same convention as the
        // artifact's detection rays.
        Vector3 right = Vector3.Cross(aim, Vector3.up);
        if (right.sqrMagnitude < 0.0001f) right = Vector3.Cross(aim, Vector3.right);
        right.Normalize();
        Vector3 up = Vector3.Cross(right, aim).normalized;

        var oldStrip = stripFilter.sharedMesh;
        var oldRays  = rayFilter.sharedMesh;
        stripFilter.sharedMesh = BuildBeamStripMesh(origin, aim, right, up,
            tanHalfAngle, maxRange, out Vector3[] rayTargetsLocal);
        rayFilter.sharedMesh = BuildRayBundleMesh(rayTargetsLocal);
        if (oldStrip != null) Destroy(oldStrip);
        if (oldRays  != null) Destroy(oldRays);
    }

    /// <summary>
    /// Builds a strip of vertices by casting forward from the muzzle through
    /// each grid sample inside the cone. The hit point becomes the vertex,
    /// so the strip drapes onto whatever the beam actually touches —
    /// terrain, building walls, props — instead of only the ground.
    ///
    /// The cone is parameterised so that uv.y = depth along the beam (0 at
    /// the muzzle, 1 at max range), uv.x = lateral position across the cone
    /// at that depth. The shader then sweeps a line from far → near (and
    /// back) using uv.y, exactly as before.
    /// </summary>
    private static Mesh BuildBeamStripMesh(Vector3 origin, Vector3 aim, Vector3 right, Vector3 up,
        float tanHalfAngle, float maxRange, out Vector3[] rayTargetsLocal)
    {
        int nx = WidthSlices + 1;
        int ny = LengthSlices + 1;
        var verts = new Vector3[nx * ny];
        var uvs   = new Vector2[nx * ny];
        var hit   = new bool[nx * ny];

        // UV.y = each vertex's along-axis depth / maxRange. The sweep line
        // is therefore anchored to physical distance from the muzzle, not to
        // the strip's currently-visible cells — so the line moves smoothly
        // regardless of what the beam happens to hit on any given frame
        // (close wall vs. far terrain vs. a robot in the middle).
        for (int y = 0; y < ny; y++)
        {
            float ty = (float)y / (ny - 1);
            float radiusAtY = maxRange * tanHalfAngle * ty;
            for (int x = 0; x < nx; x++)
            {
                float tx = (float)x / (nx - 1);
                float sideDist = (tx - 0.5f) * 2f * radiusAtY;
                Vector3 sample = origin + aim * (ty * maxRange) + right * sideDist;
                Vector3 dir = (sample - origin);
                float maxDist = dir.magnitude;
                int idx = y * nx + x;
                if (maxDist < 0.0001f) { verts[idx] = Vector3.zero; hit[idx] = false; uvs[idx] = new Vector2(tx, 0f); continue; }
                dir /= maxDist;

                if (Physics.Raycast(origin, dir, out RaycastHit h, maxDist,
                        CastMask, QueryTriggerInteraction.Ignore))
                {
                    Vector3 worldPos = h.point + h.normal * SurfaceOffset;
                    Vector3 local = worldPos - origin;
                    verts[idx] = local;
                    hit[idx] = true;
                    float depth = Mathf.Max(0f, Vector3.Dot(local, aim));
                    uvs[idx] = new Vector2(tx, Mathf.Clamp01(depth / maxRange));
                }
                else
                {
                    verts[idx] = sample - origin;
                    hit[idx] = false;
                    uvs[idx] = new Vector2(tx, ty);
                }
            }
        }

        // Only keep cells where all four corners hit. Skipping cells over
        // open sky keeps the visual on actual surfaces. Stretching across
        // depth discontinuities (a near roof next to a far ground) is
        // unavoidable without a much denser grid; the per-vertex offset
        // along the surface normal at least keeps the cell on the correct
        // side of each surface.
        var trisList = new System.Collections.Generic.List<int>(WidthSlices * LengthSlices * 6);
        for (int y = 0; y < LengthSlices; y++)
        {
            for (int x = 0; x < WidthSlices; x++)
            {
                int i00 = y * nx + x;
                int i10 = i00 + 1;
                int i01 = i00 + nx;
                int i11 = i01 + 1;
                if (!hit[i00] || !hit[i10] || !hit[i01] || !hit[i11]) continue;
                trisList.Add(i00); trisList.Add(i01); trisList.Add(i11);
                trisList.Add(i00); trisList.Add(i11); trisList.Add(i10);
            }
        }

        // Ray fan base = strip's far-edge row, so the wedge always lands on
        // whatever the strip's far edge touches.
        int farRowStart = LengthSlices * nx;
        rayTargetsLocal = new Vector3[nx];
        for (int x = 0; x < nx; x++)
            rayTargetsLocal[x] = verts[farRowStart + x];

        var m = new Mesh { name = "RuinScannerStrip" };
        m.indexFormat = verts.Length > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        m.vertices  = verts;
        m.uv        = uvs;
        m.triangles = trisList.ToArray();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>
    /// Builds a single triangle fan from the muzzle (apex) out to the strip's
    /// full far-edge — one connected plane of color. uv.y = -1 at the apex,
    /// 0 at the base; uv.x = cross-width position 0..1 for shader edge fade.
    /// </summary>
    private static Mesh BuildRayBundleMesh(Vector3[] targets)
    {
        int n = targets.Length;
        if (n < 2) return new Mesh { name = "RuinScannerRays" };

        var verts = new Vector3[n + 1];
        var uvs   = new Vector2[n + 1];
        verts[0] = Vector3.zero;
        uvs  [0] = new Vector2(0.5f, -1f);

        for (int i = 0; i < n; i++)
        {
            verts[i + 1] = targets[i];
            float tx = (float)i / (n - 1);
            uvs  [i + 1] = new Vector2(tx, 0f);
        }

        var tris = new int[(n - 1) * 3];
        for (int i = 0; i < n - 1; i++)
        {
            int o = i * 3;
            tris[o + 0] = 0;
            tris[o + 1] = i + 1;
            tris[o + 2] = i + 2;
        }

        var m = new Mesh { name = "RuinScannerRays" };
        m.vertices  = verts;
        m.uv        = uvs;
        m.triangles = tris;
        m.RecalculateBounds();
        return m;
    }

    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        startTime = Time.time;
    }

    private void OnDestroy()
    {
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
    }

    private void Update()
    {
        // Track the muzzle: re-anchor the pulse root every frame so
        // translation is free, and rebuild the terrain projection when the
        // player has moved or re-aimed enough to make it visibly stale.
        if (muzzleTracker != null)
        {
            Vector3 origin = muzzleTracker.position;
            Vector3 aimDir = ResolveAimDirection(muzzleTracker);

            transform.position = origin;

            float distSqr = (origin - lastBuildOrigin).sqrMagnitude;
            float aimCos  = Vector3.Dot(aimDir, lastBuildAim);
            if (distSqr > RebuildMoveDistSqr || aimCos < RebuildAimCosThresh)
            {
                RebuildMeshes(origin, aimDir);
                lastBuildOrigin = origin;
                lastBuildAim    = aimDir;
            }
        }

        float t = Mathf.Clamp01((Time.time - startTime) / Mathf.Max(0.01f, duration));

        if (stripRenderer != null)
        {
            stripRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(ProgressId, t);
            stripRenderer.SetPropertyBlock(mpb);
        }
        if (rayRenderer != null)
        {
            rayRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(ProgressId, t);
            rayRenderer.SetPropertyBlock(mpb);
        }

        if (t >= 1f) Destroy(gameObject);
    }

    /// <summary>
    /// Live aim direction for the tracking pulse. Mirrors the artifact's
    /// preference order at fire time — currently active main camera first,
    /// then the muzzle's own forward as fallback.
    /// </summary>
    private static Vector3 ResolveAimDirection(Transform muzzle)
    {
        var cam = Camera.main;
        if (cam != null && cam.isActiveAndEnabled) return cam.transform.forward;
        return muzzle.forward;
    }
}
