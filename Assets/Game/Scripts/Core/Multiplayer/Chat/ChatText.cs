using System.Text;

namespace SpaceGame.Core
{
    /// <summary>
    /// Turns whatever a player typed into something safe to broadcast and safe to draw.
    /// <para>
    /// Pure string work with no Unity dependency, so the rules below are covered by edit-mode tests
    /// rather than by playing the game and trying things.
    /// </para>
    /// </summary>
    public static class ChatText
    {
        /// <summary>What the input field will let you type.</summary>
        public const int MaxCharacters = 180;

        /// <summary>
        /// The cap that actually protects the wire. <see cref="MaxCharacters"/> is not enough on
        /// its own: the message crosses as a <c>FixedString512Bytes</c>, which holds 509 bytes, and
        /// a line written in a non-Latin script runs to three bytes a character — 180 of those is
        /// 540, and assigning that throws inside Unity.Collections rather than truncating.
        /// </summary>
        public const int MaxBytes = 400;

        /// <summary>
        /// Normalises a typed line: trims it, drops control characters, defuses TMP markup and
        /// clamps it to both limits. Returns an empty string for anything with nothing left in it,
        /// which every caller treats as "do not send".
        /// </summary>
        public static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            var builder = new StringBuilder(raw.Length);

            foreach (char c in raw)
            {
                // Newlines and tabs included: the field is single-line, and a line break smuggled
                // in by a paste would let one message occupy the whole log.
                if (char.IsControl(c)) continue;
                builder.Append(c);
            }

            string text = DefuseNoparse(builder.ToString()).Trim();

            if (text.Length > MaxCharacters) text = text[..MaxCharacters];

            // Trimmed by bytes after by characters, because only the byte count can overflow the
            // wire type. Mirrors PlayerIdentity.Truncate, for the same reason.
            while (Encoding.UTF8.GetByteCount(text) > MaxBytes && text.Length > 0)
                text = text[..^1];

            return text.Trim();
        }

        /// <summary>
        /// Breaks any closing <c>noparse</c> tag in the text.
        /// <para>
        /// The view draws a message inside <c>&lt;noparse&gt;…&lt;/noparse&gt;</c> so that markup a
        /// player types shows up as the characters they typed. That containment has exactly one
        /// hole: typing the closing tag yourself ends the block early and hands you the rest of the
        /// line as live rich text — <c>&lt;/noparse&gt;&lt;size=400%&gt;</c> is a message that
        /// covers everyone else's screen. TMP matches tags case-insensitively, so this does too.
        /// </para>
        /// </summary>
        private static string DefuseNoparse(string text)
        {
            const string closing = "</noparse";
            int at = text.IndexOf(closing, System.StringComparison.OrdinalIgnoreCase);
            if (at < 0) return text;

            var builder = new StringBuilder(text.Length + 8);
            int from = 0;

            while (at >= 0)
            {
                builder.Append(text, from, at - from);
                // A space after the slash: still readable as what they typed, no longer a tag.
                // The word is copied out of the original rather than written as a literal, so
                // somebody who typed "</NoParse>" still sees their own capitalisation.
                builder.Append("</ ").Append(text, at + 2, closing.Length - 2);
                from = at + closing.Length;
                at = text.IndexOf(closing, from, System.StringComparison.OrdinalIgnoreCase);
            }

            builder.Append(text, from, text.Length - from);
            return builder.ToString();
        }
    }
}
