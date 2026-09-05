// Builds the camp lantern: the first placeable, and the worked example of the pair.
//
// A placeable is TWO prefabs, and this is why they cannot be one:
//
//   Lantern.prefab        the thing in your hand and lying in the sand. Grip pose, pickup,
//                         physics, and PlaceableItem, which spawns the other one and spends
//                         itself doing it.
//   PlacedLantern.prefab  the thing standing on the ground. A Light, a collider you cannot walk
//                         through, and PlacedObject, which hands the item back on Q.
//
// Re-running is safe and is the intended workflow: both prefabs are rebuilt in place, so anything
// added by hand in the Inspector is discarded by the next run with nothing said.
//
// Re-run from: Tools > Items > Build Camp Lantern
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class LanternBuilder
    {
        private const string Fbx = "Assets/Game/Art/Models/Items/camp_lantern.fbx";
        private const string HeldDir = "Assets/Game/Prefabs/Items/Artifacts/Gadgets";
        private const string HeldPath = HeldDir + "/Lantern.prefab";
        private const string PlacedDir = "Assets/Game/Prefabs/Items/Placed";
        private const string PlacedPath = PlacedDir + "/PlacedLantern.prefab";
        private const string AssetDir = "Assets/Game/Resources/Items/Artifacts";
        private const string AssetPath = AssetDir + "/Lantern.asset";

        // Warm and short-range: it lights a camp, not a football pitch, and a placeable light that
        // outshines the sun is how a survival game stops being dark.
        private static readonly Color FlameColour = new Color(1.00f, 0.77f, 0.42f);
        private const float FlameRange = 9f;
        private const float FlameIntensity = 3.2f;

        [MenuItem("Tools/Items/Build Camp Lantern")]
        public static void Build()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (source == null)
            {
                Debug.LogError($"No lantern model at {Fbx}. Export it first:\n" +
                               "  blender --background --python components/props/camp_lantern_export.py");
                return;
            }

            InventoryItem asset = BuildItemAsset();
            GameObject placed = BuildPlaced(source, asset);
            GameObject held = BuildHeld(source, asset, placed);

            // The files point at each other, so these links can only be made once all three exist.
            if (asset != null && held != null)
            {
                var so = new SerializedObject(asset);
                so.FindProperty("itemPrefab").objectReferenceValue = held;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Camp lantern built.\n  held:   {HeldPath}\n  placed: {PlacedPath}\n" +
                      $"  asset:  {AssetPath}\n" +
                      "Now run Tools > SpaceGame > Multiplayer > Sync Network Prefabs (BOTH prefabs " +
                      "spawn at runtime), then Tools > Generate All Item Icons.");
        }

        /// <summary>
        /// The lantern standing on the ground. A spawned NetworkObject, so it needs the network
        /// prefab list and a SaveableEntity or it is gone on the next load.
        /// </summary>
        private static GameObject BuildPlaced(GameObject source, InventoryItem asset)
        {
            EnsureFolder(PlacedDir);

            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            root.name = "PlacedLantern";
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);

            // Solid, not a trigger: you should not be able to walk through a lantern, and a solid
            // collider is also what lets the crosshair land on it. The interactable is on the ROOT,
            // which is where GetComponentInParent finds it from whichever child was hit.
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.16f, 0f);
            box.size = new Vector3(0.26f, 0.32f, 0.26f);

            // Placed where the model says, not where a constant guesses.
            Transform flame = Find(root.transform, "LIGHT_Flame");
            var lightGo = new GameObject("Flame");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition =
                flame != null ? flame.localPosition : new Vector3(0f, 0.135f, 0f);

            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = FlameColour;
            light.range = FlameRange;
            light.intensity = FlameIntensity;
            light.shadows = LightShadows.Soft;

            var placedObject = root.AddComponent<PlacedObject>();
            SetObject(placedObject, "returnItem", asset);
            SetString(placedObject, "displayName", "Camp lantern");

            root.AddComponent<NetworkObject>();
            root.AddComponent<NetRelay>();
            root.AddComponent<SaveableEntity>();
            root.AddComponent<TransformSaveable>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlacedPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>The lantern as a carryable, droppable, placeable item.</summary>
        private static GameObject BuildHeld(GameObject source, InventoryItem asset, GameObject placed)
        {
            EnsureFolder(HeldDir);

            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            root.name = "Lantern";
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);

            // The empty is the placed half's wiring and means nothing in a hand.
            Transform flame = Find(root.transform, "LIGHT_Flame");
            if (flame != null) Object.DestroyImmediate(flame.gameObject);

            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.16f, 0f);
            box.size = new Vector3(0.26f, 0.32f, 0.26f);

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = true;

            // PickupableItem is internal to Assembly-CSharp, so an editor assembly cannot name it.
            Component pickup = AddInternal(root, "SpaceGame.Items.PickupableItem");
            if (pickup != null) SetObject(pickup, "item", asset);

            var physics = root.AddComponent<DropItemPhysics>();
            SetObject(physics, "rb", body);
            SetInt(physics, "groundLayer", 128);

            root.AddComponent<NetworkObject>();
            root.AddComponent<NetRelay>();
            root.AddComponent<SaveableEntity>();
            root.AddComponent<TransformSaveable>();
            root.AddComponent<ItemGrip>();

            var placeable = root.AddComponent<PlaceableItem>();
            SetObject(placeable, "placedPrefab", placed);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, HeldPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// The registry entry. It MUST live under Resources/Items — RegistryLoader finds items with
        /// Resources.LoadAll, and an asset outside that tree never registers, never appears in the
        /// dev browser, and comes back empty from every save that held it.
        /// </summary>
        private static InventoryItem BuildItemAsset()
        {
            EnsureFolder(AssetDir);

            var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }

            var so = new SerializedObject(asset);
            SerializedProperty name = so.FindProperty("itemName");
            if (name != null) name.stringValue = "Camp Lantern";
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static Component AddInternal(GameObject go, string typeName)
        {
            System.Type type = typeof(ItemGrip).Assembly.GetType(typeName);
            if (type == null)
            {
                Debug.LogError($"No type {typeName}; the lantern will not be pickupable.");
                return null;
            }
            return go.AddComponent(type);
        }

        private static Transform Find(Transform from, string name)
        {
            foreach (Transform t in from.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        private static SerializedProperty Find(Object target, string field)
        {
            SerializedProperty p = new SerializedObject(target).FindProperty(field);
            if (p == null)
                Debug.LogWarning($"{target.GetType().Name} has no serialized field '{field}'.");
            return p;
        }

        private static void SetObject(Object target, string field, Object value)
        {
            SerializedProperty p = Find(target, field);
            if (p == null) return;
            p.objectReferenceValue = value;
            p.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(Object target, string field, string value)
        {
            SerializedProperty p = Find(target, field);
            if (p == null) return;
            p.stringValue = value;
            p.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(Object target, string field, int value)
        {
            SerializedProperty p = Find(target, field);
            if (p == null) return;
            p.intValue = value;
            p.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
