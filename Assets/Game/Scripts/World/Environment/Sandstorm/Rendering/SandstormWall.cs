// Layer 1: the storm seen from outside — the cliff of sand on the horizon.
//
// The mesh is a closed shell that only says WHERE on screen the storm might be; the shader
// intersects the storm's real analytic shape per pixel and raymarches it. So this class's job is
// to put a bounding volume in the right place and hand the shader the same nine numbers the CPU
// uses, and nothing about the storm's appearance is decided here.
//
// The shell is closed rather than open because the shader draws back faces: a hole in the mesh
// would be a hole in the storm, and looking up from inside is exactly where you would find it.
//
// It fades out as the camera enters, as the fullscreen fog fades in. The two are never both at
// full strength, which is what keeps the combined cost flat wherever the player stands.
using UnityEngine;

namespace SpaceGame.World.Weather
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SandstormWall : MonoBehaviour
    {
        private static Mesh cellMesh;
        private static Mesh wallMesh;
        private static MaterialPropertyBlock block;

        private static readonly int ColorId = Shader.PropertyToID("_StormColor");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int ExtinctionId = Shader.PropertyToID("_Extinction");
        private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
        private static readonly int ErosionId = Shader.PropertyToID("_Erosion");
        private static readonly int AnisotropyId = Shader.PropertyToID("_Anisotropy");
        private static readonly int AmbientId = Shader.PropertyToID("_Ambient");
        private static readonly int StretchId = Shader.PropertyToID("_Stretch");
        private static readonly int StepsId = Shader.PropertyToID("_Steps");
        private static readonly int LightStepsId = Shader.PropertyToID("_LightSteps");
        private static readonly int BillowId = Shader.PropertyToID("_BillowSpeed");
        private static readonly int HeightId = Shader.PropertyToID("_StormHeight");
        private static readonly int BaseYId = Shader.PropertyToID("_StormBaseY");
        private static readonly int CenterId = Shader.PropertyToID("_StormCenter");
        private static readonly int ShapeAId = Shader.PropertyToID("_StormShapeA");
        private static readonly int ShapeBId = Shader.PropertyToID("_StormShapeB");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;

        /// <summary>
        /// Builds a shell ready to be positioned. <paramref name="hidden"/> makes it a scratch
        /// object that is never written into a scene file — that is how the editor preview shows a
        /// storm while you author it without leaving anything behind when you save.
        /// </summary>
        public static SandstormWall Create(string objectName, Material material, bool hidden)
        {
            var host = new GameObject(objectName);
            if (hidden)
                host.hideFlags = HideFlags.HideAndDontSave;

            var wall = host.AddComponent<SandstormWall>();
            wall.EnsureComponents();
            wall.meshRenderer.sharedMaterial = material;
            return wall;
        }

        private void Awake() => EnsureComponents();

        // Called from Awake and from Create, because a caller that adds this component and
        // configures it in the same statement has no guarantee about which ran first.
        private void EnsureComponents()
        {
            if (meshRenderer != null)
                return;

            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();

            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>
        /// Puts the shell around the storm and gives the shader its numbers.
        /// </summary>
        /// <param name="cameraDensity">
        /// Storm density at the camera, used to dissolve the shell as the viewer walks in and let
        /// the fullscreen fog take over. Pass 0 to hold it at full strength, as the editor preview does.
        /// </param>
        public void Apply(SandstormProfile profile, in StormFootprint footprint, float intensity, float cameraDensity)
        {
            EnsureComponents();

            // This renderer has light probes off, so the shader cannot sample the sky for itself.
            // Called here rather than only from SandstormVisuals because the editor preview draws
            // a shell without that component ever running.
            SandstormVisuals.PushSkyLight();

            bool isWall = footprint.Kind == StormShapeKind.Wall;
            Mesh mesh = isWall ? WallMesh() : CellMesh();
            if (meshFilter.sharedMesh != mesh)
                meshFilter.sharedMesh = mesh;

            // The shell covers the feathered edge as well as the core, and hangs below the base,
            // because the shader marches a volume that does not stop where the geometry does.
            float outerRadius = footprint.Radius + footprint.EdgeFeather;
            float halfWidth = isWall && footprint.LateralExtent > 0f
                ? footprint.LateralExtent + footprint.EdgeFeather
                : profile.wallDrawHalfWidth;

            transform.position = new Vector3(footprint.Center.x, footprint.BaseY, footprint.Center.y);
            transform.rotation = Quaternion.LookRotation(
                new Vector3(footprint.Heading.x, 0f, footprint.Heading.y), Vector3.up);

            Vector3 worldScale = isWall
                ? new Vector3(halfWidth, footprint.Height, outerRadius)
                : new Vector3(outerRadius, footprint.Height, outerRadius);
            transform.localScale = ToLocalScale(worldScale);

            float insideFade = 1f - StormShape.Smoothstep(0.05f, 0.45f, cameraDensity);
            float opacity = profile.wallOpacity * insideFade;

            meshRenderer.enabled = opacity > 0.002f && intensity > 0.002f;
            if (!meshRenderer.enabled)
                return;

            block ??= new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(block);

            block.SetColor(ColorId, ToRenderSpace(profile.wallColor));
            block.SetFloat(OpacityId, opacity);
            block.SetFloat(IntensityId, intensity);
            block.SetFloat(ExtinctionId, profile.wallExtinction);
            block.SetFloat(NoiseScaleId, Mathf.Max(1f, profile.wallNoiseScale));
            block.SetFloat(ErosionId, profile.erosion);
            block.SetFloat(AnisotropyId, profile.forwardScatter);
            block.SetFloat(AmbientId, profile.ambient);
            block.SetFloat(StretchId, profile.windStretch);
            block.SetFloat(BillowId, profile.wallBillowSpeed);

            block.SetFloat(StepsId, SandstormRenderFeature.WallStepsFor(SandstormRenderFeature.Quality));
            block.SetFloat(LightStepsId, SandstormRenderFeature.LightStepsFor(SandstormRenderFeature.Quality));

            // The same nine numbers StormShape.Density works from, so the shader cannot draw a
            // storm in a different place from the one doing the damage.
            block.SetVector(CenterId, new Vector4(footprint.Center.x, footprint.Center.y, 0f, 0f));
            block.SetVector(ShapeAId, new Vector4(footprint.Radius, Mathf.Max(0.01f, footprint.EdgeFeather),
                                                  footprint.Height, Mathf.Max(0.01f, footprint.HeightFeather)));
            block.SetVector(ShapeBId, new Vector4(isWall ? 1f : 0f, footprint.LateralExtent,
                                                  footprint.Heading.x, footprint.Heading.y));
            block.SetFloat(HeightId, footprint.Height);
            block.SetFloat(BaseYId, footprint.BaseY);

            meshRenderer.SetPropertyBlock(block);
        }

        // MaterialPropertyBlock does not convert colours the way Material.SetColor does, so an
        // authored colour handed to it straight comes out visibly wrong in a linear project.
        private static Color ToRenderSpace(Color color) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;

        // The shell's size is a world measurement, but localScale is relative to whatever it is
        // parented to. Dividing the parent out means the shell can safely live UNDER something —
        // which it must, because an unparented object created at runtime is not owned by any scene
        // and, if it carries DontSave, survives every scene load after it. That is how a storm ends
        // up hanging over the main menu.
        private Vector3 ToLocalScale(Vector3 worldScale)
        {
            if (transform.parent == null)
                return worldScale;

            Vector3 parent = transform.parent.lossyScale;
            return new Vector3(
                worldScale.x / Mathf.Max(1e-4f, Mathf.Abs(parent.x)),
                worldScale.y / Mathf.Max(1e-4f, Mathf.Abs(parent.y)),
                worldScale.z / Mathf.Max(1e-4f, Mathf.Abs(parent.z)));
        }

        // ── Meshes ────────────────────────────────────────────────────────────────
        // Unit-sized and shared by every storm; the transform does the sizing. Closed, and hanging
        // below y = 0, because the shader draws back faces and marches a volume that continues
        // under the storm's base — an open shell would show as a hole in the sky or in the ground.

        private const float Underhang = 0.5f;   // in units of storm height

        private static Mesh CellMesh()
        {
            if (cellMesh != null)
                return cellMesh;

            const int segments = 48;
            var vertices = new Vector3[(segments + 1) * 2 + 2];
            var triangles = new int[segments * 12];

            int topCenter = (segments + 1) * 2;
            int bottomCenter = topCenter + 1;
            vertices[topCenter] = new Vector3(0f, 1f, 0f);
            vertices[bottomCenter] = new Vector3(0f, -Underhang, 0f);

            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                var direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

                vertices[i * 2] = direction + Vector3.up * -Underhang;
                vertices[i * 2 + 1] = direction + Vector3.up;

                if (i == segments)
                    continue;

                int v = i * 2;
                int tri = i * 12;

                // Side
                triangles[tri] = v;
                triangles[tri + 1] = v + 1;
                triangles[tri + 2] = v + 2;
                triangles[tri + 3] = v + 1;
                triangles[tri + 4] = v + 3;
                triangles[tri + 5] = v + 2;

                // Caps
                triangles[tri + 6] = topCenter;
                triangles[tri + 7] = v + 3;
                triangles[tri + 8] = v + 1;
                triangles[tri + 9] = bottomCenter;
                triangles[tri + 10] = v;
                triangles[tri + 11] = v + 2;
            }

            cellMesh = Build("SandstormCellShell", vertices, triangles);
            return cellMesh;
        }

        private static Mesh WallMesh()
        {
            if (wallMesh != null)
                return wallMesh;

            // A closed box: x is half-width, z is half-thickness, y runs from below the base to the
            // storm's ceiling.
            var min = new Vector3(-1f, -Underhang, -1f);
            var max = new Vector3(1f, 1f, 1f);

            var vertices = new[]
            {
                new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z),
            };

            var triangles = new[]
            {
                0, 2, 1, 0, 3, 2,   // bottom
                4, 5, 6, 4, 6, 7,   // top
                0, 1, 5, 0, 5, 4,   // -z
                1, 2, 6, 1, 6, 5,   // +x
                2, 3, 7, 2, 7, 6,   // +z
                3, 0, 4, 3, 4, 7,   // -x
            };

            wallMesh = Build("SandstormWallShell", vertices, triangles);
            return wallMesh;
        }

        private static Mesh Build(string meshName, Vector3[] vertices, int[] triangles)
        {
            // Both shells above are authored inward-wound. The shader draws BACK faces, so they
            // have to be flipped: leave them as they are and the shell renders from outside but
            // vanishes the moment the camera steps inside it — every face is then front-facing and
            // gets culled, and the storm you just walked into disappears. Outward-wound plus
            // Cull Front gives exactly one fragment per pixel, from the far surface, either side.
            FlipWinding(triangles);

            var mesh = new Mesh
            {
                name = meshName,
                hideFlags = HideFlags.HideAndDontSave,
                vertices = vertices,
                triangles = triangles,
            };

            // Bounds are in local space and the transform scales them to kilometres, which is what
            // keeps a storm the player is standing beside from being frustum-culled.
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void FlipWinding(int[] triangles)
        {
            for (int i = 0; i < triangles.Length; i += 3)
                (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
        }
    }
}
