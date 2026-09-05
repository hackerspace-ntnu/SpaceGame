using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Where this player is pointing — the one answer every aimed item in the game is built on.
    ///
    /// <para>
    /// The contract is the crosshair. It is drawn at the centre of whichever camera this player is
    /// looking through, so the ray this class returns has to land where that crosshair sits;
    /// anything else is the HUD promising one thing and the item doing another. On foot that is the
    /// player's own eye and the ray is simply its forward, which is why <c>PlayerLook</c> may slide
    /// the eye forward on a downward pitch and take the aim with it.
    /// </para>
    /// <para>
    /// Mounted it is not. A rider is parented under the seat marker wearing the seat's rotation —
    /// the ornithopter's cradle is rotated ninety degrees, because a prone pilot faces the floor —
    /// and the camera they are actually watching is the mount's orbit camera, metres behind the
    /// craft. So whatever takes a player's view away from their own eye says so through
    /// <see cref="SetExternalView"/>, and the ray is then built by CONVERGENCE: it still leaves the
    /// eye, so darts and beams come out of the player rather than out of thin air behind them, and
    /// it points at whatever that view's crosshair covers.
    /// </para>
    /// <para>
    /// The machine a player is strapped into is transparent to all of this. <c>MountModule</c> tells
    /// the solver to ignore rider/mount collisions, but a raycast is a query and queries do not
    /// care, so the hull has to be filtered out by hand — the same rule ground probes follow.
    /// </para>
    /// </summary>
    public class AimProvider : MonoBehaviour
    {

        [SerializeField] private Camera playerCamera;

        [Header("Aiming through another view")]
        [Tooltip("How far the crosshair looks for something to converge on, in metres. Sky is " +
                 "aimed at from this distance, which is what keeps the parallax between the view " +
                 "and the eye down to a fraction of a degree.")]
        [SerializeField] private float focusRange = 500f;

        [Tooltip("What the crosshair may settle on. Triggers are excluded whatever this says — a " +
                 "detection volume is not a surface.")]
        [SerializeField] private LayerMask focusLayers = ~0;

        /// <summary>
        /// Scratch space for the filtered casts below. Shared because it holds nothing between
        /// calls: every read of it finishes inside the call that filled it.
        /// </summary>
        private static readonly RaycastHit[] Hits = new RaycastHit[32];

        /// <summary>The camera this player's screen is drawn from, while it is not their own eye.</summary>
        private Camera externalView;

        /// <summary>The machine they are inside while that is true. See <see cref="Carrier"/>.</summary>
        private Transform externalCarrier;

        /// <summary>
        /// The transform every aim in the game is measured from, or null if none is wired. Exposed
        /// so other systems can agree with the aim rather than hunting for a camera of their own —
        /// GetComponentInChildren&lt;Camera&gt; finds inactive and secondary cameras too, and a
        /// system that picks the wrong one silently disagrees with where the player is pointing.
        ///
        /// <para>
        /// This is the eye, not necessarily the view: mounted, the eye is still the pilot's head in
        /// the cradle and is still where a shot leaves from, but it is not what they are looking
        /// through. Read <see cref="GetAimRay"/> for a direction; read this for a position.
        /// </para>
        /// </summary>
        public Transform AimTransform => playerCamera != null ? playerCamera.transform : null;

        /// <summary>
        /// Hand this player's view to a camera that is not their own eye, and name the machine
        /// between the two.
        ///
        /// <para>
        /// Called by whatever took the view over — today only <c>MountModule</c>, on the local rider
        /// alone, for both of its perspectives. A first-person seat passes a null
        /// <paramref name="view"/>: the eye is still the view there, but the hull around it must
        /// still be looked past.
        /// </para>
        /// <para>
        /// Both halves are checked for staleness on every read rather than trusted, so a ride that
        /// ended by some path that never reached <see cref="ClearExternalView"/> — a rider abandoned
        /// with their mount, a scene torn down — cannot leave a player aiming through a camera that
        /// is gone.
        /// </para>
        /// </summary>
        public void SetExternalView(Camera view, Transform carrier)
        {
            externalView = view;
            externalCarrier = carrier;
        }

        /// <summary>Give the view back to the player's own eye. The mirror of <see cref="SetExternalView"/>.</summary>
        public void ClearExternalView() => SetExternalView(null, null);

        /// <summary>
        /// The camera this player is looking through: the external view while there is a live one,
        /// otherwise their own eye. A view that has been destroyed or switched off is no view at
        /// all — MountModule disables the orbit camera rather than destroying it on a perspective
        /// change, and destroys it without a word when a rider is abandoned.
        /// </summary>
        public Camera ViewCamera =>
            externalView != null && externalView.isActiveAndEnabled ? externalView : playerCamera;

        /// <summary>
        /// The machine this player is riding, or null on their own feet. Confirmed against the
        /// hierarchy rather than trusted: mounting parents the rider under the mount, so a rider
        /// who is no longer under it is a rider who has left.
        /// </summary>
        private Transform Carrier =>
            externalCarrier != null && transform.IsChildOf(externalCarrier) ? externalCarrier : null;

        /// <summary>
        /// Where this player is pointing right now: out of their eye, at what their crosshair covers.
        ///
        /// <para>
        /// First person is the identity case and is deliberately left bit-for-bit unchanged — the
        /// eye is the view, there is no parallax to correct and nothing is cast. It falls back to
        /// the body's own forward when there is no camera at all, which is every peer's copy of a
        /// remote player; an item must report its aim in its use message rather than recompute it
        /// there, and this is only so that asking does not throw.
        /// </para>
        /// </summary>
        public Ray GetAimRay()
        {
            Transform eye = AimTransform != null ? AimTransform : transform;
            Camera view = ViewCamera;

            if (view == null || view.transform == eye)
                return new Ray(eye.position, eye.forward);

            Vector3 toFocus = FocusPoint(view) - eye.position;
            return toFocus.sqrMagnitude > 1e-6f
                ? new Ray(eye.position, toFocus.normalized)
                : new Ray(eye.position, view.transform.forward);
        }

        /// <summary>
        /// The nearest thing under the aim that this player could act on, skipping their own body
        /// and the machine they are riding.
        ///
        /// <para>
        /// Quiet on a miss. Pointing a beam weapon or a grappling hook at open sky is an entirely
        /// ordinary thing to do, and at fifteen hold ticks a second a warning per miss buries the
        /// console — which is why two call sites used to hand-roll their own cast to avoid this one,
        /// and so quietly skipped the filtering.
        /// </para>
        /// </summary>
        public bool TryGetAimHit(float maxDistance, out RaycastHit hit) =>
            TryGetAimHit(maxDistance, ~0, out hit);

        /// <inheritdoc cref="TryGetAimHit(float, out RaycastHit)"/>
        public bool TryGetAimHit(float maxDistance, LayerMask layers, out RaycastHit hit)
        {
            // GetAimRay borrows the same buffer and is finished with it before this fills it again.
            Ray ray = GetAimRay();

            int count = Physics.RaycastNonAlloc(ray, Hits, maxDistance, layers,
                                                QueryTriggerInteraction.Ignore);
            return NearestOutside(Hits, count, transform, Carrier, out hit);
        }

        /// <summary>
        /// What the crosshair of <paramref name="view"/> is over, or a point <see cref="focusRange"/>
        /// away down its centre line when it is over nothing.
        ///
        /// <para>
        /// Taken through the viewport centre rather than off the camera's forward because the
        /// crosshair is a screen position, and that is the only form that stays true if a view is
        /// ever drawn off-centre.
        /// </para>
        /// </summary>
        private Vector3 FocusPoint(Camera view)
        {
            Ray look = view.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            int count = Physics.RaycastNonAlloc(look, Hits, focusRange, focusLayers,
                                                QueryTriggerInteraction.Ignore);

            return NearestOutside(Hits, count, transform, Carrier, out RaycastHit hit)
                ? hit.point
                : look.GetPoint(focusRange);
        }

        /// <summary>
        /// The nearest hit in <paramref name="hits"/> that belongs to neither <paramref name="self"/>
        /// nor <paramref name="carrier"/>.
        ///
        /// <para>
        /// Static and taking its own buffer so the rule can be tested without a camera or a mount.
        /// <c>RaycastNonAlloc</c> does not sort and truncates once the buffer is full, so the
        /// nearest is picked by hand. And the parentage is asked of the COLLIDER:
        /// <c>RaycastHit.transform</c> is the rigidbody's, which over a vehicle is its root — so a
        /// filter written against it matches everything or nothing.
        /// </para>
        /// </summary>
        public static bool NearestOutside(RaycastHit[] hits, int count, Transform self,
                                          Transform carrier, out RaycastHit nearest)
        {
            nearest = default;
            float best = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || hits[i].distance >= best)
                    continue;

                Transform hitTransform = collider.transform;
                if (self != null && hitTransform.IsChildOf(self))
                    continue;
                if (carrier != null && hitTransform.IsChildOf(carrier))
                    continue;

                best = hits[i].distance;
                nearest = hits[i];
            }

            return best < float.PositiveInfinity;
        }
    }
}
