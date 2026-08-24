using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// One expanding ground ring for a repulsor blast. Built procedurally (a flat annulus) so no
    /// prefab or FBX is needed; the material is expected to be the RepulsorShockwave shader, whose
    /// _Progress drives the wave's die-off. Local-only cosmetic — spawned by every machine from
    /// PresentHold, self-destroys, never networked.
    /// </summary>
    public class RepulsorBlastRing : MonoBehaviour
    {
        private const int Segments = 48;
        private const float InnerFraction = 0.7f;
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");

        private float maxRadius;
        private float duration;
        private float startTime;
        private Material material;

        public static void Spawn(Vector3 position, float maxRadius, float duration, Material source)
        {
            if (source == null) return;
            var go = new GameObject("RepulsorBlastRing");
            go.transform.position = position;
            var ring = go.AddComponent<RepulsorBlastRing>();
            ring.maxRadius = Mathf.Max(0.5f, maxRadius);
            ring.duration = Mathf.Max(0.05f, duration);
            ring.material = new Material(source); // instance — _Progress is animated per ring
            ring.Build();
        }

        private void Build()
        {
            var mesh = new Mesh { name = "RepulsorRing" };
            var verts = new Vector3[Segments * 2];
            var uvs = new Vector2[Segments * 2];
            var tris = new int[Segments * 6];

            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts[i * 2] = dir * InnerFraction; // unit mesh; transform scale animates size
                verts[i * 2 + 1] = dir;
                uvs[i * 2] = new Vector2(i / (float)Segments, 0f);
                uvs[i * 2 + 1] = new Vector2(i / (float)Segments, 1f);

                int next = (i + 1) % Segments;
                int t = i * 6;
                tris[t] = i * 2; tris[t + 1] = next * 2; tris[t + 2] = i * 2 + 1;
                tris[t + 3] = i * 2 + 1; tris[t + 4] = next * 2; tris[t + 5] = next * 2 + 1;
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
