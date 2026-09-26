// The character actions and the catalog built from them, read off disk.
//
// An action is only playable if the controller was rebuilt after it was added, and the failure is
// an error at runtime on the first play rather than anything at edit time. These catch the three
// ways that happens: an action added but never built, content the builder would refuse, and a
// catalog that no longer matches the controller the wire ids index into.
using System.Linq;
using NUnit.Framework;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEditor.Animations;

namespace SpaceGame.EditorTools
{
    public class CharacterActionAssetTests
    {
        [Test]
        public void CatalogListsExactlyTheActionsFolderInPathOrder()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterActionCatalog>(HumanoidControllerBuilder.CatalogPath);
            Assert.IsNotNull(catalog, $"{HumanoidControllerBuilder.CatalogPath} is missing — run Rebuild Humanoid Controller");

            CollectionAssert.AreEqual(HumanoidControllerBuilder.CollectActions(), catalog.Actions,
                                      "an action was added, removed or renamed since the last Rebuild; the wire ids " +
                                      "and the controller's states are out of step with the folder");
        }

        [Test]
        public void EveryActionPassesTheBuildersContentCheck()
        {
            var profile = AssetDatabase.LoadAssetAtPath<HumanoidAnimationProfile>(HumanoidControllerBuilder.ProfilePath);
            Assert.IsNotNull(profile, $"{HumanoidControllerBuilder.ProfilePath} is missing");

            var problems = HumanoidContentCheck.Problems(profile, HumanoidControllerBuilder.CollectActions());
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void EveryActionVariantHasItsStateOnItsSlotLayer()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(HumanoidControllerBuilder.ControllerPath);

            foreach (CharacterAction action in HumanoidControllerBuilder.CollectActions())
            {
                AnimatorStateMachine sm = controller.layers
                    .First(l => l.name == HumanoidLayers.ForSlot(action.BodySlot)).stateMachine;
                string[] states = sm.states.Select(s => s.state.name).ToArray();

                for (int v = 0; v < action.VariantCount; v++)
                {
                    Assert.Contains(HumanoidLayers.StateName(action, v, HumanoidLayers.Stage.Main), states,
                                    $"'{action.name}' variant {v} has no state — rebuild the controller");
                    if (action.Mode != CharacterAction.Playback.EnterLoopExit) continue;

                    Assert.Contains(HumanoidLayers.StateName(action, v, HumanoidLayers.Stage.Enter), states);
                    Assert.Contains(HumanoidLayers.StateName(action, v, HumanoidLayers.Stage.Exit), states);
                }
            }
        }
    }
}
