using System.IO;
using SpaceGame.Items;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools.Outposts
{
    /// <summary>
    /// The outposts stand hand-carried things about: an oil tank, a bucket, a cart, a lantern on a post. They are
    /// <c>Carry_*</c> and <c>Tool_*</c> items, whose prefabs are networked world items (pickups, rigidbodies, savers) and cannot be
    /// scenery. So each gets a plain model prefab made from the item's own meshes, standing on its own floor point,
    /// and the ones residents carry also get a <see cref="SettlementProp"/> saying which item a hand holds.
    /// </summary>
    public static class OutpostItemModels
    {
        private const string ItemAssetDir = "Assets/Game/Resources/Items/Tools";
        public const string ModelDir = "Assets/Game/Prefabs/Environment/Structures/Outpost/Models";

        /// <summary>Whether a piece kind is an item rather than a decoration prefab.</summary>
        public static bool IsItem(string kind) => kind.StartsWith("Carry_") || kind.StartsWith("Tool_");

        /// <summary>The model for <paramref name="item"/>, made on first use and rebuilt in place after: <c>Outpost_Prop_X</c> carries the item, <c>Outpost_Scenery_X</c> does not.</summary>
        public static GameObject EnsureModel(InventoryItem item, bool carried, out float lift)
        {
            GameObject source = item.itemPrefab;
            lift = LiftOf(source);

            string path = $"{ModelDir}/Outpost_{(carried ? "Prop" : "Scenery")}_{item.name}.prefab";
            Directory.CreateDirectory(ModelDir);
            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            try
            {
                var model = new GameObject("Model").transform;
                model.SetParent(root.transform, false);
                model.localPosition = Vector3.up * lift;
                foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!filter.TryGetComponent(out MeshRenderer renderer)) continue;

                    var part = new GameObject(filter.name);
                    part.transform.SetParent(model, false);
                    part.transform.localPosition = source.transform.InverseTransformPoint(filter.transform.position);
                    part.transform.localRotation = Quaternion.Inverse(source.transform.rotation) * filter.transform.rotation;
                    part.transform.localScale = filter.transform.lossyScale;
                    part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    part.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                }

                if (carried)
                {
                    var so = new SerializedObject(root.AddComponent<SettlementProp>());
                    so.FindProperty("item").objectReferenceValue = item;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        public static InventoryItem LoadItem(string name) => AssetDatabase.LoadAssetAtPath<InventoryItem>($"{ItemAssetDir}/{name}.asset");

        // How far the model must be raised so its lowest point is on the floor: the item's own origin is where a hand holds it.
        private static float LiftOf(GameObject source)
        {
            float lowest = float.PositiveInfinity;
            foreach (MeshRenderer renderer in source.GetComponentsInChildren<MeshRenderer>(true))
                lowest = Mathf.Min(lowest, renderer.bounds.min.y);
            return float.IsInfinity(lowest) ? 0f : -lowest;
        }
    }
}
