using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// A camera pose the way a focus flight interpolates it: a position, two ABSOLUTE angles —
    /// yaw about world up, pitch about the horizontal — and a field of view. Never a rotation.
    /// </summary>
    public readonly struct FlightPose
    {
        public readonly Vector3 Position;
        public readonly float Yaw;
        public readonly float Pitch;
        public readonly float Fov;

        public FlightPose(Vector3 position, float yaw, float pitch, float fov)
        {
            Position = position;
            Yaw = yaw;
            Pitch = pitch;
            Fov = fov;
        }

        /// <summary>The pose a transform is in now, read as yaw and pitch about world axes.</summary>
        public static FlightPose Of(Transform t, float fov)
        {
            Vector3 euler = t.rotation.eulerAngles;
            return new FlightPose(t.position, euler.y, euler.x, fov);
        }

        /// <summary>The rotation this pose means. Roll is not a term in it, by construction.</summary>
        public Quaternion Rotation => Quaternion.Euler(Pitch, Yaw, 0f);
    }

    /// <summary>
    /// The flight every focus camera takes between two poses.
    ///
    /// <para>
    /// Interpolated as position + yaw + pitch, not as a pose: <c>Quaternion.Slerp</c> takes the
    /// geodesic between two rotations, and between a player's eyeline and a shot 180° round the
    /// other side of a pack that path rolls through 19° at the halfway point — the camera
    /// cartwheels on its way over. Blending the two angles keeps it level throughout, and
    /// <see cref="Mathf.LerpAngle"/> takes the short way round.
    /// </para>
    /// </summary>
    public static class FocusFlight
    {
        public static FlightPose Blend(in FlightPose from, in FlightPose to, float t)
        {
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            return new FlightPose(
                Vector3.Lerp(from.Position, to.Position, k),
                Mathf.LerpAngle(from.Yaw, to.Yaw, k),
                Mathf.LerpAngle(from.Pitch, to.Pitch, k),
                Mathf.Lerp(from.Fov, to.Fov, k));
        }
    }
}
