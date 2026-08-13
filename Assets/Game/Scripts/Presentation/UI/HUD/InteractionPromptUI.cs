// Tells the player what they are looking at and what the buttons will do.
//
// Written for the dune foiler's deck, which is the place the lack hurt most: four winches within a
// couple of metres of each other, all identical grey spheres, all answering the same two buttons,
// each meaning something different — and one of them, until it was reworked, striking the entire rig
// on a single left click. The information needed to prevent that was already there (Prompt has been
// on DuneFoilRiggingStation since it was written) and nothing in the project read it.
//
// It now draws EVERY interactable in the game, not just the ones that describe themselves.
// InteractionPromptResolver works out what to say from the component's type name when nothing
// better is available, so a door, a lever, a pickup and a rope winch all get a prompt with no
// authoring at all — and an InteractionPrompt component on the object turns one off or renames it.
//
// Nothing here knows about sails, doors or anything else. It draws whatever the resolver returns.
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    [DisallowMultipleComponent]
    public class InteractionPromptUI : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The player's Interactor. Found in the scene when empty.")]
        [SerializeField] private Interactor playerInteractor;

        [Header("Layout")]
        [Tooltip("Where the panel sits, as a fraction of the screen from the bottom-left. Below " +
                 "the crosshair, so it never covers what you are aiming at.")]
        [SerializeField] private Vector2 screenAnchor = new Vector2(0.5f, 0.34f);

        [SerializeField] private Vector2 panelSize = new Vector2(340f, 96f);

        [Header("Appearance")]
        [SerializeField] private Color panelColor = new Color(0.04f, 0.05f, 0.07f, 0.72f);
        [SerializeField] private Color labelColor = new Color(1f, 0.85f, 0.5f);
        [SerializeField] private Color promptColor = new Color(0.85f, 0.9f, 0.95f);
        [SerializeField] private Color barColor = new Color(0.35f, 0.8f, 1f);
        [SerializeField] private Color barTrackColor = new Color(1f, 1f, 1f, 0.15f);

        [Tooltip("Seconds for the panel to fade in and out. A hard cut as the crosshair crosses " +
                 "two handles 1.5 m apart flickers badly.")]
        [SerializeField, Min(0.01f)] private float fadeDuration = 0.12f;

        [Tooltip("Seconds the bar takes to catch up. The controls are held, so the value is " +
                 "moving continuously while the player reads it.")]
        [SerializeField, Min(0f)] private float fillLerpDuration = 0.08f;

        private CanvasGroup group;
        private TextMeshProUGUI labelText;
        private TextMeshProUGUI promptText;
        private TextMeshProUGUI valueText;
        private Image barFill;
        private RectTransform barTrack;

        private float alpha;
        private float displayedFill;

        private void Awake()
        {
            if (playerInteractor == null) playerInteractor = FindFirstObjectByType<Interactor>();
            BuildWidget();
        }

        private void Update()
        {
            // The player is spawned, so the Interactor usually does not exist when this wakes.
            if (playerInteractor == null)
            {
                playerInteractor = FindFirstObjectByType<Interactor>();
                if (playerInteractor == null) { Hide(); return; }
            }

            // An Interactor that isn't running isn't hovering anything, whatever it last recorded.
            // Interactor clears its own hover state on disable, so this is belt and braces — but it
            // is the belt that holds if some future path stops the Interactor updating without
            // disabling it. Mounting takes exactly that route today.
            if (!playerInteractor.isActiveAndEnabled)
            {
                Hide();
                return;
            }

            if (!InteractionPromptResolver.TryResolve(playerInteractor.HoveredInteractable,
                                                     out InteractionDisplay display))
            {
                Hide();
                return;
            }

            Show(display);
        }

        private void Hide()
        {
            alpha = Mathf.MoveTowards(alpha, 0f, Time.unscaledDeltaTime / fadeDuration);
            if (group != null) group.alpha = alpha;
        }

        private void Show(in InteractionDisplay display)
        {
            alpha = Mathf.MoveTowards(alpha, 1f, Time.unscaledDeltaTime / fadeDuration);
            if (group != null) group.alpha = alpha;

            if (labelText != null) labelText.text = display.Label;
            if (promptText != null) promptText.text = display.Prompt;

            float? value = display.Value01;
            bool hasValue = value.HasValue;

            if (barTrack != null && barTrack.gameObject.activeSelf != hasValue)
                barTrack.gameObject.SetActive(hasValue);

            if (valueText != null) valueText.text = hasValue ? display.ValueText : string.Empty;

            if (!hasValue || barFill == null) return;

            float target = Mathf.Clamp01(value.Value);
            displayedFill = fillLerpDuration <= 0f
                ? target
                : Mathf.Lerp(displayedFill, target,
                             1f - Mathf.Exp(-Time.unscaledDeltaTime / fillLerpDuration));
            barFill.fillAmount = displayedFill;
        }

        // ----------------------------------------------------------------------
        // Built in code rather than authored as a prefab.
        //
        // This has to appear on the player's HUD, and the player is spawned by the netcode rather
        // than living in the scene, so there is no authored canvas to hang it under at edit time.
        // Building it here means adding this one component anywhere in the scene is the whole
        // installation — which is also what keeps it from being quietly left out of a scene the way
        // the Prompt strings were quietly left unread.

        private void BuildWidget()
        {
            GameObject canvasObject = new GameObject("InteractionPromptCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            group = canvasObject.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            RectTransform panel = NewRect("Panel", canvasObject.transform);
            panel.anchorMin = panel.anchorMax = screenAnchor;
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = panelSize;

            Image background = panel.gameObject.AddComponent<Image>();
            background.color = panelColor;
            background.raycastTarget = false;

            labelText = NewText("Label", panel, 26f, FontStyles.Bold, labelColor,
                                TextAlignmentOptions.Left);
            Anchor(labelText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(12f, -32f), new Vector2(-12f, -6f));

            valueText = NewText("Value", panel, 22f, FontStyles.Normal, promptColor,
                                TextAlignmentOptions.Right);
            Anchor(valueText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(12f, -32f), new Vector2(-12f, -6f));

            promptText = NewText("Prompt", panel, 20f, FontStyles.Normal, promptColor,
                                 TextAlignmentOptions.Left);
            Anchor(promptText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                   new Vector2(12f, -58f), new Vector2(-12f, -34f));

            barTrack = NewRect("BarTrack", panel);
            Anchor(barTrack, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(12f, 12f), new Vector2(-12f, 20f));
            Image track = barTrack.gameObject.AddComponent<Image>();
            track.color = barTrackColor;
            track.raycastTarget = false;

            RectTransform fill = NewRect("BarFill", barTrack);
            Anchor(fill, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            barFill = fill.gameObject.AddComponent<Image>();
            barFill.color = barColor;
            barFill.raycastTarget = false;
            barFill.type = Image.Type.Filled;
            barFill.fillMethod = Image.FillMethod.Horizontal;
            barFill.fillAmount = 0f;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, float size,
                                               FontStyles style, Color colour,
                                               TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.fontStyle = style;
            text.color = colour;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            return text;
        }

        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max,
                                   Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }
}
