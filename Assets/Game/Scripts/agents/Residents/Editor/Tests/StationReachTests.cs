// Where a worker stands so its tool lands on its work: the stand-point rule as a table, and the exit check that runs it over
// every authored work spot with a target and a measured reach (the reach is measured by ActionReachMeasurer and stored on
// the actions). The numbers it finds are written to the test log, one line per spot.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SpaceGame.EditorTools;
using SpaceGame.Presentation;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.Agents.Residents.Tests
{
    public class StationReachTests
    {
        private const string DecorationFolder = "Assets/Game/Prefabs/Environment/Decorations";
        private const float Epsilon = 1e-4f;

        private static readonly Vector3 Target = new Vector3(0f, 1f, 0f);

        /// <summary>
        /// Strike stations whose tool still lands more than the tolerance from the prop, though the stand point has moved as far as it may:
        /// the clip's reach is shorter than the NavMesh lets the body stand from the prop. Pinned so a station that gets fixed (a closer
        /// authored spot, a longer-reaching clip) fails here until it is taken off, and a new one cannot join unseen.
        /// </summary>
        private static readonly HashSet<string> KnownShort = new HashSet<string> { "Deco_ButcherBlock/Spot_Butchery" };

        [Test]
        public void ThePointMovesTowardTheTargetUntilTheToolReaches_ByAtMostTheShift()
        {
            var authored = new Vector3(0f, 0.2f, 2f);

            Vector3 near = StationStand.Derive(authored, Target, forwardReach: 1.8f, maxShift: 0.6f, minDistance: 1f);
            Assert.AreEqual(1.8f, Flat(near - Target), Epsilon, "a tool that reaches 1.8 m lands on the target from 1.8 m");
            Assert.AreEqual(0.2f, near.y, Epsilon, "the authored height is kept for the NavMesh to settle");

            Vector3 far = StationStand.Derive(authored, Target, forwardReach: 1.1f, maxShift: 0.6f, minDistance: 1f);
            Assert.AreEqual(1.4f, Flat(far - Target), Epsilon, "a short reach is held to the shift: the authored point is the walkable one");

            Vector3 clear = StationStand.Derive(authored, Target, forwardReach: 0.2f, maxShift: 5f, minDistance: 1f);
            Assert.AreEqual(1f, Flat(clear - Target), Epsilon, "never closer than a body's clearance");
        }

        [Test]
        public void ThePointNeverMovesAwayFromTheTarget_NorWhenAlreadyCloseEnough()
        {
            var authored = new Vector3(2f, 0f, 0f);
            Assert.AreEqual(authored, StationStand.Derive(authored, Target, forwardReach: 3f, maxShift: 0.6f, minDistance: 1f),
                            "a tool that would reach from farther is no reason to back off the authored point");
            Assert.AreEqual(authored, StationStand.Derive(authored, Target, 0.5f, 0.6f, authored.x + 1f),
                            "already within the clearance: left where it was authored");
            Assert.AreEqual(Vector3.zero, StationStand.Derive(Vector3.zero, Target, 1f, 1f, 1f), "a point already on the target is not pushed anywhere");
        }

        [Test]
        public void TheReachOfAStationIsTheMeanOfItsMeasuredStandingLoops()
        {
            CharacterAction near = Action(CharacterAction.Playback.Loop, BodyPosture.Standing, new Vector3(0.1f, 1f, 1.0f));
            CharacterAction far = Action(CharacterAction.Playback.Loop, BodyPosture.Standing | BodyPosture.Seated, new Vector3(-0.1f, 1f, 1.6f));
            CharacterAction oneShot = Action(CharacterAction.Playback.OneShot, BodyPosture.Standing, new Vector3(0f, 1f, 9f));
            CharacterAction walking = Action(CharacterAction.Playback.Loop, BodyPosture.Moving, new Vector3(0f, 1f, 9f));
            CharacterAction unmeasured = Action(CharacterAction.Playback.Loop, BodyPosture.Standing, Vector3.zero);
            try
            {
                Assert.IsTrue(StationStand.TryForwardReach(new[] { near, far, oneShot, walking, unmeasured }, out float forward, out float spread));
                Assert.AreEqual(1.3f, forward, Epsilon);
                Assert.AreEqual(0.6f, spread, Epsilon, "the shortest and the longest clip a resident may draw");
                Assert.IsFalse(StationStand.TryForwardReach(new[] { oneShot, walking, unmeasured }, out _, out _),
                               "nothing measured: the authored point stands");
            }
            finally
            {
                foreach (CharacterAction action in new[] { near, far, oneShot, walking, unmeasured }) Object.DestroyImmediate(action);
            }
        }

        [Test]
        public void EveryMeasuredWorkSpotLandsItsToolOnItsTarget()
        {
            ResidentTuning tuning = ResidentTuning.Instance;
            var actions = HumanoidControllerBuilder.CollectActions();
            var log = new StringBuilder();
            var misses = new List<string>();
            var stale = new List<string>();
            var strikes = new HashSet<string>(ActionReachMeasurer.Jobs.Where(j => j.Rule != ActionReachMeasurer.ContactRule.CycleMean).Select(j => j.Action));
            int measured = 0, checkedSpots = 0;

            foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { DecorationFolder }).Select(AssetDatabase.GUIDToAssetPath))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Bounds footprint = Footprint(root);
                    foreach (SettlementSpot spot in root.GetComponentsInChildren<SettlementSpot>(true))
                    {
                        if (spot.Use == null || spot.Use.seated || !spot.HasTarget || spot.HoldCue == null) continue;
                        List<CharacterAction> loops = actions.Where(a => a.Cues.Contains(spot.HoldCue)).ToList();
                        if (!StationStand.TryForwardReach(loops, out float forward, out float spread)) continue;

                        // Only a strike's reach is where the tool lands; the hand of continuous work (a pan, a cloth) is the hand, not the tool.
                        bool toolEnd = loops.Where(a => a.Loops && a.HasReach).All(a => strikes.Contains(a.name));

                        measured++;
                        Vector3 stand = StationStand.Derive(spot.Position, spot.FacePoint, forward, tuning.stationMaxShift, tuning.stationMinDistance);
                        Vector3 toTarget = Flat3(spot.FacePoint - stand).normalized;
                        Vector3 landing = stand + toTarget * forward;
                        float before = Mathf.Abs(StationStand.Shortfall(spot.Position, spot.FacePoint, forward));
                        float after = Mathf.Abs(StationStand.Shortfall(stand, spot.FacePoint, forward));
                        float gap = Mathf.Sqrt(footprint.SqrDistance(new Vector3(landing.x, footprint.center.y, landing.z)));
                        bool hit = after <= tuning.stationReachTolerance || gap <= tuning.stationReachTolerance;
                        string key = System.IO.Path.GetFileNameWithoutExtension(path) + "/" + spot.name;

                        string line = $"{key} '{spot.HoldCue.name}': reach {forward:0.00} (spread {spread:0.00}), " +
                                      $"target {Flat(spot.FacePoint - spot.Position):0.00} m from the authored point, miss {before:0.00} -> {after:0.00} m, " +
                                      $"tool lands {gap:0.00} m from the prop{(toolEnd ? "" : " (a hand, not a tool: reported, not checked)")}";
                        log.AppendLine(line);

                        Assert.LessOrEqual(Flat(stand - spot.FacePoint), Flat(spot.Position - spot.FacePoint) + Epsilon, key + ": the stand point moved away from its target");
                        Assert.LessOrEqual(Flat(stand - spot.Position), tuning.stationMaxShift + Epsilon, key + ": the stand point moved farther than stationMaxShift");
                        if (!toolEnd) continue;

                        checkedSpots++;
                        if (!hit && !KnownShort.Contains(key)) misses.Add(line);
                        if (hit && KnownShort.Contains(key)) stale.Add(key);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            Debug.Log($"[StationReach] {measured} spots with a measured reach, {checkedSpots} of them strikes and checked (tolerance {tuning.stationReachTolerance:0.00} m):\n{log}");
            Assert.Greater(checkedSpots, 0, "no work spot has a measured reach: run Tools > SpaceGame > Animation > Measure Action Reach");
            Assert.IsEmpty(misses, string.Join("\n", misses));
            Assert.IsEmpty(stale, "these stations now land their tool: remove them from KnownShort");
        }

        private static CharacterAction Action(CharacterAction.Playback playback, BodyPosture postures, Vector3 reach)
        {
            var action = ScriptableObject.CreateInstance<CharacterAction>();
            var so = new SerializedObject(action);
            so.FindProperty("playback").enumValueIndex = (int)playback;
            so.FindProperty("postures").intValue = (int)postures;
            so.FindProperty("reach").vector3Value = reach;
            so.ApplyModifiedPropertiesWithoutUndo();
            return action;
        }

        // What the prop covers: every renderer under the prefab (the spot and its face have none).
        private static Bounds Footprint(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.zero);

            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static float Flat(Vector3 v) => Flat3(v).magnitude;

        private static Vector3 Flat3(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
