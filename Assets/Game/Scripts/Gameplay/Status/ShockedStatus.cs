using System;
using SpaceGame.Gameplay.Ragdoll;
using UnityEngine;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// Tased: the legs go, the body drops, and it lies twitching until the current stops.
    ///
    /// <para>
    /// Helpless like <see cref="FrozenStatus"/> — <see cref="Suppresses"/> starves a creature's
    /// brain through <c>AgentController</c> and takes a player's body through <see cref="BodyHold"/>
    /// — but on the ground rather than standing: a shock that leaves its victim upright reads as
    /// nothing having happened. A creature goes down through <see cref="AgentRagdoll.HoldDown"/>,
    /// the claim the net gun takes, so both kinds of body fall on every machine off the same
    /// replicated flag and get up the frame it clears.
    /// </para>
    /// <para>
    /// <b>It deals no damage and it cannot be chained.</b> Whatever delivered the shock bills its
    /// own hit; the condition is only the seconds of not playing. Those seconds are the whole price,
    /// so a body that has just got up is proof against the next one for <see cref="immunity"/>:
    /// two harpoons taking turns must not be able to keep somebody on the floor for good
    /// (GDC-L1-BAL-0004 — a setback the victim can answer, not a sentence).
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class ShockedStatus : StatusBehaviour
    {
        /// <summary>Three seconds: long enough to close the distance, too short to finish a fight in.</summary>
        private const float DefaultDuration = 3f;

        public ShockedStatus() : base(DefaultDuration) { }

        [Tooltip("Seconds after a shock ends before this body can be shocked again. Without it two " +
                 "shooters alternating could keep a victim down indefinitely.")]
        [SerializeField] private float immunity = 2f;

        private readonly BodyHold hold = new BodyHold();
        private AgentRagdoll heldCreature;

        /// <summary>When the last shock on this body ended, on the deciding machine's clock.</summary>
        private float lastEnded = float.NegativeInfinity;

        public override StatusKind Kind => StatusKind.Shocked;

        public override bool Suppresses => true;

        /// <summary>One shock at a time, and a breather between them. See the class summary.</summary>
        public override bool CanApply(StatusReceiver body, bool running) =>
            !running && Time.time - lastEnded >= immunity;

        public override void OnApplied(StatusReceiver body)
        {
            hold.Take(body);
            if (hold.HasHold) return;

            AgentRagdoll creature = body.GetComponentInParent<AgentRagdoll>();
            if (creature != null && creature.HoldDown(this)) heldCreature = creature;
        }

        public override void OnCleared(StatusReceiver body)
        {
            lastEnded = Time.time;
            hold.Release();

            if (heldCreature == null) return;
            heldCreature.ReleaseHold(this);
            heldCreature = null;
        }
    }
}
