// "Hear yourself" for the audio options page.
//
// It runs the REAL pipeline -- capture, Opus encode, Opus decode, FMOD playback -- rather than
// echoing raw microphone samples, because the point of a microphone test is to hear what everyone
// else will hear: the codec's colouring, the gate clipping a quiet word, a device recording at the
// wrong rate. A test that skipped the codec would sound better than the game ever does.
//
// It is also the first thing proving the voice pipeline works end to end, so when it plays back
// cleanly, capture, resampling, the codec and playback are all sound and only the network remains.
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Voice
{
    /// <summary>
    /// A self-contained loopback of the local microphone, owned by whatever screen is showing it.
    /// Create it with <see cref="Create"/> and destroy the GameObject to stop.
    /// </summary>
    public sealed class MicrophoneTest : MonoBehaviour
    {
        private VoiceCapture capture;
        private VoiceEncoder encoder;
        private VoiceDecoder decoder;
        private VoicePlayback playback;
        private readonly VoiceGate gate = new VoiceGate();

        private byte[] packet;
        private short[] decoded;
        private float appliedVolume = -1f;

        /// <summary>Peak input level, 0-1 — what an input meter draws.</summary>
        public float InputLevel => capture?.InputLevel ?? 0f;

        /// <summary>Whether the gate is currently passing audio, for a live "transmitting" light.</summary>
        public bool GateOpen => gate.IsOpen;

        /// <summary>Player-readable reason the test is not running, or null when it is.</summary>
        public string Error { get; private set; }

        public bool IsRunning => capture != null && capture.IsRecording;

        /// <summary>The device being listened to, for display.</summary>
        public string DeviceName => capture?.DeviceName;

        // How many tests are open. Live voice reads this and releases the microphone rather than
        // calling recordStart on a driver the test already holds.
        private static int running;

        /// <summary>Whether any microphone test currently holds the recording device.</summary>
        public static bool AnyRunning => running > 0;

        /// <summary>Creates the test on its own GameObject, already running.</summary>
        public static MicrophoneTest Create()
        {
            var host = new GameObject(nameof(MicrophoneTest));
            return host.AddComponent<MicrophoneTest>();
        }

        /// <summary>
        /// Reopens the microphone — after the player picks a different device, or plugs one in.
        /// </summary>
        public void Restart()
        {
            Teardown();
            Setup();
        }

        private void OnEnable()
        {
            running++;
            Setup();
        }

        // Both exits, because they are different: OnDisable is the screen closing, OnDestroy is the
        // object going away, and an FMOD handle leaked on either one keeps the microphone open.
        private void OnDisable()
        {
            running = Mathf.Max(0, running - 1);
            Teardown();
        }

        private void OnDestroy() => Teardown();

        private void Update()
        {
            if (capture == null || !capture.IsRecording) return;

            capture.Poll(OnFrame);

            // Parks playback once the gate has been shut a while; see VoicePlayback.Tick.
            playback?.Tick(Time.unscaledDeltaTime);

            // Only on change. Pushing the same gain into FMOD every frame is a call per frame that
            // buys nothing.
            float volume = GameSettings.VoiceVolume;
            if (!Mathf.Approximately(appliedVolume, volume))
            {
                appliedVolume = volume;
                playback?.SetVolume(volume);
            }
        }

        private void OnFrame(short[] pcm)
        {
            // The gate is stepped per AUDIO frame, in audio time, and judged on that frame's own
            // level. Stepping it once per rendered frame after Poll meant each frame was gated on
            // the previous one's decision, which swallowed the first frame of every phrase — 60 ms
            // off the front of every word. Honouring it here is also what makes the threshold
            // slider tunable: the player drags it until their voice opens it and their keyboard
            // does not.
            float level = VoiceGate.Peak(pcm, pcm.Length);
            if (!gate.Step(level, GameSettings.VoiceGateThreshold,
                           VoiceFormat.FrameMilliseconds / 1000f))
            {
                return;
            }

            int bytes = encoder.Encode(pcm, packet);
            if (bytes <= 0) return;

            int samples = decoder.Decode(packet, bytes, decoded);
            if (samples > 0) playback.Submit(decoded, samples);
        }

        private void Setup()
        {
            Error = null;

            if (!VoiceDevices.TryResolve(GameSettings.VoiceInputDevice, out VoiceDevices.Device device))
            {
                Error = "No microphone is connected.";
                return;
            }

            packet = new byte[VoiceFormat.MaxPacketBytes];
            decoded = new short[VoiceFormat.FrameSamples];
            encoder = new VoiceEncoder();
            decoder = new VoiceDecoder();

            capture = new VoiceCapture();
            if (!capture.TryStart(device, out string captureError))
            {
                Error = captureError;
                Teardown();
                return;
            }

            playback = new VoicePlayback();
            if (!playback.TryStart(out string playbackError))
            {
                Error = playbackError;
                Teardown();
                return;
            }

            appliedVolume = GameSettings.VoiceVolume;
            playback.SetVolume(appliedVolume);
        }

        private void Teardown()
        {
            capture?.Dispose();
            capture = null;

            playback?.Dispose();
            playback = null;

            encoder?.Dispose();
            encoder = null;

            decoder?.Dispose();
            decoder = null;

            gate.Reset();
            appliedVolume = -1f;
        }
    }
}
