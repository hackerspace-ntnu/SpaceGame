// Lines said near this machine's player, over the heads of whoever said them.
//
// The dialog popup is one box for the conversation the player is having; a settlement is many
// people talking at once, mostly not to you. Those lines float here instead — a view of every
// Speaker on this machine, through Speaker.AnySaid, so nothing new crosses the wire: whatever made a
// character say something locally (chatter, a war cry, a resident's NetMsg.ResidentSaid) put it here.
//
// Bounded on purpose. At most a handful of bubbles, the more urgent channel winning a slot, and a
// line that loses is dropped rather than queued: a remark shown ten seconds late, after the speaker
// has walked off, is worse than one never shown. The cap and the ranking are the UI's hierarchy:
// what matters most (a reply to you, a warning) wins the eye, ambient talk recedes
// (GDC-L1-UX-0003 readability and hierarchy; GDC-L1-UX-0002 cognitive load).
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using SpaceGame.Presentation.Speech;

namespace SpaceGame.Presentation
{
    [DisallowMultipleComponent]
    public class SpeechBubbles : MonoBehaviour
    {
        [Header("Audience")]
        [Tooltip("Lines said farther than this from the camera (m) are not shown. Matches the residents' earshot.")]
        [SerializeField, Min(1f)] private float earshot = 20f;

        [Tooltip("Most bubbles on screen at once. A lower-priority line arriving when full is dropped.")]
        [SerializeField, Min(1)] private int maxVisible = 4;

        [Header("Look")]
        [SerializeField] private Color textColor = new(1f, 0.97f, 0.88f);
        [SerializeField] private float fontSize = 24f;

        [Tooltip("Canvas units a bubble may be wide before its line wraps.")]
        [SerializeField] private float width = 420f;

        [Tooltip("Canvas units above the projected head point.")]
        [SerializeField] private float verticalOffset = 10f;

        [Tooltip("Seconds a bubble takes to fade out at the end of its line.")]
        [SerializeField, Min(0f)] private float fadeSeconds = 0.4f;

        private sealed class Bubble
        {
            public TextMeshProUGUI Text;
            public Speaker Speaker;     // null = free slot
            public int Line;            // the speaker's line number this bubble shows
            public float HeadOffset;    // metres above the speaker's origin
        }

        private readonly List<Bubble> bubbles = new();

        private void OnEnable() => Speaker.AnySaid += OnSaid;

        private void OnDisable()
        {
            Speaker.AnySaid -= OnSaid;
            foreach (Bubble bubble in bubbles) Free(bubble);
        }

        private void OnSaid(Speaker speaker, string text, SpeechChannel channel)
        {
            // The popup shows dialog, and a line already in the popup here would only be said twice.
            if (channel == SpeechChannel.Dialog || speaker.OnPopup) return;

            WorldOverlay overlay = WorldOverlay.Instance;
            if (overlay == null || !InEarshot(overlay, speaker)) return;

            Bubble slot = Find(speaker) ?? FreeSlot(overlay) ?? Weakest();
            if (slot == null) return;
            if (slot.Speaker != null && slot.Speaker != speaker &&
                Rank(slot.Speaker.CurrentChannel) >= Rank(channel))
                return;

            if (slot.Speaker != speaker)
                slot.HeadOffset = WorldOverlay.HeadOffset(speaker.gameObject);

            slot.Speaker = speaker;
            slot.Line = speaker.LineNumber;
            slot.Text.text = text;
            slot.Text.maxVisibleCharacters = 0;
        }

        private void LateUpdate()
        {
            WorldOverlay overlay = WorldOverlay.Instance;

            foreach (Bubble bubble in bubbles)
            {
                // "is null" is a free slot; a speaker destroyed mid-line is Unity-null and is freed below.
                if (bubble.Speaker is null) continue;

                Speaker speaker = bubble.Speaker;
                if (speaker == null || speaker.LineNumber != bubble.Line || !speaker.IsShowing)
                {
                    Free(bubble);
                    continue;
                }

                Vector3 head = speaker.transform.position + Vector3.up * bubble.HeadOffset;
                Vector2 point = default;
                bool visible = overlay != null && InEarshot(overlay, speaker) && overlay.Project(head, out point);
                visible = visible && overlay.IsOnScreen(point, width * 0.5f);
                bubble.Text.enabled = visible;
                if (!visible) continue;

                bubble.Text.rectTransform.anchoredPosition = point + new Vector2(0f, verticalOffset);
                bubble.Text.maxVisibleCharacters = speaker.VisibleCharacters;

                float remaining = speaker.LineEndsAt - Time.time;
                bubble.Text.alpha = fadeSeconds > 0f ? Mathf.Clamp01(remaining / fadeSeconds) : 1f;
            }
        }

        private bool InEarshot(WorldOverlay overlay, Speaker speaker)
        {
            Camera eye = overlay.Eye;
            return eye != null &&
                   (speaker.transform.position - eye.transform.position).sqrMagnitude <= earshot * earshot;
        }

        // The channel enum is declared in priority order, Ambient lowest.
        private static int Rank(SpeechChannel channel) => (int)channel;

        private Bubble Find(Speaker speaker)
        {
            foreach (Bubble bubble in bubbles)
                if (bubble.Speaker == speaker) return bubble;
            return null;
        }

        private Bubble FreeSlot(WorldOverlay overlay)
        {
            foreach (Bubble bubble in bubbles)
                if (bubble.Speaker == null && bubble.Text != null) return bubble;

            if (bubbles.Count >= maxVisible) return null;

            var created = new Bubble { Text = WorldOverlay.CreateLabel(overlay.Layer, "SpeechBubble", fontSize, width) };
            created.Text.textWrappingMode = TextWrappingModes.Normal;
            created.Text.alignment = TextAlignmentOptions.Bottom;
            created.Text.rectTransform.pivot = new Vector2(0.5f, 0f);
            created.Text.color = textColor;
            created.Text.enabled = false;
            bubbles.Add(created);
            return created;
        }

        /// <summary>The lowest-ranked bubble showing; on a tie, the one closest to finishing.</summary>
        private Bubble Weakest()
        {
            Bubble weakest = null;
            foreach (Bubble bubble in bubbles)
            {
                if (bubble.Speaker == null || bubble.Text == null) continue;
                if (weakest == null) { weakest = bubble; continue; }

                int rank = Rank(bubble.Speaker.CurrentChannel), weakestRank = Rank(weakest.Speaker.CurrentChannel);
                if (rank < weakestRank || (rank == weakestRank && bubble.Speaker.LineEndsAt < weakest.Speaker.LineEndsAt))
                    weakest = bubble;
            }
            return weakest;
        }

        private static void Free(Bubble bubble)
        {
            bubble.Speaker = null;
            if (bubble.Text != null) bubble.Text.enabled = false;
        }
    }
}
