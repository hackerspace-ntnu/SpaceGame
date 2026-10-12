using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.Core
{
    /// <summary>
    /// Every player-facing option in one place, persisted to PlayerPrefs.
    /// <para>
    /// Static rather than a MonoBehaviour because the things that consume these values are spread
    /// across scenes and spawn at different times — the FMOD buses come up in Bootstrap, the camera
    /// and look sensitivity arrive with a networked player object, and the pause menu that edits
    /// them is created on demand. A static store with a <see cref="Changed"/> event lets each of
    /// those subscribe whenever it happens to exist, instead of every one of them needing a
    /// serialized reference to a settings object that may not be loaded yet.
    /// </para>
    /// <para>
    /// Values are read lazily: the first property access loads from PlayerPrefs, so this behaves
    /// the same in an EditMode test as it does in a build, with no initialisation order to get
    /// right.
    /// </para>
    /// </summary>
    public static class GameSettings
    {
        private const string Prefix = "SpaceGame.Settings.";

        // Bumped when a default changes in a way that should override what players already have
        // stored. Everything is re-seeded from defaults when the stored version is older.
        private const int SchemaVersion = 2;

        /// <summary>
        /// Screen shake is the most over-applied juice technique there is, and the crash landing
        /// arrival shakes for the better part of half a minute with no way to skip it — which makes
        /// this a motion-sickness and vestibular-accessibility control, not a polish dial. Zero is a
        /// supported value and must stay one: "nearly off" still makes a susceptible player ill.
        /// </summary>
        public const float MinCameraShake = 0f;
        public const float MaxCameraShake = 1f;

        public const float MinSensitivity = 0.1f;
        public const float MaxSensitivity = 5f;
        public const float MinFieldOfView = 50f;
        public const float MaxFieldOfView = 110f;
        public const int MaxNameLength = 20;

        /// <summary>
        /// Bounds for the voice gate, as peak amplitude rather than decibels — the same scale
        /// <see cref="Voice.VoiceCapture.InputLevel"/> reports, so the slider and the input meter
        /// on the audio page are directly comparable. The floor is deliberately above zero: at
        /// exactly zero the gate never closes and a player with an open mic transmits their room.
        /// </summary>
        public const float MinVoiceGateThreshold = 0.005f;
        public const float MaxVoiceGateThreshold = 0.3f;

        /// <summary>
        /// Unbound: push-to-talk is off by default and not in use yet, so it ships without a key
        /// rather than claiming one. When it wants a default, V is the conventional choice and is
        /// free in this project's bindings — T is chat, and the rest of the left hand is already
        /// movement and gear.
        /// </summary>
        public const string DefaultPushToTalkBinding = "";

        /// <summary>Frame cap choices offered by the video page. 0 means uncapped.</summary>
        public static readonly int[] FrameRateCaps = { 0, 30, 60, 90, 120, 144, 165, 240 };

        /// <summary>Raised after any value changes. Consumers re-read what they care about.</summary>
        public static event Action Changed;

        private static bool loaded;

        private static string playerName;
        private static int suitColorIndex;
        private static float masterVolume;
        private static float musicVolume;
        private static float sfxVolume;
        private static float uiVolume;
        private static float ambienceVolume;
        private static float voiceVolume;
        private static bool voicePushToTalk;
        private static float voiceGateThreshold;
        private static string voiceInputDevice;
        private static bool voiceSelfMuted;
        private static bool voicePushToTalkToggle;
        private static string voicePushToTalkBinding;
        private static string voiceMuteBinding;
        private static float mouseSensitivity;
        private static float cameraShakeIntensity;
        private static bool invertLookY;
        private static bool invertHotbarScroll;
        private static bool devMode;
        private static float fieldOfView;
        private static int qualityLevel;
        private static bool fullscreen;
        private static int resolutionIndex;
        private static bool vSync;
        private static int frameRateCap;
        private static int visorDetail;
        private static bool reduceVisorMotion;

        private static Resolution[] resolutionChoices;

        // ------------------------------------------------------------------ profile

        /// <summary>Name this player is shown as to everyone else in the session.</summary>
        public static string PlayerName
        {
            get { EnsureLoaded(); return playerName; }
            set
            {
                EnsureLoaded();
                string sanitised = SanitiseName(value);
                if (sanitised == playerName) return;
                playerName = sanitised;
                PlayerPrefs.SetString(Prefix + "PlayerName", playerName);
                Raise();
            }
        }

        /// <summary>
        /// The suit colour this player wears, as an index into <c>SuitPalette.Swatches</c>.
        /// <para>
        /// Stored rather than picked per session, because it is meant to be recognisably yours:
        /// friends learn that you are the green one. Seeded once with a random vivid swatch — see
        /// <see cref="EnsureLoaded"/> for why that beats defaulting everyone to the same colour.
        /// </para>
        /// </summary>
        public static int SuitColorIndex
        {
            get { EnsureLoaded(); return suitColorIndex; }
            set
            {
                EnsureLoaded();

                // Clamped against the palette rather than trusted: this value also arrives from
                // PlayerPrefs written by an older build, where the list may have been longer.
                int clamped = SuitPalette.Clamp(value);
                if (clamped == suitColorIndex) return;

                suitColorIndex = clamped;
                PlayerPrefs.SetInt(Prefix + "SuitColorIndex", suitColorIndex);
                Raise();
            }
        }

        // -------------------------------------------------------------------- audio

        public static float MasterVolume
        {
            get { EnsureLoaded(); return masterVolume; }
            set => SetFloat(ref masterVolume, value, 0f, 1f, "MasterVolume");
        }

        public static float MusicVolume
        {
            get { EnsureLoaded(); return musicVolume; }
            set => SetFloat(ref musicVolume, value, 0f, 1f, "MusicVolume");
        }

        public static float SfxVolume
        {
            get { EnsureLoaded(); return sfxVolume; }
            set => SetFloat(ref sfxVolume, value, 0f, 1f, "SfxVolume");
        }

        public static float UiVolume
        {
            get { EnsureLoaded(); return uiVolume; }
            set => SetFloat(ref uiVolume, value, 0f, 1f, "UiVolume");
        }

        /// <summary>Drives the reverb bus — the wet ambience of the space you are standing in.</summary>
        public static float AmbienceVolume
        {
            get { EnsureLoaded(); return ambienceVolume; }
            set => SetFloat(ref ambienceVolume, value, 0f, 1f, "AmbienceVolume");
        }

        // -------------------------------------------------------------------- voice

        /// <summary>
        /// How loud other players' voices are. Separate from <see cref="SfxVolume"/> because voice
        /// is the one sound a player cannot afford to lose under the mix — turning the world down
        /// to hear a teammate should not be the only option.
        /// </summary>
        public static float VoiceVolume
        {
            get { EnsureLoaded(); return voiceVolume; }
            set => SetFloat(ref voiceVolume, value, 0f, 1f, "VoiceVolume");
        }

        /// <summary>
        /// Transmit only while the push-to-talk channel is open — the key held, or latched on in
        /// toggle mode (<see cref="VoicePushToTalkToggle"/>) — instead of whenever the gate opens.
        /// <para>
        /// Off by default: proximity chat is meant to feel like talking to the person next to you,
        /// and a key you have to hold undoes that. It is the answer for a player on speakers rather
        /// than headphones, whose microphone would otherwise echo the whole session back.
        /// </para>
        /// </summary>
        public static bool VoicePushToTalk
        {
            get { EnsureLoaded(); return voicePushToTalk; }
            set => SetBool(ref voicePushToTalk, value, "VoicePushToTalk");
        }

        /// <summary>
        /// Peak level the microphone has to reach before the gate opens, on the same 0-1 scale as
        /// the input meter. Applies in push-to-talk too, where it is a plain noise gate.
        /// </summary>
        public static float VoiceGateThreshold
        {
            get { EnsureLoaded(); return voiceGateThreshold; }
            set => SetFloat(ref voiceGateThreshold, value, MinVoiceGateThreshold,
                            MaxVoiceGateThreshold, "VoiceGateThreshold");
        }

        /// <summary>
        /// Which microphone to record from, by NAME. Empty means the system default.
        /// <para>
        /// Stored by name rather than by index because device indices are reassigned whenever
        /// anything is plugged in or removed — a stored index quietly starts recording from a
        /// different microphone than the one the player chose.
        /// </para>
        /// </summary>
        public static string VoiceInputDevice
        {
            get { EnsureLoaded(); return voiceInputDevice; }
            set => SetString(ref voiceInputDevice, value, "VoiceInputDevice");
        }

        /// <summary>
        /// The player has muted their own microphone. Persisted, because someone who muted
        /// themselves expects to still be muted when they come back, not to rejoin live.
        /// </summary>
        public static bool VoiceSelfMuted
        {
            get { EnsureLoaded(); return voiceSelfMuted; }
            set => SetBool(ref voiceSelfMuted, value, "VoiceSelfMuted");
        }

        /// <summary>
        /// Push-to-talk as a toggle — each press flips the channel — rather than hold-to-talk.
        /// Hold is the default because it cannot be left on by accident.
        /// </summary>
        public static bool VoicePushToTalkToggle
        {
            get { EnsureLoaded(); return voicePushToTalkToggle; }
            set => SetBool(ref voicePushToTalkToggle, value, "VoicePushToTalkToggle");
        }

        /// <summary>Input System path of the push-to-talk key, e.g. <c>&lt;Keyboard&gt;/v</c>.</summary>
        public static string VoicePushToTalkBinding
        {
            get { EnsureLoaded(); return voicePushToTalkBinding; }
            set => SetString(ref voicePushToTalkBinding, value, "VoicePushToTalkBinding");
        }

        /// <summary>
        /// Input System path of the key that toggles <see cref="VoiceSelfMuted"/>. Empty — unbound —
        /// by default: it is an extra for players who want one, and an unexpected key that silences
        /// your microphone is worse than no key at all.
        /// </summary>
        public static string VoiceMuteBinding
        {
            get { EnsureLoaded(); return voiceMuteBinding; }
            set => SetString(ref voiceMuteBinding, value, "VoiceMuteBinding");
        }

        // ----------------------------------------------------------------- controls

        /// <summary>Multiplier on the look sensitivity authored on the player prefab.</summary>
        public static float MouseSensitivity
        {
            get { EnsureLoaded(); return mouseSensitivity; }
            set => SetFloat(ref mouseSensitivity, value, MinSensitivity, MaxSensitivity, "MouseSensitivity");
        }

        public static bool InvertLookY
        {
            get { EnsureLoaded(); return invertLookY; }
            set => SetBool(ref invertLookY, value, "InvertLookY");
        }

        /// <summary>
        /// How hard camera shake hits, from 0 (off) to 1 (full). A multiplier on every shake in the
        /// game rather than an arrival-specific dial — see <see cref="MinCameraShake"/> for why it
        /// has to reach a true zero.
        /// </summary>
        public static float CameraShakeIntensity
        {
            get { EnsureLoaded(); return cameraShakeIntensity; }
            set => SetFloat(ref cameraShakeIntensity, value, MinCameraShake, MaxCameraShake,
                            "CameraShakeIntensity");
        }

        public static bool InvertHotbarScroll
        {
            get { EnsureLoaded(); return invertHotbarScroll; }
            set => SetBool(ref invertHotbarScroll, value, "InvertHotbarScroll");
        }

        // ---------------------------------------------------------------- visor

        /// <summary>Full visor: the vitals and the world annotations both drawn.</summary>
        public const int VisorDetailFull = 0;

        /// <summary>Vitals only — the readouts you play by. Markers and commentary go away.</summary>
        public const int VisorDetailVitals = 1;

        /// <summary>Nothing. The screenshot state.</summary>
        public const int VisorDetailOff = 2;

        /// <summary>
        /// How much of the helmet visor is drawn, cycled by H.
        /// <para>
        /// Three states rather than two because health lives on the visor now: a plain on/off
        /// toggle would let the player hide their own health bar, which the old two-state toggle
        /// deliberately never could.
        /// </para>
        /// </summary>
        public static int VisorDetail
        {
            get { EnsureLoaded(); return visorDetail; }
            set => SetInt(ref visorDetail, Mathf.Clamp(value, VisorDetailFull, VisorDetailOff), "VisorDetail");
        }

        /// <summary>
        /// Switches off the visor's idle motion — the sway, the boot rise and the bloom.
        /// <para>
        /// A vestibular-accessibility control in the same family as <see cref="CameraShakeIntensity"/>,
        /// not a polish dial. Off by default, because the motion is the thing that makes the layer
        /// read as projected rather than pasted on.
        /// </para>
        /// </summary>
        public static bool ReduceVisorMotion
        {
            get { EnsureLoaded(); return reduceVisorMotion; }
            set => SetBool(ref reduceVisorMotion, value, "ReduceVisorMotion");
        }

        // ---------------------------------------------------------------- developer

        /// <summary>
        /// Unlocks the in-game developer tools — currently the artifact browser on O.
        /// <para>
        /// Persisted like any other preference so it survives a restart, and off by default so a
        /// player who never opens the dev page cannot open the browser by pressing O.
        /// </para>
        /// </summary>
        public static bool DevMode
        {
            get { EnsureLoaded(); return devMode; }
            set => SetBool(ref devMode, value, "DevMode");
        }

        // -------------------------------------------------------------------- video

        public static float FieldOfView
        {
            get { EnsureLoaded(); return fieldOfView; }
            set => SetFloat(ref fieldOfView, value, MinFieldOfView, MaxFieldOfView, "FieldOfView");
        }

        public static int QualityLevel
        {
            get { EnsureLoaded(); return qualityLevel; }
            set
            {
                EnsureLoaded();
                int clamped = Mathf.Clamp(value, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
                if (clamped == qualityLevel) return;
                qualityLevel = clamped;
                PlayerPrefs.SetInt(Prefix + "QualityLevel", qualityLevel);
                QualitySettings.SetQualityLevel(qualityLevel, true);
                Raise();
            }
        }

        public static bool Fullscreen
        {
            get { EnsureLoaded(); return fullscreen; }
            set
            {
                EnsureLoaded();
                if (value == fullscreen) return;
                fullscreen = value;
                PlayerPrefs.SetInt(Prefix + "Fullscreen", fullscreen ? 1 : 0);
                ApplyScreen();
                Raise();
            }
        }

        public static int ResolutionIndex
        {
            get { EnsureLoaded(); return resolutionIndex; }
            set
            {
                EnsureLoaded();
                int clamped = Mathf.Clamp(value, 0, Mathf.Max(0, ResolutionChoices.Length - 1));
                if (clamped == resolutionIndex) return;
                resolutionIndex = clamped;
                PlayerPrefs.SetInt(Prefix + "ResolutionIndex", resolutionIndex);
                ApplyScreen();
                Raise();
            }
        }

        public static bool VSync
        {
            get { EnsureLoaded(); return vSync; }
            set
            {
                EnsureLoaded();
                if (value == vSync) return;
                vSync = value;
                PlayerPrefs.SetInt(Prefix + "VSync", vSync ? 1 : 0);
                ApplyFrameRate();
                Raise();
            }
        }

        /// <summary>Frames per second ceiling, or 0 for uncapped. Ignored while VSync is on.</summary>
        public static int FrameRateCap
        {
            get { EnsureLoaded(); return frameRateCap; }
            set
            {
                EnsureLoaded();
                if (value == frameRateCap) return;
                frameRateCap = Mathf.Max(0, value);
                PlayerPrefs.SetInt(Prefix + "FrameRateCap", frameRateCap);
                ApplyFrameRate();
                Raise();
            }
        }

        /// <summary>
        /// Distinct width x height modes the display supports, largest last. Refresh rates are
        /// collapsed because a resolution list that repeats "1920 x 1080" five times is unusable;
        /// the highest rate for the chosen size is the one applied.
        /// </summary>
        public static Resolution[] ResolutionChoices
        {
            get
            {
                if (resolutionChoices != null) return resolutionChoices;

                var best = new Dictionary<int, Resolution>();
                foreach (Resolution mode in Screen.resolutions)
                {
                    int key = mode.width * 100000 + mode.height;
                    if (!best.TryGetValue(key, out Resolution existing) ||
                        RefreshHz(mode) > RefreshHz(existing))
                    {
                        best[key] = mode;
                    }
                }

                // A headless or virtual display can report nothing at all; the current window is
                // always a valid choice, so the list is never empty and the UI never has to
                // special-case a zero-length array.
                if (best.Count == 0)
                {
                    resolutionChoices = new[] { CurrentWindowResolution() };
                    return resolutionChoices;
                }

                var list = new List<Resolution>(best.Values);
                list.Sort((a, b) => a.width != b.width
                    ? a.width.CompareTo(b.width)
                    : a.height.CompareTo(b.height));

                resolutionChoices = list.ToArray();
                return resolutionChoices;
            }
        }

        // ------------------------------------------------------------------ seeding

        /// <summary>
        /// Adopts a value authored in the scene or on a prefab as the default, but only while the
        /// player has never touched the setting. Lets the camera's own field of view be the
        /// starting point of the slider rather than a number picked here, without a later launch
        /// overwriting whatever the player then chose.
        /// </summary>
        public static void SeedFieldOfView(float authored)
        {
            EnsureLoaded();
            if (PlayerPrefs.HasKey(Prefix + "FieldOfView")) return;
            fieldOfView = Mathf.Clamp(authored, MinFieldOfView, MaxFieldOfView);
        }

        /// <inheritdoc cref="SeedFieldOfView"/>
        public static void SeedInvertHotbarScroll(bool authored)
        {
            EnsureLoaded();
            if (PlayerPrefs.HasKey(Prefix + "InvertHotbarScroll")) return;
            invertHotbarScroll = authored;
        }

        // ------------------------------------------------------------------ lifetime

        /// <summary>
        /// Pushes the settings that live on engine-wide state (quality, window, frame pacing) into
        /// the engine. Called once at startup; the individual setters keep it current after that.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void ApplyEngineSettings()
        {
            EnsureLoaded();

            if (QualitySettings.names.Length > 0)
                QualitySettings.SetQualityLevel(Mathf.Clamp(qualityLevel, 0, QualitySettings.names.Length - 1), true);

            ApplyFrameRate();

            // Deliberately not applying the window mode in the editor: the Game view is not a
            // window the player sized, and forcing it fullscreen on entering play mode hides the
            // rest of the editor every single run.
            if (!Application.isEditor)
                ApplyScreen();

            Raise();
        }

        public static void Save() => PlayerPrefs.Save();

        public static void ResetToDefaults()
        {
            foreach (string key in new[]
            {
                "PlayerName", "SuitColorIndex", "MasterVolume", "MusicVolume", "SfxVolume", "UiVolume", "AmbienceVolume",
                "MouseSensitivity", "InvertLookY", "InvertHotbarScroll", "DevMode", "FieldOfView",
                "QualityLevel", "Fullscreen", "ResolutionIndex", "VSync", "FrameRateCap",
                "VoiceVolume", "VoicePushToTalk", "VoiceGateThreshold", "VoiceInputDevice",
                "VoiceSelfMuted", "VoicePushToTalkToggle", "VoicePushToTalkBinding", "VoiceMuteBinding",
                "VisorDetail", "ReduceVisorMotion", "Version",
            })
            {
                PlayerPrefs.DeleteKey(Prefix + key);
            }

            loaded = false;
            EnsureLoaded();
            ApplyEngineSettings();
        }

        // ------------------------------------------------------------------ internals

        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true; // set first: the seeding below reads PlayerPrefs, not these properties

            if (PlayerPrefs.GetInt(Prefix + "Version", 0) < SchemaVersion)
            {
                PlayerPrefs.SetInt(Prefix + "Version", SchemaVersion);
            }

            playerName = PlayerPrefs.GetString(Prefix + "PlayerName", string.Empty);
            if (string.IsNullOrWhiteSpace(playerName))
            {
                // Generated once and kept, so a player who never opens the menu is still the same
                // person to everyone else across sessions.
                playerName = $"Pilot-{UnityEngine.Random.Range(1000, 10000)}";
                PlayerPrefs.SetString(Prefix + "PlayerName", playerName);
            }

            // Random, and written down the moment it is drawn, for the same reason as the name
            // above. Defaulting everyone to swatch 0 would mean a lobby of four people who have
            // never opened the cycler is four identical astronauts — and so is the game they then
            // play together, where telling each other apart matters more than in the menu.
            if (PlayerPrefs.HasKey(Prefix + "SuitColorIndex"))
            {
                suitColorIndex = SuitPalette.Clamp(PlayerPrefs.GetInt(Prefix + "SuitColorIndex"));
            }
            else
            {
                suitColorIndex = SuitPalette.RandomDefault();
                PlayerPrefs.SetInt(Prefix + "SuitColorIndex", suitColorIndex);
            }

            masterVolume = PlayerPrefs.GetFloat(Prefix + "MasterVolume", 1f);
            musicVolume = PlayerPrefs.GetFloat(Prefix + "MusicVolume", 0.7f);
            sfxVolume = PlayerPrefs.GetFloat(Prefix + "SfxVolume", 1f);
            uiVolume = PlayerPrefs.GetFloat(Prefix + "UiVolume", 0.85f);
            ambienceVolume = PlayerPrefs.GetFloat(Prefix + "AmbienceVolume", 1f);
            voiceVolume = PlayerPrefs.GetFloat(Prefix + "VoiceVolume", 1f);
            voicePushToTalk = PlayerPrefs.GetInt(Prefix + "VoicePushToTalk", 0) == 1;
            voiceGateThreshold = PlayerPrefs.GetFloat(Prefix + "VoiceGateThreshold", 0.03f);
            voiceInputDevice = PlayerPrefs.GetString(Prefix + "VoiceInputDevice", string.Empty);
            voiceSelfMuted = PlayerPrefs.GetInt(Prefix + "VoiceSelfMuted", 0) == 1;
            voicePushToTalkToggle = PlayerPrefs.GetInt(Prefix + "VoicePushToTalkToggle", 0) == 1;
            voicePushToTalkBinding = PlayerPrefs.GetString(Prefix + "VoicePushToTalkBinding",
                                                           DefaultPushToTalkBinding);
            voiceMuteBinding = PlayerPrefs.GetString(Prefix + "VoiceMuteBinding", string.Empty);

            mouseSensitivity = PlayerPrefs.GetFloat(Prefix + "MouseSensitivity", 1f);
            invertLookY = PlayerPrefs.GetInt(Prefix + "InvertLookY", 0) == 1;

            // Defaults to full, so nobody loses an effect they already had; only a
            // player who deliberately turns it down gets less.
            cameraShakeIntensity = PlayerPrefs.GetFloat(Prefix + "CameraShakeIntensity", 1f);
            invertHotbarScroll = PlayerPrefs.GetInt(Prefix + "InvertHotbarScroll", 0) == 1;
            devMode = PlayerPrefs.GetInt(Prefix + "DevMode", 0) == 1;
            fieldOfView = PlayerPrefs.GetFloat(Prefix + "FieldOfView", 60f);

            qualityLevel = PlayerPrefs.GetInt(Prefix + "QualityLevel", QualitySettings.GetQualityLevel());
            fullscreen = PlayerPrefs.GetInt(Prefix + "Fullscreen", Screen.fullScreen ? 1 : 0) == 1;
            resolutionIndex = PlayerPrefs.GetInt(Prefix + "ResolutionIndex", DefaultResolutionIndex());
            vSync = PlayerPrefs.GetInt(Prefix + "VSync", QualitySettings.vSyncCount > 0 ? 1 : 0) == 1;
            frameRateCap = PlayerPrefs.GetInt(Prefix + "FrameRateCap", 0);
            visorDetail = PlayerPrefs.GetInt(Prefix + "VisorDetail", VisorDetailFull);
            reduceVisorMotion = PlayerPrefs.GetInt(Prefix + "ReduceVisorMotion", 0) == 1;

            masterVolume = Mathf.Clamp01(masterVolume);
            musicVolume = Mathf.Clamp01(musicVolume);
            sfxVolume = Mathf.Clamp01(sfxVolume);
            uiVolume = Mathf.Clamp01(uiVolume);
            ambienceVolume = Mathf.Clamp01(ambienceVolume);
            voiceVolume = Mathf.Clamp01(voiceVolume);
            voiceGateThreshold = Mathf.Clamp(voiceGateThreshold, MinVoiceGateThreshold,
                                             MaxVoiceGateThreshold);
            mouseSensitivity = Mathf.Clamp(mouseSensitivity, MinSensitivity, MaxSensitivity);
            cameraShakeIntensity = Mathf.Clamp(cameraShakeIntensity, MinCameraShake, MaxCameraShake);
            fieldOfView = Mathf.Clamp(fieldOfView, MinFieldOfView, MaxFieldOfView);
            resolutionIndex = Mathf.Clamp(resolutionIndex, 0, Mathf.Max(0, ResolutionChoices.Length - 1));
        }

        private static void SetFloat(ref float field, float value, float min, float max, string key)
        {
            EnsureLoaded();
            float clamped = Mathf.Clamp(value, min, max);
            if (Mathf.Approximately(clamped, field)) return;

            field = clamped;
            PlayerPrefs.SetFloat(Prefix + key, clamped);
            Raise();
        }

        private static void SetBool(ref bool field, bool value, string key)
        {
            EnsureLoaded();
            if (value == field) return;

            field = value;
            PlayerPrefs.SetInt(Prefix + key, value ? 1 : 0);
            Raise();
        }

        private static void SetString(ref string field, string value, string key)
        {
            EnsureLoaded();
            string text = value ?? string.Empty;
            if (text == field) return;

            field = text;
            PlayerPrefs.SetString(Prefix + key, text);
            Raise();
        }

        private static void SetInt(ref int field, int value, string key)
        {
            EnsureLoaded();
            if (value == field) return;

            field = value;
            PlayerPrefs.SetInt(Prefix + key, value);
            Raise();
        }

        private static void Raise() => Changed?.Invoke();

        /// <summary>
        /// Trims and length-limits a typed name, and never returns empty — an empty name would
        /// leave a blank row in everyone else's player list.
        /// </summary>
        public static string SanitiseName(string raw)
        {
            string trimmed = (raw ?? string.Empty).Trim();
            if (trimmed.Length > MaxNameLength)
                trimmed = trimmed[..MaxNameLength];

            return string.IsNullOrWhiteSpace(trimmed) ? "Pilot" : trimmed;
        }

        private static void ApplyScreen()
        {
            Resolution[] choices = ResolutionChoices;
            if (choices.Length == 0) return;

            Resolution target = choices[Mathf.Clamp(resolutionIndex, 0, choices.Length - 1)];
            FullScreenMode mode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;

#if UNITY_2022_2_OR_NEWER
            Screen.SetResolution(target.width, target.height, mode, target.refreshRateRatio);
#else
            Screen.SetResolution(target.width, target.height, mode, target.refreshRate);
#endif
        }

        private static void ApplyFrameRate()
        {
            QualitySettings.vSyncCount = vSync ? 1 : 0;

            // A cap alongside vsync fights the swap interval — vsync already paces the frame, so
            // the cap is only handed to the engine when vsync is off.
            Application.targetFrameRate = vSync || frameRateCap <= 0 ? -1 : frameRateCap;
        }

        private static int DefaultResolutionIndex()
        {
            Resolution[] choices = ResolutionChoices;
            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i].width == Screen.width && choices[i].height == Screen.height)
                    return i;
            }

            return Mathf.Max(0, choices.Length - 1);
        }

        private static Resolution CurrentWindowResolution()
        {
            var fallback = new Resolution { width = Mathf.Max(640, Screen.width), height = Mathf.Max(480, Screen.height) };
            return fallback;
        }

        private static double RefreshHz(Resolution mode)
        {
#if UNITY_2022_2_OR_NEWER
            return mode.refreshRateRatio.value;
#else
            return mode.refreshRate;
#endif
        }

        /// <summary>"1920 x 1080" for the video page's resolution cycler.</summary>
        public static string DescribeResolution(int index)
        {
            Resolution[] choices = ResolutionChoices;
            if (choices.Length == 0) return "—";

            Resolution mode = choices[Mathf.Clamp(index, 0, choices.Length - 1)];
            return $"{mode.width} x {mode.height}";
        }

        /// <summary>"Uncapped" or "144 FPS" for the frame-cap cycler.</summary>
        public static string DescribeFrameRateCap(int cap) => cap <= 0 ? "Uncapped" : $"{cap} FPS";
    }
}
