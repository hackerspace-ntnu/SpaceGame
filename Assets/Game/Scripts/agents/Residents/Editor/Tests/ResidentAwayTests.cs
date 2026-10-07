// A resident away with a band: GoAway hides it like a sleeper indoors but nothing at home brings it back out —
// not a noise, not a wake, not ComeOnstage — until ComeHome puts the body where it is told; both are safe to call
// twice. Its settlement learns who is away from SettlementSociety.AbsenceQuery (stubbed here) and plans no day
// for that resident, pairs nobody with it, and re-plans when told the answer changed for its own id only.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.World;

namespace SpaceGame.Agents.Residents.Tests
{
    public class ResidentAwayTests
    {
        private const string SettlementId = "settlement-under-test";
        private const int Day = 0;

        private GameObject root;
        private Settlement settlement;
        private SettlementConfig config;
        private ResidentArchetype roamer;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<SettlementConfig>();
            config.culture = ScriptableObject.CreateInstance<SettlementCulture>();
            roamer = ScriptableObject.CreateInstance<ResidentArchetype>();

            root = new GameObject("Settlement");
            settlement = root.AddComponent<Settlement>();
            var so = new SerializedObject(settlement);
            so.FindProperty("config").objectReferenceValue = config;
            so.ApplyModifiedPropertiesWithoutUndo();

            var identity = new SerializedObject(root.AddComponent<SaveableEntity>());
            identity.FindProperty("instanceId").stringValue = SettlementId;
            identity.FindProperty("authored").boolValue = true;
            identity.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            SettlementSociety.AbsenceQuery = null;
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(config.culture);
            Object.DestroyImmediate(config);
            Object.DestroyImmediate(roamer);
        }

        [Test]
        public void GoAway_HidesTheResident_AndIsIdempotent()
        {
            Resident resident = NewResident(0);

            resident.GoAway();
            resident.GoAway();

            Assert.IsTrue(resident.IsAway);
            Assert.IsTrue(resident.IsOffstage, "away is offstage too: every check that skips an offstage resident skips it");
        }

        [Test]
        public void AwayResident_IsNotBroughtOnstage_ByAWakeOrComeOnstage()
        {
            Resident resident = NewResident(0);
            resident.GoAway();

            resident.Wake(Vector3.one, approach: true);
            resident.ComeOnstage();

            Assert.IsTrue(resident.IsAway);
            Assert.IsTrue(resident.IsOffstage);
            Assert.AreEqual(OverrideKind.None, resident.Override, "a noise at home sends nobody out to look who is not there");
            Assert.IsNull(resident.GlancePoint);
        }

        [Test]
        public void ComeHome_PutsTheBodyThere_AndBringsItOnstage_Once()
        {
            Resident resident = NewResident(0);
            resident.GoAway();
            var home = new Vector3(12f, 0f, -4f);
            Quaternion facing = Quaternion.Euler(0f, 90f, 0f);

            resident.ComeHome(home, facing);

            Assert.IsFalse(resident.IsAway);
            Assert.IsFalse(resident.IsOffstage);
            Assert.AreEqual(home, resident.transform.position);
            Assert.AreEqual(facing.eulerAngles.y, resident.transform.rotation.eulerAngles.y, 1e-3f);

            resident.ComeHome(Vector3.zero, Quaternion.identity);
            Assert.AreEqual(home, resident.transform.position, "a second homecoming moves a resident who is already home nowhere");
        }

        [Test]
        public void ComeHome_ForAResidentAtHome_DoesNothing()
        {
            Resident resident = NewResident(0);
            resident.transform.position = Vector3.forward;

            resident.ComeHome(Vector3.right * 50f, Quaternion.identity);

            Assert.AreEqual(Vector3.forward, resident.transform.position);
            Assert.IsFalse(resident.IsOffstage);
        }

        [Test]
        public void Society_AwayFromDirector_IsPassedToPlanner()
        {
            Resident home = NewResident(0), away = NewResident(1);
            string askedFor = null;
            SettlementSociety.AbsenceQuery = (settlementId, residentKey) =>
            {
                askedFor = settlementId;
                return residentKey == SettlementSociety.ResidentKey(away);
            };

            SettlementSociety society = settlement.Society;
            society.RebuildPlans();

            Assert.AreEqual(SettlementId, askedFor, "the question names the settlement by its identity");
            Assert.AreEqual("r:1", SettlementSociety.ResidentKey(away));
            Assert.IsEmpty(society.PlanFor(away.index, Day).segments, "no day is planned for a resident out with a band");
            Assert.IsNotEmpty(society.PlanFor(home.index, Day).segments);
        }

        [Test]
        public void Society_AwayResident_IsNotPaired()
        {
            Resident leader = NewResident(0, friend: 1), follower = NewResident(1, friend: 0);
            SettlementSociety society = settlement.Society;
            society.RebuildPlans();
            Assume.That(society.LeaderOf(follower), Is.EqualTo(leader), "two roaming friends walk together");

            SettlementSociety.AbsenceQuery = (_, residentKey) => residentKey == SettlementSociety.ResidentKey(leader);
            society.RebuildPlans();

            Assert.IsNull(society.LeaderOf(follower), "nobody follows a friend who is away");
            Assert.IsNull(society.FollowerOf(leader));
        }

        [Test]
        public void Society_AbsenceChanged_RePlansOnlyThatSettlement()
        {
            Resident resident = NewResident(0);
            SettlementSociety society = settlement.Society;
            society.Enable();
            try
            {
                society.RebuildPlans();
                Assume.That(society.PlanFor(resident.index, Day).segments, Is.Not.Empty);
                SettlementSociety.AbsenceQuery = (_, _) => true;

                SettlementSociety.OnAbsenceChanged("another-settlement");
                Assert.IsNotEmpty(society.PlanFor(resident.index, Day).segments, "another settlement's news re-plans nothing here");

                SettlementSociety.OnAbsenceChanged(SettlementId);
                Assert.IsEmpty(society.PlanFor(resident.index, Day).segments);
            }
            finally
            {
                society.Disable();
            }
        }

        // A roaming resident of the settlement under test. Edit mode runs no Awake: it has no health (never dead), no
        // agent, no presence — GoAway and ComeHome only change the state these tests read.
        private Resident NewResident(int index, int friend = -1)
        {
            var go = new GameObject($"resident {index}");
            go.transform.SetParent(root.transform, false);
            Resident resident = go.AddComponent<Resident>();
            (resident.index, resident.seed, resident.archetype, resident.settlement) = (index, 100 + index, roamer, settlement);
            if (friend >= 0) resident.bonds = new[] { new ResidentBond { other = friend, kind = BondKind.Friend } };
            return resident;
        }
    }
}
