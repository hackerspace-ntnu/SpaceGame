using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class StriderCrabOutriderTests
    {
        private static GameObject Prefab()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(StriderCrabOutriderBuilder.PrefabPath);
            Assert.IsNotNull(p, "run Tools/Creatures/Build Strider Crab Outrider");
            return p;
        }

        [Test]
        public void IsAStriderAgent_ThatReplicatesAndSaves()
        {
            GameObject crab = Prefab();
            Assert.IsNotNull(crab.GetComponent<AgentController>());
            Assert.IsNotNull(crab.GetComponent<HealthComponent>());
            var net = crab.GetComponent<Unity.Netcode.NetworkObject>();
            Assert.IsNotNull(net); Assert.AreNotEqual(0u, net.PrefabIdHash);
            Assert.IsFalse(string.IsNullOrEmpty(crab.GetComponent<SpaceGame.Core.Persistence.SaveableEntity>().PrefabId));
            var faction = new SerializedObject(crab.GetComponent<EntityFaction>()).FindProperty("faction").objectReferenceValue;
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.StriderFactionPath), faction);
        }

        [Test]
        public void StaysOnTheGround_AndCannotBeShovedAround()
        {
            GameObject crab = Prefab();
            Assert.IsNotNull(crab.GetComponent<SpaceGame.World.Safety.UnderTerrainGuard>(),
                "a migrating agent that falls through a chunk seam is lost without the guard");
            Assert.AreEqual(100f, crab.GetComponent<Rigidbody>().mass, 1e-3f, "CrabWalker6's hand-tuned mass");
        }

        [Test]
        public void CarriesAStriderRider_AndHasNoAttack()
        {
            GameObject crab = Prefab();
            var passenger = crab.GetComponent<NpcPassenger>();
            Assert.IsNotNull(passenger);
            var rider = new SerializedObject(passenger).FindProperty("riderPrefab").objectReferenceValue as GameObject;
            Assert.IsNotNull(rider);
            StringAssert.StartsWith("Strider_", rider.name, "faction is not replicated: the rider must ship as a Strider");
            Assert.IsNull(crab.GetComponent<CloseCombatModule>(), "the mount carries, the rider shoots");
            Assert.IsNull(crab.GetComponent<AgentRangedCombatModule>());
        }

        [Test]
        public void KeepsUpWithTheCity()
        {
            float speed = new SerializedObject(Prefab().GetComponent<SpaceGame.Creatures.CrabDriver>()).FindProperty("moveSpeed").floatValue;
            Assert.GreaterOrEqual(speed, StriderCityBuilder.CityLeaderSpeed,
                "LeggedDriver cannot exceed its moveSpeed; a slower crab falls behind the column for good");
            float step = new SerializedObject(Prefab().GetComponent<SpaceGame.Creatures.Crab.CrabLocomotion>()).FindProperty("stepDuration").floatValue;
            Assert.LessOrEqual(step, 0.22f + 1e-4f, "the stock 0.4 s gait caps the crab at 1.76 m/s whatever moveSpeed says (spike)");
        }

        [Test]
        public void HoldsItsFlankAtAStop()
        {
            var formation = new SerializedObject(Prefab().GetComponent<FormationModule>());
            Assert.IsTrue(formation.FindProperty("holdSlotAtRest").boolValue,
                "the rest ring is 3-6 m from the lead house's pivot: a crab gathering there walks in among its legs -- nothing parks (user 2026-09-24)");
        }
    }
}
