// Flying a station on something that moves: a wingman on its leader's craft, a war-party flier on its
// airship, a resident circling its drifting city. Pure — no Transform reads, no clock, no physics — so the
// station-keeping is a unit test; NpcAviator feeds it the anchor's pose and velocity each tick.
//
// The station is an offset in the anchor's YAW frame (x right, y up, z forward, the anchor's forward
// flattened): a leader banking through a turn or pitching into a climb must not swing its wingmen up and
// down. An orbit station turns that offset about the anchor's up axis at a fixed rate. On top, each flier
// drifts about its station on a slow Perlin of its own seed, so a formation reads as several fliers and
// not one rigid model (GDC-L1-ANIM-0005).
//
// Speed is matched, not merely chased: the flier asks for the station's own speed plus catchUpGain per
// metre it lags along the station's travel (less when it is ahead), so it sits ON its station at
// equilibrium rather than trailing it by speed ÷ gain.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.Vehicles
{
    /// <summary>Where a flier keeps station: an anchor, an offset in its yaw frame, an orbit rate, a drift seed.</summary>
    public readonly struct FlightStation
    {
        /// <summary>What the station moves with: a leader's craft, an airship hull, a city. Any Transform.</summary>
        public readonly Transform Anchor;

        /// <summary>Metres in the anchor's yaw frame at orbit angle zero: x right, y up, z forward.</summary>
        public readonly Vector3 Offset;

        /// <summary>Degrees per second the offset turns about the anchor's up axis; 0 for a fixed station.</summary>
        public readonly float OrbitDegreesPerSecond;

        /// <summary>This flier's own drift; stable for the flight (a member index, never a frame count).</summary>
        public readonly int Seed;

        public FlightStation(Transform anchor, Vector3 offset, float orbitDegreesPerSecond, int seed)
        {
            Anchor = anchor;
            Offset = offset;
            OrbitDegreesPerSecond = orbitDegreesPerSecond;
            Seed = seed;
        }

        public bool Orbits => !Mathf.Approximately(OrbitDegreesPerSecond, 0f);

        public static FlightStation Fixed(Transform anchor, Vector3 offset, int seed) =>
            new FlightStation(anchor, offset, 0f, seed);

        /// <summary>A circle of <paramref name="radius"/> at <paramref name="height"/> over the anchor, starting at <paramref name="phaseDegrees"/> (0 = dead ahead).</summary>
        public static FlightStation Orbit(Transform anchor, float radius, float height, float degreesPerSecond,
                                          float phaseDegrees, int seed)
        {
            float phase = phaseDegrees * Mathf.Deg2Rad;
            var offset = new Vector3(Mathf.Sin(phase) * radius, height, Mathf.Cos(phase) * radius);
            return new FlightStation(anchor, offset, degreesPerSecond, seed);
        }
    }

    [Serializable]
    public class EscortSettings
    {
        [Tooltip("Extra speed asked for per metre the flier lags its station along the station's travel, m/s per m.")]
        [Min(0.01f)] public float catchUpGain = 0.15f;

        [Tooltip("How far ahead (s) along the station's own motion the flier aims, so it turns with its station.")]
        [Min(0f)] public float leadSeconds = 1.5f;

        [Tooltip("Slowest the flier goes while ahead of its station, 0..1 of its top speed — never a hover.")]
        [Range(0.05f, 1f)] public float minSpeed = 0.35f;

        [Tooltip("Fastest it goes catching up, 0..1 of its top speed.")]
        [Range(0.05f, 1f)] public float maxSpeed = 1f;

        [Tooltip("Another flier keeping station closer than this pushes this one away, metres.")]
        [Min(0f)] public float separationRadius = 9f;

        [Tooltip("Never aim lower than this above the ground under the flier, metres — a wingman below its " +
                 "leader must not meet the ridge the leader cleared.")]
        [Min(0f)] public float minClearance = 25f;

        [Tooltip("Within this of a fixed station the flier holds the anchor's heading instead of facing its " +
                 "travel, metres. Orbits always face their travel.")]
        [Min(0f)] public float facingDistance = 30f;

        [Tooltip("How far a flier drifts about its station on its own slow Perlin, metres.")]
        [Min(0f)] public float driftAmplitude = 2.5f;

        [Tooltip("How fast that drift wanders, cycles per second-ish.")]
        [Min(0f)] public float driftRate = 0.05f;

        [Tooltip("Seconds over which an anchor with no motor of its own has its velocity read off its motion.")]
        [Min(0.01f)] public float anchorVelocitySmoothing = 0.3f;
    }

    /// <summary>One tick's order: aim here at this share of top speed, optionally holding a heading.</summary>
    public readonly struct EscortStep
    {
        public readonly Vector3 Target;
        public readonly float Speed;
        public readonly bool HoldsHeading;
        public readonly Vector3 Heading;

        public EscortStep(Vector3 target, float speed, bool holdsHeading, Vector3 heading)
        {
            Target = target;
            Speed = speed;
            HoldsHeading = holdsHeading;
            Heading = heading;
        }
    }

    /// <summary>The anchor as seen this tick.</summary>
    public readonly struct AnchorState
    {
        public readonly Vector3 Position;
        public readonly Vector3 Forward;
        public readonly Vector3 Velocity;

        public AnchorState(Vector3 position, Vector3 forward, Vector3 velocity)
        {
            Position = position;
            Forward = forward;
            Velocity = velocity;
        }
    }

    public static class EscortSteering
    {
        // Below this a flat vector names no bearing; below this speed a station has no travel direction.
        private const float MinFlatSqr = 1e-4f;
        private const float StillSpeed = 0.1f;

        // Salts that keep the two drift axes and the station's personal phase apart.
        private const int DriftPhaseSalt = 53;
        private const float DriftPhaseRange = 100f;

        /// <summary>The anchor's forward flattened onto the ground plane; +Z when it points straight up or down.</summary>
        public static Vector3 YawForward(Vector3 forward)
        {
            forward.y = 0f;
            return forward.sqrMagnitude > MinFlatSqr ? forward.normalized : Vector3.forward;
        }

        /// <summary>The station's offset at <paramref name="time"/>: turned by the orbit about the anchor's up axis.</summary>
        public static Vector3 OffsetAt(in FlightStation station, float time) =>
            station.Orbits ? Quaternion.Euler(0f, station.OrbitDegreesPerSecond * time, 0f) * station.Offset : station.Offset;

        /// <summary>The world point of <paramref name="offset"/> in the yaw frame of an anchor at <paramref name="anchorPosition"/>, plus the seed's drift.</summary>
        public static Vector3 StationPoint(Vector3 anchorPosition, Vector3 anchorForward, Vector3 offset, int seed,
                                           EscortSettings settings, float time)
        {
            Quaternion yaw = Quaternion.LookRotation(YawForward(anchorForward), Vector3.up);
            return anchorPosition + yaw * offset + Drift(seed, settings, time);
        }

        /// <summary>A slow wander of up to driftAmplitude on each axis, its own per seed.</summary>
        public static Vector3 Drift(int seed, EscortSettings settings, float time)
        {
            if (settings.driftAmplitude <= 0f) return Vector3.zero;

            float phase = FormationMath.Hash01(seed, DriftPhaseSalt) * DriftPhaseRange;
            float t = time * settings.driftRate + phase;
            return new Vector3(Wave(t, 0.5f),
                               Wave(0.5f, t),
                               Wave(t, t + 0.5f)) * (2f * settings.driftAmplitude);
        }

        // Perlin centred on zero, -0.5..0.5. Unity's Perlin strays a tenth or more past 0..1, which would carry a
        // flier past its stated driftAmplitude.
        private static float Wave(float x, float y) => Mathf.Clamp01(Mathf.PerlinNoise(x, y)) - 0.5f;

        /// <summary>
        /// A push away from every neighbour closer than <paramref name="radius"/>, growing linearly to
        /// <paramref name="radius"/> metres at contact. The flier's own position (distance zero) is skipped.
        /// </summary>
        public static Vector3 Separation(Vector3 position, IReadOnlyList<Vector3> neighbours, float radius)
        {
            Vector3 push = Vector3.zero;
            if (neighbours == null || radius <= 0f) return push;

            foreach (Vector3 other in neighbours)
            {
                Vector3 away = position - other;
                float distance = away.magnitude;
                if (distance <= Mathf.Epsilon || distance >= radius) continue;
                push += away / distance * (radius - distance);
            }

            return push;
        }

        /// <summary>
        /// The order for a flier at <paramref name="position"/> keeping <paramref name="station"/> on
        /// <paramref name="anchor"/> at <paramref name="time"/>, over ground at <paramref name="groundBelow"/>.
        /// </summary>
        public static EscortStep Step(Vector3 position, in AnchorState anchor, in FlightStation station,
                                      EscortSettings settings, float topSpeed, float groundBelow, float time,
                                      IReadOnlyList<Vector3> neighbours)
        {
            float lead = settings.leadSeconds;
            Vector3 now = StationPoint(anchor.Position, anchor.Forward, OffsetAt(station, time), station.Seed, settings, time);

            // The station's own velocity: the anchor's, plus the orbit's sweep. Differenced over the lead
            // (never zero: an orbit seen at one instant has no travel to read).
            float span = Mathf.Max(lead, StationSpan);
            Vector3 later = StationPoint(anchor.Position, anchor.Forward, OffsetAt(station, time + span), station.Seed,
                                         settings, time + span);
            Vector3 stationVelocity = anchor.Velocity + (later - now) / span;

            Vector3 target = StationKeeping.Led(now, stationVelocity, lead);
            target.y = Mathf.Max(target.y, groundBelow + settings.minClearance);
            target += Separation(position, neighbours, settings.separationRadius);

            float speed = MatchedSpeed(now - position, stationVelocity, settings.catchUpGain);
            float fraction = StationKeeping.SpeedFraction(speed, 1f, topSpeed, settings.minSpeed, settings.maxSpeed);

            Vector3 heading = YawForward(anchor.Forward);
            bool holds = !station.Orbits && Vector3.Distance(position, now) <= settings.facingDistance;
            return new EscortStep(target, fraction, holds, heading);
        }

        /// <summary>
        /// Speed to hold a station <paramref name="error"/> away that moves at <paramref name="stationVelocity"/>:
        /// its speed, plus <paramref name="gain"/> per metre of lag along its travel (minus while ahead). A
        /// station standing still is simply flown at, <paramref name="gain"/> per metre off.
        /// </summary>
        public static float MatchedSpeed(Vector3 error, Vector3 stationVelocity, float gain)
        {
            float stationSpeed = stationVelocity.magnitude;
            if (stationSpeed < StillSpeed) return error.magnitude * gain;

            float along = Vector3.Dot(error, stationVelocity / stationSpeed);
            return Mathf.Max(0f, stationSpeed + along * gain);
        }

        // Seconds over which a station's own sweep is differenced when it has no lead to use.
        private const float StationSpan = 0.5f;
    }
}
