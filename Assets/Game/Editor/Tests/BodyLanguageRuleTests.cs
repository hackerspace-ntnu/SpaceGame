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

        private CharacterAction Action(CharacterAction.Slot slot, CharacterAction.Playback playback, BodyPosture postures = 0)
        {
            var action = ScriptableObject.CreateInstance<CharacterAction>();
            made.Add(action);

            var so = new SerializedObject(action);
            so.FindProperty("slot").intValue = (int)slot;
            so.FindProperty("playback").intValue = (int)playback;
            so.FindProperty("postures").intValue = (int)postures;
            so.FindProperty("variants").arraySize = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            return action;
        }
    }
}
