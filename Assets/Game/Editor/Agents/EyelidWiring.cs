using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Gives a character blinking eyelids made of its own skin: an <see cref="EyeBlink"/> on its
    /// root, pointed at every renderer wearing the stylized eye shader, with the lids painted the
    /// colour of the skin around the sockets.
    ///
    /// <para>
    /// The colour is SAMPLED rather than typed in, and re-sampled on every run, so the lids follow
    /// the skin when the skin is repainted. It is the median, per channel, of the skin texels under
    /// every vertex within <see cref="SocketReach"/> eye radii of an eye: a socket is often painted
    /// darker than the face around it, and one dark ring must not tint the whole lid.
    /// </para>
    ///
    /// <para>
    /// Drifters get this from <c>SculptCharacterBuilder.ApplyBehaviour</c>. Any other character with
    /// stylized eyes gets it from <i>Find Eyes And Match Skin</i> on the <see cref="EyeBlink"/>
    /// component's menu.
    /// </para>
    /// </summary>
    public static class EyelidWiring
    {
        /// <summary>
        /// How far out from an eye's centre, in eye radii, a vertex still counts as the skin of its
        /// socket. Two radii reaches past the socket rim onto the face without reaching the next
        /// feature over; every drifter found 88-225 vertices in it per eye.
        /// </summary>
        private const float SocketReach = 2f;

        [MenuItem("CONTEXT/EyeBlink/Find Eyes And Match Skin")]
        private static void FromContextMenu(MenuCommand command)
        {
            var blink = (EyeBlink)command.context;
            Undo.RecordObject(blink, "Find Eyes And Match Skin");
            Ensure(blink.gameObject);
        }

        /// <summary>
        /// Adds or updates the <see cref="EyeBlink"/> on <paramref name="root"/>. Idempotent. Owns
        /// the eye list and the lid colour and sheen; leaves every other field -- rest angles,
        /// timing -- as the component or a person set it.
        /// </summary>
        /// <returns>False, with the reason logged, if the character has no eyes to blink.</returns>
        public static bool Ensure(GameObject root)
        {
            var eyes = new List<Renderer>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (StylizedEyeBuilder.WearsEyeShader(renderer))
                    eyes.Add(renderer);

            if (eyes.Count == 0)
            {
                Debug.LogError($"[EyelidWiring] {root.name}: no renderer wears " +
                               $"{StylizedEyeBuilder.EyeShaderName}, so there are no eyes to blink. " +
                               "Rebuild the eye materials: Tools > SpaceGame > Art > Build Stylized Eye Materials.");
                return false;
            }

            var blink = root.GetComponent<EyeBlink>();
            if (blink == null) blink = root.AddComponent<EyeBlink>();

            var so = new SerializedObject(blink);
            SerializedFields.SetObjects(so, "eyes", eyes);

            if (TrySampleSkin(root, eyes, out Color skin, out float smoothness))
            {
                SerializedFields.SetColor(so, "lidColour", skin);
                SerializedFields.SetFloat(so, "lidSmoothness", smoothness);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        /// <summary>
        /// The skin around the sockets, and the sheen of the material it is painted on. Taken from
        /// whichever renderer has the most vertices around the eyes, which is the face -- found
        /// rather than assumed, so a character whose head is a separate mesh works the same.
        /// </summary>
        private static bool TrySampleSkin(GameObject root, List<Renderer> eyes, out Color skin,
                                          out float smoothness)
        {
            skin = default;
            smoothness = 0f;

            List<Color> best = null;
            Material bestMaterial = null;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (eyes.Contains(renderer)) continue;

                var material = renderer.sharedMaterial;
                if (material == null || !(material.mainTexture is Texture2D texture)) continue;

                var samples = SampleAroundEyes(renderer, texture, material, eyes);
                if (samples != null && (best == null || samples.Count > best.Count))
                {
                    best = samples;
                    bestMaterial = material;
                }
            }

            if (best == null || best.Count == 0)
            {
                Debug.LogError($"[EyelidWiring] {root.name}: no textured skin within {SocketReach} " +
                               "eye radii of the eyes, so the lids keep their current colour.");
                return false;
            }

            skin = Median(best);
            smoothness = bestMaterial.HasProperty("_Smoothness") ? bestMaterial.GetFloat("_Smoothness") : 0f;
            return true;
        }

        private static List<Color> SampleAroundEyes(Renderer renderer, Texture2D texture, Material material,
                                                    List<Renderer> eyes)
        {
            if (!TryReadMesh(renderer, out Vector3[] positions, out Vector2[] uvs)) return null;

            var pixels = ReadPixels(texture);
            if (pixels == null) return null;

            var samples = new List<Color>();
            try
            {
                foreach (var eye in eyes)
                {
                    var mesh = MeshOf(eye);
                    if (mesh == null) continue;

                    Vector3 centre = eye.transform.TransformPoint(mesh.bounds.center);
                    float radius = mesh.bounds.extents.x * eye.transform.lossyScale.x;

                    for (int i = 0; i < positions.Length; i++)
                    {
                        float distance = Vector3.Distance(positions[i], centre);
                        if (distance < radius || distance > radius * SocketReach) continue;

                        Vector2 uv = uvs[i] * material.mainTextureScale + material.mainTextureOffset;
                        samples.Add(pixels.GetPixelBilinear(uv.x, uv.y));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(pixels);
            }

            return samples;
        }

        /// <summary>
        /// World-space vertex positions in the pose the prefab is saved in, with their UVs.
        ///
        /// <para>
        /// A skinned body is baked first, since its rest vertices are not where its bones hold them.
        /// <c>BakeMesh(useScale: true)</c> then <c>TransformPoint</c> is the one combination that
        /// lands on the renderer's own bounds: on the drifters, whose model carries a 1.44 scale,
        /// <c>useScale: false</c> + <c>TransformPoint</c> came out 1.4x too big and
        /// <c>useScale: true</c> + position-and-rotation 1.4x too small -- both measuring every
        /// socket as empty.
        /// </para>
        /// </summary>
        private static bool TryReadMesh(Renderer renderer, out Vector3[] positions, out Vector2[] uvs)
        {
            positions = null;
            uvs = null;

            Mesh source = MeshOf(renderer);
            if (source == null) return false;

            uvs = source.uv;
            if (uvs.Length == 0) return false;

            if (renderer is SkinnedMeshRenderer skinned)
            {
                var baked = new Mesh();
                try
                {
                    skinned.BakeMesh(baked, true);
                    positions = baked.vertices;
                }
                finally
                {
                    Object.DestroyImmediate(baked);
                }
            }
            else
            {
                positions = source.vertices;
            }

            for (int i = 0; i < positions.Length; i++)
                positions[i] = renderer.transform.TransformPoint(positions[i]);

            return positions.Length == uvs.Length;
        }

        private static Mesh MeshOf(Renderer renderer) =>
            renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
            : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh
            : null;

        /// <summary>
        /// A readable copy of the texture's pixels, straight from the file on disk so the importer's
        /// Read/Write flag does not matter. The values are the file's own, in gamma space -- which is
        /// what a Color field holds, so they go into <see cref="EyeBlink"/> untouched.
        /// </summary>
        private static Texture2D ReadPixels(Texture2D texture)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            var copy = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!string.IsNullOrEmpty(path) && File.Exists(path) &&
                ImageConversion.LoadImage(copy, File.ReadAllBytes(path)))
                return copy;

            Object.DestroyImmediate(copy);
            Debug.LogError($"[EyelidWiring] Cannot read the pixels of '{texture.name}' ({path}); only " +
                           "PNG and JPG skins can be sampled.");
            return null;
        }

        private static Color Median(List<Color> colours)
        {
            var channel = new float[colours.Count];
            float MedianOf(System.Func<Color, float> pick)
            {
                for (int i = 0; i < colours.Count; i++) channel[i] = pick(colours[i]);
                System.Array.Sort(channel);
                return channel[channel.Length / 2];
            }

            return new Color(MedianOf(c => c.r), MedianOf(c => c.g), MedianOf(c => c.b), 1f);
        }
    }
}
