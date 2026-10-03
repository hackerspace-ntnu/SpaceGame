using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Remembers that a <see cref="ShelfStock"/> has laid its items, so a reload does not lay them again beside the
    /// ones already lying there, or replace the ones players took. The items themselves are ordinary world pickups
    /// and save as such.
    /// </summary>
    [RequireComponent(typeof(ShelfStock))]
    public class ShelfStockSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "shelfStock";   // written into save files — NEVER rename

        private ShelfStock shelf;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake.
        private ShelfStock Shelf => shelf != null ? shelf : shelf = GetComponent<ShelfStock>();

        public string SaveKey => Key;

        public struct State
        {
            public bool stocked;
        }

        public object CaptureState() => Shelf != null && Shelf.Stocked ? new State { stocked = true } : (object)null;

        public void RestoreState(JObject state)
        {
            if (Shelf == null) return;
            Shelf.RestoreStocked(state != null && state.ToObject<State>(SaveSerializer.Serializer).stocked);
        }
    }
}
