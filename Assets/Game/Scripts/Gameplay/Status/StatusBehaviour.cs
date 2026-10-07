using System;
using UnityEngine;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// What one kind of status actually does: when it starts, every frame it runs, and when it
    /// stops.
    ///
    /// <para>
    /// Behaviour lives here rather than on <see cref="StatusReceiver"/> on purpose. Five conditions
    /// with a switch apiece is how the receiver becomes the class that knows about fire, ice, mass
    /// and AI at once; here the receiver only knows that a kind has a clock and somebody to tell.
    /// Adding a sixth condition is a new enum value, a new class and one field — and nothing in the
    /// receiver changes.
    /// </para>
    /// <para>
    /// A behaviour is a plain serializable object held by a concrete field on the receiver, not a
    /// component and not a <c>SerializeReference</c>. That gives every one of its numbers an
    /// Inspector row on the body that carries it, without a component per condition on every
    /// creature in the game and without storing a type name in a prefab.
    /// </para>
    /// <para>
    /// <b>Every hook runs on every machine.</b> The status message reaches all of them, which is
    /// the point: the flag replicates and the look is rebuilt locally from it, so nothing visual
    /// travels. A behaviour that decides something — bills damage, kills, clears itself — asks
    /// <see cref="StatusReceiver.Decides"/> first and does that part on one machine only.
    /// </para>
    /// </summary>
    [Serializable]
    public abstract class StatusBehaviour
    {
        [Tooltip("How long this condition lasts from the moment it is applied or refreshed, in " +
                 "seconds. Whatever applies the status may override it, but this is the number the " +
                 "condition itself says it is worth — the flamethrower does not get to decide how " +
                 "long a fire burns.")]
        [SerializeField] private float seconds;

        /// <summary>
        /// Subclasses pass their authored default up, because a serialized field's default is a
        /// field initialiser and the field lives here rather than in each of them.
        /// </summary>
        protected StatusBehaviour(float defaultSeconds) => seconds = defaultSeconds;

        /// <summary>Which condition this is. Must be unique across the behaviours on a receiver.</summary>
        public abstract StatusKind Kind { get; }

        /// <summary>How long an application lasts when the caller does not name a duration.</summary>
        public float DefaultSeconds => seconds;

        /// <summary>
        /// Does this condition stop the body acting at all while it runs?
        ///
        /// <para>
        /// The kind's own answer, because "helpless" is what the condition MEANS and not something
        /// each body is authored to agree with. Everything that has to know — the creature brain,
        /// the player's body hold — reads it through <see cref="StatusReceiver.Suppressed"/> every
        /// frame and writes nothing, so a body cannot be left suppressed by a condition that has
        /// already ended.
        /// </para>
        /// </summary>
        public virtual bool Suppresses => false;

        /// <summary>
        /// May this condition be applied to <paramref name="body"/> right now? Asked on the
        /// deciding machine only, before anything is announced.
        ///
        /// <para>
        /// The default answer is yes, including while the condition is already running — that is
        /// the refresh every continuous source relies on. A kind that must not be extended by the
        /// source holding it there says so here rather than in each of its callers; see
        /// <see cref="BurningStatus"/>, where a fire is worth its own duration once.
        /// </para>
        /// </summary>
        /// <param name="running">Whether the condition is already on this body.</param>
        public virtual bool CanApply(StatusReceiver body, bool running) => true;

        /// <summary>
        /// The condition just started. Not called on a refresh — a body is burning or it is not,
        /// and a jet of flame held on a target must not restart the effect fifteen times a second.
        /// </summary>
        public virtual void OnApplied(StatusReceiver body) { }

        /// <summary>Every frame the condition is running, on every machine.</summary>
        public virtual void OnTick(StatusReceiver body, float deltaTime) { }

        /// <summary>
        /// The condition stopped — expired, cleared early, or the body was torn down under it.
        /// Whatever <see cref="OnApplied"/> took, this gives back, and it must be safe to reach
        /// twice.
        /// </summary>
        public virtual void OnCleared(StatusReceiver body) { }
    }
}
