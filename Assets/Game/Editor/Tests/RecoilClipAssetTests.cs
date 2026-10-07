// The generated recoil clips and their actions, read off disk.
//
// They are output of Tools/SpaceGame/Animation/Generate Recoil Clips, so what these pin is that the
// output is current and shaped the way the additive layer needs: a profile edited without
// regenerating, or a clip touched by hand, fails here instead of kicking wrongly in play.
using System.Linq;
using NUnit.Framework;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class RecoilClipAssetTests
    {
        private const float Tolerance = 1e-5f;

        private static RecoilProfile Profile
        {
            get
            {
                var profile = AssetDatabase.LoadAssetAtPath<RecoilProfile>(RecoilClipGenerator.ProfilePath);
                Assert.IsNotNull(profile, $"{RecoilClipGenerator.ProfilePath} is missing");
                return profile;
            }
        }

        [Test]
        public void EveryRecoilClipOnDiskIsWhatTheProfileGenerates()
        {
            RecoilProfile profile = Profile;
            foreach (RecoilProfile.Recoil recoil in profile.Recoils)
            {
                var onDisk = AssetDatabase.LoadAssetAtPath<AnimationClip>(RecoilClipGenerator.ClipPath(recoil));
                Assert.IsNotNull(onDisk, $"'{recoil.actionName}' has no clip — run Generate Recoil Clips");

                AnimationClip fresh = RecoilClipGenerator.Build(recoil, profile.FrameRate);
                try
                {
                    AssertSameCurves(fresh, onDisk, recoil.actionName);

                    AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(onDisk);
                    Assert.IsTrue(settings.hasAdditiveReferencePose && settings.additiveReferencePoseTime == 0f
                                  && settings.additiveReferencePoseClip == null,
                                  $"'{onDisk.name}' must add its motion relative to its own first frame");
                    Assert.IsFalse(settings.loopTime, $"'{onDisk.name}' is a one-shot kick");
                }
                finally
                {
                    Object.DestroyImmediate(fresh);
                }
            }
        }

        [Test]
        public void EveryRecoilActionIsAnAdditiveOneShotOfItsClipAnsweringShoot()
        {
            var shoot = AssetDatabase.LoadAssetAtPath<CharacterCue>(RecoilClipGenerator.CuePath);
            foreach (RecoilProfile.Recoil recoil in Profile.Recoils)
            {
                var action = AssetDatabase.LoadAssetAtPath<CharacterAction>(RecoilClipGenerator.ActionPath(recoil));
                Assert.IsNotNull(action, $"'{recoil.actionName}' has no action — run Generate Recoil Clips");

                Assert.AreEqual(CharacterAction.Slot.Additive, action.BodySlot,
                                $"'{action.name}' on an overriding slot would snap an aimed arm level on every shot");
                Assert.AreEqual(CharacterAction.Playback.OneShot, action.Mode);
                Assert.AreEqual(1, action.VariantCount);
                Assert.AreSame(AssetDatabase.LoadAssetAtPath<AnimationClip>(RecoilClipGenerator.ClipPath(recoil)),
                               action.GetVariant(0).clip, $"'{action.name}' must play its generated clip");
                CollectionAssert.Contains(action.Cues.ToArray(), shoot, $"'{action.name}' answers the shoot cue");
            }
        }

        private static void AssertSameCurves(AnimationClip expected, AnimationClip actual, string recoil)
        {
            EditorCurveBinding[] want = AnimationUtility.GetCurveBindings(expected);
            EditorCurveBinding[] have = AnimationUtility.GetCurveBindings(actual);
            CollectionAssert.AreEquivalent(want.Select(b => b.propertyName), have.Select(b => b.propertyName),
                                           $"'{recoil}' animates other muscles than the profile names — regenerate");

            foreach (EditorCurveBinding binding in want)
            {
                Keyframe[] a = AnimationUtility.GetEditorCurve(expected, binding).keys;
                Keyframe[] b = AnimationUtility.GetEditorCurve(actual, binding).keys;
                Assert.AreEqual(a.Length, b.Length, $"'{recoil}' {binding.propertyName} — regenerate");
                for (int i = 0; i < a.Length; i++)
                {
                    string where = $"'{recoil}' {binding.propertyName} key {i} — regenerate";
                    Assert.AreEqual(a[i].time, b[i].time, Tolerance, where);
                    Assert.AreEqual(a[i].value, b[i].value, Tolerance, where);
                    Assert.AreEqual(a[i].inTangent, b[i].inTangent, Tolerance, where);
                    Assert.AreEqual(a[i].outTangent, b[i].outTangent, Tolerance, where);
                }
            }
        }
    }
}
