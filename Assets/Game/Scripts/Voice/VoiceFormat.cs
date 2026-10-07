// The wire format every machine in a session must agree on.
//
// These are NOT tunables. A frame encoded at one sample rate and decoded at another is noise, so
// changing any of them changes the protocol and breaks compatibility with every other build in the
// session. The things a player may actually vary -- input device, gate threshold, volume -- live in
// GameSettings instead.
//
// 48 kHz mono is Opus's native rate and FMOD's default output rate, so the common case needs no
// resampling at either end. 60 ms frames are a deliberate choice over the more usual 20 ms: at
// voice bitrates the packet headers (UDP + Relay + the netcode RPC) can outweigh the payload, and
// tripling the frame cuts the packet count to a third for 40 ms of added latency nobody can hear in
// speech. Opus only accepts 2.5, 5, 10, 20, 40, 60, 80, 100 and 120 ms, so this is not a free dial.

namespace SpaceGame.Voice
{
    /// <summary>The audio format and framing shared by capture, codec, transport and playback.</summary>
    public static class VoiceFormat
    {
        /// <summary>Opus's native rate, and FMOD's — the common path resamples nowhere.</summary>
        public const int SampleRate = 48000;

        /// <summary>Voice is mono. Position comes from the 3D mix, never from the capture.</summary>
        public const int Channels = 1;

        /// <summary>Must be one of Opus's legal frame durations. See the header for why 60.</summary>
        public const int FrameMilliseconds = 60;

        /// <summary>Samples in one encoded frame — 2880 at 48 kHz.</summary>
        public const int FrameSamples = SampleRate / 1000 * FrameMilliseconds;

        /// <summary>
        /// Ceiling for one encoded packet. A 60 ms frame at the maximum bitrate is about 480 bytes;
        /// this leaves headroom and still sits far under the ~1200 byte transport MTU, so a voice
        /// packet never fragments.
        /// </summary>
        public const int MaxPacketBytes = 600;

        /// <summary>Plenty for speech, and a quarter of what the world sim already sends per player.</summary>
        public const int DefaultBitrate = 24000;

        public const int MinBitrate = 8000;
        public const int MaxBitrate = 64000;

        /// <summary>
        /// Encoder complexity, 0-10. Concentus is a managed port running at roughly half the speed
        /// of native libopus, so this buys headroom on weaker machines; 5 is transparent for speech.
        /// </summary>
        public const int EncoderComplexity = 5;

        /// <summary>Bytes a netcode client id takes on the wire.</summary>
        public const int ClientIdBytes = sizeof(ulong);

        /// <summary>
        /// Packs a client id least-significant byte first. By hand rather than through
        /// FastBufferWriter.WriteValueSafe so the layout is explicit and identical at both ends
        /// whatever the machine's own endianness — and so <see cref="VoiceRoster"/>, which is a
        /// plain byte array rather than a netcode buffer, packs ids exactly the same way.
        /// </summary>
        public static void WriteClientId(byte[] into, int offset, ulong id)
        {
            for (int i = 0; i < ClientIdBytes; i++) into[offset + i] = (byte)(id >> (i * 8));
        }

        /// <inheritdoc cref="WriteClientId"/>
        public static ulong ReadClientId(byte[] from, int offset)
        {
            ulong id = 0;
            for (int i = 0; i < ClientIdBytes; i++) id |= (ulong)from[offset + i] << (i * 8);
            return id;
        }
    }
}
