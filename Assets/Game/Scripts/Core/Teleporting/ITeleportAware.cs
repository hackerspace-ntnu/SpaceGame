// "I was just moved somewhere else, instantly."
//
// ─────────── what this is for ───────────
//
// Almost nothing in the game needs this. A crate is its transform and a Rigidbody, and moving those
// two moves the crate. The systems that DO need it are the ones that keep a second copy of where
// they are, in world space, outside the transform — and every one of them is silently broken by a
// teleport until it is told:
//
//   • a legged machine holds its path position and every planted foot in world space, and re-writes
//     the body's transform from them each frame. Teleport one and it walks straight back.
//   • a NavMeshAgent holds its own position and stops dead off its polygon.
//   • a look rig holds pitch as a float outside the transform.
//
// Rather than have each teleporting FEATURE learn that list — the portal gun, the respawn, the save
// load, the interior transition, the chat command — the object states its own needs once, here, and
// every teleport in the game satisfies them. Adding a system with world-space state costs one
// interface; adding a new way to teleport costs nothing at all.
//
// ─────────── how it is delivered ───────────
//
// <see cref="SpaceGame.Core.Persistence.SaveTeleport"/> is the single function in this project that
// moves an object instantly, and it raises this on every implementor under the object AFTER the
// move has landed. So there is exactly one call site, no registration, and nothing to remember to
// wire up.
//
// Implementations must be idempotent in the sense that matters: they receive the move once, they
// rebase, and they do not move the object again. Teleporting something else from inside one is a
// re-entrant teleport and is the one thing this cannot do for you.
namespace SpaceGame.Teleporting
{
    public interface ITeleportAware
    {
        /// <summary>
        /// Bring this component's world-space state along.
        ///
        /// Called after the transform and every Rigidbody under it are already at the destination,
        /// so reading the transform here reads the new pose. Not called for a resync — a move whose
        /// start and end are the same pose is not a teleport, and the netcode makes several of those
        /// a second.
        /// </summary>
        void OnTeleported(in TeleportMove move);
    }
}
