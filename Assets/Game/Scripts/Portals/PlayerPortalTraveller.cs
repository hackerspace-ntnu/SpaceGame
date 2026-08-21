// A player going through a portal.
//
// The generic traveller poses a transform and calls it done, which is correct
// for a crate and wrong for a player, because a player's look direction is not
// entirely in their transform. PlayerLook keeps pitch as a float and spends yaw
// on the Rigidbody a physics step later, so applying a portal's rotation to the
// body alone produces two distinct failures: a portal on a floor lays the player
// on their side, and the banked yaw from before the traversal is spent after it,
// turning the view by an amount that meant something in the room they left.
//
// So the traversal is decomposed. The body takes only the yaw, which keeps the
// player upright the way every portal game does; the pitch goes to the camera
// where it already lived. What the player sees does not change through the
// aperture — which is the whole point, since they were looking through it a
// frame ago.
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core.Persistence;

namespace SpaceGame.Portals
{
    [DisallowMultipleComponent]
    public sealed class PlayerPortalTraveller : PortalTraveller
    {
        [Tooltip("The look rig. Resolved from this object's hierarchy when left empty.")]
        [SerializeField] private PlayerLook look;

        private PlayerMovement movement;

        protected override void Awake()
        {
            base.Awake();
            if (look == null) look = GetComponentInChildren<PlayerLook>(true);
            movement = GetComponent<PlayerMovement>();
        }

        protected override void ApplyTraversal(Portal from, Portal to, Matrix4x4 transfer,
                                               Vector3 position, Quaternion rotation)
        {
            // Before the move, because the caller restores the rotated velocity
            // immediately after this returns and the very next FixedUpdate is
            // where it would otherwise be lerped away. See PlayerMovement.
            if (movement != null) movement.CarryMomentum();

            if (look == null || look.cameraRoot == null)
            {
                base.ApplyTraversal(from, to, transfer, position, rotation);
                return;
            }

            // Where the player is looking now, carried through the aperture.
            Vector3 lookDirection = transfer.MultiplyVector(look.cameraRoot.forward);

            // Upright: the body keeps only the horizontal part of the new
            // facing. A ceiling portal composes a full 180 degree roll into the
            // transfer matrix, and handing that to a capsule that walks on its
            // feet is how a player ends up lying down in mid-air.
            Vector3 flat = Vector3.ProjectOnPlane(lookDirection, Vector3.up);
            if (flat.sqrMagnitude < 1e-6f)
            {
                // Looking straight up or down through the aperture: the yaw is
                // undefined from the view, so take it from the body instead.
                flat = Vector3.ProjectOnPlane(transfer.MultiplyVector(transform.forward), Vector3.up);
            }
            if (flat.sqrMagnitude < 1e-6f) flat = transform.forward;

            Quaternion upright = Quaternion.LookRotation(flat.normalized, Vector3.up);

            SaveTeleport.Move(gameObject, position, upright, zeroVelocity: false);

            // Pitch last, and through PlayerLook rather than the camera
            // transform, or Update overwrites it from the stored float on the
            // very next frame and the view snaps level.
            look.LookAlong(lookDirection);
        }
    }
}
