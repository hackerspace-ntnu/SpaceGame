// The body-language data holds together: every moment code raises has a reaction, every reaction
// can actually play something, and the talking loop exists for every posture a speaker is in.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class BodyLanguageAssetTests
    {
        [Test]
        public void EveryMomentHasAReactionThatCanPlay()
        {
            var table = AssetDatabase.LoadAssetAtPath<MomentReactions>(HumanoidControllerBuilder.ReactionsPath);
            Assert.IsNotNull(table, "the default reaction table is missing");
            List<CharacterAction> actions = HumanoidControllerBuilder.CollectActions();

            foreach (CharacterMoment moment in System.Enum.GetValues(typeof(CharacterMoment)))
            {
                if (moment == CharacterMoment.None) continue;

                MomentReactions.Row row = table.Find(moment);
                Assert.IsNotNull(row, $"{moment} is raised by code but the default table has no row for it");
                if (row.action != null) continue;

                Assert.IsTrue(Answered(row.cue, actions, a => a.Mode == CharacterAction.Playback.OneShot),
                              $"{moment} asks for '{(row.cue != null ? row.cue.name : "nothing")}', and no one-shot " +
                              "action is tagged with it or its fallbacks, so the moment shows nothing");
            }
        }

        [Test]
        public void CueFallbacksEndAndEveryCueSaysWhatItMeans()
        {
            foreach (CharacterCue cue in HumanoidControllerBuilder.CollectCues())
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(cue.Meaning), $"cue '{cue.name}' has no meaning to show in the library");

                var seen = new HashSet<CharacterCue>();
                for (CharacterCue step = cue; step != null; step = step.Fallback)
                    Assert.IsTrue(seen.Add(step), $"cue '{cue.name}' falls back in a circle");
                Assert.LessOrEqual(seen.Count, BodyLanguage.MaxFallbacks + 1,
                                   $"cue '{cue.name}' has fallbacks past the depth BodyLanguage follows");
            }
        }

        [Test]
        public void GestureCuesNeedFreeHandsAndWorkCuesDoNot()
        {
            string[] gestures = { "greet", "farewell", "gesture", "fidget", "talk", "point", "bow" };
            string[] withTheItem = { "work", "hammer", "farm", "mine", "cook", "carry", "attack", "give", "pickup" };
            CharacterCue[] cues = HumanoidControllerBuilder.CollectCues().ToArray();

            foreach (string word in gestures)
                Assert.IsTrue(cues.First(c => c.name == word).NeedsFreeHands, $"'{word}' plays over a held item, so the item waves");
            foreach (string word in withTheItem)
                Assert.IsFalse(cues.First(c => c.name == word).NeedsFreeHands, $"'{word}' is done WITH the item");
        }

        [Test]
        public void TheTalkingLoopFitsAStandingAndASeatedSpeaker()
        {
            CharacterCue talk = HumanoidControllerBuilder.CollectCues().FirstOrDefault(c => c.name == CharacterActionWiring.TalkingCue);
            Assert.IsNotNull(talk, $"no '{CharacterActionWiring.TalkingCue}' cue for SpeechGestures to hold");

            List<CharacterAction> actions = HumanoidControllerBuilder.CollectActions();
            foreach (BodyPosture posture in new[] { BodyPosture.Standing, BodyPosture.Moving, BodyPosture.Seated })
            {
                Assert.IsTrue(actions.Any(a => a.Cues.Contains(talk) && a.Loops && a.Fits(posture)),
                              $"an NPC talking while {posture} has no talking loop and stands stiff through its line");
            }
        }

        [Test]
        public void ASeatedBodyGesturesWithItsArmsOnlyWhileAStandingOneMayBowOrStretch()
        {
            var host = new GameObject("sitter");
            try
            {
                host.AddComponent<CharacterActions>();
                var body = host.AddComponent<BodyLanguage>();
                CharacterCue greet = HumanoidControllerBuilder.CollectCues().First(c => c.name == "greet");

                bool standingTookTheWholeBody = false;
                for (int i = 0; i < 60; i++)
                    standingTookTheWholeBody |= body.Pick(greet).BodySlot == CharacterAction.Slot.Full;
                Assert.IsTrue(standingTookTheWholeBody, "a standing body is greeted with a bow or a curtsey, or this test proves nothing");

                body.SitOn(true);
                for (int i = 0; i < 60; i++)
                    Assert.AreNotEqual(CharacterAction.Slot.Full, body.Pick(greet).BodySlot,
                                       "a greeting that stands a resident up out of its seat for a second");

                body.SitOn(false);
                standingTookTheWholeBody = false;
                for (int i = 0; i < 60; i++)
                    standingTookTheWholeBody |= body.Pick(greet).BodySlot == CharacterAction.Slot.Full;
                Assert.IsTrue(standingTookTheWholeBody, "standing up gives the whole body back");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static bool Answered(CharacterCue cue, List<CharacterAction> actions, System.Func<CharacterAction, bool> usable)
        {
            for (int depth = 0; cue != null && depth <= BodyLanguage.MaxFallbacks; depth++, cue = cue.Fallback)
                if (actions.Any(a => a.Cues.Contains(cue) && usable(a))) return true;
            return false;
        }
    }
}
