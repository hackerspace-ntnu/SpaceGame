using Newtonsoft.Json.Linq;

namespace SpaceGame.Persistence
{
    /// <summary>
    /// Implemented by any component that owns a slice of persisted state.
    ///
    /// A saver owns one key and everything under it. It is never told what the rest of the save
    /// contains, and nothing else may write to its key — which is what lets savers be added,
    /// removed and reshaped without a format migration.
    ///
    /// Restore is called on an object that already exists and is already positioned. Placement is
    /// the store's job, not a saver's, because the store has to place a runtime object before any
    /// of its components can be asked anything.
    /// </summary>
    public interface ISaveable
    {
        /// <summary>
        /// This saver's namespace within its owner's <see cref="StateBag"/>. Must be stable across
        /// builds — it is written into save files, so renaming it orphans everything saved under
        /// the old spelling. Use a short lower-case noun: "health", "inventory".
        /// </summary>
        string SaveKey { get; }

        /// <summary>
        /// The state to persist, as any object the save serializer can write. Returning null stores
        /// nothing, which is the right answer for a component that is currently at its defaults.
        /// </summary>
        object CaptureState();

        /// <summary>
        /// Applies previously captured state. <paramref name="state"/> is exactly what
        /// <see cref="CaptureState"/> produced, possibly in an older shape — read it defensively
        /// and leave anything it does not mention alone.
        /// </summary>
        void RestoreState(JObject state);
    }

    /// <summary>
    /// Optional companion to <see cref="ISaveable"/> for state that cannot be applied the instant
    /// the object appears.
    ///
    /// A saver that needs the world around it — ground to stand on, a chunk that is still streaming
    /// in, a NetworkObject that has not spawned yet — implements this and gets called again once
    /// the load has settled. Without it, such a saver has to choose between applying state too
    /// early and not applying it at all.
    /// </summary>
    public interface IDeferredSaveable
    {
        /// <summary>Called after every scene in the load has been hydrated and players are spawned.</summary>
        void OnLoadComplete();
    }
}
