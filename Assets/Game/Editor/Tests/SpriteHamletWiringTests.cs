// The sprite hamlet questline is wired by NAME: steps name QuestActors, QuestSites and ChantCircles by
// string id, and windows name steps by id. Nothing at runtime can tell a typo from a character that
// simply has not streamed in yet — a misspelt id is a step that never finishes, silently. This reads
// every chain, step and site prefab and checks that every name resolves.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Gameplay.SpriteHamlet;

namespace SpaceGame.EditorTools
{
    public class SpriteHamletWiringTests
    {
        private const string ChainDir = "Assets/Game/ScriptableObjects/Objectives/SpriteHamlet/";
        private static readonly string[] Chains = { "SpriteHamletChain", "FavourFallenLogChain", "FavourAcornBasketChain", "FavourSporeWellChain" };
        private static readonly string[] Prefabs =
        {
            "Assets/Game/Prefabs/Environment/SpriteHamlet/SpriteHamlet.prefab",
            "Assets/Game/Prefabs/Environment/SpriteHamlet/WitchGlade.prefab",
            "Assets/Game/Prefabs/Agents/Characters/SpriteHamlet/Morwen.prefab",
        };

        private static IEnumerable<ObjectiveChain> LoadChains() =>
            Chains.Select(c => AssetDatabase.LoadAssetAtPath<ObjectiveChain>(ChainDir + c + ".asset"));

        private static IEnumerable<GameObject> LoadPrefabs() => Prefabs.Select(AssetDatabase.LoadAssetAtPath<GameObject>);

        private static HashSet<string> Ids<T>(System.Func<T, string> id) where T : Component =>
            new(LoadPrefabs().SelectMany(p => p.GetComponentsInChildren<T>(true)).Select(id));

        private static IEnumerable<(ObjectiveStep step, string field, string value)> StepStrings(params string[] fields)
        {
            foreach (ObjectiveChain chain in LoadChains())
            foreach (ObjectiveStep step in chain.Steps)
            {
                var so = new SerializedObject(step);
                foreach (string field in fields)
                {
                    SerializedProperty p = so.FindProperty(field);
                    if (p == null) continue;
                    if (p.isArray && p.propertyType != SerializedPropertyType.String)
                    {
                        for (int i = 0; i < p.arraySize; i++) yield return (step, field, p.GetArrayElementAtIndex(i).stringValue);
                    }
                    else if (p.propertyType == SerializedPropertyType.String && !string.IsNullOrEmpty(p.stringValue))
                    {
                        yield return (step, field, p.stringValue);
                    }
                }
            }
        }

        [Test]
        public void EveryChainAndPrefabExists()
        {
            foreach (string c in Chains) Assert.That(AssetDatabase.LoadAssetAtPath<ObjectiveChain>(ChainDir + c + ".asset"), Is.Not.Null, c);
            foreach (string p in Prefabs) Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(p), Is.Not.Null, p);
        }

        [Test]
        public void EveryActorAStepNamesExists()
        {
            HashSet<string> actors = Ids<QuestActor>(a => a.ActorId);
            var missing = StepStrings("actorId", "escortIds")
                .Where(s => !actors.Contains(s.value)).Select(s => $"{s.step.name}.{s.field} = '{s.value}'").ToList();
            Assert.That(missing, Is.Empty, "Steps name actors no prefab carries");
        }

        [Test]
        public void EverySiteAStepNamesExists()
        {
            HashSet<string> sites = Ids<QuestSite>(s => s.SiteId);
            var missing = StepStrings("siteId", "fallbackSiteId")
                .Where(s => !sites.Contains(s.value)).Select(s => $"{s.step.name}.{s.field} = '{s.value}'").ToList();
            Assert.That(missing, Is.Empty, "Steps name sites no prefab carries");
        }

        [Test]
        public void EveryChantCircleIsNamedAndManned()
        {
            HashSet<string> actors = Ids<QuestActor>(a => a.ActorId);
            var circles = LoadPrefabs().SelectMany(p => p.GetComponentsInChildren<ChantCircle>(true)).ToList();
            HashSet<string> circleIds = new(circles.Select(c => c.CircleId));

            var missing = StepStrings("circleId").Where(s => !circleIds.Contains(s.value)).Select(s => s.value).ToList();
            Assert.That(missing, Is.Empty, "Chant steps name circles no prefab carries");

            foreach (ChantCircle circle in circles)
            {
                var so = new SerializedObject(circle);
                var keepers = so.FindProperty("keeperIds");
                Assert.That(so.FindProperty("stoneOffsets").arraySize, Is.EqualTo(keepers.arraySize), $"{circle.CircleId}: one stone per keeper");
                for (int i = 0; i < keepers.arraySize; i++)
                    Assert.That(actors, Does.Contain(keepers.GetArrayElementAtIndex(i).stringValue), $"{circle.CircleId} keeper");
            }
        }

        [Test]
        public void EveryRueStrikeNamesARealTarget()
        {
            HashSet<string> actors = Ids<QuestActor>(a => a.ActorId);
            var missing = Directory.GetFiles("Assets/Game/Prefabs/Agents/Characters/SpriteHamlet", "*.prefab")
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .SelectMany(p => p.GetComponentsInChildren<SpaceGame.Agents.RueStrikeModule>(true))
                .Select(m => (m.name, id: new SerializedObject(m).FindProperty("targetActorId").stringValue))
                .Where(m => !actors.Contains(m.id)).Select(m => $"{m.name} strikes '{m.id}'").ToList();

            Assert.That(missing, Is.Empty, "Keepers carry rue for an actor no prefab carries");
        }

        [Test]
        public void StasisLetsTheHamletsOwnThrough()
        {
            var hamlets = AssetDatabase.LoadAssetAtPath<Object>("Assets/Game/ScriptableObjects/Factions/Core/HamletsFaction.asset");
            var dome = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs[0]).GetComponentInChildren<StasisDome>(true);
            var bolt = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Items/Artifacts/Gadgets/StasisBolt.prefab")
                .GetComponent<SpaceGame.Items.StasisBolt>();

            foreach (Component layer in new Component[] { dome, bolt })
            {
                Assert.That(layer, Is.Not.Null);
                var list = new SerializedObject(layer).FindProperty("passes.factions");
                bool listed = Enumerable.Range(0, list.arraySize).Any(i => list.GetArrayElementAtIndex(i).objectReferenceValue == hamlets);
                Assert.That(listed, $"{layer.GetType().Name} would freeze the hamlet's own keepers and villagers");
            }
        }

        /// <summary>
        /// The witch's thorns come up out of the ground under the player, so there is no cover and no
        /// angle — the ground warning (the earth heaving where they will break through) is the whole
        /// of the counterplay. A cast with no telegraph is damage on a timer, and nothing at runtime
        /// says so.
        /// </summary>
        [Test]
        public void TheWitchsStrikeIsWarnedAndDrawn()
        {
            var witch = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Agents/Characters/SpriteHamlet/Morwen.prefab");
            var cast = witch.GetComponent<SpaceGame.Agents.ConjurerCastModule>();
            Assert.That(cast, Is.Not.Null, "Morwen has no cast");

            var so = new SerializedObject(cast);
            var telegraph = so.FindProperty("telegraphPrefab").objectReferenceValue as GameObject;
            var strike = so.FindProperty("strikeVFXPrefab").objectReferenceValue as GameObject;

            Assert.That(telegraph != null && telegraph.GetComponent<SpaceGame.Gameplay.StrikeTelegraph>() != null,
                        "her cast has no ground warning, so it cannot be dodged");
            Assert.That(strike != null && strike.GetComponentInChildren<SpaceGame.Gameplay.IStrikeEffect>() != null,
                        "her strike prefab has no IStrikeEffect, so nothing is drawn where it lands");
        }

        /// <summary>
        /// The fight is wired by keeper id three times over — the witch's WitchFight, the glade's
        /// WitchArena posts and the keepers' own QuestActors — and a keeper missing from any of them
        /// either never fails the fight, never climbs her hat, or stands wherever it was set.
        /// </summary>
        [Test]
        public void EveryKeeperOfTheFightHasAPost()
        {
            HashSet<string> actors = Ids<QuestActor>(a => a.ActorId);
            var witch = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs[2]);
            var keeperIds = new SerializedObject(witch.GetComponent<WitchFight>()).FindProperty("keeperIds");
            var arena = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs[1]).GetComponentInChildren<WitchArena>(true);
            Assert.That(arena, Is.Not.Null, "the glade has no WitchArena");

            Assert.That(keeperIds.arraySize, Is.GreaterThan(0), "WitchFight names no keepers");
            for (int i = 0; i < keeperIds.arraySize; i++)
            {
                string id = keeperIds.GetArrayElementAtIndex(i).stringValue;
                Assert.That(actors, Does.Contain(id), $"WitchFight names '{id}', which no prefab carries");
                Assert.That(arena.TryGetPost(id, out _), $"the arena has no post for '{id}'");
            }
        }

        [Test]
        public void EveryKeeperCanBeCarriedPoisonedAndThrows()
        {
            foreach (string path in Directory.GetFiles("Assets/Game/Prefabs/Agents/Characters/SpriteHamlet", "Keeper_*.prefab"))
            {
                var keeper = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(keeper.GetComponent<PoisonTally>(), Is.Not.Null, $"{keeper.name} cannot be poisoned");
                Assert.That(keeper.GetComponent<KeeperPose>(), Is.Not.Null, $"{keeper.name} has no poses");
                Assert.That(keeper.GetComponent<PoisonPips>(), Is.Not.Null, $"{keeper.name} shows no pips");

                var carry = keeper.GetComponent<KeeperCarry>();
                Assert.That(carry, Is.Not.Null, $"{keeper.name} cannot be carried");
                var handle = new SerializedObject(carry).FindProperty("liftHandle").objectReferenceValue as Collider;
                Assert.That(handle != null && handle.isTrigger && handle.GetComponent<KeeperLiftHandle>() != null,
                            $"{keeper.name}'s lift handle is not a trigger with a KeeperLiftHandle — the crosshair finds its dialog instead");

                var strike = keeper.GetComponent<SpaceGame.Agents.RueStrikeModule>();
                Assert.That(new SerializedObject(strike).FindProperty("bombPrefab").objectReferenceValue, Is.Not.Null,
                            $"{keeper.name} has nothing to throw");
            }
        }

        [Test]
        public void TheWitchHasHerWholeFight()
        {
            var witch = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs[2]);
            var spiral = witch.GetComponent<SpaceGame.Agents.PoisonSpiralModule>();
            Assert.That(spiral, Is.Not.Null, "no spin attack");
            Assert.That(new SerializedObject(spiral).FindProperty("ballPrefab").objectReferenceValue, Is.Not.Null, "her spin throws nothing");

            var cloak = witch.GetComponent<ShimmerCloak>();
            Assert.That(cloak != null && new SerializedObject(cloak).FindProperty("shimmerMaterial").objectReferenceValue != null,
                        "she never hides");

            var fight = new SerializedObject(witch.GetComponent<WitchFight>());
            var failed = fight.FindProperty("failedCutscene").objectReferenceValue as SpaceGame.Presentation.QuestFailedCutscene;
            Assert.That(failed, Is.Not.Null, "losing the fight shows nothing");
            Assert.That(new SerializedObject(failed).FindProperty("windowMaterial").objectReferenceValue, Is.Not.Null,
                        "the failure screen has no window material — it would be solid black");
            Assert.That(fight.FindProperty("finisherCutscene").objectReferenceValue, Is.Not.Null, "winning the fight shows nothing");
        }

        [Test]
        public void TheWitchShowsOneHatHerbPerSlice()
        {
            var witch = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Agents/Characters/SpriteHamlet/Morwen.prefab");
            var life = witch.GetComponent<SpaceGame.Agents.HerbBoundLife>();
            Assert.That(life, Is.Not.Null, "Morwen has no HerbBoundLife");

            var so = new SerializedObject(life);
            var visuals = so.FindProperty("herbVisuals");
            Assert.That(visuals.arraySize, Is.EqualTo(so.FindProperty("herbs").intValue), "one hat herb per slice of her life");
            for (int i = 0; i < visuals.arraySize; i++)
                Assert.That(visuals.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null, $"herb visual {i}");
        }

        [Test]
        public void EveryWindowNamesRealSteps()
        {
            var bad = new List<string>();
            foreach (GameObject prefab in LoadPrefabs().Concat(Directory.GetFiles("Assets/Game/Prefabs/Agents", "*.prefab", SearchOption.AllDirectories)
                         .Select(AssetDatabase.LoadAssetAtPath<GameObject>)))
            {
                if (prefab == null) continue;
                foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null) continue;
                    var it = new SerializedObject(component).GetIterator();
                    while (it.Next(true))
                    {
                        if (it.type != nameof(QuestStepWindow)) continue;
                        var chain = it.FindPropertyRelative("chain").objectReferenceValue as ObjectiveChain;
                        if (chain == null) continue;
                        foreach (string end in new[] { "from", "until" })
                        {
                            string id = it.FindPropertyRelative(end).stringValue;
                            if (!string.IsNullOrEmpty(id) && id != QuestStepWindow.CompleteToken && chain.IndexOf(id) < 0)
                                bad.Add($"{prefab.name}/{component.GetType().Name}.{it.propertyPath}.{end} = '{id}' (not in {chain.Id})");
                        }
                    }
                }
            }

            Assert.That(bad, Is.Empty);
        }

        [Test]
        public void EveryChainIsInTheDirectorsQuestCatalog()
        {
            string scene = File.ReadAllText("Assets/Game/Scenes/world/persistentScene.unity");
            foreach (string c in Chains)
            {
                string guid = AssetDatabase.AssetPathToGUID(ChainDir + c + ".asset");
                Assert.That(scene, Does.Contain(guid), $"{c} is not in the director's quest catalog");
            }
        }
    }
}
