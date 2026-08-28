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
        /// Moves a row onto a world position. False when that position is behind the camera and
        /// the row should not be drawn at all.
        ///
        /// The behind-the-camera test is not defensive padding: WorldToScreenPoint happily returns a
        /// mirrored on-screen point for anything behind the lens, so without it a figure the camera
        /// has turned away from puts its name back on screen in the wrong place.
        /// </summary>
        public bool Place(Camera camera, RectTransform row, Vector3 worldPoint)
        {
            if (Rect == null || row == null) return false;

            Vector3 screen = camera.WorldToScreenPoint(worldPoint);
            if (screen.z <= 0f) return false;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screen, null, out Vector2 local);

            row.anchoredPosition = local;
            return true;
        }

        public void Destroy()
        {
            if (Rect != null) Object.Destroy(Rect.gameObject);
        }
    }
}
