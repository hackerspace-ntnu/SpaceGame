using SpaceGame.Core.Persistence;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Characters
{
    [RequireComponent(typeof(PlayerController))]
    public class NetworkPlayerController : NetworkBehaviour
    {
        private PlayerController controller;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
        }

        public override void OnNetworkSpawn()
        {
            AdoptSpawnPose();

            if (IsOwner)
            {
                controller.EnablePlayer();
            }
            else
            {
                controller.DisablePlayer();
            }
        }

        /// <summary>
        /// Put the body where Netcode has just put the transform, before the body writes its own
        /// pose back over it.
        ///
        /// This one call decides where a remote player stands, and without it they stand in the
        /// prefab. The server spawns a player with Instantiate(prefab, position, rotation), so its
        /// body is BORN at the spawn point and nothing is wrong locally. Every other machine gets
        /// the object through NetworkSpawnManager.InstantiateNetworkPrefab, which does the two
        /// steps in the other order:
        ///
        ///     var networkObject = Object.Instantiate(networkPrefab)...;          // at the PREFAB's pose
        ///     networkObject.transform.SetPositionAndRotation(position, rotation); // moved afterwards
        ///
        /// That trailing move is a transform write, and a transform write does not relocate a body
        /// in this project — the exact failure <see cref="SaveTeleport"/> exists for. The player's
        /// Rigidbody is dynamic with interpolation on, so it restores the pose it last simulated
        /// (still the authored prefab pose) within the frame, and the player materialises hundreds
        /// of metres from the spawn point.
        ///
        /// Nothing corrects it afterwards, either: the player's NetworkTransform is
        /// OWNER-authoritative, so on the joining client the owner IS the authority. The wrong
        /// position is not overwritten by the server, it is PUBLISHED to it as the truth, and the
        /// world then streams its chunks around a player who was never there.
        ///
        /// Runs on every instance rather than just the owner. A remote replica is made kinematic by
        /// NetworkRigidbody and would be dragged into place by the next state update anyway, but
        /// doing it here costs nothing and spares it a visible slide across the map.
        /// </summary>
        private void AdoptSpawnPose() =>
            SaveTeleport.Move(gameObject, transform.position, transform.rotation);
    }
}
