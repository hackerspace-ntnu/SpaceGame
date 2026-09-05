// Builds the two carried supply units the oxygen plant deals in:
//
//   Prefabs/Items/Supplies/OxygenTank.prefab  the tank, at any fill level
//   Prefabs/Items/Supplies/Battery.prefab     the slab battery the plant runs on
//
// plus one InventoryItem each under Resources/Items/Supplies and their entries in the network
// prefab list the NetworkManager actually reads.
//
// ONE tank prefab, not two. Until 2026-09-04 a tank's charge was its IDENTITY -- OxygenTank and
// OxygenTankEmpty were separate assets -- because ItemState does not replicate and an id does.
// A tank the player reads to a percent cannot work that way (a hundred assets for a hundred
// readings, and a hundred more per tank type), so the charge is a fraction on the instance and
// SupplyCharge carries it through every container. See DockableSupply and Oxygen.md.
//
// PowerCell was renamed to Battery in the same pass, by MoveAsset rather than by writing a new
// file: a move PRESERVES the GUID, and an InventoryItem's ID is its GUID, so every existing save
// file and every prefab reference keeps resolving. Creating Battery.asset fresh would have made a
// second item and orphaned every cell already in a player's world.
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

        public const string TankAsset = AssetFolder + "OxygenTank.asset";
        public const string BatteryAsset = AssetFolder + "Battery.asset";

        /// <summary>
        /// What the battery's item asset and prefab used to be called. Renamed rather than
        /// replaced, so their GUIDs -- and therefore the item's ID in every save file -- survive.
        /// </summary>
        private const string LegacyCellAsset = AssetFolder + "PowerCell.asset";
        private const string LegacyCellPrefab = PrefabFolder + "PowerCell.prefab";

        /// <summary>
        /// The drained tank, merged into <see cref="TankAsset"/> on 2026-09-04.
        ///
        /// <para>
        /// DELETED rather than renamed, because unlike the cell it has no successor to be renamed
        /// INTO: the surviving tank asset already exists with its own GUID. A world saved with an
        /// empty tank in it therefore names an item this build cannot resolve, and the pack's own
        /// restore drops it with a warning naming the id. That is a real, one-time loss of one item
        /// per affected save, accepted because the alternative -- keeping a second tank asset alive
        /// as an alias -- is a permanent second way for a tank to exist.
        /// </para>
        /// </summary>
        private const string MergedEmptyTankAsset = AssetFolder + "OxygenTankEmpty.asset";
        private const string MergedEmptyTankPrefab = PrefabFolder + "OxygenTankEmpty.prefab";

        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

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

            /// <summary>What this holds, which is what decides the receptacle that takes it.</summary>
            public readonly SupplyKind Kind;

            /// <summary>A full one, in the kind's own unit: SECONDS of air, or WATT-HOURS.</summary>
            public readonly float Capacity;

            /// <summary>How full one enters the world, 0..1.</summary>
            public readonly float StartingCharge;

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

            public Supply(string model, string file, string name, SupplyKind kind, float capacity,
                          float startingCharge, string readoutPart, int readoutIndex,
                          float packSize, Vector3 lay)
            {
                Lay = lay;
                Model = model;
                Prefab = PrefabFolder + file + ".prefab";
                Asset = AssetFolder + file + ".asset";
                Name = name;
                Kind = kind;
                Capacity = capacity;
                StartingCharge = startingCharge;
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

        /// <summary>
        /// Seconds of air a full tank holds: thirty minutes. The one number that decides whether
        /// the open world reads as a journey or as a stopwatch, and the only one to move when it
        /// reads wrong -- SuitOxygen's drain is fixed at one second per second so that a capacity
        /// IS a duration.
        /// </summary>
        private const float TankSeconds = 30f * 60f;

        /// <summary>Watt-hours a full battery holds. Twenty-five tank fills at the plant's 4%.</summary>
        private const float BatteryWattHours = 1000f;

        /// <summary>
        /// Both enter the world FULL.
        ///
        /// <para>
        /// A tank stocked empty was the obvious alternative -- an empty tank is what the plant is
        /// for -- and it is wrong now that running out of air kills: the arrival is a crash landing,
        /// and starting it with sixty seconds of suit reserve and nothing else makes the opening
        /// minute a scramble against a system the player has not been taught yet.
        /// </para>
        /// </summary>
        private const float StockedFull = 1f;

        private static readonly Supply[] Roster =
        {
            new(TankModel, "OxygenTank", "Oxygen Tank", SupplyKind.Oxygen, TankSeconds, StockedFull,
                TankGauge, TankGaugeIndex, TankPackSize, BottleLiesDown),

            // No readout. The battery's charge ladder is five bars with three lit, authored into
            // the model, and there is no separate emissive submesh to drive -- so its charge is
            // read off the machine it is fitted to and off the reticle, not off the brick.
            new(CellModel, "Battery", "Battery", SupplyKind.Power, BatteryWattHours, StockedFull,
                null, 0, CellPackSize, Vector3.zero),
        };

        [MenuItem("Tools/SpaceGame/Items/Build Oxygen Gear")]
        public static void Build()
        {
            MigrateLegacyAssets();

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
            Field.SetEnum(supplySo, "kind", (int)supply.Kind);
            Field.SetFloat(supplySo, "capacity", supply.Capacity);
            Field.SetFloat(supplySo, "startingCharge", supply.StartingCharge);
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
            var tank = AssetDatabase.LoadAssetAtPath<InventoryItem>(TankAsset);
            var battery = AssetDatabase.LoadAssetAtPath<InventoryItem>(BatteryAsset);
            if (tank == null || battery == null) return;

            // A SPARE tank on the wall as well as the one on the rig, which the container can now
            // actually hold: PackItemKey gives every placement its own instance handle, so two of
            // one asset are two placements. Before it, a second tank was silently refused.
            Stock(GearWallPrefab, "the ship's gear wall", tank, battery);
            Stock(ExpeditionRigPrefab, "the expedition rig", tank);
        }

        /// <summary>
        /// Rename what 2026-09-04 renamed, and delete what it merged.
        ///
        /// <para>
        /// <see cref="AssetDatabase.MoveAsset"/> rather than a fresh <c>CreateAsset</c>, because a
        /// move preserves the GUID and an <c>InventoryItem</c>'s <c>ID</c> IS its GUID. Every save
        /// file naming a power cell, and every prefab holding a reference to one, keeps resolving
        /// to the same item under its new name.
        /// </para>
        /// <para>
        /// Idempotent: each step is skipped once its destination exists, so the ordinary run of
        /// this builder on an already-migrated project does nothing at all.
        /// </para>
        /// </summary>
        private static void MigrateLegacyAssets()
        {
            Rename(LegacyCellAsset, BatteryAsset);
            Rename(LegacyCellPrefab, PrefabFolder + "Battery.prefab");

            Delete(MergedEmptyTankAsset);
            Delete(MergedEmptyTankPrefab);
        }

        private static void Rename(string from, string to)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(from) == null) return;
            if (AssetDatabase.LoadAssetAtPath<Object>(to) != null) return;

            string error = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError("[OxygenGear] Could not rename " + from + " to " + to + ": " + error);
                return;
            }

            Debug.Log("[OxygenGear] Renamed " + from + " to " + to + " (GUID preserved).");
        }

        private static void Delete(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) == null) return;

            AssetDatabase.DeleteAsset(path);
            Debug.LogWarning("[OxygenGear] Deleted " + path + " — a tank's charge is a number now, " +
                             "not a second asset. Worlds saved with an empty tank in them will log " +
                             "one unresolved item and lose it.");
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

            // Dangling entries first. Deleting an item asset does not remove references to it —
            // it nulls them in place, silently — so merging the two tanks left a hole in this very
            // list, and a hole here is an item the container tries to stow on every spawn and
            // cannot resolve. Pruned before the adds so the count below is honest.
            int pruned = 0;
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue != null) continue;

                list.DeleteArrayElementAtIndex(i);
                pruned++;
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

            if (added.Count == 0 && pruned == 0) return;

            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(prefab);

            Debug.Log("[OxygenGear] " + what + ": stocked " +
                      (added.Count > 0 ? string.Join(", ", added) : "nothing") +
                      (pruned > 0 ? ", pruned " + pruned + " dangling entry/entries" : "") + ".");
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
                else
                {
                    // The three numbers that make a reservoir what it is. Checked off the saved
                    // asset rather than trusted, because a capacity that failed to serialise reads
                    // as the component's own default -- a battery that silently became a
                    // thirty-minute tank would still fill, still drain and still be wrong.
                    if (supplyItem.Kind != supply.Kind)
                        problems.Add(supply.Name + " is the wrong SupplyKind");
                    if (!Mathf.Approximately(supplyItem.Capacity, supply.Capacity))
                        problems.Add(supply.Name + " capacity reads " + supplyItem.Capacity);
                    if (!Mathf.Approximately(supplyItem.StartingCharge, supply.StartingCharge))
                        problems.Add(supply.Name + " startingCharge reads " + supplyItem.StartingCharge);
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
