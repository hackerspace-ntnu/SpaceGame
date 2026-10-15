// A garment names its bones; a skeleton that lacks one the mesh never uses (the Classic Raxy has no Jaw, a belt gives it no weight)
// can still wear it, and one that lacks a bone the mesh does use cannot.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpaceGame.EditorTools
{
    public class SkinnedGarmentBindTests
    {
        private readonly List<Object> made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made)
                if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        // A one-triangle mesh whose three vertices are weighted to the bones named by index (one bone each).
        private Mesh MeshWeightedTo(int boneCount, params int[] boneOfVertex)
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            var weights = new BoneWeight[3];
            for (int i = 0; i < 3; i++) weights[i] = new BoneWeight { boneIndex0 = boneOfVertex[i], weight0 = 1f };
            mesh.boneWeights = weights;
            var bindPoses = new Matrix4x4[boneCount];
            for (int i = 0; i < boneCount; i++) bindPoses[i] = Matrix4x4.identity;
            mesh.bindposes = bindPoses;
            made.Add(mesh);
            return mesh;
        }

        private SkinnedGarment Wear(Mesh mesh, string[] boneNames, out Transform wearer)
        {
            var root = new GameObject("wearer");
            made.Add(root);
            wearer = root.transform;
            new GameObject("Hips").transform.SetParent(wearer, false);

            var garment = new GameObject("garment");
            garment.transform.SetParent(wearer, false);
            var skin = garment.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            var worn = garment.AddComponent<SkinnedGarment>();
            var so = new SerializedObject(worn);
            SerializedProperty names = so.FindProperty("boneNames");
            names.arraySize = boneNames.Length;
            for (int i = 0; i < boneNames.Length; i++) names.GetArrayElementAtIndex(i).stringValue = boneNames[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return worn;
        }

        [Test]
        public void WeightedBonesAreThoseTheMeshGivesAnyWeight()
        {
            bool[] weighted = SkinnedGarment.WeightedBones(MeshWeightedTo(4, 0, 0, 2), 4);

            CollectionAssert.AreEqual(new[] { true, false, true, false }, weighted);
        }

        [Test]
        public void ASkeletonWithoutABoneTheMeshNeverUsesStillWearsTheGarment()
        {
            SkinnedGarment belt = Wear(MeshWeightedTo(2, 0, 0, 0), new[] { "Hips", "Jaw" }, out Transform wearer);

            Assert.IsTrue(belt.Bind(), "the jaw is not weighted, so nothing needs it");

            Transform[] bones = belt.GetComponent<SkinnedMeshRenderer>().bones;
            Assert.AreEqual("Hips", bones[0].name);
            Assert.AreSame(wearer, bones[1], "the unused slot rides the wearer's root, which the garment never moves");
            Assert.IsTrue(belt.Bind(), "binding again accepts what it bound");
        }

        [Test]
        public void ASkeletonWithoutABoneTheMeshUsesCannotWearTheGarment()
        {
            SkinnedGarment mask = Wear(MeshWeightedTo(2, 0, 1, 1), new[] { "Hips", "Jaw" }, out _);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("has no bone 'Jaw'"));
            Assert.IsFalse(mask.Bind(), "a mask skinned to the jaw would be dragged through the face");
        }
    }
}
