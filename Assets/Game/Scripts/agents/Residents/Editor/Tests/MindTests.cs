// The resident mind's pure rules: opening stances by temperament quadrant, the stance ladder and its line
// families, grudge expiry, the daily talk cap, the memory's save round-trip through Newtonsoft (the
// serializer the save file uses), observation priority, what temper and nerve make of provocation, word of
// mouth (news passed on however it was learned, once each, re-read through the listener's bond), bedtime and
// hearth gossip at the close of a day (and which day changes count as one), favor (a
// deed's full worth to its subject, a smaller share the further a listener stands from them), and how residents
// answer a fighter calling for help (the bold and the caller's bonded join, one hop per call; the timid stay out).
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.Agents.Residents.Tests
{
    public class MindTests
    {
        private const float WarmAt = 20f;
        private const float ColdAt = 20f;

        [TestCase(0.8f, 0.2f, Stance.Curious)]
        [TestCase(0.8f, 0.8f, Stance.Protective)]
        [TestCase(0.2f, 0.2f, Stance.Asking)]
        [TestCase(0.2f, 0.8f, Stance.Afraid)]
        [TestCase(0.6f, 0.4f, Stance.Unsure)]
        public void Opening_FollowsTheTemperamentQuadrant(float nerve, float temper, Stance expected) =>
            Assert.AreEqual(expected, Attitude.Opening(nerve, temper));

        [Test]
        public void AFight_IsHostile_EvenToAFriend()
        {
            var read = new PlayerRead();
            Assert.AreEqual(Stance.Hostile, Attitude.StanceFor(0.2f, 0.2f, 100f, false, in read, AggressionBand.Grudge, WarmAt, ColdAt));
        }

        [Test]
        public void AGrudge_MakesTheBoldCold_AndTheTimidAfraid()
        {
            var read = new PlayerRead();
            Assert.AreEqual(Stance.Cold, Attitude.StanceFor(0.8f, 0.2f, 100f, true, in read, AggressionBand.Calm, WarmAt, ColdAt));
            Assert.AreEqual(Stance.Afraid, Attitude.StanceFor(0.2f, 0.2f, 0f, false, in read, AggressionBand.Drawn, WarmAt, ColdAt));
        }

        [Test]
        public void RegardAtOrBelowMinusColdAt_IsCold_WithoutAGrudge()
        {
            var read = new PlayerRead();
            Assert.AreEqual(Stance.Cold, Attitude.StanceFor(0.8f, 0.2f, -ColdAt, false, in read, AggressionBand.Calm, WarmAt, ColdAt));
            Assert.AreEqual(Stance.Afraid, Attitude.StanceFor(0.2f, 0.2f, -ColdAt, false, in read, AggressionBand.Calm, WarmAt, ColdAt));
            Assert.AreEqual(Stance.Curious, Attitude.StanceFor(0.8f, 0.2f, -ColdAt + 1f, false, in read, AggressionBand.Calm, WarmAt, ColdAt),
                "a little lost favor leaves a resident as it opened");
        }

        [Test]
        public void Familiarity_PastWarmAt_IsWarm_EvenWhenArmed()
        {
            var read = new PlayerRead { armed = true };
            Assert.AreEqual(Stance.Warm, Attitude.StanceFor(0.2f, 0.8f, WarmAt, false, in read, AggressionBand.Calm, WarmAt, ColdAt));
        }

        [Test]
        public void AnArmedStranger_FrightensTheTimid_AndPutsTheBoldOnGuard()
        {
            var read = new PlayerRead { armed = true };
            Assert.AreEqual(Stance.Afraid, Attitude.StanceFor(0.2f, 0.2f, 0f, false, in read, AggressionBand.Calm, WarmAt, ColdAt));
            Assert.AreEqual(Stance.Protective, Attitude.StanceFor(0.8f, 0.2f, 0f, false, in read, AggressionBand.Calm, WarmAt, ColdAt));
        }

        [TestCase(Stance.Warm, StanceFamily.Friendly)]
        [TestCase(Stance.Curious, StanceFamily.Neutral)]
        [TestCase(Stance.Asking, StanceFamily.Neutral)]
        [TestCase(Stance.Unsure, StanceFamily.Neutral)]
        [TestCase(Stance.Protective, StanceFamily.Wary)]
        [TestCase(Stance.Afraid, StanceFamily.Wary)]
        [TestCase(Stance.Cold, StanceFamily.Wary)]
        [TestCase(Stance.Hostile, StanceFamily.Hostile)]
        public void EveryStance_HasItsFamily(Stance stance, StanceFamily family) =>
            Assert.AreEqual(family, Attitude.FamilyOf(stance));

        [Test]
        public void AGrudge_ExpiresAfterItsForgiveDays()
        {
            var memory = new ResidentMemory();
            memory.AddDeed("p", ActKind.Hit, victim: 1, day: 2, heldDays: 3f);

            Assert.IsTrue(memory.HoldsPersonalGrudge("p", 4));
            Assert.IsFalse(memory.HoldsPersonalGrudge("p", 5));

            memory.Expire(5);
            Assert.AreEqual(0, memory.Deeds.Count);
        }

        [Test]
        public void HarmOnlyHeardOf_IsPersonal_OnlyWhenItWasKin_AndKindnessNever()
        {
            var memory = new ResidentMemory();
            memory.AddDeed("stranger hit", ActKind.Hit, victim: 1, day: 2, heldDays: 3f, heard: true);
            memory.AddDeed("kin hit", ActKind.HarmedKin, victim: 1, day: 2, heldDays: 3f, heard: true);
            memory.AddDeed("defended", ActKind.Defended, victim: 1, day: 2, heldDays: 3f);

            Assert.IsFalse(memory.HoldsPersonalGrudge("stranger hit", 2));
            Assert.IsTrue(memory.HoldsPersonalGrudge("kin hit", 2));
            Assert.IsFalse(memory.HoldsPersonalGrudge("defended", 2));
        }

        [Test]
        public void ALethalGrudge_IsNeverForgiven()
        {
            var memory = new ResidentMemory();
            memory.AddDeed("p", ActKind.KilledKin, victim: 1, day: 2, heldDays: ResidentMemory.Never);

            memory.Expire(100000);
            Assert.IsTrue(memory.KinHarmedBy("p", 100000));
        }

        [Test]
        public void Talking_StopsCountingAtTheDailyCap_AndResetsNextDay()
        {
            var memory = new ResidentMemory();
            for (int i = 0; i < 12; i++) memory.Talked("p", day: 1, gain: 2f, dailyCap: 10);
            Assert.AreEqual(20f, memory.Familiarity("p"));

            memory.Talked("p", day: 2, gain: 2f, dailyCap: 10);
            Assert.AreEqual(22f, memory.Familiarity("p"));
            Assert.IsTrue(memory.Met("p"));
            Assert.IsFalse(memory.Met("stranger"));
        }

        [Test]
        public void Memory_RoundTripsThroughTheSaveSerializer()
        {
            var memory = new ResidentMemory();
            memory.Talked("p", day: 3, gain: 2f, dailyCap: 10);
            memory.AddDeed("p", ActKind.HarmedKin, victim: 4, day: 3, heldDays: 6f, heard: true);
            memory.ChangeFavor("p", value: -25f, share: 0.5f, limit: 100f);

            string json = JsonConvert.SerializeObject(memory.Capture());
            var restored = new ResidentMemory();
            restored.Restore(JsonConvert.DeserializeObject<ResidentMemory.MemoryState>(json));

            Assert.AreEqual(2f, restored.Familiarity("p"));
            Assert.IsTrue(restored.KinHarmedBy("p", 8));
            Assert.IsTrue(restored.Deeds[0].heard);
            Assert.AreEqual(-12.5f, restored.FavorOf("p"));
            Assert.AreEqual(-10.5f, restored.Regard("p"), "regard is familiarity plus favor");

            restored.Restore(null);
            Assert.IsFalse(restored.Met("p"));
        }

        [Test]
        public void Memory_LoadsASaveThatStillHoldsKnownDeaths()
        {
            const string json = "{\"favor\":[{\"profile\":\"p\",\"favor\":-5}],\"knownDeaths\":[{\"victim\":7,\"day\":3}]}";
            var restored = new ResidentMemory();
            restored.Restore(JObject.Parse(json).ToObject<ResidentMemory.MemoryState>(SaveSerializer.Serializer));

            Assert.AreEqual(-5f, restored.FavorOf("p"), "the retired knownDeaths key is ignored, the rest still loads");
        }

        [Test]
        public void Observation_FollowsTheFixedPriority()
        {
            var read = new PlayerRead
            {
                recentHits = 1, menacing = true, armed = true, gauntlet = true, curio = true, sprinting = true,
            };
            Assert.AreEqual(Observation.KinHarmed, read.Observe(kinHarmed: true));
            Assert.AreEqual(Observation.Hitting, read.Observe(false));
            read.recentHits = 0;
            Assert.AreEqual(Observation.Menacing, read.Observe(false));
            read.menacing = false;
            Assert.AreEqual(Observation.ArmedHeld, read.Observe(false));
            read.armed = false;
            Assert.AreEqual(Observation.Gauntlet, read.Observe(false));
            read.gauntlet = false;
            Assert.AreEqual(Observation.Curio, read.Observe(false));
            read.curio = false;
            Assert.AreEqual(Observation.Sprinting, read.Observe(false));
            read.sprinting = false;
            Assert.AreEqual(Observation.Approaching, read.Observe(false));
        }

        [TestCase(0f, 3)]
        [TestCase(1f, 2)]
        public void Temper_PicksJostlesToFight_AndABumpIsNeverAFight(float temper, int expected)
        {
            var go = new GameObject("resident");
            try
            {
                Resident resident = go.AddComponent<Resident>();
                resident.temperOverride = temper;
                Assert.AreEqual(expected, resident.JostlesToFight);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [TestCase(0.9f, true)]
        [TestCase(0.1f, false)]
        public void Nerve_DecidesWhetherAnAllysAlertAloneIsAFight(float nerve, bool joins)
        {
            var settlement = new TestSettlement();
            try
            {
                Resident resident = NewResident(settlement.transform, nerve, out ProvocationModule provocation);

                resident.ApplyDerivedTuning();

                Assert.AreEqual(joins, resident.JoinsAlliesFights);
                Assert.AreEqual(joins ? provocation.Settings.attackAt : 0f, provocation.Settings.allyHurtGain,
                    joins ? "the first alert fills the meter" : "the timid leave the meter alone and go home");
                Assert.Greater(provocation.Settings.jostlesToFight, 1);
            }
            finally
            {
                settlement.Dispose();
            }
        }

        [Test]
        public void ACaravanCopyOfAResidentPrefab_KeepsThePrefabsOwnTemperament()
        {
            var nomad = new GameObject("caravan");
            try
            {
                Resident resident = NewResident(nomad.transform, 0.9f, out ProvocationModule provocation);
                AggressionSettings authored = provocation.Settings;

                resident.ApplyDerivedTuning();

                Assert.AreEqual(authored.allyHurtGain, provocation.Settings.allyHurtGain);
                Assert.AreEqual(authored.jostlesToFight, provocation.Settings.jostlesToFight);
            }
            finally
            {
                Object.DestroyImmediate(nomad);
            }
        }

        [Test]
        public void WordOfMouth_PassesOnWhatWasOnlyHeard_OnceEach_ThroughTheListenersOwnBond()
        {
            var root = new TestSettlement();
            try
            {
                SettlementSociety settlement = root.Society;
                Resident victim = Plain(root.transform), witness = Plain(root.transform),
                         neighbour = Plain(root.transform), sister = Plain(root.transform);
                Resident[] all = { victim, witness, neighbour, sister };
                for (int i = 0; i < all.Length; i++) all[i].index = i;
                sister.bonds = new[] { new ResidentBond { other = victim.index, kind = BondKind.Family } };
                witness.Memory.AddDeed("p", ActKind.Hit, victim.index, day: 1, heldDays: 3f);

                var told = new List<ResidentMemory.Deed>();
                Gossip.Pass(settlement, witness, neighbour, 1, told);
                Gossip.Pass(settlement, neighbour, sister, 1, told);

                Assert.AreEqual(2, told.Count, "first hand to the neighbour, second hand on to the sister");
                Assert.IsTrue(neighbour.Memory.Holds("p", ActKind.Hit, victim.index, 1));
                Assert.IsTrue(sister.Memory.Holds("p", ActKind.HarmedKin, victim.index, 1), "the sister hears harm to her kin");
                Assert.IsTrue(sister.Memory.Deeds[0].heard);

                told.Clear();
                Gossip.Pass(settlement, witness, neighbour, 1, told);
                Gossip.Pass(settlement, witness, victim, 1, told);
                Assert.AreEqual(0, told.Count, "nobody is told twice, and the victim is never told about itself");
            }
            finally
            {
                root.Dispose();
            }
        }

        [Test]
        public void Favor_IsAllOfADeedForItsSubject_AndLessTheFurtherAListenerStandsFromThem()
        {
            var root = new TestSettlement();
            try
            {
                ResidentTuning tuning = ScriptableObject.CreateInstance<ResidentTuning>();
                Resident subject = Plain(root.transform), sister = Plain(root.transform), friend = Plain(root.transform),
                         coworker = Plain(root.transform), stranger = Plain(root.transform);
                Resident[] all = { subject, sister, friend, coworker, stranger };
                for (int i = 0; i < all.Length; i++) all[i].index = i;
                subject.bonds = new[] { new ResidentBond { other = sister.index, kind = BondKind.Family } };
                // A bond counts whichever of the two holds it.
                friend.bonds = new[]
                {
                    new ResidentBond { other = subject.index, kind = BondKind.Coworker },
                    new ResidentBond { other = subject.index, kind = BondKind.Friend },
                };
                coworker.bonds = new[] { new ResidentBond { other = subject.index, kind = BondKind.Coworker } };

                Assert.AreEqual(1f, Favor.ShareOf(subject, subject, tuning));
                Assert.AreEqual(tuning.familyShare, Favor.ShareOf(sister, subject, tuning));
                Assert.AreEqual(tuning.friendShare, Favor.ShareOf(friend, subject, tuning), "the closest of two bonds counts");
                Assert.AreEqual(tuning.coworkerShare, Favor.ShareOf(coworker, subject, tuning));
                Assert.AreEqual(tuning.neighbourShare, Favor.ShareOf(stranger, subject, tuning));
                Assert.Greater(tuning.familyShare, tuning.friendShare);
                Assert.Greater(tuning.friendShare, tuning.coworkerShare);
                Assert.Greater(tuning.coworkerShare, tuning.neighbourShare);
                Assert.AreEqual(tuning.favorLimit, Favor.Apply(tuning.favorLimit - 1f, tuning.favorForDefending, 1f, tuning.favorLimit));
                Object.DestroyImmediate(tuning);
            }
            finally
            {
                root.Dispose();
            }
        }

        [Test]
        public void GoodNews_SpreadsLikeBad_AndWarmsAFriendOfTheSubjectMoreThanAStranger()
        {
            var root = new TestSettlement();
            try
            {
                SettlementSociety settlement = root.Society;
                Resident subject = Plain(root.transform), witness = Plain(root.transform),
                         friend = Plain(root.transform), stranger = Plain(root.transform);
                Resident[] all = { subject, witness, friend, stranger };
                for (int i = 0; i < all.Length; i++) all[i].index = i;
                friend.bonds = new[] { new ResidentBond { other = subject.index, kind = BondKind.Friend } };
                witness.Memory.AddDeed("p", ActKind.Defended, subject.index, day: 1, heldDays: 3f);

                var told = new List<ResidentMemory.Deed>();
                Gossip.Pass(settlement, witness, friend, 1, told);
                Gossip.Pass(settlement, friend, stranger, 1, told);
                Gossip.Pass(settlement, witness, stranger, 1, told);

                ResidentTuning tuning = ResidentTuning.Instance;
                Assert.AreEqual(2, told.Count, "the stranger counts the deed once, however many tell it");
                Assert.AreEqual(tuning.favorForDefending * tuning.friendShare, friend.Memory.FavorOf("p"), 0.001f);
                Assert.AreEqual(tuning.favorForDefending * tuning.neighbourShare, stranger.Memory.FavorOf("p"), 0.001f);
                Assert.IsFalse(friend.Memory.HoldsPersonalGrudge("p", 1));
            }
            finally
            {
                root.Dispose();
            }
        }

        [TestCase(4, 5, true)]
        [TestCase(int.MinValue, 5, false)]
        [TestCase(5, 5, false)]
        [TestCase(2, 5, false)]
        [TestCase(6, 5, false)]
        public void OnlyANaturalDayChange_EndsADay(int builtDay, int today, bool ends) =>
            Assert.AreEqual(ends, SettlementSociety.EndsADay(builtDay, today),
                "the first build, a multi-day skip and a clock that went back are no evening");

        [Test]
        public void AtBedtime_FamilyHearWhatTheirKinSaw_AndNobodyElseDoes()
        {
            var root = new TestSettlement();
            try
            {
                SettlementSociety settlement = root.Society;
                Resident victim = Plain(root.transform), witness = Plain(root.transform),
                         sister = Plain(root.transform), neighbour = Plain(root.transform);
                Resident[] all = { victim, witness, sister, neighbour };
                for (int i = 0; i < all.Length; i++) all[i].index = i;
                witness.bonds = new[] { new ResidentBond { other = sister.index, kind = BondKind.Family } };
                witness.Memory.AddDeed("p", ActKind.Hit, victim.index, day: 1, heldDays: 3f);

                Gossip.SpreadAtBedtime(settlement, 1);

                Assert.IsTrue(sister.Memory.Holds("p", ActKind.Hit, victim.index, 1));
                Assert.IsTrue(sister.Memory.Deeds[0].heard, "told, not seen");
                Assert.Less(sister.Memory.FavorOf("p"), 0f, "hearing of the hit costs the player her favor");
                Assert.AreEqual(0, neighbour.Memory.Deeds.Count, "bedtime is for family");
            }
            finally
            {
                root.Dispose();
            }
        }

        [Test]
        public void AtTheHearth_FriendsWhoBothSatThere_TellEachOther()
        {
            const int day = 1;
            var root = new TestSettlement();
            try
            {
                SettlementSociety settlement = root.Society;
                Resident victim = Plain(root.transform), teller = Plain(root.transform),
                         companion = Plain(root.transform), absent = Plain(root.transform);
                Resident[] all = { victim, teller, companion, absent };
                for (int i = 0; i < all.Length; i++) all[i].index = i;
                teller.bonds = new[]
                {
                    new ResidentBond { other = companion.index, kind = BondKind.Friend },
                    new ResidentBond { other = absent.index, kind = BondKind.Friend },
                };
                teller.Memory.AddDeed("p", ActKind.Hit, victim.index, day, heldDays: 3f);
                SetPlans(settlement, day, Plan(teller, day, Activity.Hearth), Plan(companion, day, Activity.Hearth),
                         Plan(absent, day, Activity.Work), Plan(victim, day, Activity.Work));

                Gossip.SpreadAtHearth(settlement, day);

                Assert.IsTrue(companion.Memory.Holds("p", ActKind.Hit, victim.index, day));
                Assert.Less(companion.Memory.FavorOf("p"), 0f);
                Assert.AreEqual(0, absent.Memory.Deeds.Count, "a friend who was not at the hearth is not told there");
            }
            finally
            {
                root.Dispose();
            }
        }

        // One segment doing <paramref name="activity"/>: all a hearth check reads of a plan.
        private static DayPlan Plan(Resident resident, int day, Activity activity)
        {
            var plan = new DayPlan { residentIndex = resident.index, day = day };
            plan.segments.Add(new PlanSegment { activity = activity });
            return plan;
        }

        // Plans are built by the planner from places and a NavMesh; a test hands the society its day directly.
        private static void SetPlans(SettlementSociety settlement, int day, params DayPlan[] plans)
        {
            var byDay = (Dictionary<int, DayPlan[]>)typeof(SettlementSociety)
                .GetField("plansByDay", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settlement);
            byDay[day] = plans;
        }

        [Test]
        public void ACallForHelp_PullsInTheBoldAndTheCallersFriends_InRange_OneHopPerCall()
        {
            var root = new TestSettlement();
            var enemy = new GameObject("enemy");
            var people = ScriptableObject.CreateInstance<FactionDefinition>();
            people.ID = System.Guid.NewGuid().ToString("N");
            var table = ScriptableObject.CreateInstance<FactionRelationshipTable>();
            var roster = new List<Resident>();
            try
            {
                Resident caller = Fighter(root.transform, roster, people, table, nerve: 0.9f, new Vector3(0f, 0f, 0f));
                Resident bold = Fighter(root.transform, roster, people, table, nerve: 0.9f, new Vector3(15f, 0f, 0f));
                Resident friend = Fighter(root.transform, roster, people, table, nerve: 0.1f, new Vector3(0f, 0f, 15f));
                Resident timid = Fighter(root.transform, roster, people, table, nerve: 0.1f, new Vector3(-15f, 0f, 0f));
                Resident beyond = Fighter(root.transform, roster, people, table, nerve: 0.9f, new Vector3(30f, 0f, 0f));
                friend.bonds = new[] { new ResidentBond { other = caller.index, kind = BondKind.Friend } };
                Assert.IsFalse(caller.GetComponent<AlertBroadcaster>().CallForHelp(), "nobody calls before a fight");
                caller.Provocation.Provoke(enemy.transform, announce: false);

                Assert.IsTrue(caller.GetComponent<AlertBroadcaster>().CallForHelp());
                Assert.AreEqual(enemy.transform, bold.Provocation.Aggressor, "the bold answer the first call");
                Assert.AreEqual(enemy.transform, friend.Provocation.Aggressor, "the caller's friend joins, timid or not");
                Assert.IsFalse(timid.Provocation.IsProvoked, "the timid stay out of it");
                Assert.IsFalse(beyond.Provocation.IsProvoked, "30 m from the caller is past its 20 m call");

                Assert.IsTrue(bold.GetComponent<AlertBroadcaster>().CallForHelp(), "one pulled in calls in turn");
                Assert.AreEqual(enemy.transform, beyond.Provocation.Aggressor);
                friend.GetComponent<AlertBroadcaster>().CallForHelp();
                Assert.IsFalse(timid.Provocation.IsProvoked, "called again, the timid still stay out");
            }
            finally
            {
                foreach (Resident r in roster)
                {
                    Invoke(r, "OnDisable");
                    EntityTargetRegistry.Unregister(r.GetComponent<EntityFaction>());
                }
                root.Dispose();
                Object.DestroyImmediate(enemy);
                Object.DestroyImmediate(people);
                Object.DestroyImmediate(table);
            }
        }

        // A resident with the stack a call for help runs through — faction, broadcaster, provocation, alert
        // receiver — awake, enabled and in the target registry, all on one faction (allied with itself).
        private static Resident Fighter(Transform settlement, List<Resident> roster, FactionDefinition faction,
                                        FactionRelationshipTable table, float nerve, Vector3 position)
        {
            var go = new GameObject("resident");
            go.transform.SetParent(settlement, false);
            go.transform.position = position;
            var entity = go.AddComponent<EntityFaction>();
            entity.SetFaction(faction, table);
            EntityTargetRegistry.Register(entity);
            var call = new UnityEditor.SerializedObject(go.AddComponent<AlertBroadcaster>());
            call.FindProperty("alertRadius").floatValue = 20f;
            call.ApplyModifiedPropertiesWithoutUndo();
            go.AddComponent<ProvocationModule>();
            go.AddComponent<AlertReceiverModule>();
            Resident resident = go.AddComponent<Resident>();
            resident.nerveOverride = nerve;
            resident.index = roster.Count;
            roster.Add(resident);
            // Edit mode runs neither Awake nor OnEnable: one finds the stack, the other hears ally alerts.
            Invoke(resident, "Awake");
            Invoke(resident, "OnEnable");
            resident.ApplyDerivedTuning();
            return resident;
        }

        private static void Invoke(Resident resident, string method) =>
            typeof(Resident).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(resident, null);

        // A bare resident: edit mode runs no Awake, so it has no health and is never dead.
        private static Resident Plain(Transform parent)
        {
            var go = new GameObject("resident");
            go.transform.SetParent(parent, false);
            return go.AddComponent<Resident>();
        }

        // A settlement whose config has a culture, so every resident under it is a member of its society.
        private sealed class TestSettlement : System.IDisposable
        {
            private readonly GameObject root;
            private readonly SettlementConfig config;
            private readonly Settlement settlement;

            public TestSettlement()
            {
                root = new GameObject("settlement");
                settlement = root.AddComponent<Settlement>();
                config = ScriptableObject.CreateInstance<SettlementConfig>();
                config.culture = ScriptableObject.CreateInstance<SettlementCulture>();
                var so = new UnityEditor.SerializedObject(settlement);
                so.FindProperty("config").objectReferenceValue = config;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            public Transform transform => root.transform;
            public SettlementSociety Society => settlement.Society;

            public void Dispose()
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(config.culture);
                Object.DestroyImmediate(config);
            }
        }

        // A resident body under <paramref name="parent"/>; a Settlement with a culture above it makes it a member.
        private static Resident NewResident(Transform parent, float nerve, out ProvocationModule provocation)
        {
            var go = new GameObject("resident");
            go.transform.SetParent(parent, false);
            provocation = go.AddComponent<ProvocationModule>();
            Resident resident = go.AddComponent<Resident>();
            resident.nerveOverride = nerve;
            // Edit mode runs no Awake, and Awake is where the resident finds its provocation.
            typeof(Resident).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(resident, null);
            return resident;
        }
    }
}
