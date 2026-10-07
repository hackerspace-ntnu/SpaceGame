using System;
using SpaceGame.Core.Persistence;
using UnityEngine;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// Pumped up — or pumped down. One signed scalar drives size and weight together.
    ///
    /// <para>
    /// The scalar runs −1 to +1. Positive is bigger and lighter, which floats; negative is smaller
    /// and heavier, which is the ballast half of the same property. Building the property rather
    /// than the item is what makes the second item a sign rather than a second implementation
    /// (GDC-L1-SYS-0005 — the two would otherwise be one system built twice).
    /// </para>
    /// <para>
    /// <b>Deflation is the clock, not a second variable.</b> What is presented is the replicated
    /// magnitude scaled by how much of the condition's own time is left, so a pump that keeps
    /// refreshing the status holds the body at size, and the moment it stops the body eases back
    /// down over the remaining seconds and clears at its authored size. Every machine derives the
    /// same number from the same three replicated facts — kind, magnitude, expiry — so nothing
    /// about the swelling goes on the wire per frame.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class InflatedStatus : StatusBehaviour
    {
        /// <summary>
        /// How long a body takes to come back down once nobody is pumping it — twice the three
        /// seconds it takes to fill, so deflation is visibly the slower half. It is also the
        /// condition's whole duration, because a body only stays big while the pump keeps
        /// refreshing it.
        /// </summary>
        private const float DefaultDuration = 6f;

        public InflatedStatus() : base(DefaultDuration) { }

        [Tooltip("Size at a scalar of +1, as a multiple of the body's authored scale.")]
        [SerializeField] private float scaleAtFull = 2.5f;

        [Tooltip("Size at a scalar of -1. The ballast half of the same property.")]
        [SerializeField] private float scaleAtEmpty = 0.5f;

        [Tooltip("Mass at a scalar of +1, as a share of the body's authored mass. Well under the " +
                 "volume it is carrying, which is what makes a fully pumped body buoyant — and " +
                 "buoyant here has to beat a gravity of 18, not 9.81.")]
        [SerializeField] private float massAtFull = 0.15f;

        [Tooltip("Mass at a scalar of -1, as a share of the body's authored mass.")]
        [SerializeField] private float massAtEmpty = 4f;

        private Transform scaled;
        private Vector3 authoredScale;
        private Rigidbody weighted;
        private float authoredMass;

        /// <summary>Logged once per body rather than once per pump. See <see cref="WarnIfSaved"/>.</summary>
        private bool warnedAboutSavedScale;

        public override StatusKind Kind => StatusKind.Inflated;

        public override void OnApplied(StatusReceiver body)
        {
            scaled = body.InflationTarget;
            authoredScale = scaled != null ? scaled.localScale : Vector3.one;

            weighted = body.GetComponentInParent<Rigidbody>();
            authoredMass = weighted != null ? weighted.mass : 0f;

            WarnIfSaved(body);
        }

        public override void OnTick(StatusReceiver body, float deltaTime)
        {
            // Derived from the replicated pair every frame rather than integrated from it, so a
            // machine that joined halfway through, or missed a tick, or paused, lands on exactly
            // the same size as everyone else.
            float scalar = Mathf.Clamp(body.MagnitudeOf(Kind), -1f, 1f) * body.RemainingShare(Kind);

            if (scaled != null)
                scaled.localScale = authoredScale * Curve(scalar, scaleAtEmpty, scaleAtFull);

            if (weighted != null)
                weighted.mass = authoredMass * Curve(scalar, massAtEmpty, massAtFull);
        }

        public override void OnCleared(StatusReceiver body)
        {
            if (scaled != null) scaled.localScale = authoredScale;
            if (weighted != null) weighted.mass = authoredMass;

            scaled = null;
            weighted = null;
        }

        /// <summary>
        /// The value of a property at <paramref name="scalar"/>, given what it is at each end.
        /// Straight lines through 1 at zero: the interesting shape is in the two ends being
        /// authored independently, not in a curve between them.
        /// </summary>
        private static float Curve(float scalar, float atEmpty, float atFull) =>
            scalar >= 0f
                ? Mathf.Lerp(1f, atFull, scalar)
                : Mathf.Lerp(1f, atEmpty, -scalar);

        /// <summary>
        /// Say so, once, when this is about to scale something a save file reads.
        ///
        /// <para>
        /// The world save writes every saveable entity's ROOT scale unconditionally, so a quicksave
        /// taken while a creature is inflated would load that creature permanently at 2.5x, with
        /// nothing in the console and no way for it to shrink again — the condition itself is not
        /// saved and would not be there to undo it. Pointing the receiver's inflation target at a
        /// model child instead keeps the swelling out of the record entirely. A player or a loose
        /// prop has no such record and is left alone.
        /// </para>
        /// </summary>
        private void WarnIfSaved(StatusReceiver body)
        {
            if (warnedAboutSavedScale || scaled == null) return;

            var entity = body.GetComponentInParent<SaveableEntity>();
            if (entity == null || entity.transform != scaled) return;

            warnedAboutSavedScale = true;
            Debug.LogWarning(
                "[Inflated] " + body.name + " is a saved entity and is being inflated by its own " +
                "root scale, which the world save records. A save taken while it is pumped would " +
                "load it inflated forever. Point StatusReceiver.inflationTarget at a model child.",
                body);
        }
    }
}
