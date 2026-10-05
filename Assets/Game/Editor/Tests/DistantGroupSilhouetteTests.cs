// The folded Strider city drawn from afar: in the live spawn's slots and dealt order for every seed and
// heading, every vehicle with a merged level and far dust, shown while folded with a camera (even inside
// spawnRadius, until the live city replaces it), nothing over unloaded ground, and gliding -- never
// stepping -- between the server's writes.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class DistantGroupSilhouetteTests
    {
        [Test]
        public void TheSilhouette_StandsInTheLiveSpawnsSlots_InTheSameDealtOrder([Values(1, 7, 1234, -99, 424242)] int seed)
        {
            NpcGroupTemplate city = StriderCityTemplateTests.ReadTemplate(RosterAuthoring.StriderCityTemplateId);
            var group = new NpcGroup { Id = city.id, TemplateId = city.id, RosterSeed = seed };
            List<PlannedMember> plan = NpcGroupComposition.Resolve(group, city);
            var origin = new Vector3(812f, 40f, -1530f);
            Quaternion turn = Quaternion.Euler(0f, 123f, 0f);
            List<ColumnPlace> live = GroupColumnLayout.Places(plan, origin, turn * Vector3.forward, city.formation)
                .Where(p => !plan[p.PlanIndex].Crew).ToList();

            List<DistantGroupSilhouette.Place> drawn = DistantGroupSilhouette.Layout(city, seed);

            Assert.AreEqual(RosterAuthoring.CityFollowers + 1, drawn.Count, "every vehicle and no crew");
            Assert.AreEqual(live.Count, drawn.Count);
            for (int i = 0; i < drawn.Count; i++)
            {
                Assert.AreSame(plan[live[i].PlanIndex].Prefab, drawn[i].Prefab, $"place {i}: not the vehicle ColumnDeal dealt there");
                Assert.Less(Vector3.Distance(live[i].Position, origin + turn * drawn[i].Local), 1e-3f, $"place {i}");
            }
        }

        [Test]
        public void EveryVehicleInTheCity_HasAMergedLevelAndFarDust()
        {
            NpcGroupTemplate city = StriderCityTemplateTests.ReadTemplate(RosterAuthoring.StriderCityTemplateId);
            foreach (GameObject vehicle in DistantGroupSilhouette.Layout(city, 1).Select(p => p.Prefab).Distinct())
            {
                var lod = vehicle.GetComponent<MergedLod>();
                Assert.IsNotNull(lod, $"{vehicle.name}: run Tools/SpaceGame/Art/Bake Settlement LODs");
                Assert.IsNotNull(lod.Mesh);
                Assert.IsNotNull(vehicle.GetComponentInChildren<FarDust>(true), $"{vehicle.name}: rebuild it for far dust");
            }
        }

        [Test]
        public void ItIsDrawn_WhileFolded_WithACamera_EvenInsideSpawnRadius()
        {
            Assert.IsTrue(DistantGroupSilhouette.ShouldShow(spawned: false, cameraDistance: 600f));
            Assert.IsFalse(DistantGroupSilhouette.ShouldShow(spawned: true, cameraDistance: 600f), "the live city is drawn");
            Assert.IsTrue(DistantGroupSilhouette.ShouldShow(spawned: false, cameraDistance: 200f), "inside spawnRadius but not yet spawned: still drawn, or only dust stands there until the server ticks");
            Assert.IsFalse(DistantGroupSilhouette.ShouldShow(spawned: false, cameraDistance: float.NaN), "no camera");
        }

        [Test]
        public void GroundUnder_IsNaN_OffEveryLoadedTile() =>
            Assert.IsNaN(DistantGroupSilhouette.GroundUnder(new Terrain[0], new Vector3(5f, 0f, 5f)));

        [Test]
        public void ItGlides_TowardWhereTheServerSaid_WithoutOvershooting()
        {
            var shown = Vector3.zero;
            var target = new Vector3(3f, 0f, 0f);
            float last = 0f;
            for (int i = 0; i < 360; i++)
            {
                shown = DistantGroupSilhouette.FollowPosition(shown, target, 1f / 60f, 1.5f);
                Assert.GreaterOrEqual(shown.x, last);
                Assert.LessOrEqual(shown.x, target.x);
                last = shown.x;
            }
            Assert.Less(target.x - shown.x, 0.1f, "most of the gap closed within four lags (6 s)");
            Assert.AreEqual(15f, Mathf.Repeat(DistantGroupSilhouette.FollowYaw(355f, 15f, 1f, 1e-3f), 360f), 1e-2f,
                            "turns the short way round, through 0");
            Assert.AreEqual(5f, Mathf.Repeat(DistantGroupSilhouette.FollowYaw(355f, 15f, 1.5f * Mathf.Log(2f), 1.5f), 360f), 1e-2f,
                            "halfway after one half-life is 5 degrees, not 185");
        }

        [Test]
        public void TheSessionObject_DrawsTheDistantGroups()
        {
            var session = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Systems/NetworkGameManager.prefab");
            Assert.IsNotNull(session.GetComponent<DistantGroupSilhouette>());
        }
    }
}
