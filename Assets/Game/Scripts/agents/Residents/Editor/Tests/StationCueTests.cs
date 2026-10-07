// What a spot makes its resident do: the loop held at a spot is the one its prop calls for, and one exists for the body that
// stands (or sits) there. Read off the assets, so a spot pointed at a cue nobody can play, or a station cue left empty, fails here
// instead of showing as a resident standing stiff or miming another job.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceGame.EditorTools;
using SpaceGame.Presentation;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.Agents.Residents.Tests
{
    public class StationCueTests
    {
        /// <summary>Folders whose prefabs bring SettlementSpots: the decorations and the settlement's buildings.</summary>
        private static readonly string[] SpotFolders =
        {
            "Assets/Game/Prefabs/Environment/Decorations",
            "Assets/Game/Prefabs/Environment/Structures/NomadSettlement",
        };

        private const string SpotUseFolder = "Assets/Game/ScriptableObjects/Settlements/Spots";

        /// <summary>The kitchen's stations: one word per thing done at a stove, a board, a sink.</summary>
        public static readonly string[] CookStations = { "stir", "cookpan", "cookwok", "grill", "chop", "wash", "blend", "plate", "season" };

        /// <summary>Every cue that names a prop's own work, so its clips are the ones that prop calls for and no other job's.</summary>
        public static readonly string[] StationCues = CookStations.Concat(new[] { "wipe", "weave", "rummage" }).ToArray();

        // Of the stations, the ones with no loop (a plate laid down, a pinch of salt): asked for with Express, never held by a spot.
        private static readonly string[] OneShotStations = { "plate", "season" };

        private static CharacterCue Cue(string word) => HumanoidControllerBuilder.CollectCues().FirstOrDefault(c => c.name == word);

        private static IEnumerable<CharacterAction> Tagged(CharacterCue cue) =>
            HumanoidControllerBuilder.CollectActions().Where(a => a.Cues.Contains(cue));

        [Test]
        public void EveryStationCueExistsIsAnsweredAndNeverFallsBackToAnotherJob()
        {
            foreach (string word in StationCues)
            {
                CharacterCue cue = Cue(word);
                Assert.IsNotNull(cue, $"no '{word}' cue under ScriptableObjects/Animation/Cues");
                Assert.IsNotEmpty(Tagged(cue).ToList(), $"no action is tagged '{word}': a spot holding it would stand still");
                Assert.IsNull(cue.Fallback, $"'{word}' falls back to '{(cue.Fallback != null ? cue.Fallback.name : "")}': a station loop that does not fit " +
                                            "must leave the body standing, not mime the wider job");
                Assert.IsFalse(cue.NeedsFreeHands, $"'{word}' is done WITH the item in hand");
                Assert.IsFalse(string.IsNullOrWhiteSpace(cue.Meaning), $"'{word}' has no meaning");
            }
        }

        [Test]
        public void EveryCookingActionIsTaggedWithExactlyOneStation()
        {
            CharacterCue cook = Cue("cook");
            foreach (CharacterAction action in Tagged(cook))
            {
                string[] stations = action.Cues.Where(c => CookStations.Contains(c.name)).Select(c => c.name).ToArray();
                Assert.AreEqual(1, stations.Length,
                                $"'{action.name}' is a cooking action tagged with {stations.Length} stations ({string.Join(", ", stations)}): " +
                                "one clip shows one thing done at one station");
            }
        }

        [Test]
        public void EveryCookStationHasAnActionAndTheOnesASpotHoldsHaveALoop()
        {
            foreach (string word in CookStations)
            {
                List<CharacterAction> actions = Tagged(Cue(word)).ToList();
                Assert.IsNotEmpty(actions, $"the '{word}' station has no action");
                if (OneShotStations.Contains(word)) continue;

                Assert.IsTrue(actions.Any(a => a.Loops && (a.Fits(BodyPosture.Standing) || a.Fits(BodyPosture.Seated))),
                              $"the '{word}' station has no loop a standing cook can hold");
            }
        }

        [Test]
        public void AStationLoopNeverPlaysWhileTheBodyWalks()
        {
            foreach (string word in StationCues)
                foreach (CharacterAction action in Tagged(Cue(word)))
                {
                    if (!action.Loops) continue;
                    Assert.IsFalse(action.Fits(BodyPosture.Moving),
                                   $"'{action.name}' is a '{word}' station loop with no posture limit: a Hold plays it on a walking body");
                }
        }

        [Test]
        public void EverySpotWithAHoldCueHasALoopThatFitsItsPosture()
        {
            var problems = new List<string>();
            foreach (SpotUse use in AllAssets<SpotUse>(SpotUseFolder))
                Check(use.name, use.holdCue, use.seated, problems);

            foreach (string path in AssetDatabase.FindAssets("t:Prefab", SpotFolders).Select(AssetDatabase.GUIDToAssetPath))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (SettlementSpot spot in prefab.GetComponentsInChildren<SettlementSpot>(true))
                    if (spot.Use != null) Check($"{prefab.name}/{spot.name} ({spot.Use.name})", spot.HoldCue, spot.Use.seated, problems);
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void ASpotsOwnStationCueBeatsItsUses()
        {
            var host = new GameObject("spot");
            var use = ScriptableObject.CreateInstance<SpotUse>();
            var useCue = ScriptableObject.CreateInstance<CharacterCue>();
            var stationCue = ScriptableObject.CreateInstance<CharacterCue>();
            try
            {
                use.holdCue = useCue;
                var so = new SerializedObject(host.AddComponent<SettlementSpot>());
                so.FindProperty("use").objectReferenceValue = use;
                so.ApplyModifiedPropertiesWithoutUndo();
                SettlementSpot spot = host.GetComponent<SettlementSpot>();
                Assert.AreSame(useCue, spot.HoldCue, "with no station cue the spot holds what its use is for");

                so.FindProperty("stationCue").objectReferenceValue = stationCue;
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreSame(stationCue, spot.HoldCue);

                var place = new SettlementPlace(PlaceKind.Post, use, 0, 0, Vector3.zero, null, spot.HoldCue);
                Assert.AreSame(stationCue, place.HoldCue, "the place carries it to every machine's presence");
                Assert.AreSame(useCue, new SettlementPlace(PlaceKind.Post, use, 0, 0, Vector3.zero, null).HoldCue);
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(use);
                Object.DestroyImmediate(useCue);
                Object.DestroyImmediate(stationCue);
            }
        }

        private static void Check(string where, CharacterCue cue, bool seated, List<string> problems)
        {
            if (cue == null) return;

            // A resident on a Seat never raises the animator's Seated flag (only the player's ChairPose does), so its posture still
            // reads Standing: a sit loop may be limited to either.
            BodyPosture fitting = seated ? BodyPosture.Standing | BodyPosture.Seated : BodyPosture.Standing;
            if (!Tagged(cue).Any(a => a.Loops && a.Fits(fitting)))
                problems.Add($"{where} holds '{cue.name}', and no looping action tagged with it fits a {(seated ? "sitting" : "standing")} body: its resident has nothing to do");
        }

        private static IEnumerable<T> AllAssets<T>(string folder) where T : Object =>
            AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder }).Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)));
    }
}
