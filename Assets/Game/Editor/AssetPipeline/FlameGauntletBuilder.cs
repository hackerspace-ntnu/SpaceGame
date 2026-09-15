using System;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Builds the Flame Gauntlet: the forearm burner that throws one whole burst of fire per
    /// press. Its prefab, its <see cref="InventoryItem"/> asset, and its entry in the network
    /// prefab list.
    ///
    /// <para>
    /// It is the lance's fire on a gauntlet's body. The artifact, jet, tank, ground fire and body
    /// fire are all the lance's own types; what makes it a different item is one bool
    /// (<c>commitToBurst</c>) and a tank sized to a single burst. The particle rig is attached by
    /// <see cref="FlamethrowerJetBuilder.AttachFire"/>, the same code that dresses the lance, so
    /// the two flames are one flame (GDC-L1-ARCH-0002: one implementation per effect).
    /// </para>
    /// <para>
    /// A script rather than hand-authored YAML because the prefab nests an imported FBX. Re-running
    /// REPLACES the prefab wholesale: tuning belongs in the numbers below and in the artifact's
    /// serialized defaults, not in the Inspector.
    /// </para>
    /// </summary>
    public static class FlameGauntletBuilder
    {
        private const string LogTag = "FlameGauntlet";

        private const string ModelPath  = "Assets/Game/Art/Models/Items/gauntlet_flame.fbx";
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/FlameGauntlet.prefab";
        private const string ItemPath   = "Assets/Game/Resources/Items/Artifacts/FlameGauntlet.asset";
        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        // ── The burst ──
        // One press is one burst, and the burst is the tank: it must be full to light and it
        // burns until dry. So the tank IS the burst length, and the refill is the cooldown.
        /// <summary>Seconds of fire per press. The number the item was asked for.</summary>
        public const float BurstSeconds = 3f;
        /// <summary>Seconds after a burst before the next can start.</summary>
        public const float RefillSeconds = 5f;
        /// <summary>Seconds of not firing before the refill begins.</summary>
        private const float RefillDelay = 0.5f;

        // ── The cone ──
        // Longer and narrower than the lance's: an arm-mounted burst the wearer cannot steer for
        // long wants reach more than spread.
        private const float Range = 8f;
        private const float ConeHalfAngle = 22f;

        [MenuItem("Tools/Build Flame Gauntlet Artifact")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[{LogTag}] No model at {ModelPath}. Run models/gear/gauntlet_flame_export.py first.");
                return;
            }

            GameObject root = BuildHierarchy(model, out Transform jetRoot);
            if (root == null) return;

            if (!FlamethrowerJetBuilder.AttachFire(root, jetRoot))
            {
                Debug.LogError($"[{LogTag}] The fire could not be attached; prefab not written.");
                UnityEngine.Object.DestroyImmediate(root);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? ".");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) { Debug.LogError($"[{LogTag}] Prefab save failed."); return; }

            InventoryItem item = EnsureItem(prefab);
            WireItemIntoPickup(prefab, item);
            RegisterNetworkPrefab(prefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{LogTag}] Built {PrefabPath} and {ItemPath}: {BurstSeconds:0}s burst, " +
                      $"{RefillSeconds:0}s refill. Run Tools/Generate All Item Icons for its icon.");
        }

        // ── Hierarchy ───────────────────────────────────────────────────────────

        private static GameObject BuildHierarchy(GameObject model, out Transform jetRoot)
        {
            jetRoot = null;
            var root = new GameObject("FlameGauntlet");

            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.name = "Model";
            modelInstance.transform.SetParent(root.transform, false);

            // Authored in the gauntlet family's frame on the bracer's deck: origin at the wrist
            // joint, the arm down the model's -Z, the burner's mouth out past the hand on +Z.
            Transform grip = GauntletPrefab.AdoptMarker(root.transform, modelInstance.transform,
                                                        "Marker_Grip", "GripPoint", LogTag);
            Transform muzzle = GauntletPrefab.AdoptMarker(root.transform, modelInstance.transform,
                                                          "Marker_Muzzle", "Muzzle", LogTag);
            GauntletPrefab.HideRemainingMarkers(modelInstance.transform);
            if (grip == null || muzzle == null)
            {
                UnityEngine.Object.DestroyImmediate(root);
                return null;
            }

            // The flame leaves along the prefab's forward, the way the bore points. The jet root
            // is the lance's own child under its muzzle: FlamethrowerJetBuilder owns everything
            // beneath it and clears it on a re-run.
            muzzle.localRotation = Quaternion.LookRotation(Vector3.forward);
            var jet = new GameObject("Jet");
            jet.transform.SetParent(muzzle, false);
            jetRoot = jet.transform;

            // ── Pickup / world presence ──
            var netObject = root.AddComponent<NetworkObject>();
            netObject.SynchronizeTransform = true;

            AddInternal(root, "SpaceGame.Items.PickupableItem");
            ItemWorldPresence.Apply(root);

            root.AddComponent<SpaceGame.Core.NetRelay>();
            root.AddComponent<SpaceGame.Core.Persistence.SaveableEntity>();
            root.AddComponent<SpaceGame.Core.Persistence.TransformSaveable>();

            // ── Worn on the forearm, like every gauntlet ──
            GauntletPrefab.MakeWorn(root, grip, modelInstance.transform);

            // ── The tank: one burst, refilled between bursts ──
            var tank = root.AddComponent<SupplyReservoir>();
            GauntletPrefab.SetPrivate(tank, "kind", SupplyKind.Reagent);
            GauntletPrefab.SetPrivate(tank, "capacity", BurstSeconds);
            GauntletPrefab.SetPrivate(tank, "startingCharge", 1f);
            GauntletPrefab.SetPrivate(tank, "drainPerSecond", 1f / BurstSeconds);
            GauntletPrefab.SetPrivate(tank, "refillPerSecond", 1f / RefillSeconds);
            GauntletPrefab.SetPrivate(tank, "refillDelay", RefillDelay);
            // Full, or nothing: this is what makes a press one whole burst rather than whatever
            // is left in the bottle. SetCharge clamps to exactly 1, so 1 is reachable.
            GauntletPrefab.SetPrivate(tank, "restartFraction", 1f);

            // ── The jet and the artifact ──
            root.AddComponent<FlameJet>();
            var artifact = root.AddComponent<FlamethrowerArtifact>();
            GauntletPrefab.SetPrivate(artifact, "range", Range);
            GauntletPrefab.SetPrivate(artifact, "coneHalfAngle", ConeHalfAngle);
            GauntletPrefab.SetPrivate(artifact, "commitToBurst", true);
            GauntletPrefab.SetPrivate(artifact, "tank", tank);
            GauntletPrefab.SetPrivate(artifact, "jet", root.GetComponent<FlameJet>());
            GauntletPrefab.SetPrivate(artifact, "muzzle", muzzle);
            GauntletPrefab.SetPrivate(artifact, "useSoundId", SpaceGame.Audio.SfxId.WeaponEnergyFire);

            return root;
        }

        // ── Item asset, pickup, network registration ─────────────────────────────

        private static InventoryItem EnsureItem(GameObject prefab)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ItemPath) ?? ".");

            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(item, ItemPath);
            }

            item.itemName = "Flame Gauntlet";
            item.itemPrefab = prefab;
            // Gauntlet, not Hand: worn on a forearm and fired on that arm's key, hands left free.
            item.equipKind = EquipKind.Gauntlet;
            // The registry id. OnValidate stamps it for an asset edited by hand, but not in time
            // for one created in this same call, and a null id breaks RegistryLoader in builds.
            item.ID = AssetDatabase.AssetPathToGUID(ItemPath);
            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>The item references the prefab and the prefab references the item; this half
        /// can only be made once both exist.</summary>
        private static void WireItemIntoPickup(GameObject prefab, InventoryItem item)
        {
            Component pickup = prefab.GetComponents<Component>()
                .FirstOrDefault(c => c != null && c.GetType().FullName == "SpaceGame.Items.PickupableItem");
            if (pickup == null) { Debug.LogError($"[{LogTag}] PickupableItem missing."); return; }

            var so = new SerializedObject(pickup);
            so.FindProperty("item").objectReferenceValue = item;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(prefab);
        }

        /// <summary>The list NetworkManager reads. Unregistered item prefabs fail on clients only.</summary>
        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null) { Debug.LogError($"[{LogTag}] No list at {NetworkPrefabsPath}."); return; }
            if (list.Contains(prefab)) return;

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }

        private static void AddInternal(GameObject go, string typeName)
        {
            Type type = typeof(ItemGrip).Assembly.GetType(typeName);
            if (type == null) { Debug.LogError($"[{LogTag}] No type {typeName}."); return; }
            go.AddComponent(type);
        }
    }
}

