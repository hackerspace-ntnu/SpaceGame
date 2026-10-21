using System;
using System.Collections.Generic;
using SpaceGame.Presentation;
using SpaceGame.World.Posters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SpaceGame.EditorTools.Posters
{
    /// <summary>Which ink a material prints in.</summary>
    internal enum InkRole
    {
        /// <summary>The character's own body: the slate spot colour.</summary>
        Skin,
        /// <summary>Dyed, warm cloth: the poster's accent colour.</summary>
        Accent,
        /// <summary>Everything else: the wash (cream tinted toward the accent), dense dots in shadow.</summary>
        Neutral,
        /// <summary>Kept in full colour.</summary>
        Eye,
    }

    /// <summary>Three aligned renders of one subject, top row first.</summary>
    internal sealed class PortraitPasses
    {
        public int Width;
        public int Height;
        /// <summary>The lit render, sRGB.</summary>
        public PosterImage Shade;
        /// <summary>Per pixel, the index into <see cref="Roles"/> of the material drawn there, or -1.</summary>
        public int[] Material;
        public InkRole[] Roles;
        /// <summary>View-space normals.</summary>
        public Vector3[] Normal;
        /// <summary>Distance from the camera plane in metres; 0 where nothing was drawn.</summary>
        public float[] Depth;
        /// <summary>The subject's full height in metres, hat and all.</summary>
        public float SubjectHeight;
        /// <summary>Metres per pixel of the render.</summary>
        public float MetresPerPixel;
    }

    /// <summary>
    /// Renders a character prefab front-on in an isolated preview scene: once lit, once as
    /// material ids, once as normals and depth. Nothing is saved and the open scenes are not touched.
    /// </summary>
    internal static class PortraitCapture
    {
        public const string ShaderPath = "Assets/Game/Editor/Posters/PortraitPasses.shader";
        private const float CameraDistance = 10f;
        private const string EyeShaderMarker = "Eye";

        private static readonly int PassModeId = Shader.PropertyToID("_PassMode");
        private static readonly int PassIdId = Shader.PropertyToID("_PassId");

        public static PortraitPasses Capture(WantedPoster poster, WantedPosterStyle style)
        {
            if (poster.Agent == null)
                throw new InvalidOperationException($"[Posters] '{poster.name}' has no agent to draw.");

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) throw new InvalidOperationException($"[Posters] Missing {ShaderPath}.");

            Scene scene = EditorSceneManager.NewPreviewScene();
            var temporary = new List<Object>();
            try
            {
                GameObject subject = Place(poster.Agent, scene);

                List<Renderer> renderers = NearestRenderers(subject);
                if (renderers.Count == 0)
                    throw new InvalidOperationException($"[Posters] '{poster.Agent.name}' has nothing to render.");

                Bounds bounds = renderers[0].bounds;
                foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);

                float height = bounds.size.y;
                float span = poster.PortraitSpan * height;
                float top = bounds.max.y + poster.Headroom * height;
                int width = style.renderWidth;
                int pixelsHigh = Mathf.RoundToInt(width * style.portraitAspect);

                Camera camera = CreateCamera(scene,
                    new Vector3(bounds.center.x, top - span * 0.5f, bounds.center.z), span, (float)width / pixelsHigh);
                CreateLights(scene, style);

                var passes = new PortraitPasses
                {
                    Width = width,
                    Height = pixelsHigh,
                    SubjectHeight = height,
                    MetresPerPixel = span / pixelsHigh,
                };

                passes.Shade = PosterImage.FromBottomUp(
                    Render(camera, width, pixelsHigh, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, TextureFormat.RGBA32, false, warmUp: true),
                    width, pixelsHigh);

                var slots = new MaterialSlots(renderers);
                Material dataMaterial = Temporary(new Material(shader), temporary);
                dataMaterial.SetFloat(PassModeId, 0f);

                var idMaterials = new Material[slots.Count];
                for (int i = 0; i < slots.Count; i++)
                {
                    idMaterials[i] = Temporary(new Material(shader), temporary);
                    idMaterials[i].SetFloat(PassModeId, 1f);
                    idMaterials[i].SetVector(PassIdId, EncodeId(i));
                }

                slots.Apply(index => idMaterials[index]);
                Color[] ids = Render(camera, width, pixelsHigh, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear, TextureFormat.RGBA32, true);

                slots.Apply(_ => dataMaterial);
                Color[] data = Render(camera, width, pixelsHigh, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear, TextureFormat.RGBAFloat, true);

                Unpack(passes, ids, data);
                passes.Roles = slots.Classify(passes, style);
                return passes;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (Object o in temporary) Object.DestroyImmediate(o);
            }
        }

        /// <summary>A copy of the agent standing at the origin of the preview scene: a prefab as an instance, a scene character as a clone.</summary>
        private static GameObject Place(GameObject agent, Scene scene)
        {
            GameObject copy;
            if (PrefabUtility.IsPartOfPrefabAsset(agent))
            {
                copy = (GameObject)PrefabUtility.InstantiatePrefab(agent, scene);
            }
            else
            {
                copy = Object.Instantiate(agent);
                SceneManager.MoveGameObjectToScene(copy, scene);
            }

            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return copy;
        }

        /// <summary>
        /// Only the nearest LOD of every LODGroup draws, as it would up close in game. A renderer
        /// listed in several levels (a Raxy's eyes are in all three) belongs to the nearest.
        /// </summary>
        private static List<Renderer> NearestRenderers(GameObject subject)
        {
            var hidden = new HashSet<Renderer>();
            foreach (LODGroup group in subject.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = group.GetLODs();
                var nearest = lods.Length > 0 ? new HashSet<Renderer>(lods[0].renderers) : new HashSet<Renderer>();
                for (int i = 1; i < lods.Length; i++)
                    foreach (Renderer r in lods[i].renderers)
                        if (r != null && !nearest.Contains(r)) hidden.Add(r);
                group.enabled = false;
            }

            var result = new List<Renderer>();
            foreach (Renderer r in subject.GetComponentsInChildren<Renderer>())
            {
                if (hidden.Contains(r))
                {
                    r.enabled = false;
                    continue;
                }

                if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)) result.Add(r);
                else r.enabled = false;
            }

            return result;
        }

        private static Camera CreateCamera(Scene scene, Vector3 centre, float span, float aspect)
        {
            var go = new GameObject("PosterCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(go, scene);
            var camera = go.GetComponent<Camera>();
            camera.scene = scene;
            camera.orthographic = true;
            camera.orthographicSize = span * 0.5f;
            camera.aspect = aspect;
            // Characters face +Z; the camera stands in front of them and looks back.
            camera.transform.SetPositionAndRotation(centre + Vector3.forward * CameraDistance, Quaternion.LookRotation(Vector3.back));
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = CameraDistance * 2f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.allowMSAA = false;
            camera.allowHDR = false;
            camera.enabled = false;
            return camera;
        }

        private static void CreateLights(Scene scene, WantedPosterStyle style)
        {
            // Key from the front, left and above; rim from behind, right and above.
            Light("PosterKey", scene, style.keyLight, new Vector3(1f, -1.2f, -1.5f));
            Light("PosterRim", scene, style.rimLight, new Vector3(-0.6f, -0.8f, 1f));
        }

        private static void Light(string name, Scene scene, float intensity, Vector3 travel)
        {
            var go = new GameObject(name, typeof(Light));
            SceneManager.MoveGameObjectToScene(go, scene);
            var light = go.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            go.transform.rotation = Quaternion.LookRotation(travel.normalized);
        }

        private static Color[] Render(Camera camera, int width, int height, RenderTextureFormat format,
            RenderTextureReadWrite readWrite, TextureFormat readFormat, bool linear, bool warmUp = false)
        {
            var rt = new RenderTexture(width, height, 24, format, readWrite) { antiAliasing = 1 };
            var read = new Texture2D(width, height, readFormat, false, linear);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                // The first render after an import can be Unity's placeholder (INVARIANTS.md).
                if (warmUp) camera.Render();
                camera.Render();
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                read.Apply();
                return read.GetPixels();
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(read);
            }
        }

        private static void Unpack(PortraitPasses passes, Color[] idsBottomUp, Color[] dataBottomUp)
        {
            int w = passes.Width, h = passes.Height;
            passes.Material = new int[w * h];
            passes.Normal = new Vector3[w * h];
            passes.Depth = new float[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int source = (h - 1 - y) * w + x;
                int target = y * w + x;
                passes.Material[target] = DecodeId(idsBottomUp[source]);
                Color d = dataBottomUp[source];
                passes.Normal[target] = new Vector3(d.r * 2f - 1f, d.g * 2f - 1f, d.b * 2f - 1f);
                passes.Depth[target] = d.a;
            }
        }

        // Ids are written as 8-bit channels, offset by one so a cleared pixel (0) reads as empty.
        private static Vector4 EncodeId(int index)
        {
            int value = index + 1;
            return new Vector4((value & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f, 0f, 1f);
        }

        private static int DecodeId(Color c)
        {
            if (c.a < 0.5f) return -1;
            int value = Mathf.RoundToInt(c.r * 255f) + (Mathf.RoundToInt(c.g * 255f) << 8);
            return value - 1;
        }

        private static T Temporary<T>(T o, List<Object> temporary) where T : Object
        {
            o.hideFlags = HideFlags.HideAndDontSave;
            temporary.Add(o);
            return o;
        }

        /// <summary>Every distinct material on the subject, and which renderers draw it as body.</summary>
        private sealed class MaterialSlots
        {
            private readonly List<Renderer> renderers;
            private readonly List<Material> materials = new();
            private readonly Dictionary<Material, int> index = new();
            private readonly HashSet<int> body = new();
            private readonly int[][] slotIndex;

            public int Count => materials.Count;

            public MaterialSlots(List<Renderer> renderers)
            {
                this.renderers = renderers;
                slotIndex = new int[renderers.Count][];
                for (int r = 0; r < renderers.Count; r++)
                {
                    Renderer renderer = renderers[r];
                    bool isBody = renderer is SkinnedMeshRenderer &&
                                  renderer.GetComponentInParent<SkinnedGarment>(true) == null;
                    Material[] shared = renderer.sharedMaterials;
                    slotIndex[r] = new int[shared.Length];
                    for (int s = 0; s < shared.Length; s++)
                    {
                        Material m = shared[s];
                        if (m == null)
                        {
                            slotIndex[r][s] = -1;
                            continue;
                        }

                        if (!index.TryGetValue(m, out int i))
                        {
                            i = materials.Count;
                            materials.Add(m);
                            index.Add(m, i);
                        }

                        slotIndex[r][s] = i;
                        if (isBody) body.Add(i);
                    }
                }
            }

            /// <summary>Swaps every renderer on the instance to the material <paramref name="pick"/> gives each slot.</summary>
            public void Apply(Func<int, Material> pick)
            {
                for (int r = 0; r < renderers.Count; r++)
                {
                    var swapped = new Material[slotIndex[r].Length];
                    for (int s = 0; s < swapped.Length; s++)
                        swapped[s] = slotIndex[r][s] >= 0 ? pick(slotIndex[r][s]) : null;
                    renderers[r].sharedMaterials = swapped;
                }
            }

            /// <summary>
            /// Eyes by shader; skin is the body material that covers the most of the portrait; every
            /// other material is warm (accent) or not (neutral) by its rendered colour.
            /// </summary>
            public InkRole[] Classify(PortraitPasses passes, WantedPosterStyle style)
            {
                var counts = new int[materials.Count];
                var sums = new Color[materials.Count];
                for (int i = 0; i < passes.Material.Length; i++)
                {
                    int m = passes.Material[i];
                    if (m < 0) continue;
                    counts[m]++;
                    sums[m] += passes.Shade.Pixels[i];
                }

                int skin = -1;
                foreach (int m in body)
                    if (!IsEye(materials[m]) && (skin < 0 || counts[m] > counts[skin])) skin = m;

                var roles = new InkRole[materials.Count];
                for (int m = 0; m < materials.Count; m++)
                {
                    if (IsEye(materials[m])) roles[m] = InkRole.Eye;
                    else if (m == skin) roles[m] = InkRole.Skin;
                    else roles[m] = IsWarm(counts[m] > 0 ? sums[m] / counts[m] : Color.black, style) ? InkRole.Accent : InkRole.Neutral;
                }

                return roles;
            }

            private static bool IsEye(Material m) => m.shader != null && m.shader.name.Contains(EyeShaderMarker);

            private static bool IsWarm(Color c, WantedPosterStyle style)
            {
                float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                float saturation = max > 0f ? (max - min) / max : 0f;
                return c.r > c.b * style.warmRatio && saturation > style.warmSaturation;
            }
        }
    }
}
