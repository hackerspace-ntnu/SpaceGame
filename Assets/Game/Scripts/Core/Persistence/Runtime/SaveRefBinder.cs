using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// The live half of <see cref="SaveRef"/>: turns an object into a reference and a reference back
    /// into an object, using the two registries this system already maintains.
    ///
    /// Split from <see cref="SaveRef"/> because the format assembly cannot see either registry, and
    /// installed by <see cref="SaveManager"/> for the lifetime of a session.
    /// </summary>
    public class SaveRefBinder : ISaveRefBinder
    {
        private readonly PlayerSaveService players;

        public SaveRefBinder(PlayerSaveService players) => this.players = players;

        /// <summary>
        /// Names whatever entity <paramref name="target"/> belongs to.
        ///
        /// <b>Walks up the hierarchy, and that is the point.</b> A rider reference is captured from
        /// whatever the mount happens to be holding — a seat point, a capsule, the object a raycast
        /// hit — and none of those have an identity of their own. Describing the child would produce a
        /// reference that can never resolve, which is the silent-failure shape this whole system is
        /// built to avoid.
        ///
        /// Players are tested first. A player carries a <see cref="SaveableEntity"/> like anything
        /// else, so checking that first would describe the player as a world entity and file the
        /// reference under an instance id that no load ever recreates — its record is keyed by
        /// profile, and the object Netcode spawns next session is a different instance.
        /// </summary>
        public bool TryDescribe(GameObject target, out string kind, out string id)
        {
            kind = null;
            id = null;
            if (target == null) return false;

            for (Transform t = target.transform; t != null; t = t.parent)
            {
                if (players != null && players.TryGetProfileFor(t.gameObject, out string profileId))
                {
                    kind = SaveRef.PlayerKind;
                    id = profileId;
                    return true;
                }

                var entity = t.GetComponent<SaveableEntity>();
                if (entity == null || string.IsNullOrEmpty(entity.InstanceId)) continue;

                // An entity marked External is owned by another system and keyed by something other
                // than its instance id — a player whose binding has not happened yet is the case that
                // reaches here. Referencing it by instance would produce a dangling ref, so say no
                // and let the caller store nothing.
                if (!entity.BelongsToWorld) return false;

                kind = SaveRef.EntityKind;
                id = entity.InstanceId;
                return true;
            }

            return false;
        }

        public bool TryResolve(string kind, string id, out GameObject target)
        {
            target = null;
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(id)) return false;

            switch (kind)
            {
                case SaveRef.PlayerKind:
                    return players != null && players.TryGetBoundPlayer(id, out target);

                case SaveRef.EntityKind:
                    // Includes disabled objects, because a corpse is a disabled GameObject in this
                    // project and a reference to one is legitimate. SaveableEntity registers for the
                    // object's whole lifetime rather than while it is enabled, which is what makes
                    // this lookup able to answer at all.
                    if (!SaveableEntity.LiveEntities.TryGetValue(id, out SaveableEntity entity)) return false;
                    if (entity == null) return false;

                    target = entity.gameObject;
                    return true;

                default:
                    return false;
            }
        }
    }
}
