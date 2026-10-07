using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>How loudly a system message asks to be read, and therefore where it is drawn.</summary>
    public enum MessageSeverity
    {
        /// <summary>Something happened. Quiet, fades on its own.</summary>
        Info = 0,

        /// <summary>Something you should act on. Full ink, holds longer.</summary>
        Notice = 1,

        /// <summary>Something is wrong. Goes to the banner, holds while it stays true.</summary>
        Warning = 2,

        /// <summary>Something is about to kill you. Banner, critical colour, pulsing.</summary>
        Alarm = 3,
    }

    /// <summary>
    /// The one channel the game speaks to the player through.
    ///
    /// <para>
    /// System voice only — this is the suit and the world reporting facts, never a character
    /// talking. Anything with a speaker belongs in <c>NpcDialogPopupUI</c>, and anything from
    /// another player belongs in chat.
    /// </para>
    /// <para>
    /// Static and not a component, for <see cref="SpaceGame.Core.ChatLog"/>'s reason: messages
    /// must outlive scene loads. The world streams chunk scenes constantly and an arena can be
    /// loaded over the top, so a buffer living on a scene object would be emptied by events the
    /// player did not cause.
    /// </para>
    /// <para>
    /// <b>Messages are addressed by id.</b> Posting the same id again replaces that message rather
    /// than stacking a second copy, and <see cref="Clear"/> only ever takes down its own — a late
    /// clear cannot erase somebody else's newer message. This is the rule <c>PlayerHints</c>
    /// established and it is the reason two systems announcing the same condition do not fight.
    /// </para>
    /// <para>
    /// Nothing here draws. <see cref="VisorMessageStack"/> renders <see cref="MessageSeverity.Info"/>
    /// and <see cref="MessageSeverity.Notice"/>; <see cref="VisorWarningBanner"/> renders the rest.
    /// </para>
    /// </summary>
    public static class SystemMessages
    {
        /// <summary>One posted message. Immutable — a change is a repost under the same id.</summary>
        public readonly struct Entry
        {
            public readonly string Id;
            public readonly string Text;
            public readonly MessageSeverity Severity;

            /// <summary>Unscaled time this was posted. Drives the fade-in.</summary>
            public readonly float PostedAt;

            /// <summary>Unscaled time this expires, or infinity for "until cleared".</summary>
            public readonly float ExpiresAt;

            public Entry(string id, string text, MessageSeverity severity, float postedAt, float expiresAt)
            {
                Id = id;
                Text = text;
                Severity = severity;
                PostedAt = postedAt;
                ExpiresAt = expiresAt;
            }

            public bool IsBanner => Severity >= MessageSeverity.Warning;
        }

        /// <summary>
        /// How many <see cref="MessageSeverity.Info"/> / <see cref="MessageSeverity.Notice"/> lines
        /// the stack shows at once. Beyond this the oldest is dropped: a wall of text on a visor is
        /// read by nobody (<c>GDC-L1-UX-0003</c> — every element competes for attention).
        /// </summary>
        public const int StackDepth = 4;

        private static readonly List<Entry> entries = new(8);

        /// <summary>Raised whenever the set of live messages changes. Views redraw from it.</summary>
        public static event Action Changed;

        /// <summary>Live messages, oldest first. Never null.</summary>
        public static IReadOnlyList<Entry> Active => entries;

        /// <summary>
        /// Statics survive a domain reload, and survive entirely when "Enter Play Mode Options"
        /// has domain reloading off — so without this the second play session in an editor starts
        /// with the first one's warnings still on screen. Same guard as <c>ChatLog</c>.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            entries.Clear();
            // Subscribers are objects from the previous play session: gone, but a surviving static
            // delegate still points at them.
            Changed = null;
        }

        /// <summary>How long a severity holds when the caller does not say.</summary>
        public static float DefaultSeconds(MessageSeverity severity) => severity switch
        {
            MessageSeverity.Info => 4f,
            MessageSeverity.Notice => 6f,
            // A warning describes a condition, so it stays until the condition does not hold. The
            // poster is responsible for calling Clear.
            _ => float.PositiveInfinity,
        };

        /// <summary>Posts a message that lives for its severity's default time.</summary>
        public static void Post(string id, string text, MessageSeverity severity) =>
            Post(id, text, severity, DefaultSeconds(severity));

        /// <summary>
        /// Posts a message under <paramref name="id"/>, replacing any message already posted under
        /// that id. <paramref name="seconds"/> of <see cref="float.PositiveInfinity"/> holds it
        /// until <see cref="Clear"/>.
        /// </summary>
        public static void Post(string id, string text, MessageSeverity severity, float seconds)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(text)) return;

            float now = Time.unscaledTime;
            float expires = float.IsPositiveInfinity(seconds)
                ? float.PositiveInfinity
                : now + Mathf.Max(0f, seconds);

            int existing = IndexOf(id);
            if (existing >= 0)
            {
                // Keep the original PostedAt when the text is unchanged, so a warning re-posted
                // every frame while its condition holds does not restart its fade-in forever.
                Entry previous = entries[existing];
                float postedAt = previous.Text == text && previous.Severity == severity
                    ? previous.PostedAt
                    : now;

                entries[existing] = new Entry(id, text, severity, postedAt, expires);
            }
            else
            {
                entries.Add(new Entry(id, text, severity, now, expires));
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// Takes down the message with this id. A message someone else has since posted under a
        /// different id stays up. Safe to call when nothing is posted.
        /// </summary>
        public static void Clear(string id)
        {
            int index = IndexOf(id);
            if (index < 0) return;

            entries.RemoveAt(index);
            Changed?.Invoke();
        }

        /// <summary>Drops everything. For a session ending, not for ordinary use.</summary>
        public static void ClearAll()
        {
            if (entries.Count == 0) return;

            entries.Clear();
            Changed?.Invoke();
        }

        /// <summary>
        /// Drops expired messages. Called by the views each frame rather than by a ticker of its
        /// own, so this class needs no scene presence at all.
        /// </summary>
        public static void DropExpired()
        {
            float now = Time.unscaledTime;
            bool removed = false;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (now < entries[i].ExpiresAt) continue;

                entries.RemoveAt(i);
                removed = true;
            }

            if (removed) Changed?.Invoke();
        }

        /// <summary>
        /// The newest <see cref="StackDepth"/> stack-bound messages, oldest first, written into
        /// <paramref name="into"/>. Allocation-free for the caller, which redraws every frame.
        /// </summary>
        public static void CollectStack(List<Entry> into)
        {
            into.Clear();

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].IsBanner) continue;
                into.Add(entries[i]);
            }

            // Oldest first, so the newest is at the bottom nearest the reading eye; trimming from
            // the front is what makes "latest wins" true when the stack is full.
            while (into.Count > StackDepth) into.RemoveAt(0);
        }

        /// <summary>
        /// The single message the banner should show: highest severity, and among equals the most
        /// recently posted. Returns false when nothing is banner-worthy.
        /// </summary>
        public static bool TryGetBanner(out Entry banner)
        {
            banner = default;
            bool found = false;

            foreach (Entry entry in entries)
            {
                if (!entry.IsBanner) continue;

                if (!found || entry.Severity > banner.Severity ||
                    (entry.Severity == banner.Severity && entry.PostedAt > banner.PostedAt))
                {
                    banner = entry;
                    found = true;
                }
            }

            return found;
        }

        private static int IndexOf(string id)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Id == id) return i;
            }

            return -1;
        }
    }
}
