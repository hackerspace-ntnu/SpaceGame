// Builds the three carried supply units the oxygen plant deals in:
//
//   Prefabs/Items/Supplies/OxygenTank.prefab       a filled bottle
//   Prefabs/Items/Supplies/OxygenTankEmpty.prefab  the same bottle, gauge dark
//   Prefabs/Items/Supplies/PowerCell.prefab        the slab battery the plant runs on
//
// plus one InventoryItem each under Resources/Items/Supplies and their entries in the network
// prefab list the NetworkManager actually reads.
//
// TWO bottle prefabs for one model, on purpose. A bottle's charge is its IDENTITY here rather than
// a number in its ItemState bag: the hotbar replicates item IDs and ItemState does not replicate at
// all, so a charge kept in a bag would be a value only the server could ever see. Two items means
// the state reaches the wire, the save file, the hotbar, the pack mat and the icon for free — see
// DockableSupply and Oxygen.md.
//
// A script rather than hand-authored YAML because these prefabs nest an imported FBX, and the file
// ids Unity assigns inside a model are decided at import time — a hand-written prefab referencing
// guessed ids loads with a missing model and no error.
//
// Re-runnable, and re-running REPLACES each prefab wholesale. Every tunable therefore belongs in
// the table below rather than in the Inspector, or the next run quietly undoes it.
//
// Re-run from: Tools > SpaceGame > Items > Build Oxygen Gear
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public static class OxygenGearBuilder
    {
        private const string TankModel = "Assets/Game/Art/Models/Props/oxygen_tank.fbx";
        private const string CellModel = "Assets/Game/Art/Models/Props/power_cell.fbx";

        private const string PrefabFolder = "Assets/Game/Prefabs/Items/Supplies/";
        private const string AssetFolder = "Assets/Game/Resources/Items/Supplies/";

        public const string ChargedTankAsset = AssetFolder + "OxygenTank.asset";
        public const string DrainedTankAsset = AssetFolder + "OxygenTankEmpty.asset";
        public const string PowerCellAsset = AssetFolder + "PowerCell.asset";

        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        /// <summary>
        /// The drained bottle's gauge, as a MATERIAL rather than as a runtime tint.
        ///
        /// <para>
        /// A <c>MaterialPropertyBlock</c> is not serialized and needs <c>Awake</c>, which never runs
        /// on a prefab in the editor — so the empty bottle and the full one rendered the same icon,
        /// and, worse, looked identical lying beside each other on the pack mat and on the ship's
        /// gear wall, where <c>DisplayCopy.Strip</c> takes the component off entirely. Whatever
        /// tells the two apart has to be on the prefab, not in a script.
        /// </para>
        /// <para>
        /// Built as a COPY of the model's own gauge material, so it inherits that material's shader
        /// and every property the palette gave it, and only the colours move. Rewritten on every
        /// run: a <c>.mat</c> otherwise freezes the shader defaults it was born with.
        /// </para>
        /// </summary>
        private const string DarkGaugePath =
            "Assets/Game/Art/Materials/Items/Mat_OxygenTank_Gauge_Dark.mat";

        /// <summary>
        /// The two containers whose authored starting contents are where these items enter the
        /// game. Both builders read their starting lists off the existing prefab and write them
        /// back, so stocking them here survives a rebuild of either.
        /// </summary>
        private const string GearWallPrefab = "Assets/Game/Prefabs/Items/Equipment/InventoryWall.prefab";
        private const string ExpeditionRigPrefab = "Assets/Game/Prefabs/Items/Equipment/ExpeditionRig.prefab";

        /// <summary>
        /// Metres along the longest axis once held — the <c>BigTool</c> bracket of
        /// <see cref="ItemScaleLadder"/>.
        ///
        /// <para>
        /// Not the size the models were built at. Both objects are true-scale hardware (a 0.54 m
        /// bottle, a 0.55 m brick) and this hand is roughly 1.7x a human's, so life size reads as a
        /// toy in it. 0.90 is two thirds of the Dragon Bazooka anchor: bulky enough to read as a
        /// two-handed object and short of the guns, which is the silhouette the bracket buys.
        /// </para>
        /// </summary>
        private const float HoldSize = 0.90f;

        /// <summary>
        /// The bottle's size on the pack mat, in the frame <c>packSize</c> is authored in.
        ///
        /// <para>
        /// <b>Not the roster's usual "true size rounded up to the next pitch plus a cell".</b> That
        /// rule gave 0.72, which is right for an item standing on its base and wrong for this one:
        /// the bottle LIES DOWN now (see <see cref="Supply.Lay"/>), so its length is part of its
        /// FOOTPRINT rather than sticking up out of it, and 0.72 laid down is 4 x 8 = 32 cells —
        /// half the leaf, and more than either back panel can hold.
        /// </para>
        /// <para>
        /// 0.50 draws it 0.525 m long, which is life size to within 3%, and lands on <b>3 x 6 = 18
        /// cells</b>: exactly a back panel, and comfortably inside the leaf, the rack and both
        /// wings. The two cell counts come out at 2.53 and 5.56 of a cell — neither near an integer,
        /// which is the trap the power cell's own size note describes.
        /// </para>
        /// </summary>
        private const float TankPackSize = 0.50f;

        /// <summary>
        /// The cell's size on the mat: its true 0.55 m rounded up to the next pitch, and the extra
        /// cell of margin deliberately NOT taken.
        ///
        /// <para>
        /// At 0.72 the slab measures exactly the leaf's eight cells across, and eight cells is a
        /// float division landing exactly on an integer — which rounds either way and so decides at
        /// random whether the item fits the leaf at all. 0.63 is 7 x 3 = 21 cells with a column to
        /// spare. See Oxygen.md's Gotchas.
        /// </para>
        /// </summary>
        private const float CellPackSize = 0.63f;

        /// <summary>Gauge colour at full. The palette's own CRT green, read off the model.</summary>
        private static readonly Color GaugeFull = new Color(0.36f, 0.95f, 0.45f);

        /// <summary>
        /// Gauge colour at empty. Dark glass rather than black: a black gauge reads as a hole
        /// punched in the bottle instead of as an instrument that is off.
        /// </summary>
        private static readonly Color GaugeEmpty = new Color(0.05f, 0.08f, 0.06f);

        /// <summary>One supply unit: which model, which files, and how it reads.</summary>
        private readonly struct Supply
        {
            public readonly string Model;
            public readonly string Prefab;
            public readonly string Asset;
            public readonly string Name;
            public readonly bool Charged;

            /// <summary>The mesh carrying the emissive gauge, or null for one that never changes.</summary>
            public readonly string ReadoutPart;

            /// <summary>Which submesh of that mesh is the emissive material.</summary>
            public readonly int ReadoutIndex;

            public readonly float PackSize;

            /// <summary>
            /// Euler degrees the MODEL is turned by inside the prefab, or zero for one that is
            /// already lying the way it should.
            ///
            /// <para>
            /// This is the only thing that decides how an item lies on a surface, because
            /// <c>BackpackItemVisual</c> seats a copy with the ITEM's own up along the surface
            /// NORMAL and never turns it over: a bottle modelled standing on its skirt stands off
            /// a vertical back panel by its whole length. Turning the geometry is the fix, and the
            /// grip's <c>rotationOffset</c> is given the inverse so the pose in the HAND does not
            /// move — the rule <c>ItemPackOrientation</c> exists to apply, put in the builder
            /// instead because a builder-owned prefab is replaced wholesale on the next run.
            /// </para>
            /// </summary>
            public readonly Vector3 Lay;

            public Supply(string model, string file, string name, bool charged,
                          string readoutPart, int readoutIndex, float packSize, Vector3 lay)
            {
                Lay = lay;
                Model = model;
                Prefab = PrefabFolder + file + ".prefab";
                Asset = AssetFolder + file + ".asset";
                Name = name;
                Charged = charged;
                ReadoutPart = readoutPart;
                ReadoutIndex = readoutIndex;
                PackSize = packSize;
            }
        }

        /// <summary>
        /// Submesh 0 of the bottle's five-material gauge mesh is its <c>Mat_Emissive_Green_CRT</c>.
        /// The index matters: the same mesh also carries the orange collar and two metals, so
        /// painting the whole renderer would enamel the bottle.
        /// </summary>
        private const string TankGauge = "Mesh_OxygenTank_Gauge";
        private const int TankGaugeIndex = 0;

        /// <summary>
        /// A quarter turn back about X, which lays the bottle on its side and points its GAUGE
        /// along the item's own +Y — the axis every surface seats along its normal, so the gauge
        /// ends up facing out of whatever the bottle is lying on and is readable there.
        ///
        /// <para>
        /// The sign is the half that is easy to get wrong, so it is derived rather than guessed:
        /// Unity's X rotation carries +Z to (0, sin, cos), so at -90 the model's +Z — its gauge
        /// flank, measured at z +0.0975 on the barrel — lands on +Y, and at +90 it lands on -Y,
        /// buried in the surface. <c>OxygenSystemTests.TheBottleLiesDownWithItsGaugeOutward</c>
        /// measures it off the built prefab rather than trusting this note.
        /// </para>
        /// </summary>
        private static readonly Vector3 BottleLiesDown = new Vector3(-90f, 0f, 0f);

        private static readonly Supply[] Roster =
        {
            new(TankModel, "OxygenTank", "Oxygen Tank", true, TankGauge, TankGaugeIndex,
                TankPackSize, BottleLiesDown),
            new(TankModel, "OxygenTankEmpty", "Oxygen Tank (Empty)", false,
                TankGauge, TankGaugeIndex, TankPackSize, BottleLiesDown),

            // No readout. The cell's charge ladder is five bars with three lit, authored in the
            // model — and the cell never drains, so there is nothing for the game to drive. Wiring
            // it would be a gauge that can only ever be repainted the colour it already is.
            new(CellModel, "PowerCell", "Power Cell", true, null, 0, CellPackSize, Vector3.zero),
        };

        [MenuItem("Tools/SpaceGame/Items/Build Oxygen Gear")]
        public static void Build()
        {
            var built = new List<GameObject>();

            foreach (Supply supply in Roster)
            {
                GameObject prefab = BuildOne(supply);
                if (prefab == null) return;
                built.Add(prefab);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // A NetworkObject added by script ships GlobalObjectIdHash 0, and NGO silently drops
            // all but one prefab when several share a hash. The hash is filled in by the
            // component's own OnValidate, which only resolves against the saved ASSET — so each
            // prefab has to be re-imported and reserialized or the corrected value, and the
            // SaveableEntity's prefabId beside it, never reach the YAML.
            string[] paths = Roster.Select(s => s.Prefab).ToArray();
            foreach (string path in paths)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ForceReserializeAssets(paths);
            AssetDatabase.Refresh();

            RouteIntoTheGame();

            if (!Verify()) return;

            Debug.Log("[OxygenGear] Built " + built.Count + " supply items under " + PrefabFolder +
                      " and " + AssetFolder + ". Run Tools/Generate All Item Icons for the " +
                      "inventory icons, then Tools/SpaceGame/Build Oxygen Generator Prefab.");
        }

        // ─────────────────────────── One item ───────────────────────────

        private static GameObject BuildOne(Supply supply)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(supply.Model);
            if (model == null)
            {
                Debug.LogError("[OxygenGear] No model at " + supply.Model +
                               " — run the _Source~ export for it first.");
                return null;
            }

            GameObject root = BuildHierarchy(model, supply);
            if (root == null) return null;

            Directory.CreateDirectory(Path.GetDirectoryName(supply.Prefab) ?? ".");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, supply.Prefab);
            Object.DestroyImmediate(root);

            if (prefab == null)
            {
                Debug.LogError("[OxygenGear] Prefab save failed for " + supply.Prefab +
                               " — is this a read-only editor clone?");
                return null;
            }

            InventoryItem asset = EnsureItemAsset(supply, prefab);
            WireItemIntoPickup(prefab, asset);
            RegisterNetworkPrefab(prefab);

            return prefab;
        }

        private static GameObject BuildHierarchy(GameObject model, Supply supply)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(supply.Prefab));

            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.name = "Model";
            modelInstance.transform.SetParent(root.transform, false);

            // Before anything is measured: the grip point, the footprint and the fitted collider
            // are all read off the ROOT's frame, so a turn applied after them would leave every
            // one describing a pose the item no longer has.
            modelInstance.transform.localRotation = Quaternion.Euler(supply.Lay);

            Renderer readout = null;
            if (supply.ReadoutPart != null)
            {
                readout = FindReadout(modelInstance, supply);
                if (readout == null)
                {
                    Object.DestroyImmediate(root);
                    return null;
                }

                // The one difference between the two bottles that survives an icon render and a
                // stripped display copy.
                if (!supply.Charged) DarkenGauge(readout, supply.ReadoutIndex);
            }

            // ── Pickup / world presence ──
            // One prefab is both the thing in the hand and the thing lying in the sand, so it
            // carries both sets of components, component for component with the other items.
            NetworkObject netObject = root.AddComponent<NetworkObject>();
            netObject.SynchronizeTransform = true;

            AddByName(root, "SpaceGame.Items.PickupableItem");

            // The body, a collider the shape of the item, the world sizing and the netcode that
            // lets another machine watch it be shoved about. One shared block — see
            // ItemWorldPresence for what nine hand-written copies of it cost.
            ItemWorldPresence.Apply(root);

            root.AddComponent<NetRelay>();
            root.AddComponent<SaveableEntity>();
            root.AddComponent<TransformSaveable>();

            // ── Grip ──
            // The hand closes on the middle of the object rather than on its origin, which for both
            // of these models is a corner of the base. Measured, not typed, so a remodel follows.
            Transform gripPoint = AddGripPoint(root, modelInstance);

            ItemGrip grip = root.AddComponent<ItemGrip>();
            var gripSo = new SerializedObject(grip);
            Field.Set(gripSo, "gripPoint", gripPoint);
            Field.SetFloat(gripSo, "holdSize", HoldSize);
            Field.SetFloat(gripSo, "packSize", supply.PackSize);
            Field.Set(gripSo, "sizeReference", modelInstance.transform);

            // The exact inverse of the turn above. EquipItemSocket seats an item as
            // `handRotation * Euler(rotationOffset)`, so turning the contents by R and the offset
            // by R-inverse multiplies back out: the mat gets the new lie and the HAND keeps the
            // pose it was tuned with.
            Field.SetVector3(gripSo, "rotationOffset",
                             Quaternion.Inverse(Quaternion.Euler(supply.Lay)).eulerAngles);

            // Both are hugged rather than gripped: a pressure bottle with no handle (its wire bail
            // was cut in the model) and a two-handed brick.
            Field.SetEnum(gripSo, "holdStyle", (int)ItemGrip.HoldStyle.TwoHanded);
            gripSo.ApplyModifiedPropertiesWithoutUndo();

            // ── The item's own behaviour ──
            DockableSupply supplyItem = root.AddComponent<DockableSupply>();
            var supplySo = new SerializedObject(supplyItem);
            Field.SetBool(supplySo, "charged", supply.Charged);
            Field.Set(supplySo, "readout", readout);
            Field.SetInt(supplySo, "readoutMaterialIndex",
                         readout != null ? supply.ReadoutIndex : EmissiveLamp.WholeRenderer);
            Field.SetColor(supplySo, "chargedColour", GaugeFull);
            Field.SetColor(supplySo, "emptyColour", GaugeEmpty);
            supplySo.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        /// <summary>
        /// A child at the measured middle of the model, which is what lands in the palm.
        ///
        /// A child of the ROOT rather than of the model, so the wiring points at a transform this
        /// prefab owns and a re-import of the FBX cannot null it.
        /// </summary>
        private static Transform AddGripPoint(GameObject root, GameObject modelInstance)
        {
            Bounds bounds = ItemBounds.Measure(root, modelInstance.transform);

            var point = new GameObject("GripPoint").transform;
            point.SetParent(root.transform, false);
            point.localPosition = bounds.center;
            return point;
        }

        /// <summary>
        /// Swap the drained bottle's gauge submesh onto a dark copy of the model's own gauge
        /// material — see <see cref="DarkGaugePath"/> for why this cannot be a runtime tint.
        ///
        /// <para>
        /// Only the submesh at <paramref name="index"/> moves. These meshes are one per PART and up
        /// to five materials deep, so replacing the whole array would enamel the orange collar and
        /// both metals along with the readout.
        /// </para>
        /// </summary>
        private static void DarkenGauge(Renderer readout, int index)
        {
            Material[] materials = readout.sharedMaterials;
            Material source = materials[index];

            var dark = AssetDatabase.LoadAssetAtPath<Material>(DarkGaugePath);
            if (dark == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DarkGaugePath) ?? ".");
                dark = new Material(source);
                AssetDatabase.CreateAsset(dark, DarkGaugePath);
            }

            // Restated every run rather than trusted: a .mat freezes the shader defaults it was
            // born with, and this one is meant to be the model's gauge with the light off.
            dark.shader = source.shader;
            dark.CopyPropertiesFromMaterial(source);
            dark.name = Path.GetFileNameWithoutExtension(DarkGaugePath);
            dark.SetColor("_BaseColor", GaugeEmpty);
            dark.SetColor("_Color", GaugeEmpty);
            dark.SetColor("_EmissionColor", GaugeEmpty * EmissiveLamp.EmissionGain);
            EditorUtility.SetDirty(dark);

            materials[index] = dark;
            readout.sharedMaterials = materials;
        }

        /// <summary>
        /// The emissive gauge, and proof that the submesh index still names an emissive material.
        /// An index that has drifted paints the shell instead, which looks like a broken shader
        /// rather than like a wrong number.
        /// </summary>
        private static Renderer FindReadout(GameObject modelInstance, Supply supply)
        {
            Renderer found = modelInstance.GetComponentsInChildren<Renderer>(true)
                .FirstOrDefault(r => r.name == supply.ReadoutPart);

            if (found == null)
            {
                Debug.LogError("[OxygenGear] No '" + supply.ReadoutPart + "' in " + supply.Model +
                               " — the model script names the parts this builder wires; re-export, " +
                               "or update both.");
                return null;
            }

            Material[] materials = found.sharedMaterials;
            if (supply.ReadoutIndex < 0 || supply.ReadoutIndex >= materials.Length)
            {
                Debug.LogError("[OxygenGear] " + supply.ReadoutPart + " has " + materials.Length +
                               " materials, so submesh " + supply.ReadoutIndex + " does not exist.");
                return null;
            }

            Material material = materials[supply.ReadoutIndex];
            if (material == null || !material.name.Contains("Emissive"))
            {
                Debug.LogError("[OxygenGear] Submesh " + supply.ReadoutIndex + " of " +
                               supply.ReadoutPart + " is '" +
                               (material != null ? material.name : "null") +
                               "', which is not an emissive material. The model's material order " +
                               "changed; re-read it before trusting the index.");
                return null;
            }

            return found;
        }

        // ─────────────────────────── The item asset ───────────────────────────

        private static InventoryItem EnsureItemAsset(Supply supply, GameObject prefab)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(supply.Asset) ?? ".");

            var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(supply.Asset);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(asset, supply.Asset);
            }

            asset.itemName = supply.Name;
            asset.itemPrefab = prefab;
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>
        /// The item asset references the saved prefab and the prefab references the item, so one of
        /// the two links can only be made once both files exist.
        /// </summary>
        private static void WireItemIntoPickup(GameObject prefab, InventoryItem asset)
        {
            Component pickup = prefab.GetComponents<Component>()
                .FirstOrDefault(c => c != null &&
                                     c.GetType().FullName == "SpaceGame.Items.PickupableItem");

            if (pickup == null)
            {
                Debug.LogError("[OxygenGear] PickupableItem missing on " + prefab.name + ".");
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
                Debug.LogError("[OxygenGear] No list at " + NetworkPrefabsPath + ".");
                return;
            }

            if (list.Contains(prefab)) return;

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }

        // ─────────────────────────── Into the game ───────────────────────────

        /// <summary>
        /// Stock the two containers the player actually starts from.
        ///
        /// <para>
        /// This is the whole route in. Without it the plant is unreachable — a machine that needs a
        /// cell nobody can obtain — and these two are the diegetic answer: the ship's gear wall
        /// holds the stores (a spare bottle and the cell, three metres from the plant that eats
        /// them), and the expedition rig carries the bottle you set out with.
        /// </para>
        /// <para>
        /// The rig's is not a nicety. Until 2026-09-03 an oxygen bottle was MODELLED INTO the rig —
        /// <c>Mesh_Rig_OxygenTank</c>, authored as "a fixed fitting, not an item" — so the pack
        /// showed a bottle the player could see and could never take off. That geometry is deleted;
        /// this is what puts a real one back, and a real one lifts off the mat and goes on again.
        /// </para>
        /// <para>
        /// The rig gets the CHARGED bottle (it replaces one that always read as full) and the wall
        /// the drained one, so the plant has something to do on the first visit.
        /// </para>
        /// </summary>
        private static void RouteIntoTheGame()
        {
            var charged = AssetDatabase.LoadAssetAtPath<InventoryItem>(ChargedTankAsset);
            var drained = AssetDatabase.LoadAssetAtPath<InventoryItem>(DrainedTankAsset);
            var cell = AssetDatabase.LoadAssetAtPath<InventoryItem>(PowerCellAsset);
            if (charged == null || drained == null || cell == null) return;

            Stock(GearWallPrefab, "the ship's gear wall", drained, cell);
            Stock(ExpeditionRigPrefab, "the expedition rig", charged);
        }

        /// <summary>
        /// Add items to one <see cref="PackContainer"/>'s authored starting list, skipping any it
        /// already holds. Idempotent, because this runs on every build of the roster.
        /// </summary>
        private static void Stock(string prefabPath, string what, params InventoryItem[] items)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var container = prefab != null ? prefab.GetComponent<PackContainer>() : null;

            if (container == null)
            {
                Debug.LogWarning("[OxygenGear] No PackContainer at " + prefabPath +
                                 ", so the new items are in the registry but nowhere in the world.");
                return;
            }

            var so = new SerializedObject(container);
            SerializedProperty list = so.FindProperty("startingMainItems");
            if (list == null || !list.isArray)
            {
                Debug.LogWarning("[OxygenGear] " + what + " has no startingMainItems list.");
                return;
            }

            var added = new List<string>();
            foreach (InventoryItem item in items)
            {
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == item) present = true;

                if (present) continue;

                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = item;
                added.Add(item.itemName);
            }

            if (added.Count == 0) return;

            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(prefab);
            Debug.Log("[OxygenGear] Stocked " + string.Join(", ", added) + " on " + what + ".");
        }

        // ─────────────────────────── Proof ───────────────────────────

        /// <summary>
        /// Re-read everything this run wrote, off disk, and assert it landed. Unity's AssetDatabase
        /// goes read-only in some sessions and discards prefab and asset saves outright without
        /// raising anything, so a run that reports success having written nothing is a real outcome
        /// rather than a hypothetical one.
        /// </summary>
        private static bool Verify()
        {
            var problems = new List<string>();
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);

            foreach (Supply supply in Roster)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(supply.Prefab);
                var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(supply.Asset);

                if (prefab == null) { problems.Add("no prefab at " + supply.Prefab); continue; }
                if (asset == null) problems.Add("no item asset at " + supply.Asset);
                else if (asset.itemPrefab != prefab)
                    problems.Add(supply.Asset + " does not point at its prefab");

                var netObject = prefab.GetComponent<NetworkObject>();
                if (netObject == null) problems.Add(supply.Name + " has no NetworkObject");
                else if (netObject.PrefabIdHash == 0)
                    problems.Add(supply.Name + " has GlobalObjectIdHash 0");

                var entity = prefab.GetComponent<SaveableEntity>();
                if (entity == null) problems.Add(supply.Name + " has no SaveableEntity");
                else if (string.IsNullOrEmpty(entity.PrefabId))
                    problems.Add(supply.Name + " has no stamped prefabId — run " +
                                 "Tools/Save System/Wire Saveable Prefabs, stopped");

                var grip = prefab.GetComponent<ItemGrip>();
                if (grip == null) problems.Add(supply.Name + " has no ItemGrip");
                else
                {
                    if (!Mathf.Approximately(grip.HoldSize, HoldSize))
                        problems.Add(supply.Name + " holdSize reads " + grip.HoldSize.ToString("F3"));
                    if (!Mathf.Approximately(grip.PackSize, supply.PackSize))
                        problems.Add(supply.Name + " packSize reads " + grip.PackSize.ToString("F3"));
                }

                var supplyItem = prefab.GetComponent<DockableSupply>();
                if (supplyItem == null) problems.Add(supply.Name + " has no DockableSupply");
                else if (supplyItem.Charged != supply.Charged)
                    problems.Add(supply.Name + " has the wrong charged flag");
                else if (supply.ReadoutPart != null && !supply.Charged)
                {
                    // The drained bottle has to be TOLD APART from the full one by something the
                    // prefab carries, or the icon, the pack mat and the gear wall all draw the two
                    // identically — a runtime tint reaches none of them.
                    Renderer gauge = supplyItem.Readout;
                    Material fitted = gauge != null && supply.ReadoutIndex < gauge.sharedMaterials.Length
                        ? gauge.sharedMaterials[supply.ReadoutIndex]
                        : null;

                    if (fitted == null || AssetDatabase.GetAssetPath(fitted) != DarkGaugePath)
                        problems.Add(supply.Name + "'s gauge is not on " + DarkGaugePath +
                                     ", so it is drawn exactly like a full bottle");
                }

                problems.AddRange(ItemWorldPresence.ProblemsWith(prefab)
                                                   .Select(p => supply.Name + ": " + p));

                if (list == null || !list.Contains(prefab))
                    problems.Add(supply.Name + " is not in " + NetworkPrefabsPath);
            }

            if (problems.Count == 0)
            {
                Debug.Log("[OxygenGear] VERIFIED off disk: three items, holdSize " +
                          HoldSize.ToString("F2") + ", packSize " + TankPackSize.ToString("F2") +
                          "/" + CellPackSize.ToString("F2") + ", all registered for clients.");
                return true;
            }

            Debug.LogError("[OxygenGear] NOT VERIFIED:\n  " + string.Join("\n  ", problems));
            return false;
        }

        // ─────────────────────────── Shared ───────────────────────────

        /// <summary>
        /// PickupableItem is internal to Assembly-CSharp, so it cannot be named from an editor
        /// assembly at all.
        /// </summary>
        private static void AddByName(GameObject go, string fullName)
        {
            System.Type type = typeof(ItemGrip).Assembly.GetType(fullName);
            if (type == null)
            {
                Debug.LogError("[OxygenGear] No such component: " + fullName + ".");
                return;
            }

            go.AddComponent(type);
        }

        /// <summary>
        /// Private [SerializeField] fields are not reachable from an editor script any other way,
        /// and widening the runtime API for a build-time convenience would be the wrong trade. A
        /// missing name warns loudly rather than silently doing nothing.
        /// </summary>
        private static class Field
        {
            public static void Set(SerializedObject so, string name, Object value)
            {
                SerializedProperty p = Find(so, name);
                if (p != null) p.objectReferenceValue = value;
            }

            public static void SetFloat(SerializedObject so, string name, float value)
            {
                SerializedProperty p = Find(so, name);
                if (p != null) p.floatValue = value;
            }

            public static void SetInt(SerializedObject so, string name, int value)
            {
                SerializedProperty p = Find(so, name);
                if (p != null) p.intValue = value;
            }

            public static void SetBool(SerializedObject so, string name, bool value)
            {
                SerializedProperty p = Find(so, name);
                if (p != null) p.boolValue = value;
            }

            public static void SetEnum(SerializedObject so, string name, int value)
            {
                SerializedProperty p = Find(so, name);
                if (p != null) p.enumValueIndex = value;
            }

            public static void SetVector3(SerializedObject so, string name, Vector3 value)
            {
                SerializedProperty p = Find(so, name);
                if (p != null) p.vector3Value = value;
            }

            public static void SetColor(SerializedObject so, string name, Color value)
            {
                SerializedProperty p = Find(so, name);
                if (p != null) p.colorValue = value;
            }

            private static SerializedProperty Find(SerializedObject so, string name)
            {
                SerializedProperty p = so.FindProperty(name);
                if (p == null)
                    Debug.LogWarning("[OxygenGear] " + so.targetObject.GetType().Name +
                                     " has no serialized field '" + name +
                                     "' — it was renamed; this value is unset.");
                return p;
            }
        }
    }
}
