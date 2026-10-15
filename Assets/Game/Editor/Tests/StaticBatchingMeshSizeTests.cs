// Unity's static batcher packs every batching-static renderer into a combined mesh with 16-bit
// indices, even when the source mesh has more vertices than 16 bits can address. The indices wrap,
// and in play mode the model turns into a tangle of stray triangles in the wrong materials, while
// the Scene view, the prefab and the FBX all look perfect. Measured on NomadBuilding_B10 (126k
// vertices): 8,138 m² of surface became 42,254 m² of garbage once batched.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class StaticBatchingMeshSizeTests
    {
        [Test]
        public void NoBatchingStaticRendererHasAMeshTooLargeForSixteenBitIndices()
        {
            var failures = new List<string>();

            foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs" })
                                                 .Select(AssetDatabase.GUIDToAssetPath))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null || mesh.vertexCount <= ushort.MaxValue) continue;
                    if (filter.GetComponent<MeshRenderer>() == null) continue;
                    if (!GameObjectUtility.AreStaticEditorFlagsSet(filter.gameObject, StaticEditorFlags.BatchingStatic)) continue;

                    failures.Add($"{path}: '{filter.name}' ({mesh.vertexCount} vertices) is Batching Static; " +
                                 "clear that flag or split the mesh below 65,536 vertices");
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}
