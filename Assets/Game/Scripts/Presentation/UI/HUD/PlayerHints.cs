using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The one place a gameplay hint is drawn: a single textbox at the top of the screen.
    ///
    /// <para>
    /// Built for the lesson-at-the-moment-it-matters pattern (GDC-L1-UX-0001): a system that
    /// knows the player needs telling something calls <see cref="Show"/> when the moment arrives
    /// and <see cref="Hide"/> when it has passed. Hints are addressed by id so an owner only ever
    /// takes down its own — a late <c>Hide</c> cannot erase somebody else's newer hint.
    /// </para>
    ///
    /// <para>
    /// One slot, latest wins, on purpose. Two simultaneous hints mean two systems both claiming
    /// this instant is teachable, and stacking them teaches neither; the newer claim is the one
    /// about whatever just happened. Draws only — it decides nothing and reads no input, the same
    /// contract the old seat prompt kept.
    /// </para>
    ///
    /// <para>
    /// Self-instantiating and <c>DontDestroyOnLoad</c>, the <see cref="LetterboxOverlay"/>
    /// pattern, so a caller never has to care whether a scene placed one. It sorts UNDER the
    /// letterbox: a hint must never punch through a cutscene's blackout.
    /// </para>
    /// </summary>
    public class PlayerHints : MonoBehaviour
    {
        private static PlayerHints instance;

        private static PlayerHints Instance
        {
            get
            {
                if (instance != null) return instance;

                instance = FindFirstObjectByType<PlayerHints>();
                if (instance != null) return instance;

                var go = new GameObject("PlayerHints");
                instance = go.AddComponent<PlayerHints>();
                return instance;
            }
        }

        [Tooltip("Where the box sits, as a fraction of the screen. High and centred: hints are " +
                 "read once and glanced past, so they live out of the crosshair's way.")]
        [SerializeField] private Vector2 screenAnchor = new Vector2(0.5f, 0.92f);

        [SerializeField] private Vector2 panelSize = new Vector2(420f, 56f);

        [SerializeField] private Color panelColor = new Color(0.04f, 0.05f, 0.07f, 0.72f);
        [SerializeField] private Color textColor = new Color(0.85f, 0.9f, 0.95f);

        [Tooltip("Seconds to fade in and out. Not a hard cut — a popping panel reads as a glitch.")]
        [SerializeField, Min(0.01f)] private float fadeDuration = 0.25f;

        private CanvasGroup group;
        private TextMeshProUGUI label;

        private string activeId;
        private float hideAt = float.PositiveInfinity;
        private bool wanted;
        private float alpha;

        /// <summary>Shows <paramref name="text"/> until <see cref="Hide"/> is called with the same id.</summary>
        public static void Show(string id, string text) => Show(id, text, float.PositiveInfinity);

        /// <summary>Shows <paramref name="text"/>, taking itself down after <paramref name="seconds"/>.</summary>
        public static void Show(string id, string text, float seconds)
        {
            PlayerHints hints = Instance;

            hints.activeId = id;
            hints.label.text = text;
            hints.wanted = true;
            hints.hideAt = float.IsPositiveInfinity(seconds)
                ? float.PositiveInfinity
                : Time.unscaledTime + Mathf.Max(0f, seconds);
        }

        /// <summary>Takes down the hint with this id. A hint someone else has since shown stays up.</summary>
        public static void Hide(string id)
        {
            // Deliberately not Instance: hiding must never be the thing that builds the overlay.
            if (instance == null || instance.activeId != id) return;

            instance.wanted = false;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            BuildWidget();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            if (wanted && Time.unscaledTime >= hideAt) wanted = false;

            float target = wanted ? 1f : 0f;
            if (Mathf.Approximately(alpha, target)) return;

            alpha = Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime / fadeDuration);
            group.alpha = alpha;
        }

        private void BuildWidget()
        {
            var canvasObject = new GameObject("HintCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Under LetterboxOverlay's 32000: a blackout covers hints, never the other way round.
            canvas.sortingOrder = 31000;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
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

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(rect, false);

            var textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            label = textObject.GetComponent<TextMeshProUGUI>();
            label.fontSize = 24f;
            label.color = textColor;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }
    }
}
