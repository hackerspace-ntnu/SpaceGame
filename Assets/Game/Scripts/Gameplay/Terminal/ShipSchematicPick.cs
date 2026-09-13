using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Which of the schematic's modules a point on the glass meant.
    ///
    /// <para>
    /// Pure, and separate from the stage that owns the miniature, because this is the arithmetic
    /// that decides whether the display is usable at all and it can only be checked by working it
    /// out — the same reason <see cref="ShipSchematicOrbit"/> is its own class.
    /// </para>
    /// <para>
    /// <b>The modules are far smaller than the glass they are drawn on.</b> Swept over the resting
    /// framing the eleven of them cross under 6 % of the viewport, so a hit test that demands the
    /// ray actually enter a box reads as "clicks do nothing". The fallback is therefore not
    /// optional — but it measures to a module's FOOTPRINT, the box's own outline on the viewport,
    /// never to its middle. A nuclear motor is three times the size of a belly turbine on the
    /// glass: measured from middles, the cursor a few pixels off the motor's flank is closer to the
    /// little turbine's middle than to the motor's, and picks the turbine — which is exactly the
    /// complaint that "you cannot click the part you are pointing at".
    /// </para>
    /// </summary>
    public static class ShipSchematicPick
    {
        /// <summary>The cursor is over no module. Never an index.</summary>
        public const int Nothing = -1;

        /// <summary>
        /// The module under <paramref name="uv"/> — a point in the viewport, 0..1 from its bottom
        /// left — as an index into <paramref name="boxes"/>, or <see cref="Nothing"/>.
        ///
        /// <para>
        /// A box the ray actually crosses always wins, nearest first, so aiming squarely at a motor
        /// can never pick its neighbour and a module in front of another is the one that answers.
        /// Only a miss falls through to <paramref name="margin"/>, measured in the viewport's
        /// HALF-heights: the same unit across and up, or forgiveness would be an ellipse on a
        /// viewport that is not square. A box with no size is a module the miniature has no mesh
        /// for and is not pickable.
        /// </para>
        /// </summary>
        public static int At(ShipSchematicOrbit orbit, Vector2 uv, float aspect,
                             IReadOnlyList<Bounds> boxes, float margin)
        {
            if (orbit == null || boxes == null || boxes.Count == 0) return Nothing;

            orbit.Lens(out Vector3 origin, out Quaternion rotation);
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            float size = orbit.Size;

            // The cursor in the same units ViewportOffset reports: half-heights from the middle.
            var cursor = new Vector2((uv.x - 0.5f) * 2f * aspect, (uv.y - 0.5f) * 2f);

            // Orthographic, so the ray is the lens slid across its own plane rather than fanned out
            // from a point — and the lens has not necessarily rendered this frame anyway.
            var ray = new Ray(origin + right * (cursor.x * size) + up * (cursor.y * size),
                              rotation * Vector3.forward);

            int crossed = Crossed(ray, boxes);
            return crossed != Nothing ? crossed : Nearest(cursor, boxes, margin, origin, right, up, size);
        }

        /// <summary>The nearest box along the ray, or <see cref="Nothing"/>.</summary>
        private static int Crossed(Ray ray, IReadOnlyList<Bounds> boxes)
        {
            int best = Nothing;
            float nearest = float.MaxValue;

            for (int i = 0; i < boxes.Count; i++)
            {
                Bounds box = boxes[i];
                if (box.size == Vector3.zero) continue;
                if (!box.IntersectRay(ray, out float distance) || distance >= nearest) continue;

                nearest = distance;
                best = i;
            }

            return best;
        }

        /// <summary>
        /// The module whose outline on the viewport passes nearest the cursor, if it passes within
        /// <paramref name="margin"/>. Ties — the cursor inside two outlines without crossing either
        /// box — go to the module whose middle is nearer.
        /// </summary>
        private static int Nearest(Vector2 cursor, IReadOnlyList<Bounds> boxes, float margin,
                                   Vector3 origin, Vector3 right, Vector3 up, float size)
        {
            int best = Nothing;
            float bestEdge = margin;
            float bestMiddle = float.MaxValue;

            for (int i = 0; i < boxes.Count; i++)
            {
                Bounds box = boxes[i];
                if (box.size == Vector3.zero) continue;

                Rect footprint = Footprint(box, origin, right, up, size);
                float edge = EdgeDistance(cursor, footprint);
                if (edge > bestEdge) continue;

                float middle = Vector2.Distance(cursor, footprint.center);
                if (edge == bestEdge && middle >= bestMiddle) continue;

                bestEdge = edge;
                bestMiddle = middle;
                best = i;
            }

            return best;
        }

        /// <summary>
        /// A module's outline on the viewport, in half-heights from the middle: the rectangle round
        /// its eight projected corners. Wider than the true silhouette of a box seen corner-on, and
        /// deliberately so — this is the forgiving half of the pick, and a rectangle is eight dot
        /// products where a convex hull is a sort.
        /// </summary>
        private static Rect Footprint(Bounds box, Vector3 origin, Vector3 right, Vector3 up, float size)
        {
            Vector3 min = box.min, max = box.max;
            float xMin = float.MaxValue, xMax = float.MinValue;
            float yMin = float.MaxValue, yMax = float.MinValue;

            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3((corner & 1) == 0 ? min.x : max.x,
                                        (corner & 2) == 0 ? min.y : max.y,
                                        (corner & 4) == 0 ? min.z : max.z);

                Vector3 relative = point - origin;
                float x = Vector3.Dot(relative, right) / size;
                float y = Vector3.Dot(relative, up) / size;

                if (x < xMin) xMin = x;
                if (x > xMax) xMax = x;
                if (y < yMin) yMin = y;
                if (y > yMax) yMax = y;
            }

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        /// <summary>How far the cursor sits outside a rectangle. Zero anywhere inside it.</summary>
        private static float EdgeDistance(Vector2 point, Rect rect)
        {
            float x = Mathf.Max(rect.xMin - point.x, 0f, point.x - rect.xMax);
            float y = Mathf.Max(rect.yMin - point.y, 0f, point.y - rect.yMax);

            return new Vector2(x, y).magnitude;
        }
    }
}
