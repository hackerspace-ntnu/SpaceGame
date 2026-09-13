using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The wall of air itself — a cone shell that opens out of the gauntlet along the aim and dies
    /// as it reaches full range. Sibling of <see cref="RepulsorBlastRing"/>, which draws the same
    /// blast where it meets the ground; this one draws the volume the blast actually swept, so a
    /// shot aimed up or down still reads as a blast rather than as a ring at the caster's feet.
    ///
    /// Built procedurally so no prefab or FBX is needed, and shaped to the SAME half-angle the
    /// authority sweep used — the cone the player sees is the cone that threw them.
    ///
    /// The material is expected to be the RepulsorShockwave shader, whose V runs 0 at the trailing
    /// edge to 1 at the leading one: here that is the apex at the hand out to the rim, so the hot
    /// edge rides the front of the wave and the faint skirt trails back to the gauntlet.
    /// Local-only cosmetic — spawned by every machine from Present, self-destroys, never networked.
    /// </summary>
    public class RepulsorBlastCone : MonoBehaviour
    {
        private const int Segments = 48;

        /// <summary>
        /// Where the compression front sits in V at the START and END of the shot, as a fraction of
        /// the cone's slant.
        ///
        /// <para>
        /// Load-bearing, and the difference between a shockwave and a cone-shaped light. Left at a
        /// fixed V the band is pinned to the same fraction of a shell that is itself growing, so
        /// the front does travel outward — but the whole shape inflates around it and the wave
        /// never LEAVES the hand. Sweeping the band outward at the same time detaches it: the front
        /// runs off the end of the shell it was born in while the skirt behind it thins to nothing,
        /// which is what a wall of air departing actually looks like (GDC-L1-ANIM-0001 — the
        /// follow-through is the part that sells it).
        /// </para>
        /// </summary>
        private const float FrontStart = 0.30f, FrontEnd = 1.06f;

        /// <summary>Skirt strength at the start of the shot, decayed to nothing as the front leaves.</summary>
        private const float SkirtStart = 0.55f;

        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int LeadingEdgeId = Shader.PropertyToID("_LeadingEdge");
        private static readonly int SkirtStrengthId = Shader.PropertyToID("_SkirtStrength");

        private float maxRange;
        private float halfAngleDeg;
        private float duration;
        private float startTime;
        private Material material;

        public static void Spawn(Vector3 origin, Vector3 direction, float range, float halfAngleDeg,
                                 float duration, Material source)
        {
            if (source == null) return;

            var go = new GameObject("RepulsorBlastCone");
            go.transform.position = origin;
            // A blast fired at a body directly overhead leaves nothing to aim the cone along; the
            // force path has the same fallback (FlingVelocity), so match it rather than log.
            go.transform.rotation = direction.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(direction.normalized)
                : Quaternion.identity;

            var cone = go.AddComponent<RepulsorBlastCone>();
            cone.maxRange = Mathf.Max(0.5f, range);
            cone.halfAngleDeg = Mathf.Clamp(halfAngleDeg, 1f, 89f);
            cone.duration = Mathf.Max(0.05f, duration);
            cone.material = new Material(source); // instance — _Progress is animated per cone
            cone.Build();
        }

        /// <summary>
        /// A unit cone: apex at the origin, rim one unit of SLANT away — not one unit deep. That
        /// is what makes a uniform scale by the blast radius line up with the sweep, which tests
        /// distance from the origin (RepulsorBlast.InCone) and not depth along the aim.
        /// </summary>
        private void Build()
        {
            float rad = halfAngleDeg * Mathf.Deg2Rad;
            float rimRadius = Mathf.Sin(rad);
            float rimDepth = Mathf.Cos(rad);

            var mesh = new Mesh { name = "RepulsorCone" };
            var verts = new Vector3[Segments * 2];
            var uvs = new Vector2[Segments * 2];
            var tris = new int[Segments * 3];

            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                float u = i / (float)Segments;

                // The apex is duplicated per segment so U can vary around the cone; one shared
                // apex vertex would smear the whole sweep of the texture into a single point.
                verts[i * 2] = Vector3.zero;
                verts[i * 2 + 1] = new Vector3(Mathf.Cos(a) * rimRadius, Mathf.Sin(a) * rimRadius, rimDepth);
                uvs[i * 2] = new Vector2(u, 0f);      // trailing edge, at the gauntlet
                uvs[i * 2 + 1] = new Vector2(u, 1f);  // leading rim

                int next = (i + 1) % Segments;
                int t = i * 3;
                tris[t] = i * 2; tris[t + 1] = i * 2 + 1; tris[t + 2] = next * 2 + 1;
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            startTime = Time.time;
        }

        private void Update()
        {
            float t = (Time.time - startTime) / duration;
            if (t >= 1f) { Destroy(gameObject); return; }

            float eased = 1f - (1f - t) * (1f - t); // fast out, soft stop — as the ring
            transform.localScale = Vector3.one * (maxRange * eased);
            material.SetFloat(ProgressId, t);

            // The front runs out past the rim (FrontEnd > 1) on purpose: the shader feathers the
            // last sliver of V, so a band parked exactly at 1 would spend the end of the shot half
            // cut off rather than gone. Overshooting walks it cleanly off the edge.
            material.SetFloat(LeadingEdgeId, Mathf.Lerp(FrontStart, FrontEnd, eased));
            material.SetFloat(SkirtStrengthId, SkirtStart * (1f - t));
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);

            var mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
        }
    }
}
