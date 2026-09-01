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
// What is deliberately NOT here: anything that would make a leash a way to get around. See
// LeashEnd.Pull — a rope may take a player's speed away and it may drag them, but it may never
// give them speed, and it never touches PlayerMovement.SetTethered. That flag is the grappling
// hook's swing steering: it lets a player pump an arc, preserves the speed they build across it,
// and suppresses fall damage for the whole swing. A leash that set it was a second grappling hook
// with a longer reach.
using UnityEngine;
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

        public static LeashedBody Ensure(GameObject player)
        {
            if (player == null) return null;

            return player.TryGetComponent(out LeashedBody existing)
                ? existing
                : player.AddComponent<LeashedBody>();
        }

        private void Awake() => body = GetComponentInChildren<Rigidbody>();

        private void FixedUpdate()
        {
            // Everyone has one of these once a rope has been tied to them; only the machine that
            // owns the body may move it. Elsewhere this player is a replica whose position is
            // somebody else's to publish.
            if (body == null || body.isKinematic || !Network.Owns(this)) return;

            var ropes = Leash.All;
            for (int i = ropes.Count - 1; i >= 0; i--)
            {
                Leash rope = ropes[i];
                if (rope == null) continue;

                LeashEnd mine = rope.PlayerEndOn(body);
                if (mine == null) continue;

                rope.ResolveEnd(mine, rope.Opposite(mine));
            }
        }
    }
}
