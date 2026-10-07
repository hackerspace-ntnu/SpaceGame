using Newtonsoft.Json.Linq;
using UnityEngine;

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
        ///
        /// <b><paramref name="state"/> is null when the record has no entry under this key, and
        /// that is a value, not an absence.</b> It is what <see cref="CaptureState"/> returning
        /// null writes, so it means "this component was at its defaults when the save was taken" —
        /// and a saver is required to put it back to those defaults rather than leave whatever the
        /// live component happens to be holding. Every saver is called on every restore, precisely
        /// so that "reset me" is expressible.
        ///
        /// This matters most for savers that stage work for <see cref="IDeferredSaveable"/>: the
        /// pending reference and its "has pending" flag must be cleared on the null path, or a
        /// value read at one save is re-applied after a later save that did not store it.
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

        /// <summary>
        /// Where this saver sits in the deferred pass. Lower runs first; the default is fine for
        /// almost everything.
        ///
        /// <b>It exists because the pass had an ordering dependency it could not express.</b> Savers
        /// ran in <c>GetComponents</c> order — i.e. in whatever order somebody happened to add
        /// components to a prefab — and <c>OrnithopterSaveable</c> had to route around that by
        /// abandoning its own <c>OnLoadComplete</c> and subscribing to <c>MountModule.Mounted</c>
        /// instead, because it needs the rider <c>MountSaveable</c> re-seats and might run before it.
        /// That workaround is invisible to the next person with the same problem.
        ///
        /// The convention: seat riders and re-establish ownership at <see cref="Early"/>, do
        /// everything that reads the result of that at the default, and leave <see cref="Late"/> for
        /// work that wants the whole world settled.
        /// </summary>
        int LoadOrder => Default;

        /// <summary>Runs before ordinary deferred savers. For re-establishing structure others read.</summary>
        public const int Early = -100;

        /// <summary>The order everything gets unless it says otherwise.</summary>
        public const int Default = 0;

        /// <summary>Runs after ordinary deferred savers.</summary>
        public const int Late = 100;
    }

    /// <summary>
    /// Implemented by a component whose object's live pose is not the one a save should keep.
    ///
    /// <para>
    /// A ragdoll is the case this exists for. While limp its root sits AT the pelvis and carries
    /// the body's tilt, so that every watcher can rebuild the pelvis from it — but a load puts the
    /// root back and the model on it in its standing pose. A knocked-down body is alive and
    /// standing on load, so it wants its tilt dropped; a corpse goes limp again, so it wants the
    /// root placed where its standing pose puts the pelvis back where it lay, rather than a hip
    /// height further along the tilted up axis. The object answers for itself here rather than
    /// every place that records a pose learning about ragdolls. Read it through
    /// <see cref="SavedPose"/>.
    /// </para>
    /// </summary>
    public interface ISavedPose
    {
        /// <summary>The position to record for this object's transform right now.</summary>
        Vector3 PositionToSave { get; }

        /// <summary>The rotation to record for this object's transform right now.</summary>
        Quaternion RotationToSave { get; }
    }

    /// <summary>The one way a pose capture reads a pose. See <see cref="ISavedPose"/>.</summary>
    public static class SavedPose
    {
        public static Vector3 PositionOf(Transform transform) =>
            transform.TryGetComponent(out ISavedPose saved) ? saved.PositionToSave : transform.position;

        public static Quaternion RotationOf(Transform transform) =>
            transform.TryGetComponent(out ISavedPose saved) ? saved.RotationToSave : transform.rotation;
    }
}
