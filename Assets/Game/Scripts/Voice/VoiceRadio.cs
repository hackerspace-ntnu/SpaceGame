// The suit radio: how a crewmate is still heard once they are too far away to hear in person.
//
// Story mode only, for now. There everyone is one crew, so a voice out of earshot is simply carried
// over the radio, and the host relays every voice to everyone (VoiceRouter.Everyone). Versus keeps
// pure proximity until squads get a channel of their own -- a radio everyone can hear would let the
// other team listen in.
//
// The radio does not take over at the edge of earshot. The proximity fade is steep -- a voice is
// down to about a seventh of its level by 20 m, long before it falls silent at 30 m -- so a radio
// that waited for the edge would leave a band where a crewmate is barely audible and not on the
// radio yet. Instead it fades in under the voice between two distances (VoiceTuning), and for
// that stretch both are heard at once: the crewmate across the room and in your helmet. Both are
// the same decoded audio on two channels of one buffer (VoicePlayback), so they stay in step and
// never echo each other.
//
// Everything here is pure, so the curve and the squelch can be pinned by EditMode tests.
using System;
using UnityEngine;

namespace SpaceGame.Voice
{
    /// <summary>The radio's fade curve and its squelch sounds.</summary>
    public static class VoiceRadio
    {
        /// <summary>
        /// At or above this blend a voice counts as "on the radio": the squelch plays when it starts
        /// and stops, and the speaking list says so.
        /// </summary>
        public const float MostlyRadio = 0.5f;

        /// <summary>The band a radio passes: roughly a telephone's, which is what reads as "radio".</summary>
        public const float LowCutHz = 300f;
        public const float HighCutHz = 3400f;

        /// <summary>A gentle presence bump in the middle of that band, for the nasal radio colour.</summary>
        public const float PresenceHz = 1800f;
        public const float PresenceGainDb = 4f;

        /// <summary>
        /// How much of a voice comes over the radio at <paramref name="distance"/> metres: 0 up to
        /// <paramref name="fadeStart"/>, 1 from <paramref name="fadeEnd"/>, eased in between so it
        /// neither starts nor finishes with a jump.
        /// </summary>
        public static float Blend(float distance, float fadeStart, float fadeEnd)
        {
            if (fadeEnd <= fadeStart) return distance >= fadeEnd ? 1f : 0f;

            float t = Mathf.Clamp01((distance - fadeStart) / (fadeEnd - fadeStart));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Samples in the squelch that opens a transmission.</summary>
        public static readonly int OpenSamples = VoiceFormat.SampleRate * 45 / 1000;

        /// <summary>Samples in the squelch that closes one — longer, the familiar "kssht".</summary>
        public static readonly int CloseSamples = VoiceFormat.SampleRate * 140 / 1000;

        /// <summary>
        /// A burst of band-limited static, at <see cref="VoiceFormat.SampleRate"/>, mono. Generated
        /// rather than shipped: there is no FMOD Studio project to author one in, and noise is the
        /// one sound that is easier to make than to find.
        /// <para>
        /// Deterministic for a given <paramref name="seed"/>. It ramps in over a couple of
        /// milliseconds and decays to silence, so neither end clicks on its own.
        /// </para>
        /// </summary>
        public static short[] Squelch(int samples, int seed, float peak = 0.45f)
        {
            samples = Math.Max(0, samples);
            var pcm = new short[samples];
            if (samples == 0) return pcm;

            var random = new System.Random(seed);
            float rate = VoiceFormat.SampleRate;

            // One-pole high-pass and low-pass: enough to take the hiss out of white noise and leave
            // the band the radio itself would pass.
            float lowAlpha = Alpha(HighCutHz, rate);
            float highAlpha = Alpha(LowCutHz, rate);

            int attack = Math.Max(1, (int)(rate * 0.002f));
            float decay = samples / 4f;

            float low = 0f;
            float highIn = 0f;
            float highOut = 0f;

            for (int i = 0; i < samples; i++)
            {
                float white = (float)(random.NextDouble() * 2.0 - 1.0);

                low += lowAlpha * (white - low);

                // y[n] = a * (y[n-1] + x[n] - x[n-1])
                highOut = (1f - highAlpha) * (highOut + low - highIn);
                highIn = low;

                float envelope = Math.Min(1f, (i + 1f) / attack) * (float)Math.Exp(-i / decay);

                // The last couple of milliseconds are forced to zero, so the burst always ends on
                // silence however the decay happens to land.
                int remaining = samples - 1 - i;
                if (remaining < attack) envelope *= remaining / (float)attack;

                float value = Mathf.Clamp(highOut * 2.2f * envelope * peak, -1f, 1f);
                pcm[i] = (short)(value * short.MaxValue);
            }

            return pcm;
        }

        // The smoothing factor of a one-pole filter with corner frequency hz.
        private static float Alpha(float hz, float rate)
        {
            float rc = 1f / (2f * Mathf.PI * hz);
            float dt = 1f / rate;
            return dt / (rc + dt);
        }
    }
}
