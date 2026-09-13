using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// One expanding ground wave for a repulsor blast. Built procedurally (a flat annulus) so no
    /// prefab or FBX is needed; the material is expected to be the RepulsorShockwave shader, whose
    /// _Progress drives the wave's die-off. Local-only cosmetic — spawned by every machine from
    /// Present, self-destroys, never networked.
    ///
    /// <para>
    /// Two shapes, one mesh builder. <see cref="Spawn"/> draws the full 360° ring a DETONATION
    /// makes — a rocket, a fist landing — where the blast really did go every way at once.
    /// <see cref="SpawnArc"/> draws only the wedge a DIRECTED blast swept, which matters more than
    /// it looks: a full ring under a weapon that throws things one way is the single loudest thing
    /// telling the player the blast went everywhere, and it contradicts what the physics just did
    /// to the crowd in front of them. The arc is the same wave, cut to the cone that threw it.
    /// </para>
    /// </summary>
    public class RepulsorBlastRing : MonoBehaviour
    {
        private const int Segments = 48;
        private const float InnerFraction = 0.7f;
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int ArcFadeId = Shader.PropertyToID("_ArcFade");

        private float maxRadius;
        private float duration;
        private float startTime;
        private float sweepDegrees;
        private Material material;

        /// <summary>The full ring: a blast that really did go every way at once.</summary>
        public static void Spawn(Vector3 position, float maxRadius, float duration, Material source)
            => Create(position, Quaternion.identity, 360f, maxRadius, duration, source);

        /// <summary>
        /// The wedge a directed blast swept: <paramref name="halfAngleDeg"/> either side of
        /// <paramref name="facing"/>, flattened onto the ground.
        /// </summary>
        /// <param name="halfAngleDeg">
        /// Pass the SAME half-angle the authority sweep used, so the scorch on the ground covers
        /// exactly the bodies that were thrown and nothing else.
        /// </param>
        public static void SpawnArc(Vector3 position, Vector3 facing, float halfAngleDeg,
                                    float maxRadius, float duration, Material source)
        {
            // Aimed straight up or down there is no ground direction to centre the wedge on. A full
            // ring is the honest answer for a shot with no horizontal component at all, and it is
            // also what the fling math falls back to.
            Vector3 flat = Vector3.ProjectOnPlane(facing, Vector3.up);
            if (flat.sqrMagnitude < 1e-6f)
            {
                Spawn(position, maxRadius, duration, source);
                return;
            }

            Create(position, Quaternion.LookRotation(flat.normalized, Vector3.up),
                   Mathf.Clamp(halfAngleDeg * 2f, 5f, 360f), maxRadius, duration, source);
        }

        private static void Create(Vector3 position, Quaternion rotation, float sweepDegrees,
                                   float maxRadius, float duration, Material source)
        {
            if (source == null) return;
            var go = new GameObject("RepulsorBlastRing");
            go.transform.SetPositionAndRotation(position, rotation);
            var ring = go.AddComponent<RepulsorBlastRing>();
            ring.maxRadius = Mathf.Max(0.5f, maxRadius);
            ring.duration = Mathf.Max(0.05f, duration);
            ring.sweepDegrees = sweepDegrees;
            ring.material = new Material(source); // instance — _Progress is animated per ring
            ring.Build();
        }

        private void Build()
        {
            bool closed = sweepDegrees >= 359.9f;

            // A closed ring wraps its last quad back onto vertex 0 and needs no duplicate seam; an
            // open wedge needs one more column of vertices than it has quads, or the arc comes up a
            // slice short of the angle it was asked for.
            int columns = closed ? Segments : Segments + 1;
            int quads = Segments;

            var mesh = new Mesh { name = "RepulsorRing" };
            var verts = new Vector3[columns * 2];
            var uvs = new Vector2[columns * 2];
            var tris = new int[quads * 6];

            float sweep = sweepDegrees * Mathf.Deg2Rad;
            // Centred on local +Z (the facing SpawnArc built the rotation from) rather than started
            // at it, so the wedge straddles the aim instead of sitting to one side of it.
            float start = Mathf.PI * 0.5f - sweep * 0.5f;

            for (int i = 0; i < columns; i++)
            {
                float u = i / (float)Segments;
                float a = start + sweep * u;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));

                verts[i * 2] = dir * InnerFraction; // unit mesh; transform scale animates size
                verts[i * 2 + 1] = dir;
                uvs[i * 2] = new Vector2(u, 0f);
                uvs[i * 2 + 1] = new Vector2(u, 1f);
            }

            for (int i = 0; i < quads; i++)
            {
                int next = closed ? (i + 1) % columns : i + 1;
                int t = i * 6;
                tris[t] = i * 2; tris[t + 1] = next * 2; tris[t + 2] = i * 2 + 1;
                tris[t + 3] = i * 2 + 1; tris[t + 4] = next * 2; tris[t + 5] = next * 2 + 1;
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            // An open wedge terminates in two straight radial cuts. Left hard they read as a
            // polygon lying on the sand rather than as a wave; the shader feathers U's two ends
            // instead. A closed ring must keep it off — U wraps there, so the same feather would
            // punch a gap out of one side of an otherwise seamless circle.
            material.SetFloat(ArcFadeId, closed ? 0f : 1f);

            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            startTime = Time.time;
        }

        private void Update()
        {
            float t = (Time.time - startTime) / duration;
            if (t >= 1f) { Destroy(gameObject); return; }

            float eased = 1f - (1f - t) * (1f - t); // fast out, soft stop
            transform.localScale = Vector3.one * (maxRadius * eased);
            material.SetFloat(ProgressId, t);
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);

            var mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
        }
    }
}
