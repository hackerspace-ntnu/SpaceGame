// Sails a hull round a closed loop of waypoints and moors at each one for a while — the Sky City's
// slow drift over the basin.
//
// The brain half of a drifting hull and nothing more: it decides where the hull is going and when it
// stops, and drives the motor with a MoveIntent like every other module (on the Sky City the motor is
// FlyingRigidbodyMotor in kinematic-hull mode). Whatever has to happen when the hull starts or stops
// moving — parking its residents, laying its NavMesh — reads UnderWay (SettlementDeck); nothing here
// knows about it.
//
// Arriving takes two conditions, not one: inside arriveRadius AND slower than mooredBelowSpeed. The
// motor only starts braking at the stop distance, so the hull is still sliding when it first crosses
// the radius, and declaring it moored then would lay a NavMesh under a deck that is still moving.
//
// Slow and on a fixed loop on purpose (GDC-L1-LEVEL-0002): a landmark that wanders is one the player
// cannot navigate by, so the route is learnable and the moorings make it a place for minutes at a time.
//
// Ticked by AgentController, so it only runs where the hull is simulated (the server). Its state —
// which leg, under way or not, how long is left at the mooring — is saved by DriftRouteSaveable.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.Vehicles
{
    public class DriftRouteModule : BehaviourModuleBase
    {
        [Header("Route")]
        [Tooltip("World-space waypoints, sailed in order and looped. With fewer than two the hull holds station.")]
        [SerializeField] private Vector3[] route = new Vector3[0];

        [Tooltip("Within this distance (m) of a waypoint the motor brakes, and the hull may moor.")]
        [SerializeField] private float arriveRadius = 40f;

        [Tooltip("Inside arriveRadius the hull counts as moored once it is slower than this (m/s).")]
        [SerializeField] private float mooredBelowSpeed = 0.2f;

        [Tooltip("Seconds moored at each waypoint before sailing for the next.")]
        [SerializeField] private float mooredSeconds = 120f;

        [Tooltip("Fraction of the motor's top speed to sail at.")]
        [SerializeField] private float speedMultiplier = 1f;

        // The waypoint sailed toward, or moored at.
        private int leg;
        private bool underWay;
        private float mooredRemaining;

        public bool UnderWay => underWay;
        public int Leg => leg;
        public float MooredRemaining => mooredRemaining;
        public IReadOnlyList<Vector3> Route => route;

        public override string ModuleDescription =>
            "Sails a closed loop of world waypoints and moors at each for mooredSeconds.\n\n" +
            "• route — world positions, in order, looped\n" +
            "• arriveRadius / mooredBelowSpeed — when the hull counts as moored\n" +
            "• UnderWay — read by SettlementDeck to park residents and lift the NavMesh";

        // A fresh world starts moored at its first waypoint (where the scene places the hull) with a
        // full mooring ahead of it. A load overwrites this in RestoreDrift.
        private void Awake() => mooredRemaining = mooredSeconds;

        protected override void OnValidate()
        {
            arriveRadius = Mathf.Max(0.1f, arriveRadius);
            mooredBelowSpeed = Mathf.Max(0f, mooredBelowSpeed);
            mooredSeconds = Mathf.Max(0f, mooredSeconds);
            speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
        }

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreDrift(int savedLeg, bool savedUnderWay, float savedMooredRemaining)
        {
            leg = route.Length > 0 ? Mathf.Clamp(savedLeg, 0, route.Length - 1) : 0;
            underWay = savedUnderWay;
            mooredRemaining = Mathf.Max(0f, savedMooredRemaining);
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (route == null || route.Length < 2)
                return null;

            if (!underWay)
            {
                mooredRemaining -= deltaTime;
                if (mooredRemaining > 0f)
                    return null;

                leg = (leg + 1) % route.Length;
                underWay = true;
            }

            Vector3 waypoint = route[leg];
            if (HasArrived(context.Position, context.Velocity, waypoint))
            {
                underWay = false;
                mooredRemaining = mooredSeconds;
                return null;
            }

            return MoveIntent.MoveTo(waypoint, arriveRadius, speedMultiplier);
        }

        private bool HasArrived(Vector3 position, Vector3 velocity, Vector3 waypoint) =>
            (position - waypoint).sqrMagnitude <= arriveRadius * arriveRadius &&
            velocity.sqrMagnitude <= mooredBelowSpeed * mooredBelowSpeed;
    }
}
