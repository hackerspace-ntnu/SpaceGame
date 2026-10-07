// How BodyLanguage picks an action for a cue: the rules that keep a full-body clip off a walking
// body and off the player, a loop out of a one-shot reaction, and a crowd from repeating itself.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class BodyLanguageRuleTests
    {
        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void DestroyMade()
        {
            foreach (Object o in made) Object.DestroyImmediate(o);
            made.Clear();
        }

        [Test]
        public void AFullBodyActionOnlyFitsAStillBodyThatAllowsIt()
        {
            CharacterAction bow = Action(CharacterAction.Slot.Full, CharacterAction.Playback.OneShot);
            CharacterAction wave = Action(CharacterAction.Slot.Upper, CharacterAction.Playback.OneShot);
            var both = new[] { bow, wave };

            Assert.AreSame(wave, BodyLanguage.Choose(both, BodyPosture.Moving, true, false, null, new System.Random(1)),
                           "a bow on the move would freeze the legs mid-stride");
            Assert.AreSame(wave, BodyLanguage.Choose(both, BodyPosture.Seated, true, false, null, new System.Random(1)));
            Assert.AreSame(wave, BodyLanguage.Choose(new[] { bow, wave }, BodyPosture.Standing, false, false, null, new System.Random(1)),
                           "the player's legs are its input's: no full-body pick, standing or not");
            Assert.IsNull(BodyLanguage.Choose(new[] { bow }, BodyPosture.Moving, true, false, null, new System.Random(1)));

            CharacterAction seatedTalk = Action(CharacterAction.Slot.Full, CharacterAction.Playback.Loop, BodyPosture.Seated);
            Assert.AreSame(seatedTalk, BodyLanguage.Choose(new[] { seatedTalk }, BodyPosture.Seated, true, true, null, new System.Random(1)),
                           "an action that names its postures overrides the by-slot default");
            Assert.IsNull(BodyLanguage.Choose(new[] { seatedTalk }, BodyPosture.Standing, true, true, null, new System.Random(1)));
        }

        [Test]
        public void ReactionsPickOneShotsAndHeldStatesPickLoops()
        {
            CharacterAction flinch = Action(CharacterAction.Slot.Upper, CharacterAction.Playback.OneShot);
            CharacterAction talking = Action(CharacterAction.Slot.Upper, CharacterAction.Playback.Loop);
            var both = new[] { flinch, talking };

            for (int seed = 0; seed < 20; seed++)
            {
                Assert.AreSame(flinch, BodyLanguage.Choose(both, BodyPosture.Standing, true, false, null, new System.Random(seed)),
                               "a loop picked by a one-off reaction would never be stopped");
                Assert.AreSame(talking, BodyLanguage.Choose(both, BodyPosture.Standing, true, true, null, new System.Random(seed)));
            }
        }

        [Test]
        public void TheLastPickIsAvoidedWhileAnotherFits()
        {
            CharacterAction a = Action(CharacterAction.Slot.Upper, CharacterAction.Playback.OneShot);
            CharacterAction b = Action(CharacterAction.Slot.Upper, CharacterAction.Playback.OneShot);

            for (int seed = 0; seed < 20; seed++)
                Assert.AreSame(b, BodyLanguage.Choose(new[] { a, b }, BodyPosture.Standing, true, false, a, new System.Random(seed)),
                               "the same gesture twice in a row is the tell of a one-clip character");

            Assert.AreSame(a, BodyLanguage.Choose(new[] { a }, BodyPosture.Standing, true, false, a, new System.Random(0)),
                           "with nothing else tagged, repeating beats showing nothing");
        }

        [Test]
        public void AnOccupiedArmBlocksTheActionsThatMoveIt()
        {
            CharacterAction bow = Action(CharacterAction.Slot.Upper, CharacterAction.Playback.OneShot);
            CharacterAction leftWave = Action(CharacterAction.Slot.LeftArm, CharacterAction.Playback.OneShot);
            CharacterAction rightWave = Action(CharacterAction.Slot.RightArm, CharacterAction.Playback.OneShot);
            CharacterAction recoil = Action(CharacterAction.Slot.Additive, CharacterAction.Playback.OneShot);
            var all = new[] { bow, leftWave, rightWave, recoil };

            for (int seed = 0; seed < 20; seed++)
            {
                var roll = new System.Random(seed);
                CharacterAction one = BodyLanguage.Choose(all, BodyPosture.Standing, true, false, null, roll, BodyArms.Right);
                Assert.That(one, Is.SameAs(leftWave).Or.SameAs(recoil), "a hammer in the right hand rules out every clip that moves that arm");
                Assert.IsNull(BodyLanguage.Choose(new[] { bow, rightWave }, BodyPosture.Standing, true, false, null, roll, BodyArms.Right));
                Assert.That(BodyLanguage.Choose(new[] { bow, rightWave }, BodyPosture.Standing, true, false, null, roll),
                            Is.SameAs(bow).Or.SameAs(rightWave), "empty hands: nothing is ruled out");
            }
            Assert.IsNull(BodyLanguage.Choose(new[] { bow, leftWave, rightWave }, BodyPosture.Standing, true, false, null,
                                              new System.Random(0), BodyArms.Both));
        }

        [Test]
        public void ATool_DrawnMidGesture_DropsOnlyTheGesturesOverTheArmsItOccupies()
        {
            CharacterCue gesture = Cue(needsFreeHands: true);
            CharacterCue doneWithTheItem = Cue(needsFreeHands: false);
            CharacterAction stretch = Action(CharacterAction.Slot.Full, CharacterAction.Playback.OneShot, cue: gesture);
            CharacterAction rightWave = Action(CharacterAction.Slot.RightArm, CharacterAction.Playback.OneShot, cue: gesture);
            CharacterAction leftWave = Action(CharacterAction.Slot.LeftArm, CharacterAction.Playback.OneShot, cue: gesture);
            CharacterAction strike = Action(CharacterAction.Slot.Upper, CharacterAction.Playback.OneShot, cue: doneWithTheItem);
            CharacterAction talkLoop = Action(CharacterAction.Slot.Upper, CharacterAction.Playback.Loop, cue: gesture);

            Assert.IsTrue(BodyLanguage.IsGestureOver(stretch, BodyArms.Right), "a full-body stretch moves the arm holding the basket");
            Assert.IsTrue(BodyLanguage.IsGestureOver(rightWave, BodyArms.Right));
            Assert.IsFalse(BodyLanguage.IsGestureOver(leftWave, BodyArms.Right), "the free arm may finish its wave");
            Assert.IsFalse(BodyLanguage.IsGestureOver(strike, BodyArms.Both), "an action done WITH the item is not a gesture");
            Assert.IsFalse(BodyLanguage.IsGestureOver(talkLoop, BodyArms.Both), "a loop is its owner's to hold or release");
            Assert.IsFalse(BodyLanguage.IsGestureOver(stretch, BodyArms.None), "nothing in the hand, nothing to drop");
            Assert.IsFalse(BodyLanguage.IsGestureOver(null, BodyArms.Both));
        }

        private CharacterCue Cue(bool needsFreeHands)
        {
            var cue = ScriptableObject.CreateInstance<CharacterCue>();
            made.Add(cue);

            var so = new SerializedObject(cue);
            so.FindProperty("needsFreeHands").boolValue = needsFreeHands;
            so.ApplyModifiedPropertiesWithoutUndo();
            return cue;
        }

        private CharacterAction Action(CharacterAction.Slot slot, CharacterAction.Playback playback, BodyPosture postures = 0, CharacterCue cue = null)
        {
            var action = ScriptableObject.CreateInstance<CharacterAction>();
            made.Add(action);

            var so = new SerializedObject(action);
            so.FindProperty("slot").intValue = (int)slot;
            so.FindProperty("playback").intValue = (int)playback;
            so.FindProperty("postures").intValue = (int)postures;
            so.FindProperty("variants").arraySize = 1;
            if (cue != null)
            {
                so.FindProperty("cues").arraySize = 1;
                so.FindProperty("cues").GetArrayElementAtIndex(0).objectReferenceValue = cue;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return action;
        }
    }
}
