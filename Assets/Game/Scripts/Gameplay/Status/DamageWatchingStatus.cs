using System;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// A condition that a hit can end, or end the body it is on.
    ///
    /// <para>
    /// <see cref="FoamedStatus"/> wants it, where a hit breaks the encasement early, and so does
    /// anything added later that a hit must end. It subscribes while it runs, unsubscribes when it
    /// stops, ignores a restore, and acts only on the deciding machine. That is the whole of the
    /// shared part, so it is written once here and a subclass is left with a threshold and a
    /// consequence. <see cref="FrozenStatus"/> deliberately does NOT use it: a freeze deals no
    /// damage and ends on its own clock alone.
    /// </para>
    /// <para>
    /// The restore check is not optional. A load writes real health values through the same path a
    /// real hit does, and a status that cannot tell them apart breaks itself off a creature for the
    /// crime of being loaded.
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
            // a client that ended a condition on its own would be deciding world state the server
            // never agreed to (GDC-L1-MP-0004). The clear that follows comes back to it as a
            // message like everyone else's.
            if (!watching.Decides) return;

            OnHit(watching, amount);
        }

        /// <summary>
        /// A real hit landed on this body, on the machine entitled to do something about it.
        /// </summary>
        protected abstract void OnHit(StatusReceiver body, int amount);
    }
}
