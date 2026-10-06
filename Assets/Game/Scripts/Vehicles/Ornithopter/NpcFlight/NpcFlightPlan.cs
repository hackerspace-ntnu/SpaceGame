// Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight/NpcFlightPlan.cs
// Where an NPC-flown ornithopter should be heading next: a point to fly at and how fast. Pure — no
// Transform, no clock, no physics — and deliberately NOT the energy model the player flies (user
// decision 2026-10-06: an NPC "just flies"). NpcAviator turns each step into a MoveIntent for
// FlyingRigidbodyMotor; NpcFlightPlanTests fly it against a stand-in for that motor.
//
// En route the craft climbs (or sinks) toward cruiseHeight over the ground under it, at most ClimbSlope
// up or DescentSlope down, aiming LookAhead metres along the line to the landing. When the landing is
// within FlareDistance, or the descent to it has become ApproachSlope, it approaches: straight at the
// touchdown point, slowing to FlareSpeed inside FlareDistance. If that line would be steeper than
// MaxApproachSlope it spirals down around the landing first. A wreck spirals down at WreckSlope around
// the point where its pilot died.
using System;
using UnityEngine;

namespace SpaceGame.Vehicles.Ornithopter
{
    public enum NpcFlightPhase { Idle, EnRoute, Approach, Landed, Wreck }

    [Serializable]
    public class NpcFlightSettings
    {
        [Tooltip("How far along the route the craft aims while en route, metres.")]
        [Min(1f)] public float LookAhead = 80f;

        [Tooltip("Steepest climb en route, degrees.")]
        [Range(1f, 60f)] public float ClimbSlope = 25f;

        [Tooltip("Steepest descent en route, degrees — off the Sky City it sinks to cruise height at this.")]
        [Range(1f, 60f)] public float DescentSlope = 15f;

        [Tooltip("Descent angle to the landing at which the approach begins, degrees.")]
        [Range(1f, 45f)] public float ApproachSlope = 12f;

        [Tooltip("Steepest straight approach, degrees. Steeper than this, it spirals down first.")]
        [Range(5f, 70f)] public float MaxApproachSlope = 35f;

        [Tooltip("Inside this distance of the touchdown point it slows to FlareSpeed, metres.")]
        [Min(1f)] public float FlareDistance = 45f;

        [Tooltip("Speed through the flare, 0..1 of cruise speed.")]
        [Range(0.05f, 1f)] public float FlareSpeed = 0.25f;

        [Tooltip("Speed while spiralling down to a landing, 0..1 of cruise speed.")]
        [Range(0.05f, 1f)] public float SpiralSpeed = 0.4f;

        [Tooltip("Radius of a spiral descent (a landing arrived at too high, and a wreck), metres.")]
        [Min(1f)] public float SpiralRadius = 25f;

        [Tooltip("How far round the spiral the craft aims ahead of where it is, degrees.")]
        [Range(5f, 90f)] public float SpiralLead = 40f;

        [Tooltip("Height of the craft's origin (the cradle) above the ground when it is down, metres — " +
                 "the player's craft's landing probe distance.")]
        [Min(0.1f)] public float TouchdownHeight = 1.4f;

        [Tooltip("Slack on TouchdownHeight before it counts as down, metres.")]
        [Min(0.05f)] public float TouchdownTolerance = 0.6f;

        [Tooltip("Height above the touchdown band the craft must climb past before it can land, metres — " +
                 "a craft still resting where it started has not flown, so it cannot have arrived.")]
        [Min(0.5f)] public float TakeOffClearance = 3f;

        [Tooltip("Flat distance from the landing within which reaching touchdown height counts as landed, metres.")]
        [Min(0.5f)] public float LandingTolerance = 5f;

        [Tooltip("How steeply a wreck spirals in, degrees.")]
        [Range(5f, 85f)] public float WreckSlope = 40f;
    }

    public readonly struct NpcFlightStep
    {
        public readonly Vector3 Target;
        public readonly float Speed;
        public readonly bool Touchdown;

        public NpcFlightStep(Vector3 target, float speed, bool touchdown)
        {
            Target = target;
            Speed = speed;
            Touchdown = touchdown;
        }
    }

    public sealed class NpcFlightPlan
    {
        // Below this a flat vector names no bearing.
        private const float MinFlat = 0.01f;

        private readonly NpcFlightSettings s;
        private Vector3 wreckCentre;
        private bool airborne;

        public NpcFlightPlan(NpcFlightSettings settings)
        {
            s = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public NpcFlightPhase Phase { get; private set; }

        public void Begin()
        {
            airborne = false;
            Phase = NpcFlightPhase.EnRoute;
        }

        /// <summary>Nobody is flying it any more: spiral in around <paramref name="at"/>.</summary>
        public void Wreck(Vector3 at)
        {
            wreckCentre = at;
            Phase = NpcFlightPhase.Wreck;
        }

        /// <param name="groundBelow">World height of the ground straight under the craft.</param>
        /// <param name="landing">Where to set down, on the ground.</param>
        public NpcFlightStep Step(Vector3 position, float groundBelow, Vector3 landing, float cruiseHeight)
        {
            if (Phase == NpcFlightPhase.Idle || Phase == NpcFlightPhase.Landed)
                return new NpcFlightStep(position, 0f, false);

            float aboveGround = position.y - groundBelow;
            float touchdownBand = s.TouchdownHeight + s.TouchdownTolerance;
            bool down = aboveGround <= touchdownBand;
            airborne |= aboveGround > touchdownBand + s.TakeOffClearance;

            if (Phase == NpcFlightPhase.Wreck)
                return down ? Land(position) : new NpcFlightStep(Spiral(wreckCentre, position, s.WreckSlope), 1f, false);

            Vector3 touchdown = landing + Vector3.up * s.TouchdownHeight;
            Vector3 toLanding = touchdown - position;
            toLanding.y = 0f;
            float flat = toLanding.magnitude;
            float above = position.y - touchdown.y;

            if (Phase == NpcFlightPhase.EnRoute && airborne &&
                (flat <= s.FlareDistance || (above > 0f && flat <= above / Tan(s.ApproachSlope))))
                Phase = NpcFlightPhase.Approach;

            if (Phase == NpcFlightPhase.EnRoute)
            {
                Vector3 ahead = flat > s.LookAhead
                    ? position + toLanding / flat * s.LookAhead
                    : new Vector3(touchdown.x, position.y, touchdown.z);
                float rise = groundBelow + cruiseHeight - position.y;
                ahead.y = position.y + Mathf.Clamp(rise, -Tan(s.DescentSlope) * s.LookAhead, Tan(s.ClimbSlope) * s.LookAhead);
                return new NpcFlightStep(ahead, 1f, false);
            }

            if (down || (above <= s.TouchdownTolerance && flat <= s.LandingTolerance))
                return Land(position);

            float slope = Mathf.Atan2(above, Mathf.Max(flat, MinFlat)) * Mathf.Rad2Deg;
            if (above > 0f && slope > s.MaxApproachSlope)
                return new NpcFlightStep(Spiral(touchdown, position, s.MaxApproachSlope), s.SpiralSpeed, false);

            float distance = Vector3.Distance(position, touchdown);
            return new NpcFlightStep(touchdown, distance <= s.FlareDistance ? s.FlareSpeed : 1f, false);
        }

        private NpcFlightStep Land(Vector3 position)
        {
            Phase = NpcFlightPhase.Landed;
            return new NpcFlightStep(position, 0f, true);
        }

        /// <summary>The next point round a circle of SpiralRadius about <paramref name="centre"/>, sunk by <paramref name="slopeDegrees"/>.</summary>
        private Vector3 Spiral(Vector3 centre, Vector3 position, float slopeDegrees)
        {
            Vector3 fromCentre = position - centre;
            fromCentre.y = 0f;
            float bearing = fromCentre.sqrMagnitude > MinFlat * MinFlat
                ? Mathf.Atan2(fromCentre.x, fromCentre.z) * Mathf.Rad2Deg
                : 0f;
            float next = (bearing + s.SpiralLead) * Mathf.Deg2Rad;

            Vector3 point = centre + new Vector3(Mathf.Sin(next), 0f, Mathf.Cos(next)) * s.SpiralRadius;
            Vector3 chord = point - position;
            chord.y = 0f;
            point.y = position.y - Tan(slopeDegrees) * chord.magnitude;
            return point;
        }

        private static float Tan(float degrees) => Mathf.Tan(degrees * Mathf.Deg2Rad);
    }
}
