// Keeps one dead entry in Netcode's spawn table from breaking every later join.
//
// A client that joins a session in progress is synchronised by the server walking
// NetworkSpawnManager.SpawnedObjectsList and calling NetworkObject.Serialize on every entry. That
// walk has no error handling of its own: the first entry that throws aborts the whole scene-event
// message, so the joining client never receives the world it was promised. The symptom on the host
// is a single NullReferenceException out of NetworkObject.Serialize, under
// SceneEventData.WriteSceneSynchronizationData, with nothing in it that names the object.
//
// The entry that throws is a NetworkObject whose GameObject has been destroyed while it was still
// spawned. Netcode normally cleans that up from NetworkObject.OnDestroy, but two of its own paths
// give up quietly instead: OnDestroy returns early when there is no NetworkManager to ask, and
// NetworkSpawnManager.OnDespawnObject returns early when the object it is handed already reads as
// null. Either leaves the id in SpawnedObjects and the reference in SpawnedObjectsList. Serializing
// it then touches the destroyed transform — which is a MissingReferenceException in the editor and
// a NullReferenceException in a player, i.e. exactly what the host reports and what nothing sees
// while testing in the editor.
//
// So this measures the outcome rather than enumerating the causes, the same bargain UnderTerrainGuard
// makes: whatever destroyed the object, the joinable state of the session is one sweep away, and a
// cause nobody has hit yet is still caught. It runs on the server only — the table it repairs is
// only read there — and it logs every entry it removes as an error, because a dead entry is a real
// bug somewhere else and this must not be the thing that hides it.
//
// It cannot repair the other way Serialize can throw: an object spawned with a null
// NetworkManagerOwner. That field is internal to Netcode, and Netcode already logs
// "NetworkManagerOwner should not be null!" from SpawnNetworkObjectLocallyCommon when it happens —
// so if a join still fails with this guard reporting nothing, that line in the host's log is where
// to look next.
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Core
{
    public class SpawnSyncGuard : MonoBehaviour
    {
        /// <summary>
        /// Seconds between sweeps. A dead entry stays dead until something removes it, so this only
        /// decides how stale the table may be when a client happens to connect — not whether the
        /// entry is found. Netcode gives no hook that runs before synchronisation (connection
        /// approval is off, and OnClientConnectedCallback fires after the message has already been
        /// written), which is why this polls instead of reacting to a join.
        /// </summary>
        public const float SweepIntervalSeconds = 1f;

        private static SpawnSyncGuard instance;

        // Reused so a sweep that finds nothing — every sweep, in a healthy session — allocates
        // nothing. Removal cannot happen while the tables are being enumerated.
        private readonly List<NetworkObject> deadEntries = new();
        private readonly List<ulong> deadIds = new();

        private float nextSweepTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var go = new GameObject(nameof(SpawnSyncGuard));
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SpawnSyncGuard>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || !manager.IsServer) return;

            // Unscaled: a paused game still has a session, and a client can still be joining it.
            if (Time.unscaledTime < nextSweepTime) return;
            nextSweepTime = Time.unscaledTime + SweepIntervalSeconds;

            Sweep(manager);
        }

        /// <summary>
        /// Removes every destroyed <see cref="NetworkObject"/> from the spawn tables and returns how
        /// many there were. Server-side; safe to call at any time.
        /// </summary>
        public int Sweep(NetworkManager manager)
        {
            NetworkSpawnManager spawnManager = manager != null ? manager.SpawnManager : null;
            if (spawnManager == null) return 0;

            int removed = 0;

            deadEntries.Clear();
            deadIds.Clear();

            // Both tables, because the paths that leave an entry behind can leave it in either one:
            // the list is what synchronisation walks, the dictionary is what every id lookup reads.
            foreach (NetworkObject spawned in spawnManager.SpawnedObjectsList)
                if (spawned == null) deadEntries.Add(spawned);

            foreach (KeyValuePair<ulong, NetworkObject> entry in spawnManager.SpawnedObjects)
                if (entry.Value == null) deadIds.Add(entry.Key);

            for (int i = 0; i < deadEntries.Count; i++)
            {
                NetworkObject dead = deadEntries[i];
                spawnManager.SpawnedObjectsList.Remove(dead);

                // A destroyed object still answers for its id — that plain managed property is the
                // only thing left that says which object this was. A genuine null reference in the
                // set answers for nothing, and is reported as itself.
                if (ReferenceEquals(dead, null))
                {
                    removed++;
                    Debug.LogError("[SpawnSyncGuard] A null entry was left in the spawn table. " +
                                   "Removed, so joining clients can be synchronised.");
                    continue;
                }

                ulong id = dead.NetworkObjectId;
                if (!deadIds.Contains(id)) deadIds.Add(id);
            }

            for (int i = 0; i < deadIds.Count; i++)
            {
                spawnManager.SpawnedObjects.Remove(deadIds[i]);
                removed++;
                Debug.LogError($"[SpawnSyncGuard] NetworkObject #{deadIds[i]} was destroyed while " +
                               "still spawned and left in the spawn table. Removed, so joining " +
                               "clients can be synchronised. Despawn it before destroying it.");
            }

            return removed;
        }
    }
}
