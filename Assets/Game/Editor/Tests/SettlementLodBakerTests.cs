// The generated far level, baked over a scratch hierarchy: one merged mesh per material in the root's
// own space, the original renderers as LOD0, no particle in any level, mirrored parts the right way
// out, and a re-bake that overwrites its own asset instead of minting a new one.
using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Vehicles;
using Object = UnityEngine.Object;

namespace SpaceGame.EditorTools
{
    public class SettlementLodBakerTests
    {
        private const string Folder = "Assets/SettlementLodBakerTests_Scratch";
        private const string PrefabPath = Folder + "/Scratch.prefab";

        private static readonly SettlementLodSettings.Profile Profile = new SettlementLodSettings.Profile
        {
            mergedBeyondMetres = 100f,
            cullBeyondMetres = 1000f,
            meshLodLimit = 2,
            referenceFovDegrees = 60f,
        };

        private GameObject root;
        private Material red, blue;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "SettlementLodBakerTests_Scratch");
            red = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "red" };
            blue = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "blue" };
            root = new GameObject("Scratch");
            Part(PrimitiveType.Cube, red, new Vector3(0f, 1f, 0f), Vector3.one);
            Part(PrimitiveType.Sphere, red, new Vector3(3f, 1f, 0f), Vector3.one);
            Part(PrimitiveType.Cylinder, blue, new Vector3(-3f, 1f, 0f), Vector3.one);
            var smoke = new GameObject("FX_Smoke");
            smoke.transform.SetParent(root.transform, false);
            smoke.AddComponent<ParticleSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(red);
            Object.DestroyImmediate(blue);
            AssetDatabase.DeleteAsset(Folder);
        }

        private MeshRenderer Part(PrimitiveType type, Material material, Vector3 at, Vector3 scale)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = at;
            part.transform.localScale = scale;
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        private MeshRenderer[] Parts() =>
            root.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name != MergedLod.ChildName).ToArray();

        [Test]
        public void TheMergedLevel_HasOneSubmeshPerMaterial_AndEveryVertexOfTheParts()
        {
            MergedLod lod = SettlementLodBaker.Bake(root, PrefabPath, Profile);

            Assert.AreEqual(2, lod.Mesh.subMeshCount, "one submesh (one draw) per distinct material");
            CollectionAssert.AreEqual(new[] { red, blue }, lod.Materials);
            Assert.AreEqual(Parts().Sum(r => r.GetComponent<MeshFilter>().sharedMesh.vertexCount), lod.Mesh.vertexCount);
        }

        [Test]
        public void LodZero_IsTheOriginalRenderers_AndNoParticleIsInAnyLevel()
        {
            MergedLod lod = SettlementLodBaker.Bake(root, PrefabPath, Profile);
            LOD[] lods = lod.Group.GetLODs();

            Assert.AreEqual(2, lods.Length);
            CollectionAssert.AreEquivalent(Parts(), lods[0].renderers, "LOD0 is the prefab's own renderers, untouched");
            CollectionAssert.AreEqual(new Renderer[] { lod.MergedRenderer }, lods[1].renderers);
            Assert.IsFalse(lods.SelectMany(l => l.renderers).Any(r => r is ParticleSystemRenderer),
                           "smoke and dust must run at every distance");
            Assert.AreSame(root.GetComponent<LODGroup>(), lod.Group);
        }

        [Test]
        public void TheTransitions_AreTheProfilesDistances_AsScreenHeights()
        {
            MergedLod lod = SettlementLodBaker.Bake(root, PrefabPath, Profile);
            LOD[] lods = lod.Group.GetLODs();
            float size = lod.Group.size;

            Assert.AreEqual(SettlementLodBaker.ScreenHeightAt(size, Profile.mergedBeyondMetres, Profile.referenceFovDegrees, QualitySettings.lodBias),
                            lods[0].screenRelativeTransitionHeight, 1e-5f);
            Assert.AreEqual(SettlementLodBaker.ScreenHeightAt(size, Profile.cullBeyondMetres, Profile.referenceFovDegrees, QualitySettings.lodBias),
                            lods[1].screenRelativeTransitionHeight, 1e-5f);
        }

        [Test]
        public void ScreenHeightAt_IsHalfTheSizeOverTheHalfFrustum_ScaledByTheLodBias()
        {
            float expected = 5f / (100f * Mathf.Tan(30f * Mathf.Deg2Rad));
            Assert.AreEqual(expected, SettlementLodBaker.ScreenHeightAt(10f, 100f, 60f, 1f), 1e-6f);
            Assert.AreEqual(2f * expected, SettlementLodBaker.ScreenHeightAt(10f, 100f, 60f, 2f), 1e-6f);
        }

        // Not rotated: the AABB of rotated parts' AABBs is looser than the AABB of the rotated merged mesh,
        // so the two would differ for a right answer. Translation and scale keep both exact.
        [Test]
        public void TheMergedLevel_SitsWhereThePartsDo_UnderAMovedScaledRoot()
        {
            root.transform.position = new Vector3(10f, 2f, -4f);
            root.transform.localScale = Vector3.one * 1.5f;
            MergedLod lod = SettlementLodBaker.Bake(root, PrefabPath, Profile);

            MeshRenderer[] parts = Parts();
            Bounds union = parts[0].bounds;
            foreach (MeshRenderer part in parts.Skip(1)) union.Encapsulate(part.bounds);
            Bounds merged = lod.MergedRenderer.bounds;
            Assert.Less(Vector3.Distance(union.center, merged.center), 1e-3f);
            Assert.Less(Vector3.Distance(union.size, merged.size), 1e-3f);
        }

        [Test]
        public void AMirroredPart_IsNotTurnedInsideOut()
        {
            foreach (MeshRenderer part in Parts()) Object.DestroyImmediate(part.gameObject);
            Part(PrimitiveType.Cube, red, Vector3.zero, Vector3.one);
            Part(PrimitiveType.Cube, red, new Vector3(4f, 0f, 0f), new Vector3(-1f, 1f, 1f));

            Mesh mesh = SettlementLodBaker.Bake(root, PrefabPath, Profile).Mesh;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.GetTriangles(0);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                Assert.Greater(Vector3.Dot(Vector3.Cross(b - a, c - a), normals[triangles[t]]), 0f,
                               $"triangle {t / 3} faces inward: its winding disagrees with its normal");
            }
        }

        [Test]
        public void Rebaking_OverwritesTheSameMeshAsset_AndLeavesOneMergedChild()
        {
            SettlementLodBaker.Bake(root, PrefabPath, Profile);
            string path = SettlementLodBaker.MergedMeshPath(PrefabPath);
            string guid = AssetDatabase.AssetPathToGUID(path);
            Part(PrimitiveType.Capsule, blue, new Vector3(0f, 1f, 3f), Vector3.one);

            MergedLod again = SettlementLodBaker.Bake(root, PrefabPath, Profile);

            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path), "a rebuild must keep the asset every reference resolves to");
            Assert.AreEqual(path, AssetDatabase.GetAssetPath(again.Mesh));
            Assert.AreEqual(1, root.transform.Cast<Transform>().Count(t => t.name == MergedLod.ChildName));
            Assert.AreEqual(Parts().Sum(r => r.GetComponent<MeshFilter>().sharedMesh.vertexCount), again.Mesh.vertexCount,
                            "the overwritten asset holds the new parts, not the old ones");
        }

        [Test]
        public void ANestedLodGroup_IsRefused()
        {
            root.transform.GetChild(0).gameObject.AddComponent<LODGroup>();
            Assert.Throws<InvalidOperationException>(() => SettlementLodBaker.Bake(root, PrefabPath, Profile));
        }

        [Test]
        public void AWreck_HoldsFullDetail_AndTheLivingLetTheGroupChoose()
        {
            Assert.AreEqual(0, MergedLod.ForcedLevel(alive: false), "a body lying where it fell is not its rest pose");
            Assert.AreEqual(-1, MergedLod.ForcedLevel(alive: true));
        }

        [Test]
        public void TheMergedMesh_IsSavedBesideItsPrefab() =>
            Assert.AreEqual("Assets/A/B/House_LOD1_Merged.asset", SettlementLodBaker.MergedMeshPath("Assets/A/B/House.prefab"));
    }
}
