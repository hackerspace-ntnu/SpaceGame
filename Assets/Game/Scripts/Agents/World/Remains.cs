// What a dead NPC leaves behind -- its body, and the loot it shed beside it -- lying where it fell for
// a while before the world takes it away (the user's call, 2026-10-05: "let the body lay there, and
// the loot, then let it despawn after a little while").
//
// Started on the server by whoever made the remains: HealthReactionModule for a body (corpseLifetime),
// EntityLootTable for each item it drops (lootLifetime). Never on an item a player drops, and an item
// a player picks up is despawned by the pickup and takes its countdown with it.
//
// Once the lifetime is up it goes the first moment no player is within `unseenDistance`
// (UnseenRemoval, the rule AbandonedVehicle uses), through GameServices.World.Despawn: a network
// despawn, so every machine loses it together, and a tombstone for a body that was authored into a
// chunk scene, so the chunk does not put it back.
//
// The countdown is saved (RemainsSaveable), so a save/quit/load -- or its chunk unloading -- pauses
// it rather than restarting it.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public sealed class Remains : MonoBehaviour
    {
        [Tooltip("Once its lifetime is up it goes the first moment no player is within this distance " +
                 "(metres, flat), so nobody watches it blink out.")]
        [SerializeField] private float unseenDistance = 150f;

        /// <summary>Whether it is counting down to being taken away.</summary>
        public bool Counting { get; private set; }

        /// <summary>Seconds left before it may be taken away. Meaningful only while <see cref="Counting"/>.</summary>
        public float Remaining { get; private set; }

        private readonly List<Vector3> players = new();

        /// <summary>The <see cref="Remains"/> on <paramref name="target"/>, added if it has none.</summary>
        public static Remains On(GameObject target) =>
            target.TryGetComponent(out Remains remains) ? remains : target.AddComponent<Remains>();

        /// <summary>Count down from <paramref name="lifetime"/> seconds. Also how a load puts back a saved countdown.</summary>
        public void Begin(float lifetime)
        {
            Counting = true;
            Remaining = Mathf.Max(0f, lifetime);
        }

        /// <summary>
        /// <see cref="Begin"/>, unless a countdown is already running. For a death replayed by a load,
        /// which must not restart the countdown the saver may already have put back.
        /// </summary>
        public void BeginUnlessCounting(float lifetime)
        {
            if (!Counting) Begin(lifetime);
        }

        /// <summary>No longer remains: brought back to life.</summary>
        public void Stop() => Counting = false;

        private void Update()
        {
            if (!Network.Decides) return;
            Tick(Time.deltaTime);
        }

        /// <summary>One server step. Public so the rule can be tested without the player loop.</summary>
        public void Tick(float delta)
        {
            if (!Counting) return;

            Remaining = Mathf.Max(0f, Remaining - delta);
            if (!UnseenRemoval.IsDue(Remaining, transform.position, unseenDistance, players)) return;

            Counting = false;
            GameServices.World.Despawn(gameObject);
        }
    }
}
