// Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentationMath.cs
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Vehicles.Monowheel
{
    /// <summary>
    /// The arithmetic behind a monowheel's presentation (spec §2 and §4): how fast it is going
    /// judged from its own transform, how far each ring turns for that, how much sand and smoke that
    /// is worth, how much of it the camera's distance allows, and which way a ring's axle points.
    /// Pure, so every one-frame edge case — a teleport, a reverse, no camera — is a unit test.
    /// </summary>
    public static class MonowheelPresentationMath
    {
        /// <summary>
        /// Smoothed signed ground speed along <paramref name="forward"/>. A step implying more than
        /// <paramref name="maxPlausibleSpeed"/> is a snap (a save restore, a streaming migrate, a
        /// NetworkTransform correction), not motion: the previous speed is kept and
        /// <paramref name="teleported"/> says so. Judged by implied SPEED, not distance, so a long
        /// frame hitch at top speed still reads as driving.
        /// </summary>
        public static float StepSpeed(float smoothed, Vector3 from, Vector3 to, Vector3 forward,
                                      float dt, float smoothing, float maxPlausibleSpeed,
                                      out bool teleported)
        {
            teleported = false;
            if (dt <= 0f) return smoothed;

            Vector3 step = to - from;
            if (step.magnitude > maxPlausibleSpeed * dt)
            {
                teleported = true;
                return smoothed;
            }

            float raw = Vector3.Dot(step, forward.normalized) / dt;
            if (smoothing <= 0f) return raw;
            return Mathf.Lerp(smoothed, raw, 1f - Mathf.Exp(-dt / smoothing));
        }

        /// <summary>Degrees a ring of <paramref name="radius"/> turns in <paramref name="dt"/> rolling at <paramref name="speed"/>.</summary>
        public static float SpinDegrees(float speed, float radius, float dt) =>
            radius <= 0f ? 0f : speed / radius * dt * Mathf.Rad2Deg;

        /// <summary>|speed| as a fraction of <paramref name="fullSpeed"/>, clamped to [0, 1]. Reversing throws sand too.</summary>
        public static float SpeedFraction(float speed, float fullSpeed) =>
            fullSpeed <= 0f ? 0f : Mathf.Clamp01(Mathf.Abs(speed) / fullSpeed);

        /// <summary>Emission rate between <paramref name="idle"/> (standing) and <paramref name="max"/> (full speed).</summary>
        public static float Rate(float idle, float max, float fraction) => Mathf.Lerp(idle, max, fraction);

        /// <summary>
        /// 1 up to <paramref name="near"/>, 0 from <paramref name="far"/>, linear between. A NaN
        /// distance means there is no camera to be far from (a server, a loading screen): full.
        /// </summary>
        public static float LodFactor(float distance, float near, float far)
        {
            if (float.IsNaN(distance) || distance <= near) return 1f;
            if (distance >= far) return 0f;
            return 1f - (distance - near) / (far - near);
        }

        /// <summary>
        /// Unit normal of the best-fit plane through <paramref name="points"/> — for a ring mesh,
        /// its axle. The smallest-variance direction of the points' covariance, found by power
        /// iteration on (trace·I − C). Throws if the points do not span a plane.
        /// </summary>
        public static Vector3 PlaneNormal(IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count < 3)
                throw new ArgumentException("A plane needs at least three points.", nameof(points));

            Vector3 c = Vector3.zero;
            foreach (Vector3 p in points) c += p;
            c /= points.Count;

            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (Vector3 p in points)
            {
                Vector3 d = p - c;
                xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
                yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
            }

            float trace = xx + yy + zz;
            // Two of the three variances must be real for the points to be a plane, not a line.
            float[] v = { xx, yy, zz };
            Array.Sort(v);
            if (trace <= 1e-9f || v[1] <= trace * 1e-4f)
                throw new ArgumentException("The points are collinear or coincident; no plane.", nameof(points));

            // Power iteration on B = trace·I − C: B's largest eigenvector is C's smallest.
            Vector3 n = new Vector3(0.577f, 0.577f, 0.577f);
            for (int i = 0; i < 64; i++)
            {
                Vector3 b = new Vector3(
                    (trace - xx) * n.x - xy * n.y - xz * n.z,
                    -xy * n.x + (trace - yy) * n.y - yz * n.z,
                    -xz * n.x - yz * n.y + (trace - zz) * n.z);
                n = b.normalized;
            }
            return n;
        }
    }
}
