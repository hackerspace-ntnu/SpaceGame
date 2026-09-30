// How loud each other person is, to you: 0-200%, 100% by default, and a mute that remembers.
//
// People's microphones differ wildly — one friend is a whisper, another is a foghorn — and the
// global Voice volume cannot fix that, because it moves everyone together. This is the per-person
// half. What reaches the mix is GameSettings.VoiceVolume multiplied by the level here.
//
// Remembered by Unity account id across sessions (see VoiceAccount), so a quiet microphone you
// turned up stays turned up next time. A peer with no account id — a solo or direct test session,
// where Unity Services never signed in — gets a level keyed by client id for this session only, and
// ForgetSession drops it, because that client id will be somebody else next time.
//
// Kept under its own PlayerPrefs keys rather than in GameSettings: GameSettings is a fixed set of
// this install's options, and this is an open-ended map of opinions about other people. It is
// flushed by the same PlayerPrefs.Save the pause menu calls on close.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Voice
{
    /// <summary>Per-person listening levels and mutes, as this player has set them.</summary>
    public static class VoicePeerLevels
    {
        public const float MinGain = 0f;

        /// <summary>Twice a speaker's own level — the most a quiet microphone can be boosted.</summary>
        public const float MaxGain = VoicePlayback.MaxVolume;

        public const float DefaultGain = 1f;

        /// <summary>Slider step: 1%, so two voices can be matched exactly rather than nearly.</summary>
        public const float GainStep = 0.01f;

        private const string Prefix = "SpaceGame.Voice.Peer.";

        /// <summary>Raised when any level or mute changes.</summary>
        public static event Action Changed;

        private struct Level
        {
            public float Gain;
            public bool Muted;
        }

        private static readonly Level Untouched = new Level { Gain = DefaultGain, Muted = false };

        // Read-through caches: VoiceSession asks for every audible voice every frame, and PlayerPrefs
        // is not something to hit at that rate.
        private static readonly Dictionary<string, Level> byAccount = new Dictionary<string, Level>();
        private static readonly Dictionary<ulong, Level> bySession = new Dictionary<ulong, Level>();

        public static float GainOf(string accountId, ulong clientId) => Read(accountId, clientId).Gain;

        public static bool IsMuted(string accountId, ulong clientId) => Read(accountId, clientId).Muted;

        /// <summary>
        /// What actually reaches the mix for this person: zero while muted, otherwise their level.
        /// Muting leaves the level alone, which is the whole point — unmuting puts it back.
        /// </summary>
        public static float EffectiveGain(string accountId, ulong clientId)
        {
            Level level = Read(accountId, clientId);
            return level.Muted ? 0f : level.Gain;
        }

        public static void SetGain(string accountId, ulong clientId, float gain)
        {
            Level level = Read(accountId, clientId);
            float clamped = Clamp(gain);
            if (Mathf.Approximately(level.Gain, clamped)) return;

            level.Gain = clamped;
            Write(accountId, clientId, level);
        }

        public static void SetMuted(string accountId, ulong clientId, bool muted)
        {
            Level level = Read(accountId, clientId);
            if (level.Muted == muted) return;

            level.Muted = muted;
            Write(accountId, clientId, level);
        }

        public static float Clamp(float gain) => Mathf.Clamp(gain, MinGain, MaxGain);

        /// <summary>
        /// Drops the session-only levels. Their keys are client ids, and a client id means somebody
        /// else in the next session — carrying them over would turn a stranger down.
        /// </summary>
        public static void ForgetSession()
        {
            if (bySession.Count == 0) return;

            bySession.Clear();
            Changed?.Invoke();
        }

        private static Level Read(string accountId, ulong clientId)
        {
            string account = VoiceRoster.CleanAccountId(accountId);

            if (account.Length == 0)
                return bySession.TryGetValue(clientId, out Level session) ? session : Untouched;

            if (byAccount.TryGetValue(account, out Level cached)) return cached;

            var loaded = new Level
            {
                Gain = Clamp(PlayerPrefs.GetFloat(Prefix + account + ".Gain", DefaultGain)),
                Muted = PlayerPrefs.GetInt(Prefix + account + ".Muted", 0) == 1,
            };

            byAccount[account] = loaded;
            return loaded;
        }

        private static void Write(string accountId, ulong clientId, Level level)
        {
            string account = VoiceRoster.CleanAccountId(accountId);

            if (account.Length == 0)
            {
                bySession[clientId] = level;
            }
            else
            {
                byAccount[account] = level;
                PlayerPrefs.SetFloat(Prefix + account + ".Gain", level.Gain);
                PlayerPrefs.SetInt(Prefix + account + ".Muted", level.Muted ? 1 : 0);
            }

            Changed?.Invoke();
        }
    }
}
