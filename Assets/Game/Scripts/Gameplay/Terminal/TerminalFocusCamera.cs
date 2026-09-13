using System;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The view used while reading a terminal: parked out along the glass's normal, looking
    /// squarely back into it, close enough that the screen fills most of the frame.
    ///
    /// <para>
    /// The shot is derived from the screen, not authored per terminal: <see cref="TerminalShot"/>
    /// puts the lens wherever a plate of this height fills <see cref="Shot.Fill"/> of a
    /// <see cref="Shot.FieldOfView"/> frame, and faces it down the plate's own normal. A screen
    /// that leans back is therefore looked down onto and one that stands upright is looked at
    /// level, with no numbers to retune when a model changes.
    /// </para>
    /// <para>
    /// The base class owns the handover, the flight in and out, the cursor parallax and the
    /// depth of field — see <see cref="FocusCamera"/>. Nothing here touches the player's own
    /// camera.
    /// </para>
    /// </summary>
    public sealed class TerminalFocusCamera : FocusCamera
    {
        /// <summary>The authored lens. Serialized on <see cref="TerminalFocusSession"/> so it is tuned in the Inspector.</summary>
        [Serializable]
        public struct Shot
        {
            [Tooltip("Vertical field of view, degrees. 40 is the family's narrow lens: flat perspective, honest sizes.")]
            public float FieldOfView;

            [Tooltip("Share of the frame's height the glass fills once the camera has landed.")]
            [Range(0.3f, 1f)] public float Fill;

            [Tooltip("Seconds the flight from the player's eye takes.")]
            public float FlyInSeconds;

            public static Shot Default => new()
            {
                FieldOfView = 40f,
                Fill = 0.8f,
                FlyInSeconds = 0.35f,
            };
        }

        private Transform anchor;
        private float screenHeight;
        private Shot shot;
        private float distance;

        /// <param name="screenAnchor">A transform at the glass's centre whose forward is the outward normal and whose up is the screen's up.</param>
        /// <param name="height">The glass's height along that up, metres.</param>
        /// <param name="playerCamera">Switched off, with its AudioListener, for the duration.</param>
        public static TerminalFocusCamera Spawn(Transform screenAnchor, float height, in Shot shot, Camera playerCamera)
        {
            if (screenAnchor == null) return null;

            var go = new GameObject("TerminalFocusCamera");
            var focus = go.AddComponent<TerminalFocusCamera>();
            focus.anchor = screenAnchor;
            focus.screenHeight = height;
            focus.shot = shot;
            focus.distance = TerminalShot.Distance(height, shot.FieldOfView, shot.Fill);

            // Last, and only once every field the base reads is set: Begin asks for the shot's
            // pose immediately, to seed the flight and the depth of field.
            focus.Begin(playerCamera);
            return focus;
        }

        /// <summary>Metres from the glass to the lens once landed.</summary>
        public float Distance => distance;

        protected override bool HasTarget => anchor != null;
        protected override float Fov => shot.FieldOfView;
        protected override float FlyInSeconds => shot.FlyInSeconds;
        protected override float PitchDown => TerminalShot.PitchDown(Plane());
        protected override float LensYaw() => TerminalShot.Yaw(Plane());
        protected override Vector3 LensPosition() => TerminalShot.LensPosition(Plane(), distance);
        protected override float FocusDistance() => distance;

        /// <summary>
        /// Read live off the anchor rather than captured at spawn: the terminal stands in a ship
        /// that may be hovering, and a shot pinned where the glass WAS would drift off it.
        /// </summary>
        private ScreenPlane Plane() =>
            new(anchor.position, anchor.forward, anchor.up, anchor.right, 0f, screenHeight);
    }
}
