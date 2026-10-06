// Deploying a craft for a pilot, the two halves shared by the player's wing pack (WingPackItem) and a
// Sky nomad's flight (NpcFlightModule / NpcAviator), so neither copies the other.
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    public static class CraftDeployment
    {
        /// <summary>
        /// Where the craft's root has to be spawned for its SEAT to land on the pilot, lifted by
        /// <paramref name="lift"/>. <paramref name="seatPoint"/> is the seat in the same space as
        /// <paramref name="craftRoot"/> — read off the PREFAB, before the craft exists, because the
        /// spawn pose is the only pose that replicates (Ornithopter.md Gotchas).
        /// </summary>
        public static Vector3 LaunchPosition(Transform craftRoot, Vector3 seatPoint, Vector3 pilotPosition,
                                             Quaternion facing, float lift)
        {
            Vector3 lifted = pilotPosition + Vector3.up * lift;
            if (craftRoot == null) return lifted;

            // The seat in the root's own frame, turned to the launch heading: the craft is spawned
            // rotated, so an offset measured in the prefab's frame has to turn with it.
            Vector3 seatLocal = craftRoot.InverseTransformPoint(seatPoint);
            return lifted - facing * seatLocal;
        }

        /// <summary>
        /// Take a craft out of the world for everybody. Only the server may retire a networked object;
        /// on a client the authoritative despawn arrives from the server, so nothing is destroyed out
        /// from under it here.
        /// </summary>
        public static void Retire(GameObject craft)
        {
            if (craft == null) return;

            if (Network.IsNetworked && !Network.Server &&
                craft.TryGetComponent(out NetworkObject netObj) && netObj.IsSpawned)
                return;

            GameServices.World.Despawn(craft);
        }
    }
}
