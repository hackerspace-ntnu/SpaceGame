// The built Clanker, read off disk. Each assertion is one way the builder can produce a prefab
// that looks complete in the Inspector and does nothing in play:
//
//   the Animator anywhere but on the object that owns "rig.001" binds every clip to nothing;
//   an FBX sub-asset material left on a submesh is RPR's flat colour, not the palette;
//   a NetworkObject with hash 0 spawns for the host alone;
//   a SaveableEntity with no prefab id vanishes on load;
//   a module added by script keeps priority 0 and ties with the patrol.
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class ClankerPrefabTests
    {
        private GameObject prefab;

        [SetUp]
        public void SetUp()
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClankerBuilder.PrefabPath);
            if (prefab == null)
                Assert.Ignore($"{ClankerBuilder.PrefabPath} has not been built (Tools > SpaceGame > Agents > Build Clanker Prefab).");
        }

        [Test]
        public void AnimatorSitsOnTheRigRootSoTheClipsBind()
        {
            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator);
            Assert.IsNotNull(animator.transform.Find("rig.001"), "clip paths start at rig.001");
            Assert.IsNotNull(animator.runtimeAnimatorController);
            Assert.IsFalse(animator.applyRootMotion, "the motor owns movement");
        }

        [Test]
        public void ControllerCarriesTheDriverContract()
        {
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ClankerBuilder.ControllerPath);
            Assert.IsNotNull(controller, ClankerBuilder.ControllerPath);
            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.AreEqual(controller, animator.runtimeAnimatorController, "the prefab must use the built controller");
            var names = controller.parameters.Select(p => p.name).ToArray();
            foreach (string required in new[] { "SpeedX", "SpeedY", "FallSpeed", "IsGrounded", "IsImmobalized", "IsAiming", "Death", "Die", "Hurt", "AssualtShoot" })
                Assert.Contains(required, names, $"AgentAnimatorDriver or a combat module writes '{required}' every frame");
        }

        [Test]
        public void BodyIsScaledToTargetHeightWithSolesOnTheGround()
        {
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            Assert.AreEqual(ClankerBuilder.TargetHeight, b.size.y, 0.05f);
            Assert.AreEqual(0f, b.min.y, 0.05f, "soles at the pivot, like every other agent prefab");
        }

        [Test]
        public void EverySubmeshWearsAPaletteMaterial()
        {
            var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.IsNotNull(skin);
            foreach (var m in skin.sharedMaterials)
            {
                Assert.IsNotNull(m);
                Assert.IsTrue(m.name.StartsWith("Clanker_"), $"'{m.name}' is not one of the builder's materials");
            }
        }

        [Test]
        public void CollidersAndAgentAreInTrueMetresOnTheUnscaledRoot()
        {
            Assert.AreEqual(Vector3.one, prefab.transform.localScale);
            var capsule = prefab.GetComponent<CapsuleCollider>();
            var agent = prefab.GetComponent<NavMeshAgent>();
            Assert.IsNotNull(capsule);
            Assert.IsNotNull(agent);
            Assert.AreEqual(ClankerBuilder.TargetHeight, capsule.height, 0.1f);
            Assert.Greater(agent.speed, 2f);
            Assert.Less(agent.speed, 8f);
        }

        [Test]
        public void ModulesHaveExplicitPriorities()
        {
            var chase = prefab.GetComponent<ChaseModule>();
            var use = prefab.GetComponent<NpcItemUseModule>();
            var patrol = prefab.GetComponent<PatrolModule>();
            Assert.IsNotNull(chase); Assert.IsNotNull(use); Assert.IsNotNull(patrol);
            Assert.AreEqual(ModulePriority.Reactive, chase.Priority);
            Assert.AreEqual(ModulePriority.RangedAttack, use.Priority);
            Assert.AreEqual(ModulePriority.Fallback, patrol.Priority);
        }

        [Test]
        public void ControllerHasStatesToPlay()
        {
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ClankerBuilder.ControllerPath);
            var root = controller.layers[0].stateMachine;
            Assert.GreaterOrEqual(root.states.Length, 2, "an empty state machine is a robot frozen in its bind pose");
            Assert.IsNotNull(root.defaultState);
            Assert.AreEqual("Locomotion", root.defaultState.name);
            Assert.IsNotNull(root.defaultState.motion, "the default state must drive the blend tree");
        }

        [Test]
        public void HoldsARealWeaponItRolledAtSpawn()
        {
            Assert.IsNull(prefab.GetComponent<AgentRangedCombatModule>(), "the built-in ray weapon is gone");
            Assert.IsNotNull(prefab.GetComponent<EntityInventoryComponent>());
            Assert.IsNotNull(prefab.GetComponent<EntityEquipmentController>());
            Assert.IsNotNull(prefab.GetComponent<EntityLootTable>(), "what it holds must drop");

            var loadout = prefab.GetComponent<NpcRandomLoadout>();
            Assert.IsNotNull(loadout);
            var candidates = new SerializedObject(loadout).FindProperty("candidates");
            Assert.Greater(candidates.arraySize, 0, "nothing to roll from");
            for (int i = 0; i < candidates.arraySize; i++)
                Assert.IsNotNull(candidates.GetArrayElementAtIndex(i).objectReferenceValue, $"candidate {i} is empty");

            var socket = new SerializedObject(prefab.GetComponent<EntityEquipmentController>()).FindProperty("handSocket").objectReferenceValue as Transform;
            Assert.IsNotNull(socket, "hand socket unset");
            Assert.AreEqual("DEF-hand.R", socket.name);
        }

        [Test]
        public void IsOnTheClankerFactionWithTheGlobalTable()
        {
            var faction = prefab.GetComponent<EntityFaction>();
            Assert.IsNotNull(faction);
            Assert.IsNotNull(faction.Faction);
            Assert.IsNotNull(faction.RelationshipTable);
        }

        [Test]
        public void ReplicatesAndSaves()
        {
            var net = prefab.GetComponent<NetworkObject>();
            Assert.IsNotNull(net);
            Assert.AreNotEqual(0u, net.PrefabIdHash, "clients cannot spawn a hash-0 prefab");
            Assert.IsNotNull(prefab.GetComponent<NetworkedHealthComponent>());
            var saveable = prefab.GetComponent<SaveableEntity>();
            Assert.IsNotNull(saveable);
            Assert.IsFalse(string.IsNullOrEmpty(saveable.PrefabId), "no prefab id -> vanishes on load");
            Assert.IsNotNull(prefab.GetComponent<TransformSaveable>());
            Assert.IsNotNull(prefab.GetComponent<HealthSaveable>());
            Assert.IsNotNull(prefab.GetComponent<AgentStateSaveable>());
        }
    }
}
