// The over-the-shoulder view while the jetpack is flying.
//
// It exists because the machine is on your BACK. In first person a jetpack pilot cannot see their
// own pods vector, their flames scale with the throttle, or the tips going red — every piece of
// feedback the pack produces is behind the camera, and the only readout left is the visor gauge.
// Stepping the lens back is what makes the hardware readable at all (GDC-L1-UX-0003).
using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Pulls the player's own camera back and up while it is enabled, and puts it back exactly
    /// where it was when it is not.
    ///
    /// <para>
    /// <b>It moves the existing camera rather than spawning one.</b> The mount's third-person
    /// camera is a whole subsystem — spawned unparented, tagged with <c>MountRuntimeCamera</c>,
    /// swept by an editor hook because orphans survived domain reloads and rendered over the
    /// player's own view for days. None of that is worth inheriting to look at your own back:
    /// there is already a camera pointing where the player is looking, and it only needs to stand
    /// further away.
    /// </para>
    /// <para>
    /// Written in <c>LateUpdate</c>, after <c>PlayerLook</c>'s <c>Update</c>, and always from the
    /// pose captured on enable rather than from the current one — otherwise each frame's offset
    /// compounds on the last and the camera walks off into the desert. <c>PlayerLook</c> owns the
    /// lens's ROTATION and is left alone; this only writes position.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(960)]
    public class JetpackThirdPerson : MonoBehaviour
    {
        [Tooltip("How far behind the player the lens stands, metres. Far enough to see the pods " +
                 "on the wearer's back, which is the whole reason this exists.")]
        [SerializeField, Min(0f)] private float distance = 4.2f;

        [Tooltip("How far above the eye, metres. A little, so the shot looks down the player's " +
                 "back rather than through the back of their helmet.")]
        [SerializeField] private float height = 0.9f;

        [Tooltip("How fast the lens travels between first and third person, per second. Not " +
                 "instant: a cut on the frame the motors light reads as a camera bug rather than " +
                 "as the view stepping out.")]
        [SerializeField, Min(0.01f)] private float ease = 6f;

        [Tooltip("Radius of the sphere cast that keeps the lens out of walls. Bigger pulls in " +
                 "earlier and never lets the near plane clip through geometry.")]
        [SerializeField, Min(0.01f)] private float probeRadius = 0.35f;

        [Tooltip("How much clear space to leave between the lens and whatever it backed into.")]
        [SerializeField, Min(0f)] private float wallClearance = 0.2f;

        [SerializeField] private LayerMask blocking = ~0;

        private PlayerLook look;
        private Transform lens;
        private Vector3 restLocalPosition;
        private bool captured;

        /// <summary>
        /// How far out the view currently is, 0..1. Held across enable/disable so a flight that
        /// ends part-way through the step-out eases back from where it actually was.
        /// </summary>
        private float extent;

        private void Awake()
        {
            look = GetComponentInChildren<PlayerLook>();
            if (look != null) lens = look.cameraRoot;

            Capture();
        }

        /// <summary>
        /// Remember where the lens sits in first person. Taken once, and never while the offset is
        /// applied — capturing a displaced pose is how "put it back" becomes "leave it there".
        /// </summary>
        private void Capture()
        {
            if (captured || lens == null) return;

            restLocalPosition = lens.localPosition;
            captured = true;
        }

        private void OnEnable()
        {
            Capture();

            // The player's own body and pack are hidden from their own camera in first person.
            // In third person they are the thing being looked at.
            if (look != null) look.SetFirstPersonHidden(false);
        }

        private void OnDisable()
        {
            if (look != null) look.SetFirstPersonHidden(true);

            // Put it back, on this frame rather than over the next few: the component is disabled
            // when the flight ends, so there is nothing left running to finish an ease.
            if (lens != null && captured) lens.localPosition = restLocalPosition;
            extent = 0f;
        }

        private void LateUpdate()
        {
            if (lens == null || !captured) return;

            extent = Mathf.MoveTowards(extent, 1f, ease * Time.deltaTime);

            // The offset is in the EYE PARENT's space, not the lens's own. The lens carries the
            // look pitch, so an offset along its local -Z would swing under the player when they
            // looked down and end up in front of them; the parent's frame is the body's, which
            // only ever yaws.
            Vector3 wanted = restLocalPosition
                             + Vector3.back * (distance * extent)
                             + Vector3.up * (height * extent);

            lens.localPosition = PulledIn(wanted);
        }

        /// <summary>
        /// The same offset, shortened if there is a wall in the way.
        ///
        /// <para>
        /// A sphere cast rather than a ray, because a ray threads gaps a camera cannot fit through
        /// and the failure is the near plane ending up inside a rock. Cast from the resting eye —
        /// which is inside the player's own head, so the player's colliders have to be ignored;
        /// <c>QueryTriggerInteraction.Ignore</c> also keeps interaction volumes from shoving the
        /// shot about.
        /// </para>
        /// </summary>
        private Vector3 PulledIn(Vector3 wantedLocal)
        {
            Transform parent = lens.parent;
            if (parent == null) return wantedLocal;

            Vector3 from = parent.TransformPoint(restLocalPosition);
            Vector3 to = parent.TransformPoint(wantedLocal);

            Vector3 delta = to - from;
            float reach = delta.magnitude;
            if (reach <= 1e-3f) return wantedLocal;

            Vector3 direction = delta / reach;

            if (!Physics.SphereCast(from, probeRadius, direction, out RaycastHit hit, reach,
                                    blocking, QueryTriggerInteraction.Ignore))
                return wantedLocal;

            // Anything belonging to this player is not a wall — the capsule the cast starts inside
            // would otherwise stop it immediately and pin the camera to the eye.
            if (hit.transform.IsChildOf(transform)) return wantedLocal;

            float allowed = Mathf.Max(0f, hit.distance - wallClearance);
            return parent.InverseTransformPoint(from + direction * allowed);
        }
    }
}
