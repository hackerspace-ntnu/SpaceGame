// What a player picks up, carries and drops in the workshop's story: one item asset and one item prefab each. The gear
// doors' loose gears (GearDoorBuilder) and the stolen Heart Gear (WorkshopMissionBuilder) are modelled from the gear kit;
// the vault's sealed reactor core (WorkshopCraneBuilder) from the ship's own module.
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using SpaceGame.World.Safety;

namespace SpaceGame.EditorTools
{
    public static class CarriedGearItems
    {
        private const string GearFolder = "Assets/Game/Art/Models/Props/Gears/";
        private const string ItemFolder = "Assets/Game/Resources/Items/Workshop/";
        private const string ItemPrefabFolder = "Assets/Game/Prefabs/Items/Workshop/";

        // A carried gear is drawn no bigger than this across in the hands, so the biggest does not blind its carrier.
        private const float MaxHoldSize = 1.3f;

        public static GameObject LoadModel(string file)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(GearFolder + file + ".fbx");
            if (model == null) throw new FileNotFoundException($"Run gear_kit_export.py: missing {GearFolder}{file}.fbx");
            return model;
        }

        /// <summary>
        /// The item <paramref name="name"/>: its asset and its prefab (the kit's <paramref name="model"/> drawn at
        /// <paramref name="modelScale"/>, <paramref name="across"/> metres wide), each made the first time and kept after
        /// that. <paramref name="addRules"/> adds what the item does beyond being carried, on a prefab being made.
        /// </summary>
        public static InventoryItem Ensure(string name, string label, string model, float modelScale, float across,
                                           Action<GameObject, InventoryItem> addRules = null) =>
            Ensure(name, label, LoadModel(model), modelScale, across, addRules);

        /// <summary>As above, drawn from any <paramref name="model"/>, not only the gear kit's.</summary>
        public static InventoryItem Ensure(string name, string label, GameObject model, float modelScale, float across,
                                           Action<GameObject, InventoryItem> addRules = null)
        {
            Directory.CreateDirectory(ItemFolder);
            Directory.CreateDirectory(ItemPrefabFolder);

            string assetPath = ItemFolder + name + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(assetPath);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(item, assetPath);
            }
            // Saves store this id, and only OnValidate would otherwise write it: see InventoryItem.ID.
            if (string.IsNullOrEmpty(item.ID)) item.ID = AssetDatabase.AssetPathToGUID(assetPath);
            item.itemName = label;

            // Made once and then kept: a prefab saved again from a new object gets new file ids, and the network prefab
            // list (and any scene override) still names the old ones. Delete it to have it made again.
            string prefabPath = ItemPrefabFolder + name + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) prefab = BuildPrefab(name, model, modelScale, item, across, addRules);
            else if (!prefab.TryGetComponent(out UnderTerrainGuard _)) prefab = AddTerrainGuard(prefabPath);
            item.itemPrefab = prefab;
            EditorUtility.SetDirty(item);
            // Only when it has none: an icon already made is left as it is.
            if (!BatchIconGenerator.CreateFor(item, out string iconNote))
                Debug.LogWarning($"[CarriedGearItems] no inventory icon for {name}: {iconNote}", item);
            return item;
        }

        private static GameObject BuildPrefab(string name, GameObject model, float modelScale, InventoryItem item, float across,
                                              Action<GameObject, InventoryItem> addRules)
        {
            var root = new GameObject(name);
            try
            {
                var drawn = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                drawn.transform.localScale *= modelScale;

                PickupableItem pickup = root.AddComponent<PickupableItem>();
                ItemGrip grip = root.AddComponent<ItemGrip>();

                var so = new SerializedObject(pickup);
                SerializedFields.Set(so, "item", item);
                so.ApplyModifiedPropertiesWithoutUndo();

                so = new SerializedObject(grip);
                so.FindProperty("holdStyle").intValue = (int)ItemGrip.HoldStyle.TwoHanded;
                so.FindProperty("holdSize").floatValue = Mathf.Min(across, MaxHoldSize);
                so.ApplyModifiedPropertiesWithoutUndo();

                addRules?.Invoke(root, item);

                // Dropped and picked up like any loose item: it needs an entity for that to reach every machine.
                root.AddComponent<Unity.Netcode.NetworkObject>();
                root.AddComponent<SpaceGame.Core.NetRelay>();

                // A carried thing in the sand is the size it is drawn at.
                ItemWorldPresence.Apply(root, ItemWorldSizing.Authored);
                // The story's things must never be lost under the dunes: lifted back out if they fall through.
                root.AddComponent<UnderTerrainGuard>();
                // A dropped one is a runtime record, re-made from its prefab id on load.
                SaveablePolicy.Ensure(root, out _);
                string path = ItemPrefabFolder + name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return StampOnDisk(path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// A prefab made before the guard was part of the recipe gets one, in place: its file ids, and so its place in
        /// the network prefab list, are kept.
        /// </summary>
        private static GameObject AddTerrainGuard(string path)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                contents.AddComponent<UnderTerrainGuard>();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            return StampOnDisk(path);
        }

        /// <summary>
        /// Two ids a prefab saved from a scene object gets wrong ON DISK while the editor shows them right: its network
        /// hash (0, or another prefab's; NGO keeps one prefab per hash) and its save prefab id (empty: a runtime copy
        /// could never be re-made on load). A player reads the file, so write both into it and check they landed. A
        /// variant made from a scene instance also keeps that instance's in-scene source hash, which no prefab may have.
        /// </summary>
        public static GameObject StampOnDisk(string path)
        {
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var netObj = saved.GetComponent<Unity.Netcode.NetworkObject>();
            var entity = new SerializedObject(saved.GetComponent<SaveableEntity>());
            string guid = AssetDatabase.AssetPathToGUID(path);
            entity.FindProperty("prefabId").stringValue = guid;
            entity.ApplyModifiedPropertiesWithoutUndo();
            var network = new SerializedObject(netObj);
            network.FindProperty("InScenePlacedSourceGlobalObjectIdHash").uintValue = 0;
            network.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(netObj);
            AssetDatabase.SaveAssetIfDirty(saved);

            string file = File.ReadAllText(path);
            if (netObj.PrefabIdHash == 0 || !Holds(file, "GlobalObjectIdHash", netObj.PrefabIdHash.ToString()) ||
                !Holds(file, "prefabId", guid) || Holds(file, "InScenePlacedSourceGlobalObjectIdHash", "[1-9][0-9]*"))
                throw new InvalidDataException($"{path} does not hold its own network hash and save id on disk.");
            return saved;
        }

        /// <summary>
        /// Whether the prefab file sets <paramref name="field"/> to a value matching the pattern <paramref name="value"/>:
        /// as its own field, or as a variant's override of its base.
        /// </summary>
        private static bool Holds(string file, string field, string value) =>
            Regex.IsMatch(file, $@"(^|\n)\s*{field}: {value}\n") ||
            Regex.IsMatch(file, $@"propertyPath: {field}\n\s*value: {value}\n");
    }
}
