// Moves a networked body somewhere, and makes the move stick.
//
// Placing an entity is not the same problem as moving it. The player's NetworkTransform is
// owner-authoritative (AuthorityMode: Owner), which means the server does not get to write a remote
// player's position: the owner's next state update overwrites it, within a tick, silently. Every
// system in this project that teleports a player has run into that independently — MatchManager
// solved it with an RPC of its own, the respawn flow and the interior loader did not, and both of
// those simply did nothing for anyone but the host.
//
// So the rule lives in one place: whoever OWNS the body performs the move, and everybody else finds
// out through the transform sync that is already running.
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core.Persistence;

namespace SpaceGame.Core
{
    /// <summary>
    /// Authoritative placement for one entity. Add it beside a <see cref="NetworkObject"/> whose
    /// transform is owner-driven; anything else can be moved directly and does not need this.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkedTeleport : NetworkBehaviour
    {
        /// <summary>
        /// Put <paramref name="target"/> at <paramref name="position"/>, from the server or offline.
        ///
        /// Degrades the way the rest of this codebase does: an entity with no
        /// <see cref="NetworkedTeleport"/>, or one that is not spawned, or a session that is not
        /// networked at all, is simply moved here and now. That is the right answer for a bot, a
        /// prop, or single-player, and it means a caller never has to ask which case it is in.
        /// </summary>
        public static void Move(GameObject target, Vector3 position, Quaternion rotation)
        {
            if (target == null) return;

            var teleport = target.GetComponent<NetworkedTeleport>();
            if (teleport == null || !teleport.IsSpawned || !Network.IsNetworked)
            {
                Place(target, position, rotation);
                return;
            }

            teleport.MoveInternal(position, rotation);
        }

        private void MoveInternal(Vector3 position, Quaternion rotation)
        {
            // Ours already — no round trip, and the host keeps the same code path as offline play.
            if (IsOwner)
            {
                Place(gameObject, position, rotation);
                return;
            }

            if (!IsServer)
            {
                Debug.LogWarning($"[Net] '{name}' was told to teleport by a machine that neither owns " +
                                 "it nor is the server. Ignored — the move would not have replicated.", this);
                return;
            }

            PlaceOnOwnerRpc(position, rotation, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void PlaceOnOwnerRpc(Vector3 position, Quaternion rotation, RpcParams rpcParams) =>
            Place(gameObject, position, rotation);

        /// <summary>
        /// The move itself, via <see cref="SaveTeleport"/> because assigning transform.position is
        /// not enough for anything that drives itself: a CharacterController caches its own position
        /// and writes it back on its next Move, a NavMeshAgent navigates from its own, and a
        /// Rigidbody keeps whatever velocity it arrived with — so a body teleported mid-fall resumes
        /// falling in a place where that means nothing.
        /// </summary>
        private static void Place(GameObject target, Vector3 position, Quaternion rotation) =>
            SaveTeleport.Move(target, position, rotation);
    }
}
