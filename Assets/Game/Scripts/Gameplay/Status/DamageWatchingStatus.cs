using System;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// A condition that a hit can end, or end the body it is on.
    ///
    /// <para>
    /// Two kinds want this and they want exactly the same plumbing: <see cref="FrozenStatus"/>,
    /// where a hard enough hit shatters the body, and <see cref="FoamedStatus"/>, where a hit
    /// breaks the encasement early. Both subscribe while they run, unsubscribe when they stop,
    /// ignore a restore, and act only on the deciding machine. That is the whole of the shared
    /// part, so it is written once here and the two subclasses are left with a threshold and a
    /// consequence.
    /// </para>
    /// <para>
    /// The restore check is not optional. A load writes real health values through the same path a
    /// real hit does, and a status that cannot tell them apart shatters a creature for the crime of
    /// being loaded.
    /// </para>
    /// </summary>
    [Serializable]
    public abstract class DamageWatchingStatus : StatusBehaviour
    {
        protected DamageWatchingStatus(float defaultSeconds) : base(defaultSeconds) { }

        private HealthComponent watched;
        private StatusReceiver watching;

        public override void OnApplied(StatusReceiver body)
        {
            // Unsubscribed first: OnApplied is only reached for a condition that was not running,
            // but a body whose HealthComponent was swapped underneath it would otherwise leave a
            // subscription on the old one that nothing can ever remove.
            Unwatch();

            watched = body.Health;
            if (watched == null) return;

            watching = body;
            watched.OnDamage += OnDamaged;
        }

        public override void OnCleared(StatusReceiver body) => Unwatch();

        private void Unwatch()
        {
            if (watched != null) watched.OnDamage -= OnDamaged;
            watched = null;
            watching = null;
        }

        private void OnDamaged(int amount)
        {
            if (watching == null || watched == null) return;

            // A load is not a hit. See the class comment.
            if (watched.IsRestoring) return;

            // Every machine hears its own copy of the damage, and only one of them may act on it —
            // a client that shattered a body would be deciding a death the server never agreed to
            // (GDC-L1-MP-0004). The clear that follows comes back to it as a message like everyone
            // else's.
            if (!watching.Decides) return;

            OnHit(watching, amount);
        }

        /// <summary>
        /// A real hit landed on this body, on the machine entitled to do something about it.
        /// </summary>
        protected abstract void OnHit(StatusReceiver body, int amount);
    }
}
