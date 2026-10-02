// The one clock every spoken line is revealed on.
//
// The popup, the speech bubbles, a talking mouth and the server deciding how long a resident's line
// keeps the floor all have to agree on when each letter appears and when the line is over. They used
// to be one coroutine inside NpcDialogPopupUI, which nothing else could ask; here the timing is a
// pure function of the text and the seconds since it started, so any machine — and the server, with
// no UI at all — gets the same answer without anything being sent.
//
// Constants rather than Inspector fields for that reason: the server has no scene object to read
// them from. The values are the ones DialogePanel.prefab was tuned to.
namespace SpaceGame.Presentation.Speech
{
    public static class Typewriter
    {
        /// <summary>Letters revealed per second.</summary>
        public const float CharactersPerSecond = 28f;

        /// <summary>Extra seconds the reveal waits after a punctuation mark, so a comma reads as a breath.</summary>
        public const float PunctuationPause = 0.08f;

        /// <summary>Seconds a fully revealed line stays up to be read.</summary>
        public const float HoldAfterTyping = 0.8f;

        /// <summary>Seconds the reveal waits after <paramref name="letter"/> before the next one.</summary>
        public static float DelayAfter(char letter)
        {
            float delay = 1f / CharactersPerSecond;
            switch (letter)
            {
                case '.': case ',': case '!': case '?': case ';': case ':':
                    return delay + PunctuationPause;
                default:
                    return delay;
            }
        }

        /// <summary>Seconds from the first letter appearing until the whole line has been revealed.</summary>
        public static float TypingSeconds(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0f;

            float seconds = 0f;
            for (int i = 0; i < text.Length; i++) seconds += DelayAfter(text[i]);
            return seconds;
        }

        /// <summary>Seconds the line stays up: revealed, then held to be read. The server times lines with this.</summary>
        public static float DurationFor(string text) => TypingSeconds(text) + HoldAfterTyping;

        /// <summary>
        /// How many letters of <paramref name="text"/> are showing <paramref name="elapsed"/> seconds in.
        /// The first letter shows at once, as it always did in the popup.
        /// </summary>
        public static int VisibleChars(string text, float elapsed)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            float revealAt = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                if (elapsed < revealAt) return i;
                revealAt += DelayAfter(text[i]);
            }
            return text.Length;
        }
    }
}
