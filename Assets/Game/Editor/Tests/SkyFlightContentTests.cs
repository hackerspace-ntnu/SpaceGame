// The Sky tribe as built: every nomad wears a folded wing pack and can fly it, the StrawHat wears the
// opted-in Repulsor, a sky-wing group hops between ground sites, and a flying group's record is put
// back on the ground (D4: members come back on foot).
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
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
        public void EverySkyNomad_WearsTheWingPack_AndCanFlyIt()
        {
            foreach (NomadPrefabBuilder.NomadRecipe recipe in NomadPrefabBuilder.SkyNomads)
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
    }
}
