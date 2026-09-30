// Microphone capture: FMOD records into a looping ring, this drains it into whole Opus frames.
//
// FMOD records at the DEVICE's sample rate -- it will not resample for us, and creating the record
// sound at any other rate fails outright. 44.1 kHz microphones are common, so anything that is not
// already 48 kHz goes through Concentus's Speex resampler on the way out. The 48 kHz case, which is
// most headsets, copies straight through.
//
// The ring is read by position rather than by callback because FMOD's record position is the only
// thing that says how much is actually new; polling it once a frame is both simpler and safer than
// a callback landing on FMOD's own thread while Unity is mid-frame.
using System;
using System.Runtime.InteropServices;
using Concentus.Common;
using UnityEngine;

namespace SpaceGame.Voice
{
    /// <summary>
    /// Owns one recording device and hands out complete <see cref="VoiceFormat.FrameSamples"/>
    /// frames at <see cref="VoiceFormat.SampleRate"/>. Nothing here throws; failures are reported
    /// through <see cref="TryStart"/> and then by going quiet.
    /// </summary>
    public sealed class VoiceCapture : IDisposable
    {
        /// <summary>Seconds of microphone held in FMOD's ring. Well past one poll at any frame rate.</summary>
        private const float RingSeconds = 1f;

        /// <summary>Speex resampler quality, 0-10. Mid is transparent for speech and cheap.</summary>
        private const int ResamplerQuality = 5;

        private FMOD.Sound sound;
        private int driver = -1;
        private uint readCursor;
        private int ringSamples;
        private int deviceRate;

        private SpeexResampler resampler;
        private short[] captured = Array.Empty<short>();
        private short[] converted = Array.Empty<short>();

        private readonly short[] frame = new short[VoiceFormat.FrameSamples];
        private int framed;

        /// <summary>The device this is recording from, or null.</summary>
        public string DeviceName { get; private set; }

        public bool IsRecording => driver >= 0;

        /// <summary>
        /// Peak amplitude seen in the last poll, 0-1. What the settings page draws as an input
        /// meter, and what a gate threshold is compared against.
        /// </summary>
        public float InputLevel { get; private set; }

        /// <summary>
        /// Opens <paramref name="device"/> and starts recording. Returns false with a
        /// player-readable reason rather than throwing — a missing microphone is not exceptional.
        /// </summary>
        public bool TryStart(VoiceDevices.Device device, out string error)
        {
            Stop();

            if (!device.IsValid)
            {
                error = "No microphone is connected.";
                return false;
            }

            try
            {
                FMOD.System core = FMODUnity.RuntimeManager.CoreSystem;

                deviceRate = device.SampleRate;
                ringSamples = Mathf.Max(VoiceFormat.FrameSamples * 2,
                                        Mathf.RoundToInt(deviceRate * RingSeconds));

                var exinfo = new FMOD.CREATESOUNDEXINFO
                {
                    cbsize = Marshal.SizeOf(typeof(FMOD.CREATESOUNDEXINFO)),
                    numchannels = VoiceFormat.Channels,
                    defaultfrequency = deviceRate,
                    format = FMOD.SOUND_FORMAT.PCM16,
                    length = (uint)(ringSamples * sizeof(short) * VoiceFormat.Channels),
                };

                FMOD.RESULT result = core.createSound(IntPtr.Zero,
                    FMOD.MODE.OPENUSER | FMOD.MODE.LOOP_NORMAL, ref exinfo, out sound);

                if (result != FMOD.RESULT.OK)
                {
                    error = $"Could not create the capture buffer ({result}).";
                    return false;
                }

                result = core.recordStart(device.Index, sound, true);
                if (result != FMOD.RESULT.OK)
                {
                    sound.release();
                    sound.clearHandle();
                    error = $"Could not open \"{device.Name}\" ({result}).";
                    return false;
                }

                // Only built when the device disagrees with Opus's rate; the 48 kHz path is a copy.
                resampler = deviceRate == VoiceFormat.SampleRate
                    ? null
                    : new SpeexResampler(VoiceFormat.Channels, deviceRate, VoiceFormat.SampleRate,
                                         ResamplerQuality);

                driver = device.Index;
                DeviceName = device.Name;
                readCursor = 0;
                framed = 0;
                InputLevel = 0f;
                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = $"Could not start capture: {e.Message}";
                driver = -1;
                return false;
            }
        }

        /// <summary>Stops recording and releases the buffer. Safe to call when not recording.</summary>
        public void Stop()
        {
            if (driver >= 0)
            {
                try { FMODUnity.RuntimeManager.CoreSystem.recordStop(driver); }
                catch (Exception e) { Debug.LogWarning($"[Voice] recordStop failed: {e.Message}"); }
                driver = -1;
            }

            if (sound.hasHandle())
            {
                sound.release();
                sound.clearHandle();
            }

            resampler = null;
            DeviceName = null;
            framed = 0;
            InputLevel = 0f;
        }

        /// <summary>
        /// Drains whatever the microphone has produced since the last call, invoking
        /// <paramref name="onFrame"/> once per complete frame. The array handed to the callback is
        /// reused, so a caller that keeps it must copy.
        /// </summary>
        public void Poll(Action<short[]> onFrame)
        {
            if (driver < 0 || !sound.hasHandle() || onFrame == null) return;

            int available = NewSampleCount();
            if (available <= 0) return;

            if (captured.Length < available) captured = new short[available];
            if (!ReadRing(available)) return;

            TrackLevel(available);

            short[] source = captured;
            int count = available;

            if (resampler != null)
            {
                count = Resample(available);
                if (count <= 0) return;
                source = converted;
            }

            EmitFrames(source, count, onFrame);
        }

        public void Dispose() => Stop();

        // ------------------------------------------------------------------ internals

        /// <summary>How many samples FMOD has written since the last drain, across the wrap.</summary>
        private int NewSampleCount()
        {
            if (FMODUnity.RuntimeManager.CoreSystem.getRecordPosition(driver, out uint position)
                != FMOD.RESULT.OK)
            {
                return 0;
            }

            long delta = (long)position - readCursor;
            if (delta < 0) delta += ringSamples;

            // A stall long enough to lap the ring means the oldest audio is already overwritten;
            // taking the whole ring is the least-wrong recovery and stays in sync afterwards.
            return (int)Math.Min(delta, ringSamples);
        }

        /// <summary>Copies <paramref name="count"/> samples out of the ring, handling the wrap.</summary>
        private bool ReadRing(int count)
        {
            uint byteOffset = readCursor * sizeof(short) * VoiceFormat.Channels;
            uint byteLength = (uint)(count * sizeof(short) * VoiceFormat.Channels);

            if (sound.@lock(byteOffset, byteLength, out IntPtr ptr1, out IntPtr ptr2,
                            out uint len1, out uint len2) != FMOD.RESULT.OK)
            {
                return false;
            }

            try
            {
                int first = (int)(len1 / sizeof(short));
                if (first > 0) Marshal.Copy(ptr1, captured, 0, first);

                int second = (int)(len2 / sizeof(short));
                if (second > 0 && ptr2 != IntPtr.Zero) Marshal.Copy(ptr2, captured, first, second);
            }
            finally
            {
                sound.unlock(ptr1, ptr2, len1, len2);
            }

            readCursor = (uint)(((long)readCursor + count) % ringSamples);
            return true;
        }

        private void TrackLevel(int count) => InputLevel = VoiceGate.Peak(captured, count);

        /// <summary>Device rate to 48 kHz. Returns how many samples landed in <c>converted</c>.</summary>
        private int Resample(int count)
        {
            // Ceiling plus slack: the output length varies by a sample either way as the
            // resampler's internal phase advances, and a buffer that is exactly right truncates.
            int capacity = (int)((long)count * VoiceFormat.SampleRate / deviceRate) + 16;
            if (converted.Length < capacity) converted = new short[capacity];

            // Looped until the input is fully consumed. Process reports how much it actually took,
            // and taking that on trust — as this did — silently discards whatever it left behind,
            // which is a click at the seam of every poll and a drift that never corrects.
            int consumed = 0;
            int produced = 0;

            try
            {
                while (consumed < count && produced < converted.Length)
                {
                    int inLength = count - consumed;
                    int outLength = converted.Length - produced;

                    resampler.Process(0, captured.AsSpan(consumed, inLength), ref inLength,
                                      converted.AsSpan(produced, outLength), ref outLength);

                    // No progress on either side means another pass would spin forever.
                    if (inLength <= 0 && outLength <= 0) break;

                    consumed += inLength;
                    produced += outLength;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Voice] Resample from {deviceRate} Hz failed: {e.Message}");
                return 0;
            }

            return produced;
        }

        /// <summary>Accumulates into whole frames and hands each one out.</summary>
        private void EmitFrames(short[] source, int count, Action<short[]> onFrame)
        {
            int consumed = 0;

            while (consumed < count)
            {
                int take = Math.Min(VoiceFormat.FrameSamples - framed, count - consumed);
                Array.Copy(source, consumed, frame, framed, take);

                framed += take;
                consumed += take;

                if (framed < VoiceFormat.FrameSamples) continue;

                framed = 0;
                onFrame(frame);
            }
        }
    }
}
