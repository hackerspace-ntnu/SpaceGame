// The prefabs that wear the humanoid controller, and the one that replicates it.
//
// Every failure pinned here was silent before this existed: a trigger name the controller does not
// have (Die, Throw, ShootRifle, Pet, AssaultShoot — all fired for months into nothing), a network
// animator still synchronising the old parameter list, a held-item style with no pose.
using System;
using System.Linq;
using NUnit.Framework;
using SpaceGame.Agents;
using SpaceGame.Items;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class HumanoidWiringAssetTests
    {
        /// <summary>
        /// Every serialized string that names an animator parameter on its own prefab's animator.
        /// NpcPassenger's seated bool is not here on purpose: it is optional by design and checked
        /// against the controller at runtime.
        /// </summary>
        private static readonly (Type Component, string Field, AnimatorControllerParameterType Type)[] Named =
        {
            (typeof(CloseCombatModule), "attackAnimTrigger", AnimatorControllerParameterType.Trigger),
            (typeof(HealthReactionModule), "hurtAnimTrigger", AnimatorControllerParameterType.Trigger),
            (typeof(HealthReactionModule), "dieAnimTrigger", AnimatorControllerParameterType.Trigger),
            (typeof(PettableModule), "happyTrigger", AnimatorControllerParameterType.Trigger),
            (typeof(FightOrFlightModule), "roarTrigger", AnimatorControllerParameterType.Trigger),
        };

        [Test]
        public void EveryAnimatorStringOnAPrefabNamesAParameterItsControllerHas()
        {
            var failures = new System.Collections.Generic.List<string>();

            foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs" })
                                                 .Select(AssetDatabase.GUIDToAssetPath))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Animator animator = prefab != null ? prefab.GetComponentInChildren<Animator>(true) : null;
                RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
                if (controller is AnimatorOverrideController overrides) controller = overrides.runtimeAnimatorController;
                if (!(controller is UnityEditor.Animations.AnimatorController animatorController)) continue;

                foreach ((Type component, string field, AnimatorControllerParameterType type) in Named)
                {
                    foreach (Component c in prefab.GetComponentsInChildren(component, true))
                    {
                        string value = new SerializedObject(c).FindProperty(field)?.stringValue;
                        if (string.IsNullOrEmpty(value)) continue;
                        if (animatorController.parameters.Any(p => p.name == value && p.type == type)) continue;

                        failures.Add($"{path}: {component.Name}.{field} = '{value}', which {controller.name} has no {type} called");
                    }
                }
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void PlayerNetworkAnimatorSynchronisesExactlyTheControllerParameters()
        {
            string[] baked = NetworkAnimatorRebake.BakedParameters(NetworkAnimatorRebake.PlayerPrefab);
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                HumanoidControllerBuilder.ControllerPath);

            CollectionAssert.AreEquivalent(controller.parameters.Select(p => p.name), baked,
                                           "the player prefab's NetworkAnimator was baked against another controller; " +
                                           "remote players would miss every new parameter");
        }

        [Test]
        public void EveryHoldStyleHasAPose()
        {
            var profile = AssetDatabase.LoadAssetAtPath<HumanoidAnimationProfile>(HumanoidControllerBuilder.ProfilePath);
            ItemGrip.HoldStyle[] posed = profile.HoldPoses.Select(p => p.style).ToArray();

            foreach (ItemGrip.HoldStyle style in Enum.GetValues(typeof(ItemGrip.HoldStyle)))
            {
                if (style == ItemGrip.HoldStyle.None) continue;
                Assert.Contains(style, posed, $"an item held {style} would stand the body in its idle");
            }
        }
    }
}
