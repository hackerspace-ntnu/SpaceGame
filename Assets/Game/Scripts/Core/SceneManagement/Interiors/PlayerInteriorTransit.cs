// The wire for one player's interior transitions.
//
// InteriorManager used to declare its own [Rpc(SendTo.Server)] methods, but it is a plain
// MonoBehaviour — Netcode's code generator only rewrites RPCs on a NetworkBehaviour, so those
// methods were ordinary calls that ran on whoever invoked them. A client walking into a building
// therefore ran the SERVER half on itself: it moved its own body into a scene the server had not
// loaded, tried to drive NetworkManager.SceneManager.LoadScene (which a client may not do), and
// recorded a return position in a dictionary the server would never see. The player ended up
// somewhere no other machine agreed with, and could not get back out.
//
// This is the same "sync component beside the component" split the project already uses for
// PlayerSaveSync: the logic stays where it belongs, and the RPC lives on a
// NetworkBehaviour that actually has a channel. It sits on the PLAYER rather than on the manager
// because a transition has exactly one subject, and the player is the NetworkObject in the story.
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Core
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerInteriorTransit : NetworkBehaviour
    {
        /// <summary>
        /// Send this player into <paramref name="def"/>. Runs the transition directly when this
        /// machine is already the server (host, or offline), so there is one path rather than two.
        /// </summary>
        public void RequestEnter(InteriorScene def)
        {
            if (def == null || string.IsNullOrEmpty(def.SceneName)) return;

            if (!Network.IsNetworked || Network.Server)
            {
                ServerEnter(def.SceneName, def.SpawnAnchorId);
                return;
            }

            // FixedString64Bytes throws rather than truncating, so anything that would not fit is
            // refused here with a name to look at instead of an exception inside Unity.Collections.
            if (!Fits(def.SceneName, nameof(def.SceneName)) ||
                !Fits(def.SpawnAnchorId, nameof(def.SpawnAnchorId)))
                return;

            EnterInteriorServerRpc(def.SceneName, def.SpawnAnchorId ?? string.Empty);
        }

        /// <summary>Bring this player back out to where they came in.</summary>
        public void RequestExit()
        {
            if (!Network.IsNetworked || Network.Server)
            {
                ServerExit();
                return;
            }

            ExitInteriorServerRpc();
        }

        // Owner-only, unlike most of this project's server RPCs. Entering a building moves a body
        // and loads a scene for the whole session; there is no reason for one client to be able to
        // do that to another, and "the player who walked through the door" is always the owner.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void EnterInteriorServerRpc(FixedString64Bytes sceneName, FixedString64Bytes anchorId) =>
            ServerEnter(sceneName.ToString(), anchorId.ToString());

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ExitInteriorServerRpc() => ServerExit();

        // ─────────── Server → the one machine that has to redraw ───────────

        /// <summary>
        /// Server side: this player is now inside <paramref name="interiorSceneName"/>. Tell the
        /// machine that is looking through their eyes, and nobody else.
        /// </summary>
        public void NotifyEntered(string interiorSceneName) =>
            NotifyViewer(interiorSceneName, entered: true);

        /// <summary>Server side: this player is back outside, in <paramref name="exteriorSceneName"/>.</summary>
        public void NotifyExited(string exteriorSceneName) =>
            NotifyViewer(exteriorSceneName, entered: false);

        private void NotifyViewer(string sceneName, bool entered)
        {
            // Offline, or the host's own player: no wire to cross, and going through one would mean
            // a different code path for the commonest case in the game.
            if (!IsSpawned || !Network.IsNetworked || IsOwner)
            {
                ApplyView(sceneName, entered);
                return;
            }

            if (!Fits(sceneName, nameof(sceneName))) return;

            ViewChangedRpc(sceneName, entered, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void ViewChangedRpc(FixedString64Bytes sceneName, bool entered, RpcParams rpcParams) =>
            ApplyView(sceneName.ToString(), entered);

        private void ApplyView(string sceneName, bool entered)
        {
            InteriorManager manager = InteriorManager.Instance;
            if (manager == null) return;

            if (entered) manager.ShowInteriorView(sceneName);
            else manager.ShowExteriorView(sceneName);
        }

        private void ServerEnter(string sceneName, string anchorId)
        {
            InteriorManager manager = InteriorManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("[Interior] No InteriorManager in the scene — the request is dropped.", this);
                return;
            }

            manager.ServerEnterInterior(gameObject, sceneName, anchorId);
        }

        private void ServerExit()
        {
            InteriorManager manager = InteriorManager.Instance;
            if (manager == null) return;

            manager.ServerExitInterior(gameObject);
        }

        private bool Fits(string value, string field)
        {
            // 64 bytes minus the length prefix FixedString64Bytes stores alongside the payload.
            const int Capacity = 61;

            if (string.IsNullOrEmpty(value)) return true;
            if (System.Text.Encoding.UTF8.GetByteCount(value) <= Capacity) return true;

            Debug.LogError($"[Interior] {field} '{value}' is too long to send over the network " +
                           $"(max {Capacity} bytes). Rename the scene or the anchor.", this);
            return false;
        }
    }
}
