// The half of a rope's physics that only a player's own machine can run.
//
// Two separate rules meet here, and the component exists because no other place satisfies both.
//
// The first is ownership. A player's Rigidbody is the one thing the server is NOT authoritative
// over — their NetworkTransform is owner-authoritative, so anything the server pushes into that
// body is overwritten by the owner's next state update, silently, within a tick. That is the same
// failure that made server-side respawn teleports snap back.
//
// The second is ORDER, which is what the rope this replaces got wrong. PlayerMovement.FixedUpdate
// assigns rb.linearVelocity outright — Lerp(current, desired, 1) while grounded — so a pull applied
// before it runs is not merely reduced, it is deleted. The execution order attribute below is
// therefore load-bearing: it is the difference between a rope that holds a walking player and one
// that does nothing at all.
//
// What is deliberately NOT here: anything that would make a leash a way to get around. A rope may
// now TOW — it adds speed to whichever end is losing the pull contest, which is the whole point of
// the item — and what keeps it from being a second grappling hook is structural rather than a
// clamp: there is no winch anywhere in this system, so a player cannot pull THEMSELVES along a
// rope, and nothing here touches PlayerMovement.SetTethered. That flag is the grappling hook's
// swing steering: it lets a player pump an arc, preserves the speed they build across it, and
// suppresses fall damage for the whole swing. A leash that set it would be a second grappling hook
// with a longer reach.
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// Applies every rope tied to this player, on this player's own machine, after they have moved.
    ///
    /// <para>
    /// Added on demand rather than authored on the prefab, because a rope can be tied to any player
    /// at any time and the alternative is a component every player carries for a case most of them
    /// never hit. Same shape and same reason as <see cref="LeashAttachable.GetOrAdd"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    [DefaultExecutionOrder(200)] // after PlayerMovement — see the note at the top of this file
    public sealed class LeashedBody : MonoBehaviour
    {
        private Rigidbody body;
        private PlayerMovement movement;

        public static LeashedBody Ensure(GameObject player)
        {
            if (player == null) return null;

            return player.TryGetComponent(out LeashedBody existing)
                ? existing
                : player.AddComponent<LeashedBody>();
        }

        private void Awake()
        {
            body = GetComponentInChildren<Rigidbody>();
            movement = GetComponentInChildren<PlayerMovement>();
        }

        private void FixedUpdate()
        {
            // Everyone has one of these once a rope has been tied to them; only the machine that
            // owns the body may move it. Elsewhere this player is a replica whose position is
            // somebody else's to publish.
            if (body == null || !Network.Owns(this)) return;

            // A seated rider's body is kinematic and parented into the seat, so there is nothing
            // here to push. The rope is not inert in that case — the end resolves against the
            // machine underneath instead, through its ITowable branch in LeashEnd.Pull.
            if (body.isKinematic && GetComponentInParent<ITowable>() == null) return;

            var ropes = Leash.All;
            for (int i = ropes.Count - 1; i >= 0; i--)
            {
                Leash rope = ropes[i];
                if (rope == null) continue;

                LeashEnd mine = rope.PlayerEndOn(body);
                if (mine == null) continue;

                rope.ResolveEnd(mine, rope.Opposite(mine));
                Struggle(rope, mine);
            }
        }

        /// <summary>
        /// One step of fighting the rope. Strain builds while the player's movement input points
        /// squarely away from the knot and decays when it does not; at full strain the rope parts.
        ///
        /// <para>
        /// Here rather than in <see cref="Leash"/> because the input it reads is LOCAL — only the
        /// struggling player's own machine has it, which is also why this end's owner is the one
        /// that announces the snap. Strain itself is never sent.
        /// </para>
        /// </summary>
        private void Struggle(Leash rope, LeashEnd mine)
        {
            LeashEnd other = rope.Opposite(mine);
            if (!other.IsAlive) return;

            // A passenger cannot struggle. Their body is kinematic and parented into a seat, so it
            // reports no velocity of its own however fast the mount is carrying them — and reading
            // that zero as "the rope is holding me" would charge full strain and tear the rope off
            // a mounted player in a fifth of a second. This became reachable the moment LeggedDriver
            // started implementing ITowable, which stopped FixedUpdate returning early for riders.
            if (body.isKinematic) return;

            Vector3 knotToMe = body.position - other.Position;
            if (knotToMe.sqrMagnitude <= 1e-4f) return;

            Vector3 away = knotToMe.normalized;
            Vector3 wish = movement != null ? movement.WishDirection : Vector3.zero;

            // Only a rope that is actually pulling can be fought. Struggling against a slack one
            // earns nothing, so walking around a knot you are standing next to never tears it off.
            float wishAway = rope.IsTaut ? Mathf.Max(0f, Vector3.Dot(wish, away)) : 0f;

            // ...and only the part of that the rope actually STOPPED. Towing and struggling are the
            // same input — a movement key pointing away from a taut rope — so the input alone
            // cannot tell them apart, and reading it as a struggle meant hauling any dropped item
            // tore the rope off in 0.2 s. What separates them is whether the load came along.
            // See Leash.HeldBackFraction.
            float against = wishAway * Leash.HeldBackFraction(
                wishAway, Vector3.Dot(body.linearVelocity, away), mine.TopSpeed);

            float seconds = Leash.ResistSeconds(other.PullStrength, mine.PullStrength,
                                                rope.ResistBaseSeconds);

            rope.SetStrainOn(mine,
                Leash.ResistStrain(rope.StrainOn(mine), against, seconds,
                                   Time.fixedDeltaTime, rope.StrainDecay));

            if (rope.StrainOn(mine) >= 1f) rope.Snap();
        }
    }
}
