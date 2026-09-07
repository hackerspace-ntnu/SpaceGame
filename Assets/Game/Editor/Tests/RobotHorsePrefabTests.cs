// The built robot horses, read off disk. Each assertion is one way RobotHorseBuilder can write a
// prefab that looks complete and does something else in play:
//
//   a socket that is not startSaddled ships a horse nobody can ride;
//   an agent speed that disagrees with the blend tree's Run threshold skates the feet;
//   an outrider on the Fauna side patrols the Clankers' town as a peaceful animal;
//   an outrider with no NpcPassenger, or one whose rider is not the Clanker, rides empty;
//   a SaveableEntity with no prefab id vanishes on load.
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;

namespace SpaceGame.EditorTools
{
    public class RobotHorsePrefabTests
    {
        private GameObject wild, outrider;

        [SetUp]
        public void SetUp()
        {
            wild = AssetDatabase.LoadAssetAtPath<GameObject>(RobotHorseBuilder.WildPrefabPath);
            outrider = AssetDatabase.LoadAssetAtPath<GameObject>(RobotHorseBuilder.OutriderPrefabPath);
            if (wild == null || outrider == null)
                Assert.Ignore("The robot horses have not been built (Tools > Creatures > Build Robot Horse).");
        }

        private static T Field<T>(Object target, string field) where T : Object =>
            new SerializedObject(target).FindProperty(field).objectReferenceValue as T;

        [Test]
        public void BothHorsesAreBornSaddledWithARideableSeat()
        {
            foreach (GameObject horse in new[] { wild, outrider })
            {
                var socket = horse.GetComponent<SaddleSocket>();
                Assert.IsNotNull(socket, horse.name);
                Assert.IsTrue(new SerializedObject(socket).FindProperty("startSaddled").boolValue,
                              $"{horse.name} must spawn with its saddle on");
                Assert.IsNotNull(Field<GameObject>(socket, "saddlePrefab"), $"{horse.name} has no saddle to wear");
                Assert.IsNotNull(horse.GetComponent<MountModule>(), horse.name);
                Assert.IsNotNull(horse.GetComponent<SteerModule>(), horse.name);
                Assert.IsNotNull(horse.GetComponent<MountNetworkSync>(), horse.name);
                Assert.IsNotNull(horse.GetComponent<SaddleSaveable>(), horse.name);
            }
        }

        [Test]
        public void TheAgentRunsAtTheSpeedTheRunClipIsPlayedFor()
        {
            var agent = wild.GetComponent<NavMeshAgent>();
            Assert.AreEqual(RobotHorseBuilder.RunSpeed, agent.speed, 1e-3f);
            Assert.Greater(agent.speed, 12f, "a mounted gallop is meant to be fast");

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(RobotHorseBuilder.ControllerPath);
            var locomotion = controller.layers[0].stateMachine.states.Single(s => s.state.name == "Locomotion").state;
            var tree = (BlendTree)locomotion.motion;
            Assert.AreEqual(RobotHorseBuilder.RunSpeed, tree.children.Max(c => c.threshold), 1e-3f,
                            "the Run threshold and the agent speed are the same number or the feet skate");

            var driver = wild.GetComponent<AgentAnimatorDriver>();
            Assert.AreEqual(RobotHorseBuilder.AnimatorSpeedScale,
                            new SerializedObject(driver).FindProperty("animatorSpeedScale").floatValue, 1e-3f);
        }

        [Test]
        public void TheWildHorseIsFaunaAndTheOutriderIsAClanker()
        {
            Assert.AreEqual("Fauna", Field<FactionDefinition>(wild.GetComponent<EntityFaction>(), "faction").factionName);
            Assert.IsNotNull(wild.GetComponent<WanderModule>());
            Assert.IsNotNull(wild.GetComponent<FleeModule>());
            Assert.IsNull(wild.GetComponent<NpcPassenger>(), "a wild horse carries nobody");

            var outriderFaction = Field<FactionDefinition>(outrider.GetComponent<EntityFaction>(), "faction");
            var clanker = AssetDatabase.LoadAssetAtPath<GameObject>(ClankerBuilder.PrefabPath);
            Assert.AreEqual(Field<FactionDefinition>(clanker.GetComponent<EntityFaction>(), "faction"), outriderFaction,
                            "the outrider is on the Clankers' side");
            Assert.IsNotNull(outrider.GetComponent<PatrolModule>());
            Assert.IsNotNull(outrider.GetComponent<AlertReceiverModule>(), "an outrider answers the town's alerts");
            Assert.IsNotNull(outrider.GetComponent<AlertBroadcaster>());
        }

        [Test]
        public void TheOutriderCarriesAClankerInTheSaddle()
        {
            var passenger = outrider.GetComponent<NpcPassenger>();
            Assert.IsNotNull(passenger);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<GameObject>(ClankerBuilder.PrefabPath),
                            Field<GameObject>(passenger, "riderPrefab"));
            var seat = Field<Transform>(passenger, "seatPoint");
            Assert.IsNotNull(seat, "the rider needs a seat marker");
            Assert.Greater(seat.localPosition.y, 1f, "the seat is on the back, not at the hooves");
            Assert.IsTrue(new SerializedObject(passenger).FindProperty("spawnOnStart").boolValue);
        }

        [Test]
        public void TheClankerHasASeatedStateForTheRideOver()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ClankerBuilder.ControllerPath);
            Assert.IsTrue(controller.parameters.Any(p => p.name == ClankerBuilder.SeatedParameter
                                                        && p.type == AnimatorControllerParameterType.Bool));
            Assert.IsTrue(controller.layers[0].stateMachine.states.Any(s => s.state.name == "Seated"));
        }

        [Test]
        public void BothHorsesPersistAndReplicate()
        {
            foreach (GameObject horse in new[] { wild, outrider })
            {
                Assert.IsFalse(string.IsNullOrEmpty(horse.GetComponent<SaveableEntity>().PrefabId), horse.name);
                Assert.IsNotNull(horse.GetComponent<Unity.Netcode.NetworkObject>(), horse.name);
                Assert.IsNotNull(horse.GetComponent<TransformSaveable>(), horse.name);
                Assert.IsNotNull(horse.GetComponent<HealthSaveable>(), horse.name);
            }
        }
    }
}
