using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The arithmetic of a camera parked in front of a screen: how far out along the glass's
    /// normal it has to sit for the glass to fill a given share of the frame, and which way it
    /// then faces. Pure, so the shot can be asserted without a camera.
    /// </summary>
    public static class TerminalShot
    {
        /// <summary>
        /// Distance from the glass at which a screen <paramref name="height"/> metres tall covers
        /// <paramref name="fill"/> of a frame with a vertical field of view of
        /// <paramref name="fovDegrees"/>. Height-limited on purpose: every screen in the family
        /// is wider than it is tall by less than the frame is, so fitting the height fits the
        /// width for free.
        /// </summary>
        public static float Distance(float height, float fovDegrees, float fill)
        {
            float halfFov = Mathf.Deg2Rad * Mathf.Clamp(fovDegrees, 1f, 179f) * 0.5f;
            float visibleHalfHeight = Mathf.Max(0.001f, height) * 0.5f / Mathf.Clamp(fill, 0.05f, 1f);
            return visibleHalfHeight / Mathf.Tan(halfFov);
        }

        /// <summary>Where the lens sits: out along the glass's normal.</summary>
        public static Vector3 LensPosition(in ScreenPlane screen, float distance) =>
            screen.Centre + screen.Normal * distance;

        /// <summary>
        /// The compass heading of a lens looking back INTO the glass, as
        /// <see cref="Presentation.FocusCamera"/> wants it: a yaw about world up.
        /// </summary>
        public static float Yaw(in ScreenPlane screen)
        {
            Vector3 gaze = -screen.Normal;
            var flat = new Vector3(gaze.x, 0f, gaze.z);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
            return Quaternion.LookRotation(flat, Vector3.up).eulerAngles.y;
        }

        /// <summary>
        /// Degrees the lens looks DOWN to face the glass squarely. A screen leaning back (normal
        /// tipped upward) is looked down onto; one leaning forward is looked up at, which is a
        /// negative pitch-down.
        /// </summary>
        public static float PitchDown(in ScreenPlane screen) =>
            Mathf.Asin(Mathf.Clamp(screen.Normal.y, -1f, 1f)) * Mathf.Rad2Deg;
    }
}
