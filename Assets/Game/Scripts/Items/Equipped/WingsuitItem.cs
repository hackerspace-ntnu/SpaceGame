// The wingsuit: a membrane worn on the back that turns a fall into a glide.
//
// The item is a switch and a set of wings. All of the flying is WingsuitFlight, on the player's own
// body; all of the looking-like-flying is WingsuitWings and WingsuitPose. What is here is the
// gesture, the rule about when it is allowed, and putting the three of them on and off the wearer.
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Vehicles.Ornithopter;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Worn in the torso slot, fired by a double tap of Space, exactly like the wing pack — and
    /// mutually exclusive with it for free, because there is one torso slot and both want it.
    ///
    /// <para>
    /// Owner-authoritative. The whole effect is the holder's own body, and the player's
    /// NetworkTransform is owner-authoritative: a glide begun on the server would be overwritten
    /// by the owner's next state update, silently, which is the mistake <c>UsableItem</c> warns
    /// about for exactly this shape of item.
    /// </para>
    /// </summary>
    public class WingsuitItem : UsableItem, IItemDeferredRestore
    {
        [Tooltip("The membranes. Left on the pack until the suit is worn, then strapped to the " +
                 "wearer's arms.")]
        [SerializeField] private WingsuitWings wings;

        private WingsuitFlight flight;
        private WingsuitPose pose;

        /// <summary>Owner-authoritative: the effect is the holder's own body and nothing else.</summary>
        public override UseAuthority Authority => UseAuthority.Owner;

        /// <summary>Worn, not gripped — the hands stay free.</summary>
        protected override bool UsesHoldPose => false;

        // ── Wearing ────────────────────────────────────────────────────────────

        /// <summary>
        /// Strap the suit on. Runs on every machine, because a worn instance is derived from
        /// replicated slot state rather than sent — so the wings and the pose exist for a peer
        /// watching somebody else fly, and only the flight itself is owner-only.
        /// </summary>
        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);
            if (holder == null) return;

            if (wings != null) wings.AttachTo(holder);

            pose = holder.GetComponent<WingsuitPose>();
            if (pose == null) pose = holder.AddComponent<WingsuitPose>();

            // The flight writes velocity onto a body only its owner may move. On a peer it would
            // be a second simulation of somebody else's flight, fighting the replicated one.
            if (OwnerIsLocal())
            {
                flight = holder.GetComponent<WingsuitFlight>();
                if (flight == null) flight = holder.AddComponent<WingsuitFlight>();
            }

            Repaint(holder);
        }

        /// <summary>
        /// Take it off, and put back everything it changed. Reached from a slot swap, a drop, a
        /// death and a despawn alike, so it has to be safe from all four.
        /// </summary>
        public override void OnUnequipped(GameObject holder)
        {
            // Before anything is destroyed: End hands the body back to PlayerMovement and
            // PlayerLook, and a suit taken off mid-glide must not leave them switched off.
            if (flight != null)
            {
                flight.End();
                Destroy(flight);
                flight = null;
            }

            if (pose != null)
            {
                Destroy(pose);
                pose = null;
            }

            if (wings != null) wings.Detach();

            base.OnUnequipped(holder);
        }

        /// <summary>
        /// Paint the membrane in the wearer's own suit colour.
        ///
        /// <para>
        /// Asked of <c>PlayerIdentity</c>, which owns the replicated swatch index and is what
        /// paints the astronaut — so the wing is the same colour on every machine for the same
        /// reason the suit is, and follows the wearer if they change it. The settings fallback is
        /// for a body with no identity on it: the editor placeholder player, and tests.
        /// </para>
        /// </summary>
        private void Repaint(GameObject holder)
        {
            var identity = holder.GetComponent<PlayerIdentity>();
            if (identity != null)
            {
                identity.Repaint();
                return;
            }

            var recolor = GetComponentInChildren<WingsuitRecolor>(true);
            if (recolor != null) recolor.Apply(GameSettings.SuitColorIndex);
        }

        // ── The gesture ────────────────────────────────────────────────────────

        /// <summary>
        /// A double tap of Space spreads the wings, or folds them if they are already out.
        ///
        /// <para>
        /// The ground test is <c>PlayerMovement.IsOnGround</c> rather than a probe of this item's
        /// own, and that is not just tidiness: the same probe is what ends a glide, so asking a
        /// different one could let the suit deploy into a state where it lands on the very next
        /// physics step. One probe, one answer, and the suit can never open and shut in a frame.
        /// </para>
        /// </summary>
        protected override bool CanUse()
        {
            if (!base.CanUse()) return false;
            if (owner == null) return false;

            // Folding is always legal. The player is airborne by definition while gliding, and
            // refusing to let them shut the wings would be a trap rather than a rule.
            if (flight != null && flight.IsGliding) return true;

            var movement = owner.GetComponent<PlayerMovement>();
            if (movement != null && movement.IsOnGround)
            {
                // Deliberately not silent, for the reason the wing pack's refusal is not: "nothing
                // happened" is indistinguishable from a broken item.
                Debug.Log("Wingsuit: nothing to glide on — jump, or step off something.", this);
                return false;
            }

            return true;
        }

        /// <summary>Owner side, because <see cref="Authority"/> is Owner. A plain toggle.</summary>
        protected override void Use()
        {
            if (flight == null) return;

            if (flight.IsGliding) flight.End();
            else flight.Begin();
        }

        // No Present override. The snap of the wings is `useSoundId` on the prefab, which PlayUse
        // already plays on every machine, and the wings themselves are not shown from a use event
        // at all — they follow the replicated glide bool in Update, which is still true a minute
        // later for a late joiner where a one-off message would have left them folded.

        /// <summary>
        /// Follow the replicated glide bool, on every machine.
        ///
        /// The animator parameter is the one piece of state the flight publishes, and
        /// <c>ClientNetworkAnimator</c> replicates it like any other — so reading it back here is
        /// what makes a peer's copy of the suit open and shut in step with the flight, with
        /// nothing of its own on the wire.
        /// </summary>
        private void Update()
        {
            bool gliding = IsWearerGliding();

            if (wings != null) wings.Spread = gliding;
            if (pose != null) pose.Active = gliding;
        }

        private bool IsWearerGliding()
        {
            if (owner == null) return false;

            var animator = owner.GetComponent<Animator>();
            return animator != null && animator.GetBool(WingsuitFlight.GlidingParameter);
        }

        // ── Persistence ────────────────────────────────────────────────────────
        //
        // The suit itself is saved by the body slot it sits in; the only thing here worth a bag is
        // a glide that was in progress. Without it a mid-air quicksave reloads standing in thin
        // air with no speed, which is a fall — the ornithopter learned the same lesson and answers
        // it the same way, in the deferred pass.

        private const string GlideKey = "glide";
        private const string NoseKey = "nose";

        private bool pendingGlide;
        private Vector3 pendingFlight;
        private float pendingNose;

        public bool HasPendingRestore => pendingGlide;

        public override void CaptureItemState(ItemState state)
        {
            base.CaptureItemState(state);
            if (state == null || flight == null || !flight.IsGliding) return;

            OrnithopterFlightState live = flight.State;

            state.Set(GlideKey, new Vector3(live.Airspeed, live.Gamma, live.Heading));
            state.Set(NoseKey, live.Pitch);
        }

        public override void RestoreItemState(ItemState state)
        {
            base.RestoreItemState(state);

            pendingGlide = false;
            pendingFlight = Vector3.zero;
            pendingNose = 0f;

            if (state == null) return;

            Vector3 saved = state.GetVector3(GlideKey);
            if (saved.x <= 0f) return;

            pendingFlight = saved;
            pendingNose = state.GetFloat(NoseKey);
            pendingGlide = true;
        }

        /// <summary>
        /// Runs after every saver has restored, and more than once — so it holds its pending state
        /// until the flight component actually exists, and clears it the moment it has been spent.
        /// </summary>
        public void TryCompleteRestore()
        {
            if (!pendingGlide || flight == null) return;

            flight.Resume(pendingFlight.x, pendingFlight.y, pendingFlight.z, pendingNose);
            pendingGlide = false;
        }
    }
}
