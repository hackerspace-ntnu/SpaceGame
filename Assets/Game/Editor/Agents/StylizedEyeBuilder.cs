using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Bakes the stylized eye look -- pupil, iris, limbal ring and painted highlight -- into an
    /// equirectangular texture per style, and wraps each in a material on
    /// <see cref="EyeShaderName"/>.
    ///
    /// <para>
    /// The eyes on the sculpt-base characters are plain spheres, so the whole look can live in the
    /// albedo: no second UV set, no per-character texture. One set of materials serves every
    /// character that has spherical eyes, and swapping a character's eye colour is swapping which
    /// of these <c>.mat</c> files its eye slots point at. The shader lights the map as URP/Lit did;
    /// what it adds is the eyelids <c>EyeBlink</c> closes, painted over the same ball.
    /// </para>
    ///
    /// <para>
    /// The cost of baking into a texture is that an eye cannot be re-coloured in the Inspector --
    /// the colours are in the pixels. That is why a look is an <see cref="EyeStyle"/> ASSET rather
    /// than a table in here: tune it against the live preview, bake, and the texture and material
    /// are rewritten at the same paths so every reference to them survives.
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

        /// <summary>
        /// The shader every eye material is on. It is also how an eye is recognised on a finished
        /// character -- see <see cref="WearsEyeShader"/>.
        /// </summary>
        public const string EyeShaderName = "SpaceGame/Characters/StylizedEye";

        /// <summary>
        /// True for a renderer that is one of these eyes. Asked of the material's shader rather than
        /// of the object's name: the importer calls the eyes <c>Sphere</c> / <c>Sphere.001</c>, and
        /// the <c>_eyes</c> material name the FBX arrives with is replaced as soon as a style is
        /// assigned.
        /// </summary>
        public static bool WearsEyeShader(Renderer renderer)
        {
            var material = renderer.sharedMaterial;
            return material != null && material.shader != null && material.shader.name == EyeShaderName;
        }

        // Equirectangular, so twice as wide as tall. Only the front cap is ever seen, but the
        // sphere carries the whole map, and 1024 across leaves the iris about 110 px wide.
        private const int Width = 1024;
        private const int Height = 512;

        /// <summary>Floor on a shape's half-width, so an Aspect of nearly 0 cannot divide by 0.</summary>
        private const float MinHalfSize = 0.05f;

        /// <summary>Floor on the superellipse exponent, for the same reason.</summary>
        private const float MinRoundness = 0.05f;

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

        /// <summary>Where the seeded style assets are written. Yours may live anywhere.</summary>
        private const string StyleFolder = "Assets/Game/ScriptableObjects/Eyes";

        /// <summary>
        /// Every <see cref="EyeStyle"/> in the project, wherever it was filed. Searching the whole
        /// project rather than one folder is deliberate: a style is authored art, and the person
        /// authoring it should be able to keep it next to whatever it belongs to.
        /// </summary>
        public static EyeStyle[] LoadStyles()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(EyeStyle));
            var styles = new EyeStyle[guids.Length];
            for (int i = 0; i < guids.Length; i++)
                styles[i] = AssetDatabase.LoadAssetAtPath<EyeStyle>(AssetDatabase.GUIDToAssetPath(guids[i]));

            Array.Sort(styles, (a, b) => string.CompareOrdinal(a.name, b.name));
            return styles;
        }

        [MenuItem("Tools/SpaceGame/Art/Build Stylized Eye Materials")]
        public static void BuildAll()
        {
            EnsureFolder(MaterialFolder);
            EnsureFolder(TextureFolder);

            var styles = LoadStyles();
            if (styles.Length == 0)
            {
                EyeStylePresets.CreateMissing();
                styles = LoadStyles();
            }

            foreach (var style in styles)
                Build(style);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[StylizedEyeBuilder] Baked " + styles.Length + " eye styles into " + MaterialFolder + ".");
        }

        /// <summary>
        /// Re-creates any built-in style whose asset is missing. It never overwrites one that is
        /// already there -- these are starting points to duplicate and tune, and clobbering someone's
        /// edits because they happened to reuse a name would be the worst thing this could do.
        /// </summary>
        [MenuItem("Tools/SpaceGame/Art/Create Built-in Eye Styles")]
        public static void CreateBuiltInStyles()
        {
            int made = EyeStylePresets.CreateMissing();
            Debug.Log(made == 0
                ? "[StylizedEyeBuilder] Every built-in style already exists in " + StyleFolder + "."
                : "[StylizedEyeBuilder] Created " + made + " built-in eye style(s) in " + StyleFolder + ".");
        }

        /// <summary>The folder new styles are seeded into.</summary>
        public static string SeedFolder => StyleFolder;

        /// <summary>The material for a style by asset name, baked if it is not on disk yet.</summary>
        public static Material Load(string styleName)
        {
            string path = MaterialPath(styleName);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            var styles = LoadStyles();
            var style = Array.Find(styles, s => s.name == styleName);
            if (style == null)
            {
                EyeStylePresets.CreateMissing();
                styles = LoadStyles();
                style = Array.Find(styles, s => s.name == styleName);
            }

            if (style == null)
            {
                Debug.LogError("[StylizedEyeBuilder] No eye style asset named '" + styleName +
                               "'. Known: " + (styles.Length == 0
                                   ? "(none -- run Tools > SpaceGame > Art > Create Built-in Eye Styles)"
                                   : string.Join(", ", Array.ConvertAll(styles, s => s.name))));
                return null;
            }

            return Build(style);
        }

        /// <summary>Bakes one style's textures and writes its material, in place.</summary>
        public static Material Build(EyeStyle style)
        {
            if (style == null)
            {
                Debug.LogError("[StylizedEyeBuilder] Asked to bake a null style.");
                return null;
            }

            var shader = Shader.Find(EyeShaderName);
            if (shader == null)
            {
                Debug.LogError("[StylizedEyeBuilder] Shader '" + EyeShaderName + "' is missing or failed " +
                               "to compile, so '" + style.name + "' was not baked.");
                return null;
            }

            EnsureFolder(MaterialFolder);
            EnsureFolder(TextureFolder);

            var albedo = WriteTexture(style.name + "_BaseColor", Bake(style, emission: false), sRGB: true);

            Texture2D emission = style.EmissionStrength > 0f
                ? WriteTexture(style.name + "_Emission", Bake(style, emission: true), sRGB: true)
                : null;

            string path = MaterialPath(style.name);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            // Set on an existing material too: that is how the ones first written on URP/Lit move
            // over. Every property below has the same name on both shaders, so none of it is lost.
            material.shader = shader;
            material.SetTexture("_BaseMap", albedo);
            material.mainTexture = albedo;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", style.Smoothness);

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

                // A style that used to glow leaves its emission PNG behind otherwise, and the next
                // reader of the folder cannot tell it from a live one.
                string stale = TextureFolder + "/" + AssetPrefix + style.name + "_Emission.png";
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(stale) != null)
                    AssetDatabase.DeleteAsset(stale);
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

        /// <summary>
        /// The colour of one point on the eyeball. Shared by the bake and by the Inspector preview,
        /// so what the author tunes is literally what gets written.
        /// </summary>
        /// <param name="direction">A unit direction in the map's frame -- see <see cref="Gaze"/>.</param>
        /// <param name="emission">Paint the emission map rather than the albedo.</param>
        public static Color Shade(EyeStyle style, Vector3 direction, bool emission)
        {
            Vector2 plane = EyePlane(direction);
            float softness = style.EdgeSoftness;

            float irisDistance = EdgeDistance(plane, style.Iris);
            float irisReach = Reach(style.Iris);

            // The gradient runs from the iris centre out to where the limbal ring starts, so the
            // ring reads as a band laid ON the gradient rather than as the gradient's own dark end.
            float limbal = Mathf.Min(style.LimbalWidth, irisReach);

            // The gradient spans the VISIBLE band of iris -- from the pupil's edge out to where the
            // limbal ring starts -- not the whole disc from the centre. Running it from the centre
            // wastes most of it under the pupil and leaves the ring you can actually see painted in
            // IrisOuter, which merges with the pupil and reads as a swollen, warped eye.
            float fromCentre = irisDistance + irisReach;
            float t = Mathf.InverseLerp(Reach(style.PupilShape), irisReach - limbal, fromCentre);
            Color iris = Color.Lerp(style.IrisInner, style.IrisOuter, t);

            float ringMask = Band(irisDistance + limbal, softness);
            float scleraMask = Band(irisDistance, softness);
            float pupilMask = Band(EdgeDistance(plane, style.PupilShape), softness);

            Color colour = Color.Lerp(iris, style.LimbalRing, ringMask);
            colour = Color.Lerp(colour, style.Sclera, scleraMask);
            colour = Color.Lerp(style.Pupil, colour, pupilMask);

            if (emission)
            {
                // The albedo times a mask, rather than a colour of its own: a glow that does not
                // agree with what is painted underneath reads as a second, misaligned eye.
                float lit;
                switch (style.EmissionArea)
                {
                    case EyeEmissionArea.WholeEye:
                        lit = 1f;
                        break;
                    case EyeEmissionArea.IrisAndPupil:
                        lit = 1f - scleraMask;
                        break;
                    default:
                        lit = (1f - ringMask) * pupilMask;
                        break;
                }

                return colour * lit;
            }

            // Painter's order, so a later catchlight covers an earlier one. Max() would be cheaper
            // and cannot express two glints of different colours overlapping.
            if (style.Catchlights != null)
            {
                foreach (var light in style.Catchlights)
                {
                    float coverage = (1f - Band(EdgeDistance(plane, light.Shape), softness)) * light.Strength;
                    if (coverage > 0f)
                        colour = Color.Lerp(colour, light.Colour, coverage);
                }
            }

            return colour;
        }

        /// <summary>
        /// A direction from a point on the face of the ball as the Inspector preview sees it:
        /// <paramref name="sx"/> right, <paramref name="sy"/> up, both -1..1 across the disc, and
        /// <paramref name="sz"/> the height of the sphere over that point.
        /// </summary>
        public static Vector3 PreviewDirection(float sx, float sy, float sz) =>
            sx * Side + sy * Up + sz * Gaze;

        /// <summary>
        /// Flattens a direction into the eye plane: how many degrees out from the gaze, in which
        /// direction around it. x runs along <see cref="Side"/>, y along <see cref="Up"/>, both in
        /// degrees.
        ///
        /// <para>
        /// This is an azimuthal-equidistant projection about the gaze, which is why a shape written
        /// here stays the shape it was drawn as: distance from the centre is the angle itself, so a
        /// circle is a circle rather than something the map's stretching decides.
        /// </para>
        /// </summary>
        private static Vector2 EyePlane(Vector3 direction)
        {
            float forward = Mathf.Clamp(Vector3.Dot(direction, Gaze), -1f, 1f);
            float theta = Mathf.Rad2Deg * Mathf.Acos(forward);

            float up = Vector3.Dot(direction, Up);
            float side = Vector3.Dot(direction, Side);
            float lateral = Mathf.Sqrt(up * up + side * side);

            // Dead on the gaze there is no direction to point in, and the whole plane is the origin.
            if (lateral < 1e-6f) return Vector2.zero;

            float scale = theta / lateral;
            return new Vector2(side * scale, up * scale);
        }

        /// <summary>
        /// How far outside <paramref name="shape"/> a point in the eye plane lies, in degrees --
        /// negative inside, 0 on the outline. Every band in <see cref="Shade"/> is a soft step on
        /// this one number, which is what lets a slit pupil and a round iris share all the same code.
        /// </summary>
        private static float EdgeDistance(Vector2 plane, EyeShape shape)
        {
            // Size 0 switches a shape off -- a pupil-less eye, a style with one catchlight instead
            // of two. Returning "very far outside" is what makes that mean nothing is drawn.
            if (shape.Size <= 0f) return float.MaxValue;

            float halfUp = shape.Size;
            float halfSide = Mathf.Max(shape.Size * shape.Aspect, MinHalfSize);
            float power = Mathf.Max(shape.Roundness, MinRoundness);

            float x = plane.x - shape.OffsetSide;
            float y = plane.y - shape.OffsetUp;

            float turn = shape.Rotation * Mathf.Deg2Rad;
            float cos = Mathf.Cos(turn);
            float sin = Mathf.Sin(turn);
            float alongSide = x * cos + y * sin;
            float alongUp = -x * sin + y * cos;

            float radius = Mathf.Pow(
                Mathf.Pow(Mathf.Abs(alongSide) / halfSide, power) +
                Mathf.Pow(Mathf.Abs(alongUp) / halfUp, power),
                1f / power);

            // Scaled by the NARROW half-width, so the softness of a thin slit's long sides matches
            // the softness of its ends instead of smearing the whole thing away.
            return (radius - 1f) * Mathf.Min(halfUp, halfSide);
        }

        /// <summary>How far it is from a shape's centre to its outline at the narrowest, in degrees.</summary>
        private static float Reach(EyeShape shape) =>
            Mathf.Max(Mathf.Min(shape.Size, shape.Size * shape.Aspect), MinHalfSize);

        /// <summary>0 inside the outline, 1 outside, soft across the seam.</summary>
        private static float Band(float distance, float softness)
        {
            if (softness <= 0f) return distance > 0f ? 1f : 0f;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-softness, softness, distance));
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

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
