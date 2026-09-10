// Builds the bottled singularity's event horizon: the white half-transparent ball and the black
// ring around it, onto the SingularityWell prefab.
//
// PROCEDURAL RATHER THAN MODELLED, and that is the whole reason this file exists. A sphere and a
// flat annulus are the two shapes with nothing to author — no silhouette, no detail, no material
// palette to sit in — so a .blend for them would be a round trip through Blender, an FBX, an import
// and a hand-wire for two numbers. The mesh is Unity's own sphere primitive; the ring is thirty
// lines of trigonometry saved once as a shared asset.
//
// IT EDITS THE PREFAB IN PLACE. Unlike JetpackBuilder this does NOT own SingularityWell.prefab —
// the bottle model, its collar and core markers, the particle systems and the network wiring are
// all authored there and a SaveAsPrefabAsset over a freshly built root would delete them. So the
// prefab is opened with LoadPrefabContents, the two children are added or refreshed, and it is
// written back. Re-runnable: a second run finds its own children by name and updates them.
//
// THE MATERIALS ARE SET FIELD BY FIELD, never left to the shader's defaults. A .mat freezes the
// defaults it was born with, so a material created from a shader whose transparency fields were
// never touched is an OPAQUE white ball on every machine that opens the project after this one.
using SpaceGame.Items;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.EditorTools
{
    /// <summary>Generates the singularity's horizon sphere and ring, and wires them to the shell.</summary>
    public static class SingularityBuilder
    {
        private const string WellPrefabPath =
            "Assets/Game/Prefabs/Items/Artifacts/Gadgets/SingularityWell.prefab";

        private const string RingMeshPath =
            "Assets/Game/Art/Models/Generated/SingularityRing.mesh";

        private const string HorizonMaterialPath =
            "Assets/Game/Art/Materials/Items/SingularityHorizon.mat";

        private const string RingMaterialPath =
            "Assets/Game/Art/Materials/Items/SingularityRing.mat";

        private const string UnlitShader = "Universal Render Pipeline/Unlit";

        /// <summary>The child names this builder owns. Anything else on the prefab is left alone.</summary>
        private const string HorizonName = "Horizon";
        private const string RingName = "HorizonRing";

        /// <summary>
        /// How much of the ring's outer radius is hole. Saturn's proportions rather than a
        /// hoop's — a thin band reads as a ring, a thick one reads as a disc with a dot missing.
        /// </summary>
        private const float RingInnerShare = 0.68f;

        /// <summary>Segments around the ring. Sixty-four is smooth at the size this is ever seen at.</summary>
        private const int RingSegments = 64;

        [MenuItem("Tools/SpaceGame/Items/Build Singularity Horizon")]
        public static void Build()
        {
            Mesh ringMesh = EnsureRingMesh();
            Material horizonMaterial = EnsureHorizonMaterial();
            Material ringMaterial = EnsureRingMaterial();

            if (ringMesh == null || horizonMaterial == null || ringMaterial == null) return;

            GameObject root = PrefabUtility.LoadPrefabContents(WellPrefabPath);
            if (root == null)
            {
                Debug.LogError($"[Singularity] No prefab at {WellPrefabPath}, so the horizon has " +
                               "nothing to be built onto.");
                return;
            }

            try
            {
                var shell = root.GetComponentInChildren<SingularityShell>(true);
                if (shell == null)
                {
                    Debug.LogError("[Singularity] The well prefab has no SingularityShell, so the " +
                                   "horizon would be built and never driven.");
                    return;
                }

                float mouthHeight = MouthHeightOf(root);

                Transform sphere = EnsurePart(root.transform, HorizonName, SphereMesh(),
                                              horizonMaterial, mouthHeight);
                Transform ring = EnsurePart(root.transform, RingName, ringMesh,
                                            ringMaterial, mouthHeight);

                Wire(shell, sphere, ring);

                PrefabUtility.SaveAsPrefabAsset(root, WellPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Singularity] Horizon built: white sphere and black ring wired to the shell.");
        }

        /// <summary>
        /// The mouth's height above the bottle, read off the well rather than repeated here.
        ///
        /// The horizon is centred on the point the pull is measured from, and that point is
        /// <c>SingularityWell.mouthHeight</c> up from where the bottle sits. A second copy of that
        /// number here is a sphere drawn a hand's breadth away from the field it is describing.
        /// </summary>
        private static float MouthHeightOf(GameObject root)
        {
            var well = root.GetComponent<SingularityWell>();
            if (well == null) return 0f;

            return new SerializedObject(well).FindProperty("mouthHeight").floatValue;
        }

        /// <summary>Unity's own unit sphere — diameter 1, which is the contract the shell scales against.</summary>
        private static Mesh SphereMesh()
        {
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh mesh = probe.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(probe);

            return mesh;
        }

        /// <summary>
        /// Add or refresh one drawn child: mesh, material, and nothing else.
        ///
        /// <para>
        /// No collider, ever. The horizon is a picture of a field, and a field is not a surface —
        /// a sphere the player could stand on would also stop the bottle's own landing trace, block
        /// every aim ray in an eight-metre bubble, and catch the pull it is drawing.
        /// </para>
        /// <para>
        /// Shadows off both ways. A transparent ball casting a hard shadow across the dunes reads
        /// as a solid one, and the ring would lay a black stripe on the sand that has nothing to do
        /// with where the effect reaches.
        /// </para>
        /// </summary>
        private static Transform EnsurePart(Transform parent, string name, Mesh mesh,
                                            Material material, float mouthHeight)
        {
            Transform existing = parent.Find(name);

            GameObject part = existing != null ? existing.gameObject : new GameObject(name);
            if (existing == null) part.transform.SetParent(parent, worldPositionStays: false);

            part.transform.localPosition = Vector3.up * mouthHeight;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = Vector3.one;

            // Explicit null checks, never ??. A GetComponent that finds nothing returns a
            // destroyed-object stand-in that compares equal to null through Unity's own operator
            // and is NOT null to C#, so ?? hands back the stand-in and the next line throws
            // MissingComponentException on an object the builder just made.
            MeshFilter filter = part.GetComponent<MeshFilter>();
            if (filter == null) filter = part.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Off on the prefab, because a bottle that has not opened yet must not be wearing an
            // eight-metre ball. The shell switches them on the frame it has a radius to draw.
            part.SetActive(false);

            return part.transform;
        }

        private static void Wire(SingularityShell shell, Transform sphere, Transform ring)
        {
            var serialized = new SerializedObject(shell);

            serialized.FindProperty("sphere").objectReferenceValue = sphere;
            serialized.FindProperty("sphereRenderer").objectReferenceValue =
                sphere.GetComponent<MeshRenderer>();
            serialized.FindProperty("ring").objectReferenceValue = ring;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// A flat annulus in the XZ plane, outer radius 0.5.
        ///
        /// <para>
        /// Outer radius 0.5 and not 1, so that a scale of one draws a ring one metre ACROSS — the
        /// same diameter-is-the-scale contract Unity's own sphere primitive uses, which is what
        /// lets <c>SingularityShell</c> drive both from one number without a conversion per shape.
        /// </para>
        /// <para>
        /// One set of triangles rather than two: the material draws both faces
        /// (<see cref="EnsureRingMaterial"/>), so a second wound-backwards copy would double the
        /// geometry to fix something the cull mode already fixes.
        /// </para>
        /// </summary>
        private static Mesh EnsureRingMesh()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(RingMeshPath);
            if (existing != null) return existing;

            var vertices = new Vector3[RingSegments * 2];
            var normals = new Vector3[RingSegments * 2];
            var uvs = new Vector2[RingSegments * 2];
            var triangles = new int[RingSegments * 6];

            const float outer = 0.5f;
            const float inner = outer * RingInnerShare;

            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i / (float)RingSegments * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                vertices[i * 2] = new Vector3(cos * inner, 0f, sin * inner);
                vertices[i * 2 + 1] = new Vector3(cos * outer, 0f, sin * outer);

                normals[i * 2] = Vector3.up;
                normals[i * 2 + 1] = Vector3.up;

                uvs[i * 2] = new Vector2(i / (float)RingSegments, 0f);
                uvs[i * 2 + 1] = new Vector2(i / (float)RingSegments, 1f);

                int next = (i + 1) % RingSegments;

                triangles[i * 6] = i * 2;
                triangles[i * 6 + 1] = i * 2 + 1;
                triangles[i * 6 + 2] = next * 2 + 1;

                triangles[i * 6 + 3] = i * 2;
                triangles[i * 6 + 4] = next * 2 + 1;
                triangles[i * 6 + 5] = next * 2;
            }

            var mesh = new Mesh { name = "SingularityRing" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RingMeshPath));
            AssetDatabase.CreateAsset(mesh, RingMeshPath);

            return mesh;
        }

        /// <summary>
        /// The horizon's material: unlit, transparent, two-sided, and writing no depth.
        ///
        /// <para>
        /// <b>Unlit on purpose.</b> A lit ball would be shaded by the desert sun, so the side away
        /// from it would be dark — and "dark" is the one thing this surface says when it is
        /// closing. The colour has to mean the phase and nothing else, so nothing else may write it.
        /// </para>
        /// <para>
        /// <b>Two-sided and depth-writeless</b> because the player is routinely INSIDE it: the
        /// pull's whole joke is that the thrower gets caught too, and a single-sided sphere that
        /// writes depth vanishes the moment the camera crosses its surface and hides everything
        /// behind it while it does.
        /// </para>
        /// </summary>
        private static Material EnsureHorizonMaterial()
        {
            Material material = EnsureMaterial(HorizonMaterialPath);
            if (material == null) return null;

            material.SetFloat("_Surface", 1f);                        // Transparent
            material.SetFloat("_Blend", 0f);                          // Alpha
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_AlphaClip", 0f);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");

            material.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.35f));
            material.renderQueue = (int)RenderQueue.Transparent;

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// The ring's material: unlit, opaque, pure black, two-sided.
        ///
        /// Completely black is the whole brief, and unlit is the only way to get it — a lit black
        /// surface picks up the sky's ambient and comes out charcoal.
        /// </summary>
        private static Material EnsureRingMaterial()
        {
            Material material = EnsureMaterial(RingMaterialPath);
            if (material == null) return null;

            material.SetFloat("_Surface", 0f);                        // Opaque
            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_AlphaClip", 0f);

            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");

            material.SetColor("_BaseColor", Color.black);
            material.renderQueue = (int)RenderQueue.Geometry;

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureMaterial(string path)
        {
            Shader shader = Shader.Find(UnlitShader);
            if (shader == null)
            {
                Debug.LogError($"[Singularity] Shader '{UnlitShader}' not found, so the horizon " +
                               "would be drawn magenta. Is the project still on URP?");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            return material;
        }
    }
}
