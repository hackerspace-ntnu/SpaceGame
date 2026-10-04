// The Strider elder: the user's cyborg, a humanoid torso on four robotic legs, built on the crab
// walker's legged stack. It rides its house's standing post and goes ashore with the crew.
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Creatures;
using SpaceGame.Creatures.Crab;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class StriderElderTests
    {
        private static readonly string[] LegIds = { "FL", "FR", "RL", "RR" };
        private static readonly string[] Joints = { "Coxa", "Hip", "Knee", "Ankle", "Foot" };

        private static GameObject Prefab()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(StriderElderBuilder.PrefabPath);
            Assert.IsNotNull(p, "run Tools/Creatures/Build Strider Elder");
            return p;
        }

        private static Transform Find(GameObject root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        [Test]
        public void HasFourLegs_EachJointWithAPin()
        {
            GameObject elder = Prefab();
            foreach (string id in LegIds)
            foreach (string joint in Joints)
            {
                Transform bone = Find(elder, $"{joint}_{id}");
                Assert.IsNotNull(bone, $"{joint}_{id}: WalkerRig assembles a leg from these names");
                Transform pin = bone.Cast<Transform>().FirstOrDefault(c => c.name.Contains("Pin"));
                Assert.IsNotNull(pin, $"{joint}_{id} has no pin: its hinge would be guessed from the rest pose");
                Assert.IsNotNull(pin.GetComponent<MeshFilter>(), $"{pin.name} needs a mesh to be measured");
            }
        }

        [Test]
        public void TheLegsMeasureAsFourLegs()
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(Prefab());
            try
            {
                var legs = instance.GetComponent<CrabLocomotion>();
                legs.Initialise();
                Assert.IsTrue(legs.IsReady);
                Assert.AreEqual(4, legs.LegCount);
                Assert.Greater(legs.MaxSpeed, 0.5f, "a gait that cannot walk strands the elder ashore");
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void IsAStriderAgent_ThatReplicatesSavesAndWalksToAGoal()
        {
            GameObject elder = Prefab();
            Assert.IsNotNull(elder.GetComponent<AgentController>());
            Assert.IsNotNull(elder.GetComponent<HealthComponent>());
            Assert.IsNotNull(elder.GetComponent<GoalTravelModule>(), "CrewShift walks it to and from the gangway by goal");
            Assert.IsNotNull(elder.GetComponent<StandingRider>(), "it rides a standing post, never a crew seat");
            var net = elder.GetComponent<Unity.Netcode.NetworkObject>();
            Assert.IsNotNull(net);
            Assert.AreNotEqual(0u, net.PrefabIdHash);
            Assert.IsTrue(net.SceneMigrationSynchronization);
            Assert.IsFalse(string.IsNullOrEmpty(elder.GetComponent<SpaceGame.Core.Persistence.SaveableEntity>().PrefabId));
            var faction = new SerializedObject(elder.GetComponent<EntityFaction>()).FindProperty("faction").objectReferenceValue;
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.StriderFactionPath), faction);
            Assert.IsNull(elder.GetComponent<CloseCombatModule>(), "the elder has no attack of its own");
            Assert.IsNull(elder.GetComponent<AgentRangedCombatModule>());
        }

        [Test]
        public void WalksFacingForward_AndSwaysItsTorso()
        {
            GameObject elder = Prefab();
            var driver = new SerializedObject(elder.GetComponent<CrabDriver>());
            Assert.IsFalse(driver.FindProperty("lateralSteering").boolValue, "an elder walks where it faces, not crabwise");
            var sway = new SerializedObject(elder.GetComponent<StriderElderSway>());
            Assert.IsNotNull(sway.FindProperty("chest").objectReferenceValue);
            Assert.IsNotNull(sway.FindProperty("head").objectReferenceValue);
        }

        [Test]
        public void StandsAsTallAsTheOtherNpcs()
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(Prefab());
            try
            {
                instance.transform.position = Vector3.zero;
                float low = float.MaxValue, high = float.MinValue;
                foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
                {
                    low = Mathf.Min(low, r.bounds.min.y);
                    high = Mathf.Max(high, r.bounds.max.y);
                }
                Assert.AreEqual(StriderElderBuilder.Height, high - low, 0.15f);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void IsRegisteredAsANetworkPrefab()
        {
            var list = AssetDatabase.LoadAssetAtPath<Unity.Netcode.NetworkPrefabsList>(
                "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset");
            Assert.IsNotNull(list);
            Assert.IsTrue(list.PrefabList.Any(p => p.Prefab == Prefab()), "a spawned elder would exist for the host alone");
        }
    }
}
