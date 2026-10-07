// Decides whether the microphone is open, from the input level alone.
//
// Pure and stateful-but-tiny on purpose: no Unity types beyond Mathf, no FMOD, no netcode, so the
// whole thing is exercised in an EditMode test by feeding it numbers. The same shape the lobby's
// readers use (LobbyRoster, LobbyTeams) and for the same reason -- decision logic that is awkward
// to reproduce live is logic that should not need a live session to test.
//
// The hold is what makes it usable. A bare threshold chops the quiet tail off every word and
// stutters on any sound that sits near the line; holding the gate open briefly after the level
// drops turns that into one continuous phrase.
using UnityEngine;

namespace SpaceGame.Voice
{
    /// <summary>A noise gate with hysteresis: opens on level, closes after a quiet hold.</summary>
    public sealed class VoiceGate
    {
        /// <summary>How long the gate stays open after the level falls below the threshold.</summary>
        public const float HoldSeconds = 0.35f;

        /// <summary>
        /// The level must fall this far under the threshold before the hold starts, so a voice
        /// hovering right on the line does not chatter the gate open and shut.
        /// </summary>
        public const float ReleaseMargin = 0.7f;

        private float heldFor;

        /// <summary>True while the gate is passing audio.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// Advances the gate. <paramref name="level"/> is peak amplitude 0-1 (what
        /// <see cref="VoiceCapture.InputLevel"/> reports) and <paramref name="threshold"/> is the
        /// player's setting on the same scale.
        /// </summary>
        public bool Step(float level, float threshold, float deltaTime)
        {
            if (level >= threshold)
            {
                IsOpen = true;
                heldFor = 0f;
                return true;
            }

            if (!IsOpen) return false;

            // Still close to the threshold — treat it as the same phrase, not a new silence.
            if (level >= threshold * ReleaseMargin)
            {
                heldFor = 0f;
                return true;
            }

            heldFor += Mathf.Max(0f, deltaTime);
            if (heldFor < HoldSeconds) return true;

            IsOpen = false;
            heldFor = 0f;
            return false;
        }

        /// <summary>
        /// Peak amplitude of a block of samples, 0-1 — the scale both the threshold setting and
        /// the input meter are expressed in.
        /// <para>
        /// It lives on the gate rather than on the capture so that everything feeding a threshold
        /// agrees on what "level" means; a second implementation is a second answer.
        /// </para>
        /// </summary>
        public static float Peak(short[] samples, int count)
        {
            if (samples == null) return 0f;
            if (count > samples.Length) count = samples.Length;

            int peak = 0;
            for (int i = 0; i < count; i++)
            {
                // Widened to int first: negating short.MinValue does not fit back in a short.
                int magnitude = samples[i] < 0 ? -samples[i] : samples[i];
                if (magnitude > peak) peak = magnitude;
            }

            return Mathf.Clamp01(peak / (float)short.MaxValue);
        }

        /// <summary>Shuts the gate immediately — on stopping capture, or on releasing push-to-talk.</summary>
        public void Reset()
        {
            IsOpen = false;
            heldFor = 0f;
        }
    }
}
