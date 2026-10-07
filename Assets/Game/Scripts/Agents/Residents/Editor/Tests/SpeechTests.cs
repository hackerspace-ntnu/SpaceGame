// The speech data path: a line id is a stable hash of its key, the table reports what is wrong with a
// sheet instead of throwing, the matcher prefers the most specific fresh line and picks the same one for
// the same seed, and the shipped NomadLines.txt parses clean, names only real archetypes, uses only
// known tokens and has a fallback line for every topic a resident can be asked for.
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.Agents.Residents.Tests
{
    public class SpeechTests
    {
        private const string Header = "key\tspeaker\ttopic\tstance\tobservation\tactivity\trepliesTo\tregister\ttext\n";
        private const string ShippedLines = "Game/ScriptableObjects/Residents/Lines/NomadLines.txt";
        private const string ArchetypeFolder = "Assets/Game/ScriptableObjects/Residents/Archetypes/";

        private static string Row(string key, string speaker, string topic, string stance, string text,
                                  string observation = "") =>
            string.Join("\t", key, speaker, topic, stance, observation, "", "", "", text) + "\n";

        private static LineQuery Query(Topic topic, Stance stance = Stance.Unsure) =>
            new LineQuery { topic = topic, stance = stance };

        [Test]
        public void IdOf_IsFnv1aOfTheKey()
        {
            Assert.AreEqual(2166136261u, LineTable.IdOf(""));
            Assert.AreEqual(0xe40c292cu, LineTable.IdOf("a"));
            Assert.AreEqual(0xbf9cf968u, LineTable.IdOf("foobar"));
        }

        [Test]
        public void Ids_SurviveReorderingRows()
        {
            LineTable sorted = LineTable.Parse(Header + Row("one", "", "Greeting", "", "Hello.") + Row("two", "", "Farewell", "", "Bye."));
            LineTable shuffled = LineTable.Parse(Header + Row("two", "", "Farewell", "", "Bye.") + Row("one", "", "Greeting", "", "Hello."));

            Assert.IsTrue(shuffled.TryGet(LineTable.IdOf("one"), out LineRow row));
            Assert.AreEqual("Hello.", row.text);
            Assert.IsTrue(sorted.TryGet(LineTable.IdOf("two"), out row));
            Assert.AreEqual("Bye.", row.text);
        }

        [Test]
        public void Parse_ReportsDuplicateKeysAndKeepsTheFirst()
        {
            LineTable t = LineTable.Parse(Header + Row("same", "", "Greeting", "", "First.") + Row("same", "", "Greeting", "", "Second."));

            Assert.AreEqual(1, t.Rows.Count);
            Assert.AreEqual(1, t.Errors.Count);
            StringAssert.Contains("duplicate", t.Errors[0]);
            Assert.AreEqual("First.", t.Rows[0].text);
        }

        [Test]
        public void Parse_ReportsUnknownEnumsAndTokens_SkipsCommentsAndBlankLines()
        {
            LineTable t = LineTable.Parse("# a comment\n\n" + Header
                                          + Row("bad.topic", "", "Smalltalk", "", "Hi.")
                                          + Row("bad.stance", "", "Greeting", "grumpy", "Hi.")
                                          + Row("bad.token", "", "Greeting", "", "Hi {nobody}.")
                                          + Row("too.many", "", "Greeting", "", "{name}, {kin}, {friend}.")
                                          + "# another\n" + Row("ok", "", "Greeting", "", "Hi."));

            Assert.AreEqual(4, t.Errors.Count, string.Join("\n", t.Errors));
            Assert.AreEqual(1, t.Rows.Count);
            Assert.AreEqual("ok", t.Rows[0].key);
        }

        [Test]
        public void Stance_FamilyAndExactStanceBothMatch()
        {
            LineTable t = LineTable.Parse(Header + Row("family", "", "Greeting", "wary", "Who sent you?")
                                                 + Row("exact", "", "Farewell", "Afraid", "Go away."));

            Assert.IsTrue(t.Rows[0].MatchesStance(Stance.Cold));
            Assert.IsFalse(t.Rows[0].MatchesStance(Stance.Warm));
            Assert.IsTrue(t.Rows[1].MatchesStance(Stance.Afraid));
            Assert.IsFalse(t.Rows[1].MatchesStance(Stance.Cold));
        }

        [Test]
        public void Pick_PersonBeatsArchetypeBeatsGeneric()
        {
            LineTable t = LineTable.Parse(Header + Row("generic", "", "Greeting", "", "Hello.")
                                                 + Row("archetype", "Smith", "Greeting", "", "Anvil's hot.")
                                                 + Row("person", "Raxa", "Greeting", "", "Raxa here."));
            LineQuery q = Query(Topic.Greeting);
            q.archetype = "Smith";
            q.person = "Raxa";

            Assert.AreEqual("person", LineMatcher.Pick(t, q, null, out int score).key);
            Assert.AreEqual(LineMatcher.PersonScore + LineMatcher.CriterionScore, score);

            q.person = "Someone";
            Assert.AreEqual("archetype", LineMatcher.Pick(t, q, null, out _).key);

            q.archetype = "Cook";
            Assert.AreEqual("generic", LineMatcher.Pick(t, q, null, out _).key);
        }

        [Test]
        public void Pick_FilledCriterionMustHold_AndMoreCriteriaWin()
        {
            LineTable t = LineTable.Parse(Header + Row("any", "", "Remark", "", "Hm.")
                                                 + Row("armed", "", "Remark", "", "Put it away.", "ArmedHeld"));

            Assert.AreEqual("armed", LineMatcher.Pick(t, new LineQuery { topic = Topic.Remark, observation = Observation.ArmedHeld }, null, out _).key);
            Assert.AreEqual("any", LineMatcher.Pick(t, new LineQuery { topic = Topic.Remark, observation = Observation.Curio }, null, out _).key);
            Assert.IsNull(LineMatcher.Pick(t, Query(Topic.Greeting), null, out _));
        }

        [Test]
        public void Pick_PassesOverRecentlySaid_ButRepeatsRatherThanFallSilent()
        {
            LineTable t = LineTable.Parse(Header + Row("a", "", "Work", "", "Hot.") + Row("b", "", "Work", "", "Heavy."));
            var recent = new HashSet<uint> { LineTable.IdOf("a") };

            for (int seed = 0; seed < 32; seed++)
            {
                LineQuery q = Query(Topic.Work);
                q.seed = seed;
                Assert.AreEqual("b", LineMatcher.Pick(t, q, recent, out _).key);
            }

            recent.Add(LineTable.IdOf("b"));
            Assert.IsNotNull(LineMatcher.Pick(t, Query(Topic.Work), recent, out _));
        }

        [Test]
        public void Pick_IsDeterministicPerSeed_AndSpreadsAcrossTheTopBand()
        {
            LineTable t = LineTable.Parse(Header + Row("a", "", "Gossip", "", "One.") + Row("b", "", "Gossip", "", "Two.")
                                                 + Row("c", "", "Gossip", "", "Three."));
            var picked = new HashSet<string>();
            var runnersUp = new List<(LineRow, int)>();

            for (int seed = 0; seed < 64; seed++)
            {
                LineQuery q = Query(Topic.Gossip);
                q.seed = seed;
                LineRow first = LineMatcher.Pick(t, q, null, out _, runnersUp);
                Assert.AreSame(first, LineMatcher.Pick(t, q, null, out _));
                Assert.AreEqual(2, runnersUp.Count);
                picked.Add(first.key);
            }

            Assert.AreEqual(3, picked.Count);
        }

        [Test]
        public void ShippedLines_ParseCleanWithKnownTokensAndRealSpeakers()
        {
            LineTable t = LineTable.Parse(File.ReadAllText(Path.Combine(Application.dataPath, ShippedLines)));

            Assert.IsEmpty(t.Errors, string.Join("\n", t.Errors));
            Assert.Greater(t.Rows.Count, 0);
            foreach (LineRow row in t.Rows)
            {
                Assert.LessOrEqual(SpeechTokens.CountTokens(row.text, out string unknown), LineTable.MaxTokensPerLine, row.key);
                Assert.IsNull(unknown, row.key);
                StringAssert.DoesNotContain("{", SpeechTokens.Resolve(row.text, null, null), row.key);
                if (row.speaker != null)
                    Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<ResidentArchetype>(ArchetypeFolder + row.speaker + ".asset"),
                                     $"{row.key}: no archetype asset named '{row.speaker}'");
            }
        }

        [Test]
        public void ThePlaceTokenIsKnownAndSaysHereWhenThereIsNoSpot()
        {
            CollectionAssert.Contains(SpeechTokens.Known, "{place}");
            Assert.AreEqual("Quiet at here.", SpeechTokens.Resolve("Quiet at {place}.", null, null));
        }

        [Test]
        public void ShippedLines_WarnAboutAShoveInItsOwnWordsForEveryStance()
        {
            LineTable t = LineTable.Parse(File.ReadAllText(Path.Combine(Application.dataPath, ShippedLines)));

            foreach (Stance stance in Enum.GetValues(typeof(Stance)))
            {
                var query = new LineQuery { topic = Topic.Warning, stance = stance, observation = Observation.Jostling };
                LineRow row = LineMatcher.Pick(t, in query, null, out _);
                Assert.IsNotNull(row, $"no jostle warning for a {stance} resident");
                Assert.AreEqual(Observation.Jostling, row.observation, $"a {stance} resident warns about a shove generically ({row.key})");
            }
        }

        [Test]
        public void ShippedLines_AnswerEveryTopicForEveryStance()
        {
            LineTable t = LineTable.Parse(File.ReadAllText(Path.Combine(Application.dataPath, ShippedLines)));

            foreach (Topic topic in Enum.GetValues(typeof(Topic)))
            {
                // A remark is only ever made about something noticed; it has no observation-free fallback by design.
                if (topic == Topic.Remark) continue;
                foreach (Stance stance in Enum.GetValues(typeof(Stance)))
                    Assert.IsNotNull(LineMatcher.Pick(t, Query(topic, stance), null, out _), $"no {topic} line for a {stance} resident");
            }
        }
    }
}
