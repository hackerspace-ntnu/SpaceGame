using System;
using System.Collections.Generic;

namespace SpaceGame.Core
{
    /// <summary>
    /// Runs a slash command on behalf of one client and returns what to tell them, or null to say
    /// nothing. The return value is a <see cref="ChatKind.Notice"/> — it goes back to the sender
    /// alone, never to the session.
    /// </summary>
    /// <param name="sender">The client that typed it. Always trusted, because the server reads it
    /// off the RPC rather than off the message.</param>
    /// <param name="args">Whitespace-separated words after the command name. Never null, may be empty.</param>
    public delegate string ChatCommandHandler(ulong sender, string[] args);

    /// <summary>
    /// The command table, and the parser that decides a typed line is a command at all.
    /// <para>
    /// Split from <see cref="ChatNetwork"/> so that "what does <c>/tp bob</c> parse to" and "who is
    /// allowed to run it" are answerable without a network session — and split from
    /// <see cref="ChatBuiltinCommands"/> so the table itself has no idea what a player or a
    /// teleport is. Registration is open: any system can add a command in its own Awake without
    /// this file knowing about it.
    /// </para>
    /// </summary>
    public static class ChatCommands
    {
        public const char Prefix = '/';

        public readonly struct Entry
        {
            /// <summary>Canonical name, lower case, without the slash.</summary>
            public readonly string Name;

            /// <summary>How to type it, e.g. <c>/tp &lt;player&gt;</c>. Shown by /help and on misuse.</summary>
            public readonly string Usage;

            public readonly string Summary;
            public readonly ChatCommandHandler Handler;

            public Entry(string name, string usage, string summary, ChatCommandHandler handler)
            {
                Name = name;
                Usage = usage;
                Summary = summary;
                Handler = handler;
            }

            public bool IsValid => Handler != null;
        }

        // Aliases and canonical names share one table; `ordered` holds only the canonical entries,
        // so /help lists each command once rather than once per spelling.
        private static readonly Dictionary<string, Entry> byName =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly List<Entry> ordered = new();

        /// <summary>Every registered command, once each, in registration order.</summary>
        public static IReadOnlyList<Entry> All => ordered;

        /// <summary>True if this line should be run rather than broadcast.</summary>
        public static bool IsCommand(string text) =>
            !string.IsNullOrEmpty(text) && text[0] == Prefix;

        /// <summary>
        /// Adds a command. Re-registering a name replaces it, so a domain reload that re-runs a
        /// registrar leaves one entry rather than a duplicate.
        /// </summary>
        public static void Register(string name, string usage, string summary,
                                    ChatCommandHandler handler, params string[] aliases)
        {
            if (string.IsNullOrWhiteSpace(name) || handler == null) return;

            name = name.Trim().ToLowerInvariant();
            var entry = new Entry(name, usage, summary, handler);

            int existing = ordered.FindIndex(e => e.Name == name);
            if (existing >= 0) ordered[existing] = entry;
            else ordered.Add(entry);

            byName[name] = entry;

            if (aliases == null) return;
            foreach (string alias in aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                    byName[alias.Trim().ToLowerInvariant()] = entry;
            }
        }

        /// <summary>Drops the whole table. Used by the play-session reset and by tests.</summary>
        public static void Clear()
        {
            byName.Clear();
            ordered.Clear();
        }

        /// <summary>
        /// Splits a typed line into a command name and its arguments.
        /// <para>
        /// Returns false for anything that is not a command, and for a bare <c>/</c> — which is
        /// what the player has typed for the entire moment between opening chat with a slash and
        /// typing the first letter, so it must not be an error.
        /// </para>
        /// </summary>
        public static bool TryParse(string text, out string name, out string[] args)
        {
            name = string.Empty;
            args = Array.Empty<string>();

            if (!IsCommand(text)) return false;

            string body = text[1..].Trim();
            if (body.Length == 0) return false;

            string[] parts = body.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            name = parts[0].ToLowerInvariant();
            args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();
            return true;
        }

        public static bool TryGet(string name, out Entry entry) =>
            byName.TryGetValue(name ?? string.Empty, out entry) && entry.IsValid;

        /// <summary>
        /// Server-side: runs <paramref name="text"/> as <paramref name="sender"/>.
        /// <para>
        /// Always returns something for a line that reached here, because a command that answers
        /// with silence is indistinguishable from one the server never received. A handler that
        /// throws is reported to the sender and logged rather than taking the chat RPC down with
        /// it — a command is player input, and player input must not be able to kill the server's
        /// message pump.
        /// </para>
        /// </summary>
        public static string Execute(ulong sender, string text)
        {
            if (!TryParse(text, out string name, out string[] args))
                return "Type a command after the slash, or /help to see them.";

            if (!TryGet(name, out Entry entry))
                return $"Unknown command '{Prefix}{name}'. Try {Prefix}help.";

            try
            {
                return entry.Handler(sender, args);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[Chat] Command '{name}' threw: {e}");
                return $"{Prefix}{name} failed. Check the log.";
            }
        }
    }
}
