// Carries a wing-pack launch request from a client to the server.
//
// The held wing pack is a plain MonoBehaviour on an item prefab with no NetworkObject of its own,
// so it has no RPC channel. This component lives on the PLAYER — which is a NetworkObject — and
// relays on the item's behalf. Same shape as EquipmentNetworkSync.
//
// The ornithopter prefab is passed by index into a serialized whitelist rather than by name or
// path: an RPC argument is attacker-controlled, and "spawn me the prefab at this string" is a
// remote-instantiate primitive. The index is validated against the list on the server.
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Items
{
    [RequireComponent(typeof(NetworkObject))]
    public class WingPackNetworkSync : NetworkBehaviour
    {
        [Tooltip("Every craft a wing pack is allowed to launch. The RPC sends an index into this " +
                 "list, so a client can never ask the server to spawn something arbitrary.")]
        [SerializeField] private GameObject[] launchableCraft;

        /// <summary>
        /// Ask the server to spawn and mount the craft. Called on the owning client by WingPackItem.
        /// </summary>
        public void RequestLaunch(GameObject prefab, Quaternion facing, float carriedSpeed)
        {
            int index = IndexOf(prefab);
            if (index < 0)
            {
                Debug.LogError($"[WingPackNetworkSync] '{(prefab ? prefab.name : "null")}' is not in " +
                               "launchableCraft, so the server will refuse it. Add it to the list on " +
                               "the player prefab.", this);
                return;
            }

            RequestLaunchServerRpc(index, facing, carriedSpeed);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestLaunchServerRpc(int craftIndex, Quaternion facing, float carriedSpeed,
                                            RpcParams rpcParams = default)
        {
            // Only the player who owns this body may launch from it.
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
                return;

            if (craftIndex < 0 || launchableCraft == null || craftIndex >= launchableCraft.Length)
                return;

            // The pack that actually launches is the one the player is holding. Going through the
            // equipped item keeps every teardown path (landing, dismount, unequip) working on the
            // server exactly as it does offline.
            WingPackItem pack = GetComponentInChildren<WingPackItem>(true);
            if (pack == null)
                return;

            pack.SpawnAndMountCraft(gameObject, facing, carriedSpeed);
        }

        private int IndexOf(GameObject prefab)
        {
            if (prefab == null || launchableCraft == null)
                return -1;

            for (int i = 0; i < launchableCraft.Length; i++)
            {
                if (launchableCraft[i] == prefab)
                    return i;
            }

            return -1;
        }
    }
}
