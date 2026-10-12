// Who is talking right now, on the right edge of the screen — in the lobby and in the world.
//
// Two principles decided the shape (docs/game-development-constitution):
//
// - GDC-L1-UX-0003: every action gets feedback. The first row is always YOURS whenever there is
//   anything to know about your own microphone: "You" while you are transmitting, "You · live" while
//   a push-to-talk channel is open over silence — a latched toggle is live whether or not you are
//   speaking, and must never be forgotten — and "You · muted" for as long as you have muted yourself.
// - GDC-L1-UX-0006: never encode information in colour alone. Speaking is a pip that appears and
//   breathes (VoicePip), not a name that changes tint; your own state is spelled out in words; and a
//   speaker's team is shown by its colour AND its tag ("T2"), so it still reads for a colour-blind
//   player.
//
// Rows are ordered you, your squad, then the other teams in team order — the question that matters
// mid-fight is whether the voice behind you is a teammate or an enemy.
//
// It is read every frame, against UI.md's "HUD data sources are events" rule, deliberately: whether
// someone is speaking is a continuous signal that changes on every 60 ms frame, like the input
// meter, and an event per frame per speaker would be polling with extra steps.
//
// The right edge because the left is taken: chat owns the bottom-left, and the visor's message
// stack and gauges own the top-left. The canvas has no raycaster, so it can never take a click from
// the lobby buttons or anything else drawn beneath it.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Voice;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The speaking list. Bootstrapped from a static like the other in-game overlays, because it has
    /// to exist in the main menu's lobby and survive the load into the world.
    /// </summary>
    public sealed class VoiceSpeakingList : MonoBehaviour
    {
        /// <summary>Above menu pages (900) and the match screens, below chat (1500) — see UI.md.</summary>
        private const int SortingOrder = 1200;

        /// <summary>
        /// Rows before the rest collapse into "+N more". At 24-player open mic the full list would
        /// be a wall the eye has to search, which is the failure UX-0003 describes.
        /// </summary>
        private const int MaxRows = 6;

        private const float RowHeight = 40f;
        private const float RowWidth = 280f;
        private const float RowGap = 6f;
        private const float MarginRight = 28f;

        /// <summary>How far up the right edge the list's top sits, as a fraction of screen height.</summary>
        private const float TopFraction = 0.66f;

        private const float PipSize = 12f;
        private const float PipSlot = 38f;
        private const float LabelInsetRight = 14f;
        private const float BadgeWidth = 40f;

        /// <summary>The team stripe down each row's left edge.</summary>
        private const float StripeWidth = 4f;
        private const float StripeInset = 7f;

        private static VoiceSpeakingList instance;

        private readonly List<ulong> speaking = new List<ulong>();
        private readonly Dictionary<ulong, int> teamOf = new Dictionary<ulong, int>();
        private readonly List<Row> rows = new List<Row>();

        private Comparison<ulong> bySquad;
        private int localTeam;

        private GameObject canvasObject;
        private RectTransform stack;

        /// <summary>What your own row says, if anything.</summary>
        private enum SelfState
        {
            None,
            Talking,
            Live,
            Muted,
        }

        private sealed class Row
        {
            public RectTransform Rect;
            public VoicePip Pip;
            public Image Stripe;
            public TextMeshProUGUI Label;
            public TextMeshProUGUI Badge;
            public string Shown;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var host = new GameObject(nameof(VoiceSpeakingList));
            DontDestroyOnLoad(host);
            instance = host.AddComponent<VoiceSpeakingList>();
        }

        // Cached once: a method group converted to a delegate allocates, and this sorts every frame.
        private void Awake() => bySquad = CompareBySquad;

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void LateUpdate()
        {
            // Nobody to hear you — singleplayer, or a host still waiting alone in the lobby — so
            // nothing here has anything to say.
            if (!VoiceSession.HasCompany)
            {
                SetVisible(false);
                return;
            }

            VoiceSession.CollectSpeaking(speaking);
            SelfState self = SelfStateNow();

            int total = speaking.Count + (self != SelfState.None ? 1 : 0);
            if (total == 0)
            {
                SetVisible(false);
                return;
            }

            EnsureBuilt();
            SetVisible(true);
            OrderBySquad();

            // With more speakers than rows, the last row becomes the count of the rest.
            int named = total <= MaxRows ? total : MaxRows - 1;
            float time = Time.unscaledTime;
            int shown = 0;

            switch (self)
            {
                case SelfState.Talking:
                    Fill(shown++, "You", localTeam, withPip: true, UITheme.Bright, time);
                    break;
                case SelfState.Live:
                    Fill(shown++, "You · live", localTeam, withPip: false, UITheme.Muted, time);
                    break;
                case SelfState.Muted:
                    Fill(shown++, "You · muted", localTeam, withPip: false, UITheme.Danger, time);
                    break;
            }

            for (int i = 0; shown < named && i < speaking.Count; i++)
            {
                ulong id = speaking[i];
                Fill(shown++, VoiceSession.NameOf(id), teamOf[id], withPip: true, UITheme.Bright, time);
            }

            if (total > MaxRows)
                Fill(shown++, $"+{total - named} more", SpeakerTeams.NoTeam, withPip: false, UITheme.Faint, time);

            for (int i = shown; i < rows.Count; i++)
            {
                if (rows[i].Rect.gameObject.activeSelf) rows[i].Rect.gameObject.SetActive(false);
            }
        }

        private static SelfState SelfStateNow()
        {
            // Muted outranks everything: it is the state a player most needs to be reminded of.
            if (VoiceSession.IsSelfMuted) return SelfState.Muted;
            if (VoiceSession.IsTransmitting) return SelfState.Talking;
            return VoiceSession.IsPushToTalkOpen ? SelfState.Live : SelfState.None;
        }

        // -------------------------------------------------------------------- ordering

        /// <summary>
        /// Your squad first, then the other teams in team order, then anyone with no team — which in
        /// story mode is everyone, so the list is simply by client id there.
        /// </summary>
        private void OrderBySquad()
        {
            localTeam = SpeakerTeams.LocalTeam;

            teamOf.Clear();
            for (int i = 0; i < speaking.Count; i++) teamOf[speaking[i]] = SpeakerTeams.TeamOf(speaking[i]);

            speaking.Sort(bySquad);
        }

        private int CompareBySquad(ulong a, ulong b)
        {
            int teamA = teamOf[a];
            int teamB = teamOf[b];

            int rankA = Rank(teamA);
            int rankB = Rank(teamB);
            if (rankA != rankB) return rankA.CompareTo(rankB);
            if (teamA != teamB) return teamA.CompareTo(teamB);

            return a.CompareTo(b);
        }

        private int Rank(int team)
        {
            if (team < 0) return 2;
            return team == localTeam ? 0 : 1;
        }

        // ----------------------------------------------------------------------- rows

        private void Fill(int index, string text, int team, bool withPip, Color labelColor, float time)
        {
            Row row = RowAt(index);

            if (!row.Rect.gameObject.activeSelf) row.Rect.gameObject.SetActive(true);

            if (row.Shown != text)
            {
                row.Label.text = text;
                row.Shown = text;
            }

            row.Label.color = labelColor;

            // Colour and tag together, never the colour alone — the tag is what still reads for a
            // colour-blind player. The overflow row passes no team, so it gets neither.
            bool hasTeam = SpeakerTeams.TryColorOf(team, out Color teamColor);

            row.Stripe.enabled = hasTeam;
            row.Badge.enabled = hasTeam;

            if (hasTeam)
            {
                row.Stripe.color = teamColor;
                row.Badge.color = teamColor;

                string tag = SpeakerTeams.TagOf(team);
                if (row.Badge.text != tag) row.Badge.text = tag;
            }

            row.Pip.Show(withPip);
            if (!withPip) return;

            row.Pip.SetColor(hasTeam ? teamColor : UITheme.Accent);
            row.Pip.Animate(time, 1f);
        }

        // ------------------------------------------------------------------- building

        private void EnsureBuilt()
        {
            if (canvasObject != null) return;

            canvasObject = new GameObject("VoiceSpeakingCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            UIScale.Configure(canvasObject.GetComponent<CanvasScaler>());

            stack = UIBuilder.Rect("Stack", canvasObject.transform);
            stack.anchorMin = stack.anchorMax = new Vector2(1f, TopFraction);
            stack.pivot = new Vector2(1f, 1f);
            stack.anchoredPosition = new Vector2(-MarginRight, 0f);
            stack.sizeDelta = new Vector2(RowWidth, MaxRows * (RowHeight + RowGap));
        }

        private Row RowAt(int index)
        {
            while (rows.Count <= index) rows.Add(BuildRow(rows.Count));
            return rows[index];
        }

        private Row BuildRow(int index)
        {
            RectTransform rect = UIBuilder.Rect("Speaker", stack);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(0f, -index * (RowHeight + RowGap));
            rect.sizeDelta = new Vector2(RowWidth, RowHeight);

            UIBuilder.Sprite(UIBuilder.Fill(UIBuilder.Rect("Chip", rect)), UITheme.ChipSprite, UITheme.Panel);

            RectTransform stripeRect = UIBuilder.Rect("TeamStripe", rect);
            stripeRect.anchorMin = new Vector2(0f, 0f);
            stripeRect.anchorMax = new Vector2(0f, 1f);
            stripeRect.pivot = new Vector2(0f, 0.5f);
            stripeRect.offsetMin = new Vector2(0f, StripeInset);
            stripeRect.offsetMax = new Vector2(StripeWidth, -StripeInset);
            Image stripe = UIBuilder.Solid(stripeRect, UITheme.Accent);
            stripe.raycastTarget = false;
            stripe.enabled = false;

            RectTransform pipSlot = UIBuilder.LeftColumn(UIBuilder.Rect("PipSlot", rect), 0f, PipSlot);
            var pip = new VoicePip(pipSlot, PipSize, UITheme.Accent);

            RectTransform labelRect = UIBuilder.Fill(UIBuilder.Rect("Name", rect), PipSlot, 0f,
                                                     LabelInsetRight + BadgeWidth, 0f);
            TextMeshProUGUI label = UIBuilder.Label(labelRect, string.Empty, UITheme.LabelSize,
                                                    UITheme.Bright, TextAlignmentOptions.Left);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;

            RectTransform badgeRect = UIBuilder.RightColumn(UIBuilder.Rect("TeamTag", rect), LabelInsetRight,
                                                            BadgeWidth);
            TextMeshProUGUI badge = UIBuilder.Label(badgeRect, string.Empty, UITheme.CaptionSize,
                                                    UITheme.Accent, TextAlignmentOptions.Right, FontStyles.Bold);
            badge.raycastTarget = false;
            badge.enabled = false;

            rect.gameObject.SetActive(false);
            return new Row { Rect = rect, Pip = pip, Stripe = stripe, Label = label, Badge = badge };
        }

        private void SetVisible(bool visible)
        {
            if (canvasObject != null && canvasObject.activeSelf != visible)
                canvasObject.SetActive(visible);
        }
    }
}
