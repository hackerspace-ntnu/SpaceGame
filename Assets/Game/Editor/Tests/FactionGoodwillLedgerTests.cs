// The goodwill ledger: rows, events, spread, and being hunted by association.
//
// GoodwillMathTests covers the arithmetic. This file covers the BOOKKEEPING, which is where the
// interesting failures are — a row keyed by the wrong thing, a delta spreading to a faction that
// keeps no ledger, one player's war reaching a crewmate who has done nothing.
//
// No session and no save system here, so the ledger's own `TryGetProfile` (which goes through
// SaveManager) cannot resolve anybody. The tests therefore drive the id-keyed API directly —
// Report's attacker resolution is the one part that needs a live SaveManager and is left to the
// two-process run. Everything else is exercised exactly as it runs.
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;

namespace SpaceGame.EditorTools
{
    public class FactionGoodwillLedgerTests
    {
        private const string PlayerA = "profile-a";
        private const string PlayerB = "profile-b";

        private readonly System.Collections.Generic.List<Object> junk = new();

        private FactionGoodwillLedger ledger;
        private FactionDefinition sand, sky, clankers, humans;
        private FactionRelationshipTable table;

        [SetUp]
        public void SetUp()
        {
            sand = Faction("SandTribe");
            sky = Faction("Sky");
            clankers = Faction("Clankers", FactionRelationship.Hostile);
            humans = Faction("Humans");

            table = ScriptableObject.CreateInstance<FactionRelationshipTable>();
            junk.Add(table);

            var go = new GameObject("Ledger");
            junk.Add(go);
            ledger = go.AddComponent<FactionGoodwillLedger>();

            var so = new SerializedObject(ledger);
            SerializedProperty tribes = so.FindProperty("tribes");
            tribes.arraySize = 2;
            tribes.GetArrayElementAtIndex(0).objectReferenceValue = sand;
            tribes.GetArrayElementAtIndex(1).objectReferenceValue = sky;
            so.FindProperty("crewFaction").objectReferenceValue = humans;
            so.FindProperty("relationships").objectReferenceValue = table;
            so.ApplyModifiedPropertiesWithoutUndo();

            // AddComponent does not run Awake in edit mode, and Awake is what builds the tracked
            // set from the serialized list.
            Invoke("Awake");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private FactionDefinition Faction(string name,
            FactionRelationship stance = FactionRelationship.Neutral)
        {
            var f = ScriptableObject.CreateInstance<FactionDefinition>();
            f.name = f.factionName = name;
            f.ID = name;
            f.defaultStance = stance;
            junk.Add(f);
            return f;
        }

        private void Invoke(string method) =>
            typeof(FactionGoodwillLedger)
                .GetMethod(method, System.Reflection.BindingFlags.Instance |
                                   System.Reflection.BindingFlags.NonPublic)
                .Invoke(ledger, null);

        /// <summary>Move a row without needing a live SaveManager to resolve a player object.</summary>
        private void Move(FactionDefinition faction, string profileId, float delta) =>
            typeof(FactionGoodwillLedger)
                .GetMethod("Move", System.Reflection.BindingFlags.Instance |
                                   System.Reflection.BindingFlags.NonPublic)
                .Invoke(ledger, new object[] { faction, profileId, delta });

        // ── Who keeps a ledger ─────────────────────────────────────────────────────

        [Test]
        public void OnlyTheListedTribesKeepAnOpinion()
        {
            Assert.IsTrue(ledger.Tracks(sand));
            Assert.IsTrue(ledger.Tracks(sky));
            Assert.IsFalse(ledger.Tracks(humans), "the crew does not keep goodwill about itself");
            Assert.IsFalse(ledger.Tracks(clankers), "you cannot befriend a Clanker");
            Assert.IsFalse(ledger.Tracks(null));
        }

        [Test]
        public void AFactionThatShootsOnSightIsRefusedEvenIfListed()
        {
            var so = new SerializedObject(ledger);
            SerializedProperty tribes = so.FindProperty("tribes");
            tribes.arraySize = 3;
            tribes.GetArrayElementAtIndex(2).objectReferenceValue = clankers;
            so.ApplyModifiedPropertiesWithoutUndo();

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Clankers"));
            Invoke("Awake");

            Assert.IsFalse(ledger.Tracks(clankers),
                "a Hostile default means the number could never be moved — a row for it is a lie");
        }

        // ── Rows ───────────────────────────────────────────────────────────────────

        [Test]
        public void EverybodyStartsWary()
        {
            Assert.AreEqual(GoodwillBand.Wary, ledger.BandFor(sand, PlayerA));
            Assert.AreEqual(0f, ledger.ValueFor(sand, PlayerA), 1e-4f);
        }

        /// <summary>
        /// The decision the design rests on: goodwill is per player, not per crew. One astronaut can
        /// be an outlaw to the Sand Tribe while their crewmate trades with it.
        /// </summary>
        [Test]
        public void TwoPlayersHaveIndependentRows()
        {
            Move(sand, PlayerA, -50f);

            Assert.AreEqual(GoodwillBand.HostileOnSight, ledger.BandFor(sand, PlayerA));
            Assert.AreEqual(GoodwillBand.Wary, ledger.BandFor(sand, PlayerB),
                            "B has done nothing and the tribe has no quarrel with them");
        }

        [Test]
        public void TwoFactionsHaveIndependentRowsForTheSamePlayer()
        {
            Move(sand, PlayerA, -50f);

            Assert.AreEqual(GoodwillBand.HostileOnSight, ledger.BandFor(sand, PlayerA));
            Assert.AreEqual(GoodwillBand.Wary, ledger.BandFor(sky, PlayerA),
                            "the Sky tribe was not there and did not hear about it");
        }

        [Test]
        public void ABandChangeIsAnnouncedOnceForTheCrossing()
        {
            int calls = 0;
            GoodwillBand from = GoodwillBand.Wary, to = GoodwillBand.Wary;
            ledger.BandChanged += (f, p, a, b) => { calls++; from = a; to = b; };

            Move(sand, PlayerA, -45f);
            Assert.AreEqual(1, calls);
            Assert.AreEqual(GoodwillBand.Wary, from);
            Assert.AreEqual(GoodwillBand.HostileOnSight, to);

            Move(sand, PlayerA, -5f);
            Assert.AreEqual(1, calls, "further down inside the same band is not a crossing");
        }

        [Test]
        public void TheMeterCannotLeaveItsRange()
        {
            Move(sand, PlayerA, -500f);
            Assert.AreEqual(GoodwillMath.Min, ledger.ValueFor(sand, PlayerA), 1e-3f);

            Move(sand, PlayerB, 500f);
            Assert.AreEqual(GoodwillMath.Max, ledger.ValueFor(sand, PlayerB), 1e-3f);
        }

        // ── Decay ──────────────────────────────────────────────────────────────────

        [Test]
        public void TimeMovesEveryRowBackTowardZero()
        {
            Move(sand, PlayerA, -50f);
            Move(sky, PlayerB, 30f);

            ledger.DecayAll(10f);   // ten in-game hours at the default 0.5/hour

            Assert.AreEqual(-45f, ledger.ValueFor(sand, PlayerA), 1e-3f);
            Assert.AreEqual(25f, ledger.ValueFor(sky, PlayerB), 1e-3f);
        }

        [Test]
        public void DecayAnnouncesTheBandItCrossesBackThrough()
        {
            Move(sand, PlayerA, -45f);
            Assert.AreEqual(GoodwillBand.HostileOnSight, ledger.BandFor(sand, PlayerA));

            int calls = 0;
            ledger.BandChanged += (f, p, a, b) => calls++;

            ledger.DecayAll(40f);   // -45 -> -25 at the tuned 0.5/hour, past the -30 hysteresis edge

            Assert.AreEqual(GoodwillBand.Wary, ledger.BandFor(sand, PlayerA),
                            "a war can be waited out — the brake on the design's positive loop");
            Assert.AreEqual(1, calls);
        }

        // ── Restore ────────────────────────────────────────────────────────────────

        /// <summary>
        /// The band is restored, not recomputed. Hysteresis makes the band a function of its own
        /// history, so a player saved inside the sticky part of a band would otherwise come back
        /// one band better and be forgiven over a loading screen.
        /// </summary>
        [Test]
        public void ARestoredRowKeepsItsBandRatherThanRecomputingIt()
        {
            ledger.RestoreRow(sand, PlayerA, -35f, GoodwillBand.HostileOnSight);

            Assert.AreEqual(-35f, ledger.ValueFor(sand, PlayerA), 1e-3f);
            Assert.AreEqual(GoodwillBand.HostileOnSight, ledger.BandFor(sand, PlayerA),
                "-35 alone reads as Wary; it is HostileOnSight only because that is where the " +
                "player already was");
        }

        [Test]
        public void ARestoreIsNotABandChange()
        {
            int calls = 0;
            ledger.BandChanged += (f, p, a, b) => calls++;

            ledger.RestoreRow(sand, PlayerA, -90f, GoodwillBand.AtWar);

            Assert.AreEqual(0, calls, "nothing changed — the world is only being rebuilt");
        }

        [Test]
        public void AllEnumeratesEveryRowForTheSaver()
        {
            Move(sand, PlayerA, -50f);
            Move(sky, PlayerB, 25f);

            var all = ledger.All().ToList();

            Assert.AreEqual(2, all.Count);
            Assert.IsTrue(all.Any(r => r.Faction == sand && r.ProfileId == PlayerA));
            Assert.IsTrue(all.Any(r => r.Faction == sky && r.ProfileId == PlayerB));
        }

        // ── Credit ────────────────────────────────────────────────────────────────

        [Test]
        public void Credit_MovesTowardPeace_AndCanEndAWar()
        {
            Move(sand, PlayerA, -85f);
            Assert.AreEqual(GoodwillBand.AtWar, ledger.BandFor(sand, PlayerA));

            ledger.Credit(sand, PlayerA, 30f);

            Assert.AreEqual(-55f, ledger.ValueFor(sand, PlayerA), 0.001f);
            Assert.AreEqual(GoodwillBand.HostileOnSight, ledger.BandFor(sand, PlayerA));
        }

        [Test]
        public void Credit_TwoDefeats_EndTheWarToo()
        {
            Move(sand, PlayerA, -85f);

            ledger.Credit(sand, PlayerA, 12f);
            Assert.AreEqual(GoodwillBand.AtWar, ledger.BandFor(sand, PlayerA), "-73 is inside the sticky edge");

            ledger.Credit(sand, PlayerA, 12f);
            Assert.AreEqual(GoodwillBand.HostileOnSight, ledger.BandFor(sand, PlayerA));
        }

        [Test]
        public void Credit_IgnoresNonPositiveAmounts()
        {
            Move(sand, PlayerA, -50f);

            ledger.Credit(sand, PlayerA, 0f);
            ledger.Credit(sand, PlayerA, -10f);

            Assert.AreEqual(-50f, ledger.ValueFor(sand, PlayerA), 0.001f);
        }

        // ── Being hunted by association ────────────────────────────────────────────
        //
        // BandForEntity itself needs a live SaveManager to turn a GameObject into a profile id, so
        // the rule it applies is extracted and tested here instead. The loop around it is three
        // lines and a registry walk; this is the part with the decisions in it.

        private const float Radius = 200f;

        [Test]
        public void StandingNearAHuntedCrewmateMakesYouATarget()
        {
            Assert.IsTrue(FactionGoodwillLedger.IsHuntedCompany(
                Vector3.zero, humans, new Vector3(50f, 0f, 0f), humans,
                GoodwillBand.AtWar, Radius));
        }

        [Test]
        public void WalkingAwayEndsIt()
        {
            Assert.IsFalse(FactionGoodwillLedger.IsHuntedCompany(
                Vector3.zero, humans, new Vector3(Radius + 1f, 0f, 0f), humans,
                GoodwillBand.AtWar, Radius),
                "proximity rather than membership is what keeps a bystander from being punished " +
                "for somebody else's war for the rest of the session");
        }

        /// <summary>
        /// Hostile-on-sight is not the same as being hunted. The tribe shoots that player when it
        /// sees them; it is not sending anybody after them, so nobody is caught up in it.
        /// </summary>
        [Test]
        public void OnlyAWarSpreadsAndMerelyBeingShotAtDoesNot()
        {
            foreach (GoodwillBand band in new[]
                     { GoodwillBand.HostileOnSight, GoodwillBand.Wary,
                       GoodwillBand.Friendly, GoodwillBand.Allied })
            {
                Assert.IsFalse(FactionGoodwillLedger.IsHuntedCompany(
                    Vector3.zero, humans, Vector3.one, humans, band, Radius), band.ToString());
            }
        }

        /// <summary>
        /// The versus case, and the reason there is no game-mode check anywhere: "same side" is the
        /// crew in the open world and the TEAM in a match, because a match re-teams players
        /// into per-team factions. Somebody on the other team standing next to a hunted player is
        /// not part of their war.
        /// </summary>
        [Test]
        public void TheOtherTeamIsNotCaughtUpInIt()
        {
            FactionDefinition redTeam = Faction("RedTeam");
            FactionDefinition blueTeam = Faction("BlueTeam");

            Assert.IsTrue(FactionGoodwillLedger.IsHuntedCompany(
                Vector3.zero, redTeam, Vector3.one, redTeam, GoodwillBand.AtWar, Radius),
                "a team-mate of the hunted player is");

            Assert.IsFalse(FactionGoodwillLedger.IsHuntedCompany(
                Vector3.zero, blueTeam, Vector3.one, redTeam, GoodwillBand.AtWar, Radius),
                "somebody on the other side is not");
        }

        [Test]
        public void TwoPlayersWithNoFactionAreNotAutomaticallyTeamMates()
        {
            Assert.IsFalse(FactionGoodwillLedger.IsHuntedCompany(
                Vector3.zero, null, Vector3.one, null, GoodwillBand.AtWar, Radius),
                "two unknowns are not the same side — guessing would put a spectator in a war");
        }

        [Test]
        public void TheRadiusIsInclusiveAtItsEdge()
        {
            Assert.IsTrue(FactionGoodwillLedger.IsHuntedCompany(
                Vector3.zero, humans, new Vector3(Radius, 0f, 0f), humans,
                GoodwillBand.AtWar, Radius));
        }

        // ── A settlement asks too ──────────────────────────────────────────────────
        //
        // A town is not an entity, so SettlementAlarm asks the registry by definition and table.
        // That query used to read the table alone, and a tribe's alarm rang for — and its defenders
        // rallied against — a player the tribe itself called Allied.

        /// <summary>
        /// The definition query goes through goodwill before the table: an Allied player is no
        /// intruder where the table says Hostile, while a crewmate on no better terms still is.
        /// </summary>
        [Test]
        public void ATribesAlliedPlayerIsNoIntruderInItsSettlement()
        {
            SetStance(sand, humans, FactionRelationship.Hostile);
            EntityFaction ally = Player("Ally", PlayerA);
            EntityFaction stranger = Player("Stranger", PlayerB);
            Move(sand, PlayerA, 80f);

            SaveManager previous = SaveManager.Instance;
            SaveManager players = BindPlayers((PlayerA, ally.gameObject), (PlayerB, stranger.gameObject));
            var found = new System.Collections.Generic.List<EntityFaction>();
            try
            {
                Assert.AreEqual(GoodwillBand.Allied, ledger.BandForEntity(sand, ally));

                EntityTargetRegistry.Query(sand, table, FactionRelationship.Hostile, Vector3.zero, 100f, found);
                CollectionAssert.DoesNotContain(found, ally, "the ally's band overrides the table's Hostile row");
                CollectionAssert.Contains(found, stranger, "the stranger is Wary, so the table's row stands");

                EntityTargetRegistry.Query(sand, table, FactionRelationship.Allied, Vector3.zero, 100f, found);
                CollectionAssert.Contains(found, ally);
                CollectionAssert.DoesNotContain(found, stranger);
            }
            finally
            {
                EntityTargetRegistry.Unregister(ally);
                EntityTargetRegistry.Unregister(stranger);
                SetSaveManager(previous);
                Object.DestroyImmediate(players.gameObject);
            }
        }

        [Test]
        public void ATribesAlliedPlayerIsNotCountedAsOneOfItsTownsPeople()
        {
            EntityFaction ally = Player("Ally", PlayerA);
            ally.gameObject.tag = SpaceGame.Gameplay.SpawnClearance.PlayerTag;
            Move(sand, PlayerA, 80f);

            var resident = new GameObject("Resident");
            junk.Add(resident);
            EntityFaction inhabitant = resident.AddComponent<EntityFaction>();
            inhabitant.SetFaction(sand, table);
            EntityTargetRegistry.Register(inhabitant);

            var town = new GameObject("Town");
            junk.Add(town);
            var population = town.AddComponent<SettlementPopulation>();
            population.Configure(sand, table, new SettlementPopulation.Inhabitant[0], maxPopulation: 12,
                                 spawnInterval: 60f, innerRadius: 45f, outerRadius: 120f, countRadius: 100f);

            SaveManager previous = SaveManager.Instance;
            SaveManager players = BindPlayers((PlayerA, ally.gameObject));
            var allied = new System.Collections.Generic.List<EntityFaction>();
            try
            {
                EntityTargetRegistry.Query(sand, table, FactionRelationship.Allied, Vector3.zero, 100f, allied);
                CollectionAssert.Contains(allied, ally, "precondition: goodwill makes the player Allied");

                Assert.AreEqual(1, population.CountInhabitants(), "only the resident lives here");
            }
            finally
            {
                EntityTargetRegistry.Unregister(ally);
                EntityTargetRegistry.Unregister(inhabitant);
                SetSaveManager(previous);
                Object.DestroyImmediate(players.gameObject);
            }
        }

        private void SetStance(FactionDefinition a, FactionDefinition b, FactionRelationship stance)
        {
            var rows = new SerializedObject(table).FindProperty("relationships");
            rows.arraySize = 1;
            SerializedProperty row = rows.GetArrayElementAtIndex(0);
            row.FindPropertyRelative("factionA").objectReferenceValue = a;
            row.FindPropertyRelative("factionB").objectReferenceValue = b;
            row.FindPropertyRelative("relationship").enumValueIndex = (int)stance;
            rows.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A crew-faction entity in the registry, a few metres from the origin.</summary>
        private EntityFaction Player(string name, string profileId)
        {
            var go = new GameObject(name);
            junk.Add(go);
            go.transform.position = new Vector3(profileId == PlayerA ? 5f : -5f, 0f, 0f);
            var entity = go.AddComponent<EntityFaction>();
            entity.SetFaction(humans, table);
            EntityTargetRegistry.Register(entity);
            return entity;
        }

        /// <summary>
        /// A SaveManager holding just a player service with these bindings — the one part of it
        /// <c>BandForEntity</c> reads to turn a body into a profile id. Edit mode runs no Awake, so the
        /// instance and its service are installed by hand.
        /// </summary>
        private static SaveManager BindPlayers(params (string profileId, GameObject body)[] bindings)
        {
            var manager = new GameObject("SaveManager").AddComponent<SaveManager>();
            var service = new PlayerSaveService();
            typeof(SaveManager)
                .GetField("playerService", System.Reflection.BindingFlags.Instance |
                                           System.Reflection.BindingFlags.NonPublic)
                .SetValue(manager, service);
            SetSaveManager(manager);

            foreach ((string profileId, GameObject body) in bindings)
                service.Bind(profileId, body, applyPosition: false);

            return manager;
        }

        private static void SetSaveManager(SaveManager manager) =>
            typeof(SaveManager).GetProperty(nameof(SaveManager.Instance)).SetValue(null, manager);
    }
}
