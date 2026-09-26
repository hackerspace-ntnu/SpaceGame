// The generated humanoid controller, read off disk.
//
// It is generated, so what these pin is the contract the rest of the game relies on and that no
// compiler checks: layer names code looks up by string, parameters code writes by string, the two
// walk speeds every NPC's stride is tuned against, and the import rules whose failure is silent (a
// generic clip on a humanoid animates nothing and logs nothing).
using System.Linq;
using NUnit.Framework;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class HumanoidControllerAssetTests
    {
        private static AnimatorController Controller =>
            AssetDatabase.LoadAssetAtPath<AnimatorController>(HumanoidControllerBuilder.ControllerPath);

        [Test]
        public void LayersAndParametersAreTheContractCodeWritesAgainst()
        {
            AnimatorController controller = Controller;
            Assert.IsNotNull(controller, $"{HumanoidControllerBuilder.ControllerPath} is missing");

            CollectionAssert.AreEqual(HumanoidLayers.Order, controller.layers.Select(l => l.name).ToArray(),
                                      "PlayerAimRig, HoldAnimator and CharacterActions find layers by these names, " +
                                      "and the Glide layer must stay on top");

            string Mask(string layer) =>
                AssetDatabase.GetAssetPath(controller.layers.First(l => l.name == layer).avatarMask);
            Assert.AreEqual(HumanoidMasks.Folder + "UpperBody.mask", Mask(HumanoidLayers.UpperBody));
            Assert.AreEqual(HumanoidMasks.Folder + "LeftArm.mask", Mask(HumanoidLayers.WornLeft));
            Assert.AreEqual(HumanoidMasks.Folder + "UpperBody.mask", Mask(HumanoidLayers.ActionUpper));
            Assert.AreEqual(HumanoidMasks.Folder + "RightArm.mask", Mask(HumanoidLayers.ActionRightArm));
            Assert.AreEqual(HumanoidMasks.Folder + "UpperBody.mask", Mask(HumanoidLayers.ActionAdditive));

            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                AnimatorLayerBlendingMode expected = layer.name == HumanoidLayers.ActionAdditive
                    ? AnimatorLayerBlendingMode.Additive
                    : AnimatorLayerBlendingMode.Override;
                Assert.AreEqual(expected, layer.blendingMode,
                                $"'{layer.name}' — a recoil on an overriding layer snaps an aimed arm level, " +
                                "and any other layer set additive doubles its pose");
            }

            foreach ((string name, AnimatorControllerParameterType type, float value) in HumanoidParams.Contract)
            {
                AnimatorControllerParameter p = controller.parameters.FirstOrDefault(x => x.name == name);
                Assert.IsNotNull(p, $"parameter '{name}' is written by code and missing from the controller");
                Assert.AreEqual(type, p.type, $"parameter '{name}' has the wrong type");
                if (type == AnimatorControllerParameterType.Float)
                    Assert.AreEqual(value, p.defaultFloat, $"'{name}' default — an action speed of 0 freezes every action on frame one");
            }
        }

        [Test]
        public void LocomotionIsLayerZeroWithWalkAtFourAndRunAtSevenPointTwo()
        {
            AnimatorStateMachine baseLayer = Controller.layers[0].stateMachine;
            Assert.AreEqual(HumanoidBaseLayer.MoveState, baseLayer.defaultState.name,
                            "AgentAnimatorDriver finishes strides by reading layer 0's current state");

            var move = (BlendTree)baseLayer.defaultState.motion;
            Vector2[] cells = move.children.Select(c => c.position).ToArray();
            foreach (Vector2 expected in new[] { new Vector2(0f, 4f), new Vector2(0f, 7.2f), Vector2.zero })
            {
                Assert.Contains(expected, cells,
                                "every humanoid prefab's animation speed multiplier is tuned against these cells; " +
                                "moving them makes every NPC skate");
            }
        }

        [Test]
        public void EveryStatePlaysHumanoidClipsWithoutEventsAndWritesDefaults()
        {
            foreach (AnimatorControllerLayer layer in Controller.layers)
            {
                foreach (ChildAnimatorState child in layer.stateMachine.states)
                {
                    Assert.IsTrue(child.state.writeDefaultValues,
                                  $"'{layer.name}/{child.state.name}' — mixed Write Defaults leaves properties stuck");

                    foreach (AnimationClip clip in ClipsOf(child.state.motion))
                    {
                        Assert.IsTrue(clip.humanMotion, $"'{clip.name}' is not a Humanoid clip; it animates nothing here");
                        Assert.IsEmpty(AnimationUtility.GetAnimationEvents(clip),
                                       $"'{clip.name}' carries events nothing receives: 'AnimationEvent has no receiver' every play");
                    }
                }
            }
        }

        private static System.Collections.Generic.IEnumerable<AnimationClip> ClipsOf(Motion motion)
        {
            if (motion is AnimationClip clip) return new[] { clip };
            if (motion is BlendTree tree) return tree.children.SelectMany(c => ClipsOf(c.motion));
            return Enumerable.Empty<AnimationClip>();
        }
    }
}
