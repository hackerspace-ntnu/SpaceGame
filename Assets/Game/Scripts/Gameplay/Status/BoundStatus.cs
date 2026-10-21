using System;
using UnityEngine;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// Tied up by a lasso: helpless, with no clock — the captive works free only when LEFT ALONE. How it holds the body is
    /// <see cref="HeldStandingStatus"/>'s; the rope round it is drawn by the lasso's <c>BoundBody</c>.
    ///
    /// <para>
    /// <b>Still for <see cref="stillSecondsToEscape"/> and the ropes come off; kept moving and they never do</b> — the
    /// user's rule. A captive somebody is dragging, carrying on a jetpack or keeps nudging is not free to work at its knots;
    /// one dumped and walked away from is. Measured off the root's own position, which every machine has: the deciding
    /// machine ends the condition, every other one only reads how far along it is (<see cref="EscapeProgress"/>) to struggle
    /// harder and to show it.
    /// </para>
    /// <para>
    /// Also ended by a respawn (<c>ClearAll</c>) or a load: like every condition it is never saved.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class BoundStatus : HeldStandingStatus
    {
        /// <summary>No clock of its own: what ends a tie is stillness. A day is "until something ends it".</summary>
        private const float NoClock = 24f * 60f * 60f;

        [Tooltip("Seconds a tied body must lie still, unmoved, to work itself free.")]
        [SerializeField, Min(1f)] private float stillSecondsToEscape = 30f;

        [Tooltip("Metres a tied body must be moved for it to count as not lying still. Above physics settling and a struggle's " +
                 "rocking; below the shortest drag.")]
        [SerializeField, Min(0.01f)] private float stillTolerance = 0.3f;

        private Vector3 restingAt;
        private float stillFor;

        public BoundStatus() : base(NoClock) { }

        public override StatusKind Kind => StatusKind.Bound;

        /// <summary>How far through working free, 0 (just moved) to 1 (free), on this machine.</summary>
        public float EscapeProgress => Mathf.Clamp01(stillFor / stillSecondsToEscape);

        public override void OnApplied(StatusReceiver body)
        {
            base.OnApplied(body);
            restingAt = body.transform.position;
            stillFor = 0f;
        }

        public override void OnTick(StatusReceiver body, float deltaTime)
        {
            Vector3 at = body.transform.position;
            if ((at - restingAt).sqrMagnitude > stillTolerance * stillTolerance)
            {
                restingAt = at;
                stillFor = 0f;
                return;
            }

            stillFor += deltaTime;
            if (stillFor >= stillSecondsToEscape && body.Decides) body.Clear(StatusKind.Bound);
        }
    }
}
