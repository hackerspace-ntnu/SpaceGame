// Which microphones FMOD can see.
//
// Capture goes through FMOD rather than Unity's Microphone class on purpose. Unity's audio system
// is switched off project-wide (ProjectSettings/AudioManager.asset, m_DisableAudio: 1) and there is
// no Unity AudioListener in any gameplay scene -- see SfxFile.cs for the same reasoning applied to
// playback. Microphone lives in that same disabled module, so it is not something to rely on here.
//
// A device is remembered by NAME, not index: indices are reassigned whenever the player plugs in a
// headset, so a stored index silently starts recording from the wrong microphone.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Voice
{
    /// <summary>Enumerates the recording devices FMOD reports, and resolves a remembered one.</summary>
    public static class VoiceDevices
    {
        /// <summary>A connected recording device, as FMOD describes it.</summary>
        public readonly struct Device
        {
            public readonly int Index;
            public readonly string Name;
            public readonly int SampleRate;
            public readonly int Channels;
            public readonly bool IsSystemDefault;

            public Device(int index, string name, int sampleRate, int channels, bool isSystemDefault)
            {
                Index = index;
                Name = string.IsNullOrWhiteSpace(name) ? $"Device {index}" : name;
                SampleRate = sampleRate;
                Channels = channels;
                IsSystemDefault = isSystemDefault;
            }

            public bool IsValid => Index >= 0 && SampleRate > 0;
        }

        private const int NameBufferLength = 256;

        // Said once. A machine with no microphone is a normal state, not a per-frame complaint.
        private static bool complained;

        /// <summary>
        /// Every currently connected recording device. Empty when FMOD is not up yet or the machine
        /// has no microphone — never null, and never throws.
        /// </summary>
        public static List<Device> Connected()
        {
            var devices = new List<Device>();

            try
            {
                FMOD.System core = FMODUnity.RuntimeManager.CoreSystem;

                if (core.getRecordNumDrivers(out int total, out int _) != FMOD.RESULT.OK)
                    return devices;

                for (int i = 0; i < total; i++)
                {
                    // Implicitly typed discards: the driver's guid is a System.Guid, not an
                    // FMOD one, and naming either type here buys nothing but a way to be wrong.
                    FMOD.RESULT result = core.getRecordDriverInfo(
                        i, out string name, NameBufferLength, out _,
                        out int rate, out _, out int channels,
                        out FMOD.DRIVER_STATE state);

                    if (result != FMOD.RESULT.OK) continue;
                    if ((state & FMOD.DRIVER_STATE.CONNECTED) == 0) continue;

                    devices.Add(new Device(i, name, rate, channels,
                        (state & FMOD.DRIVER_STATE.DEFAULT) != 0));
                }
            }
            catch (System.Exception e)
            {
                if (!complained)
                {
                    complained = true;
                    Debug.LogWarning($"[Voice] Could not enumerate recording devices: {e.Message}");
                }
            }

            return devices;
        }

        /// <summary>
        /// The device stored under <paramref name="rememberedName"/>, or the system default when
        /// that microphone is not plugged in right now. False only when there is no microphone.
        /// </summary>
        public static bool TryResolve(string rememberedName, out Device device)
        {
            device = default;
            List<Device> devices = Connected();
            if (devices.Count == 0) return false;

            if (!string.IsNullOrEmpty(rememberedName))
            {
                foreach (Device candidate in devices)
                {
                    if (candidate.Name == rememberedName)
                    {
                        device = candidate;
                        return true;
                    }
                }
            }

            foreach (Device candidate in devices)
            {
                if (candidate.IsSystemDefault)
                {
                    device = candidate;
                    return true;
                }
            }

            device = devices[0];
            return true;
        }
    }
}
