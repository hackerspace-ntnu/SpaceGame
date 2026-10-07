using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>
    /// Every chat line this machine has been shown this session, oldest first.
    /// <para>
    /// Static, and deliberately not a component. The log has to outlive scene loads — the world
    /// streams chunk scenes in and out constantly and a minigame arena is loaded additively over
    /// the top — so a buffer living on any scene object would be emptied by events the player did
    /// not cause. It is cleared when a session begins rather than when one ends, because "a new
    /// session started" is an event every peer sees (<see cref="ChatNetwork"/> spawning) whereas a
    /// clean disconnect is not.
    /// </para>
    /// </summary>
    public static class ChatLog
    {
        /// <summary>
        /// Lines kept for scrollback. Beyond this the oldest is dropped.
        ///
        /// A List used as a queue rather than a real ring buffer: at this size the shuffle is a
        /// hundred pointer copies once per message, and in exchange the UI can index the log
        /// directly in arrival order instead of unwrapping it.
        /// </summary>
        public const int Capacity = 100;

        private static readonly List<ChatMessage> messages = new(Capacity);

        /// <summary>Raised on every machine that receives a line, with the line.</summary>
        public static event Action<ChatMessage> Added;

        /// <summary>Raised when the whole log is dropped, so a view can throw its rows away.</summary>
        public static event Action Cleared;

        /// <summary>Oldest first. Never null.</summary>
        public static IReadOnlyList<ChatMessage> Messages => messages;

        public static int Count => messages.Count;

        /// <summary>
        /// Statics survive a domain reload, and survive entirely when "Enter Play Mode Options"
        /// has domain reloading switched off — so without this the second play session in an
        /// editor starts with the first one's chat still in it.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            messages.Clear();
            // The events are cleared too: their subscribers are objects from the previous play
            // session, which are gone but which a surviving static delegate still points at.
            Added = null;
            Cleared = null;
        }

        public static void Add(ChatMessage message)
        {
            if (string.IsNullOrEmpty(message.Text)) return;

            messages.Add(message);
            if (messages.Count > Capacity) messages.RemoveAt(0);

            Added?.Invoke(message);
        }

        public static void AddPlayer(string sender, string text) =>
            Add(new ChatMessage(ChatKind.Player, sender, text, Now));

        public static void AddSystem(string text) =>
            Add(new ChatMessage(ChatKind.System, string.Empty, text, Now));

        public static void AddNotice(string text) =>
            Add(new ChatMessage(ChatKind.Notice, string.Empty, text, Now));

        public static void Clear()
        {
            if (messages.Count == 0) return;

            messages.Clear();
            Cleared?.Invoke();
        }

        /// <summary>
        /// The stamp put on an arriving line. Edit-mode tests read it too, where
        /// <see cref="Time.unscaledTime"/> is a valid (if frozen) number.
        /// </summary>
        private static float Now => Time.unscaledTime;
    }
}
