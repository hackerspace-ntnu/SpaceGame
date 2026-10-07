// What the ledger forgives, decided where a hit or a kill is reported (FactionGoodwillLedger.IsSelfDefence):
// only a war party hunting you -- still, after that party has been released and while its bodies stand.
// Anyone else of a tribe costs goodwill, even one attacking you (user decision 2026-10-07).
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class FactionGoodwillSelfDefenceTests
    {
        private const string Player = "profile-a";

        private readonly List<Object> junk = new();
        private FactionDefinition outlaws, drifters, crew;
        private FactionRelationshipTable table;

        [SetUp]
        public void SetUp()
        {
            outlaws = Faction("Outlaws", FactionRelationship.Hostile);
            drifters = Faction("Drifters", FactionRelationship.Neutral);
            crew = Faction("Crew", FactionRelationship.Neutral);
            table = ScriptableObject.CreateInstance<FactionRelationshipTable>();
            junk.Add(table);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private FactionDefinition Faction(string name, FactionRelationship stance)
        {
            var f = ScriptableObject.CreateInstance<FactionDefinition>();
            f.name = f.factionName = f.ID = name;
            f.defaultStance = stance;
            junk.Add(f);
            return f;
        }

        private EntityFaction Body(string name, FactionDefinition side)
        {
            var go = new GameObject(name);
            junk.Add(go);
            go.AddComponent<HealthComponent>();
            EntityFaction faction = go.AddComponent<EntityFaction>();
            faction.SetFaction(side, table);
            return faction;
        }

        private static AgentTargeting Targeting(EntityFaction npc, EntityFaction target)
        {
            AgentTargeting targeting = npc.gameObject.AddComponent<AgentTargeting>();
            if (target != null) targeting.ForceTarget(target.transform);
            return targeting;
        }

        private static bool IsSelfDefence(EntityFaction victim, EntityFaction attacker, string profileId) =>
            (bool)typeof(FactionGoodwillLedger)
                .GetMethod("IsSelfDefence", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { victim, attacker, profileId });

        // ── Outside a war party nothing is self-defence ────────────────────────────

        [Test]
        public void SomeoneAttackingYouOutsideAWarParty_StillCostsForHitsAndKills()
        {
            // User decision 2026-10-07: kill one nomad, the rest turn on you, and every one you take down
            // costs goodwill. Hits and kills are both decided by IsSelfDefence.
            EntityFaction player = Body("Player", crew);
            EntityFaction raider = Body("Raider", outlaws);
            Assume.That(Targeting(raider, player).IsFightingWith(player.transform), "the test could not aim the raider");

            Assert.IsFalse(IsSelfDefence(raider, player, Player));
        }

        [Test]
        public void ADrifterFightingBack_StillCosts()
        {
            EntityFaction player = Body("Player", crew);
            EntityFaction drifter = Body("Drifter", drifters);
            Assume.That(Targeting(drifter, player).IsFightingWith(player.transform));

            Assert.IsFalse(IsSelfDefence(drifter, player, Player));
        }

        // ── A released war party ───────────────────────────────────────────────────

        [Test]
        public void AReleasedWarPartysSurvivor_StaysFreeToFightOff_UntilItFolds()
        {
            var simGo = new GameObject("Sim");
            junk.Add(simGo);
            NpcWorldSim sim = simGo.AddComponent<NpcWorldSim>();
            var template = new NpcGroupTemplate { id = "sand-war-party", tribe = drifters, runtimeOnly = true, bountyHunters = true };
            typeof(NpcWorldSim).GetField("templates", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(sim, new[] { template });
            typeof(NpcWorldSim).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sim, null);

            NpcGroup party = sim.CreateGroup(template, "warparty:drifters:a:1", Vector3.zero);
            party.QuarryProfileId = Player;
            party.Spawned = true;

            EntityFaction player = Body("Player", crew);
            EntityFaction survivor = Body("Survivor", drifters);
            GroupMembership.Stamp(survivor.gameObject, party, 0, null);

            sim.ReleaseGroup(party.Id);

            Assert.IsTrue(IsSelfDefence(survivor, player, Player),
                          "the war's party was resolved but its last nomad is still shooting at the quarry");
        }
    }
}
