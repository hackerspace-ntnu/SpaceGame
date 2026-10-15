using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// "QUEST FAILED": the screen goes black but for two huge words, and the world is still there
    /// INSIDE the letters, scuffed with grime — then the letters swell until the world comes back
    /// through them.
    ///
    /// <para>
    /// <b>How the world shows through the letters.</b> Stencil, no render texture. The title is drawn
    /// first in <see cref="windowMaterial"/> — colour writes off, stencil written, alpha-clipped so only
    /// the letter shapes mark it, not their quads; the black is drawn after it everywhere the mark is
    /// NOT. The scene behind the overlay canvas is simply left uncovered inside the glyphs. A second
    /// copy of the title over the top in <see cref="grimeMaterial"/>, black with a speckled face
    /// texture, is the grime. Both are assets on the full Distance Field shader: the mobile one a font
    /// asset is generated with has no face texture, and a material built at runtime would need its
    /// shader shipped by name.
    /// </para>
    /// <para>
    /// A cutscene rather than a bare overlay so the <see cref="CutsceneDirector"/>'s envelope holds
    /// the player still for it and gives them back afterwards — and so it can never run over another
    /// cutscene. Local, per machine; <c>WitchFight</c> starts it from the fight's failure message. The
    /// server resets the fight while the screen is black, so <see cref="BlackBy"/> is a contract with
    /// whoever starts it.
    /// </para>
    /// </summary>
    public class QuestFailedCutscene : Cutscene
    {
        [SerializeField] private string title = "QUEST FAILED";

        [Tooltip("Heavy, condensed: the letters are the window, so the fatter the better.")]
        [SerializeField] private TMP_FontAsset titleFont;

        [Tooltip("For the line under the title. The default TMP font when left empty.")]
        [SerializeField] private TMP_FontAsset reasonFont;

        [Tooltip("The title font's atlas on TextMeshPro/Distance Field, writing stencil 1, colour mask 0, " +
                 "alpha clip on: marks the letter shapes the world shows through.")]
        [SerializeField] private Material windowMaterial;

        [Tooltip("The title font's atlas on TextMeshPro/Distance Field with a speckled face texture: the " +
                 "grime over the letters. Optional.")]
        [SerializeField] private Material grimeMaterial;

        [SerializeField, Min(1f)] private float titleSize = 300f;
        [SerializeField, Min(1f)] private float reasonSize = 34f;

        [Header("Timing")]
        [Tooltip("Seconds the black takes to close in.")]
        [SerializeField, Min(0.01f)] private float fadeIn = 0.5f;

        [Tooltip("Seconds the title holds.")]
        [SerializeField, Min(0f)] private float hold = 2.6f;

        [Tooltip("Seconds the letters take to swell, and the black to lift, letting the world back in.")]
        [SerializeField, Min(0.01f)] private float openOut = 0.7f;

        [Tooltip("How far the title drifts in while it holds, as a scale it starts at and settles to 1 from.")]
        [SerializeField, Min(1f)] private float driftFrom = 1.18f;

        [Tooltip("How big the letters grow as they open out — big enough that the black is gone.")]
        [SerializeField, Min(1f)] private float openTo = 14f;

        private const int TitleStencil = 1;


        /// <summary>The line under the title, set by whoever plays it.</summary>
        public string Reason { get; set; }

        /// <summary>Seconds from the start until the screen is fully black around the letters.</summary>
        public float BlackBy => fadeIn;

        public override IEnumerator Play(CutsceneContext ctx)
        {
            GameObject root = Build(out RectTransform window, out RectTransform grimeBox, out Image black,
                                    out TextMeshProUGUI reasonText);
            TextMeshProUGUI grimeText = grimeBox != null ? grimeBox.GetComponent<TextMeshProUGUI>() : null;
            try
            {
                float t = 0f;
                while (t < fadeIn + hold)
                {
                    t += Time.unscaledDeltaTime;
                    black.color = new Color(0f, 0f, 0f, Mathf.Clamp01(t / fadeIn));
                    reasonText.alpha = Mathf.Clamp01((t - fadeIn) / fadeIn);

                    float settle = Mathf.Clamp01(t / (fadeIn + hold));
                    ScaleTitle(window, grimeBox, Mathf.Lerp(driftFrom, 1f, 1f - (1f - settle) * (1f - settle)));
                    yield return null;
                }

                t = 0f;
                while (t < openOut)
                {
                    t += Time.unscaledDeltaTime;
                    float s = Mathf.Clamp01(t / openOut);
                    ScaleTitle(window, grimeBox, Mathf.Lerp(1f, openTo, s * s * s));
                    // The black lifts as the letters swell: the gap between the two words would
                    // otherwise grow to cover the screen before the letters ever did.
                    black.color = new Color(0f, 0f, 0f, 1f - s * s);
                    if (grimeText != null) grimeText.alpha = 1f - s;
                    reasonText.alpha = 1f - s;
                    yield return null;
                }
            }
            finally
            {
                Destroy(root);
            }
        }

        private static void ScaleTitle(RectTransform window, RectTransform grimeBox, float scale)
        {
            window.localScale = Vector3.one * scale;
            if (grimeBox != null) grimeBox.localScale = Vector3.one * scale;
        }

        /// <summary>
        /// Sibling order is the whole trick: the stencil-marking title, then the black that avoids
        /// it, then the grime over the window, then the line under it.
        /// </summary>
        private GameObject Build(out RectTransform window, out RectTransform grimeBox, out Image black,
                                 out TextMeshProUGUI reasonText)
        {
            var root = new GameObject("QuestFailedOverlay");
            root.transform.SetParent(transform, false);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32001; // over the letterbox bars
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            window = Stretch(new GameObject("Window", typeof(RectTransform)), root.transform);
            TextMeshProUGUI windowText = Title(window);
            if (windowMaterial != null) windowText.fontSharedMaterial = windowMaterial;

            black = Stretch(new GameObject("Black", typeof(RectTransform)), root.transform).gameObject.AddComponent<Image>();
            black.material = BlackMaterial();
            black.color = new Color(0f, 0f, 0f, 0f);
            black.raycastTarget = false;

            grimeBox = null;
            if (grimeMaterial != null)
            {
                grimeBox = Stretch(new GameObject("Grime", typeof(RectTransform)), root.transform);
                TextMeshProUGUI dirt = Title(grimeBox);
                dirt.fontSharedMaterial = grimeMaterial;
                dirt.color = Color.black;
                dirt.horizontalMapping = TextureMappingOptions.Paragraph;
                dirt.verticalMapping = TextureMappingOptions.Paragraph;
            }

            RectTransform reasonBox = Stretch(new GameObject("Reason", typeof(RectTransform)), root.transform);
            reasonBox.anchorMin = new Vector2(0f, 0.12f);
            reasonBox.anchorMax = new Vector2(1f, 0.22f);
            reasonText = reasonBox.gameObject.AddComponent<TextMeshProUGUI>();
            if (reasonFont != null) reasonText.font = reasonFont;
            reasonText.text = Reason ?? string.Empty;
            reasonText.fontSize = reasonSize;
            reasonText.alignment = TextAlignmentOptions.Center;
            reasonText.color = new Color(0.85f, 0.82f, 0.75f, 1f);
            reasonText.alpha = 0f;
            reasonText.raycastTarget = false;

            return root;
        }

        private TextMeshProUGUI Title(RectTransform box)
        {
            var text = box.gameObject.AddComponent<TextMeshProUGUI>();
            if (titleFont != null) text.font = titleFont;
            text.text = title;
            text.fontSize = titleSize;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform Stretch(GameObject go, Transform parent)
        {
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>UI/Default, drawing only where the title has NOT marked the stencil.</summary>
        private static Material BlackMaterial()
        {
            var material = new Material(Shader.Find("UI/Default")) { name = "QuestFailedBlack" };
            material.SetFloat("_Stencil", TitleStencil);
            material.SetFloat("_StencilComp", (float)CompareFunction.NotEqual);
            material.SetFloat("_StencilOp", (float)StencilOp.Keep);
            material.SetFloat("_StencilWriteMask", 255f);
            material.SetFloat("_StencilReadMask", 255f);
            return material;
        }
    }
}
