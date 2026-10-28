// The world's ears: one call anything can make to say "a sound happened here",
// and every NoiseReceiverModule in earshot hears about it.
//
// ## Why this is a registry and not a Physics.OverlapSphere
//
// NoiseEmitter used to do its own overlap query against a hand-set layer mask,
// and that shape does not survive contact with a gun:
//
//   * A weapon has nothing to emit from. It is destroyed and rebuilt from its
//     prefab on every equip, it is not on a physics layer anyone thought about,
//     and hanging a component off each of them just to report a shot is a lot of
//     authoring for one line of gameplay.
//   * The mask defaulted to Nothing, which meant silence with one warning. That
//     is the exact failure this codebase keeps writing gotchas about — the
//     feature looks implemented and does nothing.
//   * The query found colliders and then asked each for a NoiseReceiverModule,
//     so a receiver whose collider sat on a child, or on a layer the shooter did
//     not think to tick, was deaf for reasons invisible from either end.
//
// Receivers are agents, so there are tens of them, not thousands. Walking the
// list and comparing squared distances is cheaper than the physics query it
// replaces and cannot be misconfigured. Same reasoning as EntityTargetRegistry,
// and the same reason the scavenger example in the agent skill uses a registry
// rather than an overlap.
//
// ## What this deliberately does not do
//
// **No line of sight.** A gunshot behind a dune is still a gunshot, and an
// animal that only spooks when it can see you is an animal that never spooks.
// PerceptionModule is where seeing is decided; this is hearing.
//
// **No falloff.** A noise is heard or it is not. Receivers already own their own
// reaction per NoiseType, which is the knob that actually matters.
//
// **No replication.** Noise is a server-side gameplay input, emitted on the
// machine that decided the thing happened, and read by agents that only tick on
// the machine that owns them. Emitting it on a peer would make remote copies of
// a creature react to a shot their owner never heard.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    public static class Noise
    {
        private static readonly List<NoiseReceiverModule> receivers =
            new List<NoiseReceiverModule>(64);

        // Dispatching from a snapshot, because OnNoiseHeard runs gameplay code:
        // it can hand a target to AgentTargeting, which can enable or disable
        // modules and in principle despawn something. Iterating the live list
        // while that happens is how you get a mutated-collection exception in a
        // frame nobody can reproduce.
        private static readonly List<NoiseReceiverModule> dispatch =
            new List<NoiseReceiverModule>(64);

        public static IReadOnlyList<NoiseReceiverModule> All => receivers;

        public static void Register(NoiseReceiverModule receiver)
        {
            if (receiver == null || receivers.Contains(receiver))
                return;
            receivers.Add(receiver);
        }

        public static void Unregister(NoiseReceiverModule receiver)
        {
            receivers.Remove(receiver);
        }

        /// <summary>
        /// Report a noise at <paramref name="position"/> that carries
        /// <paramref name="radius"/> metres.
        /// </summary>
        /// <param name="instigator">
        /// Who caused it — the shooter, not the bullet. Receivers configured to
        /// aggro on this noise type will target them, so pass the entity root:
        /// a projectile is gone the frame it lands and a limb collider is not
        /// something a creature can walk toward.
        /// </param>
        /// <param name="ignore">
        /// A transform that should not hear its own noise. Anything parented
        /// under it is skipped too, so a creature does not startle itself with
        /// its own footsteps.
        /// </param>
        public static void Emit(NoiseType type, Vector3 position, float radius,
                                Transform instigator = null, Transform ignore = null)
        {
            if (radius <= 0f || receivers.Count == 0)
                return;

            float radiusSqr = radius * radius;

            dispatch.Clear();
            for (int i = receivers.Count - 1; i >= 0; i--)
            {
                NoiseReceiverModule receiver = receivers[i];

                // Destroyed without OnDisable — a scene unload, mostly.
                if (receiver == null)
                {
                    receivers.RemoveAt(i);
                    continue;
                }

                if (ignore != null && receiver.transform.IsChildOf(ignore))
                    continue;

                if ((receiver.transform.position - position).sqrMagnitude > radiusSqr)
                    continue;

                dispatch.Add(receiver);
            }

            for (int i = 0; i < dispatch.Count; i++)
            {
                NoiseReceiverModule receiver = dispatch[i];
                if (receiver != null)
                    receiver.OnNoiseHeard(type, position, radius, instigator);
            }

            dispatch.Clear();
        }
    }
}
