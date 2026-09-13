// Builds the repair station prefab from the exported repair_station.fbx.
//
// The model comes out of Blender via models/props/repair_station_export.py: a bench, a hopper on
// its worktop, a grinder spindle, a status lamp and a 4 mm marker cube at the face of the gauge
// screen. Everything above the meshes — today only the collider — is generated here rather than
// hand-wired, for the same reason the other builders exist: a prefab wired by hand is a prefab
// nobody can rebuild after the model changes.
//
// SET DRESSING, not a machine. The station used to be fed ship scrap by a RepairWorkstation
// component, with a saver and a world-space progress gauge; scrap was removed from the game and
// the mechanic went with it. The bench stays because the lander's main deck is built around it.
// It has a collider so the player cannot walk through it, and nothing else — no IInteractable, so
// the interaction raycast passes over it and no prompt is ever offered.
//
// The prefab is a SHIP FIXTURE, nested under PlayerShip.prefab by
// PlayerShipBuilder.BuildRepairStation. It carries no NetworkObject and no saver of its own: it
// holds no state, so there is nothing to replicate and nothing to save.
//
// Re-running is safe and is the intended workflow. Re-export the FBX, run this, and the prefab
// is rebuilt in place against the new geometry.
//
// Re-run from: Tools > SpaceGame > Build Repair Station Prefab
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class RepairStationBuilder
    {
        public const string PrefabPath =
            "Assets/Game/Prefabs/Environment/Structures/Facilities/RepairStation.prefab";

        private const string Fbx = "Assets/Game/Art/Models/Props/repair_station.fbx";

        // The gauge marker as repair_station.py exports it. Nothing is drawn on it any more, but
        // it is still the one part that must not be rendered and must not swell the collider.
        private const string GaugeMarkerName = "Marker_RepairStation_Gauge";

        [MenuItem("Tools/SpaceGame/Build Repair Station Prefab")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (model == null)
            {
                Debug.LogError($"[RepairStationBuilder] No model at {Fbx} — run repair_station_export.py first.");
                return;
            }

            var root = new GameObject("RepairStation");
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
                visual.name = "Model";
                visual.transform.SetParent(root.transform, false);

                Transform marker = Find(visual, GaugeMarkerName);
                if (marker == null) return;

                // The marker is a build-time landmark, not a thing to draw.
                foreach (MeshRenderer r in marker.GetComponentsInChildren<MeshRenderer>(true))
                    r.enabled = false;

                BuildCollider(root, visual, marker);

                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();

                // A read-only AssetDatabase (an MPPM clone) discards prefab saves without
                // erroring, so "saved" is only true once the file is on disk.
                if (!File.Exists(PrefabPath))
                {
                    Debug.LogError($"[RepairStationBuilder] {PrefabPath} did not reach disk — is this a read-only editor clone?");
                    return;
                }

                Debug.Log($"[RepairStationBuilder] Built {PrefabPath}.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Transform Find(GameObject visual, string name)
        {
            Transform found = visual.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == name);
            if (found == null)
                Debug.LogError($"[RepairStationBuilder] No '{name}' in {Fbx} — repair_station.py " +
                               "names the parts this builder finds; re-export, or update both.");
            return found;
        }

        /// <summary>
        /// One box over everything drawn, measured off the renderers rather than typed in, so a
        /// remodel that grows the bench grows the thing the player collides with. Measured with
        /// the root at the origin, so world bounds are local bounds.
        /// </summary>
        private static void BuildCollider(GameObject root, GameObject visual, Transform marker)
        {
            var renderers = visual.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => !r.transform.IsChildOf(marker))
                .ToArray();

            Bounds bounds = renderers[0].bounds;
            foreach (MeshRenderer r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);

            var box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(bounds.center);
            box.size = bounds.size;
        }
    }
}
