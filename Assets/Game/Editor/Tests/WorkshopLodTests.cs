// The workshop's LODs are written by WorkshopLodBuilder at the end of Rebuild Workshop Prefab. A drawn piece left out
// of every LODGroup draws its full mesh from anywhere on the map, and a LOD copy left out draws on top of its
// original at every range (LevelOfDetail.md).
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class WorkshopLodTests
    {
        private static GameObject Workshop()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorkshopPrefabBuilder.PrefabPath);
            Assert.IsNotNull(prefab, "no prefab at " + WorkshopPrefabBuilder.PrefabPath);
            return prefab;
        }

        [Test]
        public void EveryDrawnPieceIsInExactlyOneLodGroup()
        {
            GameObject workshop = Workshop();
            // A renderer may sit in several tiers of one group (a decoration keeps a part across tiers); it is
            // counted once per group.
            var groupsOf = new Dictionary<Renderer, int>();
            foreach (LODGroup group in workshop.GetComponentsInChildren<LODGroup>(true))
            {
                var listed = new HashSet<Renderer>();
                foreach (LOD lod in group.GetLODs())
                foreach (Renderer renderer in lod.renderers)
                    if (renderer != null) listed.Add(renderer);
                foreach (Renderer renderer in listed)
                    groupsOf[renderer] = groupsOf.TryGetValue(renderer, out int n) ? n + 1 : 1;
            }

            var failures = new List<string>();
            foreach (MeshRenderer renderer in workshop.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled) continue;
                int groups = groupsOf.TryGetValue(renderer, out int n) ? n : 0;
                if (groups != 1) failures.Add($"'{renderer.name}' is in {groups} LOD groups");
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void NoPieceWithMeshLodLevelsIsBatchingStatic()
        {
            var failures = new List<string>();
            foreach (MeshRenderer renderer in Workshop().GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled || !renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                if (filter.sharedMesh.lodCount < 2) continue;
                if (GameObjectUtility.AreStaticEditorFlagsSet(renderer.gameObject, StaticEditorFlags.BatchingStatic))
                    failures.Add($"'{renderer.name}' has Mesh LOD levels and is Batching Static: the batch draws level 0 at every range");
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}
