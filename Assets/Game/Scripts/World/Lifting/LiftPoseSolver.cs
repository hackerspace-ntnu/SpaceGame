// Where a load with one end lifted stands: the near end in the carrier's hands, the far end on the ground.
//
// Pure arithmetic over a measured shape, so the rule is tested without a body, a network or a scene, and so every machine that
// poses the same load from the same hands gets the same answer.
using System;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// A liftable load measured in its own frame (metres from the root, scale taken out): the grip line's middle at the near
    /// end, the near end's underside below it, and the far end's underside that rests on the ground while the near end is up.
    /// </summary>
    public readonly struct LiftShape
    {
        /// <summary>Middle of the two grips, at the top of the near end.</summary>
        public readonly Vector3 Grip;

        /// <summary>The near end's underside, below the grips: what rests on the ground when the load is put down.</summary>
        public readonly Vector3 Heel;

        /// <summary>The far end's underside: what stays on the ground while the near end is lifted.</summary>
        public readonly Vector3 Foot;

        /// <summary>The load's own up, in its frame.</summary>
        public readonly Vector3 Up;

        public LiftShape(Vector3 grip, Vector3 heel, Vector3 foot, Vector3 up)
        {
            Grip = grip;
            Heel = heel;
            Foot = foot;
            Up = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up;
        }

        /// <summary>Grip to foot, the axis the load swings about the hands on.</summary>
        public Vector3 Axis => Foot - Grip;

        /// <summary>Is there a load to lift at all: a grip and a foot apart from each other.</summary>
        public bool IsValid => Axis.sqrMagnitude > 1e-4f;
    }

    public static class LiftPoseSolver
    {
        /// <summary>How many points along the underside, heel to foot, are kept above the ground.</summary>
        public const int UndersideSamples = 6;

        /// <summary>Steepest the grip-to-foot axis may point down or up, in degrees: a load is never hung straight down.</summary>
        public const float MaxPitchDegrees = 75f;

        /// <summary>
        /// The pose that puts the load's grip on <paramref name="grip"/>, its axis along <paramref name="heading"/> (flat), and
        /// tips the far end down until the first point of its underside meets the ground — the foot on level ground, the middle
        /// of the load over a crest — so no part of it is ever below the ground under it.
        /// <paramref name="groundAt"/> answers the ground height at (x, z).
        /// </summary>
        public static Pose Solve(in LiftShape shape, Vector3 grip, Vector3 heading, Func<float, float, float> groundAt)
        {
            Vector3 flat = Flat(heading);
            Vector3 axisLocal = shape.Axis;
            float length = axisLocal.magnitude;
            Vector3 axisDir = axisLocal / length;

            // The load's up, square to its own axis: the roll it keeps while it swings.
            Vector3 upLocal = Vector3.ProjectOnPlane(shape.Up, axisDir);
            if (upLocal.sqrMagnitude < 1e-8f) upLocal = Vector3.ProjectOnPlane(Vector3.up, axisDir);
            upLocal.Normalize();

            float pitch = 0f;

            // Twice: where on the ground each point lands depends on the pitch being solved for.
            for (int pass = 0; pass < 2; pass++)
            {
                float lowest = MaxPitchDegrees * Mathf.Deg2Rad;

                for (int i = 0; i <= UndersideSamples; i++)
                {
                    Vector3 local = Vector3.Lerp(shape.Heel, shape.Foot, i / (float)UndersideSamples) - shape.Grip;
                    float along = Vector3.Dot(local, axisDir);
                    float below = -Vector3.Dot(local, upLocal);
                    if (along <= 1e-3f) continue;

                    float reach = along * Mathf.Cos(pitch) - below * Mathf.Sin(pitch);
                    Vector3 over = grip + flat * reach;
                    float allowed = PitchToTouch(along, below, grip.y - groundAt(over.x, over.z));
                    if (allowed < lowest) lowest = allowed;
                }

                pitch = Mathf.Clamp(lowest, -MaxPitchDegrees * Mathf.Deg2Rad, MaxPitchDegrees * Mathf.Deg2Rad);
            }

            Vector3 axisWorld = flat * Mathf.Cos(pitch) + Vector3.down * Mathf.Sin(pitch);
            Vector3 upWorld = Vector3.ProjectOnPlane(Vector3.up, axisWorld).normalized;

            Quaternion rotation = Quaternion.LookRotation(axisWorld, upWorld) *
                                  Quaternion.Inverse(Quaternion.LookRotation(axisDir, upLocal));
            return new Pose(grip - rotation * shape.Grip, rotation);
        }

        /// <summary>
        /// How far below level the axis may tip before a point <paramref name="along"/> it and <paramref name="below"/> it
        /// (both from the grip, in the load's frame) meets ground <paramref name="drop"/> under the grip. A point that cannot
        /// reach the ground at any pitch allows the steepest one.
        /// </summary>
        public static float PitchToTouch(float along, float below, float drop)
        {
            float radius = Mathf.Sqrt(along * along + below * below);
            if (radius < 1e-5f) return MaxPitchDegrees * Mathf.Deg2Rad;

            // along·sin(p) + below·cos(p) = drop  ⇔  radius·sin(p + φ) = drop.
            float phase = Mathf.Atan2(below, along);
            float ratio = drop / radius;
            if (ratio >= 1f) return MaxPitchDegrees * Mathf.Deg2Rad;
            return Mathf.Asin(Mathf.Max(-1f, ratio)) - phase;
        }

        /// <summary>
        /// The load put down where it is: turned to the way its axis points now, lying on its underside along the slope of the
        /// ground between where its heel and its foot come down.
        /// </summary>
        public static Pose Rest(in LiftShape shape, Pose carried, Func<float, float, float> groundAt)
        {
            Quaternion yaw = Quaternion.LookRotation(Flat(carried.rotation * shape.Axis), Vector3.up) *
                             Quaternion.Inverse(Quaternion.LookRotation(Flat(shape.Axis), Vector3.up));

            Vector3 heel = carried.position + yaw * shape.Heel;
            Vector3 foot = carried.position + yaw * shape.Foot;
            float heelGround = groundAt(heel.x, heel.z);
            float footGround = groundAt(foot.x, foot.z);

            Vector3 run = foot - heel;
            run.y = 0f;
            float distance = run.magnitude;
            Quaternion slope = distance > 1e-3f
                ? Quaternion.AngleAxis(Mathf.Atan2(heelGround - footGround, distance) * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, run / distance))
                : Quaternion.identity;

            Quaternion rotation = slope * yaw;
            Vector3 position = carried.position + (new Vector3(heel.x, heelGround, heel.z) - (carried.position + rotation * shape.Heel));
            return new Pose(position, rotation);
        }

        /// <summary>
        /// The heading after <paramref name="deltaTime"/>: swung toward <paramref name="toward"/> at <paramref name="rate"/>
        /// (per second, exponential), so the far end trails round a turn instead of snapping to it. Flat.
        /// </summary>
        public static Vector3 Swing(Vector3 heading, Vector3 toward, float rate, float deltaTime)
        {
            Vector3 from = Flat(heading), to = Flat(toward);
            float t = 1f - Mathf.Exp(-Mathf.Max(0f, rate) * Mathf.Max(0f, deltaTime));
            return Vector3.Slerp(from, to, t).normalized;
        }

        public static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
        }
    }
}
