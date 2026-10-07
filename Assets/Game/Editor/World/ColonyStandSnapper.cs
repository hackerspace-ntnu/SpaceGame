// Moves a colony spot that nobody can reach to the nearest place a resident can stand, in the building prefab.
//
// A prop's spot is authored in front of the prop, but the colony's rooms are packed: a chair's stand-up point lands under the table it
// faces, a bunk's front is a metre from the next prop, a table's back is the wall. The NavMesh keeps half a metre off every solid, so
// those stands are off the mesh and the planner skips the spot. This reads the baked world NavMesh over the generated colony, finds
// each spot that is not on it (or not reachable from the settlement's heart) and tries points on rings around what the spot looks at: the
// nearest one that is on the mesh, reachable, and no farther from the target than the seat's reach, becomes the new stand. It is written
// into the BUILDING prefab (as an override of the nested prop's spot), so every instance of that building has it.
//
// Idempotent: a spot already on the mesh is left alone. Needs the colony generated and the world NavMesh baked (the airlock links are put on
// for the run, because they are what makes an interior reachable); a prefab open in Prefab Mode is skipped and named.
//
// Run from: Tools > SpaceGame > Colony > Snap Spots To NavMesh
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using SpaceGame.Agents.Residents;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class ColonyStandSnapper
    {
        private const string BaseSceneName = "Chunk_5_3";
        private const string WorldNavMeshPath = "Assets/Game/Settings/WorldNavMesh.asset";

        // Rings tried round the target, metres, and the step between points on a ring, degrees.
        private const float FirstRing = 0.9f, RingStep = 0.3f, AngleStep = 15f;
        // A snapped stand may stand this far farther from the target than the authored one did, and no farther than a seat reaches.
        private const float FartherBy = 1.0f;
        // A spot that looks at nothing (a point to wander to) may move this far to find the mesh.
        private const float FreeSpotReach = 4f;
        // How far from the mesh a point may be and still count as on it, and how far its height may differ from the floor.
        private const float OnMeshWithin = 0.25f, FloorTolerance = 0.8f;

        [MenuItem("Tools/SpaceGame/Colony/Snap Spots To NavMesh")]
        private static void Menu() => Debug.Log(Run());

        public static string Run()
        {
            Scene scene = SceneManager.GetSceneByName(BaseSceneName);
            if (!scene.isLoaded) return $"[Colony] open {BaseSceneName} first: it holds the generated colony.";

            Settlement settlement = scene.GetRootGameObjects().Select(r => r.GetComponentInChildren<Settlement>(true)).FirstOrDefault(s => s != null);
            if (settlement == null || settlement.GeneratedRoot == null) return "[Colony] no generated settlement in the scene.";

            var report = new List<string>();
            var moves = new List<(string building, string path, Vector3 local)>();
            var asset = AssetDatabase.LoadAssetAtPath<WorldNavMeshAsset>(WorldNavMeshPath);
            NavMeshDataInstance mesh = NavMesh.AddNavMeshData(asset.bakedData);
            var links = new List<NavMeshLinkInstance>();
            try
            {
                foreach (AirlockPassage passage in settlement.GeneratedRoot.GetComponentsInChildren<AirlockPassage>(true))
                    if (passage.TryAttachLink()) links.Add(passage.Link);

                Physics.SyncTransforms();
                // One instance of each building stands for all of them: they are the same prefab, and the spots live in its frame.
                var reference = new Dictionary<string, Transform>();
                foreach (SettlementSpot spot in settlement.GeneratedRoot.GetComponentsInChildren<SettlementSpot>(true))
                {
                    Transform building = PrefabUtility.GetOutermostPrefabInstanceRoot(spot.gameObject)?.transform;
                    if (building == null || !building.name.StartsWith("Colony_")) continue;
                    if (!reference.TryAdd(building.name, building) && reference[building.name] != building) continue;

                    if (spot.Use != null && TrySnap(spot, settlement.WalkableHeart, out Vector3 stand))
                        moves.Add((building.name, PathBelow(building, spot.transform), building.InverseTransformPoint(stand)));
                }
            }
            finally
            {
                foreach (NavMeshLinkInstance link in links) if (NavMesh.IsLinkValid(link)) NavMesh.RemoveLink(link);
                mesh.Remove();
            }

            foreach (IGrouping<string, (string building, string path, Vector3 local)> building in moves.GroupBy(m => m.building))
                report.Add(Write(building.Key, building.ToList()));
            report.Insert(0, $"{moves.Count} spot(s) moved onto the NavMesh");
            return "[Colony] spot snap\n" + string.Join("\n", report);
        }

        // True with the better stand when the spot's own is off the mesh or cut off from the heart and a better one was found.
        private static bool TrySnap(SettlementSpot spot, Vector3 heart, out Vector3 stand)
        {
            stand = default;
            if (spot.Use.elevated || IsStandable(spot.Position, heart, out _)) return false;

            Vector3 target = spot.HasTarget ? spot.FacePoint : spot.Position;
            float authored = Flat(spot.Position - target);
            // A stand is within a seat's reach of the thing it sits on, and a work stand within the authored distance plus a little.
            float limit = spot.Use.seated ? ResidentTuning.Instance.seatReach : spot.HasTarget ? authored + FartherBy : FreeSpotReach;
            float floor = spot.Position.y;

            Vector3? best = null;
            float bestMove = float.MaxValue;
            for (float radius = FirstRing; radius <= limit; radius += RingStep)
                for (float angle = 0f; angle < 360f; angle += AngleStep)
                {
                    Vector3 point = target + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                    point.y = floor;
                    if (!IsStandable(point, heart, out Vector3 onMesh) || Mathf.Abs(onMesh.y - floor) > FloorTolerance) continue;

                    float move = Flat(onMesh - spot.Position);
                    if (move >= bestMove) continue;
                    best = onMesh;
                    bestMove = move;
                }

            if (best == null) return false;
            stand = best.Value;
            return true;
        }

        private static bool IsStandable(Vector3 point, Vector3 heart, out Vector3 onMesh)
        {
            onMesh = point;
            if (!NavMesh.SamplePosition(point, out NavMeshHit hit, OnMeshWithin, NavMesh.AllAreas)) return false;

            onMesh = hit.position;
            return NavMeshReach.CanWalk(heart, hit.position);
        }

        private static string Write(string buildingName, List<(string building, string path, Vector3 local)> moves)
        {
            string prefabPath = AssetDatabase.FindAssets("t:Prefab " + buildingName, new[] { "Assets/Game/Prefabs/Environment/Structures/AstronautSettlement" })
                .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == buildingName);
            if (prefabPath == null) return $"{buildingName}: no prefab found";
            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == prefabPath) return $"{buildingName}: skipped, open in Prefab Mode";

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            int written = 0;
            try
            {
                foreach ((string _, string path, Vector3 local) in moves)
                {
                    Transform spot = root.transform.Find(path);
                    if (spot == null) continue;

                    spot.position = local;
                    var so = new SerializedObject(spot.GetComponent<SettlementSpot>());
                    SerializedProperty face = so.FindProperty("face");
                    Vector3 toward = face.objectReferenceValue is Transform f ? Vector3.ProjectOnPlane(f.position - local, Vector3.up) : spot.forward;
                    if (toward.sqrMagnitude > Mathf.Epsilon) spot.rotation = Quaternion.LookRotation(toward);
                    written++;
                }
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return $"{buildingName}: {written} of {moves.Count} moved";
        }

        private static string PathBelow(Transform root, Transform item) =>
            item == root ? string.Empty : item.parent == root ? item.name : PathBelow(root, item.parent) + "/" + item.name;

        private static float Flat(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);
    }
}
