using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists whether an animal is wearing a saddle.
    ///
    /// <b>It cannot be recomputed.</b> The saddle is a plain <c>Instantiate</c> on every machine
    /// rather than a spawned entity — see <see cref="SaddleSocket"/> — so nothing about it exists
    /// in the world for a load to find. Without this, every saddled animal in a reloaded world is
    /// bare, un-rideable, and has silently eaten whatever was stowed on it.
    ///
    /// <para>
    /// The saddle's CONTENTS are not here. They belong to the <c>PackContainer</c> on the saddle
    /// instance, which carries its own saver, and that instance does not exist until this one has
    /// restored — so the two must not be merged.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(SaddleSocket))]
    public class SaddleSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "saddle";          // written into save files — NEVER rename

        private SaddleSocket socket;

        private SaddleSocket Socket => socket != null ? socket : socket = GetComponent<SaddleSocket>();

        public string SaveKey => Key;

        public struct State
        {
            public bool saddled;
        }

        public object CaptureState()
        {
            // Null for a bare animal, so the overwhelmingly common case costs no bytes.
            if (Socket == null || !Socket.IsSaddled) return null;
            return new State { saddled = true };
        }

        public void RestoreState(JObject state)
        {
            if (Socket == null) return;

            if (state == null)
            {
                Socket.ApplySaddled(false);
                return;
            }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Socket.ApplySaddled(restored.saddled);
        }
    }
}
