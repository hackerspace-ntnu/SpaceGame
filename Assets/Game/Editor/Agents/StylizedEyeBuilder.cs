using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Bakes the stylized eye look -- pupil, iris, limbal ring and painted highlight -- into an
    /// equirectangular texture per style, and wraps each in a URP/Lit material.
    ///
    /// <para>
    /// The eyes on the sculpt-base characters are plain spheres, so the whole look can live in the
    /// albedo: no eye shader, no second UV set, no per-character texture. One set of materials
    /// serves every character that has spherical eyes, and swapping a character's eye colour is
    /// swapping which of these <c>.mat</c> files its eye slots point at.
    /// </para>
    ///
    /// <para>
    /// The cost of baking into a texture is that an eye cannot be re-coloured in the Inspector --
    /// the colours are in the pixels. That is why the styles below are data: edit a
    /// <see cref="EyeStyle"/>, re-run the menu item, and the texture and material are rewritten in
    /// place so every reference to them survives.
    /// </para>
    ///
    /// <para>
    /// It also owns the other half of the deal: <see cref="EnsureEyeMesh"/> gives the eye sphere the
    /// unwrap this map is painted for, because the one the art ships with is folded and unusable.
    /// </para>
    /// </summary>
    public static class StylizedEyeBuilder
    {
        private const string MaterialFolder = "Assets/Game/Art/Materials/Characters";
        private const string TextureFolder = "Assets/Game/Art/Textures/Characters/Eyes";

        /// <summary>Prefix on every asset this builder owns, so a rebuild can find its own work.</summary>
        public const string AssetPrefix = "Eye_";

        // Equirectangular, so twice as wide as tall. Only the front cap is ever seen, but the
        // sphere carries the whole map, and 1024 across leaves the iris about 110 px wide.
        private const int Width = 1024;
        private const int Height = 512;

        /// <summary>Samples per texel per axis. The edges are already smoothstepped; this only
        /// cleans up the last of the stair-stepping on the highlight.</summary>
        private const int Supersample = 2;

        /// <summary>
        /// The frame the map is painted in: the gaze sits at the middle of the texture, uv(0.5, 0.5),
        /// with v rising toward <see cref="Up"/> and u rising toward <see cref="Side"/>.
        ///
        /// <para>
        /// These are arbitrary axes, not the eye's. Nothing reads the eye mesh's own orientation,
        /// because <see cref="EnsureEyeMesh"/> writes the UVs itself from the gaze it is handed --
        /// the two halves only have to agree with each other, and they agree by both using
        /// <see cref="Direction"/>. Putting the gaze in the middle also parks the wrap seam at
        /// u=0/1, which is the back of the eyeball, inside the head.
        /// </para>
        /// </summary>
        private static readonly Vector3 Gaze = new Vector3(0f, 0f, 1f);

        /// <summary>Up in the map. v=1 is this pole.</summary>
        private static readonly Vector3 Up = new Vector3(0f, 1f, 0f);

        /// <summary>Completes the frame. Azimuth 90 deg lies along this.</summary>
        private static readonly Vector3 Side = Vector3.Cross(Up, Gaze);

        /// <summary>
        /// One eye look. Every angle is a half-angle from the gaze direction in degrees, which is
        /// what makes the shapes round on the sphere -- a circle measured in UV space would come out
        /// stretched sideways, because u covers 360 deg over the same span v covers 180.
        /// </summary>
        public sealed class EyeStyle
        {
            public string Name;

            /// <summary>The eyeball outside the iris. Near-black reads as "all iris" and alien;
            /// an off-white reads as a human eye.</summary>
            public Color Sclera;

            /// <summary>Iris colour next to the pupil -- the bright end of the gradient.</summary>
            public Color IrisInner;

            /// <summary>Iris colour at its outer edge. Darker than <see cref="IrisInner"/> is what
            /// the "dark sides" of a stylized eye actually are.</summary>
            public Color IrisOuter;

            /// <summary>The dark band ringing the iris.</summary>
            public Color LimbalRing;

            public Color Pupil;

            /// <summary>The painted specular dot. Baked in so it reads the same under any light.</summary>
            public Color Highlight;

            public float PupilAngle;
            public float IrisAngle;

            /// <summary>Width of the limbal ring, taken inwards from <see cref="IrisAngle"/>.</summary>
            public float LimbalWidth;

            /// <summary>How soft every band edge is. Small keeps the graphic, stylized look.</summary>
            public float EdgeSoftness;

            public float HighlightAngle;

            /// <summary>Where the highlight sits around the gaze: 0 is straight up, 90 deg along
            /// <see cref="Side"/>. Both eyes share this texture and share an orientation, so one
            /// value puts the highlight in the same place on both.</summary>
            public float HighlightAzimuth;

            public float HighlightRadius;

            /// <summary>The smaller second catchlight opposite the first. Radius 0 leaves it off.</summary>
            public float SparkAngle;
            public float SparkAzimuth;
            public float SparkRadius;

            /// <summary>Iris glow. Strength 0 writes no emission map and leaves the material unlit
            /// -- which is what a human eye wants.</summary>
            public Color Emission;
            public float EmissionStrength;
        }

        /// <summary>
        /// Shared defaults, so a style below only states what makes it that style. The angles are
        /// sized against the visible cap of the eyeball: the lids crop it to roughly 50 deg of the
        /// sphere, so a 45 deg iris fills what is on show and reads large and stylized rather than
        /// human. The pupil is deliberately huge -- 25 deg of a 45 deg iris -- which is what makes
        /// these read as cartoon eyes rather than as an eyeball with a dot on it. Emission is kept
        /// well under 1 -- an iris that is both a bright albedo and an HDR emitter clips to white
        /// and the colour is gone.
        /// </summary>
        private static EyeStyle Base(string name) => new EyeStyle
        {
            Name = name,
            Sclera = Hex("120D0A"),
            Pupil = Hex("07060A"),
            LimbalRing = Hex("14090B"),
            Highlight = Hex("FFFFFF"),
            PupilAngle = 25f,
            IrisAngle = 45f,
            LimbalWidth = 6f,
            EdgeSoftness = 1.4f,
            HighlightAngle = 18f,
            HighlightAzimuth = -38f,
            HighlightRadius = 9f,
            SparkAngle = 27f,
            SparkAzimuth = 145f,
            SparkRadius = 4.5f,
            EmissionStrength = 0f,
        };

        private static EyeStyle Glowing(string name, string inner, string outer, string rim,
                                        string glow, float strength)
        {
            var style = Base(name);
            style.IrisInner = Hex(inner);
            style.IrisOuter = Hex(outer);
            style.LimbalRing = Hex(rim);
            style.Emission = Hex(glow);
            style.EmissionStrength = strength;
            return style;
        }

        public static readonly EyeStyle[] Styles = BuildStyles();

        private static EyeStyle[] BuildStyles()
        {
            var amber = Glowing("Amber", "FFA23A", "C4480A", "2B1204", "FF7A14", 0.55f);

            var ember = Glowing("Ember", "FF6A3C", "94180A", "2A0805", "FF3A12", 0.6f);

            var acid = Glowing("Acid", "B6FF5E", "2F7A14", "0E2006", "7CE01E", 0.5f);

            var glacier = Glowing("Glacier", "9FF0FF", "16679E", "061C2C", "3FC6FF", 0.5f);
            glacier.Sclera = Hex("0B1016");

            var violet = Glowing("Violet", "D49BFF", "51219A", "170728", "9A46FF", 0.5f);
            violet.Sclera = Hex("100A18");

            var gold = Glowing("Gold", "FFDC6A", "A86E06", "2A1A02", "FFB61E", 0.45f);

            // "White eyes", reading one: a white eyeball, the nearest thing here to a human eye.
            // The only style with a pale sclera, which is why its iris is a washed grey rather than
            // the white it started as -- white on white left nothing but the limbal ring visible and
            // the eye read as an empty hoop. It also pulls back from the huge pupil the rest wear.
            var ivory = Base("Ivory");
            ivory.Sclera = Hex("F1ECE2");
            ivory.IrisInner = Hex("DCE7EC");
            ivory.IrisOuter = Hex("8FA5B2");
            ivory.LimbalRing = Hex("3C4750");
            ivory.LimbalWidth = 4.5f;
            ivory.IrisAngle = 38f;
            ivory.PupilAngle = 22f;

            // "White eyes", reading two: no pupil at all, the whole eye lit blank. Pupil and iris
            // are the same white, so only the rim and the catchlight give it any shape -- and the
            // catchlight only reads because the white underneath it is held just short of full.
            var blank = Base("Blank");
            blank.Sclera = Hex("E6E6E0");
            blank.IrisInner = Hex("EDEDE8");
            blank.IrisOuter = Hex("EDEDE8");
            blank.Pupil = Hex("EDEDE8");
            blank.LimbalRing = Hex("C9C9C2");
            blank.LimbalWidth = 4f;
            blank.IrisAngle = 46f;
            blank.EdgeSoftness = 6f;
            blank.Highlight = Hex("FFFFFF");
            blank.Emission = Hex("FFFFFF");
            blank.EmissionStrength = 0.3f;

            return new[] { amber, ember, acid, glacier, violet, gold, ivory, blank };
        }

        [MenuItem("Tools/SpaceGame/Art/Build Stylized Eye Materials")]
        public static void BuildAll()
        {
            EnsureFolder(MaterialFolder);
            EnsureFolder(TextureFolder);

            foreach (var style in Styles)
                Build(style);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[StylizedEyeBuilder] Built {Styles.Length} eye materials into {MaterialFolder}.");
        }

        /// <summary>The material for a style by name, built if it is not on disk yet.</summary>
        public static Material Load(string styleName)
        {
            string path = MaterialPath(styleName);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            var style = Array.Find(Styles, s => s.Name == styleName);
            if (style == null)
            {
                Debug.LogError($"[StylizedEyeBuilder] No eye style named '{styleName}'. Known: " +
                               string.Join(", ", Array.ConvertAll(Styles, s => s.Name)));
                return null;
            }

            EnsureFolder(MaterialFolder);
            EnsureFolder(TextureFolder);
            return Build(style);
        }

        public static Material Build(EyeStyle style)
        {
            var albedo = WriteTexture(style.Name + "_BaseColor", Bake(style, emission: false), sRGB: true);

            Texture2D emission = style.EmissionStrength > 0f
                ? WriteTexture(style.Name + "_Emission", Bake(style, emission: true), sRGB: true)
                : null;

            string path = MaterialPath(style.Name);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_BaseMap", albedo);
            material.mainTexture = albedo;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);

            // Wetter than skin, but well short of a mirror: the catchlight is already painted into
            // the albedo, and a glossier eye adds a second one on top that washes the iris colour
            // out under any strong key light.
            material.SetFloat("_Smoothness", 0.55f);

            if (emission != null)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetTexture("_EmissionMap", emission);
                material.SetColor("_EmissionColor", style.Emission * style.EmissionStrength);
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                material.SetTexture("_EmissionMap", null);
                material.SetColor("_EmissionColor", Color.black);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        public static string MaterialPath(string styleName) =>
            $"{MaterialFolder}/{AssetPrefix}{styleName}.mat";

        // ------------------------------------------------------------------
        // Baking

        /// <summary>
        /// Paints one equirectangular map. Every band is decided by the angle between the texel's
        /// direction on the sphere and the gaze, so the shapes stay round however the map stretches.
        /// </summary>
        private static Color[] Bake(EyeStyle style, bool emission)
        {
            var pixels = new Color[Width * Height];
            float step = 1f / Supersample;
            float weight = 1f / (Supersample * Supersample);

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    Color sum = Color.clear;
                    for (int sy = 0; sy < Supersample; sy++)
                    {
                        for (int sx = 0; sx < Supersample; sx++)
                        {
                            float u = (x + (sx + 0.5f) * step) / Width;
                            float v = (y + (sy + 0.5f) * step) / Height;
                            sum += Shade(style, Direction(u, v), emission) * weight;
                        }
                    }

                    pixels[y * Width + x] = sum;
                }
            }

            return pixels;
        }

        /// <summary>
        /// The point on the unit sphere a texel covers, in the map's own frame. Inverted by
        /// <see cref="Unwrap"/>, which is what puts the same look on the mesh.
        /// </summary>
        private static Vector3 Direction(float u, float v)
        {
            float longitude = (u - 0.5f) * 2f * Mathf.PI;
            float latitude = (v - 0.5f) * Mathf.PI;
            float c = Mathf.Cos(latitude);
            return c * Mathf.Cos(longitude) * Gaze + c * Mathf.Sin(longitude) * Side +
                   Mathf.Sin(latitude) * Up;
        }

        /// <summary>
        /// The texel a direction lands on -- <see cref="Direction"/> run backwards, in whatever frame
        /// the caller's <paramref name="gaze"/>, <paramref name="up"/> and <paramref name="side"/>
        /// describe.
        /// </summary>
        private static Vector2 Unwrap(Vector3 direction, Vector3 gaze, Vector3 up, Vector3 side)
        {
            float longitude = Mathf.Atan2(Vector3.Dot(direction, side), Vector3.Dot(direction, gaze));
            float latitude = Mathf.Asin(Mathf.Clamp(Vector3.Dot(direction, up), -1f, 1f));
            return new Vector2(0.5f + longitude / (2f * Mathf.PI), 0.5f + latitude / Mathf.PI);
        }

        private static Color Shade(EyeStyle style, Vector3 direction, bool emission)
        {
            float theta = Mathf.Rad2Deg * Mathf.Acos(Mathf.Clamp(Vector3.Dot(direction, Gaze), -1f, 1f));

            float irisEdge = style.IrisAngle;
            float limbalEdge = Mathf.Max(style.PupilAngle, irisEdge - style.LimbalWidth);

            // Iris gradient runs from the pupil's edge to where the limbal ring starts, so the ring
            // reads as a band on top of the gradient rather than as the gradient's own dark end.
            float t = Mathf.InverseLerp(style.PupilAngle, limbalEdge, theta);
            Color iris = Color.Lerp(style.IrisInner, style.IrisOuter, t);

            Color colour = Color.Lerp(iris, style.LimbalRing, Band(theta, limbalEdge, style.EdgeSoftness));
            colour = Color.Lerp(colour, style.Sclera, Band(theta, irisEdge, style.EdgeSoftness));
            colour = Color.Lerp(style.Pupil, colour, Band(theta, style.PupilAngle, style.EdgeSoftness));

            if (emission)
            {
                // Only the iris glows: a glowing pupil erases the pupil, and a glowing sclera turns
                // the whole eyeball into a lamp.
                float lit = 1f - Band(theta, limbalEdge, style.EdgeSoftness);
                lit *= Band(theta, style.PupilAngle, style.EdgeSoftness);
                return iris * lit;
            }

            float highlight = Dot(direction, style.HighlightAngle, style.HighlightAzimuth,
                                  style.HighlightRadius, style.EdgeSoftness);
            float spark = Dot(direction, style.SparkAngle, style.SparkAzimuth,
                              style.SparkRadius, style.EdgeSoftness);

            return Color.Lerp(colour, style.Highlight, Mathf.Max(highlight, spark * 0.6f));
        }

        /// <summary>0 inside <paramref name="edge"/>, 1 outside, soft across the seam.</summary>
        private static float Band(float theta, float edge, float softness) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge - softness, edge + softness, theta));

        /// <summary>
        /// Coverage of a round catchlight centred <paramref name="angle"/> off the gaze, turned
        /// <paramref name="azimuth"/> around it from straight up.
        /// </summary>
        private static float Dot(Vector3 direction, float angle, float azimuth, float radius,
                                 float softness)
        {
            if (radius <= 0f) return 0f;

            float a = angle * Mathf.Deg2Rad;
            float z = azimuth * Mathf.Deg2Rad;
            Vector3 centre = Mathf.Cos(a) * Gaze +
                             Mathf.Sin(a) * (Mathf.Cos(z) * Up + Mathf.Sin(z) * Side);

            float theta = Mathf.Rad2Deg * Mathf.Acos(Mathf.Clamp(Vector3.Dot(direction, centre), -1f, 1f));
            return 1f - Band(theta, radius, softness);
        }

        // ------------------------------------------------------------------
        // The mesh the map is painted onto

        private const string EyeMeshFolder = "Assets/Game/Art/Models/Characters/EyeMeshes";

        /// <summary>
        /// How far off the gaze a vertex may be from the sphere it is supposed to lie on before
        /// this refuses to unwrap it, as a fraction of the sphere's radius. Generous: it is there to
        /// catch "this renderer is not an eyeball", not to grade the sculpt.
        /// </summary>
        private const float SphereTolerance = 0.2f;

        /// <summary>
        /// Gives an eye sphere a copy of its mesh with UVs that an eye map can actually be painted
        /// on, and hands it to <paramref name="eye"/>.
        ///
        /// <para>
        /// <b>Why this exists.</b> The UVs the sculpt-base eyes ship with are folded: a single
        /// longitude on the sphere carries up to FOUR different u, mirrored in pairs, so one painted
        /// pupil comes out as two or four pupils facing different ways and the eye reads as a
        /// scrambled ball. v is fine -- it is only the way round the sphere that was lost. Nothing
        /// in Unity reports this; the mesh imports clean and the material binds clean.
        /// </para>
        ///
        /// <para>
        /// The replacement is a plain equirectangular unwrap about the gaze the caller measured, so
        /// the pupil lands where <see cref="Bake"/> painted it. It writes a mesh ASSET rather than a
        /// runtime mesh because a prefab cannot reference a mesh that only exists in memory -- the
        /// eye would come back with no mesh at all next time the prefab is opened.
        /// </para>
        ///
        /// <para>
        /// The one seam is at the back of the eyeball, 180 deg from the gaze, where the triangles
        /// that straddle u=1/u=0 run the whole map backwards across a few millimetres. That is
        /// inside the head. Do NOT re-centre the map to move the seam somewhere "tidier".
        /// </para>
        /// </summary>
        /// <param name="eye">The renderer to repair. Its <c>sharedMesh</c> is left untouched.</param>
        /// <param name="gaze">Where the character looks, in the eye's own local space.</param>
        /// <param name="up">The character's up, in the eye's own local space.</param>
        /// <param name="assetName">Unique per character and per eye; names the mesh asset.</param>
        /// <returns>True if the eye came out of this with a usable unwrap.</returns>
        public static bool EnsureEyeMesh(Renderer eye, Vector3 gaze, Vector3 up, string assetName)
        {
            var source = eye is SkinnedMeshRenderer skinned
                ? skinned.sharedMesh
                : eye.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;

            if (source == null)
            {
                Debug.LogError($"[StylizedEyeBuilder] {assetName}: the eye renderer has no mesh.");
                return false;
            }

            Vector3.OrthoNormalize(ref gaze, ref up);
            Vector3 side = Vector3.Cross(up, gaze);

            var vertices = source.vertices;
            Vector3 centre = Vector3.zero;
            foreach (var vertex in vertices) centre += vertex;
            centre /= vertices.Length;

            float radius = 0f;
            foreach (var vertex in vertices) radius += (vertex - centre).magnitude;
            radius /= vertices.Length;

            float drift = 0f;
            foreach (var vertex in vertices)
                drift = Mathf.Max(drift, Mathf.Abs((vertex - centre).magnitude - radius));

            // An unwrap about a centre is only an eye map if the thing really is a ball around that
            // centre. On anything else the shapes would smear and there would be no other sign.
            if (radius <= 0f || drift / radius > SphereTolerance)
            {
                Debug.LogError($"[StylizedEyeBuilder] {assetName}: '{source.name}' is not a sphere " +
                               $"(radius {radius:F4}, worst vertex off by {drift:F4}), so it cannot " +
                               "take an equirectangular eye map. Left as it was.");
                return false;
            }

            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                uv[i] = Unwrap((vertices[i] - centre).normalized, gaze, up, side);

            var repaired = UnityEngine.Object.Instantiate(source);
            repaired.name = assetName;
            repaired.uv = uv;

            string path = $"{EyeMeshFolder}/{assetName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                EnsureFolder(EyeMeshFolder);
                AssetDatabase.CreateAsset(repaired, path);
                existing = repaired;
            }
            else
            {
                // Overwrite in place. Deleting and recreating would hand out a new GUID and null the
                // mesh on every prefab already pointing at this one.
                EditorUtility.CopySerialized(repaired, existing);
                UnityEngine.Object.DestroyImmediate(repaired);
                EditorUtility.SetDirty(existing);
            }

            if (eye is SkinnedMeshRenderer target) target.sharedMesh = existing;
            else eye.GetComponent<MeshFilter>().sharedMesh = existing;

            return true;
        }

        // ------------------------------------------------------------------
        // Assets

        private static Texture2D WriteTexture(string name, Color[] pixels, bool sRGB)
        {
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, linear: !sRGB);
            texture.SetPixels(pixels);
            texture.Apply();

            string path = $"{TextureFolder}/{AssetPrefix}{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = sRGB;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;

            // The map wraps the whole sphere, so u=1 meets u=0. Clamping would draw a hard seam
            // down the outside of the eyeball.
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Color Hex(string rgb)
        {
            int packed = int.Parse(rgb, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Color(((packed >> 16) & 0xFF) / 255f,
                             ((packed >> 8) & 0xFF) / 255f,
                             (packed & 0xFF) / 255f, 1f);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
