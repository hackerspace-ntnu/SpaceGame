// An item you put into the world, and can take back.
//
// This class owns the LOOP and deliberately nothing else:
//
//   1. aim        on the holder's machine, the only one whose crosshair is honest
//   2. validate   ask the rule whether it may go there, before anything leaves
//   3. place      on the server, through the rule
//   4. spend      only if the rule says the world actually changed
//
// What "placing" MEANS is a PlacementRule on the same prefab. A lantern wants flat ground and
// spawns a prefab; a saddle wants an animal with a socket and fits itself to a bone. Neither
// spelling of that belongs here -- which is why a saddle is a placeable at all, rather than a
// second copy of this loop living under another name.
//
// The loop conserves: placing spends the item, retrieving returns it, and at no point are there
// two. That is why nothing is consumed until the rule reports success.
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// Puts a thing into the world under its <see cref="PlacementRule"/>, and spends itself doing
    /// it. Use it directly with a different rule and item asset; subclass only for a placeable
    /// that needs to do something extra at the moment of placing.
    /// </summary>
    public class PlaceableItem : ToolItem
    {
        /// <summary>Server: a thing standing in the world is world state, not the holder's body.</summary>
        public override UseAuthority Authority => UseAuthority.Server;

        [Tooltip("This item's criteria and placement logic. Found on this prefab when left empty. " +
                 "Without one the item cannot be placed at all.")]
        [SerializeField] private PlacementRule rule;

        [Tooltip("How far away it can be placed, in metres.")]
        [SerializeField] private float range = 4f;

        [Tooltip("Played on every machine as it goes down. Cosmetic.")]
        [SerializeField] private SpaceGame.Audio.SfxId placeSound =
            SpaceGame.Audio.SfxId.NpcMumbleFriendly;

        private PlacementRule Rule => rule != null ? rule : rule = GetComponent<PlacementRule>();

        /// <summary>
        /// Owner-side: the only machine whose aim is honest. The server's <c>Camera.main</c> is the
        /// HOST's camera, so a ray recomputed there would place every client's item at the host's
        /// feet — or saddle whatever the host happened to be looking at.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            RaycastHit? hit = aimProvider != null ? aimProvider.GetRayCast(range) : null;
            if (!hit.HasValue || Rule == null) return;

            var aim = new PlacementAim(hit.Value.point, hit.Value.normal,
                                       hit.Value.collider != null ? hit.Value.collider.gameObject : null,
                                       PlacerYaw());

            // Refused here costs no round trip and, more importantly, no item.
            if (!Rule.CanPlace(aim)) return;

            arg.P = aim.Point;
            arg.B = Mathf.RoundToInt(aim.Yaw);
            arg = arg.With(aim.Target);
        }

        /// <summary>
        /// Which way the placer is facing, from the AIM ray rather than from <c>owner</c>: this
        /// runs before the request leaves, and <c>owner</c> is only assigned authority-side in
        /// <c>TryUse</c>, so reading it here gets whatever the previous use left behind.
        /// </summary>
        private float PlacerYaw()
        {
            if (aimProvider == null) return 0f;

            Vector3 facing = Vector3.ProjectOnPlane(aimProvider.GetAimRay().direction, Vector3.up);
            return facing.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(facing.normalized, Vector3.up).eulerAngles.y
                : 0f;
        }

        /// <summary>Authority only. Nothing is spent unless the rule put something somewhere.</summary>
        protected override void Use()
        {
            if (Rule == null) return;

            // No normal on this side: NetArg does not carry one, and the owner already tested the
            // slope with a real one. Rules are written knowing that — see GroundPlacement.
            var aim = new PlacementAim(UseArg.P, Vector3.zero, UseArg.Resolve(), UseArg.B);
            if (!aim.IsValid) return;

            // Asked again here, because the first answer came from a machine that decides nothing.
            if (!Rule.Place(aim, owner)) return;

            Deplete();
        }

        protected override void Present()
        {
            SpaceGame.Audio.Sfx.Play(placeSound, transform.position, GetInstanceID());
        }

        private void OnValidate() => range = Mathf.Max(0.5f, range);
    }
}
