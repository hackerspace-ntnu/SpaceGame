// Builds the inventory side of the ornithopter: the folded pack the player carries, and the
// InventoryItem that puts it in their hotbar.
//
//   Assets/Game/Prefabs/items/WingPack.prefab            the thing held in hand
//   Assets/Game/Resources/Items/Artifacts/WingPack.asset the InventoryItem that references it
//
// The held pack is the actual craft in its stowed configuration: wings swept back along the boom,
// digit spars collapsed onto each other, tail telescoped. That pose is baked to a single static
// mesh in Blender (`_Source~/models/vehicles/wing_pack_folded.py` — the skinned wings make it
// impossible to pose at build time here) and exported hand-sized, so nesting the FBX is all this
// builder has to do.
//
// Re-run from: Tools ▸ Vehicles ▸ Build Wing Pack Item.
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Vehicles.Ornithopter;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class WingPackBuilder
    {
        private const string CraftPath =
            "Assets/Game/Prefabs/Agents/Vehicles/Aircraft/DuneOrnithopter.prefab";
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Equipment/WingPack.prefab";
        private const string ItemPath = "Assets/Game/Resources/Items/Artifacts/WingPack.asset";
        private const string FoldedModelPath =
            "Assets/Game/Art/Models/Vehicles/Ornithopter/wing_pack_folded.fbx";

        [MenuItem("Tools/Vehicles/Build Wing Pack Item")]
        public static void Build()
        {
            GameObject craft = AssetDatabase.LoadAssetAtPath<GameObject>(CraftPath);
            if (craft == null)
            {
                Debug.LogError($"[WingPack] No craft at {CraftPath}. Run " +
                               "Tools ▸ Vehicles ▸ Build Dune Ornithopter Prefab first.");
                return;
            }

            var root = new GameObject("WingPack");
            BuildFoldedBundle(root);

            // Kinematic body + collider, per the inventory system's contract: a dropped item needs a
            // Rigidbody to be thrown and a collider to be picked back up, and isKinematic keeps it
            // still on the ground until DropItemPhysics takes over.
            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            // The folded craft's baked bounds; the mesh origin is its bounds centre.
            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = new Vector3(0.41f, 0.16f, 0.95f);

            WingPackItem item = root.AddComponent<WingPackItem>();
            var so = new SerializedObject(item);
            Set(so, "ornithopterPrefab", craft);
            SetFloat(so, "groundClearance", 0.6f);
            SetFloat(so, "minLaunchClearance", 6f);
            SetFloat(so, "ledgeProbeForward", 1.5f);
            SetInt(so, "groundMask", ~0);
            SetFloat(so, "speedCarry", 1f);
            SetFloat(so, "launchLift", 1.2f);
            // Unlimited: the pack is equipment, not a consumable. -1 is UsableItem's sentinel.
            SetInt(so, "maxUses", -1);
            so.ApplyModifiedPropertiesWithoutUndo();

            // True size, on the pack and in the hand alike. 1.26 m is the biggest the rack can
            // carry: at the folded craft's 0.405 : 0.95 proportions its width comes to 0.537 m —
            // exactly the rack's six columns, the hard limit — while the length rides the rack's
            // overhang rule (PackOverhang), spanning the full 0.72 m panel and hanging 0.27 m
            // past each end. Without a grip it would measure at the 0.30 m no-grip default.
            ItemGrip grip = root.AddComponent<ItemGrip>();
            var gripSo = new SerializedObject(grip);
            SetFloat(gripSo, "holdSize", 1.26f);
            gripSo.ApplyModifiedPropertiesWithoutUndo();

            AddIfPresent(root, "DropItemPhysics");

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            BuildInventoryItem(saved);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[WingPack] Built {PrefabPath} and {ItemPath}.");
        }

        /// <summary>
        /// Nest the baked folded-craft model. It is exported already hand-sized (~0.95 m long), so
        /// no scale correction belongs here — a wrong size means the export is what to fix. Axes
        /// are this wiring's job though: a bare static mesh arrives in Blender's frame (length on
        /// Y, up on Z), so it gets the standard -90° X that puts the nose on +Z and up on +Y.
        /// </summary>
        private static void BuildFoldedBundle(GameObject root)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(FoldedModelPath);
            if (model == null)
            {
                Debug.LogError($"[WingPack] No folded model at {FoldedModelPath}. Run " +
                               "_Source~/models/vehicles/wing_pack_folded_export.py first.");
                return;
            }

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.name = "FoldedCraft";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        }

        private static void BuildInventoryItem(GameObject prefab)
        {
            InventoryItem item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            bool isNew = item == null;
            if (isNew)
                item = ScriptableObject.CreateInstance<InventoryItem>();

            item.itemName = "Wing Pack";
            item.itemPrefab = prefab;

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ItemPath));
            if (isNew)
                AssetDatabase.CreateAsset(item, ItemPath);
            else
                EditorUtility.SetDirty(item);
        }

        /// <summary>Add a component by type name if the project has it, so a missing optional
        /// system does not fail the whole build.</summary>
        private static void AddIfPresent(GameObject go, string typeName)
        {
            System.Type t = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(typeName))
                .FirstOrDefault(x => x != null);
            if (t != null) go.AddComponent(t);
            else Debug.LogWarning($"[WingPack] No type '{typeName}'; skipped.");
        }

        private static void Set(SerializedObject so, string field, Object value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.objectReferenceValue = value;
        }

        private static void SetFloat(SerializedObject so, string field, float value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.floatValue = value;
        }

        private static void SetInt(SerializedObject so, string field, int value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.intValue = value;
        }

        private static SerializedProperty Find(SerializedObject so, string field)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
                Debug.LogWarning($"[WingPack] {so.targetObject.GetType().Name} has no serialized " +
                                 $"field '{field}' -- it was renamed; this value is unset.");
            return p;
        }
    }
}
