// Gives the two colony airlock prefabs their AirlockPassage: the gated off-mesh link a colonist crosses by, from a stand on the
// ramp outside the outer hatch to a stand on the room's floor past the inner leaves. Idempotent: the Passage child is rebuilt in
// place, the prefab asset is re-read after saving, and an airlock prefab open in Prefab Mode is skipped and named.
//
// The stands are MEASURED, not authored: every colony building is instantiated in a preview scene and the point `OutsideReach` out
// from the outer doorway (ray down onto the stair ramp) and `InsideReach` in from the inner doorway (ray down onto the room's floor) is
// read off the real colliders. Both airlock prefabs are placed by one rule (a bulkhead 3.0 m in from the end wall, a vestibule 0.38 m
// in front of the hub wall) and the stairs are the same everywhere, so the stands are the same in the airlock's own frame in every
// building; the check at the end ray-casts all 13 of them to prove it.
//
// Run from: Tools > SpaceGame > Colony > Place Airlock Passages
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class ColonyAirlockPassageAuthoring
    {
        private const string BuildingFolder = "Assets/Game/Prefabs/Environment/Structures/AstronautSettlement";
        private const string PassageName = "Passage";
        private const string OutsideName = "OutsideStand";
        private const string InsideName = "InsideStand";

        // Metres from a doorway's middle: outside, clear of the hatch's swing (its disc is 2.52 m across its hinge); inside, within the
        // 2 m walking lane the planner keeps from every hatch.
        private const float OutsideReach = 4f;
        private const float InsideReach = 1.8f;
        // Rays start this far above the stand: outside, over the ramp (open air to the sky); inside, level with the doorway's middle, which
        // is under the ceiling, so the ray meets the floor and not the roof.
        private const float OutsideProbeAbove = 12f;
        private const float BuildingSpacing = 500f;
        private const float MaxDrop = 24f;

        // How far a re-measured stand may be from the one written, in any building, before the prefab is called wrong.
        private const float AgreeWithin = 0.25f;

        [MenuItem("Tools/SpaceGame/Colony/Place Airlock Passages")]
        private static void Menu() => Debug.Log(Run());

        public static string Run()
        {
            var report = new List<string>();
            string[] buildingPaths = AssetDatabase.FindAssets("t:Prefab Colony_", new[] { BuildingFolder })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();

            var stands = new Dictionary<string, (Vector3 outside, Vector3 inside)>();
            WithBuildings(buildingPaths, (instances, preview) =>
            {
                foreach (GameObject building in instances)
                    foreach (AirlockChamber chamber in building.GetComponentsInChildren<AirlockChamber>(true))
                    {
                        string prefabPath = AirlockPrefabPath(chamber);
                        if (prefabPath == null || stands.ContainsKey(prefabPath)) continue;
                        if (TryMeasure(chamber, preview, out Vector3 outside, out Vector3 inside))
                            stands[prefabPath] = (chamber.transform.InverseTransformPoint(outside), chamber.transform.InverseTransformPoint(inside));
                        else
                            report.Add($"could not measure {prefabPath} on {building.name}: no floor under a stand");
                    }
            });

            foreach (KeyValuePair<string, (Vector3 outside, Vector3 inside)> pair in stands)
                report.Add(Write(pair.Key, pair.Value.outside, pair.Value.inside));

            // Saving an airlock prefab refreshes every instance of it in every open scene and drops their positions, so the check
            // measures a scene made after the writes.
            WithBuildings(buildingPaths, (instances, preview) => report.AddRange(Verify(instances, stands, preview)));

            return "[Colony] airlock passages\n" + string.Join("\n", report);
        }

        // Every colony building in a preview scene, apart from each other: each is authored around its own origin, and a ray
        // down one would hit the next.
        private static void WithBuildings(string[] buildingPaths, Action<List<GameObject>, Scene> measure)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var instances = new List<GameObject>();
                foreach (string path in buildingPaths)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), preview);
                    instance.transform.position = new Vector3(instances.Count * BuildingSpacing, 0f, 0f);
                    instances.Add(instance);
                }
                Physics.SyncTransforms();
                measure(instances, preview);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        // The airlock prefab asset an instance came from (the innermost: the building only nests it), or null when it is not an instance of one.
        private static string AirlockPrefabPath(AirlockChamber chamber)
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(chamber.gameObject);
            return source == null ? null : AssetDatabase.GetAssetPath(source.transform.root.gameObject);
        }

        private static bool TryMeasure(AirlockChamber chamber, Scene preview, out Vector3 outside, out Vector3 inside)
        {
            Vector3 outer = chamber.DoorwayCentre(AirlockSide.Outer), inner = chamber.DoorwayCentre(AirlockSide.Inner);
            Vector3 inward = Vector3.ProjectOnPlane(inner - outer, Vector3.up).normalized;
            PhysicsScene physics = preview.GetPhysicsScene();

            bool hasInside = FloorUnder(physics, inner + inward * InsideReach, 0f, out inside);
            bool hasOutside = FloorUnder(physics, outer - inward * OutsideReach, OutsideProbeAbove, out outside);
            return hasInside && hasOutside;
        }

        // The first solid thing under a point that is not a trigger: the room's floor, the stair ramp.
        private static bool FloorUnder(PhysicsScene physics, Vector3 point, float above, out Vector3 floor)
        {
            floor = point;
            Vector3 from = new(point.x, point.y + above, point.z);
            if (!physics.Raycast(from, Vector3.down, out RaycastHit hit, above + MaxDrop, Physics.AllLayers, QueryTriggerInteraction.Ignore))
                return false;

            floor = hit.point;
            return true;
        }

        private static string Write(string prefabPath, Vector3 outside, Vector3 inside)
        {
            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == prefabPath) return $"skipped {prefabPath}: open in Prefab Mode";

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                AirlockChamber chamber = root.GetComponentInChildren<AirlockChamber>(true);
                if (chamber == null) return $"skipped {prefabPath}: no AirlockChamber";

                Transform old = root.transform.Find(PassageName);
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

                var passage = new GameObject(PassageName).transform;
                passage.SetParent(root.transform, false);
                Transform outsideStand = Stand(passage, OutsideName, outside);
                Transform insideStand = Stand(passage, InsideName, inside);

                AirlockPassage gate = passage.gameObject.AddComponent<AirlockPassage>();
                var so = new SerializedObject(gate);
                SerializedFields.Set(so, "chamber", chamber);
                SerializedFields.Set(so, "outsideStand", outsideStand);
                SerializedFields.Set(so, "insideStand", insideStand);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return ReadBack(prefabPath, outside, inside);
        }

        private static Transform Stand(Transform parent, string name, Vector3 airlockLocal)
        {
            var stand = new GameObject(name).transform;
            stand.SetParent(parent, false);
            stand.localPosition = airlockLocal;
            return stand;
        }

        private static string ReadBack(string prefabPath, Vector3 outside, Vector3 inside)
        {
            GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Transform passage = saved.transform.Find(PassageName);
            var gate = passage != null ? passage.GetComponent<AirlockPassage>() : null;
            if (gate == null) return $"FAILED {prefabPath}: the Passage did not survive the save";

            var so = new SerializedObject(gate);
            bool wired = so.FindProperty("chamber").objectReferenceValue != null && so.FindProperty("outsideStand").objectReferenceValue != null &&
                         so.FindProperty("insideStand").objectReferenceValue != null;
            Transform read = passage.Find(OutsideName);
            bool placed = read != null && Vector3.Distance(read.localPosition, outside) < 0.001f &&
                          Vector3.Distance(passage.Find(InsideName).localPosition, inside) < 0.001f;
            return wired && placed
                ? $"{System.IO.Path.GetFileNameWithoutExtension(prefabPath)}: outside stand {outside:F2}, inside stand {inside:F2} (airlock-local)"
                : $"FAILED {prefabPath}: the Passage is not wired or placed as written";
        }

        // Every airlock in every building: measured again, its stands must be where the prefab puts them.
        private static IEnumerable<string> Verify(List<GameObject> instances, Dictionary<string, (Vector3 outside, Vector3 inside)> stands, Scene preview)
        {
            int checkedCount = 0;
            foreach (GameObject building in instances)
                foreach (AirlockChamber chamber in building.GetComponentsInChildren<AirlockChamber>(true))
                {
                    string path = AirlockPrefabPath(chamber);
                    if (path == null || !stands.TryGetValue(path, out var written)) continue;

                    checkedCount++;
                    if (!TryMeasure(chamber, preview, out Vector3 outside, out Vector3 inside))
                    {
                        yield return $"FAILED {building.name}/{chamber.name}: no floor under a stand";
                        continue;
                    }

                    float outsideOff = Vector3.Distance(chamber.transform.InverseTransformPoint(outside), written.outside);
                    float insideOff = Vector3.Distance(chamber.transform.InverseTransformPoint(inside), written.inside);
                    if (outsideOff > AgreeWithin || insideOff > AgreeWithin)
                        yield return $"DISAGREES {building.name}/{chamber.name}: outside stand {outsideOff:0.00} m, inside stand {insideOff:0.00} m off the prefab's";
                }
            yield return $"checked {checkedCount} airlocks in {instances.Count} buildings";
        }
    }
}
