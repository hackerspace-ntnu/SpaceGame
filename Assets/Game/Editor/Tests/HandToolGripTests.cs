// What the hand-tool roster promises about how a Raxy holds each tool, read off the BUILT assets
// rather than the roster, because a roster edit that was never followed by Build All changes
// nothing a player sees.
//
// In Editor/ because these touch Assembly-CSharp types, and an asmdef cannot reference it.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class HandToolGripTests
    {
        private static IEnumerable<HandToolSpec> Built() =>
            HandToolRoster.All.Where(s => AssetDatabase.LoadAssetAtPath<InventoryItem>(s.ItemPath) != null);

        private static ItemGrip.HoldStyle ExpectedPose(HandToolSpec spec) =>
            spec.PoseOverride ?? CarryStances.Of(spec.Stance).Pose;

        [Test]
        public void EveryBuiltTool_IsHeldInThePoseItsStanceNames()
        {
            foreach (HandToolSpec spec in Built())
            {
                var grip = AssetDatabase.LoadAssetAtPath<InventoryItem>(spec.ItemPath).itemPrefab.GetComponent<ItemGrip>();
                Assert.AreEqual(ExpectedPose(spec), grip.Style,
                    $"{spec.Id}: the built grip holds a different pose than {spec.Stance} says. Re-run Hand Tools > Build All.");
            }
        }

        [Test]
        public void OnlyToolsThatNeedBothHands_AreHeldWithBoth()
        {
            // The roster's single list of two-handed tools. Growing it is a decision, not a default.
            var bothHands = new[] { CarryStance.Push };
            var bothHandsByPose = new[] { "Tool_ElectricHarpoonGun" };

            foreach (HandToolSpec spec in HandToolRoster.All)
            {
                bool twoHanded = ExpectedPose(spec) == ItemGrip.HoldStyle.TwoHanded
                                 || ExpectedPose(spec) == ItemGrip.HoldStyle.Push;
                bool allowed = bothHands.Contains(spec.Stance) || bothHandsByPose.Contains(spec.Id);
                Assert.AreEqual(allowed, twoHanded,
                    $"{spec.Id} is {(twoHanded ? "" : "not ")}held with both hands but " +
                    $"{(allowed ? "needs" : "does not need")} to be.");
            }
        }

        [Test]
        public void CarriedTools_AreHiddenFromTheDeveloperBrowser()
        {
            foreach (HandToolSpec spec in Built())
                Assert.IsFalse(AssetDatabase.LoadAssetAtPath<InventoryItem>(spec.ItemPath).showInDevBrowser,
                    $"{spec.Id} would be listed in the artifact browser (O). Re-run Hand Tools > Build All.");
        }

        [Test]
        public void EveryHoldStyleAStanceUses_HasAPose()
        {
            var profile = AssetDatabase.LoadAssetAtPath<HumanoidAnimationProfile>(HumanoidControllerBuilder.ProfilePath);
            foreach (CarryStance stance in System.Enum.GetValues(typeof(CarryStance)))
                Assert.IsTrue(profile.HoldPoses.Any(p => p.style == CarryStances.Of(stance).Pose && p.clip != null),
                    $"{stance} needs a hold pose that the humanoid animation profile does not have.");
        }
    }
}
