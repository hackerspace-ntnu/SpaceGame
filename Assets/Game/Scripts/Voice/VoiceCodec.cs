// Opus encode and decode, wrapped so callers never see Concentus.
//
// Concentus is a pure C# port of libopus (Assets/ThirdParty/Concentus, BSD). It is used instead of
// a native binding deliberately: no per-platform .dll/.so to build, ship or sign, and nothing to go
// wrong in an IL2CPP build. The cost is roughly half the throughput of native libopus, which is
// irrelevant here -- a machine encodes one stream and decodes only the voices it can actually hear.
//
// Concentus.OpusCodecFactory is deliberately NOT used: it probes for a native libopus and P/Invokes
// it when present. Constructing the managed structs directly means there is no probing, no
// DllImport surface, and the same code path on every platform.
using System;
using Concentus.Enums;
using Concentus.Structs;
using UnityEngine;

namespace SpaceGame.Voice
{
    /// <summary>One outgoing voice stream: PCM frames in, Opus packets out.</summary>
    public sealed class VoiceEncoder : IDisposable
    {
        private OpusEncoder encoder;

        /// <param name="bitrate">Clamped to the range <see cref="VoiceFormat"/> allows.</param>
        public VoiceEncoder(int bitrate = VoiceFormat.DefaultBitrate)
        {
            encoder = new OpusEncoder(VoiceFormat.SampleRate, VoiceFormat.Channels,
                                      OpusApplication.OPUS_APPLICATION_VOIP)
            {
                Bitrate = Mathf.Clamp(bitrate, VoiceFormat.MinBitrate, VoiceFormat.MaxBitrate),
                Complexity = VoiceFormat.EncoderComplexity,

                // Speech pauses cost about 2 kbps of comfort noise instead of a full frame.
                UseDTX = true,

                // Voice over an unreliable channel: tell the encoder to expect loss so it builds in
                // recovery, rather than producing frames that only decode as an unbroken run.
                PacketLossPercent = 5,
                UseVBR = true,
            };
        }

        /// <summary>The live bitrate, re-clamped on every set so a bad value cannot reach Opus.</summary>
        public int Bitrate
        {
            get => encoder?.Bitrate ?? 0;
            set
            {
                if (encoder == null) return;
                encoder.Bitrate = Mathf.Clamp(value, VoiceFormat.MinBitrate, VoiceFormat.MaxBitrate);
            }
        }

        /// <summary>
        /// Encodes exactly one frame. <paramref name="pcm"/> must hold
        /// <see cref="VoiceFormat.FrameSamples"/> samples. Returns the byte count written to
        /// <paramref name="packet"/>, or 0 if nothing could be encoded.
        /// </summary>
        public int Encode(short[] pcm, byte[] packet)
        {
            if (encoder == null || pcm == null || packet == null) return 0;
            if (pcm.Length < VoiceFormat.FrameSamples) return 0;

            try
            {
                return encoder.Encode(pcm.AsSpan(0, VoiceFormat.FrameSamples), VoiceFormat.FrameSamples,
                                      packet.AsSpan(), packet.Length);
            }
            catch (Exception e)
            {
                // A throwing encoder is a bug in our framing, not something to swallow per frame.
                Debug.LogError($"[Voice] Opus encode failed: {e.Message}");
                return 0;
            }
        }

        public void Dispose()
        {
            encoder?.Dispose();
            encoder = null;
        }
    }

    /// <summary>One incoming voice stream: Opus packets in, PCM frames out.</summary>
    public sealed class VoiceDecoder : IDisposable
    {
        private OpusDecoder decoder;

        public VoiceDecoder()
        {
            decoder = new OpusDecoder(VoiceFormat.SampleRate, VoiceFormat.Channels);
        }

        /// <summary>
        /// Decodes one packet into <paramref name="pcm"/>, which must have room for
        /// <see cref="VoiceFormat.FrameSamples"/> samples. Returns samples written, or 0.
        /// <para>
        /// Pass <paramref name="length"/> 0 to conceal a packet that never arrived: Opus
        /// interpolates from what it last heard, which sounds far better than a hole.
        /// </para>
        /// </summary>
        public int Decode(byte[] packet, int length, short[] pcm)
        {
            if (decoder == null || pcm == null) return 0;
            if (pcm.Length < VoiceFormat.FrameSamples) return 0;

            try
            {
                ReadOnlySpan<byte> input = length > 0 && packet != null
                    ? packet.AsSpan(0, length)
                    : default;

                return decoder.Decode(input, pcm.AsSpan(0, VoiceFormat.FrameSamples),
                                      VoiceFormat.FrameSamples);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Voice] Opus decode failed: {e.Message}");
                return 0;
            }
        }

        public void Dispose()
        {
            decoder?.Dispose();
            decoder = null;
        }
    }
}
