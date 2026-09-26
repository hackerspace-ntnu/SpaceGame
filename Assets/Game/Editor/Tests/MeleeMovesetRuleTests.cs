// An NPC fighter throws one move of a moveset per attack, and its damage waits for that move's
// contact frame on the deciding machine's own clock. Two rules keep that honest across machines
// and across clips, pinned here without a session: the move and its variant reach every watcher
// intact, and each variant's blow lands on its own contact frame — a kick and a jab in one
// action do not arrive at the same moment.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Agents;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class MeleeMovesetRuleTests
    {
        private const float ClipSeconds = 2f;

        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void DestroyMade()
        {
            foreach (Object o in made) Object.DestroyImmediate(o);
            made.Clear();
        }

        [Test]
        public void AMoveAndItsVariantSurviveTheWire()
        {
            foreach (int index in new[] { -1, 0, 1, 301, 50000 })
            {
                for (int variant = -1; variant < 5; variant++)
                {
                    (int i, int v) = CloseCombatModule.UnpackMove(CloseCombatModule.PackMove(index, variant));
                    Assert.AreEqual(index, i, "a watcher would throw a different move from the one the damage is timed to");
                    Assert.AreEqual(variant, v);
                }
            }

            (int fallback, int plain) = CloseCombatModule.UnpackMove(3);
            Assert.Less(fallback, 0, "a bare variant — all B carried before moves were sent — is the body's own attack action");
            Assert.AreEqual(3, plain);
        }

        [Test]
        public void EachVariantLandsOnItsOwnContactElseOnTheActions()
        {
            CharacterAction combo = Action(0.4f, 0f, 0.75f);
            Assert.AreEqual(0.8f, combo.SecondsTo(CharacterAction.Mark.Contact, 0, 1f), 1e-4f,
                            "a variant without a contact of its own keeps the action's mark, as every older asset does");
            Assert.AreEqual(1.5f, combo.SecondsTo(CharacterAction.Mark.Contact, 1, 1f), 1e-4f,
                            "a kick cut from another take lands later than the jab the action's mark was set for");
            Assert.AreEqual(0.75f, combo.SecondsTo(CharacterAction.Mark.Contact, 1, 2f), 1e-4f,
                            "a body playing the clip faster must be hit sooner");
            Assert.AreEqual(0f, combo.SecondsTo(CharacterAction.Mark.Release, 1, 1f),
                            "a contact time is not a release time");
        }

        [Test]
        public void AVariantsOwnContactNeedsNoActionMark()
        {
            CharacterAction unmarked = Action(null, 0.5f, 0f);
            Assert.AreEqual(1f, unmarked.SecondsTo(CharacterAction.Mark.Contact, 0, 1f), 1e-4f,
                            "every CMU move carries its contact per variant and no action-level mark");
            Assert.IsFalse(unmarked.TryGetMark(CharacterAction.Mark.Contact, 1, out _),
                           "a variant with no contact of either kind lands at once rather than at a guessed frame");
        }

        /// <summary>
        /// A one-shot whose variants each play a <see cref="ClipSeconds"/> clip, with an optional
        /// action-level Contact mark and, per variant, its own contact (0 = none).
        /// </summary>
        private CharacterAction Action(float? actionContact, params float[] variantContacts)
        {
            var action = ScriptableObject.CreateInstance<CharacterAction>();
            made.Add(action);

            var clip = new AnimationClip();
            made.Add(clip);
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, ClipSeconds, 1f));

            var so = new SerializedObject(action);
            SerializedProperty variants = so.FindProperty("variants");
            variants.arraySize = variantContacts.Length;
            for (int i = 0; i < variantContacts.Length; i++)
            {
                SerializedProperty variant = variants.GetArrayElementAtIndex(i);
                variant.FindPropertyRelative("clip").objectReferenceValue = clip;
                variant.FindPropertyRelative("contactAt").floatValue = variantContacts[i];
            }

            SerializedProperty marks = so.FindProperty("marks");
            marks.arraySize = actionContact.HasValue ? 1 : 0;
            if (actionContact.HasValue)
            {
                SerializedProperty mark = marks.GetArrayElementAtIndex(0);
                mark.FindPropertyRelative("mark").enumValueIndex = (int)CharacterAction.Mark.Contact;
                mark.FindPropertyRelative("normalizedTime").floatValue = actionContact.Value;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return action;
        }
    }
}
