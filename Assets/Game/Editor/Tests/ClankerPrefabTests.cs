// The built Clankers, read off disk. Each assertion is one way the builder can produce a prefab
// that looks complete in the Inspector and does nothing in play:
//
//   the Animator anywhere but on the object that owns "rig.001" binds every clip to nothing;
//   an FBX sub-asset material left on a submesh is RPR's flat colour, not the palette;
//   a NetworkObject with hash 0 spawns for the host alone;
//   a SaveableEntity with no prefab id vanishes on load;
//   a module added by script keeps priority 0 and ties with the patrol;
//   a body variant that kept a component the RPR Clanker lacks (the old PatrolRobot herd, wander
//   and ray-gun stack) behaves like something else wearing a Clanker's gun.
//
// The behaviour tests run over every body (ClankerBuilder.AllPrefabPaths); the rig and palette
// tests are the RPR body's alone, and the body-variant tests the three Same Gev Dudios bodies'.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class ClankerPrefabTests
    {
        private const string ClankerFactionPath = "Assets/Game/ScriptableObjects/Factions/Core/ClankerFaction.asset";
        private const string HumanoidControllerPath = HumanoidControllerBuilder.ControllerPath;
        private const string HumanoidNpcPath = "Assets/Game/Prefabs/agents/Characters/Drifters/Drifter_Human.prefab";

        private static IEnumerable<string> AllBodies => ClankerBuilder.AllPrefabPaths;

        private static IEnumerable<string> VariantBodies => ClankerBodyBuilder.All.Select(r => r.PrefabPath);

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                Assert.Ignore($"{path} has not been built (Tools > SpaceGame > Agents > Build Clanker Prefab).");
            return prefab;
        }

        private static ClankerBodyBuilder.Recipe RecipeFor(string path) =>
            ClankerBodyBuilder.All.Single(r => r.PrefabPath == path);

        // ── every body ───────────────────────────────────────────────────────────

        [TestCaseSource(nameof(AllBodies))]
        public void CarriesExactlyTheClankersComponents(string path)
        {
            GameObject prefab = Load(path);
            GameObject clanker = Load(ClankerBuilder.PrefabPath);

            string[] expected = Names(clanker)
                .Concat(CharacterActionWiring.WearsHumanoidController(prefab)
                            ? CharacterActionWiring.NpcComponents.Select(t => t.Name)
                            : Enumerable.Empty<string>())
                .OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(expected, Names(prefab),
                "a body variant carries the RPR Clanker's stack plus, when it wears Humanoid.controller, the action wiring");

            Assert.IsNull(prefab.GetComponent<HerdModule>(), "the old PatrolRobot herd");
            Assert.IsNull(prefab.GetComponent<WanderModule>(), "the Clanker patrols; it does not wander");
            Assert.IsNull(prefab.GetComponent<AgentRangedCombatModule>(), "the built-in ray weapon is gone");
            Assert.IsNull(prefab.GetComponent<CloseCombatModule>());
            Assert.IsNull(prefab.GetComponentInChildren<InteractableProxy>(true));

            Assert.IsNotNull(prefab.GetComponent<AgentTargeting>());
            Assert.IsNotNull(prefab.GetComponent<ProvocationModule>());
            Assert.IsNotNull(prefab.GetComponent<IdleLookAroundModule>());
        }

        private static string[] Names(GameObject go) =>
            go.GetComponents<Component>().Select(c => c == null ? "<missing script>" : c.GetType().Name)
              .OrderBy(n => n).ToArray();

        [Test]
        public void TheHumanoidWiringAddsExactlyItsNpcComponents()
        {
            // The list the stack test above expects is the list Ensure adds, read off a fresh humanoid
            // NPC: if one changes without the other, a Clanker body passes with the wrong stack.
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var body = (GameObject)PrefabUtility.InstantiatePrefab(Load(HumanoidNpcPath), scene);
                PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (System.Type type in CharacterActionWiring.NpcComponents.Reverse())
                    Object.DestroyImmediate(body.GetComponent(type));
                string[] before = Names(body);

                Assert.IsTrue(CharacterActionWiring.Ensure(body), "Ensure reported no change on a body it had to wire");

                CollectionAssert.AreEquivalent(CharacterActionWiring.NpcComponents.Select(t => t.Name),
                                               Names(body).Except(before),
                                               "Ensure added a different set from NpcComponents");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [TestCaseSource(nameof(AllBodies))]
        public void ModulesHaveExplicitPriorities(string path)
        {
            GameObject prefab = Load(path);
            var chase = prefab.GetComponent<ChaseModule>();
            var use = prefab.GetComponent<NpcItemUseModule>();
            var patrol = prefab.GetComponent<PatrolModule>();
            var formation = prefab.GetComponent<FormationModule>();
            Assert.IsNotNull(chase); Assert.IsNotNull(use); Assert.IsNotNull(patrol); Assert.IsNotNull(formation);
            Assert.AreEqual(ModulePriority.Reactive, chase.Priority);
            Assert.AreEqual(ModulePriority.RangedAttack, use.Priority);
            Assert.AreEqual(ModulePriority.Fallback, patrol.Priority);
            Assert.AreEqual(ModulePriority.Social, formation.Priority);
        }

        [TestCaseSource(nameof(AllBodies))]
        public void MovesAtTheClankersPace(string path)
        {
            GameObject prefab = Load(path);
            GameObject clanker = Load(ClankerBuilder.PrefabPath);
            Assert.AreEqual(Vector3.one, prefab.transform.localScale, "colliders and agent are in true metres on the root");
            var agent = prefab.GetComponent<NavMeshAgent>();
            Assert.IsNotNull(agent);
            Assert.IsNotNull(prefab.GetComponent<CapsuleCollider>());
            Assert.AreEqual(clanker.GetComponent<NavMeshAgent>().speed, agent.speed, 1e-3f, "every body runs at the measured stride speed");
            Assert.Greater(agent.speed, 2f);
            Assert.Less(agent.speed, 8f);
            Assert.AreEqual(ClankerStack.WalkFraction,
                            new SerializedObject(prefab.GetComponent<NavMeshAgentMotor>()).FindProperty("walkSpeedMultiplier").floatValue, 1e-4f);
        }

        [TestCaseSource(nameof(AllBodies))]
        public void HoldsARealWeaponItRolledAtSpawn(string path)
        {
            GameObject prefab = Load(path);
            Assert.IsNotNull(prefab.GetComponent<EntityInventoryComponent>());
            Assert.IsNotNull(prefab.GetComponent<EntityEquipmentController>());
            Assert.IsNotNull(prefab.GetComponent<EntityLootTable>(), "what it holds must drop");

            // Two rolls: the gun in the hand (slot 0, every candidate real, drawn) and the artifact
            // it is carrying home (slot 1, some candidates deliberately empty, never drawn).
            NpcRandomLoadout[] loadouts = prefab.GetComponents<NpcRandomLoadout>();
            Assert.AreEqual(2, loadouts.Length, "a gun to fire and an artifact to loot");
            Assert.AreEqual(2, new SerializedObject(prefab.GetComponent<EntityInventoryComponent>()).FindProperty("inventorySize").intValue);

            var gun = new SerializedObject(loadouts.Single(l => new SerializedObject(l).FindProperty("slot").intValue == 0));
            var guns = gun.FindProperty("candidates");
            Assert.GreaterOrEqual(guns.arraySize, 5, "a variety of guns");
            for (int i = 0; i < guns.arraySize; i++)
                Assert.IsNotNull(guns.GetArrayElementAtIndex(i).objectReferenceValue, $"gun candidate {i} is empty");
            Assert.IsTrue(gun.FindProperty("equipAfterRoll").boolValue, "the gun is drawn");

            var carried = new SerializedObject(loadouts.Single(l => new SerializedObject(l).FindProperty("slot").intValue == 1));
            var artifacts = carried.FindProperty("candidates");
            Assert.GreaterOrEqual(artifacts.arraySize, 10, "a variety of artifacts, some of them nothing");
            Assert.IsFalse(carried.FindProperty("equipAfterRoll").boolValue, "the artifact stays in the bag");

            var formation = prefab.GetComponent<FormationModule>();
            Assert.IsEmpty(new SerializedObject(formation).FindProperty("formationId").stringValue, "inert until a placer names the band");

            var socket = new SerializedObject(prefab.GetComponent<EntityEquipmentController>()).FindProperty("handSocket").objectReferenceValue as Transform;
            Assert.IsNotNull(socket, "hand socket unset");
            Assert.IsTrue(socket.IsChildOf(prefab.transform), "the socket must be a bone of this body");
        }

        [TestCaseSource(nameof(AllBodies))]
        public void IsOnTheClankerFactionWithTheGlobalTable(string path)
        {
            GameObject prefab = Load(path);
            var faction = prefab.GetComponent<EntityFaction>();
            Assert.IsNotNull(faction);
            Assert.IsNotNull(faction.Faction);
            Assert.AreEqual(ClankerFactionPath, AssetDatabase.GetAssetPath(faction.Faction));
            Assert.IsNotNull(faction.RelationshipTable);

            var health = prefab.GetComponent<HealthComponent>();
            Assert.IsNotNull(health);
            Assert.AreEqual(160, new SerializedObject(health).FindProperty("maxHealth").intValue);
        }

        [TestCaseSource(nameof(AllBodies))]
        public void ReplicatesAndSaves(string path)
        {
            GameObject prefab = Load(path);
            var net = prefab.GetComponent<NetworkObject>();
            Assert.IsNotNull(net);
            Assert.AreNotEqual(0u, net.PrefabIdHash, "clients cannot spawn a hash-0 prefab");
            Assert.IsNotNull(prefab.GetComponent<NetworkedHealthComponent>());
            var saveable = prefab.GetComponent<SaveableEntity>();
            Assert.IsNotNull(saveable);
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(path), saveable.PrefabId, "no prefab id -> vanishes on load");
            Assert.IsNotNull(prefab.GetComponent<TransformSaveable>());
            Assert.IsNotNull(prefab.GetComponent<HealthSaveable>());
            Assert.IsNotNull(prefab.GetComponent<AgentStateSaveable>());
        }

        [Test]
        public void AllBodiesAreDistinctPrefabs()
        {
            string[] paths = ClankerBuilder.AllPrefabPaths.ToArray();
            Assert.AreEqual(4, paths.Length, "the RPR Clanker and three Same Gev Dudios bodies");
            CollectionAssert.AllItemsAreUnique(paths);
            Assert.AreEqual(ClankerBuilder.PrefabPath, paths[0], "the RPR body first: the outrider's rider and the reference stack");
        }

        // ── the RPR body ─────────────────────────────────────────────────────────

        [Test]
        public void AnimatorSitsOnTheRigRootSoTheClipsBind()
        {
            GameObject prefab = Load(ClankerBuilder.PrefabPath);
            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator);
            Assert.IsNotNull(animator.transform.Find("rig.001"), "clip paths start at rig.001");
            Assert.IsNotNull(animator.runtimeAnimatorController);
            Assert.IsFalse(animator.applyRootMotion, "the motor owns movement");
        }

        [Test]
        public void ControllerCarriesTheDriverContract()
        {
            GameObject prefab = Load(ClankerBuilder.PrefabPath);
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
            GameObject prefab = Load(ClankerBuilder.PrefabPath);
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            Assert.AreEqual(ClankerBuilder.TargetHeight, b.size.y, 0.05f);
            Assert.AreEqual(0f, b.min.y, 0.05f, "soles at the pivot, like every other agent prefab");
            Assert.AreEqual(ClankerBuilder.TargetHeight, prefab.GetComponent<CapsuleCollider>().height, 0.1f);
        }

        [Test]
        public void EverySubmeshWearsAPaletteMaterial()
        {
            var skin = Load(ClankerBuilder.PrefabPath).GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.IsNotNull(skin);
            foreach (var m in skin.sharedMaterials)
            {
                Assert.IsNotNull(m);
                Assert.IsTrue(m.name.StartsWith("Clanker_"), $"'{m.name}' is not one of the builder's materials");
            }
        }

        [Test]
        public void HandSocketIsTheRigifyRightHand()
        {
            var equipment = Load(ClankerBuilder.PrefabPath).GetComponent<EntityEquipmentController>();
            var socket = new SerializedObject(equipment).FindProperty("handSocket").objectReferenceValue as Transform;
            Assert.IsNotNull(socket);
            Assert.AreEqual("DEF-hand.R", socket.name);
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

        // ── the Same Gev Dudios bodies ───────────────────────────────────────────

        [TestCaseSource(nameof(VariantBodies))]
        public void VariantIsAHumanoidOnTheAstronautController(string path)
        {
            GameObject prefab = Load(path);
            Animator[] animators = prefab.GetComponentsInChildren<Animator>(true);
            Assert.AreEqual(1, animators.Length, "one Animator, on the body instance");
            Animator animator = animators[0];
            Assert.AreNotEqual(prefab.transform, animator.transform, "the Animator sits on the body child, not the unscaled root");
            Assert.IsNotNull(animator.avatar);
            Assert.IsTrue(animator.avatar.isHuman, "a generic avatar leaves the body standing still with a clean console");
            Assert.AreEqual(HumanoidControllerPath, AssetDatabase.GetAssetPath(animator.runtimeAnimatorController));
            Assert.IsFalse(animator.applyRootMotion, "the motor owns movement");

            var socket = new SerializedObject(prefab.GetComponent<EntityEquipmentController>()).FindProperty("handSocket").objectReferenceValue as Transform;
            Assert.AreEqual(RecipeFor(path).HandBone, socket.name, "the humanoid avatar's right hand");
        }

        [TestCaseSource(nameof(VariantBodies))]
        public void VariantStandsOnItsSolesAndDropsTheBakedWeapons(string path)
        {
            GameObject prefab = Load(path);
            ClankerBodyBuilder.Recipe recipe = RecipeFor(path);

            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                Assert.IsInstanceOf<SkinnedMeshRenderer>(r, $"'{r.name}' is a leftover baked-in hand prop; the loadout supplies the gun");
                Mesh mesh = ((SkinnedMeshRenderer)r).sharedMesh;
                Assert.AreEqual(recipe.Fbx, AssetDatabase.GetAssetPath(mesh), $"'{r.name}' is not the body mesh");
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                Bounds body = ClankerBodyBuilder.SkinnedBounds(instance);
                Assert.AreEqual(0f, body.min.y, 0.05f, "soles at the pivot, like every other agent prefab");
                Assert.AreEqual(body.size.y, prefab.GetComponent<CapsuleCollider>().height, 0.1f, "the capsule is the body's height");
                Assert.Less(prefab.GetComponent<NavMeshAgent>().radius, body.extents.x,
                            "the footprint is the torso; a T-posed arm span is reach, not width");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [TestCaseSource(nameof(VariantBodies))]
        public void VariantPlaysItsClipsFastEnoughNotToSkate(string path)
        {
            GameObject prefab = Load(path);
            var driver = new SerializedObject(prefab.GetComponent<AgentAnimatorDriver>());
            float runSpeed = prefab.GetComponent<NavMeshAgent>().speed;
            Assert.AreEqual(runSpeed / ClankerBodyBuilder.AuthoredRunSpeed, driver.FindProperty("animatorSpeedScale").floatValue, 1e-3f,
                            "playback must scale with the run the body was retuned to, or the feet skate");
            Assert.AreEqual(ClankerBodyBuilder.AnimationSpeedMultiplier, driver.FindProperty("animationSpeedMultiplier").floatValue, 1e-4f);
            Assert.AreEqual(ClankerBodyBuilder.WalkAnimBoost, driver.FindProperty("walkAnimBoost").floatValue, 1e-4f);
        }
    }
}
