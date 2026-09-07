using System;
using UnityEngine;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// Encased: held where it stands until the foam softens, or until somebody breaks it off.
    ///
    /// <para>
    /// The same "cannot act" the Frozen condition asks for, and asked for through the same two
    /// routes — <see cref="BodyHold"/> for a player, a derived idle intent from
    /// <c>StatusReactionModule</c> for a creature. What is different is the way out: being stuck is
    /// meant to be a setback rather than a sentence, so a hit ends it early and anyone nearby can
    /// give it (GDC-L1-BAL-0004 — a bad spot the player can be helped out of is counterplay; one
    /// they can only wait out is not).
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class FoamedStatus : DamageWatchingStatus
    {
        /// <summary>Ten seconds, against the sixty a ramp of the same foam gets on the ground.</summary>
        private const float DefaultDuration = 10f;

        public FoamedStatus() : base(DefaultDuration) { }

        [Tooltip("A single hit of at least this much breaks the encasement. Deliberately low: the " +
                 "point is that a friend can free you, not that freeing you is a damage race.")]
        [SerializeField] private int breakDamage = 10;

        private readonly BodyHold hold = new BodyHold();

        public override StatusKind Kind => StatusKind.Foamed;

        public override void OnApplied(StatusReceiver body)
        {
            base.OnApplied(body);
            hold.Take(body);
        }

        public override void OnCleared(StatusReceiver body)
        {
            base.OnCleared(body);
            hold.Release();
        }

        protected override void OnHit(StatusReceiver body, int amount)
        {
            if (amount >= breakDamage) body.Clear(Kind);
        }
    }
}
