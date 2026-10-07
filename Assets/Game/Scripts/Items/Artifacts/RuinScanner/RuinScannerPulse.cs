using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Cone-of-light pulse for the Ruin Scanner. A single procedural cone mesh:
    /// tip at the scanner muzzle, opening out along the scan direction to a
    /// circular base `length` metres away. Drawn with the RuinScannerPulse shader
    /// (additive, near-transparent blue) so it reads as a translucent volume of
    /// light shining from the gun toward the scanned ground.
    ///
    /// The mesh is built once along local +Z; the GameObject is rotated so +Z
    /// follows the scan direction, so the cone always roots on the muzzle.
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

        // Optional muzzle whose POSITION the cone tip follows each frame, so walking while the
        // pulse plays keeps the cone glued to the scanner. Null = stay put.
        private Transform muzzleTracker;

        /// <summary>
        /// Spawns the cone. `origin` = muzzle position (cone tip).
        /// `direction` = scan aim (cone axis). `baseRadius` = base radius in
        /// metres. `length` = how far the cone reaches along the axis.
        /// </summary>
        public static RuinScannerPulse Spawn(Vector3 origin, Vector3 direction,
            float baseRadius, float length, float duration, Material material,
            Transform muzzleTracker = null)
        {
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
            direction.Normalize();

            var go = new GameObject("RuinScannerPulse");
            go.transform.position = origin;
            // Mesh is built along local +Z. Rotate so +Z points along the scan.
            go.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = BuildConeMesh(baseRadius, length);

            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            if (material != null) mr.sharedMaterial = material;

            var pulse = go.AddComponent<RuinScannerPulse>();
            pulse.duration      = duration;
            pulse.muzzleTracker = muzzleTracker;
            return pulse;
        }

        /// <summary>
        /// Builds a hollow cone mesh (no caps) along local +Z, from the tip at
        /// the origin out to a circular base of radius `baseRadius` at z=`length`.
        /// UVs are (radial 0..1 across the lateral surface, depth 0=tip..1=base).
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
                float cy = Mathf.Sin(a);

                // Tip ring — degenerate at origin, duplicated per segment so each
                // lateral quad gets its own tip vertex (clean UVs).
                verts[i * 2 + 0] = Vector3.zero;
                uvs  [i * 2 + 0] = new Vector2(t, 0f);

                // Base ring at z = length.
                verts[i * 2 + 1] = new Vector3(cx * baseRadius, cy * baseRadius, length);
                uvs  [i * 2 + 1] = new Vector2(t, 1f);
            }

            for (int i = 0; i < segs; i++)
            {
                int next = (i + 1) % segs;
                int a = i * 2;        // tip i
                int b = i * 2 + 1;    // base i
                int c = next * 2;     // tip next
                int d = next * 2 + 1; // base next

                // Two triangles per segment. Cull is off in the shader so winding
                // doesn't matter — both walls draw, stacking the additive fill.
                int o = i * 6;
                tris[o + 0] = a; tris[o + 1] = b; tris[o + 2] = d;
                tris[o + 3] = a; tris[o + 4] = d; tris[o + 5] = c;
            }

            var m = new Mesh { name = "RuinScannerPulseCone" };
            m.vertices  = verts;
            m.uv        = uvs;
            m.triangles = tris;
            m.RecalculateBounds();
            // Conservative bounds so it isn't culled when viewed off-axis.
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
            // Follow the muzzle's POSITION so walking while the pulse plays keeps the cone on the
            // gun. Not its direction: the cone shows the sweep that was actually scanned, and that
            // direction came off the scanning player's own view and travelled in the use message.
            // Re-deriving it here from Camera.main pointed every peer's copy of the cone down its
            // OWN player's view — and left the drawn sweep disagreeing with the reveal it was
            // drawn for even on the machine that fired.
            if (muzzleTracker != null)
                transform.position = muzzleTracker.position;

            float t = Mathf.Clamp01((Time.time - startTime) / Mathf.Max(0.01f, duration));

            mr.GetPropertyBlock(mpb);
            mpb.SetFloat(ProgressId, t);
            mr.SetPropertyBlock(mpb);

            if (t >= 1f) Destroy(gameObject);
        }
    }
}
