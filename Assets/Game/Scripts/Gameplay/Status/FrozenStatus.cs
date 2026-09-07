using System;
using UnityEngine;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// Frozen solid: helpless while it lasts, and brittle while it lasts.
    ///
    /// <para>
    /// <b>The suppression is derived, never written.</b> Nothing here disables a motor, a
    /// NavMeshAgent or a behaviour module. A creature is helpless because
    /// <c>StatusReactionModule</c> reads this flag every frame and starves everything below it, and
    /// the frame the flag goes away the creature moves again with nothing to restore. The
    /// alternative — switching the brain off — is a world save capturing a switched-off brain and a
    /// creature that reloads frozen forever with a clean console.
    /// </para>
    /// <para>
    /// A player has no behaviour module to starve, so they are held by
    /// <see cref="BodyHold"/> — see there for why that is a different question with a different
    /// answer.
    /// </para>
    /// <para>
    /// The pose is not this class's business. A frozen body reads as a statue because the Cryo
    /// Sprayer swaps in one posed from the live body; the condition itself only says the body
    /// cannot act and can be shattered.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class FrozenStatus : DamageWatchingStatus
    {
        /// <summary>Ten seconds, and it is not refreshed by being sprayed harder.</summary>
        private const float DefaultDuration = 10f;

        public FrozenStatus() : base(DefaultDuration) { }

        [Tooltip("A single hit of at least this much shatters the body and kills it outright. " +
                 "Below it the hit is an ordinary hit and the ice holds — the threshold is what " +
                 "makes freezing an execution SETUP rather than an execution.")]
        [SerializeField] private int shatterDamage = 25;

        private readonly BodyHold hold = new BodyHold();

        public override StatusKind Kind => StatusKind.Frozen;

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
            if (amount < shatterDamage) return;

            HealthComponent health = body.Health;
            if (health == null || !health.Alive) return;

            Transform killer = health.LastDamageSource;

            // Cleared BEFORE the killing blow, not after. A corpse is not frozen either way, but
            // the blow below raises the very damage event this method is standing in — so ending
            // the condition first is what takes this class off the invocation list rather than
            // relying on the health check to stop the second pass.
            body.Clear(Kind);

            // Through the ordinary damage path rather than a special delete, so the death that
            // follows is the death every other system already knows how to react to — loot, the
            // authoritative dead flag, the ragdoll, the save. A body deleted around the side of all
            // that is a body those systems disagree about.
            NetDamage.Apply(body.gameObject, health.GetHealth, killer);
        }
    }
}
