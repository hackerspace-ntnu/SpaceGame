// Rosters spec §7: a party that was out when the game saved comes back as the same party and is never
// doubled; the tier a war had escalated to survives a quit during the cooldown.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTools
{
    public class WarPartyPersistenceTests
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
            sand = ScriptableObject.CreateInstance<FactionDefinition>();
            sand.ID = sand.factionName = "Sand";
            junk.Add(sand);

            var ledgerGo = new GameObject("Ledger");
            junk.Add(ledgerGo);
            ledger = ledgerGo.AddComponent<FactionGoodwillLedger>();
            var so = new SerializedObject(ledger);
            so.FindProperty("tribes").arraySize = 1;
            so.FindProperty("tribes").GetArrayElementAtIndex(0).objectReferenceValue = sand;
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
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private static object Invoke(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Private).Invoke(target, args);

        private NpcGroup RestoreParty(string id, int tier)
        {
            var saved = new NpcGroup { Id = id, TemplateId = "sand-war-party", QuarryProfileId = Player, Tier = tier, RosterSeed = 7 };
            sim.RestoreRecords(new[] { saved.ToRecord() });
            return sim.FindGroup(id);
        }

        [Test]
        public void ARestoredParty_IsAdopted_NotDoubled()
        {
            RestoreParty("warparty:Sand:profile-a:3", 2);

            Invoke(director, "AdoptRestoredParties");
            Invoke(director, "AdoptRestoredParties");

            War war = director.Book.Find(sand, Player);
            Assert.AreEqual(1, director.Book.Wars.Count);
            Assert.AreEqual("warparty:Sand:profile-a:3", war.PartyGroupId);
            Assert.AreEqual(2, war.Tier);
            Assert.IsFalse(director.Book.ReadyToRaise(war), "the restored party IS this war's party");
            Assert.AreEqual(7, sim.FindGroup(war.PartyGroupId).RosterSeed, "same people, same guns");
        }

        [Test]
        public void ASecondPartyForTheSameWar_IsDisbanded()
        {
            RestoreParty("warparty:Sand:profile-a:1", 0);
            Invoke(director, "AdoptRestoredParties");

            NpcGroup duplicate = sim.CreateGroup(sim.WarPartyTemplateFor(sand), "warparty:Sand:profile-a:2", Vector3.zero);
            duplicate.QuarryProfileId = Player;
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("second party"));

            Invoke(director, "AdoptRestoredParties");

            Assert.IsNull(sim.FindGroup("warparty:Sand:profile-a:2"));
            Assert.IsNotNull(sim.FindGroup("warparty:Sand:profile-a:1"));
        }

        [Test]
        public void Reconcile_DoesNotEndTheWar_OfAPlayerWhoHasNotBoundYet()
        {
            RestoreParty("warparty:Sand:profile-a:1", 1);
            Invoke(director, "AdoptRestoredParties");

            // The world restores before the player does: the ledger has no row for them yet.
            Invoke(director, "Reconcile", ledger);

            Assert.IsNotNull(director.Book.Find(sand, Player));
            Assert.IsNotNull(sim.FindGroup("warparty:Sand:profile-a:1"));
        }

        [Test]
        public void Reconcile_OpensAWar_ForARestoredAtWarRow()
        {
            ledger.RestoreRow(sand, Player, -90f, GoodwillBand.AtWar);

            Invoke(director, "Reconcile", ledger);

            Assert.IsNotNull(director.Book.Find(sand, Player));
        }

        [Test]
        public void WarTier_RoundTripsOnTheStanding()
        {
            var standing = new FactionGoodwillSaveable.Standing
            {
                factionId = "Sand", value = -85f, band = GoodwillBand.AtWar, warTier = 2,
            };

            JObject json = JObject.FromObject(standing, SaveSerializer.Serializer);
            var back = json.ToObject<FactionGoodwillSaveable.Standing>(SaveSerializer.Serializer);

            Assert.AreEqual(2, back.warTier);
        }

        [Test]
        public void WarTier_FromAnOlderSave_IsZero()
        {
            var back = JObject.Parse("{\"factionId\":\"Sand\",\"value\":-85.0,\"band\":0}")
                .ToObject<FactionGoodwillSaveable.Standing>(SaveSerializer.Serializer);

            Assert.AreEqual(0, back.warTier);
        }

        [Test]
        public void ARestoredTier_IsWhatTheNextWarStartsAt()
        {
            director.RestoreWarTier(sand, Player, 2);
            ledger.RestoreRow(sand, Player, -90f, GoodwillBand.AtWar);

            Invoke(director, "Reconcile", ledger);

            Assert.AreEqual(2, director.Book.Find(sand, Player).Tier);
        }
    }
}
