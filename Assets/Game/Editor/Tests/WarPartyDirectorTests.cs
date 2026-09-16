// The director against a real ledger and world sim, without a session: wars open on AtWar, parties
// are raised with their quarry and tier, and each reckoning moves goodwill and escalation per spec §5.3.
// Anything needing a bound player (tracking, catching, notices) is the play checklist's.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class WarPartyDirectorTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Player = "profile-a";

        private readonly List<Object> junk = new();
        private FactionGoodwillLedger ledger;
        private NpcWorldSim sim;
        private WarPartyDirector director;
        private FactionDefinition sand;

        [SetUp]
        public void SetUp()
        {
            WorldSiteRegistry.Clear();

            sand = ScriptableObject.CreateInstance<FactionDefinition>();
            sand.ID = sand.factionName = "Sand";
            junk.Add(sand);

            var roster = ScriptableObject.CreateInstance<FactionRoster>();
            roster.faction = sand;
            roster.warPartyTiers = new[] { new WarPartyTier(), new WarPartyTier(), new WarPartyTier() };
            sand.roster = roster;
            junk.Add(roster);

            var table = ScriptableObject.CreateInstance<FactionRelationshipTable>();
            junk.Add(table);

            var ledgerGo = new GameObject("Ledger");
            junk.Add(ledgerGo);
            ledger = ledgerGo.AddComponent<FactionGoodwillLedger>();
            var so = new SerializedObject(ledger);
            so.FindProperty("tribes").arraySize = 1;
            so.FindProperty("tribes").GetArrayElementAtIndex(0).objectReferenceValue = sand;
            so.FindProperty("relationships").objectReferenceValue = table;
            so.ApplyModifiedPropertiesWithoutUndo();
            Invoke(ledger, "Awake");

            var simGo = new GameObject("Sim");
            junk.Add(simGo);
            sim = simGo.AddComponent<NpcWorldSim>();
            var template = new NpcGroupTemplate { id = "sand-war-party", tribe = sand, runtimeOnly = true, bountyHunters = true };
            typeof(NpcWorldSim).GetField("templates", Private).SetValue(sim, new[] { template });
            Invoke(sim, "Awake");

            director = simGo.AddComponent<WarPartyDirector>();
            Invoke(director, "Awake");
            Invoke(director, "Start");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
            WorldSiteRegistry.Clear();
        }

        private static object Invoke(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Private).Invoke(target, args);

        private void Move(float delta) => Invoke(ledger, "Move", sand, Player, delta);

        private War AtWarWithAParty()
        {
            Move(-85f);
            War war = director.Book.Find(sand, Player);
            Invoke(director, "Raise", war, Vector3.zero);
            return war;
        }

        [Test]
        public void EnteringAtWar_OpensAWar()
        {
            Move(-85f);
            Assert.IsNotNull(director.Book.Find(sand, Player));
        }

        [Test]
        public void Raise_CreatesAPartyHuntingTheQuarry_AtTheFallback_WhenNoCampExists()
        {
            War war = AtWarWithAParty();
            NpcGroup party = sim.FindGroup(war.PartyGroupId);

            Assert.IsNotNull(party);
            Assert.AreEqual(Player, party.QuarryProfileId);
            Assert.AreEqual(0, party.Tier);
            Assert.AreEqual(250f + 30f + 100f, new Vector2(party.Position.x, party.Position.z).magnitude, 0.5f);
            Assert.IsTrue(party.HasLead);
            Assert.LessOrEqual(new Vector2(party.Lead.x, party.Lead.z).magnitude, 30.001f);
        }

        [Test]
        public void Raise_UsesTheNearestCamp_WhenNobodyCanSeeIt()
        {
            WorldSiteRegistry.Register(SiteKind.Camp, new Vector3(700f, 0f, 0f), 10f, "Camp");

            War war = AtWarWithAParty();

            Assert.AreEqual(700f, sim.FindGroup(war.PartyGroupId).Position.x, 0.01f);
        }

        [Test]
        public void Defeated_Credits4_RaisesTheTier_AndTheWarGoesOn()
        {
            War war = AtWarWithAParty();
            string partyId = war.PartyGroupId;

            Invoke(director, "Resolve", war, Reckoning.Defeated);

            Assert.AreEqual(-81f, ledger.ValueFor(sand, Player), 0.001f);
            Assert.AreEqual(1, war.Tier);
            Assert.IsNull(sim.FindGroup(partyId), "a folded party is removed at once");
            Assert.AreSame(war, director.Book.Find(sand, Player));
            Assert.AreEqual(60f, war.Cooldown);
        }

        [Test]
        public void Caught_Credits15_AtDeepHostility_TheWarGoesOn()
        {
            // -85 + 15 = -70, still inside AtWar's sticky edge (atWar -80, hysteresis 10): a single
            // Caught no longer clears a war entered this deep. Longer wars (2026-09-17 tuning).
            War war = AtWarWithAParty();
            string partyId = war.PartyGroupId;

            Invoke(director, "Resolve", war, Reckoning.Caught);

            Assert.AreEqual(-70f, ledger.ValueFor(sand, Player), 0.001f);
            Assert.AreEqual(GoodwillBand.AtWar, ledger.BandFor(sand, Player));
            Assert.AreSame(war, director.Book.Find(sand, Player), "one Caught no longer clears deep hostility");
            Assert.AreEqual(0, war.Tier, "Caught does not escalate");
            Assert.IsNull(sim.FindGroup(partyId), "the party itself is still resolved");
            Assert.AreEqual(60f, war.Cooldown);
        }

        [Test]
        public void Abandoned_ChangesNothing_ButTheParty()
        {
            War war = AtWarWithAParty();
            string partyId = war.PartyGroupId;

            Invoke(director, "Resolve", war, Reckoning.Abandoned);

            Assert.AreEqual(-85f, ledger.ValueFor(sand, Player), 0.001f);
            Assert.AreEqual(0, war.Tier);
            Assert.IsNull(sim.FindGroup(partyId));
        }

        [Test]
        public void Reconcile_ARestoredWar_StartsItsCooldownFromFull()
        {
            ledger.RestoreRow(sand, Player, -90f, GoodwillBand.AtWar);

            Invoke(director, "Reconcile", ledger);

            War war = director.Book.Find(sand, Player);
            Assert.IsNotNull(war);
            Assert.AreEqual(60f, war.Cooldown);
            Assert.IsFalse(director.Book.ReadyToRaise(war));
        }

        [Test]
        public void APartyWhoseQuarryNeverJoins_IsReleasedAfterTheGrace_AndTheWarGoesOn()
        {
            // No player is ever bound in EditMode, so this quarry is absent from the first step: the
            // restored-party-after-a-load case (spec §6: an absent quarry is a disconnected one).
            War war = AtWarWithAParty();
            Move(-24f);   // keep the war going despite the credit, so the escalated tier can be checked
            Invoke(director, "Resolve", war, Reckoning.Defeated);
            Invoke(director, "Raise", war, Vector3.zero);
            string partyId = war.PartyGroupId;

            Invoke(director, "Step", 60f);
            Invoke(director, "Step", 60f);
            Assert.IsNotNull(sim.FindGroup(partyId), "within the grace the party waits for its quarry");

            Invoke(director, "Step", 1f);

            Assert.IsNull(sim.FindGroup(partyId), "past the grace it leaves, unresolved");
            Assert.AreSame(war, director.Book.Find(sand, Player), "the war waits in the quarry's save");
            Assert.AreEqual(1, war.Tier);
            Assert.IsFalse(war.HasParty);
            Assert.AreEqual(60f, war.Cooldown);
        }

        [Test]
        public void EscalationCaps_AtTheRostersLastTier()
        {
            War war = AtWarWithAParty();
            for (int i = 0; i < 5; i++)
            {
                Move(-24f);   // keep the war going despite the credits
                if (!war.HasParty) Invoke(director, "Raise", war, Vector3.zero);
                Invoke(director, "Resolve", war, Reckoning.Defeated);
            }

            Assert.AreEqual(2, war.Tier);
        }
    }
}
