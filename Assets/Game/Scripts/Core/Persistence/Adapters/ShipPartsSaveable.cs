using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.Vehicles;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists which hull modules are fitted to a ship.
    ///
    /// <para>
    /// This is the whole point of the salvage loop, so it is the one thing on a wrecked hull that
    /// must not be lost: a player who spent a session finding a nuclear motor and hauling it home
    /// would come back to the same hole in the roof and the motor gone from their pack.
    /// </para>
    /// <para>
    /// The mask, and only the mask. Where the ship is comes back on the entity record's own pose,
    /// and the parts themselves are geometry inside the prefab rather than spawned objects, so
    /// there is nothing else here to keep.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(ShipPartRack))]
    public class ShipPartsSaveable : MonoBehaviour, ISaveable
    {
        /// <summary>Written into save files — never rename.</summary>
        public const string Key = "shipparts";

        private ShipPartRack rack;

        // Resolved lazily rather than in Awake: EditMode never runs Awake, and a field cached
        // there makes the saver untestable outside play mode.
        private ShipPartRack Rack => rack != null ? rack : rack = GetComponent<ShipPartRack>();

        public string SaveKey => Key;

        public struct State
        {
            public int installed;

            /// <summary>
            /// The sockets still holding a burnt-out unit. Appended 2026-10-06: a save written before
            /// it reads null, which restores the ship's authored broken units — the transmitter
            /// socket's unit is seated, unless that save already has a transmitter fitted.
            /// </summary>
            public int? broken;
        }

        /// <summary>
        /// The fitted set, or nothing when the ship is still exactly as it was authored. An
        /// untouched wreck in every chunk of the desert has nothing worth a line in the file.
        /// </summary>
        public object CaptureState()
        {
            if (Rack == null) return null;
            if (Rack.InstalledMask == Rack.AuthoredMask && Rack.BrokenMask == Rack.AuthoredBrokenMask)
                return null;

            return new State { installed = Rack.InstalledMask, broken = Rack.BrokenMask };
        }

        /// <summary>
        /// Restores the fitted set. A null record means "this ship is at its authored state" —
        /// written out rather than assumed, because the same live rack can be handed a record and
        /// then handed none, and a hull left carrying the previous world's repairs is worse than
        /// one reset to the wreck it should be.
        /// </summary>
        public void RestoreState(JObject state)
        {
            if (Rack == null) return;

            if (state == null)
            {
                Rack.RestoreMasks(Rack.AuthoredMask, Rack.AuthoredBrokenMask);
                return;
            }

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            Rack.RestoreMasks(restored.installed, restored.broken ?? Rack.AuthoredBrokenMask);
        }
    }
}
