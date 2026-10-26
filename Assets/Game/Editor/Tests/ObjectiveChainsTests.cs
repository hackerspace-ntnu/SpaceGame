// The quest window that characters' lines and gated scenery follow — from before anybody took the quest on,
// through its steps, to its end — and the per-step marks a quest step reads (QuestStepMemory). The save and replication contract
// for several quests is QuestTests'.
//
// EditMode, so Awake never runs on its own: the director reads its quests there, and it is called by hand.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.EditorTools
{
    public class ObjectiveChainsTests
    {
        private readonly List<Object> made = new();
        private ObjectiveDirector director;
        private ObjectiveChain side;

        [SetUp]
        public void SetUp()
        {
            ObjectiveChain story = Chain("crash-site", "land", "repair");
            side = Chain("sprite-hamlet", "find-hut", "report-hut", "escort");

            var host = new GameObject("Director");
            made.Add(host);
            director = host.AddComponent<ObjectiveDirector>();

            var fields = new UnityEditor.SerializedObject(director);
            fields.FindProperty("chain").objectReferenceValue = story;
            var quests = fields.FindProperty("quests");
            quests.arraySize = 1;
            quests.GetArrayElementAtIndex(0).objectReferenceValue = side;
            fields.ApplyModifiedPropertiesWithoutUndo();

            typeof(ObjectiveDirector).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                                     .Invoke(director, null);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made)
                if (o != null) Object.DestroyImmediate(o);
            made.Clear();

            // Nothing runs OnDestroy in EditMode, so the director's Instance is let go by hand.
            typeof(ObjectiveDirector).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic)
                                     .Invoke(null, null);
        }

        private ObjectiveChain Chain(string id, params string[] stepIds)
        {
            var chain = ScriptableObject.CreateInstance<ObjectiveChain>();
            made.Add(chain);

            var fields = new UnityEditor.SerializedObject(chain);
            fields.FindProperty("id").stringValue = id;
            var steps = fields.FindProperty("steps");
            steps.arraySize = stepIds.Length;
            for (int i = 0; i < stepIds.Length; i++)
            {
                var step = ScriptableObject.CreateInstance<RepairShipStep>();
                made.Add(step);
                var stepFields = new UnityEditor.SerializedObject(step);
                stepFields.FindProperty("id").stringValue = stepIds[i];
                stepFields.ApplyModifiedPropertiesWithoutUndo();
                steps.GetArrayElementAtIndex(i).objectReferenceValue = step;
            }
            fields.ApplyModifiedPropertiesWithoutUndo();
            return chain;
        }

        /// <summary>Puts the side quest on <paramref name="stepId"/>, finished, or — with neither — not taken on.</summary>
        private void PutSide(string stepId = null, bool complete = false)
        {
            var saved = new List<SavedQuest>();
            if (stepId != null || complete)
                saved.Add(new SavedQuest { Id = side.Id, Order = 1, Step = stepId, Complete = complete });

            director.Restore(saved);
        }

        private QuestStepWindow Window(string from, string until) =>
            new QuestStepWindow { chain = side, from = from, until = until };

        [Test]
        public void Window_FollowsTheQuestFromTakingItOnToCompletion()
        {
            QuestStepWindow middle = Window("find-hut", "escort");
            QuestStepWindow beforeTakingOn = Window("", "find-hut");
            QuestStepWindow afterFinishing = Window(QuestStepWindow.CompleteToken, "");

            PutSide();
            Assert.That(middle.IsInside, Is.False, "nobody has taken the quest on yet");
            Assert.That(beforeTakingOn.IsInside, Is.True);

            PutSide("report-hut");
            Assert.That(middle.IsInside, Is.True);
            Assert.That(beforeTakingOn.IsInside, Is.False);

            PutSide("escort");
            Assert.That(middle.IsInside, Is.False, "until is the first step after the window");

            PutSide(complete: true);
            Assert.That(afterFinishing.IsInside, Is.True);
        }

        [Test]
        public void Window_WithoutADirectorIsNowhere()
        {
            typeof(ObjectiveDirector).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic)
                                     .Invoke(null, null);

            Assert.That(Window("", "").TryGetPosition(out _), Is.False);
            Assert.That(Window("", "").IsInside, Is.False);
        }

        [Test]
        public void Window_WithAMisspeltStepIsOutsideAndSaysSo()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("step 'find-hutt'"));
            PutSide("find-hut");

            Assert.That(Window("find-hutt", "").IsInside, Is.False);
        }

        [Test]
        public void QuestMarks_CountOnlyForTheStepTheyLandIn()
        {
            PutSide("find-hut");
            Quest quest = director.Find(side);

            QuestMarks.Raise(QuestMark.Talked, "hamlet-elder");
            Assert.That(quest.World.HasMark(QuestMark.Talked, "hamlet-elder"), Is.True);
            Assert.That(quest.World.HasMark(QuestMark.Dead, "hamlet-elder"), Is.False);

            quest.Restore(new ObjectiveProgress { Step = 1 });

            Assert.That(quest.World.HasMark(QuestMark.Talked, "hamlet-elder"), Is.False,
                        "talking to the elder before the step that asks for it must not finish that step");
        }
    }
}
