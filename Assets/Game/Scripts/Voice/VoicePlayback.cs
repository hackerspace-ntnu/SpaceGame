// Decoded voice into FMOD, through a looping buffer written just ahead of the play head.
//
// Playback is FMOD's Core API rather than a Studio event because no FMOD Studio project ships with
// this repo -- there is no .fspro, so no new event (and no programmer instrument) can be authored.
// SfxFile.cs solves the same problem the same way for loose audio files. A Unity AudioSource is not
// an option either: Unity audio is disabled project-wide and would be silent with no error.
//
// The lead maintained over the play head is what absorbs jitter: write too close and any late frame
// is an audible gap, too far and the conversation lags. This keeps a fixed target and resyncs when
// it drifts outside the tolerated band, which is enough for a local round trip. Once voice arrives
// over the network this grows into a real jitter buffer that adapts its depth to observed timing.
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SpaceGame.Voice
{
    /// <summary>
    /// One voice being heard: PCM frames in, audible sound out through FMOD's master group.
    /// Nothing here throws.
    /// </summary>
    public sealed class VoicePlayback : IDisposable
    {
        /// <summary>Seconds of ring. Large enough that a resync never lands on itself.</summary>
        private const float RingSeconds = 1f;

        /// <summary>Frames of audio kept ahead of the play head — 120 ms at a 60 ms frame.</summary>
        private const int TargetLeadFrames = 2;

        /// <summary>Below this the next late frame is a gap; above it the voice is needlessly behind.</summary>
        private const int MinLeadFrames = 1;
        private const int MaxLeadFrames = 5;

        /// <summary>
        /// Silence laid immediately past each written frame, so an overrun meets quiet instead of
        /// whatever the ring held a second ago. The next frame overwrites it, so during continuous
        /// speech this costs one extra copy and nothing else.
        /// </summary>
        private const int TrailingSilenceFrames = 2;

        /// <summary>
        /// How long nothing may arrive before the channel is parked. Comfortably above one frame
        /// interval, so the ordinary gap between arriving frames is not mistaken for silence.
        /// </summary>
        private const float IdleParkSeconds = VoiceFormat.FrameMilliseconds * 2 / 1000f;

        private FMOD.Sound sound;
        private FMOD.Channel channel;
        private uint writeCursor;
        private int ringSamples;
        private bool primed;
        private short[] quiet;
        private float idleSeconds;

        public bool IsRunning => sound.hasHandle();

        /// <summary>Opens the buffer and starts the channel. False with a reason on failure.</summary>
        public bool TryStart(out string error)
        {
            Stop();

            try
            {
                FMOD.System core = FMODUnity.RuntimeManager.CoreSystem;

                ringSamples = Mathf.Max(VoiceFormat.FrameSamples * (MaxLeadFrames + 2),
                                        Mathf.RoundToInt(VoiceFormat.SampleRate * RingSeconds));

                var exinfo = new FMOD.CREATESOUNDEXINFO
                {
                    cbsize = Marshal.SizeOf(typeof(FMOD.CREATESOUNDEXINFO)),
                    numchannels = VoiceFormat.Channels,
                    defaultfrequency = VoiceFormat.SampleRate,
                    format = FMOD.SOUND_FORMAT.PCM16,
                    length = (uint)(ringSamples * sizeof(short) * VoiceFormat.Channels),
                };

                FMOD.RESULT result = core.createSound(IntPtr.Zero,
                    FMOD.MODE.OPENUSER | FMOD.MODE.LOOP_NORMAL | FMOD.MODE._2D, ref exinfo, out sound);

                if (result != FMOD.RESULT.OK)
                {
                    error = $"Could not create the playback buffer ({result}).";
                    return false;
                }

                // FMOD does not promise a zeroed buffer, and an unprimed ring loops whatever was in
                // that memory until the first frame lands on it.
                Silence();

                core.getMasterChannelGroup(out FMOD.ChannelGroup master);
                result = core.playSound(sound, master, false, out channel);

                if (result != FMOD.RESULT.OK)
                {
                    sound.release();
                    sound.clearHandle();
                    error = $"Could not start playback ({result}).";
                    return false;
                }

                quiet = new short[VoiceFormat.FrameSamples * TrailingSilenceFrames];
                writeCursor = 0;
                idleSeconds = 0f;
                primed = false;
                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = $"Could not start playback: {e.Message}";
                return false;
            }
        }

        /// <summary>Queues <paramref name="count"/> samples for playback.</summary>
        public void Submit(short[] pcm, int count)
        {
            if (!IsRunning || pcm == null || count <= 0) return;
            if (count > pcm.Length) count = pcm.Length;

            idleSeconds = 0f;

            // The first frame of a burst decides where writing starts; everything after follows
            // the cursor.
            if (!primed)
            {
                if (!TryGetPlayPosition(out uint position)) return;

                // Wipe what the previous burst left behind before resuming. The pause since then
                // is exactly the window in which the loop would otherwise replay it.
                Silence();
                writeCursor = Advance(position, VoiceFormat.FrameSamples * TargetLeadFrames);

                if (channel.hasHandle()) channel.setPaused(false);
                primed = true;
            }
            else
            {
                KeepLead();
            }

            if (!WriteAt(writeCursor, pcm, count)) return;

            writeCursor = Advance(writeCursor, count);
            WriteAt(writeCursor, quiet, quiet.Length);
        }

        /// <summary>
        /// Call once a frame. Parks the channel when nothing has arrived for a while.
        /// <para>
        /// Without this a looping buffer with no new audio does not go quiet — it keeps playing,
        /// walks past the last thing written and replays whatever occupied the ring a second ago.
        /// Which is the speaker's own last sentence, over and over, until they talk again.
        /// </para>
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!IsRunning || !primed) return;

            idleSeconds += Mathf.Max(0f, deltaTime);
            if (idleSeconds < IdleParkSeconds) return;

            if (channel.hasHandle()) channel.setPaused(true);
            primed = false;
            idleSeconds = 0f;
        }

        /// <summary>Linear gain, 0-1. This voice only — the FMOD buses still apply on top.</summary>
        public void SetVolume(float volume)
        {
            if (channel.hasHandle())
                channel.setVolume(Mathf.Clamp01(volume));
        }

        public void Stop()
        {
            if (channel.hasHandle())
            {
                channel.stop();
                channel.clearHandle();
            }

            if (sound.hasHandle())
            {
                sound.release();
                sound.clearHandle();
            }

            quiet = null;
            idleSeconds = 0f;
            primed = false;
        }

        public void Dispose() => Stop();

        // ------------------------------------------------------------------ internals

        private bool TryGetPlayPosition(out uint position)
        {
            position = 0;
            return channel.hasHandle() &&
                   channel.getPosition(out position, FMOD.TIMEUNIT.PCM) == FMOD.RESULT.OK;
        }

        /// <summary>Pulls the write cursor back to target if it has drifted out of the band.</summary>
        private void KeepLead()
        {
            if (!TryGetPlayPosition(out uint position)) return;

            long lead = (long)writeCursor - position;
            if (lead < 0) lead += ringSamples;

            if (lead >= (long)VoiceFormat.FrameSamples * MinLeadFrames &&
                lead <= (long)VoiceFormat.FrameSamples * MaxLeadFrames)
            {
                return;
            }

            // Underrun means the play head has caught up and is looping stale audio; overrun means
            // frames are arriving faster than they play. Both are fixed the same way, and both are
            // audible, so anything that makes this frequent belongs in the jitter buffer instead.
            writeCursor = Advance(position, VoiceFormat.FrameSamples * TargetLeadFrames);
        }

        private bool WriteAt(uint cursor, short[] pcm, int count)
        {
            if (pcm == null || count <= 0) return false;

            uint byteOffset = cursor * sizeof(short) * VoiceFormat.Channels;
            uint byteLength = (uint)(count * sizeof(short) * VoiceFormat.Channels);

            if (sound.@lock(byteOffset, byteLength, out IntPtr ptr1, out IntPtr ptr2,
                            out uint len1, out uint len2) != FMOD.RESULT.OK)
            {
                return false;
            }

            try
            {
                int first = (int)(len1 / sizeof(short));
                if (first > 0) Marshal.Copy(pcm, 0, ptr1, first);

                int second = (int)(len2 / sizeof(short));
                if (second > 0 && ptr2 != IntPtr.Zero) Marshal.Copy(pcm, first, ptr2, second);
            }
            finally
            {
                sound.unlock(ptr1, ptr2, len1, len2);
            }

            return true;
        }

        private void Silence()
        {
            if (sound.@lock(0, (uint)(ringSamples * sizeof(short) * VoiceFormat.Channels),
                            out IntPtr ptr1, out IntPtr ptr2, out uint len1, out uint len2)
                != FMOD.RESULT.OK)
            {
                return;
            }

            try
            {
                var quiet = new byte[len1];
                Marshal.Copy(quiet, 0, ptr1, (int)len1);

                if (len2 > 0 && ptr2 != IntPtr.Zero)
                {
                    if (len2 > len1) quiet = new byte[len2];
                    Marshal.Copy(quiet, 0, ptr2, (int)len2);
                }
            }
            finally
            {
                sound.unlock(ptr1, ptr2, len1, len2);
            }
        }

        private uint Advance(uint cursor, int samples) =>
            (uint)(((long)cursor + samples) % ringSamples);
    }
}
