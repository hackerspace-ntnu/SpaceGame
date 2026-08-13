using System;
using UnityEngine;

namespace SpaceGame.Vehicles.Ornithopter
{
    /// <summary>
    /// Everything the flight model carries between steps. A plain struct so a test can build one,
    /// step it a thousand times and assert on the result without a scene existing.
    /// </summary>
    [Serializable]
    public struct OrnithopterFlightState
    {
        /// <summary>Speed along the flight path, m/s.</summary>
        public float Airspeed;

        /// <summary>Flight path angle: the direction the craft is MOVING, degrees, + is climbing.</summary>
        public float Gamma;

        /// <summary>Body pitch: the direction the craft is POINTING, degrees, + is nose up.</summary>
        public float Pitch;

        /// <summary>Bank angle, degrees, + is right wing down.</summary>
        public float Roll;

        /// <summary>Compass heading, degrees, measured from +Z toward +X.</summary>
        public float Heading;

        /// <summary>Turn rate produced by the last step, deg/s. Read by the animator for the tail fan.</summary>
        public float TurnRate;

        /// <summary>Beat position, 0..1. Drives the wings; never filtered.</summary>
        public float FlapPhase;

        /// <summary>How hard the wings are actually beating, 0..1, after stamina is taken out.</summary>
        public float FlapEffort;

        /// <summary>How far the wings are unfolded, 0..1. Deployment modulated by tuck.</summary>
        public float WingSpread;

        /// <summary>Deployment ramp, 0 folded to 1 open. Owned by the caller, not the model.</summary>
        public float Deployment;

        /// <summary>Flapping endurance, 0..1.</summary>
        public float Stamina;

        /// <summary>True while the angle of attack is past the stall.</summary>
        public bool Stalled;

        /// <summary>Angle of attack implied by the current attitude — pointing minus moving.</summary>
        public float AngleOfAttack => Pitch - Gamma;

        /// <summary>
        /// A craft launched into the air with an initial speed and heading, wings still folded.
        /// Pitch and flight path both start level, so the first thing the wings do is arrest the
        /// fall rather than pitch the pilot into the ground.
        /// </summary>
        public static OrnithopterFlightState Launch(float airspeed, float headingDegrees)
        {
            return new OrnithopterFlightState
            {
                Airspeed = Mathf.Max(0f, airspeed),
                Gamma = 0f,
                Pitch = 0f,
                Roll = 0f,
                Heading = Mathf.Repeat(headingDegrees, 360f),
                FlapPhase = 0f,
                FlapEffort = 0f,
                WingSpread = 0f,
                Deployment = 0f,
                Stamina = 1f,
                Stalled = false,
            };
        }
    }

    /// <summary>What the pilot is asking for this step. All axes are -1..1.</summary>
    public readonly struct OrnithopterFlightInput
    {
        /// <summary>+1 nose up, -1 nose down.</summary>
        public readonly float Pitch;

        /// <summary>+1 bank right, -1 bank left.</summary>
        public readonly float Roll;

        /// <summary>+1 flap hard, -1 tuck and dive.</summary>
        public readonly float Flap;

        /// <summary>Direct yaw from the tail fan, for low-speed authority.</summary>
        public readonly float Turn;

        public OrnithopterFlightInput(float pitch, float roll, float flap, float turn)
        {
            Pitch = Mathf.Clamp(pitch, -1f, 1f);
            Roll = Mathf.Clamp(roll, -1f, 1f);
            Flap = Mathf.Clamp(flap, -1f, 1f);
            Turn = Mathf.Clamp(turn, -1f, 1f);
        }

        public static readonly OrnithopterFlightInput Neutral = new OrnithopterFlightInput(0f, 0f, 0f, 0f);
    }
}
