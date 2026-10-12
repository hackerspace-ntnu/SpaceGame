using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    /// <summary>
    /// The one way an NPC uses an item, held or worn: owner hook, presentation, effect, then the peers.
    /// Moved out of EntityEquipmentController.TryUseAt when worn gauntlets needed the identical sequence.
    /// Server only (callers gate on Network.Simulates).
    /// </summary>
    public static class NpcItemFire
    {
        /// <summary>How far ahead "use it forward" aims: far enough that any aimed item reads it as a direction.</summary>
        public const float ForwardReach = 100f;

        /// <summary>
        /// How far below its own origin "use it on yourself" aims. The ground under the feet rather than
        /// nothing, because an aimed item given a degenerate direction falls back to its holder's forward,
        /// and a healing item that also raycasts would hit whatever stands in front of the NPC.
        /// </summary>
        public const float SelfAimDrop = 0.5f;

        /// <summary>
        /// A use aimed from <paramref name="origin"/> at <paramref name="aimPoint"/>, under
        /// <paramref name="slotCode"/>; faces <paramref name="fallback"/> when the two points coincide.
        /// </summary>
        public static NetArg AimedArg(int slotCode, Vector3 origin, Vector3 aimPoint, Quaternion fallback)
        {
            Vector3 direction = aimPoint - origin;
            return new NetArg
            {
                A = slotCode,
                P = origin,
                R = direction.sqrMagnitude > UsableItem.MinAimDistanceSqr
                    ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                    : fallback,
            };
        }

        public static void Fire(Component host, UsableItem item, NetArg arg)
        {
            // Owner-side hook first, exactly as the player path does it: an item that describes its own
            // use (a grapple reporting where it hooks, a gauntlet reading HolderAimRay) overrides the aim.
            item.OnRequestUse(ref arg);

            // Presentation before effect, matching EquipmentController: Weapon.Present() returns early on
            // the simulating machine after its report, so this puts no second bullet in the air.
            item.PlayUse(host.gameObject, arg);
            item.TryUse(host.gameObject, arg);

            // Peers. Nothing is excluded: no other machine has presented an NPC's use locally.
            host.NetToOthers(NetMsg.ItemUsed, arg);
        }
    }
}
