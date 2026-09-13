using UnityEngine;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// The full-page rect the rank's nameplates, team plates and colour cycler are drawn into, and
    /// the one way any of them is placed over a point in the world.
    ///
    /// Built into the page so it is destroyed with it — which is what makes every overlay go away
    /// when the roster does, without anyone having to remember each of them.
    /// </summary>
    internal sealed class LobbyOverlayLayer
    {
        public RectTransform Rect { get; }

        public LobbyOverlayLayer(RectTransform page)
        {
            Rect = UIBuilder.Fill(UIBuilder.Rect("PreviewLabels", page));
        }

        /// <summary>A row placed by its centre, which is what a projected world point gives us.</summary>
        public RectTransform Centred(string name, float width, float height)
        {
            RectTransform row = UIBuilder.Rect(name, Rect);
            row.anchorMin = row.anchorMax = row.pivot = new Vector2(0.5f, 0.5f);
            row.sizeDelta = new Vector2(width, height);
            return row;
        }

        /// <summary>
        /// Where a world point lands on this layer, in the layer's own canvas pixels. False when
        /// that point is behind the camera and nothing should be drawn for it at all.
        ///
        /// <para>
        /// The behind-the-camera test is not defensive padding: WorldToScreenPoint happily returns a
        /// mirrored on-screen point for anything behind the lens, so without it a figure the camera
        /// has turned away from puts its name back on screen in the wrong place.
        /// </para>
        ///
        /// <para>
        /// This is the ONE conversion out of world space for every overlay in the rank, and it hands
        /// back CANVAS pixels rather than screen pixels on purpose. Every size in the overlays — a
        /// font size, a row width, the pitch a label is sized against — is a canvas pixel, and a
        /// caller that measures in screen pixels has to convert; the two overlays that did that
        /// converted with a hardcoded factor describing a scaler rule the lobby does not use, which
        /// is what made nameplates come out the wrong size on anything but a 16:9 monitor. Measuring
        /// in the space the sizes are already written in removes the conversion rather than
        /// correcting it.
        /// </para>
        /// </summary>
        public bool TryToCanvas(Camera camera, Vector3 worldPoint, out Vector2 canvasPoint)
        {
            canvasPoint = default;
            if (Rect == null || camera == null) return false;

            Vector3 screen = camera.WorldToScreenPoint(worldPoint);
            if (screen.z <= 0f) return false;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screen, null, out canvasPoint);
            return true;
        }

        /// <summary>
        /// Moves a row onto a world position. False when that position is behind the camera and
        /// the row should not be drawn at all.
        /// </summary>
        public bool Place(Camera camera, RectTransform row, Vector3 worldPoint)
        {
            if (row == null) return false;
            if (!TryToCanvas(camera, worldPoint, out Vector2 local)) return false;

            row.anchoredPosition = local;
            return true;
        }

        public void Destroy()
        {
            if (Rect != null) Object.Destroy(Rect.gameObject);
        }
    }
}
