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
        /// </summary>
        public static void Apply(GameObject target, int amount, Transform source = null)
        {
            if (target == null || amount <= 0) return;

            HealthComponent health = target.GetComponentInParent<HealthComponent>();
            if (health == null)
            {
                // Not everything damageable owns a HealthComponent — destructible props implement
                // IDamageable directly. Those are local-only by nature, so hit them and move on.
                if (target.GetComponentInParent<IDamageable>() is { } damageable && damageable.Alive)
                    damageable.Damage(amount);
                return;
            }

            if (Network.Simulates(health))
            {
                health.Damage(amount, source);
                return;
            }

            NetMessaging.NetSendTo(health.gameObject, NetMsg.Damage,
                new NetArg { A = amount }.With(source));
        }

        /// <summary>Convenience for the common "I hit this collider" shape.</summary>
        public static void Apply(Component target, int amount, Transform source = null)
        {
            if (target != null) Apply(target.gameObject, amount, source);
        }
    }
}
