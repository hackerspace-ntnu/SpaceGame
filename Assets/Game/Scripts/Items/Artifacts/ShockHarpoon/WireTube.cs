using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Draws the line beside it as a round cable instead of a flat ribbon.
    ///
    /// <para>
    /// A <see cref="LineRenderer"/> is a camera-facing strip: lit, it shades like a sheet of card,
    /// and at the harpoon's width it read as a flickering stripe rather than a wire. The line stays
    /// the point list — <see cref="GrappleRope"/>, shared with the grappling hook, writes its whip
    /// and sag into it — and is never drawn (<see cref="Renderer.forceRenderingOff"/>); this skins
    /// those points as a tube with real normals, so the cable catches light around its
    /// circumference like the coil on the gun.
    /// </para>
    /// <para>
    /// The rings are carried along the line by parallel transport, so a whipping cable never
    /// corkscrews its own seam.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(LineRenderer), typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class WireTube : MonoBehaviour
    {
        [Tooltip("Cable radius, world metres.")]
        [SerializeField] private float radius = 0.009f;
        [Tooltip("Faces around the cable.")]
        [SerializeField, Range(3, 16)] private int sides = 8;
        [Tooltip("Metres of cable one repeat of the material's texture covers.")]
        [SerializeField] private float textureMetres = 0.1f;

        private LineRenderer line;
        private MeshRenderer skin;
        private Mesh mesh;

        private Vector3[] points = new Vector3[0];
        private Vector3[] vertices = new Vector3[0];
        private Vector3[] normals = new Vector3[0];
        private Vector2[] uvs = new Vector2[0];
        private int builtFor = -1;

        private void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.forceRenderingOff = true;

            skin = GetComponent<MeshRenderer>();
            skin.enabled = false;

            mesh = new Mesh { name = "WireTube" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        private void LateUpdate()
        {
            bool show = line.enabled && line.positionCount > 1;
            skin.enabled = show;
            if (show) Build();
        }

        private void OnDestroy() => RuntimeObjects.Discard(mesh);

        private void Build()
        {
            int count = line.positionCount;
            int ring = sides + 1;   // one duplicated seam vertex, so the texture wraps cleanly

            if (points.Length != count)
            {
                points = new Vector3[count];
                vertices = new Vector3[count * ring];
                normals = new Vector3[count * ring];
                uvs = new Vector2[count * ring];
            }
            line.GetPositions(points);
            if (!line.useWorldSpace)
                for (int i = 0; i < count; i++) points[i] = line.transform.TransformPoint(points[i]);

            Vector3 tangent = Tangent(0, count);
            Vector3 normal = Vector3.Cross(tangent, Vector3.up);
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.Cross(tangent, Vector3.right);
            normal.Normalize();

            float along = 0f;
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    along += Vector3.Distance(points[i - 1], points[i]);
                    tangent = Tangent(i, count);
                    // Parallel transport: the previous ring's normal, straightened onto this tangent.
                    Vector3 carried = normal - tangent * Vector3.Dot(normal, tangent);
                    if (carried.sqrMagnitude > 1e-8f) normal = carried.normalized;
                }
                Vector3 binormal = Vector3.Cross(tangent, normal);

                for (int j = 0; j < ring; j++)
                {
                    float angle = j / (float)sides * Mathf.PI * 2f;
                    Vector3 outward = normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
                    int v = i * ring + j;
                    vertices[v] = transform.InverseTransformPoint(points[i] + outward * radius);
                    normals[v] = transform.InverseTransformDirection(outward);
                    uvs[v] = new Vector2(j / (float)sides, along / Mathf.Max(textureMetres, 0.001f));
                }
            }

            if (builtFor != count)
            {
                mesh.Clear();
                mesh.vertices = vertices;
                mesh.triangles = Triangles(count, ring);
                builtFor = count;
            }
            else
            {
                mesh.vertices = vertices;
            }
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.RecalculateBounds();
        }

        private Vector3 Tangent(int i, int count)
        {
            Vector3 d = points[Mathf.Min(i + 1, count - 1)] - points[Mathf.Max(i - 1, 0)];
            return d.sqrMagnitude > 1e-10f ? d.normalized : Vector3.forward;
        }

        private int[] Triangles(int count, int ring)
        {
            var tris = new int[(count - 1) * sides * 6];
            int t = 0;
            for (int i = 0; i < count - 1; i++)
            for (int j = 0; j < sides; j++)
            {
                int a = i * ring + j, b = a + ring;
                tris[t++] = a; tris[t++] = a + 1; tris[t++] = b;
                tris[t++] = a + 1; tris[t++] = b + 1; tris[t++] = b;
            }
            return tris;
        }
    }
}
