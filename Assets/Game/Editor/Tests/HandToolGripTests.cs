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
        public void TheRaxyHandFrame_IsDerivedFromItsFingers_NotGuessedFromTheForearm()
        {
            // The Raxy has three fingers and a thumb. Without the ring-finger fallback it took the forearm path, whose
            // roll is read off the world's up at the moment of the call and whose origin sat 5 cm from the wrist,
            // 12 cm short of the fist: every tool lay along the wrist.
            using HandToolRig rig = HandToolRig.Create();
            Assert.AreEqual("finger bones", rig.Frame.Source, "the Raxy's grip frame fell back to the forearm path");
        }

        [Test]
        public void EveryBuiltTool_IsSeatedOnItsStance_AlongAndAboutItsLength_WithItsRootInTheFist()
        {
            using HandToolRig rig = HandToolRig.Create();
            foreach (GripAudit row in HandToolAudit.Measure(rig))
            {
                Assert.LessOrEqual(row.Residual.Along, HandToolAudit.MaxResidual,
                    $"{row.Id} points {row.Residual.Along:F1} degrees off its {row.Stance} stance. Re-run Hand Tools > Build All.");
                Assert.LessOrEqual(row.Residual.Face, HandToolAudit.MaxResidual,
                    $"{row.Id} is turned {row.Residual.Face:F1} degrees about its own length off its {row.Stance} stance. Re-run Build All.");
                Assert.LessOrEqual(row.FistToRoot, HandToolAudit.MaxFistToRoot,
                    $"{row.Id}'s root is {row.FistToRoot:F3} m from the middle of the closed fist. Re-run Build All.");
            }
        }

        [Test]
        public void NoToolIsHeldThroughTheFloor_OrByAHandInTheAir_OrWithAWristBentPastItsLimit()
        {
            string[] serious = { "THROUGH_FLOOR", "HAND_IN_AIR", "WRIST_TWISTED", "ROOT_AT_END" };

            using HandToolRig rig = HandToolRig.Create();
            var failures = HandToolAudit.Measure(rig)
                .Where(r => serious.Any(r.Flags.Contains))
                .Select(r => $"{r.Id} ({r.Stance}): {r.Flags}")
                .ToList();

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void AGripShift_SlidesTheToolAlongItsOwnLength()
        {
            using HandToolRig rig = HandToolRig.Create();
            var plain = new HandToolSpec { Id = "Probe", Stance = CarryStance.Wield, HoldSize = 0.5f };
            var shifted = new HandToolSpec { Id = "Probe", Stance = CarryStance.Wield, HoldSize = 0.5f, GripShift = 0.1f };

            GripFit from = GripFitter.Fit(rig, plain);
            GripFit to = GripFitter.Fit(rig, shifted);

            Assert.AreEqual(0.1f, (to.Position - from.Position).magnitude, 0.003f,
                "closing the hand 10 cm further up the tool must slide it 10 cm back in the palm");
            Assert.AreEqual(from.Rotation, to.Rotation, "a grip shift must not turn the tool");
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
