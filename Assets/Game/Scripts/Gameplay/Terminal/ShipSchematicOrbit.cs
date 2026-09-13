using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Where the schematic's lens is: what it is pointed at, from which way round, and how much of
    /// the hull fits in the frame.
    ///
    /// <para>
    /// Plain C# with no scene behind it, like <see cref="TerminalShot"/>, because framing maths is
    /// exactly the kind of thing that is wrong by a factor of two and only says so on a screen
    /// nobody is looking at. Angle and zoom are each held twice — a TARGET the input writes and a
    /// CURRENT the lens reads — and <see cref="Step"/> eases one toward the other, so a flicked
    /// wheel settles instead of snapping.
    /// </para>
    /// <para>
    /// Orthographic on purpose. A schematic is a drawing: perspective would make the near motor
    /// bigger than the far one and invite the reader to think that meant something.
    /// </para>
    /// <para>
    /// <b>Nothing but the reader moves this.</b> Picking a module used to fly the lens onto it,
    /// which pushed the other ten out of frame and left the pick almost impossible to change; the
    /// drag and the wheel are now the only things that reframe the hull, and the pivot stays on the
    /// hull's own centre for good.
    /// </para>
    /// </summary>
    public sealed class ShipSchematicOrbit
    {
        /// <summary>How far the lens stands off its pivot. Orthographic, so this only has to clear the near plane.</summary>
        public const float Standoff = 6f;

        /// <summary>Degrees of yaw per canvas unit dragged. Tuned so a drag across the viewport is most of a turn.</summary>
        public const float DragToDegrees = 0.6f;

        /// <summary>Degrees the hull turns per second while nobody is driving it.</summary>
        public const float IdleDegreesPerSecond = 9f;

        /// <summary>Looking straight down the poles reads as nothing; stop short of both.</summary>
        public const float MinPitch = -72f, MaxPitch = 72f;

        /// <summary>Closest and widest zoom, as a share of the framing that fits the whole hull.</summary>
        public const float MinZoom = 0.12f, MaxZoom = 1.6f;

        /// <summary>Seconds for a re-frame to be most of the way there. Below a fly-in, above a cut.</summary>
        public const float Ease = 0.14f;

        // The view the hull is first drawn from: three-quarters on and slightly above, the angle a
        // technical illustration uses because it shows a length, a width and a height at once.
        private const float HomeYaw = 35f, HomePitch = 20f;

        private Vector3 pivot;
        private float yawTarget = HomeYaw, yaw = HomeYaw;
        private float pitchTarget = HomePitch, pitch = HomePitch;
        private float sizeTarget = 1f, size = 1f;

        private float wholeShipSize = 1f;

        /// <summary>Half the height the lens sees, world units — an orthographic camera's own size.</summary>
        public float Size => size;

        public Vector3 Pivot => pivot;
        public float Yaw => yaw;
        public float Pitch => pitch;

        /// <summary>
        /// Adopt the hull as the widest view there is, and sit at the home angle looking at all of
        /// it. Called once the miniature's bounds are known, and again if it is rebuilt.
        /// </summary>
        public void Adopt(Bounds ship, float aspect)
        {
            pivot = ship.center;
            wholeShipSize = FitSize(ship, aspect);

            Home();
        }

        /// <summary>
        /// Back to the view <see cref="Adopt"/> set: the whole hull, three-quarters on. Snapped
        /// rather than eased, because the only caller is a page being opened — there is nobody
        /// watching a transition on a page that was not up a frame ago.
        /// </summary>
        public void Home()
        {
            sizeTarget = size = wholeShipSize;
            yawTarget = yaw = HomeYaw;
            pitchTarget = pitch = HomePitch;
        }

        /// <summary>Turn the hull. <paramref name="delta"/> is the cursor's travel in canvas units.</summary>
        public void Drag(Vector2 delta)
        {
            yawTarget -= delta.x * DragToDegrees;
            pitchTarget = Mathf.Clamp(pitchTarget + delta.y * DragToDegrees, MinPitch, MaxPitch);
        }

        /// <summary>One notch of wheel. Positive pulls in.</summary>
        public void Zoom(float notches)
        {
            sizeTarget = Mathf.Clamp(sizeTarget * Mathf.Pow(0.85f, notches),
                                     wholeShipSize * MinZoom, wholeShipSize * MaxZoom);
        }

        /// <summary>Turn slowly on its own, so a display nobody is driving still reads as live.</summary>
        public void Idle(float deltaTime) => yawTarget += IdleDegreesPerSecond * deltaTime;

        /// <summary>Ease the current values toward the targets. Call once a frame.</summary>
        public void Step(float deltaTime)
        {
            float t = Ease <= 0f ? 1f : 1f - Mathf.Exp(-deltaTime / Ease);

            size = Mathf.Lerp(size, sizeTarget, t);
            pitch = Mathf.Lerp(pitch, pitchTarget, t);

            // Through the shortest way round, so dragging past 180° does not unwind the long way.
            yaw = Mathf.LerpAngle(yaw, yawTarget, t);
        }

        /// <summary>Where the lens sits and which way it looks, now.</summary>
        public void Lens(out Vector3 position, out Quaternion rotation)
        {
            rotation = Quaternion.Euler(pitch, yaw, 0f);
            position = pivot - rotation * Vector3.forward * Standoff;
        }

        /// <summary>
        /// Where a point in the miniature's space lands on the viewport, measured in HEIGHTS from
        /// the middle: y is the fraction of the frame's half-height, and x is the same unit rather
        /// than a fraction of the width, so that a distance in this space means the same thing
        /// across and up. That is what lets a pick radius be one number.
        /// </summary>
        public Vector2 ViewportOffset(Vector3 point)
        {
            Lens(out Vector3 lens, out Quaternion rotation);
            Vector3 relative = point - lens;

            return new Vector2(
                Vector3.Dot(relative, rotation * Vector3.right) / Mathf.Max(0.0001f, size),
                Vector3.Dot(relative, rotation * Vector3.up) / Mathf.Max(0.0001f, size));
        }

        /// <summary>
        /// The orthographic half-height that just contains a box seen from any angle. The box's
        /// own diagonal rather than its height: the hull turns, and a framing fitted to one
        /// silhouette clips the next one.
        /// </summary>
        public static float FitSize(Bounds bounds, float aspect)
        {
            float radius = bounds.extents.magnitude;
            float byWidth = aspect > 0.001f ? radius / aspect : radius;
            return Mathf.Max(0.001f, Mathf.Max(radius, byWidth));
        }
    }
}
