using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The eye authoring surface: a live picture of the eyeball above the fields that make it, and
    /// one button that writes the texture and the material.
    ///
    /// <para>
    /// The preview draws the ball head-on through the SAME <see cref="StylizedEyeBuilder.Shade"/>
    /// the bake uses, so there is no second description of what an eye looks like that could drift
    /// out of step with the first. It is the flat albedo plus a cheap dome shade -- it shows what is
    /// PAINTED, not what the URP material will do with a light.
    /// </para>
    ///
    /// <para>
    /// The aperture ring is the honest part of it. Most of the ball is buried in the head, so a
    /// pupil that looks well proportioned against the whole sphere can be most of what a player
    /// actually sees; the ring marks roughly where the lids cut it off.
    /// </para>
    /// </summary>
    [CustomEditor(typeof(EyeStyle))]
    [CanEditMultipleObjects]
    public sealed class EyeStyleEditor : Editor
    {
        private const int PreviewSize = 260;

        /// <summary>Roughly how much of the eyeball clears the lids on the sculpt-base characters.</summary>
        private const float DefaultAperture = 50f;

        private const string AperturePref = "SpaceGame.EyeStyle.Aperture";

        /// <summary>Thickness of the drawn ring, in degrees. Thin enough to read as a guide.</summary>
        private const float ApertureLineWidth = 0.7f;

        private Texture2D preview;

        /// <summary>
        /// What the preview was last drawn from. Serialising the target to JSON and comparing is
        /// cheap next to redrawing 68k pixels, and it catches an Undo or an external edit that a
        /// change check inside OnInspectorGUI never sees.
        /// </summary>
        private string drawnFrom;

        private float aperture;

        private void OnEnable()
        {
            aperture = EditorPrefs.GetFloat(AperturePref, DefaultAperture);
        }

        private void OnDisable()
        {
            if (preview != null) DestroyImmediate(preview);
            preview = null;
        }

        public override void OnInspectorGUI()
        {
            DrawPreview();

            EditorGUILayout.Space();
            DrawDefaultInspector();
            EditorGUILayout.Space();

            DrawActions();
        }

        private void DrawPreview()
        {
            var style = target as EyeStyle;
            if (style == null) return;

            string signature = EditorJsonUtility.ToJson(style) + "|" + aperture;
            if (preview == null || drawnFrom != signature)
            {
                Redraw(style);
                drawnFrom = signature;
            }

            var rect = GUILayoutUtility.GetRect(PreviewSize, PreviewSize, GUILayout.ExpandWidth(true));
            float side = Mathf.Min(rect.width, rect.height);
            var square = new Rect(rect.x + (rect.width - side) * 0.5f, rect.y, side, side);
            GUI.DrawTexture(square, preview, ScaleMode.ScaleToFit);

            aperture = EditorGUILayout.Slider(
                new GUIContent("Preview aperture",
                    "Guide only, never baked. Marks how much of the ball the lids leave showing, " +
                    "so you can judge the pupil against what a player sees rather than against the " +
                    "whole sphere."),
                aperture, 10f, 90f);
            EditorPrefs.SetFloat(AperturePref, aperture);
        }

        private void DrawActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Bake This Eye", GUILayout.Height(26f)))
                    foreach (var each in targets)
                        Bake(each as EyeStyle);

                if (GUILayout.Button("Bake All Eyes", GUILayout.Height(26f)))
                    StylizedEyeBuilder.BuildAll();
            }

            EditorGUILayout.HelpBox(
                "Baking writes Eye_<name>_BaseColor.png and Eye_<name>.mat at fixed paths, so " +
                "anything already pointing at this eye keeps working. Characters claim a style by " +
                "this asset's NAME — renaming the asset orphans them until they are re-pointed.",
                MessageType.None);
        }

        private static void Bake(EyeStyle style)
        {
            if (style == null) return;

            var material = StylizedEyeBuilder.Build(style);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (material != null)
                Debug.Log($"[StylizedEyeBuilder] Baked '{style.name}'.", material);
        }

        /// <summary>
        /// Paints the front cap of the eyeball as seen straight on. Screen x is
        /// <c>Side</c> and screen y is <c>Up</c>, which is the frame the shapes' offsets are written
        /// in, so dragging OffsetUp moves the pupil up here too.
        /// </summary>
        private void Redraw(EyeStyle style)
        {
            if (preview == null)
                preview = new Texture2D(PreviewSize, PreviewSize, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                };

            var pixels = new Color[PreviewSize * PreviewSize];
            var backdrop = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.16f, 0.18f, 1f)
                : new Color(0.62f, 0.62f, 0.66f, 1f);

            float radius = PreviewSize * 0.5f;
            float glow = style.EmissionStrength;

            for (int y = 0; y < PreviewSize; y++)
            {
                for (int x = 0; x < PreviewSize; x++)
                {
                    float sx = (x + 0.5f - radius) / radius;
                    float sy = (y + 0.5f - radius) / radius;
                    float sq = sx * sx + sy * sy;

                    if (sq > 1f)
                    {
                        pixels[y * PreviewSize + x] = backdrop;
                        continue;
                    }

                    float sz = Mathf.Sqrt(1f - sq);
                    Vector3 direction = StylizedEyeBuilder.PreviewDirection(sx, sy, sz);

                    Color colour = StylizedEyeBuilder.Shade(style, direction, emission: false);

                    if (glow > 0f)
                    {
                        Color lit = StylizedEyeBuilder.Shade(style, direction, emission: true);
                        colour += lit * style.Emission * glow;
                    }

                    // A cheap dome shade so it reads as a ball rather than a disc. Nothing to do
                    // with the URP material; it only stops the silhouette reading flat.
                    float dome = 0.55f + 0.45f * sz;
                    colour = new Color(colour.r * dome, colour.g * dome, colour.b * dome, 1f);

                    float theta = Mathf.Rad2Deg * Mathf.Acos(Mathf.Clamp(sz, -1f, 1f));
                    if (Mathf.Abs(theta - aperture) < ApertureLineWidth)
                        colour = Color.Lerp(colour, Color.cyan, 0.5f);

                    pixels[y * PreviewSize + x] = colour;
                }
            }

            preview.SetPixels(pixels);
            preview.Apply();
        }
    }
}
