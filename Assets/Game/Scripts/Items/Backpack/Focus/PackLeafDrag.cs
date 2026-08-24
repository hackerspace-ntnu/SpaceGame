using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The arithmetic behind flipping the front leaf up by hand: which faces count as the board,
    /// where a grabbed point on it travels to, and how far along that travel the cursor has got.
    ///
    /// <para>
    /// Split out of <see cref="PackDragController"/> for the same reason <see cref="PackPointer"/>
    /// was: it is the half with no state. Everything here is a pure function, so the drag state
    /// machine never has to reason about hinges and this can be read — and tested — on its own.
    /// </para>
    /// <para>
    /// <b>Why the progress is measured on SCREEN.</b> The obvious thing is to intersect the cursor
    /// ray with the leaf's swing plane and read the angle. That plane's normal is the hinge line,
    /// which is horizontal and runs left-to-right across the rig — and the focus camera sits 1.9 m
    /// back and 1.5 m up looking 38&#176; down at it, so the camera's forward lies very nearly IN
    /// that plane. At grazing incidence the intersection runs off to infinity and the angle it
    /// yields is noise. Projecting the cursor's own motion onto the on-screen segment between where
    /// the grabbed point IS and where it will END UP is well conditioned for any camera that can
    /// see the leaf at all, and it is also the thing the player is actually doing: pulling a point
    /// on the screen from one place to another.
    /// </para>
    /// </summary>
    public static class PackLeafDrag
    {
        /// <summary>Past this point on the arc a release stands the leaf up; below it, it lies back down.</summary>
        public const float CommitAt = 0.5f;

        /// <summary>
        /// Screen pixels of cursor travel below which a press-and-release on the board is a CLICK —
        /// the flip toggled outright — rather than a drag that scrubbed the leaf a fraction of a
        /// degree and sprang back. The click is the easy gesture and the drag the precise one;
        /// without this threshold every click was a zero-length drag that committed "lay it flat"
        /// and the board never moved, which read as the feature being broken.
        /// </summary>
        public const float ClickPixels = 8f;

        /// <summary>
        /// Every face that rides <c>PIVOT_Leaf</c> — the whole front flap. The wings and the lash
        /// line are children of the leaf now, so a bare point on any of them IS the board, and
        /// clicking or dragging it flips the flap. Only the two back-panel faces and the state
        /// the leaf is in decide anything else.
        /// </summary>
        public static bool IsLeafFace(PackSurfaceId id) =>
            id == PackSurfaceId.Leaf || id == PackSurfaceId.Rack ||
            id == PackSurfaceId.LongGoods ||
            id == PackSurfaceId.WingLeft || id == PackSurfaceId.WingRight;

        /// <summary>
        /// How far a world point stands from the hinge LINE — the radius of the circle it travels
        /// on when the leaf turns. Measured square to the axis, so a point's position along the
        /// hinge does not count.
        /// </summary>
        public static float RadiusFromHinge(Vector3 point, Vector3 hingeOrigin, Vector3 hingeAxis) =>
            Vector3.ProjectOnPlane(point - hingeOrigin, hingeAxis).magnitude;

        /// <summary>
        /// Where a point on the leaf ends up when the leaf turns <paramref name="degrees"/> about
        /// its hinge.
        /// </summary>
        public static Vector3 Swing(Vector3 point, Vector3 hingeOrigin, Vector3 hingeAxis, float degrees)
        {
            if (hingeAxis.sqrMagnitude < 1e-10f) return point;

            return hingeOrigin + Quaternion.AngleAxis(degrees, hingeAxis.normalized) * (point - hingeOrigin);
        }

        /// <summary>
        /// How far along its arc the drag has pulled the leaf.
        ///
        /// <para>
        /// <paramref name="flatScreen"/> and <paramref name="rackedScreen"/> are where the grabbed
        /// point sits on screen with the leaf all the way down and all the way up. The cursor's
        /// travel since the grab is projected onto the segment between them and added to where the
        /// leaf already was, so pulling along the arc moves it, pulling square to the arc does not,
        /// and pulling back the way it came puts it back.
        /// </para>
        /// <para>
        /// Degenerate framing — the two ends of the arc landing on the same pixel, which is the
        /// hinge seen exactly end-on — holds the leaf where it is rather than dividing by zero.
        /// </para>
        /// </summary>
        public static float Progress(Vector2 flatScreen, Vector2 rackedScreen,
                                     Vector2 grabCursor, Vector2 cursor, float startProgress)
        {
            Vector2 arc = rackedScreen - flatScreen;

            // Squared pixels. A screen arc shorter than about 4 px carries no usable direction, and
            // dividing by it turns a one-pixel mouse jitter into the whole travel.
            if (arc.sqrMagnitude < 16f) return Mathf.Clamp01(startProgress);

            return Mathf.Clamp01(startProgress + Vector2.Dot(cursor - grabCursor, arc) / arc.sqrMagnitude);
        }
    }
}
