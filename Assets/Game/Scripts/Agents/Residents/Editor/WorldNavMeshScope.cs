// Edit mode has no world NavMesh — WorldNavMeshProvider only adds it at runtime — so every NavMesh query an
// editor tool makes about a settlement (door stands, walking times, trip points) finds nothing. This adds
// the baked WorldNavMeshAsset for the scope's lifetime when none covers the point, and removes it after.
using System;
using System.Linq;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Agents.Residents.EditorTools
{
    public readonly struct WorldNavMeshScope : IDisposable
    {
        private const float LoadedMeshProbe = 40f;   // any NavMesh this close to the point means one is loaded

        private readonly NavMeshDataInstance added;

        public WorldNavMeshScope(Vector3 near)
        {
            added = default;
            if (NavMesh.SamplePosition(near, out _, LoadedMeshProbe, NavMesh.AllAreas)) return;

            WorldNavMeshAsset asset = AssetDatabase.FindAssets("t:" + nameof(WorldNavMeshAsset))
                .Select(g => AssetDatabase.LoadAssetAtPath<WorldNavMeshAsset>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(a => a != null && a.bakedData != null);
            if (asset == null)
            {
                Debug.LogError("[Residents] No baked WorldNavMeshAsset — use Generate + Bake World NavMesh on the settlement.");
                return;
            }
            added = NavMesh.AddNavMeshData(asset.bakedData);
        }

        public void Dispose()
        {
            if (added.valid) added.Remove();
        }
    }
}
