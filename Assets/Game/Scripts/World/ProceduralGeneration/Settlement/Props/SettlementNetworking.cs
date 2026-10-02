// A generated building with shared state — a gate somebody opens, props people carry off — needs a NetworkObject
// for that state to reach every machine. It is put on a WRAPPER that Generate stands round the building, never on
// the building prefab: a loose scene object gets its hash from the scene, where a NetworkObject inside a prefab
// gives every scene instance the prefab's hash until each scene is re-saved by hand
// (docs/AI/systems/Multiplayer.md). The wrapper sits beside the characters, never above them, so no
// NetworkObject nests in another; the building's own DoorInteraction latches find it as their channel.
using System.Collections.Generic;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.World
{
    public static class SettlementNetworking
    {
        /// <summary>Wraps every building that needs it; returns how many were wrapped.</summary>
        public static int Wrap(IEnumerable<Transform> buildings)
        {
            int wrapped = 0;
            foreach (Transform building in buildings)
            {
                if (building == null || building.GetComponentInParent<NetworkObject>(true) != null) continue;

                bool hasProps = building.GetComponentInChildren<SettlementProp>(true) != null;
                bool hasLatches = building.GetComponentInChildren<ILatchHost>(true) != null;
                if (!hasProps && !hasLatches) continue;

                var wrapper = new GameObject(building.name).transform;
                wrapper.SetParent(building.parent, false);
                wrapper.SetPositionAndRotation(building.position, building.rotation);
                wrapper.SetSiblingIndex(building.GetSiblingIndex());
                building.SetParent(wrapper, true);

                var netObj = wrapper.gameObject.AddComponent<NetworkObject>();
                netObj.SynchronizeTransform = false;
                netObj.AutoObjectParentSync = false;
                wrapper.gameObject.AddComponent<NetRelay>();
                if (hasProps) wrapper.gameObject.AddComponent<SettlementPropSync>();
                wrapped++;
            }
            return wrapped;
        }
    }
}
