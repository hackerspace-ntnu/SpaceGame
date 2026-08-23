using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keeps the torch on.
    ///
    /// <para>
    /// One bit, and the one the player is most likely to notice missing: this is a game with a night
    /// cycle, and a reload after dark put every player back in the dark with a light they had
    /// already switched on.
    /// </para>
    /// <para>
    /// <b>Found in children, not on the player root.</b> <see cref="Flashlight"/> lives on the
    /// <c>Flashlight</c> prefab nested under the player's <c>Main Camera</c>, so a
    /// <c>GetComponent</c> here answers null on every player in the game. The saver stays on the
    /// root because that is where the player's record is collected from.
    /// </para>
    /// </summary>
    public class FlashlightSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "flashlight";

        private Flashlight torch;

        private Flashlight Torch =>
            torch != null ? torch : torch = GetComponentInChildren<Flashlight>(true);

        public string SaveKey => Key;

        public struct State
        {
            public bool on;
        }

        public object CaptureState()
        {
            if (Torch == null) return null;

            // Off is the default and the common case; storing it would add a key to every player
            // record for the state a fresh player is already in.
            return Torch.IsOn ? new State { on = true } : null;
        }

        public void RestoreState(JObject state)
        {
            if (Torch == null) return;

            Torch.RestoreOn(state != null && state.ToObject<State>(SaveSerializer.Serializer).on);
        }
    }
}
