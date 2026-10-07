// Every Strider city vehicle and every Sky fleet hull, read off disk: a LODGroup whose LOD0 is the
// prefab's own renderers and whose LOD1 is its merged mesh on disk beside it, carrying Mesh LODs and
// costing fewer draws than the original; no particle in any level; the fleet's one group on its city.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class SettlementLodPrefabTests
    {
        public static IEnumerable<string> Baked => SettlementLodBaker.StriderPrefabPaths.Concat(SettlementLodBaker.SkyPrefabPaths);

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"{path} is missing");
            return prefab;
        }

        [TestCaseSource(nameof(Baked))]
        public void EveryPrefab_DrawsItsOwnRenderersNear_AndOneMergedMeshFar(string path)
        {
            GameObject prefab = Load(path);
            var lod = prefab.GetComponent<MergedLod>();
            Assert.IsNotNull(lod, $"{prefab.name} has no merged level: run Tools/SpaceGame/Art/Bake Settlement LODs");
            Assert.AreSame(prefab.GetComponent<LODGroup>(), lod.Group);

            LOD[] lods = lod.Group.GetLODs();
            Assert.AreEqual(2, lods.Length);
            Renderer[] originals = prefab.GetComponentsInChildren<Renderer>(true)
                .Where(r => (r is MeshRenderer || r is SkinnedMeshRenderer) && r != lod.MergedRenderer).ToArray();
            CollectionAssert.AreEquivalent(originals, lods[0].renderers, "LOD0 is the prefab's own renderers");
            CollectionAssert.AreEqual(new Renderer[] { lod.MergedRenderer }, lods[1].renderers);
            Assert.IsFalse(lods.SelectMany(l => l.renderers).Any(r => r is ParticleSystemRenderer), "dust and smoke run at every distance");

            Assert.AreEqual(SettlementLodBaker.MergedMeshPath(path), AssetDatabase.GetAssetPath(lod.Mesh));
            Assert.Greater(lod.Mesh.lodCount, 1, "no Mesh LODs in the merged level");
            Assert.AreEqual(lod.Materials.Length, lod.Mesh.subMeshCount);
            Assert.Less(lod.Mesh.subMeshCount, originals.Sum(r => r.sharedMaterials.Length), "the merged level must cost fewer draws");
        }

        [TestCaseSource(nameof(Baked))]
        public void AVehicleThatCanDie_HasItsHealthOnTheRoot(string path)
        {
            GameObject prefab = Load(path);
            if (prefab.GetComponentInChildren<HealthComponent>(true) == null) return;
            Assert.IsNotNull(prefab.GetComponent<HealthComponent>(), "MergedLod holds a wreck at LOD0 by the root's health");
        }

        [Test]
        public void TheFleetsOnlyLodGroup_IsItsCitys()
        {
            GameObject fleet = Load(SkyFleetBuilder.FleetPrefabPath);
            LODGroup[] groups = fleet.GetComponentsInChildren<LODGroup>(true);
            Assert.AreEqual(1, groups.Length, "a renderer in two LODGroups draws twice");
            Assert.AreNotSame(fleet, groups[0].gameObject);
            Assert.IsNotNull(groups[0].GetComponent<MergedLod>());
        }

        [Test]
        public void TheFarDustStops_WhereTheStriderVehiclesAreCulled() =>
            Assert.AreEqual(SettlementLodSettings.Load().strider.cullBeyondMetres, VehicleDustWiring.FarDustCullDistance);
    }
}
