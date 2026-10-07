// Assets/Game/Editor/Tests/InstantiatingWorld.cs
// An IWorldService for tests that need what was spawned to be real: Spawn instantiates the prefab
// (a scene object built by the test is a fine "prefab") and Despawn only records.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Tests
{
    public sealed class InstantiatingWorld : IWorldService
    {
        public readonly List<GameObject> Spawned = new();
        public readonly List<GameObject> Despawned = new();
        private readonly List<Object> junk;

        public InstantiatingWorld(List<Object> junk) => this.junk = junk;

        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation,
                                ulong ownerClientId = NetworkSpawn.NoOwner)
        {
            GameObject go = Object.Instantiate(prefab, position, rotation);
            junk.Add(go);
            Spawned.Add(go);
            return go;
        }

        public void Despawn(GameObject gameObject) => Despawned.Add(gameObject);
    }
}
