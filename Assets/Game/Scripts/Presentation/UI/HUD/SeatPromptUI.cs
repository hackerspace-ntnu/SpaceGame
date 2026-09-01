// "ESCAPE to dismount" — shown while this machine's own player is sitting in a landed ship and
// may get up.
//
// It exists because the crash landing is the one seat in the game you are put into rather than
// choosing to sit in, so nothing has taught you how to leave it. Every other seat is entered by
// walking up to a chair and pressing a key, which is its own lesson; this one arrives with the
// player already in it, at the opening of the game, with no prior instruction to fall back on.
//
// Timed rather than permanent, and that is the point (GDC-L1-UX-0001): it appears at the moment
// the player needs it — the hull is down and getting out is the next thing to do — instead of
// being front-loaded into a controls screen nobody remembers by the time it matters. The key it
// names is the one that already gets you off every mount in the game (MountModule reads the same
// one), so it teaches a convention rather than an exception (GDC-L1-UX-0004).
//
// Draws only. SeatedRider owns whether the key does anything; this asks it nothing and decides
// nothing, so the prompt cannot end up offering an action the seat would refuse.
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Gameplay.Arrival;

namespace SpaceGame.Presentation
{
    [DisallowMultipleComponent]
    public class SeatPromptUI : MonoBehaviour
    {
        [Header("Text")]
        [Tooltip("The key cap. Named to match what SeatedRider actually reads — if the dismount " +
                 "key ever moves, this moves with it or the prompt starts lying.")]
        [SerializeField] private string keyLabel = "ESCAPE";

        [SerializeField] private string message = "to leave the seat";

        [Header("Layout")]
        [Tooltip("Where the panel sits as a fraction of the screen. Low and centred, out of the " +
                 "way of the cockpit view the player has just landed in.")]
        [SerializeField] private Vector2 screenAnchor = new Vector2(0.5f, 0.16f);

        [SerializeField] private Vector2 panelSize = new Vector2(360f, 64f);

        [Header("Appearance")]
        [SerializeField] private Color panelColor = new Color(0.04f, 0.05f, 0.07f, 0.72f);
        [SerializeField] private Color keyColor = new Color(1f, 0.85f, 0.5f);
        [SerializeField] private Color messageColor = new Color(0.85f, 0.9f, 0.95f);

        [Tooltip("Seconds to fade. Not a hard cut: this appears as a blackout lifts, and popping " +
                 "on the same frame reads as a glitch rather than as a prompt.")]
        [SerializeField, Min(0.01f)] private float fadeDuration = 0.25f;

        private CanvasGroup group;
        private float alpha;
        private bool wanted;

        private void Awake() => BuildWidget();

        private void OnEnable()
        {
            SeatedRider.LocalPlayerMayLeaveChanged += OnMayLeaveChanged;
            SeatedRider.LocalPlayerReleased += OnReleased;
        }

        private void OnDisable()
        {
            SeatedRider.LocalPlayerMayLeaveChanged -= OnMayLeaveChanged;
            SeatedRider.LocalPlayerReleased -= OnReleased;

            // Straight to hidden: there is no Update coming to finish a fade with, and a panel
            // left at half alpha would sit on the screen for the rest of the session.
            wanted = false;
            alpha = 0f;
            if (group != null) group.alpha = 0f;
        }

        private void OnMayLeaveChanged(bool mayLeave) => wanted = mayLeave;

        // Belt and braces alongside the flag: standing up is the one thing that certainly ends the
        // prompt, and it arrives on its own event whatever route the release took — the player's
        // own key, or the director's backstop turfing them out.
        private void OnReleased() => wanted = false;

        private void Update()
        {
            float target = wanted ? 1f : 0f;
            if (Mathf.Approximately(alpha, target)) return;

            alpha = Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime / fadeDuration);
            group.alpha = alpha;
        }

        private void BuildWidget()
        {
            GameObject canvasObject = new GameObject("SeatPromptCanvas",
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

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasObject.transform, false);

            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = screenAnchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = panelSize;

            Image background = panel.GetComponent<Image>();
            background.color = panelColor;
            background.raycastTarget = false;

            TextMeshProUGUI label = NewText("Prompt", rect);
            // One string, two colours: the key has to be findable at a glance, and a separate key
            // cap widget would be three more rects to keep aligned for no more information.
            label.text = $"<color=#{ColorUtility.ToHtmlStringRGB(keyColor)}><b>{keyLabel}</b></color>  {message}";
        }

        private TextMeshProUGUI NewText(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = 24f;
            text.color = messageColor;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }
    }
}
