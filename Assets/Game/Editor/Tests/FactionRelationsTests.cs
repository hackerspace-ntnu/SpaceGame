// The stance rule (design §3.2) and the three-layer resolver (§3), on factions built in memory.
//
// Pure fixtures rather than the shipped assets: this file is about the RULE, and pinning it to
// whatever GlobalRelationships.asset happens to contain would turn every authoring change into a
// test failure. FactionAssetTests is the other half — it reads the real assets and checks that the
// authored matrix says what the design says.
//
// A CreateInstance faction has no asset path, so OnValidate leaves its ID empty and it never
// registers. Ids are stamped by hand where a test needs one, because Get() keys on instance ids
// and an empty ID is otherwise indistinguishable between two fixtures.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class FactionRelationsTests
    {
        private FactionRelationshipTable table;

        [SetUp]
        public void SetUp() => table = ScriptableObject.CreateInstance<FactionRelationshipTable>();

        [TearDown]
        public void TearDown()
        {
            if (table != null) Object.DestroyImmediate(table);
        }

        private static FactionDefinition Faction(string name,
            FactionRelationship stance = FactionRelationship.Neutral)
        {
            var f = ScriptableObject.CreateInstance<FactionDefinition>();
            f.name = name;
            f.factionName = name;
            f.ID = name;              // never empty: Get keys on instance ids, but a save keys on this
            f.defaultStance = stance;
            return f;
        }

        /// <summary>Writes the row list the same way the Inspector would, since it is private.</summary>
        private void Rows(params (FactionDefinition A, FactionDefinition B, FactionRelationship R)[] rows)
        {
            var so = new SerializedObject(table);
            SerializedProperty list = so.FindProperty("relationships");
            list.arraySize = rows.Length;

            for (int i = 0; i < rows.Length; i++)
            {
                SerializedProperty row = list.GetArrayElementAtIndex(i);
                row.FindPropertyRelative("factionA").objectReferenceValue = rows[i].A;
                row.FindPropertyRelative("factionB").objectReferenceValue = rows[i].B;
                row.FindPropertyRelative("relationship").enumValueIndex = (int)rows[i].R;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── Step 1: a faction is its own ally ──────────────────────────────────────

        [Test]
        public void AFactionIsAlliedWithItself()
        {
            FactionDefinition sand = Faction("Sand");
            Assert.AreEqual(FactionRelationship.Allied, table.Get(sand, sand));
        }

        // ── Step 4: no rows, no defaults ───────────────────────────────────────────

        [Test]
        public void TwoOrdinaryFactionsWithNoRowAreNeutral()
        {
            Assert.AreEqual(FactionRelationship.Neutral, table.Get(Faction("Sand"), Faction("Sky")));
        }

        // ── Step 3: the default stance ─────────────────────────────────────────────

        [Test]
        public void AHostileDefaultOnEitherSideIsAFight()
        {
            FactionDefinition clankers = Faction("Clankers", FactionRelationship.Hostile);
            FactionDefinition humans = Faction("Humans");

            Assert.AreEqual(FactionRelationship.Hostile, table.Get(clankers, humans),
                            "the Clankers' whole design is one checkbox instead of one row per faction");
            Assert.AreEqual(FactionRelationship.Hostile, table.Get(humans, clankers),
                            "and it has to read the same from the other side, or only one of them fights");
        }

        [Test]
        public void AFactionAddedLaterIsHostileToTheClankersWithNoRowAtAll()
        {
            FactionDefinition clankers = Faction("Clankers", FactionRelationship.Hostile);
            Assert.AreEqual(FactionRelationship.Hostile, table.Get(Faction("Mechanics"), clankers),
                            "this is the forgotten-row bug the default exists to prevent");
        }

        [Test]
        public void FriendshipNeedsBothSidesButEnmityNeedsOne()
        {
            FactionDefinition generous = Faction("Generous", FactionRelationship.Allied);
            FactionDefinition ordinary = Faction("Ordinary");
            FactionDefinition alsoGenerous = Faction("AlsoGenerous", FactionRelationship.Allied);

            Assert.AreEqual(FactionRelationship.Neutral, table.Get(generous, ordinary),
                            "nobody is your ally merely because you say so");
            Assert.AreEqual(FactionRelationship.Allied, table.Get(generous, alsoGenerous));
        }

        [Test]
        public void HostileBeatsAlliedWhenTheTwoDefaultsDisagree()
        {
            FactionDefinition clankers = Faction("Clankers", FactionRelationship.Hostile);
            FactionDefinition friendly = Faction("Friendly", FactionRelationship.Allied);

            Assert.AreEqual(FactionRelationship.Hostile, table.Get(clankers, friendly),
                            "a faction that shoots on sight gets shot back whatever the victim thinks");
        }

        // ── Step 2 beats step 3: a row wins ────────────────────────────────────────

        [Test]
        public void ARowBeatsAHostileDefault()
        {
            FactionDefinition clankers = Faction("Clankers", FactionRelationship.Hostile);
            FactionDefinition fauna = Faction("Fauna");
            Rows((clankers, fauna, FactionRelationship.Neutral));

            Assert.AreEqual(FactionRelationship.Neutral, table.Get(clankers, fauna),
                            "\"Clankers shoot people, not animals\" is a Neutral row over a Hostile " +
                            "default — if the default won, every DuneRat on the map would be a target");
            Assert.AreEqual(FactionRelationship.Neutral, table.Get(fauna, clankers));
        }

        [Test]
        public void ARowBeatsANeutralDefaultToo()
        {
            FactionDefinition outlaws = Faction("Outlaws");
            FactionDefinition humans = Faction("Humans");
            Rows((outlaws, humans, FactionRelationship.Hostile));

            Assert.AreEqual(FactionRelationship.Hostile, table.Get(outlaws, humans));
        }

        /// <summary>
        /// The default is a property of the two assets, not of the row list — so changing one must
        /// take effect even though the row count never moved. The index is invalidated by row
        /// count, which is why the default is computed on the cache miss and not baked in.
        /// </summary>
        [Test]
        public void ChangingADefaultTakesEffectWithoutTouchingTheRows()
        {
            FactionDefinition a = Faction("A");
            FactionDefinition b = Faction("B");
            FactionDefinition c = Faction("C");
            Rows((a, b, FactionRelationship.Hostile));

            Assert.AreEqual(FactionRelationship.Neutral, table.Get(a, c));

            a.defaultStance = FactionRelationship.Hostile;

            Assert.AreEqual(FactionRelationship.Hostile, table.Get(a, c),
                            "a stale index served the old answer until somebody happened to add a row");
        }

        // ── The resolver: grudge beats stance ──────────────────────────────────────

        [Test]
        public void AProvokedAgentIsHostileToItsAttackerAndToNobodyElse()
        {
            FactionDefinition fauna = Faction("Fauna");
            FactionDefinition humans = Faction("Humans");

            var creature = new GameObject("creature");
            var shooter = new GameObject("shooter");
            var bystander = new GameObject("bystander");

            try
            {
                EntityFaction creatureSide = Side(creature, fauna);
                EntityFaction shooterSide = Side(shooter, humans);
                EntityFaction bystanderSide = Side(bystander, humans);

                Assert.AreEqual(FactionRelationship.Neutral,
                                FactionRelations.Resolve(creatureSide, shooterSide),
                                "precondition: Fauna and Humans have no row and no default");

                creature.AddComponent<ProvocationModule>().Provoke(shooter.transform, announce: false);

                Assert.AreEqual(FactionRelationship.Hostile,
                                FactionRelations.Resolve(creatureSide, shooterSide),
                                "a creature you shot is your enemy whatever its faction thinks");
                Assert.AreEqual(FactionRelationship.Neutral,
                                FactionRelations.Resolve(creatureSide, bystanderSide),
                                "and only yours — its neighbours need an AlertBroadcaster to join in");
                Assert.AreEqual(FactionRelationship.Neutral,
                                FactionRelations.Resolve(shooterSide, creatureSide),
                                "the grudge is asymmetric: you are not hostile to what you shot");
            }
            finally
            {
                Object.DestroyImmediate(creature);
                Object.DestroyImmediate(shooter);
                Object.DestroyImmediate(bystander);
            }
        }

        /// <summary>
        /// A hit is routinely reported through a child — a muzzle, a limb, a rider in a saddle — so
        /// the grudge is matched at the root. Otherwise an attacker walks away from its own grudge
        /// by being reported through a different part of itself.
        /// </summary>
        [Test]
        public void AGrudgeIsHeldAgainstTheWholeBodyNotTheChildThatHitYou()
        {
            var creature = new GameObject("creature");
            var shooter = new GameObject("shooter");
            var muzzle = new GameObject("muzzle");

            try
            {
                muzzle.transform.SetParent(shooter.transform);

                EntityFaction creatureSide = Side(creature, Faction("Fauna"));
                EntityFaction shooterSide = Side(shooter, Faction("Humans"));

                creature.AddComponent<ProvocationModule>().Provoke(muzzle.transform, announce: false);

                Assert.AreEqual(FactionRelationship.Hostile,
                                FactionRelations.Resolve(creatureSide, shooterSide));
            }
            finally
            {
                Object.DestroyImmediate(creature);
                Object.DestroyImmediate(shooter);
            }
        }

        private EntityFaction Side(GameObject go, FactionDefinition faction)
        {
            var side = go.AddComponent<EntityFaction>();
            var so = new SerializedObject(side);
            so.FindProperty("faction").objectReferenceValue = faction;
            so.FindProperty("relationshipTable").objectReferenceValue = table;
            so.ApplyModifiedPropertiesWithoutUndo();
            return side;
        }
    }
}
