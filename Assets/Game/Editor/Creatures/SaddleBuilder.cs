// Builds every Unity-side asset the saddle needs, from the exported FBX up.
//
// Two prefabs, because a saddle is two different things:
//
//   AppaSaddle.prefab   the saddle ON an animal. A plain prefab -- SaddleSocket instantiates it
//                       onto a bone on every machine, so it must NOT carry a NetworkObject.
//                       Carries the PackContainer and the "take it off" trigger.
//   Saddle.prefab       the saddle as an ITEM: in a hand, in a hotbar, or lying in the sand.
//                       Networked, pickupable, saveable, and a PLACEABLE: its placement rule
//                       is SaddlePlacement, whose 'ground' is an animal with a socket.
//
// Re-running is safe and is the intended workflow. Both prefabs are rebuilt in place, so
// everything the saddle needs must live in this file -- a component added by hand in the Inspector
// is discarded by the next run with nothing said.
//
// Re-run from: Tools > Creatures > Build Saddle
using System.Linq;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class SaddleBuilder
    {
        private const string WornDir = "Assets/Game/Prefabs/Items/Saddles";
        private const string ItemDir = "Assets/Game/Prefabs/Items/Artifacts/Gadgets";
        private const string AssetDir = "Assets/Game/Resources/Items/Artifacts";

        // There is ONE saddle item, and it fits every animal that has a socket for one.
        //
        // What differs per animal is the saddle you SEE once it is on: a bison's panniers hang
        // where a bounding biped has no ribs to hang them from. So the worn prefab is per animal
        // and the item is not -- you carry "a saddle", and which one appears is the animal's
        // business, exactly as SaddleSocket.saddlePrefab already said.
        private const string ItemFile = "Saddle";
        private const string ItemName = "Saddle";

        /// <summary>
        /// One animal's WORN saddle. Everything that differs between animals lives here rather
        /// than in a second copy of the builder: the model, the names of the empties inside it,
        /// how many cells each face carries, and the seat's own grip radius.
        /// </summary>
        private readonly struct Design
        {
            public readonly string Name;          // "Appa" -> AppaSaddle.prefab
            public readonly string Fbx;
            public readonly string Suffix;        // empties are SURF_SaddleLeft + this
            public readonly float GripRadius;
            public readonly (PackSurfaceId Id, string Empty, Vector2Int Cells)[] Surfaces;

            public Design(string name, string fbx, string suffix, float gripRadius,
                          string wornFile, (PackSurfaceId, string, Vector2Int)[] surfaces)
            {
                Name = name; Fbx = fbx; Suffix = suffix;
                GripRadius = gripRadius; Surfaces = surfaces;
                WornPath = $"{WornDir}/{wornFile}.prefab";
            }

            public readonly string WornPath;

            // Explicit, not derived from Name: the item shipped as Saddle.prefab / Saddle.asset
            // and deriving a tidier name would move those files, silently breaking every GUID
            // reference to them -- the network prefab list and AppaBuilder's SaddleItemPath among
            // them.
            public static string ItemPath => $"{ItemDir}/{ItemFile}.prefab";
            public static string AssetPath => $"{AssetDir}/{ItemFile}.asset";
        }

        // Faces, in whole cells. Appa gets 3x5 either side and 4x3 behind the cantle = 42 cells,
        // against the expedition rig's 255 -- a saddle is a day's gear, not a household. The
        // Sandloper carries roughly half that: he is a narrow biped with no barrel to hang
        // panniers off, so his boards sit behind the cantle rather than beside the seat.
        private static readonly Design[] Designs =
        {
            new Design("Appa",
                       "Assets/Game/Art/Models/Items/saddle_appa.fbx", "", 0.40f,
                       "AppaSaddle",
                       new (PackSurfaceId, string, Vector2Int)[]
                       {
                           (PackSurfaceId.SaddleLeft, "SURF_SaddleLeft", new Vector2Int(3, 5)),
                           (PackSurfaceId.SaddleRight, "SURF_SaddleRight", new Vector2Int(3, 5)),
                           (PackSurfaceId.SaddleRear, "SURF_SaddleRear", new Vector2Int(4, 3)),
                       }),
            new Design("Sandloper",
                       "Assets/Game/Art/Models/Items/saddle_loper.fbx", "_Loper", 0.16f,
                       "SandloperSaddle",
                       new (PackSurfaceId, string, Vector2Int)[]
                       {
                           (PackSurfaceId.LoperLeft, "SURF_SaddleLeft_Loper", new Vector2Int(2, 3)),
                           (PackSurfaceId.LoperRight, "SURF_SaddleRight_Loper", new Vector2Int(2, 3)),
                           (PackSurfaceId.LoperRear, "SURF_SaddleRear_Loper", new Vector2Int(3, 2)),
                       }),
        };

        [MenuItem("Tools/Creatures/Build Saddle")]
        public static void Build()
        {
            // One item, built from the first design's model. Which fitted saddle you actually see
            // is the animal's business -- SaddleSocket.saddlePrefab -- so the thing in your pack is
            // just "a saddle" and there is deliberately not one per creature to collect.
            GameObject itemSource = AssetDatabase.LoadAssetAtPath<GameObject>(Designs[0].Fbx);
            InventoryItem asset = BuildItemAsset();
            GameObject item = itemSource != null ? BuildItem(itemSource, asset) : null;

            if (asset != null && item != null)
            {
                var so = new SerializedObject(asset);
                so.FindProperty("itemPrefab").objectReferenceValue = item;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }

            int worn = 0;
            foreach (Design design in Designs)
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(design.Fbx);
                if (source == null)
                {
                    Debug.LogError($"No saddle model at {design.Fbx}. Export it with " +
                                   "blender --background --python models/gear/saddle_export.py");
                    continue;
                }

                BuildWorn(source, design);
                Debug.Log($"{design.Name} saddle: {design.WornPath} | " +
                          $"{design.Surfaces.Sum(f => f.Cells.x * f.Cells.y)} pack cells across " +
                          $"{design.Surfaces.Length} faces.");
                worn++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"{worn} worn saddle(s) and one item ({Design.ItemPath}). Now run Tools > " +
                      "SpaceGame > Multiplayer > Sync Network Prefabs, Tools > Save System > Wire " +
                      "Saveable Prefabs, then Tools > Generate All Item Icons.");
        }

        /// <summary>
        /// The saddle as it sits on an animal. No NetworkObject on purpose: every machine builds
        /// its own copy from <see cref="SaddleSocket"/>'s replicated flag, exactly as the backpack
        /// does, because a NetworkObject parented into a bone hierarchy has to have that parenting
        /// replicated and re-applied after every spawn and every load.
        /// </summary>
        private static GameObject BuildWorn(GameObject source, Design design)
        {
            EnsureFolder(WornDir);

            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            root.name = design.Name + "Saddle";
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);

            // -- the pack faces -------------------------------------------------
            foreach ((PackSurfaceId id, string emptyName, Vector2Int cells) in design.Surfaces)
            {
                Transform anchor = Find(root.transform, emptyName);
                if (anchor == null)
                {
                    Debug.LogError($"{design.Fbx} has no '{emptyName}'. The face cannot be placed, so " +
                                   "the saddle would silently carry nothing.");
                    continue;
                }

                var surface = anchor.gameObject.AddComponent<PackSurface>();
                SetEnum(surface, "id", (int)id);
                SetVector2(surface, "size", new Vector2(cells.x, cells.y) * PackGrid.Cell);
            }

            // The container itself. WallInventory is "a PackContainer bolted to something, with no
            // fold, no deploy and no owner" -- which is a saddle exactly. Its surface list is left
            // empty so it resolves to every PackSurface in the children.
            root.AddComponent<WallInventory>();

            // -- "take it off", offered by the saddle and not by the animal -----
            // One grip per side plus one on the seat, because ONE on the seat is not reachable.
            // The animal's torso collider is solid, and Interactor resolves the nearest thing the
            // ray hits: on Appa that box reaches x +-1.26 m and 3.45 m up, so a ray aimed at a grip
            // on his centreline crosses his ribs first and answers "ride" every time. The grips
            // that work are the ones standing outboard of the body, on the saddle's own side
            // furniture -- which is also where a person reaches to unstrap a saddle.
            foreach ((string anchor, float radius) in new[]
                     {
                         ("SURF_SaddleLeft" + design.Suffix, design.GripRadius),
                         ("SURF_SaddleRight" + design.Suffix, design.GripRadius),
                         // and from up top, once you have dismounted
                         ("SEAT_Rider" + design.Suffix, design.GripRadius * 0.75f),
                     })
            {
                Transform at = Find(root.transform, anchor);
                if (at == null)
                {
                    Debug.LogWarning($"{design.Fbx} has no '{anchor}'; that is one fewer place the saddle " +
                                     "can be taken off from.");
                    continue;
                }

                var grip = new GameObject("SaddleGrip_" + anchor);
                grip.transform.SetParent(root.transform, false);
                grip.transform.localPosition = at.localPosition;

                var sphere = grip.AddComponent<SphereCollider>();
                sphere.isTrigger = true;   // a trigger answers only for its OWN interactable, which
                sphere.radius = radius;    // is what keeps "ride" on the animal and this on the saddle
                grip.AddComponent<SaddleRemover>();
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, design.WornPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>The saddle as a carryable, droppable, saveable item.</summary>
        private static GameObject BuildItem(GameObject source, InventoryItem asset)
        {
            EnsureFolder(ItemDir);

            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            root.name = ItemName;
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);

            // The empties are the worn saddle's wiring and mean nothing in a hand.
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true).ToArray())
                if (t != null && t != root.transform && t.name.StartsWith("SURF_"))
                    Object.DestroyImmediate(t.gameObject);

            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0f, 0.05f);
            box.size = new Vector3(0.70f, 0.45f, 0.95f);

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = true;

            // PickupableItem is internal to Assembly-CSharp, so it cannot be named from an
            // editor assembly at all -- added by type name, the way every other item builder here
            // does it.
            Component pickup = AddInternal(root, "SpaceGame.Items.PickupableItem");
            if (pickup != null) SetObject(pickup, "item", asset);

            var physics = root.AddComponent<DropItemPhysics>();
            SetObject(physics, "rb", body);
            SetInt(physics, "groundLayer", 128);

            root.AddComponent<NetworkObject>();
            root.AddComponent<NetRelay>();
            root.AddComponent<SaveableEntity>();
            root.AddComponent<TransformSaveable>();

            var grip = root.AddComponent<ItemGrip>();
            // Two hands and heavy: it is a metre of leather, not a pistol.
            SetEnum(grip, "holdStyle", (int)ItemGrip.HoldStyle.TwoHanded);

            // A placeable, not a bespoke artifact. PlaceableItem owns the loop -- aim, validate,
            // place, spend -- and SaddlePlacement owns the two things that differ: the criteria
            // (an animal with a free SaddleSocket) and the logic (fit it, which is not a spawn).
            var placeable = root.AddComponent<PlaceableItem>();
            var placement = root.AddComponent<SaddlePlacement>();
            SetObject(placeable, "rule", placement);
            SetFloat(placeable, "range", 4.5f);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, Design.ItemPath);
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

            var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(Design.AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(asset, Design.AssetPath);
            }

            var so = new SerializedObject(asset);
            SerializedProperty name = so.FindProperty("itemName");
            if (name != null) name.stringValue = ItemName;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        // ── helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Add a component this assembly cannot name. Some item components are internal to
        /// Assembly-CSharp; an editor script reaches them by type name or not at all.
        /// </summary>
        private static Component AddInternal(GameObject go, string typeName)
        {
            System.Type type = typeof(ItemGrip).Assembly.GetType(typeName);
            if (type == null)
            {
                Debug.LogError($"No type {typeName}; the saddle will not be pickupable.");
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

        private static void SetFloat(Object target, string field, float value)
        {
            SerializedProperty p = Find(target, field);
            if (p == null) return;
            p.floatValue = value;
            p.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(Object target, string field, int value)
        {
            SerializedProperty p = Find(target, field);
            if (p == null) return;
            p.intValue = value;
            p.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnum(Object target, string field, int value)
        {
            SerializedProperty p = Find(target, field);
            if (p == null) return;
            p.enumValueIndex = value;
            p.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVector2(Object target, string field, Vector2 value)
        {
            SerializedProperty p = Find(target, field);
            if (p == null) return;
            p.vector2Value = value;
            p.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
