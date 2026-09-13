// Builds the interactable map-projector prefab from the exported holo base model.
//
// The model comes out of Blender via holo_base_export.py (one FBX per base variation, each
// carrying a Marker_HoloAnchor_* node at its emitter aperture). Everything above that — the
// collider, the fixed-anchor MapHologramTerrain, the power switch — is generated here rather
// than hand-wired, for the same reason the other builders exist: a prefab wired by hand is a
// prefab nobody can rebuild after the model changes.
//
// Re-running is safe and is the intended workflow. Re-export the FBX, run this, and the prefab
// is rebuilt in place against the new geometry.
//
// Re-run from: Tools > SpaceGame > Build Holo Projector Prefab
using System.IO;
using System.Linq;
using SpaceGame.Core.Persistence;
using SpaceGame.Core.Persistence.EditorTools;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class HoloProjectorBuilder
    {
        private const string Fbx = "Assets/Game/Art/Models/Props/holo_base_pedestal.fbx";
        private const string ConfigAsset = "Assets/Game/Settings/WorldStreamingConfig.asset";
        private const string PrefabPath = "Assets/Game/Prefabs/Environment/Structures/HoloProjector.prefab";

        // Pedestal geometry, measured from the export: 0.88 m tall, foot Ø0.40, aperture at 0.87.
        private static readonly Vector3 ColliderCenter = new Vector3(0f, 0.45f, 0f);
        private const float ColliderRadius = 0.20f;
        private const float ColliderHeight = 0.95f;

        // How far above the aperture the hologram's centre floats.
        private const float HologramLift = 0.4f;

        // Half-width of the charted window in chunks: 3 = 7 chunks = 3500 m across. See
        // ConfigureHologram for why that number.
        private const int ViewRadius = 3;

        [MenuItem("Tools/SpaceGame/Build Holo Projector Prefab")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (model == null) { Debug.LogError($"[HoloProjectorBuilder] No model at {Fbx} — run holo_base_export.py first."); return; }

            var config = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ConfigAsset);
            if (config == null) { Debug.LogError($"[HoloProjectorBuilder] No WorldStreamingConfig at {ConfigAsset}."); return; }

            var root = new GameObject("HoloProjector");
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
                visual.name = "Model";
                visual.transform.SetParent(root.transform, false);

                // The FBX marker's own axes carry the Blender export's -90°X / x100 bake, so only
                // its WORLD position is trusted; the anchor is a clean upright transform there.
                var marker = visual.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name.StartsWith("Marker_HoloAnchor"));
                if (marker == null) { Debug.LogError("[HoloProjectorBuilder] No Marker_HoloAnchor_* in the FBX."); return; }
                foreach (var r in marker.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;

                var anchor = new GameObject("ProjectorAnchor").transform;
                anchor.SetParent(root.transform, false);
                anchor.position = marker.position;

                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.center = ColliderCenter;
                capsule.radius = ColliderRadius;
                capsule.height = ColliderHeight;

                var hologram = root.AddComponent<MapHologramTerrain>();
                ConfigureHologram(hologram, config, anchor);

                var interaction = root.AddComponent<HoloProjectorInteraction>();
                var so = new SerializedObject(interaction);
                so.FindProperty("hologram").objectReferenceValue = hologram;
                so.ApplyModifiedPropertiesWithoutUndo();

                // Baked in rather than left to SaveablePolicy: the runtime auto-attach only runs
                // on an entity's ROOT, so a projector nested under the PlayerShip would never get
                // its saver — the ship's SaveableEntity collects child savers, but only ones that
                // exist. Standalone placements are unaffected; the policy's own add is null-guarded.
                root.AddComponent<ProjectorSaveable>();

                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();

                // A read-only AssetDatabase (an MPPM clone) discards prefab saves without erroring,
                // so "saved" is only true once the file is on disk.
                if (!File.Exists(PrefabPath))
                {
                    Debug.LogError($"[HoloProjectorBuilder] {PrefabPath} did not reach disk — is this a read-only editor clone?");
                    return;
                }

                // Wire it NOW, the way RepairStationBuilder does. The sweep gives every saveable
                // prefab a SaveableEntity and a TransformSaveable, which a standalone projector
                // needs and which PlayerShipBuilder strips off its nested instance — and it can
                // only strip what exists when it nests. Without this call a rebuild here silently
                // hands back an unwired prefab (`EveryWorldEntityPrefabIsWiredForSaving` is what
                // says so), and leaving it to the ship build's own sweep, which runs AFTER the
                // nesting, puts a second entity aboard the hull. The import first: the sweep finds
                // prefabs through the search index, which does not yet hold an asset saved in this
                // same call.
                AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
                if (!SaveableWiring.TryWirePrefabs())
                {
                    Debug.LogError("[HoloProjectorBuilder] The save-wiring pass refused to run, so the " +
                                   "prefab has no entity for the ship builder to strip — rebuild it stopped.");
                    return;
                }

                Debug.Log($"[HoloProjectorBuilder] Built {PrefabPath} (anchor at {anchor.position}).");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// A chart of the ground around the viewer, over the emitter: pinned to the anchor, lying
        /// flat, no hotkey (the interactable is the switch), fog of war still honoured so it is
        /// not a spoiler map.
        ///
        /// It centres on the player rather than on the world, because the question a map table is
        /// asked is "where am I" and the answer has to be at the middle of the plate, not off in a
        /// corner of it (GDC-L1-LEVEL-0002, GDC-L1-UX-0003). <see cref="ViewRadius"/> then sets the
        /// zoom: 7 chunks across is 3500 m in the 0.9 m footprint, near enough the whole 4000 x
        /// 3000 m world that the plate reads as the world chart it replaces.
        ///
        /// The vignette radius must exceed that window's half-diagonal (2475 m) or the chart is
        /// clipped to a circle inside its own terrain.
        /// </summary>
        private static void ConfigureHologram(MapHologramTerrain hologram, Object config, Transform anchor)
        {
            var so = new SerializedObject(hologram);
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("toggleActionName").stringValue = "";
            so.FindProperty("startVisible").boolValue = false;
            so.FindProperty("projectorAnchor").objectReferenceValue = anchor;
            so.FindProperty("helmetAnchor").objectReferenceValue = anchor;   // beam rises from the lens
            so.FindProperty("distance").floatValue = 0f;
            so.FindProperty("sideOffset").floatValue = 0f;
            so.FindProperty("height").floatValue = HologramLift;
            so.FindProperty("yawTowardPlayer").floatValue = 0f;
            so.FindProperty("leanTowardPlayer").floatValue = 0f;
            so.FindProperty("footprint").floatValue = 0.9f;
            so.FindProperty("viewRadius").intValue = ViewRadius;
            so.FindProperty("centerOnPlayer").boolValue = true;
            so.FindProperty("revealAllChunks").boolValue = true;
            so.FindProperty("beamOriginOffset").floatValue = 0.02f;
            so.FindProperty("mapRadius").floatValue = 3000f;
            so.FindProperty("mapEdgeFalloff").floatValue = 500f;
            so.FindProperty("markerLabelFontSize").floatValue = 8f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
