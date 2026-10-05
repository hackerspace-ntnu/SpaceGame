// Live voice for the whole application run: capture, routing, names, and one playback per person.
//
// It exists from startup and watches for a netcode session rather than being spawned by one, for
// the same reason PauseMenuUI is bootstrapped from a static: the session begins in the MAIN MENU
// (the host StartHosts before it creates its lobby) and continues through the scene load into the
// world. Anything placed in a scene would miss one half of that.
//
// ## Proximity
//
// In the world a voice carries VoiceTuning.SilentDistance metres and no farther, in two halves.
// The host only relays a frame to listeners within range of the speaker (VoiceRouter.Proximity),
// so distance is enforced where a client cannot argue with it. Each listener then plays what it
// receives as a 3D voice at the speaker's mouth, fading out by that same distance. In the lobby
// nobody has a body, so both halves stand down and everyone hears everyone, flat.
//
// ## What this is not, yet
//
// No cap on simultaneous speakers and no bandwidth budget: a crowd all talking in one spot is
// every one of them to every one of them. No squad channel or radio, so voice is proximity only.
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Voice
{
    /// <summary>
    /// The one live voice pipeline. Starts itself when a session appears and tears itself down when
    /// one ends; nothing has to create or find it. The static readouts are what the speaking
    /// indicators draw from.
    /// </summary>
    public sealed class VoiceSession : MonoBehaviour
    {
        /// <summary>
        /// How long a voice may go quiet before its decoder and FMOD channel are released. Long
        /// enough to outlast an ordinary pause in speech, short enough that a player who left does
        /// not hold a channel for the rest of the match.
        /// </summary>
        private const float ReapSeconds = 5f;

        /// <summary>
        /// How long someone still reads as speaking after their last frame. Frames land every 60 ms
        /// and the network does not deliver them evenly; without a hold an indicator flickers on
        /// every gap between two frames of the same word.
        /// </summary>
        public const float SpeakingHoldSeconds = 0.3f;

        /// <summary>Retry gap after the microphone refuses to open, so it is not asked every frame.</summary>
        private const float RetrySeconds = 3f;

        /// <summary>
        /// How often this peer re-announces its name until the server's table shows it. The first
        /// announcement can land before the server has registered its handler, and NGO drops a
        /// named message nobody is listening for — resending until acknowledged is what heals that.
        /// </summary>
        private const float NameResendSeconds = 2f;

        /// <summary>How often the server sweeps the name table for peers who have left.</summary>
        private const float RosterSweepSeconds = 1f;

        private static VoiceSession instance;

        private readonly VoiceTransport transport = new VoiceTransport();
        private readonly VoiceKeys keys = new VoiceKeys();
        private readonly VoiceGate gate = new VoiceGate();
        private readonly Dictionary<ulong, Heard> heard = new Dictionary<ulong, Heard>();
        private readonly List<ulong> listeners = new List<ulong>();
        private readonly List<ulong> finished = new List<ulong>();

        // Where every spawned player stands this frame, by client id. Empty in the lobby, which is
        // what makes routing and playback fall back to everyone-hears-everyone there.
        private readonly Dictionary<ulong, Vector3> bodies = new Dictionary<ulong, Vector3>();

        // The table as this peer last received it, and — on the server only — the authoritative one.
        private readonly Dictionary<ulong, VoiceRoster.Entry> names =
            new Dictionary<ulong, VoiceRoster.Entry>();
        private readonly Dictionary<ulong, VoiceRoster.Entry> serverNames =
            new Dictionary<ulong, VoiceRoster.Entry>();
        private readonly List<ulong> departed = new List<ulong>();

        private VoiceCapture capture;
        private VoiceEncoder encoder;
        private byte[] packet;
        private short[] decoded;

        private float retryIn;
        private float nameResendIn;
        private float rosterSweepIn;
        private bool complained;

        /// <summary>One person being listened to.</summary>
        private sealed class Heard
        {
            public VoiceDecoder Decoder;
            public VoicePlayback Playback;
            public float SilentFor;

            // What was last pushed into FMOD, so a volume is only sent when it actually changes.
            public float AppliedVolume = -1f;
        }

        // ------------------------------------------------------------------- readouts

        /// <summary>
        /// True while the local microphone is open AND the gate is passing audio — what "you are
        /// being heard right now" means, and the thing a player on open mic most needs to see.
        /// </summary>
        public static bool IsTransmitting =>
            instance != null && instance.capture != null && instance.capture.IsRecording &&
            instance.gate.IsOpen;

        /// <summary>The player has muted their own microphone.</summary>
        public static bool IsSelfMuted => GameSettings.VoiceSelfMuted;

        /// <summary>
        /// Push-to-talk is on and its channel is open — held, in its release tail, or latched. True
        /// over silence too, which is the point: a latched toggle is live whether or not you are
        /// speaking, and the player has to be able to see that.
        /// </summary>
        public static bool IsPushToTalkOpen =>
            instance != null && GameSettings.VoicePushToTalk && instance.keys.TalkOpen;

        /// <summary>
        /// Somebody else is in the session to hear you. False in singleplayer — a host of one —
        /// where a "you are muted" readout would be answering a question nobody asked.
        /// </summary>
        public static bool HasCompany => Network.IsNetworked && !IsAlone();

        /// <summary>
        /// Whether <paramref name="clientId"/> has been heard within the speaking hold. Someone you
        /// have muted, or turned to 0%, is never "speaking": their frames are not decoded at all,
        /// so an indicator never lights for a voice you have chosen not to hear.
        /// </summary>
        public static bool IsSpeaking(ulong clientId) =>
            instance != null && instance.heard.TryGetValue(clientId, out Heard voice) &&
            voice.SilentFor < SpeakingHoldSeconds;

        /// <summary>
        /// Everyone currently speaking, sorted by client id so a list drawn from it keeps its order
        /// instead of reshuffling every frame. <paramref name="into"/> is cleared first.
        /// </summary>
        public static void CollectSpeaking(List<ulong> into)
        {
            into.Clear();
            if (instance == null) return;

            foreach (KeyValuePair<ulong, Heard> entry in instance.heard)
            {
                if (entry.Value.SilentFor < SpeakingHoldSeconds) into.Add(entry.Key);
            }

            into.Sort();
        }

        /// <summary>
        /// The name <paramref name="clientId"/> announced, or the same <c>Player N</c> stand-in
        /// <see cref="PlayerIdentity.DisplayName"/> uses — so one person is never two different
        /// placeholders in two places while their name is in flight.
        /// </summary>
        public static string NameOf(ulong clientId)
        {
            if (instance != null && instance.names.TryGetValue(clientId, out VoiceRoster.Entry entry) &&
                entry.Name.Length > 0)
            {
                return entry.Name;
            }

            return $"Player {clientId + 1}";
        }

        /// <summary>
        /// The Unity account id <paramref name="clientId"/> announced, or empty — which is also the
        /// answer before their announcement has arrived, and in a session with no Unity Services.
        /// </summary>
        public static string AccountOf(ulong clientId) =>
            instance != null && instance.names.TryGetValue(clientId, out VoiceRoster.Entry entry)
                ? entry.AccountId
                : string.Empty;

        /// <summary>How many other people are audible right now — a cheap "is this working" readout.</summary>
        public static int VoicesHeard => instance != null ? instance.heard.Count : 0;

        // ------------------------------------------------------------------- lifetime

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
            transport.NameFromClient += OnNameFromClient;
            transport.RosterReceived += OnRosterReceived;
        }

        private void OnDisable()
        {
            transport.FrameFromClient -= OnFrameFromClient;
            transport.FrameToPlay -= OnFrameToPlay;
            transport.NameFromClient -= OnNameFromClient;
            transport.RosterReceived -= OnRosterReceived;
            Stop();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            Stop();
            keys.Dispose();
        }

        private void Update()
        {
            float delta = Time.unscaledDeltaTime;

            // Before the session check: the mute key works in the main menu too, so a player can
            // mute themselves before they ever join.
            keys.Tick(delta);

            if (!Network.IsNetworked)
            {
                Stop();
                return;
            }

            // Idempotent, and re-attaches when the NetworkManager has been replaced — which it is
            // on every new session, so subscribing once at startup would silently stop working.
            transport.Register();

            RefreshBodies();
            DriveNames(delta);
            DriveCapture(delta);
            DriveListeners(delta);
        }

        // ---------------------------------------------------------------------- names

        private void DriveNames(float delta)
        {
            // Re-announce until the table carries this peer under the name it actually has. That
            // one test covers the first announcement getting lost, a rename in the pause menu, and
            // a roster that arrived before this peer's own entry was in it.
            string mine = GameSettings.SanitiseName(GameSettings.PlayerName);
            string account = VoiceAccount.LocalId;

            bool acknowledged = names.TryGetValue(Network.LocalClientId, out VoiceRoster.Entry listed) &&
                                listed.Name == mine && listed.AccountId == account;

            if (!acknowledged)
            {
                nameResendIn -= delta;
                if (nameResendIn <= 0f)
                {
                    nameResendIn = NameResendSeconds;
                    transport.SendIdentity(mine, account);
                }
            }

            if (!Network.Server) return;

            rosterSweepIn -= delta;
            if (rosterSweepIn > 0f) return;

            rosterSweepIn = RosterSweepSeconds;
            if (DropDeparted()) transport.SendRoster(serverNames);
        }

        private void OnNameFromClient(ulong sender, VoiceRoster.Entry identity)
        {
            // Filed under the id the message arrived FROM — a client can only ever describe itself.
            serverNames[sender] = new VoiceRoster.Entry(GameSettings.SanitiseName(identity.Name),
                                                        VoiceRoster.CleanAccountId(identity.AccountId));

            // Sent even when nothing changed: a peer re-announcing an unchanged name is a peer that
            // never received the table, and the answer to that is the table.
            transport.SendRoster(serverNames);
        }

        /// <summary>Server side: forgets anyone no longer connected. True when something went.</summary>
        private bool DropDeparted()
        {
            // ConnectedClientsIds is server-only, which is exactly where this runs.
            IReadOnlyList<ulong> connected = NetworkManager.Singleton.ConnectedClientsIds;

            departed.Clear();
            foreach (ulong id in serverNames.Keys)
            {
                if (!Contains(connected, id)) departed.Add(id);
            }

            foreach (ulong id in departed) serverNames.Remove(id);
            return departed.Count > 0;
        }

        private void OnRosterReceived(IReadOnlyDictionary<ulong, VoiceRoster.Entry> table)
        {
            names.Clear();
            foreach (KeyValuePair<ulong, VoiceRoster.Entry> entry in table) names[entry.Key] = entry.Value;
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

            // Self-muted closes the device outright rather than merely not sending: "mute" should
            // mean the microphone is off, operating-system indicator and all. retryIn is cleared so
            // unmuting reopens it at once.
            if (GameSettings.VoiceSelfMuted)
            {
                StopCapture();
                retryIn = 0f;
                return;
            }

            // Alone — which is what singleplayer is, a host of one — there is nobody to hear, and a
            // microphone open for no listener is a recording light for nothing. retryIn is cleared
            // so capture starts the moment someone joins rather than up to a retry gap later.
            if (IsAlone())
            {
                StopCapture();
                retryIn = 0f;
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

        private static bool IsAlone() =>
            Network.Server && NetworkManager.Singleton.ConnectedClientsIds.Count <= 1;

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
            // Push-to-talk closes the channel between presses. Within it the gate still decides, as
            // a plain noise gate, so holding the key over silence sends nothing.
            if (GameSettings.VoicePushToTalk && !keys.TalkOpen)
            {
                gate.Reset();
                return;
            }

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
            // Positions are this frame's, refreshed in Update. A frame handled before the first
            // Update of a new session sees none and routes to everyone, which costs one frame.
            VoiceRouter.Proximity(NetworkManager.Singleton.ConnectedClientsIds, sender, bodies,
                                  VoiceTuning.Active.RouteDistance, listeners);
            transport.SendToListeners(listeners, sender, frame, length);
        }

        /// <summary>
        /// Every spawned player's position this frame, host and clients alike: the host routes by
        /// it and every peer places the voices it hears by it. Players who have not spawned — the
        /// lobby, or anyone still loading in — are simply absent, which the router reads as "no
        /// body, hears and is heard by everyone".
        /// </summary>
        private void RefreshBodies()
        {
            bodies.Clear();

            IReadOnlyList<PlayerIdentity> players = PlayerIdentity.All;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerIdentity player = players[i];
                if (player == null || !player.IsSpawned) continue;

                bodies[player.OwnerClientId] = player.transform.position;
            }
        }

        // ------------------------------------------------------------------ listening

        private void OnFrameToPlay(ulong speaker, byte[] frame, int length)
        {
            // Never your own voice. On a host the relay loops back through the same handler, and
            // hearing yourself a round trip later is the most disorienting thing voice can do.
            if (speaker == Network.LocalClientId) return;

            // Muted, or turned all the way down: not decoded at all. That saves the work, and it is
            // what keeps them out of every speaking indicator — see IsSpeaking.
            if (VoicePeerLevels.EffectiveGain(AccountOf(speaker), speaker) <= 0f) return;

            // Beyond earshot: the host sends a little past the edge (see VoiceTuning.routeMargin),
            // and what lands out there would play at zero volume anyway. Dropping it here keeps
            // "speaking" in the indicators meaning "you can hear them".
            if (IsOutOfEarshot(speaker)) return;

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
            ApplyVolume(speaker, voice);
            PlaceVoice(speaker, voice);

            heard[speaker] = voice;
            return voice;
        }

        private void DriveListeners(float delta)
        {
            finished.Clear();

            foreach (KeyValuePair<ulong, Heard> entry in heard)
            {
                Heard voice = entry.Value;

                voice.SilentFor += delta;
                voice.Playback.Tick(delta);

                ApplyVolume(entry.Key, voice);
                PlaceVoice(entry.Key, voice);
                if (voice.SilentFor >= ReapSeconds) finished.Add(entry.Key);
            }

            foreach (ulong id in finished) Release(id);
        }

        /// <summary>
        /// Your overall Voice volume times your level for this one person — pushed to FMOD only on
        /// a change. Checked every frame, because both halves can move at any time: the settings
        /// slider, or a row in the Players tab.
        /// </summary>
        private static void ApplyVolume(ulong speaker, Heard voice)
        {
            float target = GameSettings.VoiceVolume *
                           VoicePeerLevels.EffectiveGain(AccountOf(speaker), speaker);

            if (Mathf.Approximately(voice.AppliedVolume, target)) return;

            voice.AppliedVolume = target;
            voice.Playback.SetVolume(target);
        }

        /// <summary>
        /// A speaker with a body is heard from their mouth, fading with distance; one without — the
        /// lobby, or someone between bodies — is heard flat, as before proximity. Every frame,
        /// because they move; VoicePlayback only passes on what changed.
        /// </summary>
        private void PlaceVoice(ulong speaker, Heard voice)
        {
            VoiceTuning tuning = VoiceTuning.Active;

            if (!bodies.TryGetValue(speaker, out Vector3 body))
            {
                voice.Playback.SetSpatial(false, 0f, 0f);
                return;
            }

            voice.Playback.SetSpatial(true, tuning.FullVolumeDistance, tuning.SilentDistance);
            voice.Playback.SetPosition(body + Vector3.up * tuning.MouthHeight);
        }

        /// <summary>
        /// Both of you have bodies and they are farther apart than a voice carries. Measured body to
        /// body, like the host's routing, rather than from the camera FMOD fades by — the camera
        /// sits inside the helmet, so the two agree to within a head.
        /// </summary>
        private bool IsOutOfEarshot(ulong speaker)
        {
            if (!bodies.TryGetValue(speaker, out Vector3 mouth)) return false;
            if (!bodies.TryGetValue(Network.LocalClientId, out Vector3 ear)) return false;

            float reach = VoiceTuning.Active.SilentDistance;
            return (mouth - ear).sqrMagnitude > reach * reach;
        }

        private void Release(ulong speaker)
        {
            if (!heard.TryGetValue(speaker, out Heard voice)) return;

            voice.Playback?.Dispose();
            voice.Decoder?.Dispose();
            heard.Remove(speaker);
        }

        // ------------------------------------------------------------------ teardown

        private void Stop()
        {
            StopCapture();

            foreach (Heard voice in heard.Values)
            {
                voice.Playback?.Dispose();
                voice.Decoder?.Dispose();
            }

            heard.Clear();
            bodies.Clear();
            names.Clear();
            serverNames.Clear();

            encoder?.Dispose();
            encoder = null;

            transport.Unregister();
            VoicePeerLevels.ForgetSession();
            keys.ResetLatch();
            retryIn = 0f;
            nameResendIn = 0f;
            rosterSweepIn = 0f;
        }

        private static bool Contains(IReadOnlyList<ulong> ids, ulong id)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id) return true;
            }

            return false;
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
