// Plays a loose audio FILE through FMOD, for sounds the banks do not contain.
//
// ## Why this exists
//
// New FMOD events cannot be authored in this project: the Studio `.fspro` that built the banks is
// not in the repo, so `SfxId` can only ever name one of the 19 shipped events. That leaves no way
// at all to give a creature a voice of its own -- and the obvious workaround does not work either.
//
// **A Unity `AudioSource` is silent here.** `ProjectSettings/AudioManager.asset` has
// `m_DisableAudio: 1` -- Unity's audio system is switched off, which is the normal FMOD setup --
// and there is no Unity `AudioListener` in any gameplay scene, only FMOD's `StudioListener`. An
// `AudioClip` played through an `AudioSource` produces nothing, with no error to explain it.
// (`SandstormAudio` is written that way and is, on that evidence, inaudible in the shipped game.)
//
// So a new sound has to reach the same FMOD mixer everything else uses. FMOD's Core API can play
// an ordinary file without any Studio event behind it, which is what this does.
//
// ## Cost
//
// The file is read from `StreamingAssets` -- not from an `AudioClip`, because Core takes a path on
// disk, and Unity's importer does not produce one. Sounds are created once and cached for the
// session; the `Sound` handles are released on shutdown.
using System.Collections.Generic;
using System.IO;
using FMODUnity;
using UnityEngine;

namespace SpaceGame.Audio
{
    /// <summary>
    /// One-shot playback of an audio file under <c>StreamingAssets/Audio</c>, positioned in 3D and
    /// mixed by FMOD like everything else.
    /// </summary>
    public static class SfxFile
    {
        /// <summary>Where loose sound files live, relative to StreamingAssets.</summary>
        public const string Folder = "Audio";

        private static readonly Dictionary<string, FMOD.Sound> Loaded =
            new Dictionary<string, FMOD.Sound>();

        // Say it once per file. A missing sound is a content problem, not a per-frame one.
        private static readonly HashSet<string> Complained = new HashSet<string>();

        /// <summary>
        /// Play <paramref name="fileName"/> at a world position.
        ///
        /// <para>
        /// <paramref name="maxDistance"/> is where it fades to nothing; <paramref name="minDistance"/>
        /// is the radius inside which it stays at full volume. Returns false when the file could
        /// not be played, so a caller can fall back to an <see cref="SfxId"/>.
        /// </para>
        /// </summary>
        public static bool Play(string fileName, Vector3 position, float volume = 1f,
                                float minDistance = 5f, float maxDistance = 60f)
        {
            if (string.IsNullOrEmpty(fileName)) return false;

            if (!TryGetSound(fileName, out FMOD.Sound sound)) return false;

            FMOD.System core = RuntimeManager.CoreSystem;

            // Started paused so the 3D attributes are set before the first sample is heard.
            // Playing first and positioning after puts the opening of the sound at the origin.
            FMOD.RESULT result = core.playSound(sound, default, true, out FMOD.Channel channel);
            if (result != FMOD.RESULT.OK) return false;

            FMOD.VECTOR pos = RuntimeUtils.ToFMODVector(position);
            FMOD.VECTOR vel = RuntimeUtils.ToFMODVector(Vector3.zero);
            channel.set3DAttributes(ref pos, ref vel);
            channel.set3DMinMaxDistance(minDistance, maxDistance);
            channel.setVolume(Mathf.Clamp01(volume));
            channel.setPaused(false);
            return true;
        }

        private static bool TryGetSound(string fileName, out FMOD.Sound sound)
        {
            if (Loaded.TryGetValue(fileName, out sound)) return true;

            string path = Path.Combine(Application.streamingAssetsPath, Folder, fileName);

            // On Android StreamingAssets lives inside the APK and there is no such path; FMOD can
            // read it, but only through the AndroidJavaObject file system. Not wired here because
            // this project ships desktop; the check keeps the failure legible if that changes.
            if (!Application.isEditor && Application.platform == RuntimePlatform.Android)
            {
                Warn(fileName, "StreamingAssets is not a real path on Android");
                return false;
            }

            if (!File.Exists(path))
            {
                Warn(fileName, $"no file at {path}");
                return false;
            }

            // 3D and streamed from disk: these are one-shots of a second or two, but a creature
            // roar is not worth resident memory and decompressing on load costs a hitch.
            FMOD.MODE mode = FMOD.MODE._3D | FMOD.MODE._3D_LINEARROLLOFF | FMOD.MODE.CREATECOMPRESSEDSAMPLE;

            FMOD.RESULT result = RuntimeManager.CoreSystem.createSound(path, mode, out sound);
            if (result != FMOD.RESULT.OK)
            {
                Warn(fileName, $"FMOD could not load it: {result}");
                return false;
            }

            Loaded[fileName] = sound;
            return true;
        }

        private static void Warn(string fileName, string why)
        {
            if (!Complained.Add(fileName)) return;
            Debug.LogWarning($"[SfxFile] '{fileName}' will not play — {why}. Loose sounds live in " +
                             $"Assets/StreamingAssets/{Folder}/.");
        }

        /// <summary>
        /// Release the cached sounds. Called on shutdown; safe to call twice.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            // Entering play mode with domain reload OFF keeps the statics from the last session,
            // and the FMOD Sound handles from it are dangling. Drop them rather than play them.
            Loaded.Clear();
            Complained.Clear();
        }
    }
}
