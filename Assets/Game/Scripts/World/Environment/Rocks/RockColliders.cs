using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Gives the drawn rocks of colliding size classes real colliders, but only near the players:
    /// the rocks are GPU instances, not GameObjects, so a pool of convex MeshColliders is moved
    /// onto the rocks within a radius of every player and taken back from the rest.
    ///
    /// Every machine scatters the same rocks, so the host and every client put the same colliders
    /// around every player without a byte on the network. What is far from all players — an NPC
    /// alone in a boulder field — has no rock colliders, and the NavMesh never knows the rocks.
    /// </summary>
    public sealed class RockColliders : IDisposable
    {
        private readonly Transform parent;
        private readonly int layer;
        private readonly Dictionary<RockKey, MeshCollider> live = new Dictionary<RockKey, MeshCollider>();
        private readonly Stack<MeshCollider> free = new Stack<MeshCollider>();
        private readonly HashSet<RockKey> wanted = new HashSet<RockKey>();
        private readonly List<RockKey> released = new List<RockKey>();
        private readonly List<KeyValuePair<Vector2Int, RockInstance[]>> nearby = new List<KeyValuePair<Vector2Int, RockInstance[]>>();

        private readonly struct RockKey : IEquatable<RockKey>
        {
            private readonly int stream;
            private readonly Vector2Int chunk;
            private readonly int index;

            public RockKey(int stream, Vector2Int chunk, int index)
            {
                this.stream = stream;
                this.chunk = chunk;
                this.index = index;
            }

            public bool Equals(RockKey other) => stream == other.stream && chunk == other.chunk && index == other.index;
            public override bool Equals(object obj) => obj is RockKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(stream, chunk, index);
        }

        /// <param name="owner">The colliders live in this object's scene, under a root that is never saved.
        /// A root, not a child, so the owner's scale cannot reach them.</param>
        /// <param name="layer">The physics layer the colliders are on.</param>
        public RockColliders(GameObject owner, int layer)
        {
            this.layer = layer;
            var root = new GameObject($"Rock Colliders ({owner.name})") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(root, owner.scene);
            parent = root.transform;
        }

        /// <summary>Puts a collider on every colliding rock within <paramref name="radius"/> of an anchor, and none elsewhere.</summary>
        public void Sync(IReadOnlyList<Transform> anchors, IReadOnlyList<RockClassStream> streams, float radius)
        {
            wanted.Clear();
            for (int s = 0; s < streams.Count; s++)
            {
                RockClassStream stream = streams[s];
                if (!stream.Collides)
                    continue;
                foreach (Transform anchor in anchors)
                {
                    Vector3 point = anchor.position;
                    stream.ChunksNear(point, radius, nearby);
                    foreach (KeyValuePair<Vector2Int, RockInstance[]> chunk in nearby)
                    {
                        RockInstance[] rocks = chunk.Value;
                        for (int i = 0; i < rocks.Length; i++)
                        {
                            Vector4 bounds = rocks[i].Bounds;
                            float reach = radius + bounds.w;
                            float dx = bounds.x - point.x;
                            float dz = bounds.z - point.z;
                            if (dx * dx + dz * dz > reach * reach)
                                continue;
                            var key = new RockKey(s, chunk.Key, i);
                            if (!wanted.Add(key))
                                continue;
                            // A repainted chunk can hold a different rock under the same key.
                            if (live.TryGetValue(key, out MeshCollider placed))
                            {
                                if (placed.transform.position != rocks[i].Pivot)
                                    Place(placed, rocks[i], stream.MeshOf(rocks[i].Variant));
                                continue;
                            }
                            MeshCollider collider = free.Count > 0 ? free.Pop() : Create();
                            Place(collider, rocks[i], stream.MeshOf(rocks[i].Variant));
                            live.Add(key, collider);
                        }
                    }
                }
            }

            released.Clear();
            foreach (KeyValuePair<RockKey, MeshCollider> pair in live)
                if (!wanted.Contains(pair.Key))
                    released.Add(pair.Key);
            foreach (RockKey key in released)
            {
                MeshCollider collider = live[key];
                collider.gameObject.SetActive(false);
                free.Push(collider);
                live.Remove(key);
            }
        }

        public void Dispose()
        {
            if (parent == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(parent.gameObject);
            else
                UnityEngine.Object.DestroyImmediate(parent.gameObject);
        }

        private static void Place(MeshCollider collider, RockInstance rock, Mesh mesh)
        {
            Transform transform = collider.transform;
            transform.SetPositionAndRotation(rock.Pivot, rock.Orientation);
            transform.localScale = rock.Scale;
            collider.sharedMesh = mesh;
            collider.gameObject.SetActive(true);
        }

        private MeshCollider Create()
        {
            var go = new GameObject("Rock") { layer = layer, hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            var collider = go.AddComponent<MeshCollider>();
            // Convex: cheap to collide with, and the rocks are near enough to convex anyway.
            collider.convex = true;
            return collider;
        }
    }
}
