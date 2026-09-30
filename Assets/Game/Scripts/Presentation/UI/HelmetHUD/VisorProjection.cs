using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Screen and world points onto the visor's layers. Shared by everything on the visor that
    /// claims to sit ON something in the world — the reticle's bracket, the objective waypoint.
    /// </summary>
    public static class VisorProjection
    {
        /// <summary>
        /// A screen point as an anchoredPosition on a bottom-left-anchored child of
        /// <paramref name="layer"/>.
        ///
        /// <para>
        /// Through <see cref="RectTransformUtility"/> rather than by dividing the screen point by
        /// the canvas scale, because a visor layer is not guaranteed to sit at the canvas origin:
        /// <see cref="VisorSway"/> writes an offset onto the visor root every frame, so the whole
        /// helmet lags a few pixels behind a head turn. Everything else on the layer is meant to do
        /// that. A mark that claims to be ON something in the world is not, and drifting off its
        /// target exactly while the player swings the camera onto it is the worst moment to drift.
        /// Asking the rectangle where a screen point falls inside it takes that offset — and any
        /// other ancestor transform — out of the answer.
        /// </para>
        /// </summary>
        public static bool TryScreenToLayer(RectTransform layer, Canvas canvas, Vector2 screen, out Vector2 layerPoint)
        {
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, screen, uiCamera, out Vector2 local))
            {
                layerPoint = default;
                return false;
            }

            // Local space is measured from the pivot; anchoredPosition on a bottom-left-anchored
            // child is measured from the corner.
            layerPoint = local + Vector2.Scale(layer.rect.size, layer.pivot);
            return true;
        }

        /// <summary>
        /// Where a waypoint is drawn: at its projection while that is on screen, otherwise pinned
        /// to the edge of the screen (inset by <paramref name="inset"/> pixels) in its direction.
        ///
        /// <para>
        /// A point BEHIND the eye projects mirrored through the centre — <c>WorldToScreenPoint</c>
        /// flips it — so it is turned back round before pinning. Without that a waypoint over the
        /// player's left shoulder is pinned to the right edge of the screen and they turn the wrong
        /// way.
        /// </para>
        /// </summary>
        /// <param name="screen">The raw <c>WorldToScreenPoint</c> result, z included.</param>
        public static Vector2 PinToScreen(Vector3 screen, Vector2 screenSize, float inset, out bool onScreen)
        {
            Vector2 centre = screenSize * 0.5f;
            Vector2 point = new(screen.x, screen.y);
            bool behind = screen.z <= 0f;

            if (behind) point = centre - (point - centre);

            Vector2 half = Vector2.Max(centre - new Vector2(inset, inset), Vector2.one);
            Vector2 offset = point - centre;

            onScreen = !behind && Mathf.Abs(offset.x) <= half.x && Mathf.Abs(offset.y) <= half.y;
            if (onScreen) return point;

            // Straight behind: no direction to read, so the marker sits at the bottom edge — "turn round".
            if (offset.sqrMagnitude < 1e-4f) offset = Vector2.down;

            float scale = Mathf.Min(half.x / Mathf.Max(Mathf.Abs(offset.x), 1e-4f),
                                    half.y / Mathf.Max(Mathf.Abs(offset.y), 1e-4f));
            return centre + offset * scale;
        }
    }
}
