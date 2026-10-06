// The Sky tribe as built: every nomad wears a folded wing pack and can fly it, the StrawHat wears the
// opted-in Repulsor, a sky-wing group hops between ground sites, and a flying group's record is put
// back on the ground (D4: members come back on foot).
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.EditorTools;
using SpaceGame.Items;
using SpaceGame.Vehicles;
using SpaceGame.World;

namespace SpaceGame.Tests
{
    public class SkyFlightContentTests
    {
        private static readonly Vector3 FarAway = new Vector3(150000f, 6000f, 150000f);

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, path);
            return prefab;
        }

        private static InventoryItem[] StartingWorn(GameObject nomad, string name)
        {
            var body = nomad.GetComponent<EntityBodyEquipment>();
            Assert.IsNotNull(body, name);
            return (InventoryItem[])typeof(EntityBodyEquipment)
                .GetField("startingWorn", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(body);
        }

        [Test]
        public void EverySkyPerson_WearsTheWingPack_AndCanFlyIt()
        {
            foreach (NomadPrefabBuilder.NomadRecipe recipe in NomadPrefabBuilder.SkyTribePeople)
            {
                GameObject nomad = Load(recipe.PrefabPath);
                InventoryItem[] starting = StartingWorn(nomad, recipe.Name);
                Assert.AreEqual(EntityBodyEquipmentTests.WingPackPath,
                                AssetDatabase.GetAssetPath(starting[(int)BodySlot.Torso]), recipe.Name);

                var flight = nomad.GetComponent<NpcFlightModule>();
                Assert.IsNotNull(flight, recipe.Name);
                Assert.AreEqual(ModulePriority.Override, flight.Priority, recipe.Name);
                var so = new SerializedObject(flight);
                Assert.AreEqual(NpcOrnithopterBuilder.PrefabPath,
                                AssetDatabase.GetAssetPath(so.FindProperty("craftPrefab").objectReferenceValue), recipe.Name);

                Assert.IsNotNull(nomad.GetComponent<GoalTravelModule>(), $"{recipe.Name} cannot walk the last metres");
                Assert.IsNotNull(nomad.GetComponent<NpcTaskModule>(), $"{recipe.Name} cannot lead a sky-wing");
                Assert.IsNotNull(nomad.GetComponent<FormationModule>(), $"{recipe.Name} cannot follow a sky-wing");
                Assert.IsNotNull(nomad.GetComponent<SpaceGame.Core.Persistence.EntityBodyEquipmentSaveable>(),
                                 $"{recipe.Name}'s worn gear would not survive a reload");
            }
        }

        [Test]
        public void TheSkySoldier_IsFieldedByTheSkyRoster()
        {
            var roster = AssetDatabase.LoadAssetAtPath<FactionRoster>(RosterAuthoring.SkyRosterPath);
            Assert.IsNotNull(roster, RosterAuthoring.SkyRosterPath);
            Assert.IsTrue(roster.members.Any(m => AssetDatabase.GetAssetPath(m.prefab) == NomadPrefabBuilder.SkySoldier.PrefabPath),
                          "the roster skips a SkyTribePeople recipe whose prefab was never built");
        }

        [Test]
        public void TheStrawHat_WearsTheRepulsor_AndFiresIt()
        {
            NomadPrefabBuilder.NomadRecipe recipe = NomadPrefabBuilder.SkyNomads.Single(r => r.Name.EndsWith("StrawHat"));
            GameObject nomad = Load(recipe.PrefabPath);
            InventoryItem[] starting = StartingWorn(nomad, recipe.Name);
            Assert.AreEqual(EntityBodyEquipmentTests.RepulsorPath,
                            AssetDatabase.GetAssetPath(starting[(int)BodySlot.RightGauntlet]));
            Assert.IsNotNull(nomad.GetComponent<NpcGauntletUseModule>());
        }

        [Test]
        public void TheWingPack_HasAFoldedPoseForBodiesWithoutARig()
        {
            var fit = Load("Assets/Game/Prefabs/Items/Equipment/WingPack.prefab").GetComponent<WornFit>();
            Assert.IsTrue(fit.HasFoldedPose, "NPCs would wear the stowed wings hanging off nothing (D3)");
            Assert.Less(fit.FoldedSize, fit.Size, "the folded bundle is drawn at the stowed wings' size");
        }

        [Test]
        public void TheSkyWing_IsASeededGroupOfScouts_HoppingGroundSites()
        {
            NpcGroupTemplate wing = StriderCityTemplateTests.ReadTemplate(RosterAuthoring.SkyWingTemplateId);

            Assert.IsFalse(wing.runtimeOnly, "seeded at startup");
            Assert.IsTrue(wing.useStartPosition, "no Ruin exists to seed it at: it would start at the sim's origin, under the terrain");
            Assert.AreEqual(RosterAuthoring.SkyWingStart, wing.startPosition);
            Assert.AreEqual(RosterAuthoring.SkyFactionPath, AssetDatabase.GetAssetPath(wing.tribe));
            Assert.AreEqual(1, wing.members.Count(m => m.isLeader));
            Assert.IsTrue(wing.tasks.All(t => t.targetSite != SiteKind.Home), "a sky-wing cannot fly back up to the city");
            Assert.IsNull(wing.transport.smallVessel, "it flies on wing packs, not a vessel");
        }

        [Test]
        public void AFlyingGroupsRecord_IsPutBackOnTheGround()
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                block.transform.position = FarAway;
                block.transform.localScale = new Vector3(40f, 1f, 40f);
                Physics.SyncTransforms();

                Vector3 grounded = NpcWorldSim.GroundedPosition(FarAway + Vector3.up * 60f, 25f, new PhysicsGroundProbe());

                Assert.AreEqual(FarAway.y + 0.5f, grounded.y, 0.1f, "a fold mid-flight would respawn the group in the air");
            }
            finally { Object.DestroyImmediate(block); }
        }

        [Test]
        public void ARecordNearTheNavMesh_IsPutOnTheNavMesh()
        {
            Vector3 top = FarAway + Vector3.up * 0.5f;
            NavMeshData data = NpcAviatorTests.BuildSlabNavMesh(top, 40f);
            NavMeshDataInstance mesh = NavMesh.AddNavMeshData(data);
            try
            {
                Vector3 grounded = NpcWorldSim.GroundedPosition(top + new Vector3(3f, 2f, -4f), 25f, new PhysicsGroundProbe());

                Assert.AreEqual(top.y, grounded.y, 0.3f, "the record is the NavMesh point, not the point above it");
                Assert.AreEqual(top.x + 3f, grounded.x, 0.3f);
                Assert.AreEqual(top.z - 4f, grounded.z, 0.3f);
            }
            finally
            {
                mesh.Remove();
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void ASpawnedGroupsPosition_IsItsMembersCentroid_Grounded()
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var simHost = new GameObject("NpcWorldSim");
            var flier = new GameObject("Flier");
            try
            {
                block.transform.position = FarAway;
                block.transform.localScale = new Vector3(40f, 1f, 40f);
                flier.transform.position = FarAway + Vector3.up * 60f;
                Physics.SyncTransforms();
                var sim = simHost.AddComponent<NpcWorldSim>();
                var group = new NpcGroup();
                group.Live.Add(flier);

                var position = (Vector3)typeof(NpcWorldSim)
                    .GetMethod("CurrentPosition", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(sim, new object[] { group });

                Assert.AreEqual(FarAway.y + 0.5f, position.y, 0.1f, "the record keeps the fliers' mid-air centroid");
            }
            finally
            {
                Object.DestroyImmediate(flier);
                Object.DestroyImmediate(simHost);
                Object.DestroyImmediate(block);
            }
        }
    }
}
