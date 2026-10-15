// What one character is saying, right now, on this machine.
//
// Speech used to BE the dialog popup: one screen-space text box, so one line in the whole game at a
// time, and the mouth, the hands and the "who said it" all read the box. That cannot carry a
// settlement where three residents talk at once and a player across the square should see it.
// So the line now lives on the character that says it — a typewriter clock and an event — and
// everything that shows speech is a view of it: the popup (lines addressed to this player), the
// speech bubbles (everyone else in earshot), TalkingMouth and SpeechGestures (the body).
//
// Purely local and cosmetic. Whoever decides a line (a chatter module, a dialog, the server's
// resident voice via NetMsg.ResidentSaid) calls Say on every machine that should hear it; nothing
// here is sent and nothing is saved.
using System;
using UnityEngine;

namespace SpaceGame.Presentation.Speech
{
    /// <summary>
    /// What kind of line it is. Declared in bubble priority order — Ambient lowest, Reply highest —
    /// so the value is the rank. <see cref="Dialog"/> is the popup's and never becomes a bubble.
    /// </summary>
    public enum SpeechChannel : byte { Ambient, Talk, Warning, Reply, Dialog }

    [DisallowMultipleComponent]
    public sealed class Speaker : MonoBehaviour
    {
        /// <summary>Raised on this machine whenever any character starts a line. Views subscribe here.</summary>
        public static event Action<Speaker, string, SpeechChannel> AnySaid;

        /// <summary>Raised when this character starts a line.</summary>
        public event Action<string, SpeechChannel> Said;

        // Static events survive leaving play mode with domain reload off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => AnySaid = null;

        private float startedAt = float.NegativeInfinity;
        private float typingSeconds;
        private float endsAt = float.NegativeInfinity;
        private float showAtLeastUntil = float.NegativeInfinity;
        private bool finishedTyping;

        /// <summary>The line being said, in full. Empty before the first line.</summary>
        public string CurrentText { get; private set; } = string.Empty;

        public SpeechChannel CurrentChannel { get; private set; }

        /// <summary>Whether the current line was meant for this machine's dialog popup.</summary>
        public bool OnPopup { get; private set; }

        /// <summary>Counts every line, so a view can tell a new line from the one it is showing.</summary>
        public int LineNumber { get; private set; }

        /// <summary>True while the typewriter is still revealing the line: the mouth and hands are moving.</summary>
        public bool IsSpeaking => !finishedTyping && Time.time - startedAt < typingSeconds;

        /// <summary>True while the line is still up to be read, typing or held.</summary>
        public bool IsShowing => Time.time < endsAt;

        /// <summary>Time.time at which the current line stops showing.</summary>
        public float LineEndsAt => endsAt;

        /// <summary>Letters of <see cref="CurrentText"/> revealed so far.</summary>
        public int VisibleCharacters => finishedTyping
            ? CurrentText.Length
            : Typewriter.VisibleChars(CurrentText, Time.time - startedAt);

        /// <summary>Fraction of the line revealed, 0 to 1.</summary>
        public float VisibleFraction =>
            CurrentText.Length == 0 ? 1f : (float)VisibleCharacters / CurrentText.Length;

        /// <summary>
        /// The speaker for <paramref name="character"/>, added on first ask. Lives on exactly the
        /// transform passed — the character's root, which is where every caller (DialogInteraction,
        /// ChatterModule, TraderInteraction, ResidentVoice) and every reader (TalkingMouth,
        /// SpeechGestures) sits. Null for a null character.
        /// </summary>
        public static Speaker Of(Transform character)
        {
            if (character == null) return null;
            return character.TryGetComponent(out Speaker speaker)
                ? speaker
                : character.gameObject.AddComponent<Speaker>();
        }

        /// <summary>Start saying <paramref name="text"/>. It replaces whatever this character was saying.</summary>
        /// <param name="showPopup">Also show it in this machine's dialog popup — the line is addressed to the local player.</param>
        public void Say(string text, SpeechChannel channel, bool showPopup) => Say(text, channel, showPopup, 0f);

        /// <param name="showAtLeast">Keep the line up at least this many seconds, however short it is.</param>
        public void Say(string text, SpeechChannel channel, bool showPopup, float showAtLeast)
        {
            CurrentText = text ?? string.Empty;
            CurrentChannel = channel;
            OnPopup = showPopup;
            LineNumber++;

            startedAt = Time.time;
            typingSeconds = Typewriter.TypingSeconds(CurrentText);
            finishedTyping = false;
            showAtLeastUntil = startedAt + Mathf.Max(0f, showAtLeast);
            endsAt = Mathf.Max(startedAt + Typewriter.DurationFor(CurrentText), showAtLeastUntil);

            Said?.Invoke(CurrentText, channel);
            AnySaid?.Invoke(this, CurrentText, channel);
        }

        /// <summary>
        /// Skip to the end of the typing — the player pressed on. The whole line shows and stays up to
        /// be read; the mouth and hands stop at once.
        /// </summary>
        public void FinishLine()
        {
            if (!IsSpeaking) return;
            finishedTyping = true;
            endsAt = Mathf.Max(Time.time + Typewriter.HoldAfterTyping, showAtLeastUntil);
        }

        /// <summary>End the line now: the popup closed on it, or the character fell silent.</summary>
        public void Hush()
        {
            finishedTyping = true;
            endsAt = Time.time;
        }

        /// <summary>While the line is being revealed, the letter revealed last — what the jaw shapes.</summary>
        public bool TryGetSpokenCharacter(out char letter)
        {
            letter = default;
            if (!IsSpeaking) return false;

            int index = VisibleCharacters - 1;
            if (index < 0 || index >= CurrentText.Length) return false;

            letter = CurrentText[index];
            return true;
        }
    }
}
