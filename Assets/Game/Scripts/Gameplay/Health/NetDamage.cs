// The single entry point for one thing hurting another.
//
// Damage is where "actions on each other" actually lives, and it was the one place every caller
// reached straight into the victim's local HealthComponent. That works for exactly one machine:
// the shooter's. Everybody else sees a full-health target, and the two views never reconcile.
//
// Routing every call through here makes the server the only machine that decides what a hit does,
// while NetworkedHealthComponent publishes the result. Player→AI, AI→player and player→player all
// become correct at once, because they were never actually three problems.
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;

namespace SpaceGame.Gameplay
{
    public static class NetDamage
    {
        /// <summary>
        /// Hurt <paramref name="target"/> for <paramref name="amount"/>, wherever this is called from.
        ///
        /// On the server, or offline, it lands immediately. On a client it becomes a request the
        /// server carries out. On a target nobody has networked it lands locally, quietly — a
        /// creature whose health disagrees between machines is a bug, but one that cannot be hurt
        /// at all is a worse one, and it is not the shooter's fault worth logging on every bullet.
        /// <para>
        /// <paramref name="kind"/> is what the victim's defences get to judge the hit by — a guard
        /// lifts against a <see cref="DamageKind.Melee"/> blow and never against a bullet. It
        /// travels with the request, because the machine that knows what swung is not always the
        /// one that decides.
        /// </para>
        /// </summary>
        /// <returns>
        /// How the hit was met when it was decided right here; <see cref="DamageDefense.None"/>
        /// when it was taken, and also when it went to the server as a request, since the answer
        /// is not known on this machine yet.
        /// </returns>
        public static DamageDefense Apply(GameObject target, int amount, Transform source = null,
                                          DamageKind kind = DamageKind.Unspecified)
        {
            if (target == null || amount <= 0) return DamageDefense.None;

            HealthComponent health = target.GetComponentInParent<HealthComponent>();
            if (health == null)
            {
                // Not everything damageable owns a HealthComponent — destructible props implement
                // IDamageable directly. Those are local-only by nature, so hit them and move on.
                if (target.GetComponentInParent<IDamageable>() is { } damageable && damageable.Alive)
                    damageable.Damage(amount);
                return DamageDefense.None;
            }

            if (IsRiddenBySource(health, source)) return DamageDefense.None;

            if (Network.Simulates(health)) return health.Damage(amount, source, kind);

            NetMessaging.NetSendTo(health.gameObject, NetMsg.Damage,
                new NetArg { A = amount, B = (int)kind }.With(source));
            return DamageDefense.None;
        }

        /// <summary>
        /// Is <paramref name="health"/> the mount <paramref name="source"/> is riding, or someone
        /// else seated on it? A rider never hurts what they sit on.
        ///
        /// <para>
        /// Every item already looks past the ridden mount when it picks a target
        /// (<see cref="AimProvider.IsOwnOrRidden"/>), so this is the backstop for the hits no item
        /// aimed: a blast radius, a cone, a fragment. Asked here, on the machine that dealt the
        /// hit, because the replicated parenting it reads is the same on every machine. The
        /// rider's own health is left alone — whether a rider can hurt themselves is not this
        /// rule's business.
        /// </para>
        /// </summary>
        private static bool IsRiddenBySource(HealthComponent health, Transform source)
        {
            Transform ridden = AimProvider.RiddenBy(source);
            return ridden != null && health.transform.IsChildOf(ridden)
                   && source.GetComponentInParent<HealthComponent>() != health;
        }

        /// <summary>Convenience for the common "I hit this collider" shape.</summary>
        public static DamageDefense Apply(Component target, int amount, Transform source = null,
                                          DamageKind kind = DamageKind.Unspecified) =>
            target != null ? Apply(target.gameObject, amount, source, kind) : DamageDefense.None;
    }
}
