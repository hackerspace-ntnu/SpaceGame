using Unity.Netcode.Components;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Which machine writes a body's one-shot and seeded animation: exactly one per body.
    ///
    /// <para>
    /// A body with a <see cref="NetworkAnimator"/> (the player) has its layer states and weights
    /// replayed on every other machine by NGO — not just its parameters. So only that animator's
    /// authority may start an action; a watcher that played it too would play it twice, the
    /// replayed crossfade restarting the local one. A body without one (every NPC) is animated by
    /// every machine from the events that already reach them all, so every machine writes.
    /// </para>
    /// <para>
    /// The rule is NGO's own (<c>NetworkAnimator</c>'s private HasAuthority): the owner when
    /// owner-authoritative, the server otherwise. <b>Not</b> <c>NetworkBehaviour.HasAuthority</c>,
    /// which means IsServer in client-server mode and would hand the host every client's body.
    /// </para>
    /// </summary>
    public static class AnimatorAuthority
    {
        /// <summary>The NetworkAnimator replicating <paramref name="animator"/>, if any.</summary>
        public static NetworkAnimator Find(Animator animator) =>
            animator != null ? animator.GetComponentInParent<NetworkAnimator>(true) : null;

        /// <summary>
        /// True where this machine should write. Unspawned counts as writing: single player, an
        /// editor preview and a body not yet on the network all animate themselves.
        /// </summary>
        public static bool Writes(NetworkAnimator networkAnimator)
        {
            if (networkAnimator == null || !networkAnimator.IsSpawned) return true;
            return networkAnimator.IsServerAuthoritative() ? networkAnimator.IsServer : networkAnimator.IsOwner;
        }
    }
}
