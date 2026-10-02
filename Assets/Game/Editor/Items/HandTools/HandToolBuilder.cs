// Builds one item per carried tool: a prefab, an InventoryItem, an icon and a network-prefab entry.
//
// The tools are modelled in decorations.blend (rows Deco_Tools*, Deco_Carriers), exported one FBX per
// collection with its origin at the grip, and listed in HandToolRoster. This turns the list into
// items. It replaces each prefab wholesale, like every other item builder here, so tuning belongs in
// the roster; the prefab and item asset keep their GUIDs across a re-run, so nothing that points at
// one (a loadout, a save) loses its reference.
//
// Run from: Tools ▸ SpaceGame ▸ Items ▸ Hand Tools ▸ Build All
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public static class HandToolBuilder
    {
        /// <summary>Degrees a seated tool may sit off its stance before the build calls it a failure.</summary>
        private const float MaxFitResidual = 1f;

        [MenuItem("Tools/SpaceGame/Items/Hand Tools/Build All")]
        public static void BuildAll()
        {
            int built = 0;
            using (HandToolRig rig = HandToolRig.Create())
            {
                if (rig == null) return;
                foreach (HandToolSpec spec in HandToolRoster.All)
                    if (Build(spec, rig)) built++;
            }

            NetworkPrefabRegistrar.Sync(out int added, out int total);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[HandTools] Built {built} of {HandToolRoster.All.Count} tools; " +
                      $"{added} added to the network prefab list ({total} entries).");
        }

        /// <summary>Build one tool. False, with the reason logged, when its model is missing.</summary>
        public static bool Build(HandToolSpec spec, HandToolRig rig)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
            if (model == null)
            {
                Debug.LogError($"[HandTools] {spec.Id}: no model at {spec.ModelPath}.");
                return false;
            }

            GameObject root = new GameObject(spec.Id);
            try
            {
                Compose(root, model, spec, GripFitter.Fit(rig, spec));

                Directory.CreateDirectory(Path.GetDirectoryName(spec.PrefabPath) ?? ".");
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath);
                if (prefab == null)
                {
                    Debug.LogError($"[HandTools] {spec.Id}: saving {spec.PrefabPath} failed.");
                    return false;
                }

                InventoryItem item = EnsureItem(spec, prefab);
                WireItemIntoPickup(prefab, item);

                float residual = GripFitter.Residual(rig, spec, prefab);
                if (residual > MaxFitResidual)
                    Debug.LogError($"[HandTools] {spec.Id}: seated {residual:F1} degrees off its {spec.Stance} stance.");

                if (!BatchIconGenerator.CreateFor(item, out string note))
                    Debug.LogWarning($"[HandTools] {spec.Id}: no icon — {note}");

                return true;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void Compose(GameObject root, GameObject model, HandToolSpec spec, GripFit fit)
        {
            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.name = "Model";
            modelInstance.transform.SetParent(root.transform, false);

            // A NetworkObject on a prefab that is not saved through PrefabUtility ships hash 0 and
            // can never be spawned on a client; this one is, below.
            root.AddComponent<NetworkObject>().SynchronizeTransform = true;
            root.AddComponent<PickupableItem>();

            // The body, the fitted collider, the world sizing and the netcode that lets another
            // machine watch the item be shoved about. Measures the meshes, so after the model.
            ItemWorldPresence.Apply(root);

            root.AddComponent<NetRelay>();
            root.AddComponent<SaveableEntity>();
            root.AddComponent<TransformSaveable>();
            root.AddComponent<RigidbodySaveable>();

            AddGrip(root, spec, fit);
            AddBelt(root, spec);

            var tool = root.AddComponent<CarriedToolItem>();
            if (!string.IsNullOrEmpty(spec.UseAction))
                SetObject(tool, "useAction", FindAction(spec));
        }

        private static void AddGrip(GameObject root, HandToolSpec spec, GripFit fit)
        {
            var grip = root.AddComponent<ItemGrip>();
            var so = new SerializedObject(grip);
            so.FindProperty("holdStyle").enumValueIndex = (int)fit.Pose;
            so.FindProperty("rotationOffset").vector3Value = fit.Rotation;
            so.FindProperty("positionOffset").vector3Value = fit.Position;
            so.FindProperty("holdSize").floatValue = spec.HoldSize;
            so.FindProperty("packSize").floatValue = spec.PackSize;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddBelt(GameObject root, HandToolSpec spec)
        {
            if (!spec.OnBelt) return;

            // +Y toward the belt, +Z out from the body. The item's own front (+Z) is the best guess
            // at "out", unless the item hangs along it, in which case any sideways axis will do.
            Vector3 up = -spec.HangDown.normalized;
            Vector3 outward = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (outward.sqrMagnitude < 1e-4f) outward = Vector3.ProjectOnPlane(Vector3.right, up);

            var hang = new GameObject("BeltHang").transform;
            hang.SetParent(root.transform, false);
            hang.localPosition = spec.HangPoint;
            hang.localRotation = Quaternion.LookRotation(outward.normalized, up);

            var mount = root.AddComponent<BeltMount>();
            var so = new SerializedObject(mount);
            so.FindProperty("hang").objectReferenceValue = hang;
            so.FindProperty("preferred").enumValueIndex = (int)spec.Slot;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static InventoryItem EnsureItem(HandToolSpec spec, GameObject prefab)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(spec.ItemPath) ?? ".");

            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(spec.ItemPath);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(item, spec.ItemPath);
            }

            item.itemName = spec.Title;
            item.itemPrefab = prefab;
            item.equipKind = EquipKind.Hand;
            item.showInDevBrowser = false;
            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>The item asset references the saved prefab and the prefab the item, so the second link waits for both files.</summary>
        private static void WireItemIntoPickup(GameObject prefab, InventoryItem item)
        {
            var pickup = prefab.GetComponent<PickupableItem>();
            SetObject(pickup, "item", item);
            PrefabUtility.SavePrefabAsset(prefab);
        }

        private static Object FindAction(HandToolSpec spec)
        {
            string guid = AssetDatabase.FindAssets($"{spec.UseAction} t:CharacterAction")
                .FirstOrDefault(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == spec.UseAction);
            if (guid == null)
            {
                Debug.LogError($"[HandTools] {spec.Id}: no CharacterAction named '{spec.UseAction}'.");
                return null;
            }

            return AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
        }

        private static void SetObject(Component target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
