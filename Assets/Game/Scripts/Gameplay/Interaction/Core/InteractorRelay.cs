// Naming the player on both ends of an interaction RPC.
//
// Every server-authoritative interactable has the same two lines to write, and every one of them
// got them wrong the same way: `interactor.GetComponent<NetworkObject>()` on the way out and
// `netObj.GetComponent<Interactor>()` on the way back. Neither holds. An Interactor sits wherever
// the player prefab puts it — on the camera rig here, not the body — so the outbound lookup returns
// null, which becomes a default NetworkObjectReference that resolves to nothing, and the inbound
// lookup misses a component that is not on the root. Interacting from a client did nothing at all,
// on every interactable in the game, and it looked like a networking problem rather than two
// lookups reaching one level too shallow.
using System;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>The player-identification half of an interaction RPC, in one place.</summary>
    public static class InteractorRelay
    {
        /// <summary>
        /// Client side: resolve <paramref name="interactor"/> to the networked body it belongs to
        /// and hand it to <paramref name="send"/>. Does nothing, loudly, if there is no such body —
        /// which is a prefab error, not something to swallow.
        /// </summary>
        public static void RequestFrom(Interactor interactor, Action<NetworkObjectReference> send)
        {
            if (interactor == null || send == null) return;

            NetworkObject body = interactor.GetComponentInParent<NetworkObject>();
            if (body == null)
            {
                Debug.LogError($"[Interact] '{interactor.name}' is not part of a NetworkObject, so " +
                               "the server cannot be told who is interacting.", interactor);
                return;
            }

            send(body);
        }

        /// <summary>
        /// Server side: turn the reference back into the Interactor that sent it.
        ///
        /// Searches children because that is where it was found on the way out, and includes
        /// inactive ones so a player whose rig is partly switched off — mounted, mid-cutscene — is
        /// still recognised.
        /// </summary>
        public static bool TryResolve(NetworkObjectReference reference, out Interactor interactor)
        {
            interactor = null;

            if (!reference.TryGet(out NetworkObject body) || body == null) return false;

            interactor = body.GetComponentInChildren<Interactor>(true);
            return interactor != null;
        }
    }
}
