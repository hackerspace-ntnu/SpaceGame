// Drive for the robot ostrich.
//
// Everything a legged machine's driver does -- the rider channel, the AI channel, the rider-frame
// guard, acceleration, NavMesh path following, ForceStop -- is LeggedDriver. What is left here is
// the one thing that is the bird's own: what it does when nobody is driving it.
//
// The comment this file used to carry said the shared steering wanted extracting into a common base
// once both machines were known good. Both are; this is that extraction.
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Creatures.Ostrich;

namespace SpaceGame.Creatures
{
    [RequireComponent(typeof(OstrichLocomotion))]
    public class OstrichDriver : LeggedDriver
    {
        [Header("Ostrich")]
        [Tooltip("Throttle below which the bird walks rather than runs. Only affects how fast it asks " +
                 "to go; the gait's own duty blend does the rest.")]
        [Range(0f, 1f)]
        [SerializeField] private float walkThrottle = 0.45f;

        [Tooltip("Walk forward on its own, for looking at the gait without a rider aboard. Debug only.")]
        [SerializeField] private bool autoWalk;

        /// There is deliberately no keyboard fallback: SteerModule is the one input path, and a second
        /// one polling the keyboard directly would drive the bird out from under a rider who is not
        /// touching it.
        protected override void Idle()
        {
            if (!autoWalk)
            {
                base.Idle();
                return;
            }

            forward = walkThrottle;
            turn = 0f;
        }
    }
}
