// Bounty hunts: every hunt asset is complete enough to play (poster, tips, reward, a site, one owner, a cast whose
// every member spawns for clients and survives a reload), the rules every machine works out alike, the resident lines
// that carry the tips, and the hunt book's save round trip, old saves included.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Agents.Residents;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;
using SpaceGame.World;
using SpaceGame.World.Bounties;
using SpaceGame.World.Posters;

namespace SpaceGame.EditorTools
{
    public class BountyTests
    {
        private const string NetworkPrefabsPath = "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";
        private static readonly string[] LineFiles =
        {
            "Assets/Game/ScriptableObjects/Residents/Lines/NomadLines.txt",
            "Assets/Game/ScriptableObjects/Residents/Lines/ColonyLines.txt",
        };

        // ── the hunt assets ──────────────────────────────────────────────────────────────────────

        [Test]
        public void EveryHunt_HasAPosterTipsARewardASiteAndAnOwner()
        {
            var ids = new HashSet<string>();
            foreach (BountyHunt hunt in BountyCatalog.All)
            {
                Assert.IsFalse(string.IsNullOrEmpty(hunt.Id), $"{hunt.name}: no id");
                Assert.IsTrue(ids.Add(hunt.Id), $"{hunt.name}: id {hunt.Id} is shared — a copied asset; give it a new one");
                Assert.IsNotNull(hunt.Poster, $"{hunt.name}: no poster");
                Assert.IsFalse(string.IsNullOrEmpty(hunt.TargetName), $"{hunt.name}: the poster prints no name");
                Assert.GreaterOrEqual(hunt.Tips.Length, 2, $"{hunt.name}: needs at least two tips, so asking around pays");
                foreach (string tip in hunt.Tips)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(tip), $"{hunt.name}: an empty tip");
                    StringAssert.DoesNotContain("{", tip.Replace(BountyHunt.TargetToken, string.Empty),
                                                $"{hunt.name}: a tip may only use {BountyHunt.TargetToken}");
                }
                Assert.Greater(hunt.Rewards.Length, 0, $"{hunt.name}: pays nothing");
                foreach (BountyHunt.Reward reward in hunt.Rewards)
                {
                    Assert.IsNotNull(reward.item, $"{hunt.name}: a reward with no item");
                    Assert.IsNotNull(reward.item.itemPrefab, $"{hunt.name}: '{reward.item.name}' has no prefab to drop");
                }
                Assert.IsTrue(hunt.HasSite, $"{hunt.name}: no site. Drop a BountyHuntSite in a chunk scene, point it at this hunt, save the scene");

                // Whether exactly one settlement lists it can only be seen by opening every chunk scene; the recorder
                // reports a second owner when its scene is saved, so the recorded id is what is checked here.
                Assert.IsFalse(string.IsNullOrEmpty(hunt.OwnerSettlementId),
                               $"{hunt.name}: no owner, so it can never be taken. Add it to a settlement's SettlementBounties and save that scene");
            }
        }

        [Test]
        public void EveryCast_HasOneTarget_AndEveryEntryCanBeStood()
        {
            foreach (BountyHunt hunt in BountyCatalog.All)
            {
                Assert.AreEqual(1, hunt.Cast.Count(e => e != null && e.who == CastWho.Target),
                                $"{hunt.name}: a cast needs exactly one Target entry, the poster's own agent");

                for (int i = 0; i < hunt.Cast.Length; i++)
                {
                    BountyHunt.CastEntry entry = hunt.Cast[i];
                    string where = $"{hunt.name} cast[{i}]";
                    Assert.IsNotNull(entry, $"{where}: empty");
                    Assert.LessOrEqual(entry.minCount, entry.maxCount, $"{where}: minCount is above maxCount");

                    switch (entry.who)
                    {
                        case CastWho.Target:
                            Assert.AreEqual(1, entry.minCount, $"{where}: there is one wanted person");
                            Assert.AreEqual(1, entry.maxCount, $"{where}: there is one wanted person");
                            break;
                        case CastWho.Prefab:
                            Assert.IsNotNull(entry.prefab, $"{where}: a Prefab entry with no prefab");
                            break;
                        case CastWho.RosterRole:
                            Assert.IsNotNull(entry.roster, $"{where}: a RosterRole entry with no roster");
                            Assert.AreNotEqual(RosterRole.Rider, entry.role,
                                               $"{where}: a Rider entry deals a MOUNT carrying an NpcPassenger, not a person");
                            Assert.IsNotEmpty(Dealable(entry).ToList(), $"{where}: {entry.roster.name} has no {entry.role} it can deal");
                            break;
                    }

                    Assert.IsNotNull(entry.faction,
                                     $"{where}: no faction, so they fight for whoever their prefab says — possibly the town that owns the hunt");
                }
            }
        }

        [Test]
        public void EveryCastMember_SpawnsForClients_AndSurvivesAReload()
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            foreach (BountyHunt hunt in BountyCatalog.All)
            {
                foreach (BountyHunt.CastEntry entry in hunt.Cast.Where(e => e != null))
                {
                    foreach (GameObject prefab in Spawnable(hunt, entry))
                    {
                        Assert.IsNotNull(prefab, $"{hunt.name}: a {entry.who} entry spawns nothing (its poster prints no agent?)");
                        string path = AssetDatabase.GetAssetPath(prefab);
                        Assert.IsFalse(string.IsNullOrEmpty(path), $"{hunt.name}: '{prefab.name}' is a scene object, not a prefab");

                        Assert.IsTrue(list.Contains(prefab), $"{path} spawns on the host only unless it is a registered network prefab");
                        var identity = prefab.GetComponent<SaveableEntity>();
                        Assert.IsNotNull(identity, $"{path}: no SaveableEntity, so it vanishes on reload");
                        Assert.AreEqual(AssetDatabase.AssetPathToGUID(path), identity.PrefabId,
                                        $"{path}: a stale prefabId restores the wrong body");
                        Assert.IsNotNull(prefab.GetComponent<EntityFaction>(),
                                         $"{path}: no EntityFaction, so it can neither take the hunt's faction nor surrender");
                    }
                }

                GameObject target = hunt.Target;
                if (target == null) continue;
                Assert.IsNotNull(target.GetComponent<HealthComponent>(),
                                 $"{AssetDatabase.GetAssetPath(target)}: no health, so it can be neither beaten nor killed");
            }
        }

        [Test]
        public void EveryHuntsSite_IsInsideItsOwnersBounds()
        {
            // The owner's position is not on the hunt: it comes from the baked site catalogs, which list every town.
            var towns = new Dictionary<string, WorldSiteCatalog.TownEntry>();
            foreach (WorldStreamingConfig config in WorldChunkScenes.AllConfigs())
                if (config.siteCatalog != null)
                    foreach (WorldSiteCatalog.TownEntry town in config.siteCatalog.towns)
                        if (!string.IsNullOrEmpty(town.id)) towns[town.id] = town;

            foreach (BountyHunt hunt in BountyCatalog.All)
            {
                if (!hunt.HasSite || string.IsNullOrEmpty(hunt.OwnerSettlementId)) continue;   // reported above

                Assert.IsTrue(towns.TryGetValue(hunt.OwnerSettlementId, out WorldSiteCatalog.TownEntry owner),
                              $"{hunt.name}: its owner {hunt.OwnerSettlementId} is in no site catalog. Run {WorldSiteCatalogBaker.MenuPath}");

                Assert.IsTrue(BountyRules.FlatWithin(hunt.SitePosition, owner.position, hunt.BoundsRadius),
                              $"{hunt.name}: its site is {Vector3.Distance(hunt.SitePosition, owner.position):0} m from {owner.name}, " +
                              $"outside its {hunt.BoundsRadius:0} m bounds, so the crew could never reach it without the hunt expiring");
            }
        }

        /// <summary>Every prefab <paramref name="entry"/> can put at the site.</summary>
        private static IEnumerable<GameObject> Spawnable(BountyHunt hunt, BountyHunt.CastEntry entry)
        {
            switch (entry.who)
            {
                case CastWho.Target: return new[] { hunt.Target };
                case CastWho.Prefab: return new[] { entry.prefab };
                default: return entry.roster != null ? Dealable(entry) : Enumerable.Empty<GameObject>();
            }
        }

        /// <summary>The roster members a RosterRole entry can be dealt: <see cref="FactionRoster.Deal"/>'s candidates.</summary>
        private static IEnumerable<GameObject> Dealable(BountyHunt.CastEntry entry) =>
            entry.roster.members.Where(m => m != null && m.prefab != null && m.role == entry.role && m.weight > 0f)
                                .Select(m => m.prefab);

        // ── the rules ────────────────────────────────────────────────────────────────────────────

        [Test]
        public void ATipIsTheSameForTheSameResidentAndSpreadsAcrossResidents()
        {
            const string id = "test-bounty";
            var given = new HashSet<int>();
            for (int resident = 0; resident < 16; resident++)
            {
                int tip = BountyRules.TipIndex(id, resident, 3);
                Assert.That(tip, Is.InRange(0, 2));
                Assert.AreEqual(tip, BountyRules.TipIndex(id, resident, 3));
                given.Add(tip);
            }
            Assert.Greater(given.Count, 1, "every resident gives the same tip, so asking around is pointless");
        }

        [Test]
        public void TheSearchAreaIsNearTheSiteButNeverOnIt()
        {
            var site = new Vector3(100f, 5f, -40f);
            Vector3 centre = BountyRules.SearchCentre("abc", site, 90f);
            float off = Vector3.Distance(centre, site);
            Assert.That(off, Is.InRange(0.4f * 90f - 0.01f, 90f + 0.01f));
            Assert.AreEqual(centre, BountyRules.SearchCentre("abc", site, 90f));
        }

        [Test]
        public void TheOtherStatePaysAShareRoundedDown()
        {
            Assert.AreEqual(4, BountyRules.Payout(4, asWanted: true, 0.5f));
            Assert.AreEqual(2, BountyRules.Payout(4, asWanted: false, 0.5f));
            Assert.AreEqual(0, BountyRules.Payout(1, asWanted: false, 0.5f));
            Assert.IsTrue(BountyRules.BroughtAsWanted(WantedStatus.Alive, dead: false));
            Assert.IsFalse(BountyRules.BroughtAsWanted(WantedStatus.Alive, dead: true));
            Assert.IsTrue(BountyRules.BroughtAsWanted(WantedStatus.Dead, dead: true));
        }

        [Test]
        public void OneHuntAtATime()
        {
            BountyProgress none = default;
            Assert.IsTrue(BountyRules.CanTake(none, 3));
            Assert.IsFalse(BountyRules.CanTake(new BountyProgress { Open = 1 }, 3), "a second hunt while one is open");
            Assert.IsFalse(BountyRules.CanTake(none, -1));
        }

        [Test]
        public void ACastsSize_IsFixedPerWorld_AndWithinItsRange()
        {
            int seed = BountyRules.CastSeed("world-a", "hunt");
            Assert.AreEqual(seed, BountyRules.CastSeed("world-a", "hunt"), "a reload would reshuffle who stands at the site");
            for (int entry = 0; entry < 8; entry++)
            {
                int count = BountyRules.CastCount(seed, entry, 2, 5);
                Assert.That(count, Is.InRange(2, 5));
                Assert.AreEqual(count, BountyRules.CastCount(seed, entry, 2, 5));
            }
            Assert.AreEqual(3, BountyRules.CastCount(seed, 0, 3, 3));
        }

        [Test]
        public void ASpotKey_ReadsBackItsTagAndPlace()
        {
            Assert.IsTrue(BountyRules.TryReadSpotKey(BountyRules.SpotKey("gate#north", 2), out string tag, out int place));
            Assert.AreEqual("gate#north", tag, "a tag holding the separator must survive");
            Assert.AreEqual(2, place);
            Assert.IsFalse(BountyRules.TryReadSpotKey(string.Empty, out _, out _), "an empty key is a scattered piece");
        }

        [Test]
        public void AClosedHuntStaysClosed_AndRemembersHowItEnded()
        {
            var host = new GameObject("BountyBoard");
            try
            {
                var board = host.AddComponent<BountyBoard>();
                board.Restore(null, BountyPhase.Asking, Vector3.zero, 0f, Vector3.zero, new BountyHuntRunState(),
                              new[] { new ClosedHunt(2, BountyClosure.Expired), new ClosedHunt(5, BountyClosure.Claimed) });

                Assert.IsTrue(board.IsClosed(2, out BountyClosure expired));
                Assert.AreEqual(BountyClosure.Expired, expired);
                Assert.IsTrue(board.IsClosed(5, out BountyClosure claimed));
                Assert.AreEqual(BountyClosure.Claimed, claimed);
                Assert.IsFalse(board.IsClosed(3, out _), "a hunt never taken is open to take");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ATownsNoticeBoard_ShowsTheOpenHunt_ThenNewestOffers_ThenEndedOnes()
        {
            if (BountyCatalog.All.Count == 0) Assert.Ignore("No bounty assets in Resources/Bounties.");

            BountyHunt listed = BountyCatalog.All[0];
            var older = ScriptableObject.CreateInstance<BountyHunt>();
            var newer = ScriptableObject.CreateInstance<BountyHunt>();
            var host = new GameObject("BountyBoard");
            var townObject = new GameObject("Town");
            try
            {
                var board = host.AddComponent<BountyBoard>();
                var town = townObject.AddComponent<SettlementBounties>();
                var so = new SerializedObject(town);
                SerializedProperty entries = so.FindProperty("entries");
                BountyHunt[] order = { listed, older, newer };
                entries.arraySize = order.Length;
                for (int i = 0; i < order.Length; i++)
                    entries.GetArrayElementAtIndex(i).FindPropertyRelative("hunt").objectReferenceValue = order[i];
                so.ApplyModifiedPropertiesWithoutUndo();

                var shown = new List<BountyHunt>();
                int place = BountyCatalog.IndexOf(listed);
                board.Restore(null, BountyPhase.Asking, Vector3.zero, 0f, Vector3.zero, new BountyHuntRunState(),
                              new[] { new ClosedHunt(place, BountyClosure.Claimed) });
                BountyNoticeBoard.Choose(town, board, null, 3, shown);
                CollectionAssert.AreEqual(new[] { newer, older, listed }, shown, "offers newest first, then the ended hunt, stamped");

                BountyNoticeBoard.Choose(town, board, null, 1, shown);
                CollectionAssert.AreEqual(new[] { newer }, shown, "a full board keeps the newest offer, not the ended hunt");

                board.Restore(listed, BountyPhase.Located, Vector3.zero, 60f, Vector3.zero, new BountyHuntRunState(), new ClosedHunt[0]);
                BountyNoticeBoard.Choose(town, board, null, 2, shown);
                CollectionAssert.AreEqual(new[] { listed, newer }, shown, "the open hunt hangs first, so the crew can always find it");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(townObject);
                Object.DestroyImmediate(older);
                Object.DestroyImmediate(newer);
            }
        }

        // ── resident lines ───────────────────────────────────────────────────────────────────────

        [Test]
        public void EveryCultureGivesATipForEveryStance_AndThanksForBoth()
        {
            foreach (string file in LineFiles)
            {
                LineTable table = LineTable.Parse(File.ReadAllText(file));
                Assert.IsEmpty(table.Errors, string.Join("\n", table.Errors));
                foreach (Stance stance in System.Enum.GetValues(typeof(Stance)))
                {
                    var ask = new LineQuery { topic = Topic.Whereabouts, stance = stance };
                    LineRow tip = LineMatcher.Pick(table, in ask, null, out _);
                    Assert.IsNotNull(tip, $"{file}: no tip for a {stance} resident");
                    StringAssert.Contains("{tip}", tip.text, $"{file}: {tip.key} gives no tip");
                }

                foreach (Observation brought in new[] { Observation.BroughtInAlive, Observation.BroughtInDead })
                foreach (Topic topic in new[] { Topic.Remark, Topic.Gossip, Topic.Rumour })
                {
                    var query = new LineQuery { topic = topic, observation = brought };
                    LineRow row = LineMatcher.Pick(table, in query, null, out _);
                    Assert.IsNotNull(row, $"{file}: no {topic} line for {brought}");
                    Assert.AreEqual(brought, row.observation, $"{file}: {topic} about {brought} falls back to {row.key}");
                }
            }
        }

        [Test]
        public void WithNoHuntOpen_ATipLineStillSaysAWholeSentence()
        {
            Assert.AreEqual("Looking for that outlaw? I haven't heard a thing.",
                            SpeechTokens.Resolve("Looking for {target}? {tip}", null, null));
        }

        [Test]
        public void HandingInIsKindness_NotHarm()
        {
            Assert.IsFalse(Favor.IsHarm(ActKind.BroughtInAlive));
            Assert.IsFalse(Favor.IsHarm(ActKind.BroughtInDead));
            Assert.IsTrue(Favor.IsHarm(ActKind.Hit));
            var tuning = ScriptableObject.CreateInstance<ResidentTuning>();
            Assert.Greater(Favor.ValueOf(ActKind.BroughtInAlive, tuning), Favor.ValueOf(ActKind.BroughtInDead, tuning),
                           "alive is the harder deed and should count for more");
            Object.DestroyImmediate(tuning);
        }

        // ── the hunt book's save ─────────────────────────────────────────────────────────────────

        [Test]
        public void AnOpenHunt_SurvivesASaveAndLoad_WithItsCastAndClock()
        {
            if (BountyCatalog.All.Count == 0) Assert.Ignore("No hunt assets in Resources/Bounties.");

            BountyHunt open = BountyCatalog.All[0];
            var run = new BountyHuntRunState
            {
                spawned = true,
                elapsed = 1234.5f,
                outsideFor = 37f,
                pieces =
                {
                    new BountyHuntRunState.HuntPiece { body = Entity("target"), target = true, spot = BountyRules.SpotKey("hideout", 0) },
                    new BountyHuntRunState.HuntPiece { body = Entity("guard"), target = false, spot = string.Empty },
                },
                pendingDespawn = { Entity("left-behind") },
            };

            WithReloadedBoard(board => board.Restore(open, BountyPhase.Located, new Vector3(10f, 2f, -3f), 75f, open.SitePosition,
                                                     run, new List<ClosedHunt>()),
                              (saved, reloaded) =>
                              {
                                  Assert.AreEqual(saved.Progress, reloaded.Progress);
                                  Assert.AreEqual(open, reloaded.Open);
                                  Assert.IsTrue(reloaded.Run.spawned, "a reload would stand the cast a second time");
                                  CollectionAssert.AreEqual(run.pieces, reloaded.Run.pieces, "the cast is lost: a reload strands them unwatched");
                                  Assert.AreEqual(run.elapsed, reloaded.Run.elapsed, "a reload resets the hunt's clock");
                                  Assert.AreEqual(run.outsideFor, reloaded.Run.outsideFor, "a reload resets the out-of-bounds count");
                                  CollectionAssert.AreEqual(run.pendingDespawn, reloaded.Run.pendingDespawn);
                              });
        }

        [Test]
        public void ClosedHunts_AndTheirLeftoverCast_SurviveASaveAndLoad_WithNoHuntOpen()
        {
            if (BountyCatalog.All.Count == 0) Assert.Ignore("No hunt assets in Resources/Bounties.");

            var closed = BountyCatalog.All.Select((hunt, place) => new ClosedHunt(place, place % 2 == 0 ? BountyClosure.Expired : BountyClosure.Claimed))
                                          .ToList();
            var run = new BountyHuntRunState { pendingDespawn = { Entity("left-behind") } };

            WithReloadedBoard(board => board.Restore(null, BountyPhase.Asking, Vector3.zero, 0f, Vector3.zero, run, closed),
                              (saved, reloaded) =>
                              {
                                  Assert.IsNull(reloaded.Open);
                                  CollectionAssert.AreEqual(closed, reloaded.Closed, "a closed hunt reopens after a reload");
                                  CollectionAssert.AreEqual(run.pendingDespawn, reloaded.Run.pendingDespawn,
                                                            "an abandoned hunt's people come back after a reload");
                              });
        }

        [Test]
        public void AnOldSave_WithAnOpenBounty_LoadsItsTargetAsTheOnlyCastPiece()
        {
            if (BountyCatalog.All.Count == 0) Assert.Ignore("No hunt assets in Resources/Bounties.");

            BountyHunt open = BountyCatalog.All[0];
            var town = new Vector3(40f, 3f, 12f);
            JObject old = OldSave(open.Id, BountyPhase.Caught, town, 80f, Entity("kessek"), targetSpawned: true);

            WithRestoredBoard(old, reloaded =>
            {
                Assert.AreEqual(open, reloaded.Open);
                Assert.AreEqual(BountyPhase.Caught, reloaded.Progress.Phase);
                Assert.AreEqual(town, reloaded.Progress.Town);
                Assert.AreEqual(80f, reloaded.Progress.TownRadius);
                Assert.IsTrue(reloaded.Run.spawned, "an old save's target would be spawned a second time");
                Assert.AreEqual(1, reloaded.Run.pieces.Count, "the old target must become the run's one piece");
                Assert.IsTrue(reloaded.Run.pieces[0].target, "the old target must still be the one handed in");
                Assert.AreEqual(Entity("kessek"), reloaded.Run.pieces[0].body);
                Assert.IsEmpty(reloaded.Closed);
            });
        }

        [Test]
        public void AnOldSave_WithClaimedBounties_LoadsThemAsClaimedClosures()
        {
            if (BountyCatalog.All.Count == 0) Assert.Ignore("No hunt assets in Resources/Bounties.");

            BountyHunt claimed = BountyCatalog.All[0];
            JObject old = OldSave(null, BountyPhase.Asking, Vector3.zero, 0f, SaveRef.None, targetSpawned: false, claimed.Id);

            WithRestoredBoard(old, reloaded =>
            {
                Assert.IsNull(reloaded.Open);
                CollectionAssert.AreEqual(new[] { new ClosedHunt(BountyCatalog.IndexOf(claimed), BountyClosure.Claimed) },
                                          reloaded.Closed, "a bounty handed in before the update is offered again");
            });
        }

        private static SaveRef Entity(string id) => new SaveRef { Kind = SaveRef.EntityKind, Id = id };

        /// <summary>The <c>bounties</c> record as saves wrote it before hunts had a cast and could expire.</summary>
        private static JObject OldSave(string open, BountyPhase phase, Vector3 town, float townRadius, SaveRef target,
                                       bool targetSpawned, params string[] claimed) =>
            new JObject
            {
                ["open"] = open,
                ["phase"] = phase.ToString(),
                ["settlementId"] = "old-settlement",
                ["town"] = JToken.FromObject(town, SaveSerializer.Serializer),
                ["townRadius"] = townRadius,
                ["target"] = JToken.FromObject(target, SaveSerializer.Serializer),
                ["targetSpawned"] = targetSpawned,
                ["claimed"] = new JArray(claimed),
            };

        /// <summary>Fills a board, saves it through <see cref="BountySaveable"/> as text, and loads that into a second.</summary>
        private static void WithReloadedBoard(System.Action<BountyBoard> fill, System.Action<BountyBoard, BountyBoard> check)
        {
            var first = new GameObject("BountyBookA");
            try
            {
                var board = first.AddComponent<BountyBoard>();
                var saver = first.AddComponent<BountySaveable>();
                fill(board);

                object captured = saver.CaptureState();
                Assert.IsNotNull(captured, "nothing was saved");
                JObject json = JObject.Parse(JObject.FromObject(captured, SaveSerializer.Serializer).ToString());
                WithRestoredBoard(json, reloaded => check(board, reloaded));
            }
            finally
            {
                Object.DestroyImmediate(first);
            }
        }

        private static void WithRestoredBoard(JObject json, System.Action<BountyBoard> check)
        {
            var host = new GameObject("BountyBookB");
            try
            {
                var reloaded = host.AddComponent<BountyBoard>();
                host.AddComponent<BountySaveable>().RestoreState(json);
                check(reloaded);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
