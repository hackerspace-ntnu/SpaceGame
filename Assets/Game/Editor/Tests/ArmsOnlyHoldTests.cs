// A hand tool is held while its holder walks, and the hold must not bend the walk.
//
// Carry and Ready used to play on the Upper Body layer, whose mask has the Body part on, so the idle's and
// the sword idle's spine overrode the walk's: a walking Raxy stood at 72 (Carry) and 46 (Ready) degrees from
// the hips to the head against 88 empty-handed. Nothing threw; residents just walked hunched. These pin that
// the two arms-only styles leave the spine to the walk, measured on a real Raxy through the real controller.
using System.Linq;
using NUnit.Framework;
using SpaceGame.Items;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class ArmsOnlyHoldTests
    {
        private const string RaxyPath = "Assets/Game/Prefabs/Agents/Characters/Raxy/Raxy_handyman.prefab";

        /// <summary>How much a hold may bend the walking trunk, degrees from the hips to the head.</summary>
        private const float MaxTrunkChange = 3f;

        private static readonly ItemGrip.HoldStyle[] ArmsOnly = { ItemGrip.HoldStyle.Carry, ItemGrip.HoldStyle.Ready };

        [Test]
        public void CarryAndReady_ArePosedByTheArmsLayerAndTheUpperBodyLayerRests()
        {
            var profile = AssetDatabase.LoadAssetAtPath<HumanoidAnimationProfile>(HumanoidControllerBuilder.ProfilePath);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(HumanoidControllerBuilder.ControllerPath);
            AnimatorStateMachine arms = controller.layers.First(l => l.name == HumanoidLayers.HoldArms).stateMachine;
            AnimatorStateMachine upper = controller.layers.First(l => l.name == HumanoidLayers.UpperBody).stateMachine;

            foreach (ItemGrip.HoldStyle style in ArmsOnly)
            {
                Assert.IsTrue(profile.HoldPoses.First(p => p.style == style).armsOnly,
                              $"{style} is carried while walking: Generate Hold Clips marks it arms-only");
                string state = HumanoidPoseLayers.HoldStateName(style);
                Assert.IsTrue(arms.states.Any(s => s.state.name == state), $"the Hold Arms layer has no '{state}'");
                Assert.IsFalse(upper.states.Any(s => s.state.name == state),
                               $"the Upper Body layer still poses '{state}' with the body mask on, which bends the walk");
            }
        }

        [Test]
        public void AWalkingRaxy_IsAsUprightHoldingACarryOrReadyToolAsEmptyHanded()
        {
            float empty = WalkingTrunkElevation(ItemGrip.HoldStyle.None);
            foreach (ItemGrip.HoldStyle style in ArmsOnly)
                Assert.AreEqual(empty, WalkingTrunkElevation(style), MaxTrunkChange,
                                $"a walking Raxy holding a {style} tool leans away from its empty-handed walk (hips to head, degrees)");
        }

        /// <summary>Mean hips-to-head elevation over the gait, degrees from the horizontal: 90 is vertical.</summary>
        private static float WalkingTrunkElevation(ItemGrip.HoldStyle style)
        {
            GameObject prefab = AssetDatabase.LoadAllAssetsAtPath(RaxyPath).OfType<GameObject>().First(g => g.transform.parent == null);
            Animator source = prefab.GetComponentInChildren<Animator>(true);
            GameObject model = PrefabUtility.GetCorrespondingObjectFromOriginalSource(source.gameObject);

            var body = (GameObject)Object.Instantiate(model);
            body.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var animator = body.GetComponent<Animator>();
                if (animator == null) animator = body.AddComponent<Animator>();
                animator.runtimeAnimatorController = source.runtimeAnimatorController;
                animator.avatar = source.avatar;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.SetBool(HumanoidParams.IsGrounded, true);
                animator.SetFloat(HumanoidParams.SpeedY, 4f);
                animator.SetInteger(HumanoidParams.HoldStyle, (int)style);

                // What HoldAnimator does for a held item: the Upper Body layer follows the hold.
                animator.SetLayerWeight(animator.GetLayerIndex(HumanoidLayers.UpperBody), style == ItemGrip.HoldStyle.None ? 0f : 1f);

                const float step = 1f / 30f;
                for (int i = 0; i < 90; i++) animator.Update(step);

                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                float sum = 0f;
                const int samples = 60;
                for (int i = 0; i < samples; i++)
                {
                    animator.Update(step);
                    Vector3 trunk = head.position - hips.position;
                    sum += Mathf.Atan2(trunk.y, new Vector2(trunk.x, trunk.z).magnitude) * Mathf.Rad2Deg;
                }

                return sum / samples;
            }
            finally
            {
                Object.DestroyImmediate(body);
            }
        }
    }
}
