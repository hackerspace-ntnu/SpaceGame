// Quest progress fails silently when it fails: nothing throws, the errand is just back at step one
// and the player is asked for something they already handed over. So the contract is pinned here —
// including the two cases that are easy to get subtly wrong.
//
//   RestoreState(null) means "this was at its defaults", NOT "there is nothing to do". A giver that
//   is mid-errand in memory — which a re-hydrated chunk can hand you — has to be put back to zero.
//
//   A record about a DIFFERENT questline must be discarded rather than applied. Regenerating a town
//   re-deals its questlines, so an old record's step index would drop the player into the middle of
//   an errand they have never been given.
//
// The round trip goes through real JSON TEXT and lands on a DIFFERENT instance, for the reasons
// PersistenceProbe's header gives: the Unity converters live on SaveSerializer.Serializer, and
// restoring onto the object you captured from passes even when the saver restores nothing.
using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay.Quests;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTools
{
    public class QuestGiverSaveableTests
    {
        private readonly List<GameObject> spawned = new();
        private readonly List<ScriptableObject> assets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            foreach (ScriptableObject asset in assets)
                if (asset != null) Object.DestroyImmediate(asset);

            spawned.Clear();
            assets.Clear();
        }

        private InventoryItem Item(string itemName)
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.itemName = itemName;
            assets.Add(item);
            return item;
        }

        /// <summary>
        /// A questline with an id set by hand. A CreateInstance'd asset has no path, so the
        /// OnValidate that normally stamps the GUID has nothing to stamp from.
        /// </summary>
        private Questline Line(string id, int steps)
        {
            var line = ScriptableObject.CreateInstance<Questline>();
            line.ID = id;
            line.title = id;
            line.steps = new List<QuestStep>();

            for (int i = 0; i < steps; i++)
                line.steps.Add(new QuestStep { text = $"Bring me thing {i}.", required = Item($"Thing{i}") });

            assets.Add(line);
            return line;
        }

        private (QuestGiver giver, QuestGiverSaveable saver) Giver(Questline line, int step)
        {
            var go = new GameObject("Giver");
            spawned.Add(go);

            // RequireComponent pulls DialogInteraction in with it.
            var giver = go.AddComponent<QuestGiver>();
            var saver = go.AddComponent<QuestGiverSaveable>();

            giver.Assign(line);
            giver.RestoreStep(step);

            return (giver, saver);
        }

        /// <summary>Capture → real JSON text → parse, which is what a save file actually does.</summary>
        private static JObject ThroughJson(object captured)
        {
            if (captured == null) return null;

            string json = JsonConvert.SerializeObject(captured, SaveSerializer.Settings);
            return JObject.Parse(json);
        }

        // ── The round trip ───────────────────────────────────────────────────────

        [Test]
        public void ProgressSurvivesTheRoundTripOntoAFreshGiver()
        {
            Questline line = Line("quest-a", 4);

            (QuestGiver source, QuestGiverSaveable sourceSaver) = Giver(line, 2);
            JObject payload = ThroughJson(sourceSaver.CaptureState());

            Assert.IsNotNull(payload, "a giver part-way through has something to save");

            (QuestGiver target, QuestGiverSaveable targetSaver) = Giver(line, 0);
            targetSaver.RestoreState(payload);

            Assert.AreEqual(2, target.StepIndex, "the step came back");
            Assert.AreEqual(source.StepIndex, target.StepIndex);
        }

        [Test]
        public void TheRecordNamesTheQuestlineByItsAssetId()
        {
            Questline line = Line("quest-a", 3);
            (_, QuestGiverSaveable saver) = Giver(line, 1);

            JObject payload = ThroughJson(saver.CaptureState());

            Assert.AreEqual("quest-a", payload["questline"]?.ToString(),
                            "the GUID, not the name and not a list index");
            Assert.AreEqual(1, payload["step"]?.ToObject<int>());
        }

        // ── Defaults ─────────────────────────────────────────────────────────────

        [Test]
        public void AnUntouchedGiverWritesNothingAtAll()
        {
            // Otherwise every NPC in every town in the world carries a "step 0" row in every save.
            (_, QuestGiverSaveable saver) = Giver(Line("quest-a", 3), 0);

            Assert.IsNull(saver.CaptureState());
        }

        [Test]
        public void AGiverWithNoQuestlineWritesNothing()
        {
            (_, QuestGiverSaveable saver) = Giver(null, 0);

            Assert.IsNull(saver.CaptureState());
        }

        [Test]
        public void ANullPayloadPutsTheGiverBackToTheBeginning()
        {
            // The case that is easy to get wrong: null is a VALUE meaning "was at defaults", not an
            // absence meaning "leave it alone". A chunk re-hydrating can hand back a giver that is
            // still mid-errand in memory.
            Questline line = Line("quest-a", 3);
            (QuestGiver giver, QuestGiverSaveable saver) = Giver(line, 2);

            saver.RestoreState(null);

            Assert.AreEqual(0, giver.StepIndex, "a null record restores the default, it does not skip");
        }

        // ── Wrong questline ──────────────────────────────────────────────────────

        [Test]
        public void ARecordAboutADifferentQuestlineIsDiscarded()
        {
            Questline mine = Line("quest-a", 4);
            Questline theirs = Line("quest-b", 4);

            (_, QuestGiverSaveable sourceSaver) = Giver(theirs, 3);
            JObject payload = ThroughJson(sourceSaver.CaptureState());

            (QuestGiver target, QuestGiverSaveable targetSaver) = Giver(mine, 0);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("regenerated"));
            targetSaver.RestoreState(payload);

            Assert.AreEqual(0, target.StepIndex,
                            "step 3 of somebody else's errand must not become step 3 of this one");
        }

        // ── Bad numbers ──────────────────────────────────────────────────────────

        [Test]
        public void AStepPastTheEndIsClampedRatherThanTrusted()
        {
            Questline line = Line("quest-a", 2);
            (QuestGiver giver, QuestGiverSaveable saver) = Giver(line, 0);

            saver.RestoreState(JObject.Parse("{\"questline\":\"quest-a\",\"step\":99}"));

            Assert.AreEqual(2, giver.StepIndex, "clamped to the number of steps, which reads as finished");
            Assert.IsTrue(giver.IsFinished);
        }

        [Test]
        public void ANegativeStepIsClampedToZero()
        {
            Questline line = Line("quest-a", 2);
            (QuestGiver giver, QuestGiverSaveable saver) = Giver(line, 1);

            saver.RestoreState(JObject.Parse("{\"questline\":\"quest-a\",\"step\":-5}"));

            Assert.AreEqual(0, giver.StepIndex);
        }

        [Test]
        public void TheSaveKeyIsTheOneWrittenIntoSaveFiles()
        {
            // Renaming this orphans every quest record ever written.
            (_, QuestGiverSaveable saver) = Giver(Line("quest-a", 1), 0);

            Assert.AreEqual("quest", saver.SaveKey);
            Assert.AreEqual("quest", QuestGiverSaveable.Key);
        }
    }
}
