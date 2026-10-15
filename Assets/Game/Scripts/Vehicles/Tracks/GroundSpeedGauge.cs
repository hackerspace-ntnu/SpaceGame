// How fast one point of a machine is seen to move over the ground, read off its own transform frame
// by frame: shared by the track dust (RollingDust, per contact) and the track belts (TrackBelts, per
// side), so both judge a hull alike on the host, on a client watching its replicated pose, and in a
// test. A step implying an implausible speed is a snap (a load, a correction) and keeps the old speed.
using SpaceGame.Vehicles.Monowheel;
using UnityEngine;

namespace SpaceGame.Vehicles
{
    public struct GroundSpeedGauge
    {
        private Vector3 last;
        private float speed;

        /// <summary>The smoothed speed, m/s: signed along the direction last measured along.</summary>
        public float Speed => speed;

        /// <summary>Forget the point's history: after a spawn, a load or any snap into place.</summary>
        public void Reset(Vector3 position)
        {
            last = position;
            speed = 0f;
        }

        /// <summary>Signed speed along <paramref name="along"/> after the point moved to <paramref name="position"/>.</summary>
        public float Measure(Vector3 position, Vector3 along, float dt, float smoothing, float maxPlausibleSpeed)
        {
            speed = MonowheelPresentationMath.StepSpeed(speed, last, position, along, dt, smoothing, maxPlausibleSpeed, out _);
            last = position;
            return speed;
        }

        /// <summary>Speed whichever way the point went: measured along its own step.</summary>
        public float MeasureAlongStep(Vector3 position, float dt, float smoothing, float maxPlausibleSpeed) =>
            Measure(position, position - last, dt, smoothing, maxPlausibleSpeed);
    }
}
