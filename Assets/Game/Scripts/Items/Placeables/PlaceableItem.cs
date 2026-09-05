// An item you put down in the world, and can pick back up.
//
// The two halves are separate objects on purpose: this is the thing in your hand, and what it
// spawns is the thing on the ground. They are not the same prefab wearing a different hat -- the
// held one has a grip pose and a hold animation, the placed one has a footprint, a collider that
// stops you walking through it, and whatever it is actually FOR.
//
// The loop is meant to conserve: placing spends the item, retrieving returns it, and at no point
// do you have two. That is why the placement consumes only after the spawn succeeded, and why the
// placed prefab's PlacedObject must name this same item asset as what it returns.
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// Puts <see cref="placedPrefab"/> down where the holder is aiming and spends itself doing it.
    ///
    /// <para>
    /// Subclass it when a placeable needs to do something extra as it lands; most do not and can
    /// use this directly with a different prefab and item asset.
    /// </para>
    /// </summary>
    public class PlaceableItem : ToolItem
    {
        /// <summary>Server: a thing standing in the world is world state, not the holder's body.</summary>
        public override UseAuthority Authority => UseAuthority.Server;

        [Tooltip("What ends up on the ground. MUST be a registered network prefab, and its " +
                 "PlacedObject must return THIS item's asset — otherwise placing and picking up " +
                 "either transmutes the item or loses it. PlaceableItemTests checks that pairing.")]
        [SerializeField] private GameObject placedPrefab;

        [Tooltip("How far away it can be placed, in metres.")]
        [SerializeField] private float range = 4f;

        [Tooltip("Steepest ground it will stand on, in degrees. Anything sharper and it would " +
                 "sit half-buried in a dune face or slide.")]
        [SerializeField] private float maxGroundAngle = 35f;

        [Tooltip("Face it away from the placer, rather than keeping the prefab's own rotation. " +
                 "What you want for anything with a front — a chair, a workbench, a sign.")]
        [SerializeField] private bool faceAwayFromPlacer = true;

        [Tooltip("Played on every machine as it goes down. Cosmetic.")]
        [SerializeField] private SpaceGame.Audio.SfxId placeSound =
            SpaceGame.Audio.SfxId.NpcMumbleFriendly;

        /// <summary>
        /// Owner-side: the only machine whose aim is honest. The server's Camera.main is the
        /// HOST's camera, so a ray recomputed there would place every client's item at the host's
        /// feet.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            RaycastHit? hit = aimProvider != null ? aimProvider.GetRayCast(range) : null;
            if (!hit.HasValue) return;

            // Refuse a dune face. Left in, the thing stands at the angle of the slope and ends up
            // half-buried, and P being zero on the far side is already the "nothing was aimed at"
            // signal, so declining here needs no second channel.
            if (Vector3.Angle(hit.Value.normal, Vector3.up) > maxGroundAngle) return;

            // Zero means "aimed at open sky", and is checked for on the far side. Without it the
            // `?? Vector3.zero` reads as a position and the thing lands at the world origin.
            arg.P = hit.Value.point;

            // Which way it faces, as a yaw the far side can use directly. Taken from the AIM ray,
            // not from `owner`: this runs before the request leaves, and `owner` is only assigned
            // authority-side in TryUse, so reading it here gets whatever the last use left behind.
            Vector3 facing = Vector3.ProjectOnPlane(aimProvider.GetAimRay().direction, Vector3.up);
            arg.B = Mathf.RoundToInt(
                facing.sqrMagnitude > 1e-4f
                    ? Quaternion.LookRotation(facing.normalized, Vector3.up).eulerAngles.y
                    : 0f);
        }

        /// <summary>Authority only. Nothing is spent unless something was actually put down.</summary>
        protected override void Use()
        {
            if (placedPrefab == null || UseArg.P == Vector3.zero) return;

            // Facing away from the placer means facing the way they were looking.
            Quaternion rotation = faceAwayFromPlacer
                ? Quaternion.Euler(0f, UseArg.B, 0f)
                : Quaternion.identity;

            GameObject placed = GameServices.World.Spawn(placedPrefab, UseArg.P, rotation);
            if (placed == null) return;

            // Only now. Deplete before the spawn and a missing prefab registration eats the item
            // and leaves nothing standing where it was aimed.
            Deplete();
        }

        protected override void Present()
        {
            SpaceGame.Audio.Sfx.Play(placeSound, transform.position, GetInstanceID());
        }

        private void OnValidate()
        {
            range = Mathf.Max(0.5f, range);
            maxGroundAngle = Mathf.Clamp(maxGroundAngle, 0f, 89f);
        }
    }
}
