// Put a saddle on an animal. A placeable whose "ground" is a living creature.
//
// Criteria: the thing aimed at has a SaddleSocket, and is not already wearing one.
// Logic:    ask that socket to fit — which is NOT a spawn. The worn saddle is a plain Instantiate
//           parented to a bone on every machine, driven by one replicated bool, so there is no
//           NetworkObject to create here and nothing for World.Spawn to do.
//
// This is the case that shows why placement is a strategy rather than a flag on the item: nothing
// in PlaceableItem knows what an animal or a bone is, and nothing has to.
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.Items
{
    public class SaddlePlacement : PlacementRule
    {
        [Tooltip("Only fits sockets whose saddle item is this one. Left empty, any socket will " +
                 "take it — which is what you want while there is one saddle, and not what you " +
                 "want the day a bison saddle should refuse to go on a lizard.")]
        [SerializeField] private InventoryItem fitsSocketsFor;

        public override bool CanPlace(in PlacementAim aim)
        {
            return SocketFor(aim) != null;
        }

        public override bool Place(in PlacementAim aim, GameObject placer)
        {
            SaddleSocket socket = SocketFor(aim);

            // Fit() answers whether it actually went on, and PlaceableItem spends the item on that
            // answer. A click at an animal already wearing one must not eat the saddle.
            return socket != null && socket.Fit();
        }

        public override string RefusalHint(in PlacementAim aim)
        {
            GameObject target = aim.Target;
            if (target == null) return null;

            SaddleSocket socket = target.GetComponentInParent<SaddleSocket>();
            if (socket == null) return null;                 // not an animal: say nothing
            return socket.IsSaddled ? "Already saddled" : null;
        }

        /// <summary>
        /// The socket this aim would fit, or null.
        ///
        /// <para>
        /// GetComponentInParent, because an animal's collider is on its root but a ray can just as
        /// easily land on a child — a horn, a hoof, the saddle already on its back.
        /// </para>
        /// </summary>
        private SaddleSocket SocketFor(in PlacementAim aim)
        {
            if (aim.Target == null) return null;

            SaddleSocket socket = aim.Target.GetComponentInParent<SaddleSocket>();
            if (socket == null || socket.IsSaddled) return null;

            if (fitsSocketsFor != null && socket.SaddleItem != fitsSocketsFor) return null;

            return socket;
        }
    }
}
