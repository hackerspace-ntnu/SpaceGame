using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay.Puzzles;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keeps the sun shrine's vault open once it has been opened. The mirrors are the shrine's
    /// <see cref="DialSetSaveable"/>; the open door is not derivable from them, because the sun that
    /// lit the seal has moved on by the time anybody loads.
    /// </summary>
    [RequireComponent(typeof(SunSeal))]
    public class SunSealSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "sunSeal";   // written into save files — NEVER rename

        private SunSeal seal;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake.
        private SunSeal Seal => seal != null ? seal : seal = GetComponent<SunSeal>();

        public string SaveKey => Key;

        public struct State
        {
            public bool open;
        }

        public object CaptureState() =>
            Seal != null && Seal.IsOpen ? new State { open = true } : null;

        public void RestoreState(JObject state)
        {
            if (Seal == null) return;

            // No entry means sealed, and it has to be said: a previous restore this session may
            // have opened it.
            Seal.RestoreOpen(state != null && state.ToObject<State>(SaveSerializer.Serializer).open);
        }
    }
}
