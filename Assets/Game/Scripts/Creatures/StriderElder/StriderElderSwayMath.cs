// The arithmetic of the elder's torso sway, with nothing in it but numbers (StriderElderSway).
using UnityEngine;

namespace SpaceGame.Creatures
{
    public static class StriderElderSwayMath
    {
        /// <summary>Chest rolls per gait cycle: one each way for each of the two leg pairs' steps.</summary>
        public const float RollsPerCycle = 2f;

        /// <summary>
        /// Chest offset in degrees (x = lean forward, z = roll) at gait <paramref name="phase"/> [0, 1)
        /// and <paramref name="speed01"/> of the legs' top speed (clamped). Zero standing still.
        /// </summary>
        public static Vector3 Chest(float phase, float speed01, float rollDegrees, float leanDegrees)
        {
            float speed = Mathf.Clamp01(speed01);
            if (speed <= 0f) return Vector3.zero;
            float roll = rollDegrees * speed * Mathf.Sin(phase * RollsPerCycle * 2f * Mathf.PI);
            return new Vector3(leanDegrees * speed, 0f, roll);
        }

        /// <summary>The head's offset: a share of the chest's, the other way, so the gaze stays level.</summary>
        public static Vector3 Head(Vector3 chest, float steadiness) => -chest * steadiness;
    }
}
