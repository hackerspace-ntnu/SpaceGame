using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using SpaceGame.Core;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The quest log: every quest the crew have taken on, newest first, what each asks for now, and which one the
    /// visor is following. Click an unfinished quest to follow it instead. Opened and closed with J; Esc closes.
    ///
    /// <para>
    /// <b>Purely local.</b> It reads <see cref="QuestLog"/> on this machine and writes only which quest this player
    /// follows (<see cref="QuestLog.SetActive"/>); every quest keeps advancing whichever one is chosen, so there is
    /// nothing to send and nothing to save. A gameplay overlay in the <see cref="BodyInventoryUI"/> mould: a
    /// singleton that lives across scene loads, builds its canvas lazily, and holds <see cref="GameplayMenuScope"/>
    /// while it is up — stopping the clock in a solo session, like any screen you read rather than play through.
    /// </para>
    /// </summary>
    public sealed class QuestLogUI : MonoBehaviour
    {
        private const float OpenSeconds = 0.14f;
        private const int SortingOrder = 2040;   // above the HUD, below the body screen and the dev browser

        [Tooltip("Width of the panel, in canvas units.")]
        [SerializeField] private float panelWidth = 820f;

        [Tooltip("Inner margin of the panel, in canvas units.")]
        [SerializeField] private float padding = 32f;

        [Tooltip("Gap between quests, in canvas units.")]
        [SerializeField] private float rowGap = 10f;

        [Tooltip("How often the open log re-reads the quests' current objectives, seconds.")]
        [SerializeField, Min(0.05f)] private float refreshSeconds = 0.25f;

        private static QuestLogUI instance;

        public static bool IsOpen => instance != null && instance.open;

        private InputControls inputs;

        private bool open;
        private bool built;
        private float visibility;
        private float nextRefresh;

        /// <summary>What the rows were last built from, so they are rebuilt only when the log changes.</summary>
        private string shownKey;
        private readonly StringBuilder key = new();

        private CanvasGroup group;
        private RectTransform rows;

        // ------------------------------------------------------------------ bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var go = new GameObject("QuestLog");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<QuestLogUI>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            // Its own copy of the UI map, because the player's own input asset is switched off
            // for as long as this screen holds the scope — see GameplayMenuScope.
            inputs = new InputControls();
            inputs.UI.QuestLog.performed += _ => Toggle();
            inputs.UI.Enable();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;

            Close();

            if (inputs != null)
            {
                inputs.UI.Disable();
                inputs.Dispose();
            }

            // Idempotent: Exit ignores an owner that holds no claim.
            GameplayMenuScope.Exit(this);
        }

        // ---------------------------------------------------------------------- input

        public void Toggle()
        {
            // J is a letter before it is a shortcut, so a focused field owns it.
            if (TextEntry.IsTyping) return;

            if (open) Close();
            else Open();
        }

        public void Open()
        {
            // Something else already holds the controls — the pause menu, the chat box, the body screen.
            if (open || GameplayMenuScope.IsActive) return;
            if (!GameplayMenuScope.Enter(this)) return;

            open = true;
            UIBuilder.EnsureEventSystem();
            if (!built) Build();

            group.gameObject.SetActive(true);
            group.blocksRaycasts = true;
            group.interactable = true;

            QuestLog.Changed += Refresh;
            Refresh();
        }

        public void Close()
        {
            if (!open) return;

            open = false;
            group.blocksRaycasts = false;
            group.interactable = false;
            QuestLog.Changed -= Refresh;

            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            GameplayMenuScope.Exit(this);
        }

        private void Update()
        {
            if (!built) return;

            float target = open ? 1f : 0f;
            if (!Mathf.Approximately(visibility, target))
            {
                // Unscaled: the scope stops the clock in a solo session.
                visibility = Mathf.MoveTowards(visibility, target, Time.unscaledDeltaTime / OpenSeconds);
                group.alpha = visibility * visibility * (3f - 2f * visibility);

                if (visibility <= 0f) group.gameObject.SetActive(false);
            }

            if (!open) return;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            if (Time.unscaledTime >= nextRefresh) Refresh();
        }

        // ------------------------------------------------------------------- content

        /// <summary>Rebuilds the rows when a quest opened, closed, moved on or was chosen; otherwise leaves them, so a hovered row stays hovered.</summary>
        private void Refresh()
        {
            nextRefresh = Time.unscaledTime + refreshSeconds;

            IReadOnlyList<IQuestEntry> entries = QuestLog.Entries;
            IQuestEntry active = QuestLog.Active;

            key.Clear();
            foreach (IQuestEntry entry in entries)
                key.Append(entry.Name).Append('|').Append(entry.Objective?.Title).Append('|')
                   .Append(entry.IsComplete).Append('|').Append(entry == active).Append('\n');

            string now = key.ToString();
            if (now == shownKey) return;
            shownKey = now;

            for (int i = rows.childCount - 1; i >= 0; i--) Destroy(rows.GetChild(i).gameObject);

            bool any = false;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].IsComplete) continue;
                Row(entries[i], entries[i] == active);
                any = true;
            }

            if (!any) Caption("No quests right now.");

            bool headed = false;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (!entries[i].IsComplete) continue;
                if (!headed) Heading("COMPLETED");
                headed = true;
                Finished(entries[i]);
            }
        }

        /// <summary>One unfinished quest: name, what it asks for now, what it is about. Click to follow it.</summary>
        private void Row(IQuestEntry entry, bool active)
        {
            RectTransform row = UIBuilder.Rect(entry.Name, rows);
            Image backing = UIBuilder.Sprite(row, UITheme.ChipSprite, active ? UITheme.AccentSoft : UITheme.PanelRaised);
            UIBuilder.Column(row, 4f, new RectOffset(20, 20, 14, 14));

            if (!active)
            {
                Button button = UIBuilder.Clickable(row, backing, UITheme.PanelRaised, UITheme.AccentSoft);
                button.onClick.AddListener(() => QuestLog.SetActive(entry));
            }

            Line(row, active ? $"{entry.Name}   <size=70%><color=#{Hex(UITheme.Accent)}>FOLLOWING</color></size>" : entry.Name,
                 UITheme.LabelSize, UITheme.Bright, FontStyles.Bold);

            ITrackedObjective objective = entry.Objective;
            if (objective != null && !string.IsNullOrEmpty(objective.Title))
                Line(row, objective.Title, UITheme.LabelSize, active ? UITheme.Bright : UITheme.Muted, FontStyles.Normal);

            if (!string.IsNullOrEmpty(entry.Summary))
                Line(row, entry.Summary, UITheme.CaptionSize, UITheme.Faint, FontStyles.Italic);
        }

        private void Finished(IQuestEntry entry) =>
            Line(rows, entry.Name, UITheme.LabelSize, UITheme.Faint, FontStyles.Strikethrough);

        private void Heading(string text)
        {
            UIBuilder.Spacer(rows, 12f);
            Line(rows, text, UITheme.CaptionSize, UITheme.Muted, FontStyles.Bold);
        }

        private void Caption(string text) => Line(rows, text, UITheme.LabelSize, UITheme.Muted, FontStyles.Normal);

        /// <summary>One wrapping line, on a direct child of the column so the column reads its height.</summary>
        private static void Line(RectTransform parent, string text, int size, Color color, FontStyles style)
        {
            TextMeshProUGUI label = UIBuilder.Label(UIBuilder.Rect("Line", parent), text, size, color,
                                                    TextAlignmentOptions.Left, style);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
        }

        private static string Hex(Color color) => ColorUtility.ToHtmlStringRGB(color);

        // --------------------------------------------------------------------- build

        private void Build()
        {
            built = true;

            var canvasGo = new GameObject("QuestLogCanvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            UIScale.Configure(canvasGo.GetComponent<CanvasScaler>());

            group = canvasGo.GetComponent<CanvasGroup>();
            group.alpha = 0f;

            var root = (RectTransform)canvasGo.transform;
            UIBuilder.Solid(UIBuilder.Fill(UIBuilder.Rect("Backdrop", root)), UITheme.Backdrop);

            RectTransform panel = UIBuilder.Rect("Panel", root);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(panelWidth, 0f);
            UIBuilder.Sprite(panel, UITheme.PanelSprite, UITheme.Panel);

            int inset = Mathf.RoundToInt(padding);
            UIBuilder.Column(panel, rowGap, new RectOffset(inset, inset, inset, inset));
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Line(panel, "QUESTS", UITheme.HeadingSize, UITheme.Bright, FontStyles.Bold);
            Line(panel, "Every quest keeps its progress. Click one to follow it on your visor.",
                 UITheme.CaptionSize, UITheme.Muted, FontStyles.Normal);
            UIBuilder.Spacer(panel, 8f);

            rows = UIBuilder.Rect("Quests", panel);
            UIBuilder.Column(rows, rowGap);

            UIBuilder.Spacer(panel, 8f);
            Line(panel, "J OR ESC TO CLOSE", UITheme.CaptionSize, UITheme.Faint, FontStyles.Bold);

            visibility = 0f;
            group.gameObject.SetActive(false);
        }
    }
}
