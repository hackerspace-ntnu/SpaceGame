// How an NPC comes into the world: visible on every machine, and saved by nobody.
//
// Deliberately not GameServices.World.Spawn. That opts its instance into the save system, and an
// NPC that saves itself is an NPC that comes back twice — everything that spawns NPCs here (an
// NpcGroup record folding back out into a caravan, a mount putting its rider in the saddle) is
// itself persisted and rebuilds its own members on load, so a second independent record would
// restore a duplicate standing beside the real one.
using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Core;

namespace SpaceGame.Agents
{
    public static class NpcSpawn
    {
        /// <summary>
        /// Instantiate <paramref name="prefab"/> and, in a session, spawn it for every peer.
        ///
        /// <para>
        /// Authority only. Callers reach this behind their own <see cref="Network.Simulates"/>
        /// check, which also answers true offline; a client that called it anyway would get an
        /// instance nobody else can see.
        /// </para>
        /// </summary>
        /// <param name="context">Logged as the object to select when a spawn fails.</param>
        /// <param name="beforeSpawn">
        /// Runs on the new instance after Instantiate and BEFORE the network spawn — the only moment
        /// where anything read in <c>OnNetworkSpawn</c> (a seeded loadout roll) can still be set.
        /// </param>
        /// <param name="seated">
        /// The NPC goes straight into a seat, usually in mid-air: it wakes with its NavMeshAgent off.
        /// See <see cref="InstantiateSeated"/>.
        /// </param>
        public static GameObject Create(GameObject prefab, Vector3 position, Quaternion rotation,
                                        UnityEngine.Object context = null,
                                        Action<GameObject> beforeSpawn = null,
                                        bool seated = false)
        {
            if (prefab == null) return null;

            GameObject instance = seated
                ? InstantiateSeated(prefab, position, rotation)
                : UnityEngine.Object.Instantiate(prefab, position, rotation);
            DisownFromWorldSave(instance);
            beforeSpawn?.Invoke(instance);

            if (!Network.IsNetworked || !Network.Server) return instance;
            if (!instance.TryGetComponent(out NetworkObject netObj) || netObj.IsSpawned) return instance;

            try
            {
                // Destroyed with the scene: whoever spawned this NPC rebuilds it from its own record
                // on load. Spawn()'s default (false) carried every caravan member, war-party rider and
                // transport hull through an in-session reload (quickload) into the DontDestroyOnLoad
                // scene and back, where it stood beside the copy its record rebuilt.
                netObj.Spawn(destroyWithScene: true);
            }
            catch (Exception e)
            {
                // An unregistered prefab fails ONLY on clients — the server instantiates its own
                // copy and never consults the list — so this is deliberately not silent.
                Debug.LogError($"[NpcSpawn] '{prefab.name}' has a NetworkObject but could not be " +
                               $"spawned — is it in the NetworkManager's prefab list? {e.Message}",
                               context != null ? context : instance);
            }

            return instance;
        }

        /// <summary>
        /// Instantiate an NPC whose NavMeshAgent is already off when it wakes.
        ///
        /// <para>
        /// A NavMeshAgent that wakes enabled away from the NavMesh logs "Failed to create agent
        /// because it is not close enough to the NavMesh" from inside <c>Instantiate</c> itself —
        /// before <c>beforeSpawn</c>, or <c>NavMeshAgentMotor.Awake</c>, can switch it off (the motor
        /// does, a moment too late). A war party's rider is made under its hull at cruise height,
        /// so every Sky party logged it once per rider. Made under an inactive parent nothing wakes;
        /// the agents are switched off there, and only then is the NPC let go to the scene root.
        /// </para>
        /// <para>
        /// Nothing has to hand the agent back: NpcSeating does not record an agent it found off, and
        /// the re-enabled <c>NavMeshAgentMotor</c> puts it back on the NavMesh once the NPC is set down.
        /// </para>
        /// </summary>
        private static GameObject InstantiateSeated(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            var cradle = new GameObject("NpcSpawn cradle");
            cradle.SetActive(false);

            GameObject instance = UnityEngine.Object.Instantiate(prefab, position, rotation, cradle.transform);
            foreach (NavMeshAgent agent in instance.GetComponentsInChildren<NavMeshAgent>(true))
                agent.enabled = false;

            instance.transform.SetParent(null, worldPositionStays: true);
            UnityEngine.Object.DestroyImmediate(cradle);
            return instance;
        }

        /// <summary>
        /// Take the new NPC out of the world save, because whoever spawned it saves it themselves.
        ///
        /// <para>
        /// The header above says these are "saved by nobody", and plain Instantiate was taken as
        /// enough to make that true. It is not: <c>SaveableEntity</c> registers itself in Awake
        /// whatever route brought the object into the world, and every NPC prefab ships one on
        /// <c>SaveScope.World</c>. So a caravan's rider quietly got a world record of its own, and a
        /// load both re-instantiated it from that record AND let the mount seat a fresh one — two
        /// nomads, one saddle. <c>NpcWorldSim</c> already disowns its own members by hand for
        /// exactly this reason; doing it here means the next caller cannot forget.
        /// </para>
        /// </summary>
        private static void DisownFromWorldSave(GameObject instance)
        {
            if (instance.TryGetComponent(out SpaceGame.Core.Persistence.SaveableEntity saveable))
                saveable.DisownToExternal();
        }
    }
}
