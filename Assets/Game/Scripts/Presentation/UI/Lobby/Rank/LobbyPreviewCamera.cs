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

        /// <summary>
        /// How much of the canvas the page's own chrome takes along the bottom, in reference pixels:
        /// the status line's baseline plus its height. Below this the rank must not reach.
        ///
        /// <para>
        /// The TOP of the frame is not reserved. A team plate at <c>RankLayout.PlateLift</c>
        /// projects above the page title in the authored shot and always has — the title is a
        /// left-aligned column and the plates are centred over their own teams, so they coexist.
        /// </para>
        /// </summary>
        private const float ChromeBottom = MenuEntry.MessageBottom + 44f;

        /// <summary>
        /// How far above the HIGHEST team plate the shot has to reach, in metres — the plate's own
        /// height plus air. The plate lift itself is per-row (<c>RankLayout.MaxPlateLift</c>), so
        /// the fixed part here is only what sits above it; for a one-row rank the sum reproduces
        /// the authored fit exactly.
        /// </summary>
        private const float PlateHeadroom = 2.3f;

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
        public void Fit(Transform anchor, int teams, int teamSize, float groundSpread)
        {
            // No adopted view means no authored backward direction to push along, so there is
            // nothing safe to fit against — the rank keeps whatever framing the scene already has.
            if (borrowed == null || anchor == null) return;

            Camera camera = Camera.main;
            if (camera == null) return;

            float halfVertical = camera.fieldOfView * Mathf.Deg2Rad * 0.5f;
            float horizontalFov = 2f * Mathf.Atan(Mathf.Tan(halfVertical) * camera.aspect) * Mathf.Rad2Deg;
            float bandFov = 2f * Mathf.Atan(Mathf.Tan(halfVertical) * BandFraction()) * Mathf.Rad2Deg;

            float width = RankLayout.TotalWidth(teams, teamSize);
            float height = Mathf.Max(0f, groundSpread) + RankLayout.MaxPlateLift(teams) + PlateHeadroom;

            float wanted = RankLayout.CameraDistance(width, height, horizontalFov, bandFov, FitMargin);
            float authoredDistance = Vector3.Distance(viewPosition, anchor.position);

            // Never negative: a rank that already fits inside the authored shot must not pull the
            // camera IN, which is the one thing this class promises it never does.
            float extra = Mathf.Max(0f, wanted - authoredDistance);

            Vector3 backward = viewRotation * Vector3.back;
            Vector3 position = viewPosition + backward * extra;

            float wantedEye = anchor.position.y
                              + RankLayout.EyeHeight(teams, teamSize, authoredDistance + extra);

            float lift = Mathf.Max(0f, wantedEye - position.y);

            if (lift <= 0.001f)
            {
                borrowed.SetPositionAndRotation(position, viewRotation);
                return;
            }

            position += Vector3.up * lift;

            // Re-aimed at the rank's own head height, so the lift frames the astronauts rather than
            // sliding them out of the bottom of the shot. This is the ONE case where the authored
            // rotation is not reproduced — and it cannot happen with a single row, where the lift is
            // zero by construction.
            Vector3 toTarget = anchor.position + Vector3.up * RankLayout.HeadHeight - position;

            borrowed.SetPositionAndRotation(
                position,
                toTarget.sqrMagnitude < 0.0001f ? viewRotation : Quaternion.LookRotation(toTarget, Vector3.up));
        }

        /// <summary>
        /// How much of the frame's height the rank may use, as a fraction, once the status line and
        /// footer have taken theirs.
        ///
        /// <para>
        /// Asked of <see cref="UIScale"/> rather than derived here. This used to compute the canvas
        /// height as <c>1920 * Screen.height / Screen.width</c>, which is the answer for a scaler
        /// matching WIDTH — not the rule the lobby's canvas follows. It read about 14% short on a
        /// 21:9 monitor, so the chrome looked like a bigger share of the canvas than it is and the
        /// fit backed the camera off further than the shot needed.
        /// </para>
        ///
        /// <para>
        /// Under the project's rule the canvas is never shorter than the 1080 the chrome is measured
        /// in, so this is a constant 0.80 at 16:9 and at every wider aspect, and grows on a window
        /// narrower than 16:9 — which genuinely has more vertical room to give the rank.
        /// </para>
        /// </summary>
        private static float BandFraction()
        {
            float canvasHeight = UIScale.CanvasSize().y;

            return Mathf.Clamp(1f - ChromeBottom / Mathf.Max(1f, canvasHeight), 0.2f, 1f);
        }
    }
}
