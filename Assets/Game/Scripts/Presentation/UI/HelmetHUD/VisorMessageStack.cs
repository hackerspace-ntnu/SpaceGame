using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Draws the <see cref="SystemMessages"/> channel on the visor: a short column of lines on the
    /// upper left, newest at the bottom, in <see cref="VisorStyle"/>.
    ///
    /// <para>
    /// <b>Deliberately not a child of PlayerHUD.</b> It is self-instantiating and
    /// <c>DontDestroyOnLoad</c>, the <see cref="VisorWarningBanner"/> and <c>LetterboxOverlay</c>
    /// pattern, because the arrival announces things at exactly the moments the player's HUD is
    /// switched off — <c>SeatPromptUI</c>'s hint would never be seen if this drew inside the
    /// visor's own canvas. It reads as part of the visor and is hidden with it, but it does not
    /// live under it.
    /// </para>
    /// <para>
    /// Sorts under <c>LetterboxOverlay</c>'s 32000: a cutscene blackout must always cover a
    /// message, never the other way round.
    /// </para>
    /// </summary>
    public class VisorMessageStack : MonoBehaviour
    {
        private const int SortingOrder = 30900;

        private static VisorMessageStack instance;

        /// <summary>
        /// Brought into being at boot rather than on the first message, because nothing calls into
        /// this class to post — <see cref="SystemMessages"/> is a plain static with no knowledge of
        /// its views. Without this the very first message would have nothing drawing it.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() => _ = Instance;

        /// <summary>Builds the overlay on first ask. Never returns null.</summary>
        public static VisorMessageStack Instance
        {
            get
            {
                if (instance != null) return instance;

                instance = FindFirstObjectByType<VisorMessageStack>();
                if (instance != null) return instance;

                var go = new GameObject("VisorMessageStack");
                instance = go.AddComponent<VisorMessageStack>();
                return instance;
            }
        }

        [Tooltip("Where the column starts, as a fraction of the screen. Under the oxygen gauge on " +
                 "the left, clear of the crosshair.")]
        [SerializeField] private Vector2 screenAnchor = new(0.035f, 0.66f);

        [Tooltip("Width of the column. Long lines wrap rather than running under the crosshair.")]
        [SerializeField] private float width = 520f;

        [Tooltip("Vertical gap between lines.")]
        [SerializeField] private float lineSpacing = 6f;

        [Tooltip("Seconds a line takes to fade in and out. A hard cut reads as a glitch.")]
        [SerializeField, Min(0.01f)] private float fadeDuration = 0.22f;

        private CanvasGroup group;
        private RectTransform column;

        private readonly List<Entry> rows = new(SystemMessages.StackDepth);
        private readonly List<SystemMessages.Entry> live = new(SystemMessages.StackDepth);

        private bool shown = true;

        /// <summary>One drawn row, pooled. Never destroyed — only re-pointed and faded.</summary>
        private sealed class Entry
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public TextMeshProUGUI Label;
            public float Alpha;
        }

        /// <summary>
        /// Hides or shows the whole channel. Driven by <see cref="HelmetOverlayVisibility"/>, so
        /// H reaches this even though it is not parented under the visor.
        /// </summary>
        public static void SetShown(bool shown)
        {
            // Not Instance: hiding must never be the thing that builds the overlay.
            if (instance != null) instance.shown = shown;
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
            Build();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            SystemMessages.DropExpired();
            SystemMessages.CollectStack(live);

            EnsureRows(live.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                Entry row = rows[i];
                bool used = i < live.Count;

                if (used)
                {
                    SystemMessages.Entry message = live[i];
                    row.Label.text = message.Text;
                    row.Label.color = ColourFor(message.Severity);
                }

                // Rows fade rather than snapping on and off, so a message leaving does not make
                // the ones under it jump up the screen in one frame.
                float target = used && shown ? 1f : 0f;
                if (!Mathf.Approximately(row.Alpha, target))
                {
                    row.Alpha = Mathf.MoveTowards(row.Alpha, target, Time.unscaledDeltaTime / fadeDuration);
                    row.Group.alpha = row.Alpha;
                }

                row.Rect.gameObject.SetActive(used || row.Alpha > 0.001f);
            }
        }

        /// <summary>The stack draws the quiet half of the channel, so it only ever uses ink.</summary>
        private static Color ColourFor(MessageSeverity severity) => severity switch
        {
            MessageSeverity.Notice => VisorStyle.Ink,
            _ => VisorStyle.InkDim,
        };

        private void EnsureRows(int wanted)
        {
            // Pooled up to the stack depth and never shrunk: the depth is 4, so the whole pool is
            // built within the first few seconds and nothing allocates again.
            while (rows.Count < Mathf.Max(wanted, SystemMessages.StackDepth))
            {
                rows.Add(BuildRow(rows.Count));
            }
        }

        private Entry BuildRow(int index)
        {
            RectTransform rect = UIBuilder.Rect($"Message{index}", column);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, VisorStyle.BodySize + lineSpacing);
            rect.anchoredPosition = new Vector2(0f, -index * (VisorStyle.BodySize + lineSpacing));

            var rowGroup = rect.gameObject.AddComponent<CanvasGroup>();
            rowGroup.alpha = 0f;
            rowGroup.interactable = false;
            rowGroup.blocksRaycasts = false;

            TextMeshProUGUI label = UIBuilder.LabelIn(rect, "Text", string.Empty, VisorStyle.BodySize,
                                                      VisorStyle.Ink, TextAlignmentOptions.Left);
            label.characterSpacing = VisorStyle.LabelTracking * 0.5f;

            return new Entry { Rect = rect, Group = rowGroup, Label = label, Alpha = 0f };
        }

        private void Build()
        {
            var canvasObject = new GameObject("MessageCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            // UIScale is the only thing in the project that may configure a CanvasScaler.
            UIScale.Configure(canvasObject.GetComponent<CanvasScaler>());

            group = canvasObject.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;

            column = UIBuilder.Rect("Column", (RectTransform)canvasObject.transform);
            column.anchorMin = column.anchorMax = screenAnchor;
            column.pivot = new Vector2(0f, 1f);
            column.anchoredPosition = Vector2.zero;
            column.sizeDelta = new Vector2(width, SystemMessages.StackDepth * (VisorStyle.BodySize + lineSpacing));
        }
    }
}
