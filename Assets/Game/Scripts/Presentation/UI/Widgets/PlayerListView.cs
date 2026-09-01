using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The pause menu's list of everyone in the session: name, who you are, who is hosting, and
    /// each connection's round-trip time.
    /// <para>
    /// Rows are pooled rather than destroyed and rebuilt. The list rebuilds on every roster change
    /// and once a second for the ping figures, and churning a dozen GameObjects at that rate for a
    /// panel that is usually showing the same four people is wasted allocation.
    /// </para>
    /// </summary>
    public class PlayerListView : MonoBehaviour
    {
        private const float RowHeight = 52f;
        private const float PingRefreshSeconds = 1f;

        private RectTransform container;
        private TextMeshProUGUI emptyLabel;
        private float nextPingRefresh;

        private readonly List<RowWidgets> pool = new();

        private struct RowWidgets
        {
            public GameObject Host;
            public Image Chip;
            public Image Dot;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Tag;
            public TextMeshProUGUI Ping;
        }

        public static PlayerListView Create(RectTransform parent)
        {
            var rect = UIBuilder.Rect("PlayerList", parent);
            var group = UIBuilder.Column(rect, 6f);
            group.childControlHeight = true;

            var view = rect.gameObject.AddComponent<PlayerListView>();
            view.container = rect;

            var emptyRect = UIBuilder.Rect("Empty", rect);
            UIBuilder.FixedHeight(emptyRect, 46f);
            view.emptyLabel = UIBuilder.Label(UIBuilder.Fill(UIBuilder.Rect("Text", emptyRect), 20f, 0f, 0f, 0f),
                "No session running — you are playing solo.", UITheme.LabelSize, UITheme.Faint);

            return view;
        }

        private void OnEnable()
        {
            PlayerIdentity.RosterChanged += Rebuild;
            nextPingRefresh = 0f;
            Rebuild();
        }

        private void OnDisable()
        {
            PlayerIdentity.RosterChanged -= Rebuild;
        }

        private void Update()
        {
            // Unscaled, because the menu that owns this list is what stopped the game clock.
            if (Time.unscaledTime < nextPingRefresh) return;

            nextPingRefresh = Time.unscaledTime + PingRefreshSeconds;
            Rebuild();
        }

        public void Rebuild()
        {
            if (container == null) return;

            List<PlayerRoster.Entry> roster = PlayerRoster.Build();

            emptyLabel.gameObject.SetActive(roster.Count == 0);
            // The empty note is the first child; keeping it there means it does not jump to the
            // bottom of the column once rows have been created and hidden again.
            emptyLabel.transform.parent.SetSiblingIndex(0);

            EnsurePool(roster.Count);

            for (int i = 0; i < pool.Count; i++)
            {
                RowWidgets row = pool[i];
                bool used = i < roster.Count;
                row.Host.SetActive(used);
                if (!used) continue;

                PlayerRoster.Entry entry = roster[i];

                row.Name.text = entry.Name;
                row.Name.color = entry.IsLocal ? UITheme.Bright : UITheme.Muted;
                row.Chip.color = entry.IsLocal ? UITheme.AccentSoft : new Color(1f, 1f, 1f, 0.035f);
                row.Dot.color = entry.IsHost ? UITheme.AccentWarm : UITheme.Accent;

                row.Tag.text = BuildTag(entry);
                row.Ping.text = DescribePing(entry.PingMilliseconds);
                row.Ping.color = PingColor(entry.PingMilliseconds);
            }
        }

        private static string BuildTag(PlayerRoster.Entry entry)
        {
            if (entry.IsLocal && entry.IsHost) return "YOU · HOST";
            if (entry.IsLocal) return "YOU";
            return entry.IsHost ? "HOST" : string.Empty;
        }

        /// <summary>
        /// A dash rather than a zero where this peer cannot measure the link: only the server holds
        /// a connection to every client, so on a client every row but its own is unmeasurable and
        /// showing "0 ms" there would be a lie.
        /// </summary>
        private static string DescribePing(int milliseconds)
        {
            if (milliseconds < 0) return "—";
            return milliseconds == 0 ? "local" : $"{milliseconds} ms";
        }

        private static Color PingColor(int milliseconds)
        {
            if (milliseconds < 0) return UITheme.Faint;
            if (milliseconds == 0) return UITheme.Faint;
            if (milliseconds < 80) return new Color(0.42f, 0.83f, 0.53f, 1f);
            return milliseconds < 180 ? UITheme.AccentWarm : UITheme.Danger;
        }

        private void EnsurePool(int wanted)
        {
            while (pool.Count < wanted)
                pool.Add(BuildRow());
        }

        private RowWidgets BuildRow()
        {
            var rect = UIBuilder.Rect("Player", container);
            UIBuilder.FixedHeight(rect, RowHeight);

            Image chip = UIBuilder.Sprite(UIBuilder.Fill(UIBuilder.Rect("Chip", rect)), UITheme.ChipSprite,
                new Color(1f, 1f, 1f, 0.035f));

            var dotRect = UIBuilder.Rect("Dot", rect);
            dotRect.anchorMin = new Vector2(0f, 0.5f);
            dotRect.anchorMax = new Vector2(0f, 0.5f);
            dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.sizeDelta = new Vector2(10f, 10f);
            dotRect.anchoredPosition = new Vector2(24f, 0f);
            Image dot = UIBuilder.Sprite(dotRect, UITheme.CircleSprite, UITheme.Accent, Image.Type.Simple);

            var nameRect = UIBuilder.LeftColumn(UIBuilder.Rect("Name", rect), 44f, 300f);
            TextMeshProUGUI name = UIBuilder.Label(nameRect, string.Empty, UITheme.LabelSize, UITheme.Muted,
                TextAlignmentOptions.Left, FontStyles.Bold);

            var tagRect = UIBuilder.LeftColumn(UIBuilder.Rect("Tag", rect), 356f, 220f);
            TextMeshProUGUI tag = UIBuilder.Label(tagRect, string.Empty, UITheme.CaptionSize, UITheme.AccentWarm);
            tag.characterSpacing = 6f;

            var pingRect = UIBuilder.RightColumn(UIBuilder.Rect("Ping", rect), 22f, 110f);
            TextMeshProUGUI ping = UIBuilder.Label(pingRect, string.Empty, UITheme.CaptionSize, UITheme.Faint,
                TextAlignmentOptions.Right);

            return new RowWidgets
            {
                Host = rect.gameObject,
                Chip = chip,
                Dot = dot,
                Name = name,
                Tag = tag,
                Ping = ping,
            };
        }
    }
}
