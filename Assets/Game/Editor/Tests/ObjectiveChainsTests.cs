// The objective director's save contract with more than one chain: a side chain's record round-trips
// by chain id, a save from before side chains still restores the main chain, and whatever a save
// leaves out — a side chain nobody had accepted, a chain the world no longer has — comes back as a
// fresh start rather than as somebody else's progress.
//
// Plus the quest window that characters' lines and gated scenery follow, and the per-step marks a
// quest step reads.
//
// EditMode, so Awake never runs on its own: the director builds its runs there, and it is called by
// hand.
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTools
{
    public class ObjectiveChainsTests
    {
        private readonly List<Object> made = new();
        private ObjectiveDirector director;
        private ObjectiveChain main, side;

        [SetUp]
        public void SetUp()
        {
            main = Chain("crash-site", "land", "repair");
            side = Chain("sprite-hamlet", "find-hut", "report-hut", "escort");

            var host = new GameObject("Director");
            made.Add(host);
            director = host.AddComponent<ObjectiveDirector>();

            var fields = new UnityEditor.SerializedObject(director);
            fields.FindProperty("chain").objectReferenceValue = main;
            var sides = fields.FindProperty("sideChains");
            sides.arraySize = 1;
            sides.GetArrayElementAtIndex(0).objectReferenceValue = side;
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

        private ObjectiveRun Main => director.Runs[0];
        private ObjectiveRun Side => director.Runs[1];

        /// <summary>The record as the save file would hold it, read back the way a load reads it.</summary>
        private static ObjectiveSaveable.State ThroughJson(object record) =>
            JObject.FromObject(record, SaveSerializer.Serializer).ToObject<ObjectiveSaveable.State>(SaveSerializer.Serializer);

        private object Capture()
        {
            var host = new GameObject("Saver");
            made.Add(host);
            var saver = host.AddComponent<ObjectiveSaveable>();
            var fields = new UnityEditor.SerializedObject(saver);
            fields.FindProperty("director").objectReferenceValue = director;
            fields.ApplyModifiedPropertiesWithoutUndo();
            return saver.CaptureState();
        }

        [Test]
        public void Runs_MainChainIsUnderWayAndSideChainWaits()
        {
            Assert.That(director.Runs.Count, Is.EqualTo(2));
            Assert.That(Main.IsActive, Is.True, "the main chain is under way from the landing");
            Assert.That(Side.IsStarted, Is.False, "a side chain waits until somebody accepts it");
            Assert.That(Side.Current, Is.Null);
            Assert.That(director.Focused, Is.SameAs(Main));
        }

        [Test]
        public void LegacySave_RestoresTheMainChainAndLeavesSideChainsUnstarted()
        {
            var legacy = JObject.Parse("{ \"step\": \"repair\", \"complete\": false, \"begun\": true }")
                                .ToObject<ObjectiveSaveable.State>(SaveSerializer.Serializer);

            director.Restore(ObjectiveSaveable.ToProgress(director, legacy));

            Assert.That(Main.Progress.Step, Is.EqualTo(1));
            Assert.That(Main.Progress.Begun, Is.True);
            Assert.That(Side.IsStarted, Is.False);
        }

        [Test]
        public void SideChain_RoundTripsThroughTheSaveByChainId()
        {
            director.Restore(new Dictionary<string, ObjectiveProgress>
            {
                ["crash-site"] = new ObjectiveProgress { Step = 2 },
                ["sprite-hamlet"] = new ObjectiveProgress { Step = 1, Begun = true, Started = true },
            });

            ObjectiveSaveable.State read = ThroughJson(Capture());
            director.Restore(new Dictionary<string, ObjectiveProgress>());
            Assert.That(Side.IsStarted, Is.False, "an empty record set resets every run");

            director.Restore(ObjectiveSaveable.ToProgress(director, read));

            Assert.That(Main.IsComplete, Is.True);
            Assert.That(Side.IsStarted, Is.True);
            Assert.That(Side.CurrentStepId, Is.EqualTo("report-hut"));
            Assert.That(Side.Progress.Begun, Is.True);
        }

        [Test]
        public void Capture_LeavesOutSideChainsNobodyAccepted()
        {
            ObjectiveSaveable.State read = ThroughJson(Capture());

            Assert.That(read.chains, Has.Count.EqualTo(1));
            Assert.That(read.chains[0].chain, Is.EqualTo("crash-site"));
        }

        [Test]
        public void UnknownChain_IsDroppedLoudlyAndTouchesNoRun()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("chain 'retired-quest'"));

            director.Restore(new Dictionary<string, ObjectiveProgress>
            {
                ["retired-quest"] = new ObjectiveProgress { Step = 3, Started = true },
            });

            Assert.That(Main.Progress.Step, Is.EqualTo(0));
            Assert.That(Side.IsStarted, Is.False);
        }

        [Test]
        public void UnknownStep_RestartsTheChainButKeepsItAccepted()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("step 'renamed-step'"));

            ObjectiveProgress resolved = ObjectiveDirector.Resolve(side, "renamed-step", complete: false,
                                                                   begun: true, started: true);

            Assert.That(resolved.Step, Is.EqualTo(0));
            Assert.That(resolved.Begun, Is.False, "the first step's setup has not run in this world");
            Assert.That(resolved.Started, Is.True);
        }

        private void PutSide(bool started, int step) =>
            director.Restore(new Dictionary<string, ObjectiveProgress>
            {
                ["sprite-hamlet"] = new ObjectiveProgress { Step = step, Started = started },
            });

        private QuestStepWindow Window(string from, string until) =>
            new QuestStepWindow { chain = side, from = from, until = until };

        [Test]
        public void Window_FollowsTheChainFromAcceptanceToCompletion()
        {
            QuestStepWindow middle = Window("find-hut", "escort");
            QuestStepWindow beforeAccepting = Window("", "find-hut");
            QuestStepWindow afterFinishing = Window(QuestStepWindow.CompleteToken, "");

            PutSide(started: false, step: 0);
            Assert.That(middle.Contains(Side), Is.False, "nobody has accepted the chain yet");
            Assert.That(beforeAccepting.Contains(Side), Is.True);

            PutSide(started: true, step: 1);
            Assert.That(middle.Contains(Side), Is.True);
            Assert.That(beforeAccepting.Contains(Side), Is.False);

            PutSide(started: true, step: 2);
            Assert.That(middle.Contains(Side), Is.False, "until is the first step after the window");

            PutSide(started: true, step: 3);
            Assert.That(afterFinishing.Contains(Side), Is.True);
        }

        [Test]
        public void Window_ForAnotherChainNeverMatches()
        {
            Assert.That(Window("", "").Contains(Main), Is.False);
        }

        [Test]
        public void Window_WithAMisspeltStepIsOutsideAndSaysSo()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("step 'find-hutt'"));
            PutSide(started: true, step: 0);

            Assert.That(Window("find-hutt", "").Contains(Side), Is.False);
        }

        [Test]
        public void QuestMarks_CountOnlyForTheStepTheyLandIn()
        {
            Side.World.RecordMark(QuestMark.Talked, "hamlet-elder");
            Assert.That(Side.World.HasMark(QuestMark.Talked, "hamlet-elder"), Is.True);
            Assert.That(Side.World.HasMark(QuestMark.Dead, "hamlet-elder"), Is.False);

            PutSide(started: true, step: 1);

            Assert.That(Side.World.HasMark(QuestMark.Talked, "hamlet-elder"), Is.False,
                        "talking to the elder before the step that asks for it must not finish that step");
        }
    }
}
