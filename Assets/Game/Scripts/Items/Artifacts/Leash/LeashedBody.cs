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
using System.Collections.Generic;
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

        /// <summary>This player's reversal memory, shared by every rope on them. See <see cref="Yanked"/>.</summary>
        private readonly SnareStruggleReader struggle = new();

        /// <summary>Their anti-macro throttle. Built from the first rope, because it needs its rate.</summary>
        private SnareStruggleMeter meter;

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

            // Read the keys ONCE, before any rope sees them. SnareStruggleReader.Counts measures a
            // reversal against the last direction and then remembers this one, so asking it per
            // rope would have every rope after the first compare the input against itself and no
            // yank would ever land. One player, one pair of hands, one answer per step.
            bool jerked = Yanked(ropes);

            for (int i = ropes.Count - 1; i >= 0; i--)
            {
                Leash rope = ropes[i];
                if (rope == null) continue;

                LeashEnd mine = rope.PlayerEndOn(body);
                if (mine == null) continue;

                rope.ResolveEnd(mine, rope.Opposite(mine));
                Struggle(rope, mine, jerked);
            }
        }

        /// <summary>
        /// Did this player throw themselves about hard enough to count as one yank this step?
        ///
        /// <para>
        /// The reader and the meter are shared by every rope on this body — the reversal memory is
        /// a property of the player, not of a rope, and so is the throttle: it is their hands the
        /// cap exists to protect (<c>GDC-L1-UX-0006</c>). The thresholds come from the first rope
        /// tied to them, because the question is asked once and cannot be asked per rope; ropes in
        /// this project all come from one leash prefab, so in practice there is one answer anyway.
        /// </para>
        /// <para>
        /// Unlike <see cref="SnaredBody"/> this does not poll its own <c>InputControls</c>. A
        /// netted player goes limp, which switches their input off and forces the reader to hold a
        /// copy of the asset; a leashed player is on their feet with their own controls live, so
        /// <see cref="PlayerMovement.WishDirection"/> already carries what they are asking for —
        /// and it is zero while a menu holds the controls, which is the gate the reader's own
        /// <c>MayRead</c> would otherwise have to supply.
        /// </para>
        /// </summary>
        private bool Yanked(IReadOnlyList<Leash> ropes)
        {
            Leash first = FirstRopeOnThisBody(ropes);
            if (first == null || movement == null) return false;

            meter ??= new SnareStruggleMeter(first.MaxUsefulStruggleRate, first.StrainFadeSeconds);
            meter.Advance(Time.fixedDeltaTime);

            // Measured in the PLAYER'S OWN frame, not the world's. WishDirection is world-space
            // and turns with the camera, so a captive who merely spun the mouse round while
            // holding one key would be reversing their heading twice a second without ever having
            // changed which key they are pressing. Back in local space it is the keys again: A
            // against D is a reversal from any facing, and turning on the spot is not one.
            Vector3 wish = movement.transform.InverseTransformDirection(movement.WishDirection);

            bool counts = struggle.Counts(jumpPressed: false, new Vector2(wish.x, wish.z),
                                          first.StruggleMoveDeadzone, first.StruggleReversalDot);

            // Offered rather than counted. Push answering false means the yank landed inside the
            // cooldown, so hammering the key faster than a person can is worth exactly nothing.
            return counts && meter.Push();
        }

        /// <summary>The first rope of the set that is tied to this body, or null if none is.</summary>
        private Leash FirstRopeOnThisBody(IReadOnlyList<Leash> ropes)
        {
            for (int i = 0; i < ropes.Count; i++)
            {
                if (ropes[i] != null && ropes[i].PlayerEndOn(body) != null) return ropes[i];
            }

            return null;
        }

        /// <summary>
        /// One step of fighting the rope. Each accepted yank buys a fixed share of the way out and
        /// the strain fades when the player stops fighting; at full strain the rope parts.
        ///
        /// <para>
        /// Here rather than in <see cref="Leash"/> because the input it reads is LOCAL — only the
        /// struggling player's own machine has it, which is also why this end's owner is the one
        /// that announces the snap. Strain itself is never sent.
        /// </para>
        /// <para>
        /// <b>A rope you are HOLDING can never be torn off by your own movement.</b> A hand end is
        /// a hauler, not a captive: walking away with the far end tied to a 1000 kg hull is the
        /// item working, and charging that as an escape attempt is what used to part a rope
        /// mid-haul. The captive is the other end, and the only end that may fight is one the rope
        /// is tied TO.
        /// </para>
        /// </summary>
        private void Struggle(Leash rope, LeashEnd mine, bool jerked)
        {
            LeashEnd other = rope.Opposite(mine);
            if (!other.IsAlive) return;

            // See the summary: hauling is not struggling, however hard the load resists.
            if (mine.Kind == LeashEndKind.PlayerHand) return;

            // A passenger cannot struggle. Their body is kinematic and parented into a seat, and
            // the wish direction that reaches this class is the one they steer the mount with, so
            // a rider swerving would be tearing ropes off with the steering wheel. This became
            // reachable the moment LeggedDriver started implementing ITowable, which stopped
            // FixedUpdate returning early for riders.
            if (body.isKinematic) return;

            // Only a rope that is actually pulling can be fought. Yanking against a slack one earns
            // nothing, so throwing yourself about beside a knot you are standing next to never
            // tears it off.
            bool fought = jerked && rope.IsTaut;

            float jerks = Leash.ResistJerks(other.PullStrength, mine.PullStrength,
                                            other.Mass, mine.Mass,
                                            rope.ResistBaseJerks,
                                            rope.MinResistRatio, rope.MaxResistRatio);

            rope.SetStrainOn(mine,
                Leash.ResistStrain(rope.StrainOn(mine), fought, jerks,
                                   Time.fixedDeltaTime, rope.StrainDecay));

            if (rope.StrainOn(mine) >= 1f) rope.Snap();
        }
    }
}
