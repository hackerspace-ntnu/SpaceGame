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
    /// The eyes on the sculpt-base characters are plain UV spheres, so the whole look can live in
    /// the albedo: no eye shader, no second UV set, no per-character texture. One set of materials
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
    /// <para><b>The UV convention is measured, not assumed.</b> See <see cref="Gaze"/>.</para>
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
        /// Where the character is looking, in the eye mesh's own space.
        ///
        /// <para>
        /// Measured off the shipped FBX rather than assumed: all three sculpt-base eye spheres are
        /// unrotated UV spheres with the pole on local +Z, and their UVs put local +X at u=0.5,
        /// local +Y at u=0.75 and local +Z at v=1. In the authoring .blend the eye objects carry an
        /// identity rotation and the characters face +Y, so local +Y IS the gaze -- which lands the
        /// pupil at uv(0.75, 0.5). Re-measure before trusting this on a differently authored eye:
        /// a texture keyed to the wrong axis puts the pupil in the side of the head and nothing
        /// warns you.
        /// </para>
        /// </summary>
        private static readonly Vector3 Gaze = new Vector3(0f, 1f, 0f);

        /// <summary>The sphere's pole, and the up the highlight's azimuth is measured from.</summary>
        private static readonly Vector3 Up = new Vector3(0f, 0f, 1f);

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
        /// sized against the visible cap of the eyeball: roughly 50-60 deg of the sphere clears the
        /// lids, so a 38 deg iris fills most of the eye and reads large and stylized rather than
        /// human.
        /// </summary>
        private static EyeStyle Base(string name) => new EyeStyle
        {
            Name = name,
            Sclera = Hex("120D0A"),
            Pupil = Hex("07060A"),
            LimbalRing = Hex("14090B"),
            Highlight = Hex("FFFFFF"),
            PupilAngle = 12f,
            IrisAngle = 38f,
            LimbalWidth = 7f,
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
            var amber = Glowing("Amber", "FFA23A", "C4480A", "2B1204", "FF7A14", 1.6f);

            var ember = Glowing("Ember", "FF6A3C", "94180A", "2A0805", "FF3A12", 1.8f);

            var acid = Glowing("Acid", "B6FF5E", "2F7A14", "0E2006", "7CE01E", 1.5f);

            var glacier = Glowing("Glacier", "9FF0FF", "16679E", "061C2C", "3FC6FF", 1.4f);
            glacier.Sclera = Hex("0B1016");

            var violet = Glowing("Violet", "D49BFF", "51219A", "170728", "9A46FF", 1.5f);
            violet.Sclera = Hex("100A18");

            var gold = Glowing("Gold", "FFDC6A", "A86E06", "2A1A02", "FFB61E", 1.3f);

            // "White eyes", reading one: a white eyeball. The only style here with a pale sclera,
            // so it is also the only one whose iris needs a dark rim to separate it from the white.
            var ivory = Base("Ivory");
            ivory.Sclera = Hex("F1ECE2");
            ivory.IrisInner = Hex("FFFFFF");
            ivory.IrisOuter = Hex("C6C1B8");
            ivory.LimbalRing = Hex("4A4640");
            ivory.LimbalWidth = 4.5f;
            ivory.IrisAngle = 30f;
            ivory.PupilAngle = 13f;

            // "White eyes", reading two: no pupil at all, the whole eye lit blank. Pupil and iris
            // are the same white, so only the rim and the catchlight give it any shape.
            var blank = Base("Blank");
            blank.Sclera = Hex("EFEFEA");
            blank.IrisInner = Hex("FFFFFF");
            blank.IrisOuter = Hex("FFFFFF");
            blank.Pupil = Hex("FFFFFF");
            blank.LimbalRing = Hex("D2D2CC");
            blank.LimbalWidth = 3f;
            blank.IrisAngle = 44f;
            blank.EdgeSoftness = 6f;
            blank.Highlight = Hex("FFFFFF");
            blank.Emission = Hex("FFFFFF");
            blank.EmissionStrength = 0.9f;

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

            // Wetter than skin. The catchlight is painted in, so this is only the sheen that moves
            // with the light and tells the eye apart from a matte bead.
            material.SetFloat("_Smoothness", 0.75f);

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
        /// The point on the unit sphere a texel covers, in the eye mesh's own space -- the inverse
        /// of the UV sphere's own unwrap. See <see cref="Gaze"/> for where the axes come from.
        /// </summary>
        private static Vector3 Direction(float u, float v)
        {
            float longitude = (u - 0.5f) * 2f * Mathf.PI;
            float latitude = (v - 0.5f) * Mathf.PI;
            float c = Mathf.Cos(latitude);
            return new Vector3(c * Mathf.Cos(longitude), c * Mathf.Sin(longitude), Mathf.Sin(latitude));
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
