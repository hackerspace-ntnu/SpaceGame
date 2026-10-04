// CharacterActions on a real humanoid prefab, with the Animator stepped by hand.
//
// Everything about an action that can fail does so silently — a state name the builder spelled one
// way and the runtime another, a layer left at weight 1 over a pose it no longer plays, a loop that
// restarts every time gameplay re-asserts it. These drive the real controller on a real drifter,
// frame by frame, and read back what the Animator actually did.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SpaceGame.Items;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools
{
    public class CharacterActionsPlaybackTests
    {
        private const string BodyPath = "Assets/Game/Prefabs/agents/Characters/Drifters/Drifter_Human.prefab";
        private const float Step = 0.05f;

        private Scene scene;
        private CharacterActions actions;
        private Animator animator;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            var body = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadMainAssetAtPath(BodyPath), scene);
            actions = body.GetComponent<CharacterActions>();
            animator = body.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(actions, $"{BodyPath} has no CharacterActions — run Wire Humanoid Prefabs");

            // No Awake is raised, deliberately: CharacterActions must work for a caller that reaches
            // it first — another module's OnEnable during Instantiate did, on every NPC spawn. Edit
            // mode raises no LateUpdate either; Advance does.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0f);
        }

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

        [Test]
        public void ABodyAskedBeforeItsOwnAwakeAnswersRatherThanThrows()
        {
            CharacterAction aim = Action("Aim Rifle");

            Assert.DoesNotThrow(() => actions.Stop(aim),
                "AggressionTelegraphModule.OnEnable stops the drawn pose during Instantiate, before this " +
                "component's Awake when it is listed below the telegraph");
            Assert.IsFalse(actions.IsPlaying(aim));
        }

        [Test]
        public void AOneShotPlaysOnItsLayerAndHandsTheLayerBack()
        {
            CharacterAction wave = Action("Wave");
            int layer = animator.GetLayerIndex(HumanoidLayers.ForSlot(wave.BodySlot));

            Assert.IsTrue(actions.Play(wave), "a built action on a wired body must start");
            Advance(0.3f);
            Assert.AreEqual(1f, animator.GetLayerWeight(layer), "the layer must be up while the action plays");
            Assert.AreEqual(Animator.StringToHash(HumanoidLayers.StateName(wave, 0, HumanoidLayers.Stage.Main)),
                            animator.GetCurrentAnimatorStateInfo(layer).shortNameHash,
                            "the builder and the runtime must name the state the same way");

            Advance(4f);
            Assert.AreEqual(Animator.StringToHash(HumanoidLayers.EmptyState),
                            animator.GetCurrentAnimatorStateInfo(layer).shortNameHash, "a one-shot hands back to Empty on its own");
            Assert.AreEqual(0f, animator.GetLayerWeight(layer), "a layer resting in Empty must drop to weight 0");
            Assert.IsFalse(actions.IsPlaying(wave));
        }

        [Test]
        public void ALoopAskedForAgainKeepsPlayingAndStopsWhenTold()
        {
            CharacterAction talk = Action("Talk");
            int layer = animator.GetLayerIndex(HumanoidLayers.ForSlot(talk.BodySlot));

            actions.Play(talk);
            Advance(1f);
            float before = animator.GetCurrentAnimatorStateInfo(layer).normalizedTime;

            Assert.IsTrue(actions.Play(talk), "re-asserting a running loop is accepted");
            Advance(Step);
            Assert.Greater(animator.GetCurrentAnimatorStateInfo(layer).normalizedTime, before,
                           "gameplay re-asserts a loop every frame; restarting it each time would freeze it on frame one");

            actions.Stop(talk);
            Advance(1f);
            Assert.AreEqual(0f, animator.GetLayerWeight(layer), "a stopped loop gives the body back");
        }

        [Test]
        public void AnArmActionForTheOtherArmPlaysMirrored()
        {
            CharacterAction punch = Action("Punch");

            actions.Play(punch, ItemGrip.Hand.Left);
            Assert.IsTrue(animator.GetBool(HumanoidParams.ActionMirror(punch.BodySlot)),
                          "a puncher on the left wrist must not throw the right arm");

            actions.Play(punch, ItemGrip.Hand.Right);
            Assert.IsFalse(animator.GetBool(HumanoidParams.ActionMirror(punch.BodySlot)));
        }

        [Test]
        public void ARecoilKicksOverTheAimWithoutStoppingIt()
        {
            CharacterAction aim = Action("Aim Pistol");
            CharacterAction recoil = Action("Pistol Recoil");
            int additive = animator.GetLayerIndex(HumanoidLayers.ForSlot(recoil.BodySlot));

            actions.Play(aim);
            Advance(0.3f);
            Assert.IsTrue(actions.Play(recoil), "a built recoil on a wired body must start");
            Advance(Step);
            Assert.IsTrue(actions.IsPlaying(aim), "the recoil adds to the aim; a shot that drops the aim is the old level-only snap");
            Assert.AreEqual(1f, animator.GetLayerWeight(additive), "the additive layer must be up while the kick plays");

            Advance(1f);
            Assert.IsTrue(actions.IsPlaying(aim), "the aim outlasts the kick");
            Assert.AreEqual(0f, animator.GetLayerWeight(additive), "the additive layer lets go once the kick has settled");
        }

        [Test]
        public void AFullBodyActionLeavesARunningRecoilAlone()
        {
            CharacterAction recoil = Action("Pistol Recoil");

            actions.Play(recoil);
            Advance(Step);
            actions.Play(Action("Kneel"));
            Assert.IsTrue(actions.IsPlaying(recoil),
                          "a full-body action stops the overriding slots only; an additive kick adds to it");
        }

        [Test]
        public void EveryVariantOfEveryActionEntersItsFirstStateOnARealBody()
        {
            var failures = new List<string>();
            foreach (CharacterAction action in HumanoidControllerBuilder.CollectActions())
            {
                int layer = animator.GetLayerIndex(HumanoidLayers.ForSlot(action.BodySlot));
                var first = action.Mode == CharacterAction.Playback.EnterLoopExit
                    ? HumanoidLayers.Stage.Enter
                    : HumanoidLayers.Stage.Main;

                for (int variant = 0; variant < action.VariantCount; variant++)
                {
                    CharacterAction.Variant v = action.GetVariant(variant);
                    AnimationClip played = first == HumanoidLayers.Stage.Enter ? v.enter : v.clip;

                    // The Animator reports the state being left until the crossfade ends, and a clip
                    // shorter than its own fade has already handed the layer back by then.
                    float wait = action.FadeIn + 0.1f;
                    if (played == null || played.length < wait + 0.1f) continue;

                    if (!actions.Play(action, null, variant))
                    {
                        failures.Add($"{action.name} variant {variant} would not start");
                        continue;
                    }

                    Advance(wait);
                    int expected = Animator.StringToHash(HumanoidLayers.StateName(action, variant, first));
                    if (animator.GetCurrentAnimatorStateInfo(layer).shortNameHash != expected)
                        failures.Add($"{action.name} variant {variant} did not reach its '{first}' state");

                    actions.Stop(action);
                    Advance(0.5f);
                }
            }

            Assert.IsEmpty(failures, "actions that never play on a wired body:\n  " + string.Join("\n  ", failures));
        }

        private void Advance(float seconds)
        {
            for (float t = 0f; t < seconds; t += Step)
            {
                animator.Update(Step);
                Call("LateUpdate");
            }
        }

        private void Call(string method) =>
            typeof(CharacterActions).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                                    .Invoke(actions, null);

        private static CharacterAction Action(string name)
        {
            Assert.IsTrue(CharacterActionCatalog.Default.TryFind(name, out CharacterAction action),
                          $"no action named '{name}' in the catalog");
            return action;
        }
    }
}
