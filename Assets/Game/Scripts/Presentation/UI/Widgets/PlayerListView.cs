using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Voice;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The pause menu's list of everyone in the session: name, who you are, who is hosting, each
    /// connection's round-trip time — and how loud each person is to you.
    /// <para>
    /// Rows are pooled rather than destroyed and rebuilt. The list rebuilds on every roster change
    /// and once a second for the ping figures, and churning GameObjects at that rate for a panel
    /// that is usually showing the same few people is wasted allocation.
    /// </para>
    /// <para>
    /// In a versus match it is split into one section per team, your squad first. Every section
    /// stays open: GDC-L1-UX-0002 warns against hiding what players need, and a microphone that
    /// wants turning down is as likely to be an enemy's as a teammate's. Story mode has no teams
    /// and stays one plain list.
    /// </para>
    /// <para>
    /// Each row but your own carries a mute button, a 0-200% level slider and its value
    /// (<see cref="VoicePeerLevels"/>). The pooled row only writes its slider when it starts showing
    /// a DIFFERENT person — the once-a-second ping rebuild would otherwise yank the handle out from
    /// under a drag in progress.
    /// </para>
    /// </summary>
    public class PlayerListView : MonoBehaviour
    {
        private const float RowHeight = 52f;
        private const float HeaderHeight = 40f;
        private const float PingRefreshSeconds = 1f;

        // Left to right: marker, name, tag. The voice controls and ping hang off the right edge, so
        // a wider page widens the gap between them rather than stretching anything.
        private const float MarkerX = 24f;
        private const float NameX = 44f;
        private const float NameWidth = 236f;
        private const float TagX = 290f;
        private const float TagWidth = 110f;
        private const float PingInset = 22f;
        private const float PingWidth = 110f;
        private const float ControlsInset = 142f;
        private const float ControlsWidth = 330f;
        private const float MuteWidth = 78f;
        private const float MuteInsetY = 9f;
        private const float LevelValueWidth = 64f;
        private const float ControlGap = 10f;
        private const float PipSize = 12f;

        private static readonly Color IdleChip = new(1f, 1f, 1f, 0.035f);

        private RectTransform container;
        private TextMeshProUGUI emptyLabel;
        private float nextPingRefresh;

        private readonly List<RowWidgets> rows = new();
        private readonly List<HeaderWidgets> headers = new();
        private readonly List<PlayerRoster.Entry> ordered = new();

        private sealed class HeaderWidgets
        {
            public GameObject Host;
            public Image Swatch;
            public TextMeshProUGUI Title;
            public TextMeshProUGUI Count;
        }

        private sealed class RowWidgets
        {
            public GameObject Host;
            public Image Chip;
            public Image Dot;
            public VoicePip Pip;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Tag;
            public TextMeshProUGUI Ping;

            public GameObject Controls;
            public Slider Level;
            public TextMeshProUGUI LevelValue;
            public TextMeshProUGUI MuteLabel;

            // Who this pooled row is showing right now.
            public bool Bound;
            public ulong ClientId;
            public string AccountId;
            public bool IsLocal;
            public int Team;
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
            // Per frame: whether someone is speaking changes every 60 ms audio frame.
            AnimateSpeaking();

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

            int localTeam = LocalTeamOf(roster);
            Order(roster, localTeam);

            bool sectioned = AnyoneOnATeam(ordered);
            int sibling = 1;
            int rowIndex = 0;
            int headerIndex = 0;
            int section = int.MinValue;

            for (int i = 0; i < ordered.Count; i++)
            {
                PlayerRoster.Entry entry = ordered[i];

                if (sectioned && entry.Team != section)
                {
                    section = entry.Team;
                    HeaderWidgets header = HeaderAt(headerIndex++);
                    BindHeader(header, section, localTeam, HeadsOn(ordered, section));
                    header.Host.transform.SetSiblingIndex(sibling++);
                }

                RowWidgets row = RowAt(rowIndex++);
                BindRow(row, entry);
                row.Host.transform.SetSiblingIndex(sibling++);
            }

            for (int i = rowIndex; i < rows.Count; i++)
            {
                rows[i].Host.SetActive(false);
                rows[i].Bound = false;
            }

            for (int i = headerIndex; i < headers.Count; i++) headers[i].Host.SetActive(false);
        }

        // -------------------------------------------------------------------- ordering

        private static int LocalTeamOf(List<PlayerRoster.Entry> roster)
        {
            for (int i = 0; i < roster.Count; i++)
                if (roster[i].IsLocal) return roster[i].Team;

            return SpeakerTeams.NoTeam;
        }

        /// <summary>
        /// Your squad, then the other teams in order, then anyone without a team. Within a team the
        /// host comes first and then join order, which is how this list was already sorted.
        /// </summary>
        private void Order(List<PlayerRoster.Entry> roster, int localTeam)
        {
            ordered.Clear();
            ordered.AddRange(roster);

            ordered.Sort((a, b) =>
            {
                int rankA = SectionRank(a.Team, localTeam);
                int rankB = SectionRank(b.Team, localTeam);
                if (rankA != rankB) return rankA.CompareTo(rankB);
                if (a.Team != b.Team) return a.Team.CompareTo(b.Team);
                if (a.IsHost != b.IsHost) return a.IsHost ? -1 : 1;
                return a.ClientId.CompareTo(b.ClientId);
            });
        }

        private static int SectionRank(int team, int localTeam)
        {
            if (team < 0) return 2;
            return team == localTeam ? 0 : 1;
        }

        private static bool AnyoneOnATeam(List<PlayerRoster.Entry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Team >= 0) return true;

            return false;
        }

        private static int HeadsOn(List<PlayerRoster.Entry> entries, int team)
        {
            int heads = 0;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Team == team) heads++;

            return heads;
        }

        // --------------------------------------------------------------------- binding

        private static void BindHeader(HeaderWidgets header, int team, int localTeam, int heads)
        {
            header.Host.SetActive(true);

            string name = team >= 0 ? VersusRules.TeamName(team) : "NO TEAM";
            header.Title.text = team >= 0 && team == localTeam ? $"YOUR SQUAD · {name}" : name;
            header.Count.text = heads == 1 ? "1 player" : $"{heads} players";

            bool colored = SpeakerTeams.TryColorOf(team, out Color color);
            header.Swatch.enabled = colored;
            if (colored) header.Swatch.color = color;
        }

        private static void BindRow(RowWidgets row, PlayerRoster.Entry entry)
        {
            row.Host.SetActive(true);

            row.Name.text = entry.Name;
            row.Name.color = entry.IsLocal ? UITheme.Bright : UITheme.Muted;
            row.Chip.color = entry.IsLocal ? UITheme.AccentSoft : IdleChip;
            row.Dot.color = entry.IsHost ? UITheme.AccentWarm : UITheme.Accent;

            row.Tag.text = BuildTag(entry);
            row.Ping.text = DescribePing(entry.PingMilliseconds);
            row.Ping.color = PingColor(entry.PingMilliseconds);

            // The account id can arrive a moment after the player does. A level set in that gap is
            // held for the session only; once the id lands this counts as a rebind and the slider
            // shows the remembered level instead.
            string account = VoiceSession.AccountOf(entry.ClientId);
            bool rebind = !row.Bound || row.ClientId != entry.ClientId || row.AccountId != account;

            row.Bound = true;
            row.ClientId = entry.ClientId;
            row.AccountId = account;
            row.IsLocal = entry.IsLocal;
            row.Team = entry.Team;

            // You do not set your own level; everyone else does that.
            row.Controls.SetActive(!entry.IsLocal);
            if (entry.IsLocal) return;

            if (rebind) row.Level.SetValueWithoutNotify(VoicePeerLevels.GainOf(account, entry.ClientId));
            RefreshVoiceControls(row);
        }

        private static void RefreshVoiceControls(RowWidgets row)
        {
            bool muted = VoicePeerLevels.IsMuted(row.AccountId, row.ClientId);

            // The word carries the state as well as its colour, so muted never reads as colour alone.
            row.MuteLabel.text = muted ? "MUTED" : "MUTE";
            row.MuteLabel.color = muted ? UITheme.Danger : UITheme.Faint;

            row.LevelValue.text = Percent(VoicePeerLevels.GainOf(row.AccountId, row.ClientId));
            row.LevelValue.color = muted ? UITheme.Faint : UITheme.Bright;
        }

        private void AnimateSpeaking()
        {
            float time = Time.unscaledTime;

            for (int i = 0; i < rows.Count; i++)
            {
                RowWidgets row = rows[i];
                if (!row.Bound) continue;

                // Your own row lights while you transmit; everyone else's while they are heard.
                bool speaking = row.IsLocal ? VoiceSession.IsTransmitting : VoiceSession.IsSpeaking(row.ClientId);

                row.Dot.enabled = !speaking;
                row.Pip.Show(speaking);
                if (!speaking) continue;

                row.Pip.SetColor(SpeakerTeams.TryColorOf(row.Team, out Color team) ? team : UITheme.Accent);
                row.Pip.Animate(time, 1f);
            }
        }

        private static string BuildTag(PlayerRoster.Entry entry)
        {
            if (entry.IsLocal && entry.IsHost) return "YOU · HOST";
            if (entry.IsLocal) return "YOU";
            return entry.IsHost ? "HOST" : string.Empty;
        }

        private static string Percent(float gain) => $"{Mathf.RoundToInt(gain * 100f)}%";

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

        // -------------------------------------------------------------------- building

        private RowWidgets RowAt(int index)
        {
            while (rows.Count <= index) rows.Add(BuildRow());
            return rows[index];
        }

        private HeaderWidgets HeaderAt(int index)
        {
            while (headers.Count <= index) headers.Add(BuildHeader());
            return headers[index];
        }

        private HeaderWidgets BuildHeader()
        {
            var rect = UIBuilder.Rect("Team", container);
            UIBuilder.FixedHeight(rect, HeaderHeight);

            var swatchRect = UIBuilder.Rect("Swatch", rect);
            swatchRect.anchorMin = swatchRect.anchorMax = new Vector2(0f, 0.5f);
            swatchRect.pivot = new Vector2(0.5f, 0.5f);
            swatchRect.sizeDelta = new Vector2(8f, 22f);
            swatchRect.anchoredPosition = new Vector2(MarkerX, 0f);
            Image swatch = UIBuilder.Sprite(swatchRect, UITheme.Rounded(4), Color.white);

            var titleRect = UIBuilder.Fill(UIBuilder.Rect("Title", rect), NameX, 0f, PingInset + 180f, 0f);
            TextMeshProUGUI title = UIBuilder.Label(titleRect, string.Empty, UITheme.CaptionSize, UITheme.Bright,
                TextAlignmentOptions.Left, FontStyles.Bold);
            title.characterSpacing = 8f;

            var countRect = UIBuilder.RightColumn(UIBuilder.Rect("Count", rect), PingInset, 160f);
            TextMeshProUGUI count = UIBuilder.Label(countRect, string.Empty, UITheme.CaptionSize, UITheme.Faint,
                TextAlignmentOptions.Right);

            rect.gameObject.SetActive(false);
            return new HeaderWidgets { Host = rect.gameObject, Swatch = swatch, Title = title, Count = count };
        }

        private RowWidgets BuildRow()
        {
            var rect = UIBuilder.Rect("Player", container);
            UIBuilder.FixedHeight(rect, RowHeight);

            var row = new RowWidgets { Host = rect.gameObject };

            row.Chip = UIBuilder.Sprite(UIBuilder.Fill(UIBuilder.Rect("Chip", rect)), UITheme.ChipSprite, IdleChip);

            var markerRect = UIBuilder.Rect("Marker", rect);
            markerRect.anchorMin = markerRect.anchorMax = new Vector2(0f, 0.5f);
            markerRect.pivot = new Vector2(0.5f, 0.5f);
            markerRect.sizeDelta = new Vector2(10f, 10f);
            markerRect.anchoredPosition = new Vector2(MarkerX, 0f);
            row.Dot = UIBuilder.Sprite(markerRect, UITheme.CircleSprite, UITheme.Accent, Image.Type.Simple);

            // In the dot's own place: while someone speaks the dot steps aside for the pip.
            row.Pip = new VoicePip(markerRect, PipSize, UITheme.Accent);

            var nameRect = UIBuilder.LeftColumn(UIBuilder.Rect("Name", rect), NameX, NameWidth);
            row.Name = UIBuilder.Label(nameRect, string.Empty, UITheme.LabelSize, UITheme.Muted,
                TextAlignmentOptions.Left, FontStyles.Bold);
            row.Name.enableWordWrapping = false;
            row.Name.overflowMode = TextOverflowModes.Ellipsis;

            var tagRect = UIBuilder.LeftColumn(UIBuilder.Rect("Tag", rect), TagX, TagWidth);
            row.Tag = UIBuilder.Label(tagRect, string.Empty, UITheme.CaptionSize, UITheme.AccentWarm);
            row.Tag.characterSpacing = 6f;

            var pingRect = UIBuilder.RightColumn(UIBuilder.Rect("Ping", rect), PingInset, PingWidth);
            row.Ping = UIBuilder.Label(pingRect, string.Empty, UITheme.CaptionSize, UITheme.Faint,
                TextAlignmentOptions.Right);

            BuildVoiceControls(rect, row);
            return row;
        }

        private static void BuildVoiceControls(RectTransform rect, RowWidgets row)
        {
            var controls = UIBuilder.RightColumn(UIBuilder.Rect("Voice", rect), ControlsInset, ControlsWidth);
            row.Controls = controls.gameObject;

            // Mute: its own button rather than the slider's zero, so muting and unmuting is one click
            // and puts the level back exactly where it was.
            var muteSlot = UIBuilder.LeftColumn(UIBuilder.Rect("Mute", controls), 0f, MuteWidth);
            var muteRect = UIBuilder.Fill(UIBuilder.Rect("Button", muteSlot), 0f, MuteInsetY, 0f, MuteInsetY);
            Image muteChip = UIBuilder.Sprite(muteRect, UITheme.ChipSprite, Color.white);
            row.MuteLabel = UIBuilder.LabelIn(muteRect, "Text", "MUTE", UITheme.CaptionSize, UITheme.Faint,
                TextAlignmentOptions.Center, FontStyles.Bold);

            UIBuilder.Clickable(muteRect, muteChip, new Color(1f, 1f, 1f, 0.06f), new Color(1f, 1f, 1f, 0.14f))
                .onClick.AddListener(() =>
                {
                    if (!row.Bound || row.IsLocal) return;

                    VoicePeerLevels.SetMuted(row.AccountId, row.ClientId,
                                             !VoicePeerLevels.IsMuted(row.AccountId, row.ClientId));
                    RefreshVoiceControls(row);
                });

            // Level, 0-200%.
            var sliderRect = UIBuilder.Fill(UIBuilder.Rect("Level", controls), MuteWidth + ControlGap, 0f,
                                            LevelValueWidth + ControlGap, 0f);
            row.Level = SettingsWidgets.BuildSlider(sliderRect, VoicePeerLevels.MinGain, VoicePeerLevels.MaxGain);

            row.Level.onValueChanged.AddListener(value =>
            {
                if (!row.Bound || row.IsLocal) return;

                // Snapped here rather than on the Slider: a listener that rounds the slider's own
                // value cannot change the argument already handed to the listeners behind it.
                float snapped = Mathf.Round(value / VoicePeerLevels.GainStep) * VoicePeerLevels.GainStep;
                VoicePeerLevels.SetGain(row.AccountId, row.ClientId, snapped);

                row.Level.SetValueWithoutNotify(VoicePeerLevels.GainOf(row.AccountId, row.ClientId));
                RefreshVoiceControls(row);
            });

            var valueRect = UIBuilder.RightColumn(UIBuilder.Rect("Value", controls), 0f, LevelValueWidth);
            row.LevelValue = UIBuilder.Label(valueRect, "100%", UITheme.ValueSize, UITheme.Bright,
                TextAlignmentOptions.Right, FontStyles.Bold);
        }
    }
}
