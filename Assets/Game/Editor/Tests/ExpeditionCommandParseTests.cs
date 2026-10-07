// The /exp debug commands' pure half: how a typed line parses (verb, target, numbers), how a target number or id
// finds its settlement or band, and how the answer is laid out in chat lines. Plain values only, so these run
// without the Editor.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Agents.Expeditions;

namespace SpaceGame.EditorTools
{
    public class ExpeditionCommandParseTests
    {
        private static bool Parse(out ExpeditionCommand command, out string problem, params string[] args) =>
            ExpeditionCommandParser.TryParse(args, out command, out problem);

        // ---- Verbs ---------------------------------------------------------------------------------

        [Test]
        public void Parse_List()
        {
            Assert.IsTrue(Parse(out ExpeditionCommand command, out _, "list"));
            Assert.AreEqual(ExpeditionVerb.List, command.Verb);
        }

        [Test]
        public void Parse_VerbIgnoresCase()
        {
            Assert.IsTrue(Parse(out ExpeditionCommand command, out _, "LiSt"));
            Assert.AreEqual(ExpeditionVerb.List, command.Verb);
        }

        [Test]
        public void Parse_NoWordsOrHelp_GivesTheWholeUsage()
        {
            foreach (string[] args in new[] { new string[0], new[] { "help" }, new[] { "?" } })
            {
                Assert.IsFalse(ExpeditionCommandParser.TryParse(args, out _, out string problem), string.Join(" ", args));
                Assert.AreEqual(ExpeditionCommandParser.Usage, problem);
            }
        }

        [Test]
        public void Parse_UnknownVerb_NamesItAndGivesTheUsage()
        {
            Assert.IsFalse(Parse(out _, out string problem, "launch", "1"));
            StringAssert.Contains("'launch'", problem);
            StringAssert.Contains(ExpeditionCommandParser.Usage, problem);
        }

        [Test]
        public void Usage_ListsEveryVerb()
        {
            foreach (string verb in new[] { "list", "force", "depart", "advance", "days", "home", "record" })
                StringAssert.Contains("/exp " + verb, ExpeditionCommandParser.Usage);
        }

        [Test]
        public void Parse_Force_TakesSettlementAndGoal()
        {
            Assert.IsTrue(Parse(out ExpeditionCommand command, out _, "force", "2", "scout"));
            Assert.AreEqual(ExpeditionVerb.Force, command.Verb);
            Assert.AreEqual(2, command.Target.Number);
            Assert.AreEqual("scout", command.GoalId);
        }

        [Test]
        public void Parse_Force_WithoutGoal_GivesItsUsage()
        {
            Assert.IsFalse(Parse(out _, out string problem, "force", "1"));
            StringAssert.Contains("/exp force <settlement> <goal>", problem);
        }

        [Test]
        public void Parse_Depart_TakesOneBand()
        {
            Assert.IsTrue(Parse(out ExpeditionCommand command, out _, "depart", "#1"));
            Assert.AreEqual(ExpeditionVerb.Depart, command.Verb);
            Assert.AreEqual(1, command.Target.Number);

            Assert.IsFalse(Parse(out _, out string problem, "depart"));
            StringAssert.Contains("/exp depart <band>", problem);
        }

        [Test]
        public void Parse_Advance_DefaultsToOneStage()
        {
            Assert.IsTrue(Parse(out ExpeditionCommand command, out _, "advance", "1"));
            Assert.AreEqual(ExpeditionVerb.Advance, command.Verb);
            Assert.AreEqual(1, command.Count);
        }

        [Test]
        public void Parse_Advance_TakesACount()
        {
            Assert.IsTrue(Parse(out ExpeditionCommand command, out _, "advance", "1", "3"));
            Assert.AreEqual(3, command.Count);
        }

        [Test]
        public void Parse_Advance_RejectsZeroNegativeAndWords()
        {
            foreach (string count in new[] { "0", "-2", "two", "1.5" })
            {
                Assert.IsFalse(Parse(out _, out string problem, "advance", "1", count), count);
                StringAssert.Contains("/exp advance <band> [n]", problem, count);
            }
        }

        [Test]
        public void Parse_Days_TakesAFractionInAnyLocale()
        {
            Assert.IsTrue(Parse(out ExpeditionCommand command, out _, "days", "1", "0.5"));
            Assert.AreEqual(ExpeditionVerb.Days, command.Verb);
            Assert.AreEqual(0.5f, command.Days);
        }

        [Test]
        public void Parse_Days_RejectsMissingZeroAndNonNumbers()
        {
            Assert.IsFalse(Parse(out _, out _, "days", "1"));
            foreach (string days in new[] { "0", "-1", "NaN", "Infinity", "soon" })
            {
                Assert.IsFalse(Parse(out _, out string problem, "days", "1", days), days);
                StringAssert.Contains("/exp days <band> <n>", problem, days);
            }
        }

        [Test]
        public void Parse_HomeAndRecord_TakeOneBand()
        {
            Assert.IsTrue(Parse(out ExpeditionCommand home, out _, "home", "#2"));
            Assert.AreEqual(ExpeditionVerb.Home, home.Verb);
            Assert.AreEqual(2, home.Target.Number);

            Assert.IsTrue(Parse(out ExpeditionCommand record, out _, "record", "exp:abc:3"));
            Assert.AreEqual(ExpeditionVerb.Record, record.Verb);
            Assert.AreEqual("exp:abc:3", record.Target.Id);
        }

        [Test]
        public void Parse_TooManyWords_GivesTheVerbsUsage()
        {
            Assert.IsFalse(Parse(out _, out string problem, "home", "1", "now"));
            StringAssert.Contains("/exp home <band>", problem);
            Assert.IsFalse(Parse(out _, out _, "list", "all"));
        }

        [Test]
        public void Parse_TargetMissing_GivesTheVerbsUsage()
        {
            Assert.IsFalse(Parse(out _, out string problem, "record"));
            StringAssert.Contains("/exp record <band>", problem);
        }

        // ---- Targets -------------------------------------------------------------------------------

        [Test]
        public void Target_NumberWithOrWithoutHash_IsAListNumber()
        {
            Assert.IsTrue(ExpeditionCommandParser.TryParseTarget("3", out ExpeditionTarget plain, out _));
            Assert.IsTrue(ExpeditionCommandParser.TryParseTarget("#3", out ExpeditionTarget hashed, out _));
            Assert.IsTrue(plain.ByNumber);
            Assert.AreEqual(3, plain.Number);
            Assert.AreEqual(3, hashed.Number);
        }

        [Test]
        public void Target_ZeroOrHashWord_IsRefused()
        {
            Assert.IsFalse(ExpeditionCommandParser.TryParseTarget("0", out _, out string zero));
            StringAssert.Contains("start at 1", zero);
            Assert.IsFalse(ExpeditionCommandParser.TryParseTarget("#x", out _, out _));
        }

        [Test]
        public void Target_AnyOtherWord_IsAnId()
        {
            Assert.IsTrue(ExpeditionCommandParser.TryParseTarget("1c3973d9ab", out ExpeditionTarget target, out _));
            Assert.IsFalse(target.ByNumber);
            Assert.AreEqual("1c3973d9ab", target.Id);
        }

        [Test]
        public void Resolve_ByNumber_CountsFromOneInListOrder()
        {
            var bands = new List<ExpeditionRecord> { Band("a"), Band("b"), Band("c") };
            ExpeditionCommandParser.TryParseTarget("2", out ExpeditionTarget target, out _);

            Assert.IsTrue(ExpeditionCommandParser.TryResolve(target, bands, b => b.id, "band", out ExpeditionRecord found, out _));
            Assert.AreEqual("b", found.id);
        }

        [Test]
        public void Resolve_ByNumberPastTheEnd_SaysHowManyThereAre()
        {
            var bands = new List<ExpeditionRecord> { Band("a"), Band("b") };
            ExpeditionCommandParser.TryParseTarget("5", out ExpeditionTarget target, out _);

            Assert.IsFalse(ExpeditionCommandParser.TryResolve(target, bands, b => b.id, "band", out _, out string problem));
            StringAssert.Contains("#5", problem);
            StringAssert.Contains("2", problem);

            Assert.IsFalse(ExpeditionCommandParser.TryResolve(target, new List<ExpeditionRecord>(), b => b.id, "band", out _, out string none));
            StringAssert.Contains("no bands", none);
        }

        [Test]
        public void Resolve_ById_FindsExactlyThatOne()
        {
            var bands = new List<ExpeditionRecord> { Band("exp:s:1"), Band("exp:s:2") };
            ExpeditionCommandParser.TryParseTarget("exp:s:2", out ExpeditionTarget target, out _);
            Assert.IsTrue(ExpeditionCommandParser.TryResolve(target, bands, b => b.id, "band", out ExpeditionRecord found, out _));
            Assert.AreEqual("exp:s:2", found.id);

            ExpeditionCommandParser.TryParseTarget("exp:s:9", out ExpeditionTarget missing, out _);
            Assert.IsFalse(ExpeditionCommandParser.TryResolve(missing, bands, b => b.id, "band", out _, out string problem));
            StringAssert.Contains("exp:s:9", problem);
        }

        // ---- Output --------------------------------------------------------------------------------

        [Test]
        public void Stage_DescribesWhereTheTripIs()
        {
            ExpeditionRecord band = Band("b");
            band.phase = ExpeditionPhase.Announced;
            band.departDay = 12;
            StringAssert.Contains("day 12", ExpeditionCommandText.Stage(band));

            band.phase = ExpeditionPhase.Out;
            band.stageIndex = 1;
            band.waypointsDone = 1;
            band.stageMinutesLeft = 139.2f;
            string stage = ExpeditionCommandText.Stage(band);
            StringAssert.Contains("stage 2/3 Search", stage);
            StringAssert.Contains("waypoint 2/3", stage);
            StringAssert.Contains("140 min left", stage);

            band.stageIndex = 2;
            band.stageMinutesLeft = StageRecord.NoLimit;
            StringAssert.DoesNotContain("min left", ExpeditionCommandText.Stage(band));
        }

        [Test]
        public void List_NumbersSettlementsAndBands_AndShowsMembers()
        {
            var settlements = new List<SettlementState>
            {
                new SettlementState { settlementId = "s-one", rotation = 3, lastGoalId = "scout", roster = new RosterEntry[70] },
                new SettlementState { settlementId = "s-two" },
            };
            ExpeditionRecord band = Band("exp:s-two:0");
            band.settlementId = "s-two";
            band.goalId = "scout";
            band.phase = ExpeditionPhase.Out;
            band.stageIndex = 0;
            band.members = new[]
            {
                new MemberRecord { residentKey = "r:12", isLeader = true, health01 = 1f },
                new MemberRecord { residentKey = "r:4", health01 = 0.62f },
                new MemberRecord { residentKey = "r:9", dead = true, health01 = 0f },
            };

            string list = ExpeditionCommandText.List(settlements, new[] { band });

            StringAssert.Contains("#1 s-one", list);
            StringAssert.Contains("#2 s-two", list);
            StringAssert.Contains("70 residents", list);
            StringAssert.Contains("#1 exp:s-two:0", list);
            StringAssert.Contains("settlement #2", list);
            StringAssert.Contains("scout, Out", list);
            StringAssert.Contains("r:12 leader 100%", list);
            StringAssert.Contains("r:4 62%", list);
            StringAssert.Contains("r:9 dead", list);
        }

        [Test]
        public void List_Empty_SaysSo()
        {
            string list = ExpeditionCommandText.List(new List<SettlementState>(), new List<ExpeditionRecord>());
            StringAssert.Contains("No settlement runs bands", list);
            StringAssert.Contains("No bands", list);
        }

        [Test]
        public void ChatLines_SplitsLines_AndWrapsLongOnesAtASpace()
        {
            List<string> lines = ExpeditionCommandText.ChatLines("one two three four\n\nfive", 9);
            CollectionAssert.AreEqual(new[] { "one two", "three", "four", "five" }, lines);
        }

        [Test]
        public void ChatLines_CutsAWordLongerThanALine()
        {
            List<string> lines = ExpeditionCommandText.ChatLines("{\"id\":\"exp:abc\"}", 6);
            Assert.That(lines, Has.All.Length.LessThanOrEqualTo(6));
            Assert.AreEqual("{\"id\":\"exp:abc\"}", string.Concat(lines));
        }

        private static ExpeditionRecord Band(string id) => new ExpeditionRecord
        {
            id = id,
            settlementId = "s",
            stages = new[]
            {
                new StageRecord { kind = StageKind.Travel, minutes = StageRecord.NoLimit },
                new StageRecord { kind = StageKind.Search, minutes = 600f, waypoints = 3 },
                new StageRecord { kind = StageKind.ReturnHome, minutes = StageRecord.NoLimit },
            },
        };
    }
}
