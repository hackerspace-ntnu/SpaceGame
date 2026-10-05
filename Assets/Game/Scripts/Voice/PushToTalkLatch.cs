// Whether the push-to-talk channel is open, from the key's state alone.
//
// Pure — no input, no FMOD — so both modes are pinned by an EditMode test instead of by holding a
// key down in play mode.
//
// - Hold: open while the key is down, plus a short tail after release. People let go of the key a
//   fraction before they finish the word; without the tail the last syllable is cut off, which is
//   the complaint every push-to-talk implementation without one gets.
// - Toggle: each press flips it. No tail — the player said "off", explicitly.
using UnityEngine;

namespace SpaceGame.Voice
{
    /// <summary>The push-to-talk channel's open/closed state, stepped once a frame.</summary>
    public sealed class PushToTalkLatch
    {
        /// <summary>How long a released hold-to-talk key keeps the channel open.</summary>
        public const float ReleaseTailSeconds = 0.2f;

        private float tail;

        /// <summary>Toggle mode's latched state. Always false in hold mode.</summary>
        public bool Latched { get; private set; }

        /// <summary>
        /// Advances the latch and returns whether the channel is open this frame.
        /// <paramref name="pressedThisFrame"/> is the key going down; <paramref name="held"/> is it
        /// being down at all.
        /// </summary>
        public bool Step(bool toggleMode, bool pressedThisFrame, bool held, float deltaTime)
        {
            if (toggleMode)
            {
                tail = 0f;
                if (pressedThisFrame) Latched = !Latched;
                return Latched;
            }

            // Switching from toggle to hold must not leave a latch open that no key can now close.
            Latched = false;

            if (held)
            {
                tail = ReleaseTailSeconds;
                return true;
            }

            tail -= Mathf.Max(0f, deltaTime);
            return tail > 0f;
        }

        /// <summary>Closes the channel at once — a session ending, or the key being rebound.</summary>
        public void Reset()
        {
            Latched = false;
            tail = 0f;
        }
    }
}
