// The parts every ground placeable's builder shares: the item asset, the held half, the placed
// half's networking and save wiring, and the link that can only be made once all three exist.
//
// A placeable is a PAIR (see docs/AI/systems/Placeables.md): the held prefab carries the grip,
// pickup, physics and PlaceableItem + GroundPlacement; the placed prefab carries PlacedObject and
// whatever the thing is FOR. The FOR is each builder's own business -- a Light for the lantern, a
// StormWard and its shockwave for the ward. Everything else is the same recipe, written once here.
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class PlaceablePairBuilder
    {
        /// <summary>
        /// PickupableItem is internal to Assembly-CSharp, so an editor assembly can only add it by name.
        /// </summary>
        private const string PickupableItemType = "SpaceGame.Items.PickupableItem";

        /// <summary>
        /// The registry entry, created or refreshed. It MUST live under Resources/Items —
        /// RegistryLoader finds items with Resources.LoadAll, and an asset outside that tree never
        /// registers, never appears in the dev browser, and comes back empty from every save.
        /// </summary>
        public static InventoryItem EnsureItemAsset(string path, string itemName)
        {
            StaticPropBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));

            var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(asset, path);
            }

            var so = new SerializedObject(asset);
            SerializedFields.SetString(so, "itemName", itemName);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>An unpacked instance of <paramref name="source"/>, renamed. The caller saves and destroys it.</summary>
        public static GameObject Instantiate(GameObject source, string name)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            root.name = name;
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            return root;
        }

        /// <summary>
        /// The placed half's common wiring: PlacedObject returning <paramref name="asset"/>, and the
        /// NetworkObject + SaveableEntity it needs because it is spawned at runtime.
        /// </summary>
        public static void AddPlacedWiring(GameObject root, InventoryItem asset, string displayName)
        {
            var placedObject = root.AddComponent<PlacedObject>();
            var so = new SerializedObject(placedObject);
            SerializedFields.Set(so, "returnItem", asset);
            SerializedFields.SetString(so, "displayName", displayName);
            so.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<NetworkObject>();
            root.AddComponent<NetRelay>();
            root.AddComponent<SaveableEntity>();
            root.AddComponent<TransformSaveable>();
        }

        /// <summary>
        /// The held half: a carryable, droppable item that places <paramref name="placed"/> on flat
        /// ground. <paramref name="stripNames"/> are the model's wiring empties, which mean nothing
        /// in a hand. Saved to <paramref name="path"/>; returns the prefab.
        /// </summary>
        public static GameObject BuildHeld(GameObject source, string name, string path,
                                           InventoryItem asset, GameObject placed,
                                           params string[] stripNames)
        {
            StaticPropBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
            GameObject root = Instantiate(source, name);
            try
            {
                foreach (string strip in stripNames)
                {
                    Transform t = FindChild(root.transform, strip);
                    if (t != null) Object.DestroyImmediate(t.gameObject);
                }

                System.Type pickupType = typeof(ItemGrip).Assembly.GetType(PickupableItemType);
                if (pickupType == null)
                    throw new System.InvalidOperationException($"No type {PickupableItemType}; {name} would not be pickupable.");
                var pickup = new SerializedObject(root.AddComponent(pickupType));
                SerializedFields.Set(pickup, "item", asset);
                pickup.ApplyModifiedPropertiesWithoutUndo();

                // NetworkObject FIRST: ItemWorldPresence.EnsureNetworking enriches an existing
                // NetworkObject with NetworkTransform and NetAuthority; it does not create one.
                root.AddComponent<NetworkObject>();
                root.AddComponent<NetRelay>();
                ItemWorldPresence.Apply(root);

                root.AddComponent<SaveableEntity>();
                root.AddComponent<TransformSaveable>();
                root.AddComponent<ItemGrip>();

                // PlaceableItem owns the loop; GroundPlacement owns what "placing" means here.
                var placeable = root.AddComponent<PlaceableItem>();
                var placement = root.AddComponent<GroundPlacement>();
                var placementSo = new SerializedObject(placement);
                SerializedFields.Set(placementSo, "placedPrefab", placed);
                placementSo.ApplyModifiedPropertiesWithoutUndo();
                var placeableSo = new SerializedObject(placeable);
                SerializedFields.Set(placeableSo, "rule", placement);
                placeableSo.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>The item asset points at its held prefab — a link that needs both files to exist.</summary>
        public static void Link(InventoryItem asset, GameObject held)
        {
            var so = new SerializedObject(asset);
            SerializedFields.Set(so, "itemPrefab", held);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        public static Transform FindChild(Transform from, string name)
        {
            foreach (Transform t in from.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;
            return null;
        }
    }
}
