using Newtonsoft.Json;
using UnityEngine;

namespace SpaceGame.Persistence
{
    /// <summary>
    /// A reference from one saved object to another, in a form a save file can hold.
    ///
    /// <b>The gap this closes.</b> A mount's record has to say "profile <c>abc</c> was riding me"; a
    /// Golem's has to say "I was fighting entity <c>def</c>". A <see cref="StateBag"/> can hold any
    /// JSON, but a <c>Transform</c> is a live object reference and means nothing in a file — so
    /// before this type existed, mount state and combat state were not merely unimplemented, they
    /// were inexpressible. That is why the two features most obviously missing from this save system
    /// were both missing.
    ///
    /// <b>Two kinds, because there are two populations.</b> A player is keyed by profile and spawned
    /// by Netcode; a world entity is keyed by instance id and owned by the world store. That split is
    /// load-bearing everywhere else in this system (see <c>SaveScope</c>), so a reference has to carry
    /// which side of it the referent is on. Nothing else may be referenced.
    ///
    /// <b>Resolve late.</b> A reference is only meaningful once the thing it names exists, and when a
    /// mount is restored its rider does not: Netcode spawns players at a time this system does not
    /// control, and other chunks may still be streaming. Read the ref in
    /// <see cref="ISaveable.RestoreState"/>, stash it, and resolve it in
    /// <see cref="IDeferredSaveable.OnLoadComplete"/>.
    /// </summary>
    public struct SaveRef
    {
        public const string PlayerKind = "player";
        public const string EntityKind = "entity";

        /// <summary><see cref="PlayerKind"/> or <see cref="EntityKind"/>. Empty means "no referent".</summary>
        [JsonProperty("kind")] public string Kind;

        /// <summary>A profile id for a player, a <c>SaveableEntity.InstanceId</c> for an entity.</summary>
        [JsonProperty("id")] public string Id;

        [JsonIgnore] public bool IsSet => !string.IsNullOrEmpty(Kind) && !string.IsNullOrEmpty(Id);

        /// <summary>An explicitly empty reference — "nobody was riding", "no target".</summary>
        public static SaveRef None => default;

        /// <summary>
        /// Describes whatever object <paramref name="target"/> belongs to.
        ///
        /// Walks up to the owning entity rather than taking the transform handed in: a rider
        /// reference is usually captured from a seat point or a child collider, and a reference to a
        /// child is a reference to something with no identity of its own.
        /// </summary>
        public static SaveRef From(GameObject target) =>
            target != null && SaveRefBinding.Active != null &&
            SaveRefBinding.Active.TryDescribe(target, out string kind, out string id)
                ? new SaveRef { Kind = kind, Id = id }
                : None;

        public static SaveRef From(Component target) => From(target == null ? null : target.gameObject);

        /// <summary>
        /// The live object this reference names, if it is here.
        ///
        /// False is an ordinary answer, not a failure: the referent may have been destroyed since the
        /// save, or belong to a player who has not rejoined. A saver that gets false must leave its
        /// state alone — an unresolvable rider means "not mounted", which is a valid world.
        /// </summary>
        public bool TryResolve(out GameObject target)
        {
            target = null;
            if (!IsSet || SaveRefBinding.Active == null) return false;

            return SaveRefBinding.Active.TryResolve(Kind, Id, out target);
        }

        public override string ToString() => IsSet ? $"{Kind}:{Id}" : "none";
    }

    /// <summary>
    /// Turns objects into <see cref="SaveRef"/>s and back.
    ///
    /// Implemented in the runtime assembly, because answering either question needs the live
    /// registries — <c>SaveableEntity.LiveEntities</c> and the player bindings — which this assembly
    /// cannot see. Kept as an interface rather than a pair of static delegates so an EditMode test can
    /// install a fake and exercise reference-carrying savers without a scene.
    /// </summary>
    public interface ISaveRefBinder
    {
        /// <summary>Which population <paramref name="target"/> belongs to, and its id within it.</summary>
        bool TryDescribe(GameObject target, out string kind, out string id);

        /// <summary>The live object for a kind and id, or false if it is not here.</summary>
        bool TryResolve(string kind, string id, out GameObject target);
    }

    /// <summary>
    /// The binder <see cref="SaveRef"/> currently speaks through.
    ///
    /// Null is a valid state: outside a session there is nothing to resolve against, and every
    /// <see cref="SaveRef"/> operation degrades to "no referent" rather than throwing.
    /// </summary>
    public static class SaveRefBinding
    {
        public static ISaveRefBinder Active;
    }
}
