// The gestures as built into the astronaut's controller, read off disk, plus the emote table's
// own rules. Each assertion is one way the builder can leave a gesture that plays nothing: a
// state with no trigger is unreachable; an aimed state without its mirrored twin stabs with the
// wrong arm; a hold transition without the Gesturing guard evicts the clip on its second frame;
// an emote the chat knows but the controller does not is a command that does nothing.
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.EditorTools
{
    public class PlayerGestureTests
    {
        private const string ControllerPath = "Assets/Game/Art/Animations/Player/AstronautArmature.controller";
        private const string PlayerPrefabPath = "Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab";

        private AnimatorStateMachine upperBody;

        [SetUp]
        public void SetUp()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            AnimatorControllerLayer layer = controller?.layers.FirstOrDefault(l => l.name == "Upper Body");
            if (layer == null) Assert.Ignore("No Upper Body layer (Tools > SpaceGame > Player > Build Upper Body Layer).");
            upperBody = layer.stateMachine;
            if (!upperBody.states.Any(s => s.state.name == "Stab Right"))
                Assert.Ignore("Gestures not built (Tools > SpaceGame > Player > Build Gestures).");
        }

        [Test]
        public void EveryAimedGestureHasAMirroredTwinBlendedOnPitch()
        {
            foreach (string name in PlayerGestureBuilder.AimedGestures)
            {
                AnimatorState right = State(name + " Right");
                AnimatorState left = State(name + " Left");
                Assert.IsFalse(right.mirror, name + " Right plays as authored");
                Assert.IsTrue(left.mirror, name + " Left is the mirrored twin");

                foreach (AnimatorState state in new[] { right, left })
                {
                    var tree = state.motion as BlendTree;
                    Assert.IsNotNull(tree, state.name + " blends on pitch");
                    Assert.AreEqual("AimPitch", tree.blendParameter);
                    Assert.AreEqual(3, tree.children.Length, "down, level, up");

                    AnimatorStateTransition into = upperBody.anyStateTransitions.Single(t => t.destinationState == state);
                    Assert.IsTrue(into.conditions.Any(c => c.parameter == name && c.mode == AnimatorConditionMode.If), "entered on its trigger");
                    Assert.IsTrue(into.conditions.Any(c => c.parameter == "HoldMirror"), "the arm picks the twin");
                    Assert.IsTrue(state.transitions.Any(t => t.hasExitTime && t.destinationState == upperBody.defaultState), "leaves when the clip ends");
                }
            }
        }

        [Test]
        public void EveryEmoteTheChatKnowsIsAStateTheControllerHas()
        {
            foreach (PlayerEmotes.Emote emote in PlayerEmotes.Table)
            {
                AnimatorState state = State(emote.Trigger);
                Assert.IsNotNull(state.motion as AnimationClip, emote.Trigger + " plays one clip");
                AnimatorStateTransition into = upperBody.anyStateTransitions.Single(t => t.destinationState == state);
                Assert.IsTrue(into.conditions.Any(c => c.parameter == emote.Trigger && c.mode == AnimatorConditionMode.If));
                Assert.Greater(emote.Seconds, 0.5f, "the layer has to stay up for the clip");
            }
        }

        [Test]
        public void HoldAndRaiseTransitionsStandDownWhileGesturing()
        {
            var gestureNames = PlayerGestureBuilder.AimedGestures.SelectMany(n => new[] { n + " Right", n + " Left" })
                .Concat(PlayerEmotes.Table.Select(e => e.Trigger)).ToHashSet();

            foreach (AnimatorStateTransition t in upperBody.anyStateTransitions)
            {
                if (t.destinationState == null || gestureNames.Contains(t.destinationState.name)) continue;
                if (t.destinationState.name == "Pet") continue;
                Assert.IsTrue(t.conditions.Any(c => c.parameter == "Gesturing" && c.mode == AnimatorConditionMode.IfNot),
                              $"Any State -> {t.destinationState.name} would evict a running gesture");
            }
        }

        [Test]
        public void ThePlayerCanEmote()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(prefab.GetComponent<PlayerEmotes>(), "no PlayerEmotes on the player: /wave does nothing");
        }

        [Test]
        public void TheEmoteTableIsLookedUpByTypedName()
        {
            Assert.AreEqual(0, PlayerEmotes.IndexOf("wave"));
            Assert.AreEqual(0, PlayerEmotes.IndexOf(" WAVE "), "the chat is case-insensitive");
            Assert.AreEqual(-1, PlayerEmotes.IndexOf("backflip"));
            Assert.AreEqual(-1, PlayerEmotes.IndexOf(null));
            Assert.AreEqual(PlayerEmotes.Table.Length, PlayerEmotes.Table.Select(e => e.Name).Distinct().Count(), "one command per emote");
        }

        private AnimatorState State(string name)
        {
            AnimatorState state = upperBody.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
            Assert.IsNotNull(state, $"no '{name}' state on the Upper Body layer");
            return state;
        }
    }
}
