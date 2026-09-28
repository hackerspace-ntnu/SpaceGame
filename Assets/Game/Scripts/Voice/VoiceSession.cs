// Live voice for the whole application run: capture, routing and one playback per person heard.
//
// It exists from startup and watches for a netcode session rather than being spawned by one, for
// the same reason PauseMenuUI is bootstrapped from a static: the session begins in the MAIN MENU
// (the host StartHosts before it creates its lobby) and continues through the scene load into the
// world. Anything placed in a scene would miss one half of that.
//
// ## What this is not, yet
//
// Routing is VoiceRouter.Everyone -- every listener hears every speaker, at one volume, wherever
// they are. That is correct in the lobby, where there are no bodies to measure between, and it is
// deliberately WRONG in the world: proximity, the audible radius, the cap on simultaneous speakers
// and the squad channel all land in VoiceRouter next, and playback becomes 3D per speaker. Until
// then, in-game voice is global.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Voice
{
    /// <summary>
    /// The one live voice pipeline. Starts itself when a session appears and tears itself down when
    /// one ends; nothing has to create or find it.
    /// </summary>
    public sealed class VoiceSession : MonoBehaviour
    {
        /// <summary>
        /// How long a voice may go quiet before its decoder and FMOD channel are released. Long
        /// enough to outlast an ordinary pause in speech, short enough that a player who left does
        /// not hold a channel for the rest of the match.
        /// </summary>
        private const float ReapSeconds = 5f;

        /// <summary>Retry gap after the microphone refuses to open, so it is not asked every frame.</summary>
        private const float RetrySeconds = 3f;

        private static VoiceSession instance;

        private readonly VoiceTransport transport = new VoiceTransport();
        private readonly VoiceGate gate = new VoiceGate();
        private readonly Dictionary<ulong, Heard> heard = new Dictionary<ulong, Heard>();
        private readonly List<ulong> listeners = new List<ulong>();
        private readonly List<ulong> finished = new List<ulong>();

        private VoiceCapture capture;
        private VoiceEncoder encoder;
        private byte[] packet;
        private short[] decoded;

        private float retryIn;
        private float appliedVolume = -1f;
        private bool complained;

        /// <summary>One person being listened to.</summary>
        private sealed class Heard
        {
            public VoiceDecoder Decoder;
            public VoicePlayback Playback;
            public float SilentFor;
        }

        /// <summary>Whether the local microphone is currently open and transmitting frames.</summary>
        public static bool IsCapturing => instance != null && instance.capture != null &&
                                          instance.capture.IsRecording;

        /// <summary>How many other people are audible right now — a cheap "is this working" readout.</summary>
        public static int VoicesHeard => instance != null ? instance.heard.Count : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var host = new GameObject(nameof(VoiceSession));
            DontDestroyOnLoad(host);
            instance = host.AddComponent<VoiceSession>();
        }

        private void OnEnable()
        {
            transport.FrameFromClient += OnFrameFromClient;
            transport.FrameToPlay += OnFrameToPlay;
        }

        private void OnDisable()
        {
            transport.FrameFromClient -= OnFrameFromClient;
            transport.FrameToPlay -= OnFrameToPlay;
            Stop();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            Stop();
        }

        private void Update()
        {
            if (!Network.IsNetworked)
            {
                Stop();
                return;
            }

            // Idempotent, and re-attaches when the NetworkManager has been replaced — which it is
            // on every new session, so subscribing once at startup would silently stop working.
            transport.Register();

            float delta = Time.unscaledDeltaTime;

            DriveCapture(delta);
            DriveListeners(delta);
        }

        // ------------------------------------------------------------------- speaking

        private void DriveCapture(float delta)
        {
            // The microphone test owns the device while it is open. Two recordStart calls on one
            // driver is at best a fight over the same buffer, so the test wins and live voice waits.
            if (MicrophoneTest.AnyRunning)
            {
                StopCapture();
                return;
            }

            if (capture == null || !capture.IsRecording)
            {
                retryIn -= delta;
                if (retryIn > 0f) return;

                retryIn = RetrySeconds;
                StartCapture();
                return;
            }

            capture.Poll(OnCapturedFrame);
        }

        private void StartCapture()
        {
            StopCapture();

            if (!VoiceDevices.TryResolve(GameSettings.VoiceInputDevice, out VoiceDevices.Device device))
            {
                Complain("no microphone is connected");
                return;
            }

            packet ??= new byte[VoiceFormat.MaxPacketBytes];
            decoded ??= new short[VoiceFormat.FrameSamples];
            encoder ??= new VoiceEncoder();

            capture = new VoiceCapture();
            if (capture.TryStart(device, out string error))
            {
                complained = false;
                return;
            }

            Complain(error);
            capture.Dispose();
            capture = null;
        }

        private void StopCapture()
        {
            capture?.Dispose();
            capture = null;
            gate.Reset();
        }

        private void OnCapturedFrame(short[] pcm)
        {
            // Push-to-talk is NOT honoured yet: the setting is stored but has no key bound to it,
            // so gating on it would mute everyone who switched it on. The gate is what decides for
            // now — see GameSettings.VoicePushToTalk.
            float level = VoiceGate.Peak(pcm, pcm.Length);
            if (!gate.Step(level, GameSettings.VoiceGateThreshold,
                           VoiceFormat.FrameMilliseconds / 1000f))
            {
                return;
            }

            int bytes = encoder.Encode(pcm, packet);
            if (bytes > 0) transport.SendToServer(packet, bytes);
        }

        // ------------------------------------------------------------------- routing

        private void OnFrameFromClient(ulong sender, byte[] frame, int length)
        {
            // ConnectedClientsIds is server-only, which is exactly where this runs.
            VoiceRouter.Everyone(NetworkManager.Singleton.ConnectedClientsIds, sender, listeners);
            transport.SendToListeners(listeners, sender, frame, length);
        }

        // ------------------------------------------------------------------ listening

        private void OnFrameToPlay(ulong speaker, byte[] frame, int length)
        {
            // Never your own voice. On a host the relay loops back through the same handler, and
            // hearing yourself a round trip later is the most disorienting thing voice can do.
            if (speaker == Network.LocalClientId) return;

            Heard voice = Resolve(speaker);
            if (voice == null) return;

            voice.SilentFor = 0f;

            int samples = voice.Decoder.Decode(frame, length, decoded);
            if (samples > 0) voice.Playback.Submit(decoded, samples);
        }

        private Heard Resolve(ulong speaker)
        {
            if (heard.TryGetValue(speaker, out Heard existing)) return existing;

            decoded ??= new short[VoiceFormat.FrameSamples];

            var playback = new VoicePlayback();
            if (!playback.TryStart(out string error))
            {
                Complain($"could not open playback for client {speaker}: {error}");
                return null;
            }

            var voice = new Heard { Decoder = new VoiceDecoder(), Playback = playback };
            playback.SetVolume(GameSettings.VoiceVolume);

            heard[speaker] = voice;
            return voice;
        }

        private void DriveListeners(float delta)
        {
            float volume = GameSettings.VoiceVolume;
            bool volumeChanged = !Mathf.Approximately(appliedVolume, volume);
            if (volumeChanged) appliedVolume = volume;

            finished.Clear();

            foreach (KeyValuePair<ulong, Heard> entry in heard)
            {
                Heard voice = entry.Value;

                voice.SilentFor += delta;
                voice.Playback.Tick(delta);

                if (volumeChanged) voice.Playback.SetVolume(volume);
                if (voice.SilentFor >= ReapSeconds) finished.Add(entry.Key);
            }

            foreach (ulong id in finished) Release(id);
        }

        private void Release(ulong speaker)
        {
            if (!heard.TryGetValue(speaker, out Heard voice)) return;

            voice.Playback?.Dispose();
            voice.Decoder?.Dispose();
            heard.Remove(speaker);
        }

        // ------------------------------------------------------------------ lifetime

        private void Stop()
        {
            StopCapture();

            foreach (Heard voice in heard.Values)
            {
                voice.Playback?.Dispose();
                voice.Decoder?.Dispose();
            }

            heard.Clear();

            encoder?.Dispose();
            encoder = null;

            transport.Unregister();
            appliedVolume = -1f;
            retryIn = 0f;
        }

        // Once. A machine with no microphone is a normal state, not a per-frame complaint — but it
        // is also the commonest reason voice "does not work", so it is said rather than swallowed.
        private void Complain(string reason)
        {
            if (complained) return;

            complained = true;
            Debug.LogWarning($"[Voice] Not transmitting: {reason}");
        }
    }
}
