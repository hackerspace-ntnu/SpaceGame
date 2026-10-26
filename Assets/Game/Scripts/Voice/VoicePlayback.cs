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
//
// A voice starts flat (2D) and is made spatial by its owner once it has somewhere to be. Spatial
// voices are FMOD Core 3D channels, which are placed relative to FMOD's listener -- and the
// StudioListener on the camera already drives that one, since the Studio system owns the Core
// system underneath it. SfxFile's 3D one-shots rely on exactly the same thing.
//
// A voice made with the suit radio (VoiceRadio) plays the same ring on a second channel: flat, run
// through a band-pass and a little distortion, at whatever level the owner sets. Both channels live
// in one channel group per voice, and pausing is done on the group, so the two always start, stop
// and run in step: they read one buffer at one position, which is what lets them be heard together
// without the second sounding like an echo of the first.
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SpaceGame.Voice
{
    /// <summary>
    /// One voice being heard: PCM frames in, audible sound out through FMOD's master group —
    /// in person, and optionally over the radio too. Nothing here throws.
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

        private readonly bool withRadio;

        private FMOD.Sound sound;
        private FMOD.ChannelGroup group;
        private FMOD.Channel channel;

        // The radio path: a second channel on the same sound, and the two effects that make it a radio.
        private FMOD.Channel radio;
        private FMOD.DSP radioBand;
        private FMOD.DSP radioCrunch;
        private float radioBlend;
        private float appliedRadioLevel = -1f;
        private float appliedCrunch = -1f;
        private float squelchVolume;
        private float appliedVolume = 1f;

        // Generated once per FMOD system and shared by every voice. See EnsureSquelch.
        private static FMOD.Sound openSquelch;
        private static FMOD.Sound closeSquelch;
        private static IntPtr squelchSystem;
        private uint writeCursor;
        private int ringSamples;
        private bool primed;
        private short[] quiet;
        private float idleSeconds;

        // What was last pushed to FMOD for placement, so nothing is re-sent unless it changed.
        private bool spatial;
        private float appliedNear = -1f;
        private float appliedFar = -1f;

        /// <summary>The channel modes this uses. LOOP_NORMAL rides along on every change: the ring
        /// only works while it loops, and setMode is handed the whole mode each time.</summary>
        private const FMOD.MODE FlatMode = FMOD.MODE.LOOP_NORMAL | FMOD.MODE._2D;
        private const FMOD.MODE SpatialMode = FMOD.MODE.LOOP_NORMAL | FMOD.MODE._3D |
                                              FMOD.MODE._3D_WORLDRELATIVE |
                                              FMOD.MODE._3D_LINEARSQUAREROLLOFF;

        public bool IsRunning => sound.hasHandle();

        /// <summary>Whether this voice is currently placed in the world rather than played flat.</summary>
        public bool IsSpatial => spatial;

        /// <param name="withRadio">
        /// Also open the radio path. Off for the microphone test, which has no distance to fade over.
        /// </param>
        public VoicePlayback(bool withRadio = false)
        {
            this.withRadio = withRadio;
        }

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

                // Created 3D-capable, then played flat: the channel can be moved between the two at
                // any time, and flat is the right default for anything that never says otherwise
                // (the microphone test, a voice in the lobby).
                FMOD.RESULT result = core.createSound(IntPtr.Zero,
                    FMOD.MODE.OPENUSER | FMOD.MODE.LOOP_NORMAL | FMOD.MODE._3D, ref exinfo, out sound);

                if (result != FMOD.RESULT.OK)
                {
                    error = $"Could not create the playback buffer ({result}).";
                    return false;
                }

                // FMOD does not promise a zeroed buffer, and an unprimed ring loops whatever was in
                // that memory until the first frame lands on it.
                Silence();

                core.getMasterChannelGroup(out FMOD.ChannelGroup master);

                // The group starts paused, so every channel added to it waits at position 0 and the
                // first Submit releases them together.
                result = core.createChannelGroup("Voice", out group);
                if (result == FMOD.RESULT.OK) result = master.addGroup(group);
                if (result == FMOD.RESULT.OK) result = group.setPaused(true);

                if (result != FMOD.RESULT.OK)
                {
                    Stop();
                    error = $"Could not create the voice's channel group ({result}).";
                    return false;
                }

                result = core.playSound(sound, group, false, out channel);

                if (result != FMOD.RESULT.OK)
                {
                    Stop();
                    error = $"Could not start playback ({result}).";
                    return false;
                }

                result = channel.setMode(FlatMode);
                if (result != FMOD.RESULT.OK)
                    Debug.LogWarning($"[Voice] Could not start a voice flat ({result}); it may play positioned at the origin.");

                // Started in the same breath as the voice channel and into the same paused group,
                // so the two are at the same position from the first sample on. A radio that fails
                // to open costs the radio, not the voice.
                if (withRadio && !TryStartRadio(core, out string radioError))
                    Debug.LogWarning($"[Voice] No radio for this voice: {radioError}");

                quiet = new short[VoiceFormat.FrameSamples * TrailingSilenceFrames];
                writeCursor = 0;
                idleSeconds = 0f;
                primed = false;
                spatial = false;
                appliedNear = -1f;
                appliedFar = -1f;
                appliedVolume = 1f;
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

                if (group.hasHandle()) group.setPaused(false);
                primed = true;

                // The click lands a couple of frames before the voice does — the write lead — which
                // is the order a real radio puts them in.
                PlaySquelch(openSquelch);
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

            if (group.hasHandle()) group.setPaused(true);
            primed = false;
            idleSeconds = 0f;

            PlaySquelch(closeSquelch);
        }

        /// <summary>
        /// The loudest a voice may be driven: twice its own level, so a quiet microphone can be
        /// brought up to meet the others. FMOD treats channel volume as a linear multiplier and
        /// amplifies above 1, and it mixes in floating point, so the boost cannot clip inside the
        /// mixer — only at the output, and only if the speaker was already loud, which is the
        /// opposite of why anyone would turn them up.
        /// </summary>
        public const float MaxVolume = 2f;

        /// <summary>
        /// Linear gain, 0 to <see cref="MaxVolume"/>. This voice only — the buses still apply — and
        /// both of its paths: someone turned down is turned down on the radio as well.
        /// </summary>
        public void SetVolume(float volume)
        {
            appliedVolume = Mathf.Clamp(volume, 0f, MaxVolume);
            if (group.hasHandle()) group.setVolume(appliedVolume);
        }

        /// <summary>
        /// How much of this voice comes over the radio: <paramref name="blend"/> from 0 (in person
        /// only) to 1 (radio only), played at <paramref name="volume"/> times the voice's own level,
        /// with <paramref name="crunch"/> of distortion. <paramref name="squelch"/> is the level of
        /// the static at either end of a transmission, 0 for none. Cheap to call every frame: FMOD
        /// is only told about what changed. Does nothing for a voice opened without the radio.
        /// </summary>
        public void SetRadio(float blend, float volume, float crunch, float squelch)
        {
            radioBlend = Mathf.Clamp01(blend);
            squelchVolume = Mathf.Clamp01(squelch);

            if (!radio.hasHandle()) return;

            float level = radioBlend * Mathf.Clamp(volume, 0f, MaxVolume);
            if (!Mathf.Approximately(level, appliedRadioLevel))
            {
                radio.setVolume(level);
                appliedRadioLevel = level;
            }

            crunch = Mathf.Clamp01(crunch);
            if (radioCrunch.hasHandle() && !Mathf.Approximately(crunch, appliedCrunch))
            {
                radioCrunch.setParameterFloat((int)FMOD.DSP_DISTORTION.LEVEL, crunch);
                appliedCrunch = crunch;
            }
        }

        /// <summary>
        /// Places this voice in the world, fading from full volume at <paramref name="nearDistance"/>
        /// to silence at <paramref name="farDistance"/> — or, with <paramref name="enabled"/> false,
        /// plays it flat at full volume wherever the listener is. Cheap to call every frame: FMOD is
        /// only told about what changed.
        /// <para>
        /// The fade is FMOD's linear-squared rolloff. Its inverse rolloff, the physically honest
        /// one, never actually reaches zero — a voice would stay faintly audible to the far edge of
        /// the map, which is precisely what proximity chat is for not doing. Linear-squared reaches
        /// silence at the far distance and still reads as natural, falling quickly close in and
        /// trailing off gently.
        /// </para>
        /// </summary>
        public void SetSpatial(bool enabled, float nearDistance, float farDistance)
        {
            if (!channel.hasHandle()) return;

            if (enabled != spatial)
            {
                FMOD.RESULT result = channel.setMode(enabled ? SpatialMode : FlatMode);
                if (result != FMOD.RESULT.OK)
                {
                    Debug.LogWarning($"[Voice] Could not switch a voice {(enabled ? "into" : "out of")} 3D ({result}).");
                    return;
                }

                spatial = enabled;

                // Doppler off: players move slowly, network positions arrive in steps, and a voice
                // that bends in pitch every time a position update lands sounds broken, not real.
                if (enabled) channel.set3DDopplerLevel(0f);
            }

            if (!enabled) return;

            if (Mathf.Approximately(nearDistance, appliedNear) &&
                Mathf.Approximately(farDistance, appliedFar))
            {
                return;
            }

            if (channel.set3DMinMaxDistance(nearDistance, farDistance) == FMOD.RESULT.OK)
            {
                appliedNear = nearDistance;
                appliedFar = farDistance;
            }
        }

        /// <summary>Where a spatial voice comes from, in world space. Ignored while it plays flat.</summary>
        public void SetPosition(Vector3 position)
        {
            if (!spatial || !channel.hasHandle()) return;

            FMOD.VECTOR at = FMODUnity.RuntimeUtils.ToFMODVector(position);
            FMOD.VECTOR still = default;
            channel.set3DAttributes(ref at, ref still);
        }

        public void Stop()
        {
            if (channel.hasHandle())
            {
                channel.stop();
                channel.clearHandle();
            }

            StopRadio();

            if (group.hasHandle())
            {
                group.release();
                group.clearHandle();
            }

            if (sound.hasHandle())
            {
                sound.release();
                sound.clearHandle();
            }

            quiet = null;
            idleSeconds = 0f;
            primed = false;
            spatial = false;
            appliedNear = -1f;
            appliedFar = -1f;
            radioBlend = 0f;
            squelchVolume = 0f;
        }

        public void Dispose() => Stop();

        // ---------------------------------------------------------------------- radio

        private bool TryStartRadio(FMOD.System core, out string error)
        {
            FMOD.RESULT result = core.playSound(sound, group, false, out radio);
            if (result == FMOD.RESULT.OK) result = radio.setMode(FlatMode);
            if (result == FMOD.RESULT.OK) result = radio.setVolume(0f);

            // A radio is narrow: it passes the band a voice is understood in and little else.
            if (result == FMOD.RESULT.OK) result = core.createDSPByType(FMOD.DSP_TYPE.MULTIBAND_EQ, out radioBand);
            if (result == FMOD.RESULT.OK)
            {
                radioBand.setParameterInt((int)FMOD.DSP_MULTIBAND_EQ.A_FILTER,
                                          (int)FMOD.DSP_MULTIBAND_EQ_FILTER_TYPE.HIGHPASS_12DB);
                radioBand.setParameterFloat((int)FMOD.DSP_MULTIBAND_EQ.A_FREQUENCY, VoiceRadio.LowCutHz);
                radioBand.setParameterInt((int)FMOD.DSP_MULTIBAND_EQ.B_FILTER,
                                          (int)FMOD.DSP_MULTIBAND_EQ_FILTER_TYPE.LOWPASS_24DB);
                radioBand.setParameterFloat((int)FMOD.DSP_MULTIBAND_EQ.B_FREQUENCY, VoiceRadio.HighCutHz);
                radioBand.setParameterInt((int)FMOD.DSP_MULTIBAND_EQ.C_FILTER,
                                          (int)FMOD.DSP_MULTIBAND_EQ_FILTER_TYPE.PEAKING);
                radioBand.setParameterFloat((int)FMOD.DSP_MULTIBAND_EQ.C_FREQUENCY, VoiceRadio.PresenceHz);
                radioBand.setParameterFloat((int)FMOD.DSP_MULTIBAND_EQ.C_GAIN, VoiceRadio.PresenceGainDb);
                result = radio.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.TAIL, radioBand);
            }

            // Added at the tail after the band-pass, so it runs first: crunch, then filter, which
            // keeps the distortion's harshest overtones out of the result.
            if (result == FMOD.RESULT.OK) result = core.createDSPByType(FMOD.DSP_TYPE.DISTORTION, out radioCrunch);
            if (result == FMOD.RESULT.OK)
            {
                radioCrunch.setParameterFloat((int)FMOD.DSP_DISTORTION.LEVEL, 0f);
                appliedCrunch = 0f;
                result = radio.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.TAIL, radioCrunch);
            }

            if (result != FMOD.RESULT.OK)
            {
                StopRadio();
                error = result.ToString();
                return false;
            }

            appliedRadioLevel = 0f;

            if (!EnsureSquelch(core))
                Debug.LogWarning("[Voice] Could not create the radio squelch sounds; the radio will be silent at either end.");

            error = null;
            return true;
        }

        private void StopRadio()
        {
            if (radio.hasHandle())
            {
                // An effect still attached to a channel refuses to be released, so off they come first.
                if (radioCrunch.hasHandle()) radio.removeDSP(radioCrunch);
                if (radioBand.hasHandle()) radio.removeDSP(radioBand);

                radio.stop();
                radio.clearHandle();
            }

            if (radioCrunch.hasHandle())
            {
                radioCrunch.release();
                radioCrunch.clearHandle();
            }

            if (radioBand.hasHandle())
            {
                radioBand.release();
                radioBand.clearHandle();
            }

            appliedRadioLevel = -1f;
            appliedCrunch = -1f;
        }

        /// <summary>
        /// Plays one of the squelch bursts if this voice is mostly on the radio. Straight onto the
        /// master group rather than the voice's own, so the closing burst is not cut off by the very
        /// pause it marks; the voice's volume is applied by hand instead.
        /// </summary>
        private void PlaySquelch(FMOD.Sound burst)
        {
            if (!radio.hasHandle() || squelchVolume <= 0f || radioBlend < VoiceRadio.MostlyRadio) return;
            if (!burst.hasHandle()) return;

            FMOD.System core = FMODUnity.RuntimeManager.CoreSystem;
            if (core.handle != squelchSystem) return;

            if (core.getMasterChannelGroup(out FMOD.ChannelGroup master) != FMOD.RESULT.OK) return;
            if (core.playSound(burst, master, true, out FMOD.Channel click) != FMOD.RESULT.OK) return;

            click.setVolume(squelchVolume * radioBlend * appliedVolume);
            click.setPaused(false);
        }

        /// <summary>
        /// The two squelch bursts, created once for the FMOD system that is running. They belong to
        /// that system: if it has been replaced the old handles are dead, so they are made again
        /// rather than released.
        /// </summary>
        private static bool EnsureSquelch(FMOD.System core)
        {
            if (squelchSystem == core.handle && openSquelch.hasHandle() && closeSquelch.hasHandle()) return true;

            openSquelch.clearHandle();
            closeSquelch.clearHandle();
            squelchSystem = IntPtr.Zero;

            if (!TryCreateClip(core, VoiceRadio.Squelch(VoiceRadio.OpenSamples, 17), out openSquelch)) return false;
            if (!TryCreateClip(core, VoiceRadio.Squelch(VoiceRadio.CloseSamples, 23), out closeSquelch))
            {
                openSquelch.release();
                openSquelch.clearHandle();
                return false;
            }

            squelchSystem = core.handle;
            return true;
        }

        /// <summary>A short, flat, one-shot FMOD sound holding <paramref name="pcm"/>.</summary>
        private static bool TryCreateClip(FMOD.System core, short[] pcm, out FMOD.Sound clip)
        {
            clip = default;
            if (pcm == null || pcm.Length == 0) return false;

            var exinfo = new FMOD.CREATESOUNDEXINFO
            {
                cbsize = Marshal.SizeOf(typeof(FMOD.CREATESOUNDEXINFO)),
                numchannels = 1,
                defaultfrequency = VoiceFormat.SampleRate,
                format = FMOD.SOUND_FORMAT.PCM16,
                length = (uint)(pcm.Length * sizeof(short)),
            };

            if (core.createSound(IntPtr.Zero, FMOD.MODE.OPENUSER | FMOD.MODE.LOOP_OFF | FMOD.MODE._2D,
                                 ref exinfo, out clip) != FMOD.RESULT.OK)
            {
                return false;
            }

            if (clip.@lock(0, exinfo.length, out IntPtr ptr1, out IntPtr ptr2, out uint len1, out uint len2)
                != FMOD.RESULT.OK)
            {
                clip.release();
                clip.clearHandle();
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
                clip.unlock(ptr1, ptr2, len1, len2);
            }

            return true;
        }

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
