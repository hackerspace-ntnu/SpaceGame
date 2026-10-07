# Patched packages

Packages embedded under `Packages/` are forks of a registry package, kept only because a bug in the
upstream package cannot be worked around from project code. An embedded package takes precedence
over the version named in `manifest.json`, so the entry there stays as a record of which upstream
release the fork was taken from.

Every patch is marked in the source with a `SPACEGAME PATCH` comment, so `grep -rn 'SPACEGAME PATCH'
Packages/` lists the full diff surface against upstream.

## com.unity.netcode.gameobjects 2.9.1

Forked from `Library/PackageCache/com.unity.netcode.gameobjects@e40dcfef2dee`, minus
`Documentation~/` (106 MB of generated API docs Unity never imports).

| Patch | File |
| --- | --- |
| Skip destroyed / despawned objects in `MigrateNetworkObjectsIntoScenes` | [Runtime/SceneManagement/NetworkSceneManager.cs](com.unity.netcode.gameobjects/Runtime/SceneManagement/NetworkSceneManager.cs) |
| Skip an object that cannot be serialized while synchronizing a joining client | [Runtime/SceneManagement/SceneEventData.cs](com.unity.netcode.gameobjects/Runtime/SceneManagement/SceneEventData.cs) |

`MigrateNetworkObjectsIntoScenes` dereferenced `networkObject.gameObject` without a null check. The
reference is captured on the client when the `ObjectSceneChanged` message is read; the object can be
destroyed later in the same frame (a chunk unload, or a despawn) before the migration runs in
`PostLateUpdate`. The throw was caught, but the `try` wraps the whole loop, so the rest of that
frame's migrations were dropped and then cleared — leaving those objects in their old scene on that
client, to be destroyed when it unloads while the server keeps them. Symptom in the console:

```
[Netcode] The object of type 'Unity.Netcode.NetworkObject' has been destroyed but you are still trying to access it.
  at Unity.Netcode.NetworkSceneManager.MigrateNetworkObjectsIntoScenes ()
```

`WriteSceneSynchronizationData` serializes every entry of `SpawnedObjectsList` in one loop with no
error handling, so the first entry that throws aborts the whole synchronization message and the
joining client is never told about the session at all. The stack names nothing — the object being
written does not appear in it:

```
NullReferenceException
  at Unity.Netcode.NetworkObject.Serialize (System.UInt64 targetClientId, System.Boolean syncObservers)
  at Unity.Netcode.SceneEventData.WriteSceneSynchronizationData (Unity.Netcode.FastBufferWriter writer)
```

Two states reach it: an entry left in the spawn table after its GameObject was destroyed (Netcode's
own `NetworkObject.OnDestroy` and `NetworkSpawnManager.OnDespawnObject` both give up quietly when
there is no NetworkManager to ask, or when the object already reads as null), and an object spawned
with a null `NetworkManagerOwner`. The patch skips that object instead, rewinds the writer past the
partial record, corrects the object count written before the loop, and logs the `NetworkObjectId`,
the prefab hash and which of the two states it was in. The joiner loses that one object rather than
the world.

Upstream's own cleanup pass runs *after* the migration and only prunes entries keyed by
`LocalClientId`, so it never covers migrations announced by another peer.

### Upgrading netcode

The fork is a plain copy, so an upgrade is: re-copy the new version over
`Packages/com.unity.netcode.gameobjects` (excluding `Documentation~/`), re-apply each
`SPACEGAME PATCH` block, and update the version above. Check first whether the patch is still needed
— if upstream has fixed it, delete the fork entirely and let `manifest.json` resolve from the
registry again.
