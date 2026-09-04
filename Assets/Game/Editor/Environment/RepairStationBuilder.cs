// Builds the repair station prefab from the exported repair_station.fbx.
//
// The model comes out of Blender via models/props/repair_station_export.py: a bench, a scrap
// hopper on its worktop, a grinder spindle, a status lamp and a 4 mm marker cube at the face
// of the gauge screen. Everything above the meshes — the collider, the scrap-fed
// RepairWorkstation, its saver, and the world-space progress gauge — is generated here rather
// than hand-wired, for the same reason the other builders exist: a prefab wired by hand is a
// prefab nobody can rebuild after the model changes.
//
// The prefab is a SHIP FIXTURE. It carries no NetworkObject of its own: nested under
// PlayerShip.prefab (PlayerShipBuilder.BuildRepairStation) it inherits the ship's, which is what
// makes the workstation's NetworkVariable and RPCs replicate. Dropped into a chunk on its own it
// would need a NetworkObject added by whatever places it — the ShipRV wraps its older
// primitive-built RepairWorkstation.prefab the same way.
//
// Re-running is safe and is the intended workflow. Re-export the FBX, run this, and the prefab
// is rebuilt in place against the new geometry.
//
// Re-run from: Tools > SpaceGame > Build Repair Station Prefab
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Core.Persistence;
using SpaceGame.Core.Persistence.EditorTools;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public static class RepairStationBuilder
    {
        public const string PrefabPath =
            "Assets/Game/Prefabs/Environment/Structures/Facilities/RepairStation.prefab";

        private const string Fbx = "Assets/Game/Art/Models/Props/repair_station.fbx";
        private const string ScrapItemPath = "Assets/Game/Resources/Items/Pickups/Scraps.asset";

        // Part names as repair_station.py exports them. The builder finds parts by these, so a
        // rename in the model script fails here, loudly, instead of shipping a station whose
        // wheel never turns.
        private const string SpindleName = "Mesh_RepairStation_Spindle";
        private const string LampName = "Mesh_RepairStation_StatusLamp";
        private const string LidName = "Mesh_RepairStation_HopperLid";
        private const string GaugeMarkerName = "Marker_RepairStation_Gauge";

        // Five pieces, each a trip out into the desert — the same quest length as the ShipRV's.
        private const int RequiredScrap = 5;

        // The lid is 12 mm thick at model scale; a 2 cm hop reads as a clunk, not a launch.
        private const float ClunkDistance = 0.02f;

        // The gauge is a world-space canvas laid on the screen recess of the back panel: 1 canvas
        // unit = 1 mm, sized to the 0.38 x 0.16 m screen with a margin, and stood 2 mm proud of
        // it so the quad never z-fights the geometry it sits on.
        private const float CanvasUnit = 0.001f;
        private static readonly Vector2 CanvasSize = new Vector2(360f, 150f);
        private const float CanvasStandoff = 0.002f;

        [MenuItem("Tools/SpaceGame/Build Repair Station Prefab")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (model == null)
            {
                Debug.LogError($"[RepairStationBuilder] No model at {Fbx} — run repair_station_export.py first.");
                return;
            }

            var scrap = AssetDatabase.LoadAssetAtPath<InventoryItem>(ScrapItemPath);
            if (scrap == null)
            {
                Debug.LogError($"[RepairStationBuilder] No scrap item at {ScrapItemPath}.");
                return;
            }

            var root = new GameObject("RepairStation");
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
                visual.name = "Model";
                visual.transform.SetParent(root.transform, false);

                Transform spindle = Find(visual, SpindleName);
                Transform lamp = Find(visual, LampName);
                Transform lid = Find(visual, LidName);
                Transform marker = Find(visual, GaugeMarkerName);
                if (spindle == null || lamp == null || lid == null || marker == null) return;

                // The marker is a build-time landmark, not a thing to draw. Only its WORLD
                // position is trusted: its own axes carry the FBX export's -90°X / x100 bake.
                foreach (MeshRenderer r in marker.GetComponentsInChildren<MeshRenderer>(true))
                    r.enabled = false;

                BuildCollider(root, visual, marker);

                var station = root.AddComponent<RepairWorkstation>();
                var so = new SerializedObject(station);
                so.FindProperty("requiredItem").objectReferenceValue = scrap;
                so.FindProperty("requiredAmount").intValue = RequiredScrap;
                SerializedProperty spinning = so.FindProperty("spinningParts");
                spinning.arraySize = 1;
                spinning.GetArrayElementAtIndex(0).objectReferenceValue = spindle;
                // The wheel's axle runs across the bench, which is the prefab's X. Asked of the
                // spindle's own frame rather than written as a constant, because that frame is
                // whatever the importer made of the export's axis bake.
                so.FindProperty("spinAxis").vector3Value =
                    spindle.InverseTransformDirection(root.transform.right).normalized;
                so.FindProperty("statusLight").objectReferenceValue = lamp.GetComponent<MeshRenderer>();
                so.FindProperty("clunkTarget").objectReferenceValue = lid;
                so.FindProperty("clunkDistance").floatValue = ClunkDistance;
                so.ApplyModifiedPropertiesWithoutUndo();

                // Baked in rather than left to SaveablePolicy, for the reason the projector's
                // is: the runtime auto-attach only runs on an entity's ROOT, so a station nested
                // under the PlayerShip would never get its saver, and four of five scrap would be
                // back at zero after every load. The policy's own add is null-guarded, so a
                // standalone placement is unaffected.
                root.AddComponent<RepairWorkstationSaveable>();

                BuildGauge(root, station, marker);

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

                // Wire it NOW, not at the next ship build. The wiring sweep gives every saveable
                // prefab a SaveableEntity and a TransformSaveable; PlayerShipBuilder strips both
                // off its nested instance (one entity per prefab, on the root) — but it can only
                // strip what exists when it nests. Left to the ship build's own sweep, which
                // runs AFTER the nesting, the two land on this prefab afterwards, the nested
                // instance inherits them, and the ship fails Verify with a second entity aboard.
                // The import first: the sweep finds prefabs through the search index, which does
                // not yet hold an asset saved in this same call.
                AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
                if (!SaveableWiring.TryWirePrefabs())
                {
                    Debug.LogError("[RepairStationBuilder] The save-wiring pass refused to run, so the " +
                                   "prefab has no entity for the ship builder to strip — rebuild it stopped.");
                    return;
                }

                Debug.Log($"[RepairStationBuilder] Built {PrefabPath} (gauge at {marker.position}).");
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
                               "names the parts this builder wires; re-export, or update both.");
            return found;
        }

        /// <summary>
        /// One box over everything drawn, measured off the renderers rather than typed in, so a
        /// remodel that grows the bench grows the thing the interaction ray has to hit. Measured
        /// with the root at the origin, so world bounds are local bounds.
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

        /// <summary>
        /// The progress gauge, drawn on the panel's screen recess: status line on top, the fill
        /// bar across the middle, the "x / y" count under it. Its forward points INTO the panel —
        /// a world-space Canvas reads correctly from the side its forward points away from, and
        /// the player stands out front on the prefab's +Z.
        /// </summary>
        private static void BuildGauge(GameObject root, RepairWorkstation station, Transform marker)
        {
            var gauge = new GameObject("Gauge", typeof(RectTransform), typeof(Canvas));
            gauge.layer = LayerMask.NameToLayer("UI");
            var rect = gauge.GetComponent<RectTransform>();
            rect.SetParent(root.transform, false);
            rect.sizeDelta = CanvasSize;
            rect.localScale = Vector3.one * CanvasUnit;
            rect.position = marker.position + root.transform.forward * CanvasStandoff;
            rect.rotation = Quaternion.LookRotation(-root.transform.forward, root.transform.up);

            gauge.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            Image background = Panel(rect, "Panel", new Color(0.02f, 0.02f, 0.02f, 0.85f));
            background.rectTransform.anchorMin = Vector2.zero;
            background.rectTransform.anchorMax = Vector2.one;
            background.rectTransform.sizeDelta = Vector2.zero;

            TextMeshProUGUI status = Label(rect, "StatusText", 40f, new Vector2(0f, 42f), new Vector2(340f, 50f));

            Image barBackground = Panel(rect, "BarBackground", new Color(0.12f, 0.12f, 0.12f, 1f));
            barBackground.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            barBackground.rectTransform.sizeDelta = new Vector2(320f, 26f);

            Image fill = Panel(barBackground.rectTransform, "BarFill", Color.white);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.sizeDelta = new Vector2(-4f, -4f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0f;

            TextMeshProUGUI amount = Label(rect, "AmountText", 30f, new Vector2(0f, -48f), new Vector2(340f, 40f));

            var ui = gauge.AddComponent<RepairProgressUI>();
            var so = new SerializedObject(ui);
            so.FindProperty("workstation").objectReferenceValue = station;
            so.FindProperty("fillBar").objectReferenceValue = fill;
            so.FindProperty("amountText").objectReferenceValue = amount;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Image Panel(Transform parent, string name, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI Label(Transform parent, string name, float size,
                                             Vector2 position, Vector2 extent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = extent;
            // Explicit: a TextMeshProUGUI created by script outside play mode is saved with no
            // font, and a prefab whose text has none renders nothing in a build.
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }
    }
}
