using System;
using UnityEngine;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// On fire: damage on its own clock, for five seconds and then out.
    ///
    /// <para>
    /// The item that lit the body does not bill the damage. That is the whole reason this system
    /// exists — a flamethrower sweeping a cone at 15 Hz would otherwise charge a target once per
    /// tick and once per overlapping cone, and a second fire source would charge it again. Here the
    /// fire is a fact about the body, billed once per second by the one machine entitled to decide
    /// it, whoever lit it and however many times they light it again.
    /// </para>
    /// <para>
    /// Panic is not here either: this raises no flee and knows nothing about creatures. The receiver
    /// announces the condition and <c>StatusReactionModule</c> on the agent decides what a creature
    /// does about it (GDC-L1-ARCH-0003 — the sender announces, it does not call).
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class BurningStatus : StatusBehaviour
    {
        /// <summary>Five seconds from the moment a body catches, and not a second more.</summary>
        private const float DefaultDuration = 5f;

        public BurningStatus() : base(DefaultDuration) { }

        [Tooltip("Health per second while burning. Over the full duration this is what a fire is " +
                 "worth in total, which is the number to tune against a creature's health rather " +
                 "than against the item that lit it.")]
        [SerializeField] private float damagePerSecond = 4f;

        [Tooltip("How long a body is proof against catching again after a fire on it goes out, " +
                 "in seconds. Without it a creature standing in a patch of flame relights on the " +
                 "frame its own fire expires and burns for as long as it stands there.")]
        [SerializeField] private float reigniteDelay = 3f;

        /// <summary>
        /// When the last fire on this body went out, on the deciding machine's clock. Negative
        /// infinity so a body that has never burned is not serving a cooldown from time zero.
        /// </summary>
        private float lastOut = float.NegativeInfinity;

        /// <summary>
        /// Damage owed but not yet whole. Damage is an integer here, so a 4/s fire on a 60 Hz frame
        /// would round to zero every frame and cost nothing at all; this banks the fractions and
        /// spends them as they add up to a point.
        /// </summary>
        private float owed;

        public override StatusKind Kind => StatusKind.Burning;

        /// <summary>
        /// A fire burns for its own five seconds and cannot be topped up.
        ///
        /// <para>
        /// The flamethrower's cone announces this fifteen times a second and every patch of ground
        /// fire announces it again, so a refreshing fire is one that lasts exactly as long as
        /// somebody keeps the flame on it — a creature standing in a burning patch would never stop
        /// burning. Refusing the refresh makes the five seconds the whole of it, and the cooldown
        /// is what stops the next frame's announcement from simply starting it over.
        /// </para>
        /// </summary>
        public override bool CanApply(StatusReceiver body, bool running) =>
            !running && Time.time - lastOut >= reigniteDelay;

        public override void OnApplied(StatusReceiver body) => owed = 0f;

        public override void OnCleared(StatusReceiver body) => lastOut = Time.time;

        public override void OnTick(StatusReceiver body, float deltaTime)
        {
            // Clients draw flames; they never subtract health. Damage is the server's, like every
            // other contested outcome in this game (GDC-L1-MP-0004).
            if (!body.Decides) return;

            HealthComponent health = body.Health;

            // A corpse does not burn. Without this the fire goes on billing a dead body until its
            // clock runs out, which reads as loot and death effects firing late for no reason.
            if (health != null && !health.Alive)
            {
                body.Clear(Kind);
                return;
            }

            owed += damagePerSecond * deltaTime;

            int whole = Mathf.FloorToInt(owed);
            if (whole <= 0) return;

            owed -= whole;

            // Attributed to whoever lit it. Not cosmetic: the source is what ProvocationModule
            // reads to decide who a peaceful creature is now afraid of, which is what turns a
            // burning animal into one that runs away from the player holding the flamethrower.
            NetDamage.Apply(body.gameObject, whole, body.SourceOf(Kind));
        }
    }
}
