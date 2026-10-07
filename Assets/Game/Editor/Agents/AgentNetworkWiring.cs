// Getting the four netcode components onto every agent that needs them, and keeping them there.
//
// The bar is the one the spacegame-multiplayer skill sets: every gameplay system works fully
// online, and a system that is still local is unfinished rather than scoped that way. Six shipped
// agents were not, each in a way that is invisible on the host:
//
//   Appa, Sandloper   no NetworkTransform at all. NetAuthority switches the remote copy's brain and
//                     motor off, and nothing replaced them, so on every client these two stood
//                     frozen at the spot they spawned while the host watched them walk around. They
//                     are the project's two rideable creatures, so this is also the mount you climb
//                     onto and then do not move. Both also lacked MountNetworkSync, which is the
//                     half of mounting that hands ownership to the rider.
//   Ostrich,
//   NomadOstrich,
//   DesertCrawler     no NetworkedHealthComponent beside HealthComponent, so their health never
//                     replicated and they showed no damage numbers.
//
// Written as a pass rather than fixed by hand for the reason the other passes exist: these prefabs
// have no builder, the next person cannot tell which of them were deliberate, and a builder that
// rewrites its prefab wholesale would drop the components again with nothing said.
//
// NOT covered here, on purpose: CrabWalker6 and HumanoidRobot have no NetworkObject at all. Adding
// one changes a prefab's identity for netcode and means re-saving every scene that holds an
// instance (skill, verification step 3), so they are a separate, deliberate piece of work rather
// than something a wiring pass should do behind your back.
//
// Re-run from: Tools > SpaceGame > Multiplayer > Wire Agent Netcode
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public static class AgentNetworkWiring
    {
        /// <summary>
        /// Every agent prefab this pass owns. A declared list rather than "every prefab with an
        /// AgentController", because the answer for a prefab with no NetworkObject is not "add the
        /// missing components" — it is a conversation about scenes.
        /// </summary>
        private static readonly string[] Agents =
        {
            "Assets/Game/Prefabs/Agents/creatures/Appa.prefab",
            "Assets/Game/Prefabs/Agents/creatures/Sandloper.prefab",
            "Assets/Game/Prefabs/Agents/creatures/Ostrich.prefab",
            "Assets/Game/Prefabs/Agents/Caravan/NomadOstrich.prefab",
            "Assets/Game/Prefabs/Agents/Vehicles/Ground/DesertCrawler.prefab",
        };

        /// <summary>
        /// Bring one agent root up to the netcode bar. Answers whether anything changed, so a
        /// caller can skip a pointless prefab save.
        ///
        /// <para>
        /// Adds only what is missing and never reconfigures what is there: a prefab whose
        /// NetworkTransform has been tuned by hand keeps its settings. Requires a NetworkObject
        /// already — see the header for why this refuses to add one.
        /// </para>
        /// </summary>
        public static bool Ensure(GameObject root)
        {
            if (root == null || root.GetComponent<AgentController>() == null)
                return false;

            if (root.GetComponent<NetworkObject>() == null)
            {
                Debug.LogError($"[AgentNetworkWiring] {root.name} has no NetworkObject. Adding one " +
                               "changes its netcode identity and needs every scene holding an " +
                               "instance re-saved, so it is not done here.", root);
                return false;
            }

            bool changed = false;

            // ClientNetworkTransform rather than the stock one, matching every other creature: a
            // mount is driven by its RIDER once somebody is on it, and MountNetworkSync hands
            // ownership over to do exactly that. A server-authoritative transform would fight the
            // rider's own input and snap them back every tick.
            if (root.GetComponent<NetworkTransform>() == null)
            {
                root.AddComponent<ClientNetworkTransform>();
                changed = true;
            }

            // The wire discrete events travel on. Without it every NetTo degrades to a local
            // dispatch and logs "[Net] '<name>' handled message N locally" — which is the correct
            // behaviour for something deliberately unnetworked and a silent no-op for a creature
            // that is supposed to be telling other machines that it just swung at somebody.
            if (root.GetComponent<NetRelay>() == null)
            {
                root.AddComponent<NetRelay>();
                changed = true;
            }

            // Stops the remote copies running their own brain against the replicated transform.
            if (root.GetComponent<NetAuthority>() == null)
            {
                root.AddComponent<NetAuthority>();
                changed = true;
            }

            // Health that other machines can see. HealthComponent alone is local, so a creature
            // could be at 1 HP on the server and untouched everywhere else.
            if (root.GetComponent<HealthComponent>() != null &&
                root.GetComponent<NetworkedHealthComponent>() == null)
            {
                root.AddComponent<NetworkedHealthComponent>();
                changed = true;
            }

            // Only for something that can actually be ridden. MountNetworkSync is the server-decided
            // seating and the ownership transfer; on a creature with no MountModule it would be a
            // component waiting for a seat that never arrives.
            if (root.GetComponent<MountModule>() != null &&
                root.GetComponent<MountNetworkSync>() == null)
            {
                root.AddComponent<MountNetworkSync>();
                changed = true;
            }

            return changed;
        }

        [MenuItem("Tools/SpaceGame/Multiplayer/Wire Agent Netcode")]
        public static void WireAll()
        {
            int changed = 0;

            foreach (string path in Agents)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    Debug.LogError($"[AgentNetworkWiring] No prefab at {path}; it moved or was " +
                                   "deleted. Fix the list in this file.");
                    continue;
                }

                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (!Ensure(contents)) continue;

                    PrefabUtility.SaveAsPrefabAsset(contents, path, out bool saved);

                    // A read-only AssetDatabase discards a prefab save and says nothing at all.
                    if (!saved)
                    {
                        Debug.LogError($"[AgentNetworkWiring] Could not save {path}. " +
                                       "The AssetDatabase refused the write.");
                        continue;
                    }

                    changed++;
                    Debug.Log($"[AgentNetworkWiring] Brought {System.IO.Path.GetFileName(path)} up to " +
                              "the netcode bar.", contents);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[AgentNetworkWiring] {changed} of {Agents.Length} agent prefabs updated.");

            // A NetworkObject that is not in the list the NetworkManager reads is a host that works
            // and clients that see nothing, so the registration is part of the fix rather than a
            // step somebody has to remember afterwards.
            if (changed > 0)
            {
                NetworkPrefabRegistrar.Sync(out int added, out int total);
                Debug.Log($"[AgentNetworkWiring] Registered {added} network prefab(s); {total} in the project.");
            }
        }

        /// <summary>
        /// Every agent prefab that carries a <see cref="NetworkObject"/> but is missing one of the
        /// pieces above. Used by the test, and worth running by hand after adding a creature.
        /// </summary>
        public static string[] Offenders()
        {
            return AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs/agents" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p =>
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    if (go == null) return false;
                    if (go.GetComponent<AgentController>() == null) return false;
                    if (go.GetComponent<NetworkObject>() == null) return false;

                    return go.GetComponent<NetworkTransform>() == null
                        || go.GetComponent<NetRelay>() == null
                        || go.GetComponent<NetAuthority>() == null
                        || (go.GetComponent<HealthComponent>() != null &&
                            go.GetComponent<NetworkedHealthComponent>() == null)
                        || (go.GetComponent<MountModule>() != null &&
                            go.GetComponent<MountNetworkSync>() == null);
                })
                .OrderBy(p => p)
                .ToArray();
        }
    }
}
