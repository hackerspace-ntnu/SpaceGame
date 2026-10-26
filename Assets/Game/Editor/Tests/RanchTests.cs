// The Raxy ranch: winning an animal's trust before it takes a saddle, counting a herd into a corral, the lasso's
// dally from the saddle, and the quest wired end to end — assets, prefabs, the director's catalog and the chunk scene.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types. Nothing runs
// Awake or OnEnable in EditMode, so the lifecycle methods that matter are invoked by hand.
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Items;
using SpaceGame.World;

namespace SpaceGame.Tests
{
    public class RanchTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private const string SitePath = "Assets/Game/ScriptableObjects/Ranch/RaxyRanchSite.asset";
        private const string QuestPath = "Assets/Game/ScriptableObjects/Objectives/Ranch/RaxyRanchQuest.asset";
        private const string TamePath = "Assets/Game/Prefabs/agents/creatures/Esko_Ranch.prefab";
        private const string WildPath = "Assets/Game/Prefabs/agents/creatures/Esko_Wild.prefab";
        private const string WranglerPath = "Assets/Game/Prefabs/agents/Characters/Raxy/Raxy_Wrangler.prefab";
        private const string CorralPath = "Assets/Game/Prefabs/Environment/Structures/NomadSettlement/RanchCorral.prefab";
        private const string ChunkPath = "Assets/Game/Scenes/world/Chunks/Chunk_6_3.unity";
        private const string PersistentPath = "Assets/Game/Scenes/world/persistentScene.unity";
        private const string SpillexoPath = "Assets/Game/Scenes/world/gameScene.unity";
        private const string NetworkListPath = "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        private readonly List<Object> made = new();
        private readonly List<Behaviour> enabled = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Behaviour behaviour in enabled)
                if (behaviour != null) Invoke(behaviour, "OnDisable");
            enabled.Clear();

            foreach (Object obj in made)
                if (obj != null) Object.DestroyImmediate(obj);
            made.Clear();
        }

        // ── Trust ───────────────────────────────────────────────────────────────

        [Test]
        public void TrustCountsUpToItsLimitAndHolds()
        {
            CreatureTrust trust = Make("Horn").AddComponent<CreatureTrust>();

            trust.Restore(2);
            Assert.That(trust.IsTrusting, Is.False, "two of three pets is not yet trust");
            Assert.That(trust.Trust01, Is.EqualTo(2f / 3f).Within(1e-4f));

            trust.Restore(7);
            Assert.That(trust.Pets, Is.EqualTo(trust.PetsToTrust), "a count past the limit is clamped to it");
            Assert.That(trust.IsTrusting, Is.True);
        }

        [Test]
        public void TrustSurvivesASaveRoundTrip()
        {
            GameObject horn = Make("Horn");
            CreatureTrust trust = horn.AddComponent<CreatureTrust>();
            var saver = horn.AddComponent<CreatureTrustSaveable>();

            Assert.That(saver.CaptureState(), Is.Null, "an animal nobody petted writes nothing");

            trust.Restore(2);
            var state = Newtonsoft.Json.Linq.JObject.FromObject(saver.CaptureState());

            trust.Restore(0);
            saver.RestoreState(state);
            Assert.That(trust.Pets, Is.EqualTo(2));

            saver.RestoreState(null);
            Assert.That(trust.Pets, Is.Zero, "a missing record is a wary animal");
        }

        [Test]
        public void AWaryAnimalRefusesTheSaddleUntilItTrusts()
        {
            GameObject horn = Make("Horn");
            CreatureTrust trust = horn.AddComponent<CreatureTrust>();
            SaddleSocket socket = horn.AddComponent<SaddleSocket>();
            Invoke(socket, "Awake");

            Assert.That(socket.AcceptsSaddle, Is.False);
            trust.Restore(trust.PetsToTrust);
            Assert.That(socket.AcceptsSaddle, Is.True);

            SaddleSocket plain = Make("Ordinary").AddComponent<SaddleSocket>();
            Invoke(plain, "Awake");
            Assert.That(plain.AcceptsSaddle, Is.True, "an animal with no trust to win takes a saddle at once");
        }

        // ── The lasso from the saddle ──────────────────────────────────────────

        [Test]
        public void ARopeHeldOnFootWeighsAPlayer()
        {
            Transform hand = Make("Hand").transform;
            Assert.That(LassoArtifact.HolderMass(hand), Is.EqualTo(80f));
            Assert.That(LassoArtifact.HolderMass(null), Is.EqualTo(80f));
        }

        // ── The herd and the corral ────────────────────────────────────────────

        [Test]
        public void TheHerdIsPennedOnlyWhenEveryLivingOneIsInsideTheShutCorral()
        {
            var site = ScriptableObject.CreateInstance<RanchSite>();
            made.Add(site);
            Ranch ranch = MakeRanch(site, out SettlementPen corral);

            RanchStock inA = MakeStock(RanchStock.Kind.Wild, corral.transform.position + new Vector3(2f, 0f, 1f));
            MakeStock(RanchStock.Kind.Wild, corral.transform.position + new Vector3(-3f, 0f, -2f));
            RanchStock stray = MakeStock(RanchStock.Kind.Wild, corral.transform.position + new Vector3(40f, 0f, 0f));
            MakeStock(RanchStock.Kind.Tame, corral.transform.position + new Vector3(0f, 0f, 1f));

            ranch.CountHerd(inA.transform.position, out int penned, out int total, out RanchStock nearest);
            Assert.That(penned, Is.EqualTo(2));
            Assert.That(total, Is.EqualTo(3), "the tame animal is not part of the herd");
            Assert.That(nearest, Is.SameAs(stray));

            var step = ScriptableObject.CreateInstance<PenHerdStep>();
            made.Add(step);
            typeof(RanchStep).GetField("site", Hidden).SetValue(step, site);
            var world = new ObjectiveWorld();
            Assert.That(step.IsMet(world), Is.False, "one is still out on the range");

            stray.transform.position = corral.transform.position;
            Assert.That(step.IsMet(world), Is.True, "all in, and a corral with no gate counts as shut");
        }

        [Test]
        public void AnEmptyRangeIsNotAPennedHerd()
        {
            var site = ScriptableObject.CreateInstance<RanchSite>();
            made.Add(site);
            MakeRanch(site, out _);

            var step = ScriptableObject.CreateInstance<PenHerdStep>();
            made.Add(step);
            typeof(RanchStep).GetField("site", Hidden).SetValue(step, site);
            Assert.That(step.IsMet(new ObjectiveWorld()), Is.False);
        }

        // ── The quest, wired end to end ────────────────────────────────────────

        [Test]
        public void TheRanchQuestIsComplete()
        {
            var site = AssetDatabase.LoadAssetAtPath<RanchSite>(SitePath);
            var quest = AssetDatabase.LoadAssetAtPath<ObjectiveChain>(QuestPath);
            Assert.That(site, Is.Not.Null, SitePath);
            Assert.That(quest, Is.Not.Null, QuestPath);
            Assert.That(quest.Id, Is.EqualTo("raxy-ranch"));

            var ids = new HashSet<string>();
            foreach (ObjectiveStep step in quest.Steps)
            {
                Assert.That(step, Is.Not.Null, "an empty slot in the ranch quest");
                Assert.That(ids.Add(step.Id), Is.True, $"two steps share the id '{step.Id}'");
                Assert.That(step.Briefing.Count, Is.GreaterThan(0), $"'{step.name}' has no briefing");

                FieldInfo siteField = typeof(RanchStep).IsInstanceOfType(step)
                    ? typeof(RanchStep).GetField("site", Hidden)
                    : step.GetType().GetField("site", Hidden);
                Assert.That(siteField, Is.Not.Null, $"'{step.name}' is not a ranch step");
                Assert.That(siteField.GetValue(step), Is.SameAs(site), $"'{step.name}' is about another ranch");
            }
        }

        // Story: the catalog is persistentScene's, the ranch stands in the Raxy chunk. Spillexo is one scene holding both.
        [TestCase(PersistentPath, ChunkPath)]
        [TestCase(SpillexoPath, SpillexoPath)]
        public void TheRanchQuestCanBeginInEveryWorld(string catalogScene, string ranchScene)
        {
            string questGuid = AssetDatabase.AssetPathToGUID(QuestPath);
            Assert.That(File.ReadAllText(catalogScene), Does.Contain(questGuid),
                        $"the ranch quest is not in {catalogScene}'s ObjectiveDirector catalog, so it can never begin there");

            string ranch = File.ReadAllText(ranchScene);
            Assert.That(ranch, Does.Contain(AssetDatabase.AssetPathToGUID(CorralPath)), $"no corral in {ranchScene}");
            Assert.That(ranch, Does.Contain(AssetDatabase.AssetPathToGUID(TamePath)), $"no tame esko in {ranchScene}");

            // Every step of the quest has a beat on the placed ranch, so the wrangler always knows where to stand.
            var quest = AssetDatabase.LoadAssetAtPath<ObjectiveChain>(QuestPath);
            foreach (ObjectiveStep step in quest.Steps)
                Assert.That(ranch, Does.Contain(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(step))),
                            $"the ranch in {ranchScene} has no beat for '{step.name}'");
        }

        [Test]
        public void TheRanchsCreaturesAreBuiltAndNetworked()
        {
            GameObject tame = AssetDatabase.LoadAssetAtPath<GameObject>(TamePath);
            GameObject wild = AssetDatabase.LoadAssetAtPath<GameObject>(WildPath);
            GameObject wrangler = AssetDatabase.LoadAssetAtPath<GameObject>(WranglerPath);

            Assert.That(tame.GetComponent<RanchStock>().Of, Is.EqualTo(RanchStock.Kind.Tame));
            Assert.That(wild.GetComponent<RanchStock>().Of, Is.EqualTo(RanchStock.Kind.Wild));
            foreach (GameObject horn in new[] { tame, wild })
            {
                Assert.That(horn.GetComponent<CreatureTrust>(), Is.Not.Null, $"{horn.name} takes a saddle untamed");
                Assert.That(horn.GetComponent<CreatureTrustSaveable>(), Is.Not.Null, $"{horn.name} forgets its trust on load");
                Assert.That(horn.GetComponent<Rigidbody>().mass, Is.GreaterThan(100f),
                            $"{horn.name} weighs too little: a lasso tows it like a toy");
            }

            Assert.That(wrangler.GetComponent<RanchWrangler>(), Is.Not.Null);
            Assert.That(wrangler.GetComponent<QuestVoice>(), Is.Not.Null, "the wrangler's quest lines would come from the lander");

            string list = File.ReadAllText(NetworkListPath);
            foreach (string path in new[] { TamePath, WildPath, WranglerPath })
                Assert.That(list, Does.Contain(AssetDatabase.AssetPathToGUID(path)),
                            $"{path} is not a registered network prefab: it would spawn only on the host");
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private GameObject Make(string name)
        {
            var go = new GameObject(name);
            made.Add(go);
            return go;
        }

        private Ranch MakeRanch(RanchSite site, out SettlementPen corral)
        {
            GameObject root = Make("Ranch");
            root.transform.position = new Vector3(5000f, 0f, 5000f);
            Ranch ranch = root.AddComponent<Ranch>();

            GameObject pen = Make("Corral");
            pen.transform.SetParent(root.transform, false);
            corral = pen.AddComponent<SettlementPen>();
            typeof(SettlementPen).GetField("areaCenter", Hidden).SetValue(corral, new Vector3(0f, 1f, 0f));
            typeof(SettlementPen).GetField("areaSize", Hidden).SetValue(corral, new Vector3(20f, 4f, 14f));

            typeof(Ranch).GetField("site", Hidden).SetValue(ranch, site);
            typeof(Ranch).GetField("corral", Hidden).SetValue(ranch, corral);
            Enable(ranch);
            return ranch;
        }

        private RanchStock MakeStock(RanchStock.Kind kind, Vector3 at)
        {
            GameObject animal = Make(kind + "Stock");
            animal.transform.position = at;
            RanchStock stock = animal.AddComponent<RanchStock>();
            typeof(RanchStock).GetField("kind", Hidden).SetValue(stock, kind);
            Invoke(stock, "Awake");
            Enable(stock);
            return stock;
        }

        private void Enable(Behaviour behaviour)
        {
            Invoke(behaviour, "OnEnable");
            enabled.Add(behaviour);
        }

        private static void Invoke(Object target, string method) =>
            target.GetType().GetMethod(method, Hidden)?.Invoke(target, null);
    }
}
