using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance;

        [Header("Volume Settings")]
        [Tooltip("Editor-only preview. At runtime the levels come from the pause menu's audio page " +
                 "(GameSettings), so these are overwritten as soon as play starts.")]
        [SerializeField, Range(0f, 1f)] private float musicVolume =  1f;
        [SerializeField, Range(0f, 1f)] private float sfxVolume =    1f;
        [SerializeField, Range(0f, 1f)] private float reverbVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float uiVolume =     1f;

        public float SfxVolume { get => sfxVolume; set => sfxVolume = value; }
        public float MusicVolume { get => musicVolume; set => musicVolume = value; }
        public float ReverbVolume { get => reverbVolume; set => reverbVolume = value; }
        public float UIVolume { get => uiVolume; set => uiVolume = value; }


        [Header("Audio Busses")]
        private Bus master;
        private Bus music;
        private Bus sfx;
        private Bus ui;
        private Bus reverb;

        private bool bussesResolved;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            DontDestroyOnLoad(gameObject);

            Instance = this;

            ResolveBusses();
            ApplySettings();
        }

        private void OnEnable() => GameSettings.Changed += ApplySettings;

        private void OnDisable() => GameSettings.Changed -= ApplySettings;

        /// <summary>
        /// Looks up the FMOD busses, tolerating the banks not being loaded yet.
        /// <para>
        /// GetBus throws when the bank holding the bus has not finished loading, which is a real
        /// ordering possibility — this component comes up in Bootstrap alongside the bank loader.
        /// Letting it throw took the rest of Awake with it, so the volumes were never applied at
        /// all. Failing softly and retrying on the next settings change costs nothing and cannot
        /// silence the game.
        /// </para>
        /// </summary>
        private void ResolveBusses()
        {
            if (bussesResolved) return;

            try
            {
                master = RuntimeManager.GetBus("bus:/");
                music = RuntimeManager.GetBus("bus:/Music");
                sfx = RuntimeManager.GetBus("bus:/SFX");
                ui = RuntimeManager.GetBus("bus:/UI");
                reverb = RuntimeManager.GetBus("bus:/Reverb");
                bussesResolved = true;
            }
            catch (BusNotFoundException)
            {
                bussesResolved = false;
            }
            catch (SystemNotInitializedException)
            {
                // Studio itself is not up yet — same story, try again next time.
                bussesResolved = false;
            }
        }

        /// <summary>Pushes the player's chosen levels onto the busses. Safe to call at any time.</summary>
        public void ApplySettings()
        {
            musicVolume = GameSettings.MusicVolume;
            sfxVolume = GameSettings.SfxVolume;
            uiVolume = GameSettings.UiVolume;
            reverbVolume = GameSettings.AmbienceVolume;

            ResolveBusses();
            if (!bussesResolved) return;

            master.setVolume(GameSettings.MasterVolume);
            music.setVolume(musicVolume);
            sfx.setVolume(sfxVolume);
            ui.setVolume(uiVolume);
            reverb.setVolume(reverbVolume);
        }

        public void PlayTestMusic()
        {
            RuntimeManager.PlayOneShot("event:/Music/TestSong");
        } //Todo: Make more flexible

        public void PlayEvent(EventReference myevent) {
            RuntimeManager.PlayOneShot(myevent);
        }

        public void PlayEvent(EventReference myevent, Vector3 position) {
            RuntimeManager.PlayOneShot(myevent, position);
        }

        public void PlaySFX(string sound)
        {
            RuntimeManager.PlayOneShot(sound);
        } //Todo: Find easy way to hear and assign sound effects:

        public void PlaySFX3d(string sound, Vector3 worldPos)
        {
            RuntimeManager.PlayOneShot(sound, worldPos);
        }

        // Dragging a slider in the inspector during play still previews that level, but it is a
        // preview only — the next change from the settings menu re-asserts the stored value.
        private void OnValidate()
        {
            if (!Application.isPlaying || !bussesResolved) return;

            music.setVolume(musicVolume);
            sfx.setVolume(sfxVolume);
            reverb.setVolume(reverbVolume);
            ui.setVolume(uiVolume);
        }
    }
}
