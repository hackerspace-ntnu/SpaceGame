// The hoist under a driver's hand, built into Workshop.prefab by WorkshopPrefabBuilder after the machinery is wired
// (docs/AI/systems/Workshop.md): the WorkshopCrane on the hoist, the grab point and the hang-it-here box on its hook, the
// control console on the third-floor balcony where the model marks it, and the one load it carries, the vault's sealed
// reactor core (an item asset and prefab, made once and kept).
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class WorkshopCraneBuilder
    {
        private const string LiftNode = "Hoist_Pivot_Lift";
        private const string HookNode = "Hoist_Pivot_HookBlock";
        private const string PanelMark = "CraneConsole_Panel";
        private const string StanceMark = "CraneConsole_Stance";

        // The sealed core is the ship's own reactor module, at its true size, which the hoist alone can fit.
        private const string CoreModelPath = "Assets/Game/Art/Models/Items/ShipParts/reactorcore.fbx";
        private const string CoreItem = "VaultReactorCore";
        private const string CoreLabel = "Sealed reactor core";
        private const float CoreModelScale = 1f;

        // Where on the hook block a load hangs: this far up from the hook's tip, in its bowl.
        private const float HookBowl = 0.35f;
        // The hang-it-here box is the hook block's own, grown by this much all round so it is easy to aim at.
        private const float HookAimMargin = 0.4f;
        // The console's box the crosshair finds, and how far from its stance the operator may stand.
        private static readonly Vector3 PanelBox = new(1.8f, 1.4f, 1.4f);
        private const float OperatorReach = 1.6f;

        // Nothing the hoist bumps into is a player or a part of the interface.
        private static readonly string[] NotBlockers = { "Player", "Ignore Raycast", "UI", "Hologram", "PackItem", "Schematic" };

        /// <summary>Build the driven hoist into <paramref name="root"/>. Returns what it built, for the rebuild's log line.</summary>
        public static string Attach(GameObject root, WorkshopLayout layout, Dictionary<string, Transform> nodes,
                                    WorkshopMachinery machinery, out WorkshopCrane crane, out Transform console)
        {
            if (!nodes.TryGetValue(LiftNode, out Transform lift) || !nodes.TryGetValue(HookNode, out Transform hook))
                throw new InvalidDataException($"workshop_moving.fbx has no '{LiftNode}' or '{HookNode}'. Re-run workshop_export.py.");
            WorkshopLayout.Hoist hoist = layout.hoist;
            if (hoist?.bridgeTravel == null || hoist.bridgeTravel.Length < 2 || hoist.trolleyTravel == null || hoist.trolleyTravel.Length < 2)
                throw new InvalidDataException("workshop_layout.json has no hoist travel. Re-run workshop_export.py.");

            Transform holder = new GameObject("Crane").transform;
            holder.SetParent(root.transform, false);

            // The hook block as modelled, in the prefab's space: its tip is the lowest thing on it.
            Bounds block = PrefabBounds(root.transform, hook);
            Vector3 point = new(block.center.x, block.min.y + HookBowl, block.center.z);
            var box = new Bounds(block.center - point, block.size);

            // Made at metre scale and hung on the hook keeping that: the hook is an FBX node at scale 100.
            Transform loadPoint = new GameObject("LoadPoint").transform;
            loadPoint.SetPositionAndRotation(root.transform.TransformPoint(point), Quaternion.LookRotation(TravelOf(layout, "HoistTrolley"), Vector3.up));
            loadPoint.SetParent(hook, true);

            var reach = new WorkshopCraneReach
            {
                bridge = new Vector2(hoist.bridgeTravel[0], hoist.bridgeTravel[1]),
                trolley = new Vector2(hoist.trolleyTravel[0], hoist.trolleyTravel[1]),
                hookDrop = hoist.metresPerStop[2] * (hoist.stops[2] - 1),
                liftClimb = hoist.metresPerStop[3] * (hoist.stops[3] - 1),
            };

            GameObject coreModel = AssetDatabase.LoadAssetAtPath<GameObject>(CoreModelPath);
            if (coreModel == null) throw new FileNotFoundException($"Run ship_parts_export.py: missing {CoreModelPath}.");
            InventoryItem core = CarriedGearItems.Ensure(CoreItem, CoreLabel, coreModel, CoreModelScale, Across(coreModel));
            AssetDatabase.SaveAssets();

            crane = holder.gameObject.AddComponent<WorkshopCrane>();
            crane.Author(reach, TravelOf(layout, "HoistBridge"), TravelOf(layout, "HoistTrolley"), point, box, lift, loadPoint,
                         machinery, core, coreModel);
            var so = new SerializedObject(crane);
            so.FindProperty("blockers").intValue = ~LayerMask.GetMask(NotBlockers);
            so.ApplyModifiedPropertiesWithoutUndo();

            // What the crosshair finds on the hook: a trigger riding with it.
            Transform aim = new GameObject("HangHere").transform;
            aim.SetPositionAndRotation(root.transform.TransformPoint(block.center), Quaternion.identity);
            aim.SetParent(hook, true);
            BoxCollider target = aim.gameObject.AddComponent<BoxCollider>();
            target.isTrigger = true;
            target.size = (block.size + Vector3.one * (2f * HookAimMargin)) / aim.lossyScale.x;
            aim.gameObject.AddComponent<WorkshopCraneHook>().Author(crane);

            console = AddConsole(root.transform, holder, layout, crane);
            return $"crane: bridge {reach.bridge.x:0.#}..{reach.bridge.y:0.#} m, trolley {reach.trolley.x:0.#}..{reach.trolley.y:0.#} m, " +
                   $"hook {reach.hookDrop:0.#} m down, lift {reach.liftClimb:0.#} m up; console at {console.localPosition}";
        }

        private static Transform AddConsole(Transform root, Transform holder, WorkshopLayout layout, WorkshopCrane crane)
        {
            Pose panel = layout.PoseOf(PanelMark);
            Pose stance = layout.PoseOf(StanceMark);

            Transform console = new GameObject("Console").transform;
            console.SetParent(holder, false);
            console.SetLocalPositionAndRotation(panel.position, stance.rotation);
            BoxCollider box = console.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = PanelBox;

            Transform standAt = new GameObject("Stance").transform;
            standAt.SetParent(holder, false);
            standAt.SetLocalPositionAndRotation(stance.position, stance.rotation);

            console.gameObject.AddComponent<WorkshopCraneConsole>().Author(crane, standAt, OperatorReach);
            return console;
        }

        /// <summary>The way a metre of a hoist channel moves the hook, in the prefab's space, as the layout's slider says.</summary>
        private static Vector3 TravelOf(WorkshopLayout layout, string channel)
        {
            foreach (WorkshopLayout.Slider slider in layout.sliders)
                if (slider.channel == channel) return WorkshopLayout.V(slider.dir).normalized;
            throw new InvalidDataException($"workshop_layout.json has no slider on channel '{channel}'.");
        }

        /// <summary>Everything drawn under <paramref name="node"/>, in <paramref name="root"/>'s space.</summary>
        private static Bounds PrefabBounds(Transform root, Transform node)
        {
            Bounds? all = null;
            foreach (MeshFilter filter in node.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.name.StartsWith("COL")) continue;
                Bounds local = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = root.InverseTransformPoint(filter.transform.TransformPoint(new Vector3(
                        (corner & 1) == 0 ? local.min.x : local.max.x, (corner & 2) == 0 ? local.min.y : local.max.y,
                        (corner & 4) == 0 ? local.min.z : local.max.z)));
                    Bounds grown = all ?? new Bounds(point, Vector3.zero);
                    grown.Encapsulate(point);
                    all = grown;
                }
            }
            return all ?? throw new InvalidDataException($"the hoist's '{node.name}' has no meshes under it.");
        }

        /// <summary>The model's longest side, for how big it is drawn in the hands.</summary>
        private static float Across(GameObject model)
        {
            float across = 0f;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Vector3 size = Vector3.Scale(filter.sharedMesh.bounds.size, filter.transform.lossyScale);
                across = Mathf.Max(across, size.x, size.y, size.z);
            }
            return across;
        }
    }
}
