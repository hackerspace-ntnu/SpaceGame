using SpaceGame.Gameplay.Ragdoll;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// One claim on a player who must stop being able to walk about, taken while a condition runs
    /// and given back when it ends.
    ///
    /// <para>
    /// This is the net gun's hold, borrowed rather than reimplemented: <c>PlayerRagdoll.HoldDown</c>
    /// already answers "this player is helpless until somebody says otherwise", is claim-counted so
    /// two captors do not free each other's captive, refuses the cases that must be refused (a
    /// corpse, a body a seat is already placing) and runs on every machine so nobody watches a
    /// captive stroll around. Writing a second version of that for statuses would be the same code
    /// with a different name, and the second one would be the one with the bugs.
    /// </para>
    /// <para>
    /// <b>Players only, deliberately.</b> A creature's helplessness is DERIVED instead, every frame,
    /// by <c>StatusReactionModule</c> reading the flag and returning an idle intent — because
    /// suspending a creature writes to it, a world save captures what is written, and a creature
    /// that reloads with its brain switched off is a creature that never moves again. A player has
    /// no behaviour module to starve and their movement is owner-authoritative, so the ragdoll hold
    /// — which every machine performs off the same replicated flag — is what is left, and it stores
    /// nothing the world save reads.
    /// </para>
    /// </summary>
    public sealed class BodyHold
    {
        private PlayerRagdoll held;

        /// <summary>Whether this hold currently has anybody.</summary>
        public bool HasHold => held != null;

        /// <summary>
        /// Put <paramref name="body"/> down, if it is a player and the hold takes.
        ///
        /// A refusal is not an error and is not logged: most bodies a status lands on are creatures
        /// and props with no <c>PlayerRagdoll</c> at all, and the ones that have one may legitimately
        /// decline — see <c>PlayerRagdoll.HoldDown</c>.
        /// </summary>
        public void Take(StatusReceiver body)
        {
            if (held != null || body == null) return;

            // From the parent: a status can be applied to whatever collider an artifact hit, and
            // the adapter sits on the root.
            PlayerRagdoll ragdoll = body.GetComponentInParent<PlayerRagdoll>();
            if (ragdoll == null || !ragdoll.HoldDown(this)) return;

            held = ragdoll;
        }

        /// <summary>
        /// Put <paramref name="body"/> down without putting it on the ground: control is taken,
        /// the body stops where it stands, and the skeleton is left exactly as it was.
        ///
        /// <para>
        /// This is what a freeze wants and what a net does not. A body frozen solid holds the pose
        /// it was caught in — that is the whole read of the condition, and it is thrown away by a
        /// ragdoll, which replaces the pose with a heap on the sand and hands the player a
        /// third-person camera they did not ask for. Being tied up is the opposite case: there the
        /// point IS that you are on the floor.
        /// </para>
        /// </summary>
        public void TakeStanding(StatusReceiver body)
        {
            if (held != null || body == null) return;

            PlayerRagdoll ragdoll = body.GetComponentInParent<PlayerRagdoll>();
            if (ragdoll == null || !ragdoll.HoldStanding(this)) return;

            held = ragdoll;
        }

        /// <summary>Give the claim back. Safe to call when nothing was ever held.</summary>
        public void Release()
        {
            if (held == null) return;

            held.ReleaseHold(this);
            held = null;
        }
    }
}
