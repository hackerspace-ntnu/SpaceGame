using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Builds <c>FlashlightGauntlet.prefab</c>: the worn torch.
    ///
    /// <para>
    /// <b>It starts as a copy of the Ruin Scanner's prefab, on purpose.</b> A gauntlet's root
    /// carries fifteen components that have nothing to do with what the device does — a
    /// NetworkObject and NetworkTransform, NetRelay and NetAuthority, SaveableEntity with a
    /// transform and rigidbody saver, PickupableItem, WorldItem, a Rigidbody and a collider — and
    /// every one of them fails silently when it is wrong. Re-authoring that list from a blank
    /// GameObject means re-deriving fourteen sets of Inspector values from memory and finding out
    /// which one was wrong on a client, at night, three sessions later. Copying a shipped,
    /// playtested gauntlet and changing the two things that actually differ is the smaller risk.
    /// </para>
    /// <para>
    /// The two things: the model child becomes <c>gauntlet_flashlight.fbx</c>, and
    /// <see cref="RuinScannerArtifact"/> becomes <see cref="FlashlightGauntletArtifact"/>. Then one
    /// addition the other five gauntlets have no equivalent of — <c>Flashlight.prefab</c>, the
    /// authored lamp, nested on the model's <c>Emitter</c> so the beam leaves the horn.
    /// </para>
    /// <para>
    /// Re-runnable, and destructive to the prefab it owns: run it again after re-exporting the
    /// model and it rebuilds from the Ruin Scanner again. Tuning belongs in the constants here or
    /// on <c>Flashlight.prefab</c>, never hand-typed onto the built prefab.
    /// </para>
    /// <para>
    /// <b>Verified out loud.</b> Unity discards prefab saves when the AssetDatabase is read-only
    /// and says nothing (see <c>ItemScaleLadder</c>), so the result is re-loaded off disk and its
    /// wiring asserted before this reports success.
    /// </para>
    /// </summary>
    public static class FlashlightGauntletBuilder
    {
        private const string LogTag = "FlashlightGauntlet";

        private const string Donor = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/RuinScanner.prefab";
        private const string Target = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/FlashlightGauntlet.prefab";
        private const string Model = "Assets/Game/Art/Models/Items/gauntlet_flashlight.fbx";
        private const string Lamp = "Assets/Game/Prefabs/VisualEffects/Lighting/Flashlight.prefab";

        /// <summary>
        /// Under <c>Resources/Items</c> and nowhere else: <c>RegistryLoader</c> finds items with
        /// <c>Resources.LoadAll&lt;InventoryItem&gt;("Items")</c>, and an asset outside that scan
        /// never registers, never appears in the dev browser, and comes back empty out of every
        /// save slot that held it.
        /// </summary>
        private const string Item = "Assets/Game/Resources/Items/Artifacts/FlashlightGauntlet.asset";

        /// <summary>
        /// Where the donor is copied to before it is reshaped.
        ///
        /// <para>
        /// The donor is never edited in place, and the target is never deleted: deleting it would
        /// retire its asset GUID, and that GUID is what the network prefab list, the item asset and
        /// every save that holds a dropped gauntlet refer to. Overwriting the same path through
        /// <c>SaveAsPrefabAsset</c> keeps it.
        /// </para>
        /// </summary>
        private const string Scratch = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/~FlashlightGauntletBuild.prefab";

        /// <summary>The empty at the horn's mouth. The lamp hangs here, unrotated.</summary>
        private const string EmitterNode = "Emitter";

        /// <summary>The one emissive part of the device; the artifact dims it when the torch is off.</summary>
        private const string BulbNode = "Mesh_Flashlight_Bulb";

        [MenuItem("Tools/SpaceGame/Items/Build Flashlight Gauntlet")]
        public static void Apply()
        {
            var log = new StringBuilder();

            if (AssetDatabase.LoadAssetAtPath<GameObject>(Model) == null)
            {
                Debug.LogError($"[{LogTag}] No model at {Model} — run gauntlet_flashlight_export.py first.");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(Donor) == null)
            {
                Debug.LogError($"[{LogTag}] The donor prefab {Donor} is missing; nothing to copy from.");
                return;
            }

            AssetDatabase.DeleteAsset(Scratch);
            if (!AssetDatabase.CopyAsset(Donor, Scratch))
            {
                Debug.LogError($"[{LogTag}] Could not copy {Donor} to {Scratch}.");
                return;
            }
            AssetDatabase.ImportAsset(Scratch, ImportAssetOptions.ForceSynchronousImport);

            GameObject contents = PrefabUtility.LoadPrefabContents(Scratch);
            try
            {
                Build(contents, log);
                PrefabUtility.SaveAsPrefabAsset(contents, Target);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
                AssetDatabase.DeleteAsset(Scratch);
            }

            LinkItem(log);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Verify(log);

            Debug.Log($"[{LogTag}] Built {Target}.\n{log}");
        }

        private static void Build(GameObject root, StringBuilder log)
        {
            root.name = "FlashlightGauntlet";

            // The donor's brain, and everything that hung off it. DestroyImmediate rather than a
            // disable: a RuinScannerArtifact left on the prefab would be a second UsableItem on one
            // root, and the controllers take the first one they find.
            var donorArtifact = root.GetComponent<RuinScannerArtifact>();
            if (donorArtifact != null) Object.DestroyImmediate(donorArtifact, true);

            // Cleared rather than left pointing at the Ruin Scanner, which would make this prefab
            // pick up as one. LinkItem writes the real reference once the item asset exists — the
            // two files point at each other and neither can be written first.
            Component pickup = FindPickupable(root);
            if (pickup != null) GauntletPrefab.SetPrivate(pickup, "item", null);

            // Collect first, destroy after: destroying while iterating a Transform skips siblings.
            var doomed = new System.Collections.Generic.List<GameObject>();
            foreach (Transform child in root.transform) doomed.Add(child.gameObject);
            foreach (GameObject child in doomed) Object.DestroyImmediate(child);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(Model));
            model.name = "Model";
            model.transform.SetParent(root.transform, false);

            // The grip point is the wrist joint, which is the model's own origin — the frame the
            // whole family is authored in. A gauntlet can sit in a hotbar slot, where it is inert
            // but still has to be held somewhere.
            var grip = new GameObject("GripPoint");
            grip.transform.SetParent(root.transform, false);

            GauntletPrefab.MakeWorn(root, grip.transform, model.transform);

            Transform emitter = GauntletPrefab.AdoptMarker(root.transform, model.transform,
                                                           EmitterNode, "Emitter", LogTag);

            var lamp = SeatLamp(emitter, log);
            var bulb = FindRenderer(model.transform, BulbNode, log);

            var artifact = root.AddComponent<FlashlightGauntletArtifact>();
            GauntletPrefab.SetPrivate(artifact, "lamp", lamp);
            GauntletPrefab.SetPrivate(artifact, "bulb", bulb);

            GauntletPrefab.HideRemainingMarkers(model.transform);

            // The donor's SAVEABLE PREFAB ID came along with the copy, and it is the Ruin
            // Scanner's own asset GUID. A dropped flashlight gauntlet would be written into the
            // save under that id and come back on load as a ruin scanner — or as nothing, if the
            // donor were ever deleted. Cleared here and re-stamped by LinkItem, which is the first
            // point at which this prefab's own GUID exists.
            ClearPrefabId(root, log);

            log.AppendLine($"  model {System.IO.Path.GetFileName(Model)}, " +
                           $"lamp {(lamp != null ? "seated" : "MISSING")}, " +
                           $"bulb {(bulb != null ? BulbNode : "MISSING")}.");
        }

        /// <summary>
        /// Make the item asset, and close the loop between it and the prefab.
        ///
        /// <para>
        /// The two files point at each other and neither can be written first, which is why this
        /// runs after the prefab is saved rather than inside <see cref="Build"/>. An existing asset
        /// is updated rather than replaced: its GUID is its registry ID, it is written into every
        /// save that holds one, and a new asset would empty that slot for every player who owns the
        /// gauntlet.
        /// </para>
        /// </summary>
        private static void LinkItem(StringBuilder log)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Target);
            var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(Item);

            bool fresh = asset == null;
            if (fresh)
            {
                asset = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(asset, Item);
            }

            asset.itemName = "Flashlight Gauntlet";
            asset.itemPrefab = prefab;

            // Gauntlet, not Hand. BodySlotRules reads this to decide which slots will take it, and
            // EquipmentController reads it to leave the hands empty when it is selected on the
            // hotbar — a torch you have to hold is not the point of wearing one.
            asset.equipKind = EquipKind.Gauntlet;

            // The registry ID, which InventoryItem.OnValidate is supposed to stamp with the
            // asset's own GUID. It does not run in time for an asset created in the same call, and
            // an item that ships with a null ID is the failure its own docs single out as the
            // worst this project has: editor-invisible, build-only. RegistryLoader indexes a
            // dictionary by that null and throws on the first item, leaving the game with NO item
            // registry at all — and real multiplayer means built players.
            asset.ID = AssetDatabase.AssetPathToGUID(Item);

            // The icon is left alone: Tools ▸ Generate All Item Icons renders it from itemPrefab
            // and writes it back here, and overwriting the field would throw away a rendered icon
            // every time this builder ran.
            EditorUtility.SetDirty(asset);

            // Back-reference, now that the asset exists. Without it the gauntlet can be dropped but
            // never picked up again.
            GameObject contents = PrefabUtility.LoadPrefabContents(Target);
            try
            {
                Component pickup = FindPickupable(contents);
                if (pickup == null)
                {
                    log.AppendLine("  no PickupableItem on the prefab; it cannot be picked back up.");
                    return;
                }

                GauntletPrefab.SetPrivate(pickup, "item", AssetDatabase.LoadAssetAtPath<InventoryItem>(Item));
                StampPrefabId(contents, log);
                PrefabUtility.SaveAsPrefabAsset(contents, Target);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            log.AppendLine($"  item asset {(fresh ? "created" : "updated")} at {Item}.");
        }

        /// <summary>Blank the saveable prefab id the copy inherited from the donor.</summary>
        private static void ClearPrefabId(GameObject root, StringBuilder log)
        {
            Component saveable = FindByTypeName(root, "SaveableEntity");
            if (saveable == null)
            {
                log.AppendLine("  no SaveableEntity on the prefab; it cannot be saved when dropped.");
                return;
            }

            GauntletPrefab.SetPrivate(saveable, "prefabId", string.Empty);
        }

        /// <summary>
        /// Stamp this prefab's own GUID as its saveable prefab id.
        ///
        /// <para>
        /// <c>Tools ▸ Save System ▸ Wire Saveable Prefabs</c> does this for the whole project and
        /// <c>SaveWiringOnDiskTests</c> asserts it, but a builder that leaves the job to a second
        /// menu item is a builder whose output is broken between the two clicks — and the failure
        /// is a dropped gauntlet that reloads as somebody else's item.
        /// </para>
        /// </summary>
        private static void StampPrefabId(GameObject root, StringBuilder log)
        {
            Component saveable = FindByTypeName(root, "SaveableEntity");
            if (saveable == null) return;

            string guid = AssetDatabase.AssetPathToGUID(Target);
            GauntletPrefab.SetPrivate(saveable, "prefabId", guid);
            log.AppendLine($"  saveable prefabId stamped {guid}.");
        }

        /// <summary>
        /// The prefab's <c>PickupableItem</c>, found by type NAME.
        ///
        /// <para>
        /// <c>PickupableItem</c> is <c>internal</c> to Assembly-CSharp, so an editor script cannot
        /// name the type at all — <c>GetComponent&lt;PickupableItem&gt;()</c> is a compile error,
        /// not a runtime null. <see cref="GauntletReseat"/> reaches every component this way for
        /// the same reason; widening the type just to let a builder see it would be the tail
        /// wagging the dog.
        /// </para>
        /// </summary>
        private static Component FindPickupable(GameObject root) => FindByTypeName(root, "PickupableItem");

        /// <summary>A root component named at runtime rather than by type. See above.</summary>
        private static Component FindByTypeName(GameObject root, string typeName)
        {
            foreach (Component c in root.GetComponents<Component>())
                if (c != null && c.GetType().Name == typeName) return c;

            return null;
        }

        /// <summary>
        /// Nest the authored lamp on the emitter, pointing out of the horn.
        ///
        /// <para>
        /// Identity local pose, and that is the whole reason the model's <c>Emitter</c> empty is
        /// exported unrotated: the FBX axis conversion carries Blender −Y (out of the mouth) onto
        /// Unity +Z, which is the axis a spot light shines down. Any rotation typed here would be a
        /// second opinion about which way the horn faces.
        /// </para>
        /// <para>
        /// The lamp's own local position is cleared too. <c>Flashlight.prefab</c> was authored as a
        /// child of the Main Camera and carries the eye offset (0.15, 0.12, 0.5) it needed there;
        /// on the emitter that would put the light half a metre in front of the hand.
        /// </para>
        /// </summary>
        private static Flashlight SeatLamp(Transform emitter, StringBuilder log)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Lamp);
            if (asset == null)
            {
                log.AppendLine($"  no lamp prefab at {Lamp} — the gauntlet will light nothing.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            instance.name = "Lamp";
            instance.transform.SetParent(emitter, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            return instance.GetComponent<Flashlight>();
        }

        private static Renderer FindRenderer(Transform model, string node, StringBuilder log)
        {
            Transform found = GauntletPrefab.FindDeep(model, node);
            if (found == null)
            {
                log.AppendLine($"  the model has no '{node}'.");
                return null;
            }

            var renderer = found.GetComponent<Renderer>();
            if (renderer == null) log.AppendLine($"  '{node}' has no Renderer.");
            return renderer;
        }

        /// <summary>Read the prefab back off disk and check the wiring actually landed.</summary>
        private static void Verify(StringBuilder log)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Target);
            if (prefab == null)
            {
                log.AppendLine("  VERIFY: the prefab is not on disk — the save was discarded.");
                return;
            }

            if (prefab.GetComponent<FlashlightGauntletArtifact>() == null)
                log.AppendLine("  VERIFY: no FlashlightGauntletArtifact on the root.");

            if (prefab.GetComponent<RuinScannerArtifact>() != null)
                log.AppendLine("  VERIFY: the Ruin Scanner's artifact survived — two UsableItems on one root.");

            var grip = prefab.GetComponent<ItemGrip>();
            var fit = prefab.GetComponent<GauntletFit>();
            if (grip == null || fit == null)
                log.AppendLine("  VERIFY: not a worn gauntlet — ItemGrip or GauntletFit is missing.");
            else if (!Mathf.Approximately(grip.HoldSize, GauntletPrefab.HoldSize) ||
                     !Mathf.Approximately(grip.PackSize, GauntletPrefab.PackSize))
                log.AppendLine($"  VERIFY: sizes are {grip.HoldSize}/{grip.PackSize}, " +
                               $"not {GauntletPrefab.HoldSize}/{GauntletPrefab.PackSize}.");

            var lamp = prefab.GetComponentInChildren<Flashlight>(true);
            if (lamp == null)
                log.AppendLine("  VERIFY: no Flashlight under the prefab — it is a gauntlet with no lamp.");
            else if (lamp.transform.parent == null || lamp.transform.parent.name != "Emitter")
                log.AppendLine($"  VERIFY: the lamp hangs on '{lamp.transform.parent?.name}', not the Emitter.");
            else if (lamp.transform.localPosition != Vector3.zero)
                log.AppendLine($"  VERIFY: the lamp is offset {lamp.transform.localPosition} from the emitter.");

            // A prefab whose NetworkObject never had its hash computed spawns as nothing on a
            // client, with no error on the host that dropped it — see the network prefab rules.
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(Item);
            if (item == null)
                log.AppendLine($"  VERIFY: no item asset at {Item} — nothing can hold this gauntlet.");
            else if (item.itemPrefab != prefab)
                log.AppendLine("  VERIFY: the item asset does not point at this prefab.");
            else if (item.equipKind != EquipKind.Gauntlet)
                log.AppendLine($"  VERIFY: the item is a {item.equipKind}, so no forearm slot will take it.");
            else if (item.ID != AssetDatabase.AssetPathToGUID(Item))
                log.AppendLine($"  VERIFY: the item's registry ID is '{item.ID}', not its asset GUID — " +
                               "it will not register in a built player.");

            Component saveable = FindByTypeName(prefab, "SaveableEntity");
            if (saveable == null)
            {
                log.AppendLine("  VERIFY: no SaveableEntity — a dropped gauntlet will not survive a reload.");
            }
            else
            {
                var so = new SerializedObject(saveable);
                string stamped = so.FindProperty("prefabId")?.stringValue;
                if (stamped != AssetDatabase.AssetPathToGUID(Target))
                    log.AppendLine($"  VERIFY: the saveable prefabId is '{stamped}', not this prefab's " +
                                   "own GUID — a dropped gauntlet reloads as whatever that id names.");
            }

            var netObj = prefab.GetComponent<NetworkObject>();
            if (netObj == null)
                log.AppendLine("  VERIFY: no NetworkObject — this cannot be dropped in a networked game.");
            else if (netObj.PrefabIdHash == 0)
                log.AppendLine("  VERIFY: the NetworkObject's PrefabIdHash is 0. Re-import the prefab.");
        }
    }
}
