using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// The menu camera, borrowed for the lobby's own shot of the same set.
    ///
    /// <para>
    /// The menu's framing is composed around the ruin and the decorative astronauts on the right,
    /// which leaves the rank nowhere to stand. Swinging the camera onto open dune instead gives it
    /// clean ground and clean sky, and costs nothing — same scene, same lighting, pointed somewhere
    /// else. Borrowed, not permanent: the pose is saved on the way in and put back on the way out.
    /// </para>
    ///
    /// <para>
    /// Stored as values rather than as a parent or a copied transform: reparenting the menu camera
    /// would leave it somewhere unexpected if the rank died without tidying up.
    /// </para>
    /// </summary>
    internal sealed class LobbyPreviewCamera
    {
        /// <summary>
        /// How much air the fitted camera leaves around the rank — see
        /// <see cref="RankLayout.CameraDistance"/>. 1.2 leaves about a sixth of the frame as margin.
        /// Not measured against a capture — flag this if the rank reads cramped or lost in the frame.
        /// </summary>
        private const float FitMargin = 1.2f;

        private Transform borrowed;
        private Vector3 returnPosition;
        private Quaternion returnRotation;

        /// <summary>
        /// The pose the view was authored at, kept apart from the menu's OWN pose above (put back
        /// on teardown). The fit measures and pushes back from this one, never from wherever the
        /// camera happens to be sitting when a render runs.
        /// </summary>
        private Vector3 viewPosition;
        private Quaternion viewRotation;

        /// <summary>
        /// Swings the menu camera onto the authored view, remembering where it was.
        ///
        /// Silently does nothing when the scene has no object of that name, which is the right
        /// answer rather than an error: the menu's own framing is a perfectly usable shot, and a
        /// missing view means nobody has composed a better one yet. It also means the rank never
        /// fits the camera — see <see cref="Fit"/> — because there is no authored backward
        /// direction to push it along.
        /// </summary>
        public void Adopt(string viewName)
        {
            GameObject view = GameObject.Find(viewName);
            if (view == null) return;

            Camera camera = Camera.main;
            if (camera == null) return;

            borrowed = camera.transform;
            returnPosition = borrowed.position;
            returnRotation = borrowed.rotation;

            viewPosition = view.transform.position;
            viewRotation = view.transform.rotation;

            borrowed.SetPositionAndRotation(viewPosition, viewRotation);
        }

        /// <summary>
        /// Puts the camera back where the menu had it.
        ///
        /// Guarded on the borrowed transform rather than on the view still existing, so a view
        /// deleted while the lobby is open cannot strand the camera pointing at the dunes with a
        /// main menu drawn over it.
        /// </summary>
        public void Restore()
        {
            if (borrowed == null) return;

            borrowed.SetPositionAndRotation(returnPosition, returnRotation);
            borrowed = null;
        }

        /// <summary>
        /// Backs the camera off from the authored view so the whole rank fits in frame.
        ///
        /// Measured from the authored pose and only ever pushed FURTHER back along its own backward
        /// direction, never recomputed from the anchor outright. That is what guarantees a small
        /// rank reproduces the exact composed shot rather than drifting off its axis: when the rank
        /// already fits at the authored distance, the extra distance is zero and the camera sits
        /// exactly where the view put it.
        /// </summary>
        public void Fit(Transform anchor, int teams, int teamSize)
        {
            // No adopted view means no authored backward direction to push along, so there is
            // nothing safe to fit against — the rank keeps whatever framing the scene already has.
            if (borrowed == null || anchor == null) return;

            Camera camera = Camera.main;
            if (camera == null) return;

            float verticalFov = camera.fieldOfView * Mathf.Deg2Rad;
            float horizontalFov = 2f * Mathf.Atan(Mathf.Tan(verticalFov * 0.5f) * camera.aspect) * Mathf.Rad2Deg;

            float width = RankLayout.TotalWidth(teams, teamSize);
            float wanted = RankLayout.CameraDistance(width, horizontalFov, FitMargin);
            float authoredDistance = Vector3.Distance(viewPosition, anchor.position);

            // Never negative: a rank that already fits inside the authored shot must not pull the
            // camera IN, which is the one thing this class promises it never does.
            float extra = Mathf.Max(0f, wanted - authoredDistance);

            Vector3 backward = viewRotation * Vector3.back;
            borrowed.SetPositionAndRotation(viewPosition + backward * extra, viewRotation);
        }
    }
}
