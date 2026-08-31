// Builds the seven carryable hull modules from Assets/Game/Art/Models/Items/ShipParts/*.fbx:
//
//   Assets/Game/Prefabs/Items/ShipParts/<Name>.prefab      the module, in the hand and in the sand
//   Assets/Game/Resources/Items/ShipParts/<Name>.asset     the InventoryItem
//
// plus each prefab's entry in the network prefab list the NetworkManager actually reads, and a
// nine-by-nine row per module in PackShapes.asset.
//
// These are ordinary items with two deliberate departures from every other artifact:
//
//   * NO DropItemPhysics. That component exists to freeze a dropped gadget where it lands so a
//     passing creature cannot nudge it across the desert. A hull module is the opposite case —
//     it is meant to be shoved, roped and hauled — and every verb that would haul it (the lasso,
//     the leash, the grapple winch, walking into it) moves a Rigidbody and nothing else. Leaving
//     the body live IS the drag feature; there is no drag system here to write. NetAuthority
//     freezes it on machines that do not simulate it, so the copies cannot fight.
//
//   * A drawn 9x9 pack shape. The rack is the only face on the expedition rig that is nine cells
//     square, so a module authored at nine-by-nine fits the rack, fits it only when it is clear,
//     and fits nowhere else — the "one whole face per module" rule falls out of the mask system
//     with nothing added to PackLayout. Hauling an engine costs you your gear.
//
// True ship scale is kept: the eleven-metre motor lying in the sand is the same mesh, at the same
// size, as the one that ends up bolted to the roof. ItemGrip.holdSize shrinks it for the hand and
// packSize for the mat; neither touches the object in the world.
//
// Re-runnable, and re-running REPLACES every prefab wholesale. Tunables belong in Modules below.
//
// Re-run from: Tools ▸ Items ▸ Build Ship Parts
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public static class ShipPartItemBuilder
    {
        private const string ModelDir = "Assets/Game/Art/Models/Items/ShipParts";
        private const string PrefabDir = "Assets/Game/Prefabs/Items/ShipParts";
        private const string ItemDir = "Assets/Game/Resources/Items/ShipParts";

        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";
        private const string PackShapesPath =
            "Assets/Game/ScriptableObjects/Items/PackShapes.asset";

        /// <summary>
        /// One module fits one socket and is then gone. <c>UsableItem.maxUses</c> defaults to -1,
        /// which means UNLIMITED — leave it and one salvaged motor repairs every hull in the desert.
        /// </summary>
        private const int MaxUses = 1;

        /// <summary>
        /// Metres along the module's longest axis once it is lying on the pack mat.
        ///
        /// <para>
        /// Sized to the rack's 0.81 m face, because that is the face it is going on and no other.
        /// The 9x9 shape below reserves the whole face regardless; this is only what the player
        /// sees strapped there.
        /// </para>
        /// </summary>
        private const float PackSize = 0.80f;

        /// <summary>The rack is 9 x 9 cells at <c>PackGrid.Cell</c>. A module fills it exactly.</summary>
        private const int RackCells = 9;

        /// <summary>Sand, not ice: a shoved module coasts a little and stops.</summary>
        private const float LinearDamping = 1.5f;

        /// <summary>High, so a long module settles onto a flank rather than rolling off down a dune.</summary>
        private const float AngularDamping = 4f;

        /// <summary>
        /// One carryable module.
        ///
        /// <para>
        /// <c>holdSize</c> is a bracket from <see cref="ItemScaleLadder"/>, never a multiple of the
        /// module's real size: 1.00 is the large-tool bracket, 1.25 the bazooka anchor, and 1.40 is
        /// the two-handed haul above it. Scaling an 11 m motor proportionally would put it through
        /// the far wall of every room the player carried it into.
        /// </para>
        /// </summary>
        private readonly struct Module
        {
            public readonly ShipPartKind Kind;
            public readonly string Name;
            public readonly float HoldSize;
            public readonly float Mass;

            public Module(ShipPartKind kind, string name, float holdSize, float mass)
            {
                Kind = kind;
                Name = name;
                HoldSize = holdSize;
                Mass = mass;
            }

            /// <summary>Matches the filename ship_parts_export.py writes: the kind, lowercased.</summary>
            public string ModelPath => $"{ModelDir}/{Kind.ToString().ToLowerInvariant()}.fbx";

            public string PrefabPath => $"{PrefabDir}/{Kind}.prefab";
            public string ItemPath => $"{ItemDir}/{Kind}.asset";
        }

        private static readonly Module[] Modules =
        {
            new(ShipPartKind.AntiGravity,  "Anti-Gravity Spine", 1.40f, 600f),
            new(ShipPartKind.NuclearMotor, "Nuclear Motor",      1.40f, 900f),
            new(ShipPartKind.ReactorCore,  "Reactor Core",       1.25f, 700f),
            new(ShipPartKind.SmallMotor,   "Belly Motor",        1.00f, 300f),
            new(ShipPartKind.AirIntake,    "Air Intake",         1.00f, 200f),
            new(ShipPartKind.LongTurbine,  "Flank Turbine",      1.40f, 800f),
            new(ShipPartKind.Gun,          "Hull Gun",           1.25f, 500f),
        };

        [MenuItem("Tools/Items/Build Ship Parts")]
        public static void Build()
        {
            Directory.CreateDirectory(PrefabDir);
            Directory.CreateDirectory(ItemDir);

            var built = new List<(Module module, GameObject prefab, InventoryItem item)>();

            foreach (Module module in Modules)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(module.ModelPath);
                if (model == null)
                {
                    Debug.LogError($"[ShipParts] No model at {module.ModelPath}. Run " +
                                   "_Source~/models/vehicles/ship_parts_export.py first.");
                    return;
                }

                GameObject root = BuildHierarchy(module, model);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, module.PrefabPath);
                Object.DestroyImmediate(root);
                if (prefab == null)
                {
                    Debug.LogError($"[ShipParts] Prefab save failed for {module.Name}.");
                    return;
                }

                InventoryItem item = EnsureItemAsset(module, prefab);
                WireItemIntoPickup(module, prefab, item);
                RegisterNetworkPrefab(prefab);

                built.Add((module, prefab, item));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // A NetworkObject added by script ships GlobalObjectIdHash 0, and NGO silently drops all
            // but one prefab when several share a hash — with seven of them built in one run, that
            // would leave six modules that can never spawn on a client. The hash is filled in by the
            // component's own OnValidate, which only resolves against the saved ASSET, so each prefab
            // has to be re-imported and then reserialized or the corrected value never reaches the YAML.
            string[] paths = built.Select(b => b.module.PrefabPath).ToArray();
            foreach (string path in paths)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ForceReserializeAssets(paths);
            AssetDatabase.Refresh();

            // After the reserialize, so the rows point at the assets as they finally exist on disk.
            WirePackShapes(built.Select(b => b.item).ToList());

            Core.Persistence.EditorTools.SaveableWiring.WirePrefabs();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!Verify()) return;

            Debug.Log($"[ShipParts] Built {built.Count} module(s) under {PrefabDir} and {ItemDir}. " +
                      "Run Tools/Generate All Item Icons for the inventory icons.");
        }

        // ─────────────────────────── The prefab ───────────────────────────

        private static GameObject BuildHierarchy(Module module, GameObject model)
        {
            var root = new GameObject(module.Kind.ToString());

            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.name = "Model";
            modelInstance.transform.SetParent(root.transform, false);

            // The mesh was centred on its own bounds at export, so the root origin is already the
            // middle of the module — which is what a dynamic body wants for its centre of mass, and
            // where a hand should close on something this size.
            var grip = new GameObject("GripPoint");
            grip.transform.SetParent(root.transform, false);

            // ── Pickup / world presence ──
            NetworkObject netObject = root.AddComponent<NetworkObject>();
            netObject.SynchronizeTransform = true;

            Bounds bounds = ItemBounds.Measure(root, modelInstance.transform);
            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;

            Rigidbody body = root.AddComponent<Rigidbody>();

            // NOT kinematic, unlike every other item prefab. This is the whole of the "modules can
            // be dragged around" feature: the drop path un-kinematics a body and DropItemPhysics
            // would put it straight back: this prefab simply does not have that component.
            body.isKinematic = false;
            body.useGravity = true;
            body.mass = module.Mass;
            body.linearDamping = LinearDamping;
            body.angularDamping = AngularDamping;

            AddByName(root, "SpaceGame.Items.PickupableItem");

            root.AddComponent<NetRelay>();

            // Freezes the body on machines that do not simulate it, so seven live bodies do not
            // fight their own replicas. Defaults suit an unowned, server-simulated prop.
            root.AddComponent<NetAuthority>();

            root.AddComponent<SaveableEntity>();
            root.AddComponent<TransformSaveable>();
            root.AddComponent<RigidbodySaveable>();

            // ── Grip ──
            ItemGrip itemGrip = root.AddComponent<ItemGrip>();
            var gripSo = new SerializedObject(itemGrip);
            SetObject(gripSo, "gripPoint", grip.transform);
            SetFloat(gripSo, "holdSize", module.HoldSize);
            SetFloat(gripSo, "packSize", PackSize);
            SetObject(gripSo, "sizeReference", modelInstance.transform);
            gripSo.ApplyModifiedPropertiesWithoutUndo();

            // ── The module ──
            ShipPartItem part = root.AddComponent<ShipPartItem>();
            var partSo = new SerializedObject(part);
            SetEnum(partSo, "kind", (int)module.Kind);
            SetInt(partSo, "maxUses", MaxUses);
            partSo.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        // ─────────────────────────── The item ───────────────────────────

        private static InventoryItem EnsureItemAsset(Module module, GameObject prefab)
        {
            var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(module.ItemPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(asset, module.ItemPath);
            }

            asset.itemName = module.Name;
            asset.itemPrefab = prefab;

            // InventoryItem.OnValidate is what normally stamps this, and OnValidate does not run
            // for an asset a script creates and writes — so a module built here ships with a null
            // id. That is invisible in the editor and fatal in a BUILT player: RegistryLoader hands
            // every item to Registry.Register, which indexes a dictionary on the id and throws on
            // the first null, leaving the game with no item registry at all. It also silently
            // unhooks the pack shape below, which is keyed on the same id.
            string guid = AssetDatabase.AssetPathToGUID(module.ItemPath);
            if (!string.IsNullOrEmpty(guid) && asset.ID != guid) asset.ID = guid;

            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>
        /// The item asset references the saved prefab and the prefab references the item, so one of
        /// the two links can only be made once both files exist.
        /// </summary>
        private static void WireItemIntoPickup(Module module, GameObject prefab, InventoryItem asset)
        {
            Component pickup = prefab.GetComponents<Component>()
                .FirstOrDefault(c => c != null && c.GetType().FullName == "SpaceGame.Items.PickupableItem");
            if (pickup == null)
            {
                Debug.LogError($"[ShipParts] PickupableItem missing on {module.Name}.");
                return;
            }

            var so = new SerializedObject(pickup);
            so.FindProperty("item").objectReferenceValue = asset;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SavePrefabAsset(prefab);
        }

        /// <summary>
        /// The list NetworkManager actually reads. NOT Assets/DefaultNetworkPrefabs.asset, which
        /// Netcode regenerates and nothing consults. Dropping a hotbar slot routes through
        /// PlayerDropService to GameServices.World.Spawn, which needs the entry — and it fails on
        /// CLIENTS ONLY, so playing as the host can never find it missing.
        /// </summary>
        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null)
            {
                Debug.LogError($"[ShipParts] No list at {NetworkPrefabsPath}.");
                return;
            }

            if (list.Contains(prefab)) return;

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }

        // ─────────────────────────── The pack shape ───────────────────────────

        /// <summary>
        /// Give every module a solid <see cref="RackCells"/>-square row in the shape library.
        ///
        /// <para>
        /// This is the rule "a module can only go on an empty rack", expressed in the one place the
        /// pack already asks about shape. Nine by nine is the rack exactly, so the module fits it
        /// and nothing else fits beside it; every other face on the rig is smaller on at least one
        /// axis, so it fits none of them at any yaw. No new concept reaches PackLayout.
        /// </para>
        /// </summary>
        private static void WirePackShapes(List<InventoryItem> items)
        {
            var library = AssetDatabase.LoadAssetAtPath<PackShapeLibrary>(PackShapesPath);
            if (library == null)
            {
                Debug.LogError($"[ShipParts] No pack shape library at {PackShapesPath} — run " +
                               "Tools/SpaceGame/Items/Create Pack Shape Library first. The modules " +
                               "will otherwise take only the few cells their thin silhouette needs.");
                return;
            }

            var cells = new bool[RackCells * RackCells];
            for (int i = 0; i < cells.Length; i++) cells[i] = true;

            foreach (InventoryItem item in items)
            {
                if (item == null) continue;

                PackShapeLibrary.Entry row = library.Entries.FirstOrDefault(e => e != null && e.item == item);
                if (row == null)
                {
                    row = new PackShapeLibrary.Entry();
                    library.Entries.Add(row);
                }

                row.item = item;
                row.width = RackCells;
                row.height = RackCells;
                row.cells = (bool[])cells.Clone();

                // A square turned a quarter turn is the same square, so rotation is neither
                // forbidden nor useful here — left on so the module behaves like everything else
                // in the hand.
                row.allowRotation = true;
            }

            library.Invalidate();
            EditorUtility.SetDirty(library);
        }

        // ─────────────────────────── Scene placement ───────────────────────────

        private const string TestScenePath = "Assets/Game/Scenes/Tests/Ferdinand_Test_world.unity";

        /// <summary>
        /// How far out from the wreck the modules are scattered, in metres. Far enough that they
        /// are a walk rather than a step — the loop is find, haul, fit — and near enough that the
        /// test world does not become a search.
        /// </summary>
        private const float ScatterRadius = 55f;

        /// <summary>Dropped from above so each module settles onto whatever terrain is under it.</summary>
        private const float DropHeight = 12f;

        /// <summary>
        /// Scatter one of each module around the PlayerShip in the test world.
        ///
        /// <para>
        /// Run AFTER the hash-stamping pass in <see cref="Build"/>, for the reason PlayerShipBuilder
        /// places its ship last: an instance made before the hash is stamped poisons the scene with
        /// a GlobalObjectIdHash of 0.
        /// </para>
        /// </summary>
        [MenuItem("Tools/Items/Place Ship Parts In Test World")]
        public static void PlaceInTestScene()
        {
            // Additive, never Single: this can run while somebody has a scene open, and stealing
            // their scene — or silently discarding its unsaved changes — is worse than any
            // convenience.
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(TestScenePath);
            bool wasOpen = scene.IsValid() && scene.isLoaded;
            if (!wasOpen)
                scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    TestScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);

            GameObject[] roots = scene.GetRootGameObjects();
            GameObject ship = roots.FirstOrDefault(go => go.name == "PlayerShip");
            if (ship == null)
            {
                Debug.LogError("[ShipParts] No PlayerShip in the test world to scatter modules " +
                               "around — run Tools/Vehicles/Build PlayerShip Prefab first.");
                if (!wasOpen)
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                return;
            }

            var placed = new List<string>();

            for (int i = 0; i < Modules.Length; i++)
            {
                Module module = Modules[i];

                if (roots.Any(go => go.name == module.Kind.ToString()))
                    continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(module.PrefabPath);
                if (prefab == null) continue;

                // Evenly around the wreck, so no two modules land on top of each other and the
                // arrangement does not depend on the order the list happens to be in.
                float angle = i * Mathf.PI * 2f / Modules.Length;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ScatterRadius;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.position = ship.transform.position + offset + Vector3.up * DropHeight;
                placed.Add(module.Kind.ToString());
            }

            if (placed.Count == 0)
            {
                Debug.Log("[ShipParts] The test world already holds every module — left as is.");
            }
            else
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                Debug.Log($"[ShipParts] Scattered {placed.Count} module(s) around the PlayerShip: " +
                          string.Join(", ", placed));
            }

            if (wasOpen)
            {
                if (placed.Count > 0)
                    Debug.Log("[ShipParts] The test world is OPEN — save the scene to keep them.");
                return;
            }

            if (placed.Count > 0)
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
        }

        // ─────────────────────────── Proof ───────────────────────────

        /// <summary>
        /// Re-read everything this run wrote, off disk, and assert it landed.
        ///
        /// <para>
        /// Unity's AssetDatabase goes read-only in some sessions and discards prefab and asset
        /// saves outright without raising anything, so a run that reports success having written
        /// nothing is a real outcome rather than a hypothetical one.
        /// </para>
        /// </summary>
        private static bool Verify()
        {
            var problems = new List<string>();
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            var library = AssetDatabase.LoadAssetAtPath<PackShapeLibrary>(PackShapesPath);

            foreach (Module module in Modules)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(module.PrefabPath);
                var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(module.ItemPath);

                if (prefab == null) { problems.Add($"no prefab at {module.PrefabPath}"); continue; }
                if (item == null) { problems.Add($"no item asset at {module.ItemPath}"); continue; }

                var netObject = prefab.GetComponent<NetworkObject>();
                if (netObject == null) problems.Add($"{module.Name}: no NetworkObject");
                else if (netObject.PrefabIdHash == 0) problems.Add($"{module.Name}: GlobalObjectIdHash is 0");

                var part = prefab.GetComponent<ShipPartItem>();
                if (part == null) problems.Add($"{module.Name}: no ShipPartItem");
                else if (part.Kind != module.Kind)
                    problems.Add($"{module.Name}: kind reads {part.Kind}, expected {module.Kind}");

                var body = prefab.GetComponent<Rigidbody>();
                if (body == null) problems.Add($"{module.Name}: no Rigidbody");
                else if (body.isKinematic)
                    problems.Add($"{module.Name}: the body is kinematic, so it cannot be dragged");

                if (prefab.GetComponent<DropItemPhysics>() != null)
                    problems.Add($"{module.Name}: has DropItemPhysics, which would freeze it on landing");

                if (item.itemPrefab != prefab)
                    problems.Add($"{module.Name}: the item asset does not point at the prefab");

                if (string.IsNullOrEmpty(item.ID))
                    problems.Add($"{module.Name}: the item asset has no ID, so it can never be " +
                                 "registered, saved, or given a pack shape");

                if (list == null || !list.Contains(prefab))
                    problems.Add($"{module.Name}: not registered in {NetworkPrefabsPath}");

                PackShapeLibrary.Entry row = library != null ? library.Find(item.ID) : null;
                if (row == null || row.width != RackCells || row.height != RackCells)
                    problems.Add($"{module.Name}: no {RackCells}x{RackCells} pack shape row");
            }

            if (problems.Count == 0) return true;

            Debug.LogError("[ShipParts] Build did not land:\n  " + string.Join("\n  ", problems));
            return false;
        }

        // ─────────────────────────── Serialized-field helpers ───────────────────────────
        //
        // Item components serialize private fields, which is right for runtime code and simply means
        // an editor script goes in the way the Inspector does. PickupableItem is additionally
        // internal to Assembly-CSharp, so it cannot be named from an editor assembly at all.

        private static void AddByName(GameObject go, string fullName)
        {
            System.Type type = typeof(ItemGrip).Assembly.GetType(fullName);
            if (type == null) { Debug.LogError($"[ShipParts] No such component: {fullName}."); return; }

            go.AddComponent(type);
        }

        private static SerializedProperty Find(SerializedObject so, string name)
        {
            SerializedProperty property = so.FindProperty(name);
            if (property == null)
                Debug.LogWarning($"[ShipParts] {so.targetObject.GetType().Name} has no serialized " +
                                 $"field '{name}' — it was renamed; this value is unset.");
            return property;
        }

        private static void SetObject(SerializedObject so, string name, Object value)
        {
            SerializedProperty p = Find(so, name);
            if (p != null) p.objectReferenceValue = value;
        }

        private static void SetFloat(SerializedObject so, string name, float value)
        {
            SerializedProperty p = Find(so, name);
            if (p != null) p.floatValue = value;
        }

        private static void SetInt(SerializedObject so, string name, int value)
        {
            SerializedProperty p = Find(so, name);
            if (p != null) p.intValue = value;
        }

        private static void SetEnum(SerializedObject so, string name, int value)
        {
            SerializedProperty p = Find(so, name);
            if (p != null) p.enumValueIndex = value;
        }
    }
}
