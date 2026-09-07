// Tunes the palette the quantize filter snaps to, live, without a recompile or a
// play-mode restart.
//
// This replaces a browser app plus a Python server plus a polled JSON file. That
// arrangement existed to repaint *captured stills* instantly, which is only worth its
// three moving parts if the preview is stills — and it never was: the Game view is the
// real frame, already lit, already moving, and already there. One window is the whole
// tool.
//
// Nothing is written while a slider moves: the feature instance in memory is what URP
// renders from, so persisting a drag would rewrite the renderer assets on every frame of
// it. Live values are ephemeral, and a domain reload returns the committed look. A value
// leaves this window only when asked — Save ink + noise for the serialized half, Copy as
// C# for the palette, which is deliberately not serialized anywhere.
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using SpaceGame.World.Environment;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace SpaceGame.EditorTools.Environment
{
    public class LookLabWindow : EditorWindow
    {
        private const int SwatchSize = 18;

        private const string NotInstalled =
            "The pastel quantize filter is not installed. Run " +
            "SpaceGame > Environment > Install Pastel Quantize Filter first.";

        private PaletteShape shape = PaletteShape.Default;
        private InkShape ink = InkShape.Default;
        private NoiseShape noise = NoiseShape.Default;
        private float blend = 1f;
        private bool live;
        private bool showCommitted;
        private Vector2 scroll;

        private Color[] preview = Array.Empty<Color>();
        private string error;

        // What the renderer assets said before this window started overriding them, so
        // closing it leaves the project as it was found.
        private bool restoreCaptured;
        private float restoreBlend;
        private PaletteShape restoreShape;
        private InkShape restoreInk;
        private NoiseShape restoreNoise;

        [MenuItem("SpaceGame/Look Lab")]
        public static void Open()
        {
            GetWindow<LookLabWindow>("Look Lab").minSize = new Vector2(340f, 420f);
        }

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnDisable()
        {
            SetLive(false);
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool wantLive = GUILayout.Toggle(live, "Live", EditorStyles.toolbarButton);
                if (wantLive != live)
                {
                    SetLive(wantLive);
                }

                using (new EditorGUI.DisabledScope(!live))
                {
                    bool wantCommitted = GUILayout.Toggle(
                        showCommitted, "Show committed", EditorStyles.toolbarButton);
                    if (wantCommitted != showCommitted)
                    {
                        // The eye detects change far better than it detects difference, so
                        // a toggle back to the shipped look resolves a shift that two
                        // windows side by side would hide.
                        showCommitted = wantCommitted;
                        Push();
                    }
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Reset", EditorStyles.toolbarButton))
                {
                    shape = PaletteShape.Default;
                    ink = InkShape.Default;
                    noise = NoiseShape.Default;
                    blend = 1f;
                    Rebuild();
                }

                // The ink and the noise are serialized on the renderer assets, unlike the
                // palette, so they have somewhere to be saved to. Behind a button rather
                // than automatic: writing them on every frame of a slider drag would
                // rewrite the assets continuously.
                if (GUILayout.Button("Save ink + noise", EditorStyles.toolbarButton))
                {
                    Persist();
                }

                if (GUILayout.Button("Copy as C#", EditorStyles.toolbarButton))
                {
                    EditorGUIUtility.systemCopyBuffer = AsCSharp();
                    ShowNotification(new GUIContent("Copied. Paste over PaletteShape.Default."));
                }
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();

            blend = EditorGUILayout.Slider(
                new GUIContent("Blend", "0 = untouched frame, 1 = fully quantized."),
                blend, 0f, 1f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Lattice", EditorStyles.boldLabel);
            shape.hueCount = EditorGUILayout.IntSlider("Hues", shape.hueCount, 4, 32);
            shape.chromaCeiling = EditorGUILayout.Slider(
                new GUIContent("Chroma ceiling",
                    "Caps the vivid variant before the per-hue gamut fit. Past ~0.22 it buys " +
                    "no measurable separation and the palette goes neon."),
                shape.chromaCeiling, 0f, 0.4f);

            shape.lightnesses = ListField("Lightness steps", shape.lightnesses, 0.02f, 1f);
            shape.chromaFractions = ListField(
                "Chroma fractions", shape.chromaFractions, 0f, 1f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Neutrals", EditorStyles.boldLabel);
            shape.neutralCount = EditorGUILayout.IntSlider("Steps", shape.neutralCount, 2, 32);
            shape.neutralMinL = EditorGUILayout.Slider("Darkest", shape.neutralMinL, 0f, 1f);
            shape.neutralMaxL = EditorGUILayout.Slider("Palest", shape.neutralMaxL, 0f, 1f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Ink", EditorStyles.boldLabel);
            ink.amount = EditorGUILayout.Slider("Strength", ink.amount, 0f, 0.6f);
            ink.width = EditorGUILayout.Slider("Breadth (px)", ink.width, 0.5f, 6f);
            ink.color = EditorGUILayout.ColorField(
                new GUIContent("Colour", "Reaches the frame only through Tint."),
                ink.color, showEyedropper: true, showAlpha: false, hdr: false);
            ink.tint = EditorGUILayout.Slider("Tint", ink.tint, 0f, 1f);
            ink.lumaThreshold = EditorGUILayout.Slider(
                new GUIContent("Shading edges",
                    "Lightness gradient that counts as an edge. Lower draws more interior " +
                    "detail; too low and every shading ramp inks."),
                ink.lumaThreshold, 0.002f, 0.2f);
            ink.depthThreshold = EditorGUILayout.Slider(
                new GUIContent("Silhouettes", "Relative depth gradient that counts as one."),
                ink.depthThreshold, 0.002f, 0.2f);
            ink.softness = EditorGUILayout.Slider("Softness", ink.softness, 0.001f, 0.2f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Noise", EditorStyles.boldLabel);
            noise.kind = (NoiseKind)EditorGUILayout.EnumPopup("Kind", noise.kind);
            using (new EditorGUI.DisabledScope(noise.kind == NoiseKind.None))
            {
                noise.amount = EditorGUILayout.Slider("Amount", noise.amount, 0f, 0.15f);
                using (new EditorGUI.DisabledScope(noise.kind == NoiseKind.Dither))
                {
                    noise.scale = EditorGUILayout.Slider("Cell (px)", noise.scale, 1f, 32f);
                }

                using (new EditorGUI.DisabledScope(noise.kind != NoiseKind.Speckle))
                {
                    noise.density = EditorGUILayout.Slider("Density", noise.density, 0f, 1f);
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                Rebuild();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Global Volume", EditorStyles.boldLabel);
            SceneVolumeSection.Draw();

            EditorGUILayout.Space();
            if (error != null)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            else
            {
                EditorGUILayout.LabelField(
                    $"{preview.Length} colours",
                    // The shader walks every entry for every pixel of a fullscreen pass, so
                    // the count is also the per-pixel cost, not a free dial.
                    EditorStyles.miniLabel);
                DrawSwatches();
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// One slider per entry plus add and remove, so the *number* of lightness steps is
        /// itself tunable — which is the axis that decides how much surface detail survives
        /// the snap, and the one a fixed-length array would hide.
        /// </summary>
        private float[] ListField(string label, float[] values, float min, float max)
        {
            values ??= Array.Empty<float>();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("+", EditorStyles.miniButtonLeft, GUILayout.Width(22f)))
                {
                    values = values.Append(values.Length > 0 ? values[^1] : min).ToArray();
                }

                using (new EditorGUI.DisabledScope(values.Length <= 1))
                {
                    if (GUILayout.Button("−", EditorStyles.miniButtonRight, GUILayout.Width(22f)))
                    {
                        values = values.Take(values.Length - 1).ToArray();
                    }
                }
            }

            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = EditorGUILayout.Slider(i.ToString(), values[i], min, max);
                }
            }

            return values;
        }

        private void DrawSwatches()
        {
            int columns = Mathf.Max(1, Mathf.FloorToInt(
                (EditorGUIUtility.currentViewWidth - 30f) / SwatchSize));
            int rows = Mathf.CeilToInt(preview.Length / (float)columns);
            Rect area = GUILayoutUtility.GetRect(
                columns * SwatchSize, rows * SwatchSize, GUILayout.ExpandWidth(false));

            for (int i = 0; i < preview.Length; i++)
            {
                EditorGUI.DrawRect(
                    new Rect(area.x + i % columns * SwatchSize,
                             area.y + i / columns * SwatchSize,
                             SwatchSize - 1f, SwatchSize - 1f),
                    preview[i]);
            }
        }

        private void Rebuild()
        {
            if (!shape.Validate(PastelQuantizeRenderFeature.MaxPaletteSize, out error))
            {
                preview = Array.Empty<Color>();
                return;
            }

            preview = PastelPalette.Build(shape);
            Push();
        }

        private void SetLive(bool value)
        {
            if (value == live)
            {
                return;
            }

            if (value)
            {
                CaptureRestoreState();
                live = true;
                Push();
                return;
            }

            live = false;
            showCommitted = false;
            RestoreState();
        }

        private void Push()
        {
            if (!live || error != null)
            {
                return;
            }

            bool committed = showCommitted;
            int touched = ForEachFeature(pastel =>
            {
                pastel.settings.paletteShape = committed ? PaletteShape.Default : shape;
                pastel.settings.blend = committed ? 1f : blend;
                pastel.settings.ink = committed ? InkShape.Default : ink;
                pastel.settings.noise = committed ? NoiseShape.Default : noise;
            });

            if (touched == 0)
            {
                error = NotInstalled;
                return;
            }

            // In play mode the loop repaints itself; in edit mode the Game view only
            // redraws when something asks it to.
            if (!EditorApplication.isPlaying)
            {
                InternalEditorUtility.RepaintAllViews();
            }
        }

        /// <summary>
        /// Writes the serialized half of the look — ink, noise, blend — to the renderer
        /// assets for good. The palette shape is deliberately not among them: keeping it
        /// off the assets is what stops the PC and mobile renderers drifting into different
        /// colours, so it leaves this window through Copy as C# instead.
        /// </summary>
        private void Persist()
        {
            int touched = ForEachFeature(pastel =>
            {
                pastel.settings.ink = ink;
                pastel.settings.noise = noise;
                pastel.settings.blend = blend;
                EditorUtility.SetDirty(pastel);
            });

            if (touched == 0)
            {
                error = NotInstalled;
                return;
            }

            AssetDatabase.SaveAssets();
            ShowNotification(new GUIContent($"Saved to {touched} renderer(s)."));
        }

        private void CaptureRestoreState()
        {
            restoreCaptured = false;
            ForEachFeature(pastel =>
            {
                if (restoreCaptured)
                {
                    return;
                }

                restoreBlend = pastel.settings.blend;
                restoreShape = pastel.settings.paletteShape;
                restoreInk = pastel.settings.ink;
                restoreNoise = pastel.settings.noise;
                restoreCaptured = true;
            });
        }

        private void RestoreState()
        {
            if (!restoreCaptured)
            {
                return;
            }

            ForEachFeature(pastel =>
            {
                pastel.settings.blend = restoreBlend;
                pastel.settings.paletteShape = restoreShape;
                pastel.settings.ink = restoreInk;
                pastel.settings.noise = restoreNoise;
            });

            restoreCaptured = false;
            if (!EditorApplication.isPlaying)
            {
                InternalEditorUtility.RepaintAllViews();
            }
        }

        /// <summary>
        /// Applies an edit to every installed feature, so the PC and mobile renderers cannot
        /// drift into previewing different looks. Returns how many were touched. Unlike the
        /// install path in <see cref="PastelQuantizeSetup"/> this never marks anything dirty
        /// — see the note at the top of the file.
        /// </summary>
        private static int ForEachFeature(Action<PastelQuantizeRenderFeature> edit)
        {
            int touched = 0;

            foreach (var renderer in VolumetricSetup.FindRenderers())
            {
                foreach (var feature in renderer.rendererFeatures)
                {
                    if (feature is PastelQuantizeRenderFeature pastel)
                    {
                        edit(pastel);
                        touched++;
                    }
                }
            }

            return touched;
        }

        private string AsCSharp()
        {
            static string F(float value) =>
                value.ToString("0.####", CultureInfo.InvariantCulture) + "f";

            var text = new StringBuilder();
            text.AppendLine("public static PaletteShape Default => new PaletteShape");
            text.AppendLine("{");
            text.AppendLine($"    hueCount = {shape.hueCount},");
            text.AppendLine(
                $"    lightnesses = new[] {{ {string.Join(", ", shape.lightnesses.Select(F))} }},");
            text.AppendLine(
                $"    chromaFractions = new[] {{ {string.Join(", ", shape.chromaFractions.Select(F))} }},");
            text.AppendLine($"    chromaCeiling = {F(shape.chromaCeiling)},");
            text.AppendLine($"    neutralCount = {shape.neutralCount},");
            text.AppendLine($"    neutralMinL = {F(shape.neutralMinL)},");
            text.AppendLine($"    neutralMaxL = {F(shape.neutralMaxL)},");
            text.AppendLine("};");
            return text.ToString();
        }
    }
}
