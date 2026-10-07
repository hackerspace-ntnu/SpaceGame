using System;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>The tunables of one axis. Serialized on <see cref="DishRig"/>, so they are tuned in the Inspector.</summary>
    [Serializable]
    public struct SlewMotor
    {
        [Tooltip("Top speed, degrees per second.")]
        [Min(0.01f)] public float MaxSpeed;

        [Tooltip("How quickly the motor spools up and brakes, degrees per second squared.")]
        [Min(0.01f)] public float Acceleration;

        [Tooltip("A full-circle axis wraps through 360 and ignores the limits.")]
        public bool Wraps;

        [Tooltip("Lowest angle, degrees. Ignored when the axis wraps.")]
        public float Min;

        [Tooltip("Highest angle, degrees. Ignored when the axis wraps.")]
        public float Max;
    }
}
