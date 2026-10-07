using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keeps the satellite tower's dish drives awake once somebody has taken the grappling hook.
    ///
    /// <para>
    /// Null while they are still dead. A record without it — a new world, or a save from before the drives
    /// were locked — restores them dead, and <see cref="DishConsole"/> wakes them again on the first server
    /// frame if the board's own saved contents show the hook already gone, so an old world where the hook
    /// was taken is never locked out of its dish.
    /// </para>
    /// <para>
    /// On the lectern beside <see cref="DishConsole"/>, placed by hand on <c>SatelliteTower.prefab</c> and
    /// collected by the tower root's <c>SaveableEntity</c>.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(DishConsole))]
    public class DishConsoleSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "dishConsole";   // written into save files — NEVER rename

        private DishConsole console;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake.
        private DishConsole Console => console != null ? console : console = GetComponent<DishConsole>();

        public string SaveKey => Key;

        public struct State
        {
            public bool unlocked;
        }

        public object CaptureState() =>
            Console != null && Console.DrivesUnlocked ? new State { unlocked = true } : null;

        public void RestoreState(JObject state)
        {
            if (Console == null) return;

            bool unlocked = state != null && state.ToObject<State>(SaveSerializer.Serializer).unlocked;
            Console.RestoreUnlocked(unlocked);
        }
    }
}
