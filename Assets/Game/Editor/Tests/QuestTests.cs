// Several quests at once: the quest log's choice of which one the visor follows, every quest advancing on its own,
// quests begun by triggers and givers, and every quest saved and restored by id.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types. Built
// by hand: nothing runs Awake in EditMode, so the director's is invoked explicitly.
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.Tests
{
    public class QuestTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<Object> made = new();
        private readonly List<IQuestEntry> listed = new();

        /// <summary>A step met when the test says so.</summary>
        private sealed class SwitchStep : ObjectiveStep
        {
            public bool Met;
            public override bool IsMet(ObjectiveWorld world) => Met;
        }

        /// <summary>A quest log line the test finishes by hand.</summary>
        private sealed class Entry : IQuestEntry
        {
            public Entry(string name) => Name = name;
            public string Name { get; }
            public string Summary => string.Empty;
            public bool IsComplete { get; set; }
            public ITrackedObjective Objective => null;
        }

        [TearDown]
        public void CleanUp()
        {
            foreach (IQuestEntry entry in listed) QuestLog.Close(entry);
            listed.Clear();

            foreach (Object thing in made)
                if (thing != null) Object.DestroyImmediate(thing);
            made.Clear();

            // Nothing runs OnDestroy in EditMode, so the director's Instance is let go by hand.
            typeof(ObjectiveDirector).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic)
                                     .Invoke(null, null);
        }

        // ── The quest log ───────────────────────────────────────────────────────

        [Test]
        public void TheMostRecentlyAcquiredQuestIsActive()
        {
            Entry first = Open("first", acquired: true);
            Entry second = Open("second", acquired: true);
            Assert.AreSame(second, QuestLog.Active);

            QuestLog.Open(first, acquired: true);
            Assert.AreSame(first, QuestLog.Active, "acquiring an open quest again makes it the active one");
        }

        [Test]
        public void AQuestListedQuietlyDoesNotTakeTheVisor()
        {
            Entry chosen = Open("chosen", acquired: true);
            Open("restored", acquired: false);
            Assert.AreSame(chosen, QuestLog.Active, "a restored, joined or streamed-in quest is listed, not followed");
        }

        [Test]
        public void ThePlayerCanFollowAnyUnfinishedQuest()
        {
            Entry older = Open("older", acquired: true);
            Entry finished = Open("finished", acquired: true);
            Open("newer", acquired: true);
            finished.IsComplete = true;

            Assert.IsTrue(QuestLog.SetActive(older));
            Assert.AreSame(older, QuestLog.Active);

            Assert.IsFalse(QuestLog.SetActive(finished), "a finished quest has nothing to follow");
            Assert.IsFalse(QuestLog.SetActive(new Entry("never opened")));
            Assert.AreSame(older, QuestLog.Active);
        }

        [Test]
        public void AChosenQuestThatFinishesOrClosesHandsTheVisorToTheNewest()
        {
            Entry older = Open("older", acquired: true);
            Entry newer = Open("newer", acquired: true);
            QuestLog.SetActive(older);

            older.IsComplete = true;
            Assert.AreSame(newer, QuestLog.Active);

            older.IsComplete = false;
            QuestLog.SetActive(older);
            QuestLog.Close(older);
            Assert.AreSame(newer, QuestLog.Active);

            newer.IsComplete = true;
            Assert.IsNull(QuestLog.Active, "nothing unfinished is open");
        }

        // ── Quests ──────────────────────────────────────────────────────────────

        [Test]
        public void EveryQuestAdvancesWhetherOrNotItIsFollowed()
        {
            var a = new Quest(Chain("a", out SwitchStep stepA), 0);
            var b = new Quest(Chain("b", out SwitchStep stepB), 1);

            Assert.IsTrue(a.Advance() & b.Advance(), "both begin their first step");
            stepB.Met = true;
            a.Advance();
            b.Advance();

            Assert.IsFalse(a.IsComplete);
            Assert.IsTrue(b.IsComplete, "the quest nobody follows still finishes");
            stepA.Met = true;
            a.Advance();
            Assert.IsTrue(a.IsComplete);
        }

        [Test]
        public void AQuestIsSavedByIds()
        {
            var quest = new Quest(Chain("ids", out _), 3);
            quest.Advance();

            SavedQuest saved = quest.ToSaved();
            Assert.AreEqual("ids", saved.Id);
            Assert.AreEqual(3, saved.Order);
            Assert.AreEqual("ids-1", saved.Step);
            Assert.IsTrue(saved.Begun);
        }

        // ── The director ────────────────────────────────────────────────────────

        [Test]
        public void ACatalogQuestBeginsOnceAndAQuestOutsideItNever()
        {
            ObjectiveChain side = Chain("side", out _);
            ObjectiveDirector director = Director(Chain("story", out _), side);

            Assert.IsTrue(director.StartQuest(side));
            Assert.IsFalse(director.StartQuest(side), "a quest the crew have is not begun twice");
            Assert.AreEqual(1, director.Find(side).Order, "taken on after the story quest");

            ObjectiveChain stranger = Chain("stranger", out _);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("not in the ObjectiveDirector's quest catalog"));
            Assert.IsFalse(director.StartQuest(stranger));
            Assert.AreEqual(2, director.Quests.Count);
        }

        [Test]
        public void AFollowUpBeginsOnceItsQuestIsFinished()
        {
            ObjectiveChain story = Chain("story", out SwitchStep step);
            ObjectiveDirector director = Director(story);

            var trigger = Make<QuestCompletedTrigger>();
            Set(trigger, typeof(QuestCompletedTrigger), "after", story);

            Assert.IsFalse(trigger.ShouldStart(director));
            director.Story.Advance();
            step.Met = true;
            director.Story.Advance();
            Assert.IsTrue(trigger.ShouldStart(director));
        }

        [Test]
        public void ASaveRestoresEveryQuestByIdAndDropsOnesTheCatalogLost()
        {
            ObjectiveChain side = Chain("side", out _);
            ObjectiveChain later = Chain("later", out _);
            ObjectiveDirector director = Director(Chain("story", out _), side, later);
            director.StartQuest(later);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("which the catalog no longer has"));
            director.Restore(new List<SavedQuest>
            {
                new() { Id = "gone", Order = 1, Step = "gone-1" },
                new() { Id = "side", Order = 2, Step = "side-1", Begun = true },
                new() { Id = "story", Order = 0, Complete = true },
            });

            Assert.AreEqual(2, director.Quests.Count, "the story quest and 'side'; 'later' was never in this save");
            Assert.IsTrue(director.Story.IsComplete);
            Quest restored = director.Find(side);
            Assert.AreEqual(2, restored.Order);
            Assert.IsTrue(restored.Progress.Begun);
            Assert.IsNull(director.Find(later));
        }

        [Test]
        public void TheSaverWritesSideQuestsBesideTheStoryAndReadsASaveFromBeforeThem()
        {
            ObjectiveChain side = Chain("side", out _);
            ObjectiveDirector director = Director(Chain("story", out _), side);
            var saver = director.gameObject.AddComponent<ObjectiveSaveable>();
            director.StartQuest(side);

            var captured = (ObjectiveSaveable.State)saver.CaptureState();
            Assert.AreEqual("story-1", captured.step);
            Assert.AreEqual("side", captured.quests[0].id);
            Assert.AreEqual(1, captured.quests[0].order);

            saver.RestoreState(JObject.Parse("{ \"step\": \"story-1\", \"complete\": false, \"begun\": true }"));
            Assert.AreEqual(1, director.Quests.Count, "a save from before side quests has only the story quest");
            Assert.IsTrue(director.Story.Progress.Begun);
        }

        // ── Building ────────────────────────────────────────────────────────────

        private Entry Open(string name, bool acquired)
        {
            var entry = new Entry(name);
            QuestLog.Open(entry, acquired);
            listed.Add(entry);
            return entry;
        }

        /// <summary>A one-step quest; its step's id is <c>{id}-1</c>.</summary>
        private ObjectiveChain Chain(string id, out SwitchStep step)
        {
            step = Make<SwitchStep>();
            Set(step, typeof(ObjectiveStep), "id", id + "-1");

            var chain = Make<ObjectiveChain>();
            chain.name = id;
            Set(chain, typeof(ObjectiveChain), "id", id);
            Set(chain, typeof(ObjectiveChain), "steps", new ObjectiveStep[] { step });
            return chain;
        }

        private ObjectiveDirector Director(ObjectiveChain story, params ObjectiveChain[] catalog)
        {
            var go = new GameObject("Objectives");
            made.Add(go);

            var director = go.AddComponent<ObjectiveDirector>();
            Set(director, typeof(ObjectiveDirector), "chain", story);
            Set(director, typeof(ObjectiveDirector), "quests", catalog);
            typeof(ObjectiveDirector).GetMethod("Awake", Hidden).Invoke(director, null);
            return director;
        }

        private T Make<T>() where T : ScriptableObject
        {
            var thing = ScriptableObject.CreateInstance<T>();
            made.Add(thing);
            return thing;
        }

        private static void Set(object target, System.Type type, string field, object value) =>
            type.GetField(field, Hidden).SetValue(target, value);
    }
}
