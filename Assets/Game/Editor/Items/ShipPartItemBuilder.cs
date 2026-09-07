// Builds the seven carryable hull modules from Assets/Game/Art/Models/Items/ShipParts/*.fbx:
//
//   Assets/Game/Prefabs/Items/ShipParts/<Name>.prefab      the module, in the hand and in the sand
//   Assets/Game/Resources/Items/ShipParts/<Name>.asset     the InventoryItem
//
// plus each prefab's entry in the network prefab list the NetworkManager actually reads.
//
// These are ordinary items with one deliberate departure from every other artifact:
//
//   * ItemWorldSizing.Authored. Every other item is drawn in the world at the size the gear wall
//     draws it (ItemWorldScale); a hull module is not, because its real size IS the point — see
//     the note on true ship scale below.
//
// True ship scale is kept: the eleven-metre motor lying in the sand is the same mesh, at the same
// size, as the one that ends up bolted to the roof. ItemGrip.holdSize shrinks it for the hand and
// packSize for the mat; neither touches the object in the world.
//
// A module is meant to be shoved, roped and hauled, and every verb that would haul it (the lasso,
// the leash, the grapple winch, walking into it) moves a Rigidbody and nothing else. Leaving the
// body live IS the drag feature; there is no drag system here to write. That used to be a
// departure too, written out by hand here because every other item prefab froze itself on landing.
// It is now what WorldItem does for all of them.
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
        /// The haul ladder: metres along the module's longest axis once it is lying on the pack
        /// mat or strapped to the ship's gear wall — in the HAND's frame, like every other
        /// <c>ItemGrip</c> size, so <c>ItemFootprint</c> multiplies <c>PackScale.Factor</c> in.
        ///
        /// <para>
        /// <b>Brackets, never a multiple of the module's real size</b> — the same rule
        /// <see cref="ItemScaleLadder"/> states for the hand, for a related reason. The family
        /// spans 6.2 to 1 in true length (1.80 m belly turbine to 11.14 m nuclear motor), and no
        /// single ratio survives both ends of that: pin the motor inside the wall's 30 cells and
        /// the two small modules come out smaller than the 0.80 m every module used to share,
        /// which is the complaint this ladder answers. Four rungs compress the range to 2.2 to 1
        /// while keeping the ORDER — a module still reads bigger than every module genuinely
        /// smaller than it, and every one of them reads bigger than it did.
        /// </para>
        /// <para>
        /// <b>0.95 is load-bearing, not a rounded 1.00.</b> The belly turbine is the one module
        /// that is nearly as fat as it is long (1.46 x 1.80 x 1.80), so its cross axis is what
        /// binds: at 1.00 that axis measures 0.852 m and crosses into a TENTH cell, and a shape
        /// ten cells across fits the nine-cell rack at no yaw — the rack allows overhang along u
        /// only (<c>PackOverhang</c>), so the other axis is clamped by nothing. 0.95 keeps it at
        /// nine and keeps every module carryable on the pack. Both halves of that comparison ride
        /// <c>PackScale.Factor</c>, so the fit holds at any value of it.
        /// </para>
        /// </summary>
        private static class Haul
        {
            /// <summary>The nuclear motor alone: 11 m, and the longest thing a player can carry.</summary>
            public const float Spar = 2.10f;

            /// <summary>The 7 m spine and flank turbine.</summary>
            public const float Long = 1.70f;

            /// <summary>The 5 m gun barrel and reactor core.</summary>
            public const float Medium = 1.40f;

            /// <summary>The intake plate and the belly turbine — see the note on 0.95 above.</summary>
            public const float Compact = 0.95f;
        }

        /// <summary>
        /// One carryable module.
        ///
        /// <para>
        /// <c>holdSize</c> is a bracket from <see cref="ItemScaleLadder"/>, never a multiple of the
        /// module's real size: 1.00 is the large-tool bracket, 1.25 the bazooka anchor, and 1.40 is
        /// the two-handed haul above it. Scaling an 11 m motor proportionally would put it through
        /// the far wall of every room the player carried it into.
        /// </para>
        /// <para>
        /// <c>packSize</c> is a rung of <see cref="Haul"/> and is a SEPARATE decision from
        /// <c>holdSize</c>: the hand is sized for feel, the mat for telling one module from
        /// another. Nothing here reaches the module in the world — the family is
        /// <c>ItemWorldSizing.Authored</c>, so <c>ItemWorldScale</c> never resizes it.
        /// </para>
        /// </summary>
        private readonly struct Module
        {
            public readonly ShipPartKind Kind;
            public readonly string Name;
            public readonly float HoldSize;
            public readonly float PackSize;
            public readonly float Mass;

            public Module(ShipPartKind kind, string name, float holdSize, float packSize, float mass)
            {
                Kind = kind;
                Name = name;
                HoldSize = holdSize;
                PackSize = packSize;
                Mass = mass;
            }

            /// <summary>Matches the filename ship_parts_export.py writes: the kind, lowercased.</summary>
            public string ModelPath => $"{ModelDir}/{Kind.ToString().ToLowerInvariant()}.fbx";

            public string PrefabPath => $"{PrefabDir}/{Kind}.prefab";
            public string ItemPath => $"{ItemDir}/{Kind}.asset";
        }

        private static readonly Module[] Modules =
        {
            new(ShipPartKind.AntiGravity,  "Anti-Gravity Spine", 1.40f, Haul.Long,    600f),
            new(ShipPartKind.NuclearMotor, "Nuclear Motor",      1.40f, Haul.Spar,    900f),
            new(ShipPartKind.ReactorCore,  "Reactor Core",       1.25f, Haul.Medium,  700f),
            new(ShipPartKind.SmallMotor,   "Belly Motor",        1.00f, Haul.Compact, 300f),
            new(ShipPartKind.AirIntake,    "Air Intake",         1.00f, Haul.Compact, 200f),
            new(ShipPartKind.LongTurbine,  "Flank Turbine",      1.40f, Haul.Long,    800f),
            new(ShipPartKind.Gun,          "Hull Gun",           1.25f, Haul.Medium,  500f),
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

            // After the reserialize, so the rows named here are the assets as they finally exist
            // on disk.
            ClearPackShapes(built.Select(b => b.item).ToList());

            // Checked: this pass refuses in Play mode, and a builder that ignores the refusal saves
            // prefabs with no prefabId and none of their savers — silently. That is what cost
            // PlayerShip its ability to be restored from a save.
            if (!Core.Persistence.EditorTools.SaveableWiring.TryWirePrefabs())
            {
                Debug.LogError("[ShipParts] Aborting — the save-wiring pass refused to run, so the " +
                               "modules would ship with no prefabId and no savers.");
                return;
            }

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

            AddByName(root, "SpaceGame.Items.PickupableItem");

            root.AddComponent<NetRelay>();

            // The body, the collider fitted to this module's own mesh, and the netcode that lets
            // another machine watch it be hauled. Authored sizing, unlike every other item: the
            // eleven-metre motor lying in the sand has to be the motor that bolts to the roof, so
            // it is the one family ItemWorldScale does not resize. The mass is named because a hull
            // module IS a known weight; everything else lets WorldItem derive one.
            //
            // What this used to be was written out by hand here, and was the only prefab family in
            // the project that got it right - which is exactly why it is shared now.
            ItemWorldPresence.Apply(root, ItemWorldSizing.Authored, module.Mass);

            root.AddComponent<SaveableEntity>();
            root.AddComponent<TransformSaveable>();
            root.AddComponent<RigidbodySaveable>();

            // ── Grip ──
            ItemGrip itemGrip = root.AddComponent<ItemGrip>();
            var gripSo = new SerializedObject(itemGrip);
            SetObject(gripSo, "gripPoint", grip.transform);
            SetFloat(gripSo, "holdSize", module.HoldSize);
            SetFloat(gripSo, "packSize", module.PackSize);
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
        /// Make sure NO module has a row in the shape library, so every one of them is shaped by
        /// its own silhouette.
        ///
        /// <para>
        /// An authored row wins over the derived footprint outright (<c>PackShapes.For</c>), so
        /// while it existed a spar, a plate and a stubby turbine were the same object to the
        /// layout. Each module used to be stamped as a solid nine-by-nine — the rack exactly —
        /// to express "a module can only go on an empty rack". It bought that rule at the price
        /// of the module being unreadable on the mat and on the ship's gear wall: seven identical
        /// squares, all drawn at one size, telling the player nothing about which is which.
        /// </para>
        /// <para>
        /// Deleted rather than resized, because the cost of hauling survives without it. A
        /// module's derived rectangle is now genuinely large (the nuclear motor is 4 x 24 cells,
        /// the belly turbine 9 x 11 against a 9 x 9 rack), and the rack's own overhang rule —
        /// which applies to RECTANGLES ONLY, and so was unreachable while the mask was there —
        /// is what lashes a long one across the pack, occupying every cell of every column it
        /// crosses. Hauling an engine still costs most of your gear; a gun barrel now costs what
        /// a gun barrel takes up, which is the honest answer and the readable one.
        /// </para>
        /// <para>
        /// A REMOVAL and not merely a no-op: this builder is re-runnable, the rows are on disk
        /// from earlier runs, and a build that simply stopped writing them would leave every one
        /// of them in place and change nothing.
        /// </para>
        /// </summary>
        private static void ClearPackShapes(List<InventoryItem> items)
        {
            var library = AssetDatabase.LoadAssetAtPath<PackShapeLibrary>(PackShapesPath);
            if (library == null) return;

            int removed = library.Entries.RemoveAll(
                e => e != null && e.item != null && items.Contains(e.item));
            if (removed == 0) return;

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

                foreach (string problem in ItemWorldPresence.ProblemsWith(prefab))
                    problems.Add($"{module.Name}: {problem}");

                var world = prefab.GetComponent<WorldItem>();
                if (world != null && !world.KeepsAuthoredSize)
                    problems.Add($"{module.Name}: sized from its grip, so the module in the sand is " +
                                 "not the module that bolts to the roof");

                if (item.itemPrefab != prefab)
                    problems.Add($"{module.Name}: the item asset does not point at the prefab");

                if (string.IsNullOrEmpty(item.ID))
                    problems.Add($"{module.Name}: the item asset has no ID, so it can never be " +
                                 "registered or saved");

                if (list == null || !list.Contains(prefab))
                    problems.Add($"{module.Name}: not registered in {NetworkPrefabsPath}");

                var grip = prefab.GetComponent<ItemGrip>();
                if (grip == null) problems.Add($"{module.Name}: no ItemGrip");
                else if (!Mathf.Approximately(grip.PackSize, module.PackSize))
                    problems.Add($"{module.Name}: packSize reads {grip.PackSize}, expected " +
                                 $"{module.PackSize} — the module is drawn at the wrong size on " +
                                 "the mat and on the ship's gear wall");

                // An authored row would override the derived silhouette and make this module the
                // same rectangle as every other one. See ClearPackShapes.
                if (library != null && library.Find(item.ID) != null)
                    problems.Add($"{module.Name}: still has a row in {PackShapesPath}, so its " +
                                 "shape is that row rather than its own outline");
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
