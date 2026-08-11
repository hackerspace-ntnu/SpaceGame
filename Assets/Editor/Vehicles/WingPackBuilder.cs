// Builds the inventory side of the ornithopter: the folded pack the player carries, and the
// InventoryItem that puts it in their hotbar.
//
//   Assets/Prefabs/items/WingPack.prefab            the thing held in hand
//   Assets/Resources/Items/Artifacts/WingPack.asset the InventoryItem that references it
//
// The held pack is assembled from primitives rather than from the model. The ornithopter's own
// meshes are no use here: the wings are SKINNED, so at rest they are spread across a six-metre
// span, and there is no folded pose to take without instantiating the rig and running the animator
// at build time. A strapped bundle of spars is what a folded flyer looks like anyway, and it costs
// eight boxes.
//
// Re-run from: Tools ▸ Vehicles ▸ Build Wing Pack Item
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
        private const string CraftPath = "Assets/Prefabs/agents/vehicle/DuneOrnithopter.prefab";
        private const string PrefabPath = "Assets/Prefabs/items/WingPack.prefab";
        private const string ItemPath = "Assets/Resources/Items/Artifacts/WingPack.asset";
        private const string ModelPath = "Assets/Art/Models/Vehicles/Ornithopter/dune_ornithopter.fbx";

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

            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0f, 0.05f);
            box.size = new Vector3(0.22f, 0.22f, 0.95f);

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
        /// A bundle of folded spars with a strap round it. Sized to sit in a hand: about 0.9 m long,
        /// which is a folded wing, not a rucksack.
        /// </summary>
        private static void BuildFoldedBundle(GameObject root)
        {
            Material spar = MaterialFrom("Mat_Metal_Steel_Worn");
            Material cloth = MaterialFrom("Mat_Fabric_Wing_Beige");
            Material strap = MaterialFrom("Mat_Fabric_Canvas_Faded");

            // Five spars, fanned very slightly so the bundle reads as folded rather than as one stick.
            for (int i = 0; i < 5; i++)
            {
                float t = i / 4f;
                float angle = Mathf.Lerp(-6f, 6f, t);
                var s = MakeBox(root, $"Spar_{i + 1}",
                    new Vector3(Mathf.Lerp(-0.05f, 0.05f, t), Mathf.Lerp(-0.03f, 0.03f, t), 0f),
                    new Vector3(0.022f, 0.022f, Mathf.Lerp(0.82f, 0.62f, Mathf.Abs(t - 0.5f) * 2f)),
                    spar);
                s.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
            }

            // The membrane, furled around them.
            MakeBox(root, "FurledWeb", new Vector3(0f, 0f, -0.05f),
                    new Vector3(0.13f, 0.13f, 0.52f), cloth);

            // Two straps holding the bundle together.
            MakeBox(root, "Strap_Fore", new Vector3(0f, 0f, 0.26f),
                    new Vector3(0.17f, 0.17f, 0.05f), strap);
            MakeBox(root, "Strap_Aft", new Vector3(0f, 0f, -0.24f),
                    new Vector3(0.17f, 0.17f, 0.05f), strap);
        }

        private static GameObject MakeBox(GameObject parent, string name, Vector3 pos, Vector3 size,
                                          Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;

            // The primitive's own collider would fight the single box on the root and make the item
            // catch on geometry while held.
            Object.DestroyImmediate(go.GetComponent<Collider>());

            if (mat != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>
        /// Pull a material off the imported model, so the pack is bleached desert kit like the craft
        /// it folds into rather than Unity default grey.
        /// </summary>
        private static Material MaterialFrom(string name)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
                if (o is Material m && m.name == name)
                    return m;

            Debug.LogWarning($"[WingPack] No material '{name}' on the model; using default.");
            return null;
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
