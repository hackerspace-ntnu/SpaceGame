// Appa's herds and the Sand herders, read back off the assets AppaHerdAuthoring writes: the three
// prefabs and the world sim's templates. Read from disk, never from a freshly constructed component
// (INVARIANTS: a serialized field keeps its old value).
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class AppaHerdTests
    {
        private const string NetworkPrefabsPath = "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";
        private const string FaunaPath = "Assets/Game/ScriptableObjects/Factions/Core/FaunaFaction.asset";
        private const string WorldNavMeshPath = "Assets/Game/Settings/WorldNavMesh.asset";

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"no prefab at {path} — run Tools > Creatures > Author Appa Herds");
            return prefab;
        }

        private static SerializedObject Read(Component component) => new(component);

        private static string FactionPathOf(GameObject prefab) =>
            AssetDatabase.GetAssetPath(prefab.GetComponent<EntityFaction>().Faction);

        [Test]
        public void EveryAppa_CanTravelAsOneOfAGroup_AndIsInertAlone()
        {
            GameObject appa = Load(AppaHerdAuthoring.AppaPath);

            var formation = appa.GetComponent<FormationModule>();
            Assert.IsNotNull(formation, "NpcWorldSim keys the band through FormationModule");
            Assert.AreEqual(string.Empty, formation.FormationId, "no band until the sim names one, or every Appa is one herd");
            Assert.AreEqual(ModulePriority.Social, formation.Priority);
            Assert.IsNotNull(appa.GetComponent<NpcTaskModule>(), "the leader is handed the task list through it");
            Assert.AreEqual(ModulePriority.Fallback + 1, appa.GetComponent<GoalTravelModule>().Priority);
            Assert.IsNotNull(Read(appa.GetComponent<NpcTaskModule>()).FindProperty("animatorDriver").objectReferenceValue,
                             "the grazing dwell flag goes through the animator driver");

            SerializedObject wander = Read(appa.GetComponent<WanderModule>());
            Assert.IsTrue(wander.FindProperty("limitWanderRadius").boolValue, "a resting herd grazes in place");
        }

        [Test]
        public void TheHerder_IsASaddledFaunaAppa_CarryingASandNomad_WhoHerds()
        {
            GameObject herder = Load(AppaHerdAuthoring.HerderPath);

            Assert.AreEqual(Load(AppaHerdAuthoring.AppaPath), PrefabUtility.GetCorrespondingObjectFromSource(herder),
                            "a variant, so Appa's tuning reaches it");
            Assert.AreEqual(FaunaPath, FactionPathOf(herder), "a mount carries, the rider fights");

            SerializedObject passenger = Read(herder.GetComponent<NpcPassenger>());
            Assert.AreEqual(AppaHerdAuthoring.RiderPath,
                            AssetDatabase.GetAssetPath(passenger.FindProperty("riderPrefab").objectReferenceValue));
            var seat = (Transform)passenger.FindProperty("seatPoint").objectReferenceValue;
            Assert.IsNotNull(seat, "an unassigned seat seats the rider at the animal's feet");
            Assert.AreEqual(herder.transform, seat.parent);
            Assert.IsTrue(passenger.FindProperty("spawnOnStart").boolValue);

            Assert.IsTrue(Read(herder.GetComponent<SaddleSocket>()).FindProperty("startSaddled").boolValue,
                          "the rider sits on a saddle");
            Assert.IsNotNull(Read(herder.GetComponent<MountedRiderPose>()).FindProperty("mountModule").objectReferenceValue);
            Assert.AreEqual(ModulePriority.Social + 1, herder.GetComponent<HerdingModule>().Priority,
                            "above FormationModule, or the herder walks a column slot instead of its station");
        }

        [Test]
        public void TheLivestock_IsASandAppa_WithNoRiderAndNoHerding()
        {
            GameObject livestock = Load(AppaHerdAuthoring.LivestockPath);

            Assert.AreEqual(Load(AppaHerdAuthoring.AppaPath), PrefabUtility.GetCorrespondingObjectFromSource(livestock));
            Assert.AreEqual(AppaHerdAuthoring.SandFactionPath, FactionPathOf(livestock),
                            "faction is not replicated, so the prefab must already carry the tribe Stamp enlists it into");
            Assert.IsNull(livestock.GetComponent<NpcPassenger>());
            Assert.IsNull(livestock.GetComponent<HerdingModule>(), "HerdingModule is what marks a herder");
        }

        [Test]
        public void AllThreeAppas_AreRegistered_WithTheirOwnNetworkIdAndPrefabId()
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            string[] paths = { AppaHerdAuthoring.AppaPath, AppaHerdAuthoring.HerderPath, AppaHerdAuthoring.LivestockPath };

            foreach (string path in paths)
            {
                GameObject prefab = Load(path);
                Assert.IsTrue(list.Contains(prefab), $"{path} spawns host-only unless registered");
                Assert.AreEqual(AssetDatabase.AssetPathToGUID(path), prefab.GetComponent<SaveableEntity>().PrefabId,
                                $"{path}: a stale prefabId restores the wrong animal");
            }

            foreach (string path in paths)
            {
                var savers = Load(path).GetComponents<ISaveable>().Select(s => s.GetType()).ToArray();
                Assert.AreEqual(savers.Length, savers.Distinct().Count(),
                                $"{path} carries a saver twice; both write one key and the second wins at random");
            }

            uint[] hashes = paths.Select(p => Load(p).GetComponent<NetworkObject>().PrefabIdHash).ToArray();
            Assert.IsFalse(hashes.Contains(0u), "a zero hash is dropped by NGO");
            Assert.AreEqual(hashes.Length, hashes.Distinct().Count(), "duplicate hashes make NGO spawn the wrong prefab");
        }

        [Test]
        public void TheOriginBoundWildAppa_IsRetired()
        {
            Assert.IsFalse(StriderCityTemplateTests.ReadTemplates().Any(t => t.id == AppaHerdAuthoring.RetiredTemplateId),
                           "its saved record sits at the world origin, off the NavMesh; a new id is what frees an old save");
        }

        [Test]
        public void EveryWildHerd_IsAFaunaLeadAppaWithFollowers_StartingOnTheNavMesh()
        {
            GameObject appa = Load(AppaHerdAuthoring.AppaPath);

            foreach (AppaHerdAuthoring.WildHerd herd in AppaHerdAuthoring.WildHerds)
            {
                NpcGroupTemplate t = StriderCityTemplateTests.ReadTemplate(herd.Id);

                Assert.IsNull(t.tribe, $"{herd.Id}: wild animals are nobody's");
                Assert.IsFalse(t.runtimeOnly, $"{herd.Id}: seeded at startup");
                Assert.IsTrue(t.members.All(m => m.prefab == appa), $"{herd.Id}: plain Appas");
                Assert.AreEqual(1, t.members.Where(m => m.isLeader).Sum(m => m.count));
                Assert.AreEqual(herd.Followers + 1, t.members.Sum(m => m.count));
                Assert.IsTrue(t.tasks.Any(task => task.dwellFlag == "IsGrazing"), $"{herd.Id}: grazes with its head down");
                AssertStartsOnTheNavMesh(t);
            }
        }

        [Test]
        public void TheHerders_LeadFromThePoint_DriveSandLivestock_AndRideTheFlanks()
        {
            NpcGroupTemplate t = StriderCityTemplateTests.ReadTemplate(AppaHerdAuthoring.HerdersTemplateId);
            GameObject herder = Load(AppaHerdAuthoring.HerderPath);
            GameObject livestock = Load(AppaHerdAuthoring.LivestockPath);

            Assert.AreEqual(AppaHerdAuthoring.SandFactionPath, AssetDatabase.GetAssetPath(t.tribe));
            Assert.IsFalse(t.runtimeOnly);
            Assert.IsFalse(t.bountyHunters);

            Assert.IsTrue(t.members[0].isLeader && t.members[0].prefab == herder && t.members[0].count == 1,
                          "the point rider leads and routes the drive");
            Assert.AreEqual(AppaHerdAuthoring.HerdedLivestock, t.members.Where(m => m.prefab == livestock).Sum(m => m.count));
            Assert.AreEqual(AppaHerdAuthoring.HerderDrovers, t.members.Where(m => m.prefab == herder && !m.isLeader).Sum(m => m.count));
            Assert.Greater(System.Array.FindLastIndex(t.members, m => m.prefab == herder),
                           System.Array.FindLastIndex(t.members, m => m.prefab == livestock),
                           "drovers after the livestock, so their unused column slots are the tail's");
            Assert.IsTrue(t.members.All(m => m.prefab != null && !m.crew), "fixed prefabs, never the roster's Rider draw");
            AssertStartsOnTheNavMesh(t);
        }

        private static void AssertStartsOnTheNavMesh(NpcGroupTemplate t)
        {
            Assert.IsTrue(t.useStartPosition, $"{t.id}: no AnimalGround site exists, so a site start falls back to the origin");

            var data = AssetDatabase.LoadAssetAtPath<NavMeshData>(WorldNavMeshPath);
            NavMeshDataInstance instance = NavMesh.AddNavMeshData(data);
            try
            {
                Assert.IsTrue(NavMesh.SamplePosition(t.startPosition, out NavMeshHit hit, 5f, NavMesh.AllAreas),
                              $"{t.id} starts at {t.startPosition}, off the world NavMesh");
            }
            finally { NavMesh.RemoveNavMeshData(instance); }
        }
    }
}
