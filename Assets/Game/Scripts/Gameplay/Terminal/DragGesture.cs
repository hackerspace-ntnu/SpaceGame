using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// One press on a view the cursor can both turn and click: a click until it has travelled far
    /// enough from where it went down to be a drag, and a turn from then on. Never both.
    ///
    /// <para>
    /// <b>Nothing turns inside the dead zone.</b> A view that starts turning on the frame the
    /// button goes down cannot tell a click from a drag afterwards — the picture has already moved,
    /// and the reader who meant to click has been answered with a shrug. Holding the movement back
    /// until the cursor has clearly left the press point is what makes a click a click.
    /// </para>
    /// <para>
    /// <b>Travel is measured from the press point, not summed along the path.</b> A hand resting on
    /// a mouse wanders a pixel at a time in both directions; a sum counts every wobble against the
    /// reader and never gives any of it back, so a click held for half a second is a drag no matter
    /// how still the hand was.
    /// </para>
    /// <para>
    /// Pure, so the rule can be asserted without a mouse.
    /// </para>
    /// </summary>
    public struct DragGesture
    {
        private bool down;
        private bool turning;
        private Vector2 origin;
        private Vector2 last;

        /// <summary>Is a press in progress at all?</summary>
        public bool Down => down;

        /// <summary>Has this press left the dead zone? While false it is still a click.</summary>
        public bool Turning => turning;

        /// <summary>Where the press went down. What a click is picked from.</summary>
        public Vector2 Origin => origin;

        public void Press(Vector2 point)
        {
            down = true;
            turning = false;
            origin = point;
            last = point;
        }

        /// <summary>
        /// The turn this frame's movement asks for, zero while the press is still a click. The dead
        /// zone is eaten rather than paid out when it is crossed: handing over the whole distance
        /// from the press point would snap the view by the width of the zone at the moment the drag
        /// begins, which is the one frame the reader is watching.
        /// </summary>
        public Vector2 Move(Vector2 point, float deadZone)
        {
            if (!down) return Vector2.zero;

            if (!turning)
            {
                if ((point - origin).sqrMagnitude <= deadZone * deadZone)
                {
                    last = point;
                    return Vector2.zero;
                }

                turning = true;
            }

            Vector2 delta = point - last;
            last = point;
            return delta;
        }

        /// <summary>Ends the press. True when it never became a drag — a click, at <see cref="Origin"/>.</summary>
        public bool Release()
        {
            bool clicked = down && !turning;
            down = false;
            turning = false;
            return clicked;
        }

        /// <summary>Ends the press with nothing to show for it — the cursor left, or the page did.</summary>
        public void Cancel()
        {
            down = false;
            turning = false;
        }
    }
}
