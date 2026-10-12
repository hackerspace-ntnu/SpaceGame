// A war party keeps its losses: a fold no longer re-spawns the dead, a save carries who fell, and the
// party's position is where its standing members are, not the middle of its corpses. A member that
// strays -- aloft for too long, or far from every groupmate -- is given up on, so it can never hold
// off the wipe or the fold of the party it left.
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class WarPartyFoldTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private sealed class TestAirborneCarrier : MonoBehaviour, IAirborneCarrier { }

        private readonly List<Object> junk = new();
        private NpcWorldSim sim;
        private NpcGroupTemplate template;
        private NpcGroup party;

        [SetUp]
        public void SetUp()
        {
            WorldSiteRegistry.Clear();
            var sand = ScriptableObject.CreateInstance<FactionDefinition>();
            junk.Add(sand);
            template = new NpcGroupTemplate { id = "sand-war-party", tribe = sand, runtimeOnly = true, bountyHunters = true };

            sim = Junk("Sim", Vector3.zero).AddComponent<NpcWorldSim>();
            typeof(NpcWorldSim).GetField("templates", Private).SetValue(sim, new[] { template });
            Call("Awake");
            Call("Start");
            var players = (List<Transform>)typeof(NpcWorldSim).GetField("players", Private).GetValue(sim);
            players.Add(Junk("Player", Vector3.zero).transform);

            party = sim.CreateGroup(template, "warparty:sand:p:1", Vector3.zero);
            party.QuarryProfileId = "p";
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
            WorldSiteRegistry.Clear();
        }

        private object Call(string method, params object[] args) =>
            typeof(NpcWorldSim).GetMethod(method, Private).Invoke(sim, args);

        private float StrayTimeout => (float)typeof(NpcWorldSim).GetField("strayTimeout", Private).GetValue(sim);

        private GameObject Junk(string name, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            junk.Add(go);
            return go;
        }

        /// <summary>A spawned fighter of the party at <paramref name="at"/>, stamped with plan index <paramref name="index"/>.</summary>
        private GameObject Fighter(int index, Vector3 at, bool dead = false)
        {
            GameObject member = Junk($"Fighter{index}", at);
            HealthComponent health = member.AddComponent<HealthComponent>();
            GroupMembership.Stamp(member, party, index, null);
            party.Live.Add(member);
            party.Spawned = true;
            if (dead) health.Damage(999);
            return member;
        }

        private void Aloft(GameObject member)
        {
            GameObject craft = Junk("Craft", member.transform.position + Vector3.up * 60f);
            craft.AddComponent<TestAirborneCarrier>();
            member.transform.SetParent(craft.transform, worldPositionStays: true);
        }

        // NpcSpawn.Remove destroys an unseen stray with Object.Destroy, which edit mode refuses (and logs).
        private static void ExpectStrayDestroy() =>
            LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called from edit mode"));

        // ── Losses carry across folds and saves ────────────────────────────────────

        [Test]
        public void AFighterWhoFalls_IsRememberedByTheParty()
        {
            Fighter(3, Vector3.zero, dead: true);

            CollectionAssert.AreEqual(new[] { 3 }, party.Fallen);
        }

        [Test]
        public void HasFallen_CountsTheMember_OrTheRiderInItsSaddle()
        {
            party.Fallen.AddRange(new[] { 2, 5 + GroupMembership.RiderIndexOffset });

            Assert.IsTrue(party.HasFallen(2), "a foot soldier who fell");
            Assert.IsTrue(party.HasFallen(5), "a mount whose rider fell carries nobody into the fight");
            Assert.IsFalse(party.HasFallen(1));
        }

        [Test]
        public void HasFallen_IsNeverTrue_ForAGroupThatIsNoWarParty()
        {
            var caravan = new NpcGroup { Id = "caravan" };
            caravan.Fallen.Add(0);

            Assert.IsFalse(caravan.HasFallen(0), "a caravan that loses people still comes back whole (2026-10-05)");
        }

        [Test]
        public void ASpawnWhoseWholePlanHasFallen_IsAWipeOut_NotAFreshParty()
        {
            GameObject prefab = Junk("Nomad", Vector3.zero);
            party.PlannedOverride = new List<PlannedMember> { new(prefab, true), new(prefab, false) };
            party.Fallen.AddRange(new[] { 0, 1 });

            Call("Spawn", party, template);

            Assert.IsTrue(party.WipedOut);
            Assert.IsFalse(party.Spawned);
            CollectionAssert.IsEmpty(party.Live);
        }

        [Test]
        public void ThePartysPosition_IsWhereItsStandingMembersAre_NotItsCorpses()
        {
            Fighter(0, new Vector3(10f, 0f, 0f));
            Fighter(1, new Vector3(300f, 0f, 0f), dead: true);
            Fighter(2, new Vector3(300f, 0f, 20f), dead: true);

            Call("TickGroup", party, 0.1f);

            Assert.AreEqual(10f, party.Position.x, 0.01f,
                            "corpses dragged the fold and abandon checks toward where the fight was");
        }

        // ── Strays are given up on ─────────────────────────────────────────────────

        [Test]
        public void AStrayAloft_CannotHoldOffTheWipe()
        {
            Fighter(0, new Vector3(10f, 0f, 0f), dead: true);
            GameObject stray = Fighter(1, new Vector3(600f, 0f, 0f));   // circling ground no player is near
            Aloft(stray);

            ExpectStrayDestroy();
            Call("TickGroup", party, StrayTimeout + 1f);
            Call("TickGroup", party, 0.1f);

            Assert.IsFalse(party.Live.Contains(stray));
            Assert.IsTrue(party.WipedOut, "one member circling overhead kept a beaten party in the field for ever");
        }

        [Test]
        public void AStrayFarFromEveryGroupmate_IsGivenUp_AndTheRestFightOn()
        {
            Fighter(0, new Vector3(10f, 0f, 0f));
            Fighter(1, new Vector3(20f, 0f, 0f));
            GameObject stray = Fighter(2, new Vector3(3000f, 0f, 0f));

            ExpectStrayDestroy();
            Call("TickGroup", party, StrayTimeout + 1f);

            Assert.IsFalse(party.Live.Contains(stray));
            Assert.IsFalse(party.Fighters.Contains(stray));
            Assert.AreEqual(2, party.FightersSpawned, "a stray given up is no fighter the party can lose");
            Assert.IsTrue(party.Spawned, "the two who stayed together are still in the field");
            CollectionAssert.IsEmpty(party.Fallen, "given up is not fallen");
        }

        [Test]
        public void AMemberBrieflyAway_IsKept()
        {
            Fighter(0, new Vector3(10f, 0f, 0f));
            GameObject wanderer = Fighter(1, new Vector3(600f, 0f, 0f));   // the centroid stays in the player's range

            Call("TickGroup", party, StrayTimeout * 0.6f);
            wanderer.transform.position = new Vector3(20f, 0f, 0f);
            Call("TickGroup", party, 0.1f);
            wanderer.transform.position = new Vector3(600f, 0f, 0f);
            Call("TickGroup", party, StrayTimeout * 0.6f);

            Assert.IsTrue(party.Live.Contains(wanderer), "the clock restarts once a member is back with the group");
        }

        [Test]
        public void AStrayInSight_IsKept_UntilNobodyCanSeeItGo()
        {
            Fighter(0, new Vector3(10f, 0f, 0f), dead: true);
            GameObject stray = Fighter(1, new Vector3(10f, 0f, 10f));
            Aloft(stray);

            Call("TickGroup", party, StrayTimeout + 1f);

            Assert.IsTrue(party.Live.Contains(stray), "a member must never blink out in front of a player");
        }

        [Test]
        public void TheLastStanding_IsNeverGivenUp_WhileThePartyHasLostNobody()
        {
            GameObject flier = Fighter(0, new Vector3(500f, 0f, 0f));   // out of sight; its flight holds the fold
            Aloft(flier);

            Call("TickGroup", party, StrayTimeout + 1f);

            Assert.IsTrue(party.Live.Contains(flier));
            Assert.IsFalse(party.WipedOut, "giving up an unbeaten party's last member would count as a defeat");
        }

        [Test]
        public void APartyStillAboardItsVessel_IsNoStray()
        {
            template.transport.smallVessel = Junk("Skiff", Vector3.zero);
            party.Delivered = false;
            GameObject a = Fighter(0, new Vector3(10f, 0f, 0f));
            GameObject b = Fighter(1, new Vector3(12f, 0f, 0f), dead: true);
            Aloft(a);

            // TickSpawned alone: TickTransport would mark a party with no vessel object delivered.
            Call("TickSpawned", party, template, StrayTimeout + 1f);

            Assert.IsTrue(party.Live.Contains(a), "flying in is the party travelling, not a member straying");
            Assert.IsTrue(party.Live.Contains(b));
        }
    }
}
