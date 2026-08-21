using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists how much scrap has been handed to the ship — which is to say, persists the game's
    /// win condition.
    ///
    /// <para>
    /// It is a three-step run: three pieces of scrap and the ship leaves. Before this saver, two of
    /// those three steps could be undone by a reload — the count went back to zero AND the scrap was
    /// gone from the player's inventory, because handing it over consumed it. That is the single
    /// worst thing in this game a save could lose, and it is one integer.
    /// </para>
    /// <para>
    /// The count only, never the win itself. <c>GameStateSaveable</c> owns whether the run is over;
    /// re-deciding it here would load a finished world straight into the win scene.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Ship))]
    public class ShipSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "ship";       // written into save files — NEVER rename

        private Ship ship;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private Ship Vessel => ship != null ? ship : ship = GetComponent<Ship>();

        public string SaveKey => Key;

        public struct State
        {
            public int scrap;
        }

        public object CaptureState()
        {
            if (Vessel == null) return null;

            return Vessel.ScrapCollected > 0 ? new State { scrap = Vessel.ScrapCollected } : (object)null;
        }

        public void RestoreState(JObject state)
        {
            if (Vessel == null) return;

            if (state == null) { Vessel.RestoreScrap(0); return; }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Vessel.RestoreScrap(restored.scrap);
        }
    }
}
