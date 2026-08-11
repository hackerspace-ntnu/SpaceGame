// Match leaderboard: held open with Tab during play, pinned open by MatchResultUI when the
// match ends.
//
// Built at runtime for the same reason MatchResultUI is: MinigameArena is additively loaded on
// every peer and carries no UI GameObjects, so a scene-authored panel would have to be
// duplicated per arena. Fields stay serialized so a designed panel can replace the generated one
// later without touching this logic.
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    public class MatchLeaderboardUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private RectTransform rowContainer;

        // Shared with MinigameConfigUI and MatchResultUI so the minigame screens read as one set.
        private static readonly Color Backdrop = new(0.02f, 0.03f, 0.06f, 0.88f);
        private static readonly Color Accent = new(0.239f, 0.549f, 0.949f, 1f);
        private static readonly Color Muted = new(0.62f, 0.70f, 0.82f, 1f);
        private static readonly Color RowTint = new(1f, 1f, 1f, 0.04f);
        private static readonly Color LocalRowTint = new(0.239f, 0.549f, 0.949f, 0.20f);

        private const int MaxRows = 24;
        private const float RowHeight = 34f;
        private const float PanelWidth = 760f;
        private const float HeaderHeight = 64f;

        private readonly List<Row> rows = new();
        private RectTransform panelRect;
        private bool pinned;
        private bool scoresDirty = true;

        private struct Row
        {
            public GameObject Host;
            public Image Background;
            public TextMeshProUGUI Rank;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Kills;
            public TextMeshProUGUI Deaths;
        }

        // Called by MatchManager on every peer as the match starts.
        public static MatchLeaderboardUI Ensure()
        {
            var existing = FindFirstObjectByType<MatchLeaderboardUI>(FindObjectsInactive.Include);
            if (existing != null) return existing;

            var go = new GameObject("MatchLeaderboardUI");
            return go.AddComponent<MatchLeaderboardUI>();
        }

        // MatchResultUI calls this so the final standings stay up without holding Tab.
        public void SetPinned(bool value)
        {
            pinned = value;
            scoresDirty = true;
        }

        private void Awake()
        {
            if (panel == null)
                BuildOverlay();

            SetVisible(false);
        }

        private void OnEnable() => MatchManager.OnScoresChanged += MarkDirty;
        private void OnDisable() => MatchManager.OnScoresChanged -= MarkDirty;

        private void MarkDirty() => scoresDirty = true;

        private void Update()
        {
            bool held = Keyboard.current != null && Keyboard.current.tabKey.isPressed;
            bool shouldShow = pinned || held;

            SetVisible(shouldShow);

            // Rebuilt only while on screen: nobody can see a table that is hidden, and a match with
            // bots respawning generates score changes continuously.
            if (shouldShow && scoresDirty)
            {
                Rebuild();
                scoresDirty = false;
            }
        }

        private void SetVisible(bool visible)
        {
            if (panel != null && panel.activeSelf != visible)
                panel.SetActive(visible);
        }

        private void Rebuild()
        {
            var table = new List<MatchScoreEntry>(MatchManager.Scores);
            table.Sort(MatchScoreEntry.Compare);

            ulong localClientId = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
                ? NetworkManager.Singleton.LocalClientId
                : MatchScoreEntry.NoClient;

            int shown = Mathf.Min(table.Count, MaxRows);

            // Shrink the backdrop to the rows actually in play. A fixed MaxRows-tall panel would be
            // most of the screen height in a four-bot match, with the bottom two-thirds empty.
            if (panelRect != null)
                panelRect.sizeDelta = new Vector2(PanelWidth, HeaderHeight + RowHeight * Mathf.Max(1, shown) + 24f);

            for (int i = 0; i < rows.Count; i++)
            {
                if (i >= shown)
                {
                    rows[i].Host.SetActive(false);
                    continue;
                }

                MatchScoreEntry entry = table[i];
                bool isLocal = !entry.IsBot && entry.ClientId == localClientId;

                Row row = rows[i];
                row.Host.SetActive(true);
                row.Background.color = isLocal ? LocalRowTint : RowTint;
                row.Rank.text = (i + 1).ToString();
                row.Name.text = isLocal ? "You" : entry.Name.ToString();
                row.Kills.text = entry.Kills.ToString();
                row.Deaths.text = entry.Deaths.ToString();

                Color textColor = isLocal ? Color.white : Muted;
                row.Rank.color = textColor;
                row.Name.color = textColor;
                row.Kills.color = isLocal ? Color.white : Color.white * 0.92f;
                row.Deaths.color = textColor;
            }
        }

        // ──────────────────────────────────────────────
        // Generated overlay
        // ──────────────────────────────────────────────

        private void BuildOverlay()
        {
            var canvasGo = new GameObject("LeaderboardCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above MatchResultUI (1000), whose panel is a full-screen backdrop that would otherwise
            // hide this entirely when pinned. The result screen's own headline and button are
            // anchored to the top and bottom edges, so nothing actually overlaps the table.
            canvas.sortingOrder = 1100;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            panel = CreateChild("Panel", canvasGo.transform, out panelRect);
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(PanelWidth, HeaderHeight + RowHeight * 4f + 24f);
            panel.AddComponent<Image>().color = Backdrop;

            var headerGo = CreateChild("Header", panel.transform, out RectTransform headerRect);
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(24f, 0f);
            headerRect.offsetMax = new Vector2(-24f, 0f);
            headerRect.sizeDelta = new Vector2(headerRect.sizeDelta.x, HeaderHeight);
            BuildColumns(headerGo.transform, "#", "PLAYER", "K", "D", Accent, 24, out _, out _, out _, out _);

            var containerGo = CreateChild("Rows", panel.transform, out RectTransform containerRect);
            containerRect.anchorMin = new Vector2(0f, 1f);
            containerRect.anchorMax = new Vector2(1f, 1f);
            containerRect.pivot = new Vector2(0.5f, 1f);
            containerRect.offsetMin = new Vector2(24f, 0f);
            containerRect.offsetMax = new Vector2(-24f, 0f);
            containerRect.anchoredPosition = new Vector2(0f, -HeaderHeight);
            containerRect.sizeDelta = new Vector2(containerRect.sizeDelta.x, RowHeight * MaxRows);
            rowContainer = containerRect;

            // Pooled up front rather than instantiated per rebuild: a full arena is 16 entities and
            // the table is rebuilt on every death.
            for (int i = 0; i < MaxRows; i++)
                rows.Add(BuildRow(i));
        }

        private Row BuildRow(int index)
        {
            var rowGo = CreateChild($"Row{index:00}", rowContainer, out RectTransform rowRect);
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.offsetMin = new Vector2(0f, 0f);
            rowRect.offsetMax = new Vector2(0f, 0f);
            rowRect.anchoredPosition = new Vector2(0f, -index * RowHeight);
            rowRect.sizeDelta = new Vector2(rowRect.sizeDelta.x, RowHeight - 3f);

            var background = rowGo.AddComponent<Image>();
            background.color = RowTint;

            BuildColumns(rowGo.transform, "", "", "", "", Color.white, 22,
                         out TextMeshProUGUI rank, out TextMeshProUGUI name,
                         out TextMeshProUGUI kills, out TextMeshProUGUI deaths);

            rowGo.SetActive(false);

            return new Row
            {
                Host = rowGo,
                Background = background,
                Rank = rank,
                Name = name,
                Kills = kills,
                Deaths = deaths,
            };
        }

        // Rank / name / kills / deaths share one column geometry so header and rows line up.
        private static void BuildColumns(Transform parent, string rankText, string nameText,
                                         string killsText, string deathsText, Color color, int fontSize,
                                         out TextMeshProUGUI rank, out TextMeshProUGUI name,
                                         out TextMeshProUGUI kills, out TextMeshProUGUI deaths)
        {
            rank = Column(parent, "Rank", rankText, color, fontSize, 0.00f, 0.10f, TextAlignmentOptions.Center);
            name = Column(parent, "Name", nameText, color, fontSize, 0.12f, 0.68f, TextAlignmentOptions.Left);
            kills = Column(parent, "Kills", killsText, color, fontSize, 0.70f, 0.84f, TextAlignmentOptions.Center);
            deaths = Column(parent, "Deaths", deathsText, color, fontSize, 0.86f, 1.00f, TextAlignmentOptions.Center);
        }

        private static TextMeshProUGUI Column(Transform parent, string name, string text, Color color,
                                              int fontSize, float anchorMinX, float anchorMaxX,
                                              TextAlignmentOptions alignment)
        {
            var go = CreateChild(name, parent, out RectTransform rect);
            rect.anchorMin = new Vector2(anchorMinX, 0f);
            rect.anchorMax = new Vector2(anchorMaxX, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var label = go.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
                label.font = TMP_Settings.defaultFontAsset;

            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = alignment;
            label.fontStyle = FontStyles.Bold;
            label.raycastTarget = false;
            return label;
        }

        private static GameObject CreateChild(string name, Transform parent, out RectTransform rect)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            rect = go.GetComponent<RectTransform>();
            return go;
        }
    }
}
