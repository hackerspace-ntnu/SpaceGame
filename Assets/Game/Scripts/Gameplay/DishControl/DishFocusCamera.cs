using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The dish controls' camera feed: a fixed vantage on the tower, framing the whole dish so the
    /// operator watches it answer.
    ///
    /// <para>
    /// Fixed rather than riding the azimuth pivot, so a turn reads as the dish swinging against the
    /// tower instead of the world wheeling round a still dish. The vantage is a transform on the prefab,
    /// read live, and the base class owns the handover, the cut in and out, and the depth of field.
    /// </para>
    /// </summary>
    public sealed class DishFocusCamera : FocusCamera
    {
        private Transform vantage;
        private Transform subject;
        private float fieldOfView;

        /// <param name="vantagePoint">Where the lens sits; its forward is where it looks.</param>
        /// <param name="focusOn">What the depth of field focuses on — the dish.</param>
        /// <param name="playerCamera">Switched off, with its AudioListener, for the duration.</param>
        public static DishFocusCamera Spawn(Transform vantagePoint, Transform focusOn, float fov, Camera playerCamera)
        {
            if (vantagePoint == null) return null;

            var go = new GameObject("DishFocusCamera");
            var focus = go.AddComponent<DishFocusCamera>();
            focus.vantage = vantagePoint;
            focus.subject = focusOn;
            focus.fieldOfView = fov;
            focus.Begin(playerCamera);
            return focus;
        }

        protected override bool HasTarget => vantage != null;
        protected override float Fov => fieldOfView;

        /// <summary>A cut, not a flight: the feed is a camera on the tower, and the way to it is through a wall.</summary>
        protected override float FlyInSeconds => 0f;

        protected override Vector3 LensPosition() => vantage.position;

        protected override float LensYaw()
        {
            Vector3 flat = Vector3.ProjectOnPlane(vantage.forward, Vector3.up);
            return flat.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(flat).eulerAngles.y : 0f;
        }

        protected override float PitchDown =>
            -Mathf.Asin(Mathf.Clamp(vantage.forward.y, -1f, 1f)) * Mathf.Rad2Deg;

        protected override float FocusDistance() =>
            subject != null ? Vector3.Distance(vantage.position, subject.position) : 1f;
    }
}
