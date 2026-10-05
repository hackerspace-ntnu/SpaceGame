// The pure expedition rules: how many warriors (and residents of other roles) a settlement keeps, who moves in to
// keep them and who is recast, which goal a band runs, who goes, how its trip is rolled and advanced, the legs a band with stand-ins
// walks, where it musters and when it is handed off. Plain
// values only, so these run without the Editor — except Records_RoundTripThroughSaveSerializer, which
// loads Newtonsoft.
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SpaceGame.Agents.Expeditions;
using SpaceGame.Persistence;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class ExpeditionRulesTests
    {
        private static readonly Vector2Int Small = new Vector2Int(6, 8);
        private const int Large = 12, LargeFromBeds = 40;

        [Test]
        public void Quota_LargeSettlement_Is12()
        {
            Assert.AreEqual(12, ExpeditionRules.QuotaForBeds(Small, Large, LargeFromBeds, 70));
            Assert.AreEqual(12, ExpeditionRules.QuotaForBeds(Small, Large, LargeFromBeds, LargeFromBeds), "exactly largeFromBeds is large");
        }

        [Test]
        public void Quota_SmallSettlement_IsWithin6To8()
        {
            int previous = int.MinValue;
            for (int beds = 0; beds < LargeFromBeds; beds++)
            {
                int quota = ExpeditionRules.QuotaForBeds(Small, Large, LargeFromBeds, beds);
                Assert.That(quota, Is.InRange(6, 8), $"{beds} beds");
                Assert.GreaterOrEqual(quota, previous, $"{beds} beds keep fewer warriors than {beds - 1}");
                previous = quota;
            }
            Assert.AreEqual(6, ExpeditionRules.QuotaForBeds(Small, Large, LargeFromBeds, 0), "the smallest keeps the floor");
            Assert.AreEqual(8, ExpeditionRules.QuotaForBeds(Small, Large, LargeFromBeds, LargeFromBeds - 1), "just below large keeps the ceiling");
        }

        [Test]
        public void Quota_NoLargeThreshold_IsAlwaysLarge()
        {
            Assert.AreEqual(Large, ExpeditionRules.QuotaForBeds(Small, Large, 0, 0));
        }

        [Test]
        public void Quota_ScoutQuotaUsesTheSameBedBands()
        {
            var small = new Vector2Int(1, 2);

            Assert.AreEqual(1, ExpeditionRules.QuotaForBeds(small, 3, LargeFromBeds, 0));
            Assert.AreEqual(2, ExpeditionRules.QuotaForBeds(small, 3, LargeFromBeds, LargeFromBeds - 1));
            Assert.AreEqual(3, ExpeditionRules.QuotaForBeds(small, 3, LargeFromBeds, 70), "the 70-bed settlement keeps three scouts");
        }

        [Test]
        public void Recruits_OnlyFromBodiesThatSuitAWarriorArchetype()
        {
            var candidates = new List<RecruitCandidate>
            {
                new RecruitCandidate(0, 0.9f, isAdult: true, suitsRole: false, holdsRole: false),
                new RecruitCandidate(1, 0.8f, isAdult: true, suitsRole: true, holdsRole: true),
                new RecruitCandidate(2, 0.7f, isAdult: false, suitsRole: true, holdsRole: false),
                new RecruitCandidate(3, 0.2f, isAdult: true, suitsRole: true, holdsRole: false),
                new RecruitCandidate(4, 0.5f, isAdult: true, suitsRole: true, holdsRole: false),
            };

            List<int> picks = ExpeditionRules.PickRecruits(candidates, 5, 1);

            CollectionAssert.AreEqual(new[] { 4, 3 }, picks, "only adults whose body suits a warrior and who are not one already");
        }

        [Test]
        public void Recruits_HighestNerveFirst()
        {
            var candidates = new List<RecruitCandidate> { Eligible(0, 0.3f), Eligible(1, 0.9f), Eligible(2, 0.6f) };

            CollectionAssert.AreEqual(new[] { 1, 2 }, ExpeditionRules.PickRecruits(candidates, 2, 7));
        }

        [Test]
        public void Recruits_StableForSeed()
        {
            List<RecruitCandidate> candidates = Enumerable.Range(0, 12).Select(i => Eligible(i, i % 3 * 0.25f)).ToList();
            List<RecruitCandidate> reversed = Enumerable.Reverse(candidates).ToList();

            List<int> picks = ExpeditionRules.PickRecruits(candidates, 5, 42);

            CollectionAssert.AreEqual(picks, ExpeditionRules.PickRecruits(candidates, 5, 42), "same seed, same picks");
            CollectionAssert.AreEqual(picks, ExpeditionRules.PickRecruits(reversed, 5, 42), "the order candidates are listed in does not matter");
        }

        [Test]
        public void Recruits_EqualNerveIsDecidedBySeed()
        {
            List<RecruitCandidate> candidates = Enumerable.Range(0, 10).Select(i => Eligible(i, 0.5f)).ToList();

            var distinct = new HashSet<string>();
            for (int seed = 0; seed < 20; seed++)
                distinct.Add(string.Join(",", ExpeditionRules.PickRecruits(candidates, 3, seed)));

            Assert.Greater(distinct.Count, 1, "twenty seeds over ten equally bold candidates always picked the same three");
        }

        [Test]
        public void Recruits_NoneNeeded_PicksNobody()
        {
            var candidates = new List<RecruitCandidate> { Eligible(0, 0.5f) };

            Assert.IsEmpty(ExpeditionRules.PickRecruits(candidates, 0, 1));
            Assert.IsEmpty(ExpeditionRules.PickRecruits(candidates, -2, 1));
        }

        [Test]
        public void Recruits_FewerCandidatesThanNeeded_TakesAll()
        {
            var candidates = new List<RecruitCandidate> { Eligible(3, 0.5f), Eligible(8, 0.4f) };

            CollectionAssert.AreEqual(new[] { 3, 8 }, ExpeditionRules.PickRecruits(candidates, 6, 1));
        }

        private static RecruitCandidate Eligible(int residentIndex, float nerve) =>
            new RecruitCandidate(residentIndex, nerve, isAdult: true, suitsRole: true, holdsRole: false);

        // ---- Who moves in ---------------------------------------------------------------------------------

        private const ExpeditionRole W = ExpeditionRole.Warrior, S = ExpeditionRole.Scout, None = ExpeditionRole.None;

        [Test]
        public void MoveIn_QuotaRolesAreTakenFirst_InPoolOrder()
        {
            List<MoveInCandidate> pool = Pool(None, None, None, None, None, None, None, W, W, W);

            List<int> chosen = ExpeditionRules.PlanMoveIn(pool, Quotas((W, 2)), NoneSettled, 5);

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 7, 8 }, chosen, "the two earliest warriors, then the earliest of the rest, in pool order");
        }

        [Test]
        public void MoveIn_NeverMoreThanTheBeds()
        {
            List<MoveInCandidate> pool = Pool(W, W, W, W, W, S, S, S);

            Assert.AreEqual(3, ExpeditionRules.PlanMoveIn(pool, Quotas((W, 6), (S, 3)), NoneSettled, 3).Count);
            Assert.IsEmpty(ExpeditionRules.PlanMoveIn(pool, Quotas((W, 6)), NoneSettled, 0));
            Assert.AreEqual(pool.Count, ExpeditionRules.PlanMoveIn(pool, Quotas((W, 2)), NoneSettled, 50).Count, "more beds than copies: all of them");
        }

        [Test]
        public void MoveIn_PoolShortOfAQuota_TakesEveryCarrier_AndStillFillsTheBeds()
        {
            List<MoveInCandidate> pool = Pool(None, None, None, None, None, S, W);

            List<int> chosen = ExpeditionRules.PlanMoveIn(pool, Quotas((W, 3), (S, 2)), NoneSettled, 4);

            CollectionAssert.AreEqual(new[] { 0, 1, 5, 6 }, chosen);
            Assert.AreEqual(1, Carriers(pool, chosen, W), "one warrior is all the pool has: the quota stays two short");
            Assert.AreEqual(1, Carriers(pool, chosen, S));
        }

        [Test]
        public void MoveIn_CopiesMadeForTheRoleArePreferred()
        {
            var pool = new List<MoveInCandidate>
            {
                new MoveInCandidate(None, fixedRole: false),
                new MoveInCandidate(W, fixedRole: false),
                new MoveInCandidate(None, fixedRole: true),
                new MoveInCandidate(W, fixedRole: true),
            };

            CollectionAssert.AreEqual(new[] { 0, 3 }, ExpeditionRules.PlanMoveIn(pool, Quotas((W, 1)), NoneSettled, 2),
                                      "the copy made for a warrior role, though a dealt warrior comes earlier");
        }

        [Test]
        public void MoveIn_SettledAndEarlierPicksCountTowardEveryQuota()
        {
            List<MoveInCandidate> pool = Pool(None, None, None, W | S, W);

            CollectionAssert.AreEqual(new[] { 0, 1 }, ExpeditionRules.PlanMoveIn(pool, Quotas((W, 1)), new[] { W }, 2),
                                      "a special character who is a warrior already holds the quota");
            CollectionAssert.AreEqual(new[] { 0, 3 }, ExpeditionRules.PlanMoveIn(pool, Quotas((W, 1), (S, 1)), NoneSettled, 2),
                                      "the warrior taken first is also the scout the second quota needs");
        }

        [Test]
        public void MoveIn_WithoutQuotas_IsTheFirstCopiesAsBefore()
        {
            List<MoveInCandidate> pool = Pool(W, None, S, None, W);

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, ExpeditionRules.PlanMoveIn(pool, Quotas(), NoneSettled, 3));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, ExpeditionRules.PlanMoveIn(pool, Quotas((W, 0)), NoneSettled, 3));
        }

        [Test]
        public void MoveIn_StableForSeed_AndVariedAcrossSeeds()
        {
            ExpeditionRole[] roles = Enumerable.Range(0, 40).Select(i => i % 8 == 0 ? W : i % 13 == 0 ? S : None).ToArray();

            List<int> For(int seed)
            {
                var rng = new System.Random(seed);
                List<MoveInCandidate> pool = Pool(roles.OrderBy(_ => rng.Next()).ToArray());
                return ExpeditionRules.PlanMoveIn(pool, Quotas((W, 4), (S, 2)), NoneSettled, 20);
            }

            CollectionAssert.AreEqual(For(5), For(5), "same seed, same move-in");
            var distinct = new HashSet<string>();
            for (int seed = 0; seed < 10; seed++) distinct.Add(string.Join(",", For(seed)));
            Assert.Greater(distinct.Count, 1, "ten seeds always moved the same copies in");
        }

        private static readonly ExpeditionRole[] NoneSettled = new ExpeditionRole[0];

        private static List<MoveInCandidate> Pool(params ExpeditionRole[] roles) =>
            roles.Select(r => new MoveInCandidate(r, fixedRole: r != None)).ToList();

        private static List<(ExpeditionRole role, int count)> Quotas(params (ExpeditionRole role, int count)[] quotas) => quotas.ToList();

        private static int Carriers(List<MoveInCandidate> pool, List<int> chosen, ExpeditionRole role) =>
            chosen.Count(i => (pool[i].roles & role) != 0);

        // ---- The draw -------------------------------------------------------------------------------------

        [Test]
        public void ChooseGoal_OnlyAmongPossible()
        {
            var options = new[] { new GoalOption("hunt", 3f, false), new GoalOption("scout", 1f, true), new GoalOption("outpost", 5f, false) };

            for (int seed = 0; seed < 200; seed++)
                Assert.AreEqual(1, ExpeditionRules.ChooseGoalIndex(options, "", seed, 0.5f), $"seed {seed}");

            var none = new[] { new GoalOption("hunt", 3f, false), new GoalOption("scout", 0f, true) };
            Assert.AreEqual(-1, ExpeditionRules.ChooseGoalIndex(none, "", 1, 0.5f), "nothing possible with weight");

            var onlyLast = new[] { new GoalOption("scout", 1f, true) };
            Assert.AreEqual(0, ExpeditionRules.ChooseGoalIndex(onlyLast, "scout", 1, 0f),
                "a penalty that zeroes the only possible goal must not stop the rotation");
        }

        [Test]
        public void ChooseGoal_VarietyPenaltyLowersLastGoal()
        {
            var options = new[] { new GoalOption("scout", 1f, true), new GoalOption("hunt", 1f, true) };
            const int Draws = 10000;

            int scoutAfterScout = 0, scoutAfterNothing = 0;
            for (int seed = 0; seed < Draws; seed++)
            {
                if (ExpeditionRules.ChooseGoalIndex(options, "scout", seed, 0.5f) == 0) scoutAfterScout++;
                if (ExpeditionRules.ChooseGoalIndex(options, "", seed, 0.5f) == 0) scoutAfterNothing++;
            }

            Assert.That(scoutAfterScout / (float)Draws, Is.InRange(0.31f, 0.36f), "weight 1 × 0.5 against 1 is a third");
            Assert.That(scoutAfterNothing / (float)Draws, Is.InRange(0.47f, 0.53f), "no last goal, no penalty");
        }

        [Test]
        public void ChooseGoal_StableForSeed()
        {
            var options = new[] { new GoalOption("scout", 3f, true), new GoalOption("hunt", 3f, true), new GoalOption("antenna", 1f, true) };

            var seen = new HashSet<int>();
            for (int seed = 0; seed < 50; seed++)
            {
                int pick = ExpeditionRules.ChooseGoalIndex(options, "hunt", seed, 0.5f);
                Assert.AreEqual(pick, ExpeditionRules.ChooseGoalIndex(options, "hunt", seed, 0.5f), $"seed {seed}");
                seen.Add(pick);
            }
            Assert.AreEqual(3, seen.Count, "fifty seeds never drew one of the goals");
        }

        // ---- Who goes -------------------------------------------------------------------------------------

        private const int Today = 10;
        private static readonly RoleSlot[] ScoutSlots =
        {
            new RoleSlot { role = ExpeditionRole.Warrior, min = 2, max = 2 },
            new RoleSlot { role = ExpeditionRole.Scout, min = 1, max = 2 },
        };

        private static int KitOf(ExpeditionRole role) => role == ExpeditionRole.Warrior ? 0 : role == ExpeditionRole.Scout ? 1 : -1;

        private static RosterEntry Entry(int index, ExpeditionRole roles, bool adult = true, params int[] bonds) =>
            new RosterEntry { residentKey = ResidentKey.ForAuthored(index), roles = roles, adult = adult, bonds = bonds, archetypeIndex = index % 5 };

        /// <summary>Warriors 0..warriors-1, scouts after them, then enough people with no role to keep the home share.</summary>
        private static List<RosterEntry> Roster(int warriors, int scouts, int others = 40)
        {
            var roster = new List<RosterEntry>();
            for (int i = 0; i < warriors; i++) roster.Add(Entry(roster.Count, ExpeditionRole.Warrior));
            for (int i = 0; i < scouts; i++) roster.Add(Entry(roster.Count, ExpeditionRole.Scout));
            for (int i = 0; i < others; i++) roster.Add(Entry(roster.Count, ExpeditionRole.None));
            return roster;
        }

        private static bool Pick(IReadOnlyList<RosterEntry> roster, int seed, out List<MemberRecord> members,
                                 ISet<string> away = null, IReadOnlyDictionary<string, int> rest = null,
                                 int warriorsHomeMin = 6, float minHomeShare = 0.8f) =>
            ExpeditionRules.TryPickMembers(ScoutSlots, KitOf, roster, away ?? new HashSet<string>(),
                rest ?? new Dictionary<string, int>(), Today, warriorsHomeMin, minHomeShare, seed, out members);

        private static ExpeditionRole RolesOf(IReadOnlyList<RosterEntry> roster, MemberRecord member) =>
            roster.First(e => e.residentKey == member.residentKey).roles;

        [Test]
        public void PickMembers_FillsWarriorMinimum()
        {
            List<RosterEntry> roster = Roster(warriors: 10, scouts: 5);

            for (int seed = 0; seed < 50; seed++)
            {
                Assert.IsTrue(Pick(roster, seed, out List<MemberRecord> members), $"seed {seed}");

                List<MemberRecord> warriors = members.Where(m => RolesOf(roster, m) == ExpeditionRole.Warrior).ToList();
                List<MemberRecord> scouts = members.Where(m => RolesOf(roster, m) == ExpeditionRole.Scout).ToList();
                Assert.AreEqual(2, warriors.Count, $"seed {seed}: the Warrior slot holds exactly 2");
                Assert.That(scouts.Count, Is.InRange(1, 2), $"seed {seed}");
                Assert.AreEqual(members.Count, warriors.Count + scouts.Count, "nobody without the slot's role");
                Assert.IsTrue(warriors.All(m => m.kitIndex == 0) && scouts.All(m => m.kitIndex == 1), "each member carries its slot's kit");
                Assert.IsTrue(members.All(m => m.health01 == 1f && !m.dead), "a band leaves whole");
                Assert.AreEqual(members.Count, members.Select(m => m.residentKey).Distinct().Count(), "nobody twice");
                foreach (MemberRecord m in members)
                    Assert.AreEqual(roster.First(e => e.residentKey == m.residentKey).archetypeIndex, m.archetypeIndex);
            }
        }

        [Test]
        public void PickMembers_FailsWhenWarriorsHomeWouldDropBelowMinimum()
        {
            Assert.IsFalse(Pick(Roster(warriors: 7, scouts: 3), 1, out List<MemberRecord> members, warriorsHomeMin: 6),
                "7 warriors, 6 must stay: a band of 2 cannot leave");
            Assert.IsEmpty(members);

            var away = new HashSet<string> { ResidentKey.ForAuthored(0), ResidentKey.ForAuthored(1) };
            Assert.IsFalse(Pick(Roster(warriors: 9, scouts: 3), 1, out _, away, warriorsHomeMin: 6),
                "warriors already away are not home");

            Assert.IsTrue(Pick(Roster(warriors: 8, scouts: 3), 1, out _, warriorsHomeMin: 6), "8 warriors leave 6 home");
        }

        [Test]
        public void PickMembers_FailsWhenHomeShareWouldDropBelowMinimum()
        {
            // 10 residents at 0.8: at most 2 may be away, and a band needs 3.
            Assert.IsFalse(Pick(Roster(warriors: 4, scouts: 2, others: 4), 1, out _, warriorsHomeMin: 0));
            // 15 at 0.8: 12 stay, so exactly 3 go — the second scout slot stays empty.
            Assert.IsTrue(Pick(Roster(warriors: 4, scouts: 2, others: 9), 1, out List<MemberRecord> members, warriorsHomeMin: 0));
            Assert.AreEqual(3, members.Count);
        }

        [Test]
        public void PickMembers_SkipsAwayRestingAndChildren()
        {
            var roster = new List<RosterEntry>
            {
                Entry(0, ExpeditionRole.Warrior), Entry(1, ExpeditionRole.Warrior), Entry(2, ExpeditionRole.Warrior, adult: false),
                Entry(3, ExpeditionRole.Warrior), Entry(4, ExpeditionRole.Warrior), Entry(5, ExpeditionRole.Warrior),
                Entry(6, ExpeditionRole.Scout), Entry(7, ExpeditionRole.Scout), Entry(8, ExpeditionRole.Scout, adult: false),
            };
            for (int i = 0; i < 40; i++) roster.Add(Entry(roster.Count, ExpeditionRole.None));
            var away = new HashSet<string> { ResidentKey.ForAuthored(0), ResidentKey.ForAuthored(6) };
            var rest = new Dictionary<string, int>
            {
                [ResidentKey.ForAuthored(1)] = Today + 1,   // still resting
                [ResidentKey.ForAuthored(5)] = Today,       // rested: free again today
            };
            var never = new HashSet<string> { "r:0", "r:1", "r:2", "r:6", "r:8" };

            var warriorsSeen = new HashSet<string>();
            for (int seed = 0; seed < 100; seed++)
            {
                Assert.IsTrue(Pick(roster, seed, out List<MemberRecord> members, away, rest, warriorsHomeMin: 0), $"seed {seed}");
                foreach (MemberRecord m in members)
                {
                    Assert.IsFalse(never.Contains(m.residentKey), $"seed {seed} took {m.residentKey}");
                    warriorsSeen.Add(m.residentKey);
                }
            }
            Assert.IsTrue(warriorsSeen.Contains("r:5"), "a resident whose rest ends today is free again");
        }

        [Test]
        public void PickMembers_PrefersBondsOfFirstPick()
        {
            // Warriors in bonded pairs (0-1, 2-3, ...), every warrior also bonded to scout 15.
            var roster = new List<RosterEntry>();
            for (int i = 0; i < 10; i++) roster.Add(Entry(i, ExpeditionRole.Warrior, true, i ^ 1, 15));
            for (int i = 10; i < 20; i++) roster.Add(Entry(i, ExpeditionRole.Scout));
            for (int i = 20; i < 70; i++) roster.Add(Entry(i, ExpeditionRole.None));

            for (int seed = 0; seed < 50; seed++)
            {
                Assert.IsTrue(Pick(roster, seed, out List<MemberRecord> members), $"seed {seed}");

                int first = int.Parse(members[0].residentKey.Substring(2));
                Assert.AreEqual(ResidentKey.ForAuthored(first ^ 1), members[1].residentKey, $"seed {seed}: the second warrior is the first pick's partner");
                Assert.IsTrue(members.Any(m => m.residentKey == "r:15"), $"seed {seed}: the first pick's bonded scout comes along");
            }
        }

        [Test]
        public void PickMembers_LeaderIsAWarrior()
        {
            List<RosterEntry> roster = Roster(warriors: 10, scouts: 5);

            var leaderPositions = new HashSet<int>();
            for (int seed = 0; seed < 50; seed++)
            {
                Assert.IsTrue(Pick(roster, seed, out List<MemberRecord> members), $"seed {seed}");

                List<MemberRecord> leaders = members.Where(m => m.isLeader).ToList();
                Assert.AreEqual(1, leaders.Count, $"seed {seed}: exactly one leader");
                Assert.AreEqual(ExpeditionRole.Warrior, RolesOf(roster, leaders[0]), $"seed {seed}");
                leaderPositions.Add(members.IndexOf(leaders[0]));
            }
            Assert.AreEqual(2, leaderPositions.Count, "the leader is drawn, not always the first warrior picked");
        }

        [Test]
        public void PickMembers_StableForSeed()
        {
            List<RosterEntry> roster = Roster(warriors: 10, scouts: 5);
            List<RosterEntry> reversed = Enumerable.Reverse(roster).ToList();

            var bands = new HashSet<string>();
            for (int seed = 0; seed < 20; seed++)
            {
                Pick(roster, seed, out List<MemberRecord> a);
                Pick(roster, seed, out List<MemberRecord> b);
                Pick(reversed, seed, out List<MemberRecord> c);

                string band = Describe(a);
                Assert.AreEqual(band, Describe(b), $"seed {seed}: same seed, same band");
                Assert.AreEqual(band, Describe(c), $"seed {seed}: the roster's order does not matter");
                bands.Add(band);
            }
            Assert.Greater(bands.Count, 1, "twenty seeds raised the same band");
        }

        private static string Describe(IEnumerable<MemberRecord> members) =>
            string.Join(",", members.Select(m => m.residentKey + (m.isLeader ? "*" : "")));

        // ---- Stand-ins for a role (RoleSlot.fillFromAnyAdult) -----------------------------------------------

        private static readonly RoleSlot[] ScoutSlotsWithStandIns =
        {
            new RoleSlot { role = ExpeditionRole.Warrior, min = 2, max = 2 },
            new RoleSlot { role = ExpeditionRole.Scout, min = 1, max = 2, fillFromAnyAdult = true },
        };

        private static bool PickWithStandIns(IReadOnlyList<RosterEntry> roster, int seed, out List<MemberRecord> members,
                                             int warriorsHomeMin = 6) =>
            ExpeditionRules.TryPickMembers(ScoutSlotsWithStandIns, KitOf, roster, new HashSet<string>(), new Dictionary<string, int>(),
                Today, warriorsHomeMin, 0.8f, seed, out members);

        [Test]
        public void PickMembers_EveryMemberPlaysItsSlotRole()
        {
            List<RosterEntry> roster = Roster(warriors: 10, scouts: 5);

            Assert.IsTrue(Pick(roster, 3, out List<MemberRecord> members));
            foreach (MemberRecord m in members)
                Assert.AreEqual(RolesOf(roster, m), m.role, "a holder plays its own role");
        }

        [Test]
        public void PickMembers_StandInFillsAScoutSlot_OnlyWhenNoScoutIsFree()
        {
            List<RosterEntry> noScouts = Roster(warriors: 10, scouts: 0);
            Assert.IsFalse(Pick(noScouts, 1, out _), "without the flag a slot takes only its role");

            for (int seed = 0; seed < 50; seed++)
            {
                Assert.IsTrue(PickWithStandIns(noScouts, seed, out List<MemberRecord> members), $"seed {seed}");

                List<MemberRecord> scouts = members.Where(m => m.role == ExpeditionRole.Scout).ToList();
                Assert.AreEqual(1, scouts.Count, $"seed {seed}: stand-ins fill the minimum only, never up to the maximum");
                Assert.AreEqual(ExpeditionRole.None, RolesOf(noScouts, scouts[0]) & ExpeditionRole.Scout, "it is no scout itself");
                Assert.AreEqual(1, scouts[0].kitIndex, "it draws the scout's kit");
                Assert.AreEqual(noScouts.First(e => e.residentKey == scouts[0].residentKey).archetypeIndex, scouts[0].archetypeIndex,
                                "it keeps its own archetype");
                Assert.AreEqual(2, members.Count(m => m.role == ExpeditionRole.Warrior));
            }
        }

        [Test]
        public void PickMembers_ScoutsArePreferredOverStandIns()
        {
            List<RosterEntry> roster = Roster(warriors: 10, scouts: 1);
            string scout = ResidentKey.ForAuthored(10);

            for (int seed = 0; seed < 50; seed++)
            {
                Assert.IsTrue(PickWithStandIns(roster, seed, out List<MemberRecord> members), $"seed {seed}");

                List<MemberRecord> scouts = members.Where(m => m.role == ExpeditionRole.Scout).ToList();
                Assert.AreEqual(1, scouts.Count, $"seed {seed}: the one scout meets the minimum, so nobody stands in");
                Assert.AreEqual(scout, scouts[0].residentKey, $"seed {seed}");
            }
        }

        [Test]
        public void PickMembers_StandInsKeepTheWarriorsHomeMinimum()
        {
            // Eight warriors (six must stay home), two adults with no role, and children: the Warrior slot takes the two
            // warriors who may go, so the stand-in can only be one of the two adults.
            var roster = new List<RosterEntry>();
            for (int i = 0; i < 8; i++) roster.Add(Entry(roster.Count, ExpeditionRole.Warrior));
            roster.Add(Entry(roster.Count, ExpeditionRole.None));
            roster.Add(Entry(roster.Count, ExpeditionRole.None));
            for (int i = 0; i < 30; i++) roster.Add(Entry(roster.Count, ExpeditionRole.Hunter, adult: false));

            for (int seed = 0; seed < 50; seed++)
            {
                Assert.IsTrue(PickWithStandIns(roster, seed, out List<MemberRecord> members), $"seed {seed}");
                Assert.AreEqual(2, members.Count(m => (RolesOf(roster, m) & ExpeditionRole.Warrior) != 0), $"seed {seed}: no third warrior");
                Assert.AreEqual(ExpeditionRole.None, RolesOf(roster, members.Single(m => m.role == ExpeditionRole.Scout)));
            }

            var warriorsOnly = new List<RosterEntry>();
            for (int i = 0; i < 8; i++) warriorsOnly.Add(Entry(warriorsOnly.Count, ExpeditionRole.Warrior));
            for (int i = 0; i < 30; i++) warriorsOnly.Add(Entry(warriorsOnly.Count, ExpeditionRole.Hunter, adult: false));
            Assert.IsFalse(PickWithStandIns(warriorsOnly, 1, out List<MemberRecord> none),
                           "the only free adults are warriors, and a third would leave 5 home");
            Assert.IsEmpty(none);
        }

        [Test]
        public void PickMembers_StandInsAreStableForSeed()
        {
            List<RosterEntry> roster = Roster(warriors: 10, scouts: 0);
            List<RosterEntry> reversed = Enumerable.Reverse(roster).ToList();

            var bands = new HashSet<string>();
            for (int seed = 0; seed < 20; seed++)
            {
                PickWithStandIns(roster, seed, out List<MemberRecord> a);
                PickWithStandIns(roster, seed, out List<MemberRecord> b);
                PickWithStandIns(reversed, seed, out List<MemberRecord> c);

                Assert.AreEqual(Describe(a), Describe(b), $"seed {seed}: same seed, same stand-in");
                Assert.AreEqual(Describe(a), Describe(c), $"seed {seed}: the roster's order does not matter");
                bands.Add(Describe(a));
            }
            Assert.Greater(bands.Count, 1, "twenty seeds took the same stand-in");
        }

        // ---- Stages and time ------------------------------------------------------------------------------

        [Test]
        public void BuildStages_CountsWithinRanges()
        {
            var specs = new[]
            {
                new StageSpec { kind = StageKind.Travel },
                new StageSpec { kind = StageKind.Search, minutesRange = new Vector2(60f, 180f), countRange = new Vector2Int(2, 4) },
                new StageSpec { kind = StageKind.Halt, countRange = new Vector2Int(5, 9) },
                new StageSpec { kind = StageKind.ReturnHome },
            };

            var counts = new HashSet<int>();
            for (int seed = 0; seed < 300; seed++)
            {
                StageRecord[] stages = ExpeditionRules.BuildStages(specs, seed);

                CollectionAssert.AreEqual(specs.Select(s => s.kind), stages.Select(s => s.kind), "one stage per spec, in order");
                Assert.AreEqual(StageRecord.NoLimit, stages[0].minutes, "0..0 minutes is no limit");
                Assert.That(stages[1].minutes, Is.InRange(60f, 180f));
                Assert.That(stages[1].waypoints, Is.InRange(2, 4));
                Assert.AreEqual(0, stages[2].waypoints, "only a Search has waypoints");
                Assert.AreEqual(stages[1].minutes, ExpeditionRules.BuildStages(specs, seed)[1].minutes, "same seed, same rolls");
                counts.Add(stages[1].waypoints);
            }
            CollectionAssert.AreEquivalent(new[] { 2, 3, 4 }, counts, "both ends of the count range are reachable");
        }

        [Test]
        public void IsTravelHour_DawnToDusk()
        {
            const float Dusk = 19f;
            foreach (int day in new[] { 0, 3, 400 })
            {
                Assert.IsFalse(ExpeditionRules.IsTravelHour(At(day, 5.99f), Dawn, Dusk), $"day {day} before dawn");
                Assert.IsTrue(ExpeditionRules.IsTravelHour(At(day, 6f), Dawn, Dusk), $"day {day} at dawn");
                Assert.IsTrue(ExpeditionRules.IsTravelHour(At(day, 12f), Dawn, Dusk));
                Assert.IsTrue(ExpeditionRules.IsTravelHour(At(day, 18.99f), Dawn, Dusk));
                Assert.IsFalse(ExpeditionRules.IsTravelHour(At(day, 19f), Dawn, Dusk), $"day {day} at dusk");
                Assert.IsFalse(ExpeditionRules.IsTravelHour(At(day, 23.5f), Dawn, Dusk));
            }
            Assert.IsTrue(ExpeditionRules.IsTravelHour(At(1, 23f), 20f, 4f), "a window across midnight");
            Assert.IsTrue(ExpeditionRules.IsTravelHour(At(1, 2f), 20f, 4f));
            Assert.IsFalse(ExpeditionRules.IsTravelHour(At(1, 12f), 20f, 4f));
        }

        /// <summary>Game minutes (DayNightCycle.GameMinutesNow) at <paramref name="hour"/> of <paramref name="day"/>.</summary>
        private static double At(int day, float hour) => day * 1440d + hour * 60d;

        // ---- Advancing a band -----------------------------------------------------------------------------

        private const float Dawn = 6f;

        private static ExpeditionRecord Band(params StageRecord[] stages) => new ExpeditionRecord { phase = ExpeditionPhase.Out, stages = stages };

        private static StageRecord Stage(StageKind kind, float minutes = StageRecord.NoLimit, int waypoints = 0) =>
            new StageRecord { kind = kind, minutes = minutes, waypoints = waypoints };

        [Test]
        public void Advance_SettlesSeveralStagesAfterClockJump()
        {
            ExpeditionRecord band = Band(Stage(StageKind.Travel), Stage(StageKind.Search, 120f, 3), Stage(StageKind.Halt),
                                         Stage(StageKind.Search, 60f, 2), Stage(StageKind.ReturnHome));

            Assert.AreEqual(StageStep.SetTarget, ExpeditionRules.Advance(band, At(1, 10f), 0d, false, Dawn), "the trip begins with its first Travel");
            Assert.AreEqual(0, band.stageIndex);
            Assert.AreEqual(StageStep.Continue, ExpeditionRules.Advance(band, At(1, 16f), 360d, false, Dawn), "a Travel ends only on arrival");
            Assert.AreEqual(StageStep.SetTarget, ExpeditionRules.Advance(band, At(1, 17f), 60d, true, Dawn), "arrived: the Search begins");
            Assert.AreEqual(1, band.stageIndex);
            Assert.AreEqual(120f, band.stageMinutesLeft);

            // Day 1 17:00 to day 3 07:30 in one call: the Search times out at 19:00, the Halt ends at dawn on
            // day 2, the second Search times out at 07:00, and the band has been heading home since.
            double now = At(3, 7.5f);
            StageStep step = ExpeditionRules.Advance(band, now, now - At(1, 17f), false, Dawn);

            Assert.AreEqual(StageStep.Home, step);
            Assert.AreEqual(4, band.stageIndex);
            Assert.AreEqual(StageRecord.NoLimit, band.stageMinutesLeft);

            Assert.AreEqual(StageStep.Arrive, ExpeditionRules.Advance(band, now + 30d, 30d, true, Dawn), "arrived at the hand-off point");
            Assert.AreEqual(band.stages.Length, band.stageIndex, "the trip is over");
            Assert.AreEqual(StageStep.Continue, ExpeditionRules.Advance(band, now + 60d, 30d, true, Dawn), "an ended trip does nothing");
        }

        [Test]
        public void Advance_HaltHoldsUntilDawn()
        {
            ExpeditionRecord band = Band(Stage(StageKind.Halt), Stage(StageKind.Travel), Stage(StageKind.ReturnHome));

            double now = At(2, 20f);
            Assert.AreEqual(StageStep.Hold, ExpeditionRules.Advance(band, now, 0d, false, Dawn), "the trip begins with a Halt");
            while (now + 30d < At(3, Dawn))
            {
                now += 30d;
                Assert.AreEqual(StageStep.Continue, ExpeditionRules.Advance(band, now, 30d, true, Dawn), $"still night at {now % 1440d / 60d:0.0} h; arrival means nothing to a Halt");
                Assert.AreEqual(0, band.stageIndex);
            }
            Assert.AreEqual(StageStep.SetTarget, ExpeditionRules.Advance(band, At(3, Dawn), At(3, Dawn) - now, false, Dawn), "dawn: the band moves on");
            Assert.AreEqual(1, band.stageIndex);

            ExpeditionRecord early = Band(Stage(StageKind.Halt), Stage(StageKind.ReturnHome));
            ExpeditionRules.Advance(early, At(4, 3f), 0d, false, Dawn);
            Assert.AreEqual(180f, early.stageMinutesLeft, 1e-3f, "a Halt begun after midnight ends at that morning's dawn");

            ExpeditionRecord capped = Band(Stage(StageKind.Halt, 60f), Stage(StageKind.ReturnHome));
            ExpeditionRules.Advance(capped, At(4, 20f), 0d, false, Dawn);
            Assert.AreEqual(60f, capped.stageMinutesLeft, 1e-3f, "a Halt with its own timer ends at whichever comes first");
        }

        [Test]
        public void Advance_SearchMovesOnPerWaypointThenEnds()
        {
            ExpeditionRecord band = Band(Stage(StageKind.Search, StageRecord.NoLimit, 3), Stage(StageKind.ReturnHome));

            Assert.AreEqual(StageStep.SetTarget, ExpeditionRules.Advance(band, At(1, 8f), 0d, false, Dawn));
            Assert.AreEqual(StageStep.SetTarget, ExpeditionRules.Advance(band, At(1, 9f), 60d, true, Dawn), "waypoint 1 reached: aim at the next");
            Assert.AreEqual(1, band.waypointsDone);
            Assert.AreEqual(StageStep.Continue, ExpeditionRules.Advance(band, At(1, 10f), 60d, false, Dawn));
            Assert.AreEqual(StageStep.SetTarget, ExpeditionRules.Advance(band, At(1, 11f), 60d, true, Dawn));
            Assert.AreEqual(StageStep.Home, ExpeditionRules.Advance(band, At(1, 12f), 60d, true, Dawn), "the last waypoint ends the Search");
            Assert.AreEqual(1, band.stageIndex);
        }

        [Test]
        public void StageEndsUntil_CountsTheStageEndsThatReachAKind()
        {
            ExpeditionRecord band = Band(Stage(StageKind.Travel), Stage(StageKind.Search, 600f, 3), Stage(StageKind.Halt, 120f),
                                         Stage(StageKind.ReturnHome));

            Assert.AreEqual(4, ExpeditionRules.StageEndsUntil(band, StageKind.ReturnHome), "not started: the first end begins stage 1");
            band.stageIndex = 1;
            Assert.AreEqual(2, ExpeditionRules.StageEndsUntil(band, StageKind.ReturnHome));
            band.stageIndex = 3;
            Assert.AreEqual(0, ExpeditionRules.StageEndsUntil(band, StageKind.ReturnHome), "already on it");
            Assert.AreEqual(-1, ExpeditionRules.StageEndsUntil(band, StageKind.Halt), "none ahead");
            band.stageIndex = band.stages.Length;
            Assert.AreEqual(-1, ExpeditionRules.StageEndsUntil(band, StageKind.ReturnHome), "the trip is over");
        }

        [Test]
        public void StageEndsUntil_IsWhatEndingStagesTakes()
        {
            // The director's AdvanceStage ends a stage by zeroing its timer and advancing with no time passed.
            ExpeditionRecord band = Band(Stage(StageKind.Travel), Stage(StageKind.Search, 600f, 3), Stage(StageKind.Halt, 120f),
                                         Stage(StageKind.ReturnHome));
            int ends = ExpeditionRules.StageEndsUntil(band, StageKind.ReturnHome);

            for (int i = 0; i < ends; i++)
            {
                if (band.stageIndex >= 0) band.stageMinutesLeft = 0f;
                ExpeditionRules.Advance(band, At(2, 10f), 0d, false, Dawn);
            }

            Assert.AreEqual(StageKind.ReturnHome, band.stages[band.stageIndex].kind);
        }

        // ---- Search ring, muster, hand-off ----------------------------------------------------------------

        [Test]
        public void SearchRing_PointsLieWithinTheRing()
        {
            var centre = new Vector3(100f, 20f, -50f);
            var ring = new Vector2(150f, 300f);

            IReadOnlyList<Vector3> points = ExpeditionRules.SearchRing(centre, 5, 9, ring);

            Assert.AreEqual(5, points.Count);
            foreach (Vector3 p in points)
            {
                Vector3 offset = p - centre;
                Assert.AreEqual(0f, offset.y, "unsampled: at the centre's height");
                Assert.That(new Vector2(offset.x, offset.z).magnitude, Is.InRange(ring.x - 1e-3f, ring.y + 1e-3f));
            }
            CollectionAssert.AreEqual(points, ExpeditionRules.SearchRing(centre, 5, 9, ring), "same seed, same ring");
            CollectionAssert.AreNotEqual(points, ExpeditionRules.SearchRing(centre, 5, 10, ring));
        }

        /// <summary>A pose turned <paramref name="yawDegrees"/> about +Y, built without the native Quaternion.Euler.</summary>
        private static Pose Muster(Vector3 at, float yawDegrees)
        {
            float half = yawDegrees * Mathf.Deg2Rad * 0.5f;
            return new Pose(at, new Quaternion(0f, Mathf.Sin(half), 0f, Mathf.Cos(half)));
        }

        [Test]
        public void MusterRow_IsCentredAcrossTheSpotFacingOut()
        {
            Pose muster = Muster(new Vector3(10f, 2f, 5f), 90f);   // facing +X, so the row runs along Z

            Pose[] row = ExpeditionRules.MusterRow(muster, 3, 1.2f);

            Assert.AreEqual(3, row.Length);
            Assert.That(Vector3.Distance(row[1].position, muster.position), Is.LessThan(1e-4f), "an odd row stands its middle on the spot");
            Assert.That(Vector3.Distance(row[0].position, row[2].position), Is.EqualTo(2.4f).Within(1e-4f));
            foreach (Pose stand in row)
            {
                Assert.That(Mathf.Abs(stand.position.x - 10f), Is.LessThan(1e-4f), "across the spot's facing, not along it");
                Assert.AreEqual(muster.rotation, stand.rotation, "everyone faces out");
            }

            Pose[] even = ExpeditionRules.MusterRow(muster, 4, 1.2f);
            Vector3 middle = (even[0].position + even[3].position) * 0.5f;
            Assert.That(Vector3.Distance(middle, muster.position), Is.LessThan(1e-4f), "an even row is centred on the spot too");
            Assert.IsEmpty(ExpeditionRules.MusterRow(muster, 0, 1.2f));
        }

        [Test]
        public void RoadPoint_LiesOutAlongTheMusterFacing()
        {
            Vector3 road = ExpeditionRules.RoadPoint(Muster(new Vector3(10f, 2f, 5f), 90f), 150f);

            Assert.That(Vector3.Distance(new Vector3(160f, 2f, 5f), road), Is.LessThan(1e-3f));
        }

        [Test]
        public void ShouldHandOff_UnobservedPastDepartRadius()
        {
            Assert.IsFalse(ExpeditionRules.ShouldHandOff(149f, false, 150f, 400f));
            Assert.IsTrue(ExpeditionRules.ShouldHandOff(150f, false, 150f, 400f));
            Assert.IsTrue(ExpeditionRules.ShouldHandOff(260f, false, 150f, 400f));
        }

        [Test]
        public void ShouldHandOff_ObservedOnlyAtMaxDistance()
        {
            Assert.IsFalse(ExpeditionRules.ShouldHandOff(150f, true, 150f, 400f));
            Assert.IsFalse(ExpeditionRules.ShouldHandOff(399f, true, 150f, 400f));
            Assert.IsTrue(ExpeditionRules.ShouldHandOff(400f, true, 150f, 400f));
        }

        [Test]
        public void WalkOutPoint_LiesOnTheRoadThroughTheHandOffPoint()
        {
            Pose muster = Muster(new Vector3(10f, 2f, 5f), 0f);   // facing +Z
            var handoff = new Vector3(160f, 7f, 5f);              // the record's road leaves along +X

            Vector3 point = ExpeditionRules.WalkOutPoint(muster, handoff, 415f);

            Assert.That(Vector3.Distance(new Vector3(425f, 2f, 5f), point), Is.LessThan(1e-3f),
                        "along the record's road, past the hand-off point, at the spot's height");
        }

        [Test]
        public void WalkOutPoint_FollowsTheSpotFacing_WhenTheHandOffPointIsTheSpot()
        {
            Pose muster = Muster(new Vector3(10f, 2f, 5f), 90f);   // facing +X

            Vector3 point = ExpeditionRules.WalkOutPoint(muster, muster.position, 100f);

            Assert.That(Vector3.Distance(new Vector3(110f, 2f, 5f), point), Is.LessThan(1e-3f));
        }

        [Test]
        public void HandOffNow_IsShouldHandOff_OrTheWalkLimit()
        {
            Assert.IsFalse(ExpeditionRules.HandOffNow(149f, false, 239.0, 150f, 400f, 240f));
            Assert.IsTrue(ExpeditionRules.HandOffNow(150f, false, 0.0, 150f, 400f, 240f), "unobserved past departRadius");
            Assert.IsFalse(ExpeditionRules.HandOffNow(399f, true, 239.0, 150f, 400f, 240f), "watched short of the swap distance");
            Assert.IsTrue(ExpeditionRules.HandOffNow(400f, true, 0.0, 150f, 400f, 240f), "watched at the swap distance");
            Assert.IsTrue(ExpeditionRules.HandOffNow(20f, true, 240.0, 150f, 400f, 240f), "stuck and watched: the limit hands it off");
        }

        [Test]
        public void WalkedIn_AtTheMusterSpot_OrTheWalkLimit()
        {
            Assert.IsFalse(ExpeditionRules.WalkedIn(15.5f, 239.0, 15f, 240f));
            Assert.IsTrue(ExpeditionRules.WalkedIn(15f, 0.0, 15f, 240f));
            Assert.IsTrue(ExpeditionRules.WalkedIn(120f, 240.0, 15f, 240f), "stuck on the way: home where it stands");
        }

        [Test]
        public void Centroid_IsTheMeanPosition()
        {
            var poses = new[]
            {
                new Pose(new Vector3(0f, 0f, 0f), Quaternion.identity),
                new Pose(new Vector3(3f, 3f, 0f), Quaternion.identity),
                new Pose(new Vector3(0f, 0f, 6f), Quaternion.identity),
            };

            Assert.That(Vector3.Distance(new Vector3(1f, 1f, 2f), ExpeditionRules.Centroid(poses)), Is.LessThan(1e-5f));
            Assert.AreEqual(Vector3.zero, ExpeditionRules.Centroid(new Pose[0]));
        }

        [Test]
        public void FlatDistance_IgnoresHeight()
        {
            Assert.That(ExpeditionRules.FlatDistance(new Vector3(0f, 0f, 0f), new Vector3(3f, 50f, 4f)), Is.EqualTo(5f).Within(1e-5f));
        }

        [Test]
        public void EffectiveObserveRadius_NeverReachesPastWhereStandInsSpawn()
        {
            Assert.AreEqual(360f, ExpeditionRules.EffectiveObserveRadius(400f, 360f), "a watcher 360–400 m off would see the residents vanish");
            Assert.AreEqual(300f, ExpeditionRules.EffectiveObserveRadius(300f, 360f), "a smaller observe radius stands");

            float radius = ExpeditionRules.EffectiveObserveRadius(400f, 360f);
            Assert.IsTrue(ExpeditionRules.ShouldHandOff(150f, 380f <= radius, 150f, 400f),
                          "a band watched from 380 m is handed off past departRadius, as if unwatched");
            Assert.IsFalse(ExpeditionRules.ShouldHandOff(150f, 350f <= radius, 150f, 400f),
                           "a band watched from 350 m walks on to the swap distance");
        }

        // ---- Legs of a band with stand-ins -------------------------------------------------------------------

        [Test]
        public void LegPoint_IsNeverLongerThanTheLimit_AndHeadsForTheTarget()
        {
            var from = new Vector3(10f, 5f, -20f);
            var target = new Vector3(910f, 45f, 1180f);   // 1500 m away on the ground plane
            const float Limit = 120f;

            Vector3 leg = ExpeditionRules.LegPoint(from, target, Limit);

            Assert.That(ExpeditionRules.FlatDistance(from, leg), Is.EqualTo(Limit).Within(1e-3f), "a full leg, no longer");
            Vector3 toTarget = new Vector3(target.x - from.x, 0f, target.z - from.z).normalized;
            Vector3 toLeg = new Vector3(leg.x - from.x, 0f, leg.z - from.z).normalized;
            Assert.That(Vector3.Dot(toTarget, toLeg), Is.EqualTo(1f).Within(1e-5f), "straight toward the target");
            Assert.That(leg.y, Is.InRange(from.y, target.y), "its height lies between the two");
        }

        [Test]
        public void LegPoint_LastLegIsTheTargetItself()
        {
            var target = new Vector3(100f, 3f, 100f);

            Assert.AreEqual(target, ExpeditionRules.LegPoint(new Vector3(30f, 0f, 100f), target, 120f), "70 m off: the target");
            Assert.AreEqual(target, ExpeditionRules.LegPoint(new Vector3(-20f, 0f, 100f), target, 120f), "exactly the limit: the target");
            Assert.AreEqual(target, ExpeditionRules.LegPoint(new Vector3(-900f, 0f, 100f), target, 0f), "no limit: the target");
        }

        [Test]
        public void LegPoint_WalkedLegByLeg_ReachesTheTargetWithinTheLimitEachTime()
        {
            var position = new Vector3(0f, 0f, 0f);
            var target = new Vector3(-700f, 12f, 830f);   // 1085.8 m away on the ground plane
            const float Limit = 120f;

            int legs = 0;
            while (position != target)
            {
                Vector3 leg = ExpeditionRules.LegPoint(position, target, Limit);
                Assert.That(ExpeditionRules.FlatDistance(position, leg), Is.LessThanOrEqualTo(Limit + 1e-3f), $"leg {legs}");
                position = leg;
                Assert.Less(++legs, 20, "the legs never reach the target");
            }
            Assert.AreEqual(10, legs, "nine full legs of 120 m and the last one");
        }

        // ---- Finished bands --------------------------------------------------------------------------------

        private static ExpeditionRecord FinishedWithDead(params string[] deadKeys) => new ExpeditionRecord
        {
            phase = ExpeditionPhase.Home,
            members = deadKeys.Select(k => new MemberRecord { residentKey = k, dead = true })
                              .Append(new MemberRecord { residentKey = "r:1" }).ToArray(),
        };

        private static RosterEntry[] Roster(params string[] keys) => keys.Select(k => new RosterEntry { residentKey = k }).ToArray();

        [Test]
        public void MayRetire_WhenEveryDeadMemberWasFound()
        {
            ExpeditionRecord band = FinishedWithDead("r:4", "r:9");

            Assert.IsTrue(ExpeditionRules.MayRetire(band, key => true, Roster("r:1", "r:4", "r:9")));
        }

        [Test]
        public void MayRetire_KeepsTheBand_WhileTheRosterStillListsAnUnfoundDeadMember()
        {
            ExpeditionRecord band = FinishedWithDead("r:4", "r:9");

            Assert.IsFalse(ExpeditionRules.MayRetire(band, key => key != "r:9", Roster("r:1", "r:4", "r:9")),
                           "r:9's body may turn up on a later load, which must still apply its death");
        }

        [Test]
        public void MayRetire_WhenAnUnfoundDeadMemberIsNotOnTheRoster()
        {
            ExpeditionRecord band = FinishedWithDead("r:4", "r:9");

            Assert.IsTrue(ExpeditionRules.MayRetire(band, key => key != "r:9", Roster("r:1", "r:4")), "r:9 is gone with its resident");
            Assert.IsTrue(ExpeditionRules.MayRetire(band, key => false, null), "no roster lists anybody");
        }

        [Test]
        public void MayRetire_IgnoresLivingMembersNotFound()
        {
            ExpeditionRecord band = FinishedWithDead();

            Assert.IsTrue(ExpeditionRules.MayRetire(band, key => false, Roster("r:1")), "only a death is waited for");
        }

        // ---- Records --------------------------------------------------------------------------------------

        [Test]
        public void Records_RoundTripThroughSaveSerializer()
        {
            var band = new ExpeditionRecord
            {
                id = "exp:s1:3", settlementId = "s1", goalId = "scout", seed = -12345, phase = ExpeditionPhase.Out,
                stages = new[] { Stage(StageKind.Search, 90f, 3), Stage(StageKind.ReturnHome) },
                stageIndex = 1, stageMinutesLeft = 42.5f, waypointsDone = 2, departDay = 7, groupId = "g:exp:s1:3",
                members = new[] { new MemberRecord { residentKey = "r:4", archetypeIndex = 2, kitIndex = 0, health01 = 0.25f, isLeader = true,
                                                     role = ExpeditionRole.Warrior },
                                  new MemberRecord { residentKey = "r:9", archetypeIndex = 5, kitIndex = 1, dead = true, role = ExpeditionRole.Scout } },
                handoffPoint = new Vector3(2995.7f, 114.3f, 385.8f),
            };
            var state = new SettlementState
            {
                settlementId = "s1", rotation = 3, lastGoalId = "scout",
                roster = new[] { Entry(4, ExpeditionRole.Warrior | ExpeditionRole.Hunter, true, 9) },
                restUntilDay = new Dictionary<string, int> { ["r:9"] = 12 },
            };

            ExpeditionRecord bandBack = JObject.FromObject(band, SaveSerializer.Serializer).ToObject<ExpeditionRecord>(SaveSerializer.Serializer);
            SettlementState stateBack = JObject.FromObject(state, SaveSerializer.Serializer).ToObject<SettlementState>(SaveSerializer.Serializer);

            Assert.AreEqual(JObject.FromObject(band, SaveSerializer.Serializer).ToString(), JObject.FromObject(bandBack, SaveSerializer.Serializer).ToString());
            Assert.AreEqual(band.handoffPoint, bandBack.handoffPoint);
            Assert.AreEqual(ExpeditionPhase.Out, bandBack.phase);
            Assert.AreEqual(0.25f, bandBack.members[0].health01);
            Assert.AreEqual(ExpeditionRole.Scout, bandBack.members[1].role);
            Assert.AreEqual(12, stateBack.restUntilDay["r:9"]);
            CollectionAssert.AreEqual(new[] { 9 }, stateBack.roster[0].bonds);
            Assert.AreEqual(ExpeditionRole.Warrior | ExpeditionRole.Hunter, stateBack.roster[0].roles);
        }
    }
}
