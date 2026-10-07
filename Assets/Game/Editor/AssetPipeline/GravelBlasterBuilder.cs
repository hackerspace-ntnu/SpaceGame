using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FirstGearGames.SmoothCameraShaker;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Builds the Gravel Blaster artifact: its prefab, its <see cref="InventoryItem"/> asset, and
    /// its entry in the network prefab list. The emitters come from
    /// <see cref="PelletGunFxBuilder"/>, shared with the basic gun.
    ///
    /// A script rather than hand-authored YAML because the prefab nests an imported FBX, and the
    /// file ids Unity assigns inside a model are decided at import time — a hand-written prefab
    /// referencing guessed ids loads with a missing model and no error.
    ///
    /// Re-runnable, and re-running REPLACES the prefab wholesale. Tuning belongs in the numbers
    /// below, not in the Inspector.
    /// </summary>
    public static class GravelBlasterBuilder
    {
        private const string LogTag = "GravelBlaster";
        private const string ModelPath  = "Assets/Game/Art/Models/Weapons/GravelBlaster/gravel_blaster.fbx";
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/GravelBlaster.prefab";
        private const string ItemPath   = "Assets/Game/Resources/Items/Artifacts/GravelBlaster.asset";
        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";
        private const string BlastShakePath = "Assets/Game/ScriptableObjects/Shake/GravelBlastShake.asset";

        [MenuItem("Tools/Build Gravel Blaster Artifact")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[GravelBlaster] No model at {ModelPath}. Run gravel_blaster_export.py first.");
                return;
            }

            if (!PelletGunFxBuilder.TryLoadMaterials(LogTag, out PelletGunFxBuilder.Materials materials))
                return;

            ShakeData blastShake = PelletGunFxBuilder.EnsureShake(BlastShakePath, LogTag);
            if (blastShake == null) return;

            GameObject root = BuildHierarchy(model, materials, blastShake);

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? ".");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) { Debug.LogError("[GravelBlaster] Prefab save failed."); return; }

            InventoryItem item = EnsureItem(prefab);
            WireItemIntoPickup(prefab, item);
            RegisterNetworkPrefab(prefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[GravelBlaster] Built {PrefabPath} and {ItemPath}. " +
                      "Run Tools/Generate All Item Icons for the inventory icon.");
        }

        // ── Hierarchy ──────────────────────────────────────────────────────────

        private static GameObject BuildHierarchy(GameObject model,
                                                 PelletGunFxBuilder.Materials materials,
                                                 ShakeData blastShake)
        {
            Material debrisMat = materials.Debris;
            Material sparkMat = materials.Spark;
            Material smokeMat = materials.Smoke;

            var root = new GameObject("GravelBlaster");

            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.name = "Model";
            modelInstance.transform.SetParent(root.transform, false);

            // The two markers exported with the mesh exist only to carry coordinates across the
            // FBX; turned into plain transforms they become the muzzle and the grip.
            Transform muzzle = AdoptMarker(root.transform, modelInstance.transform,
                                           "Marker_Muzzle", "Muzzle");
            Transform grip = AdoptMarker(root.transform, modelInstance.transform,
                                         "Marker_Grip", "GripPoint");

            // Aim the muzzle down the barrels. The two markers both sit on the barrel axis (to
            // within a couple of degrees), so grip→muzzle is the firing direction — measured
            // rather than typed, because the FBX import rotation is exactly the kind of constant
            // that goes silently wrong.
            Vector3 fireDir = (muzzle.localPosition - grip.localPosition).normalized;
            muzzle.localRotation = Quaternion.LookRotation(fireDir);

            // The breech: where the backfire erupts, at the receiver behind the springs, facing
            // back at the holder.
            var breech = new GameObject("Breech");
            breech.transform.SetParent(root.transform, false);
            breech.transform.localPosition = Vector3.Lerp(grip.localPosition,
                                                          muzzle.localPosition, 0.18f);
            breech.transform.localRotation = Quaternion.LookRotation(-fireDir);

            // ── Backfire rig: one parent system, played with its children ──
            // Slower, wider and dirtier than the muzzle blast: this one goes off in the holder's
            // face, and it has to read as the gun failing rather than as a second shot.
            ParticleSystem backfire = PelletGunFxBuilder.BuildGravel(
                breech.transform, debrisMat, "BackfireBurst",
                count: 70, minSpeed: 6f, maxSpeed: 16f, cone: 42f);
            PelletGunFxBuilder.BuildSparks(backfire.transform, sparkMat, "BackfireSparks",
                                           count: 100, cone: 60f);
            PelletGunFxBuilder.BuildDust(backfire.transform, smokeMat, "BackfireSmoke",
                                         count: 50, cone: 50f, dark: true);

            // ── Presentation ──
            // Every emitter hangs off one component so the artifact keeps only the shot itself.
            // The per-pellet systems hang off the ROOT: they are moved to wherever a pellet landed.
            PelletGunFx fx = PelletGunFxBuilder.AddFx(root, new PelletGunFxBuilder.Rig
            {
                Muzzle = muzzle,
                MuzzleBurst = PelletGunFxBuilder.BuildGravel(muzzle, debrisMat, "GravelBurst",
                                                             count: 150, minSpeed: 22f,
                                                             maxSpeed: 44f, cone: 11f),
                MuzzleDust = PelletGunFxBuilder.BuildDust(muzzle, smokeMat, "MuzzleDust",
                                                          count: 44, cone: 18f),
                MuzzleSparks = PelletGunFxBuilder.BuildSparks(muzzle, sparkMat, "MuzzleSparks",
                                                              count: 90, cone: 14f),
                MuzzleSmoke = PelletGunFxBuilder.BuildMuzzleSmoke(muzzle, smokeMat, count: 20),
                BlastWave = PelletGunFxBuilder.BuildBlastWave(muzzle, smokeMat, count: 4,
                                                              sizeScale: 1f),
                MuzzleFlash = PelletGunFxBuilder.BuildMuzzleFlash(muzzle, range: 12f,
                                                                  intensity: 16f),
                // 70 m at 165 m/s.
                Tracers = PelletGunFxBuilder.BuildTracers(root.transform, sparkMat,
                                                          minSize: 0.035f, maxSize: 0.07f,
                                                          velocityScale: 0.022f, lengthScale: 2f),
                ImpactSparks = PelletGunFxBuilder.BuildImpactSparks(root.transform, sparkMat),
                ImpactDust = PelletGunFxBuilder.BuildImpactDust(root.transform, smokeMat),
                ImpactDebris = PelletGunFxBuilder.BuildImpactDebris(root.transform, debrisMat),
                Backfire = backfire,
                Shake = blastShake,
            });

            // ── Pickup / world presence ──
            // Mirrors LightningSpell.prefab component for component: the same prefab is both the
            // thing in your hand and the thing lying in the sand.
            var netObject = root.AddComponent<NetworkObject>();
            netObject.SynchronizeTransform = true;

            AddInternal(root, "SpaceGame.Items.PickupableItem");

            // The body, a collider the shape of the item, the sizing and the netcode that lets
            // another machine watch it be shoved about. One shared block - see ItemWorldPresence
            // for what nine hand-written copies of it cost, and why the sphere it replaces here
            // made a dropped item roll like a marble.
            ItemWorldPresence.Apply(root);

            root.AddComponent<SpaceGame.Core.NetRelay>();
            root.AddComponent<SpaceGame.Core.Persistence.SaveableEntity>();
            root.AddComponent<SpaceGame.Core.Persistence.TransformSaveable>();

            // ── Grip ──
            // Zero offsets, like the portal gun: the same Blender front (-Y) and export flags
            // land the same orientation in the hand. holdSize is the Gun bracket of
            // ItemScaleLadder — change it there and here together, because this builder rewrites
            // the prefab wholesale and would otherwise quietly undo the ladder on its next run.
            var itemGrip = root.AddComponent<ItemGrip>();
            SetPrivate(itemGrip, "gripPoint", grip);
            SetPrivate(itemGrip, "holdSize", 1.25f);
            SetPrivate(itemGrip, "sizeReference", modelInstance.transform);

            // ── The artifact ──
            var artifact = root.AddComponent<GravelBlasterArtifact>();
            SetPrivate(artifact, "fx", fx);
            SetPrivateEnum(artifact, "useSoundId", "WeaponGunFire");

            return root;
        }

        // ── Markers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Turn an exported marker cube into a plain transform on the prefab root. The 4 mm mesh
        /// exists only to carry a coordinate across the FBX; leaving its renderer would float a
        /// cube in the model.
        /// </summary>
        private static Transform AdoptMarker(Transform root, Transform model, string markerName,
                                             string wantedName)
        {
            var adopted = new GameObject(wantedName);
            adopted.transform.SetParent(root, false);

            Transform marker = FindDeep(model, markerName);
            if (marker == null)
            {
                Debug.LogWarning($"[GravelBlaster] No {markerName} in the FBX; {wantedName} left at origin.");
                return adopted.transform;
            }

            adopted.transform.localPosition = root.InverseTransformPoint(marker.position);
            marker.gameObject.SetActive(false);
            return adopted.transform;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        // ── Item asset, pickup, network registration ───────────────────────────

        private static InventoryItem EnsureItem(GameObject prefab)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ItemPath) ?? ".");

            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(item, ItemPath);
            }

            item.itemName = "Gravel Blaster";
            item.itemPrefab = prefab;
            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>
        /// The item asset references the saved prefab and the prefab references the item, so one
        /// of the two links can only be made once both files exist.
        /// </summary>
        private static void WireItemIntoPickup(GameObject prefab, InventoryItem item)
        {
            Component pickup = prefab.GetComponents<Component>()
                .FirstOrDefault(c => c != null && c.GetType().FullName == "SpaceGame.Items.PickupableItem");
            if (pickup == null) { Debug.LogError("[GravelBlaster] PickupableItem missing."); return; }

            var so = new SerializedObject(pickup);
            so.FindProperty("item").objectReferenceValue = item;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SavePrefabAsset(prefab);
        }

        /// <summary>
        /// The list NetworkManager actually reads — NOT Assets/DefaultNetworkPrefabs.asset, which
        /// regenerates itself. An unregistered item prefab fails on CLIENTS ONLY, so solo
        /// playtesting cannot find the mistake.
        /// </summary>
        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null) { Debug.LogError($"[GravelBlaster] No list at {NetworkPrefabsPath}."); return; }
            if (list.Contains(prefab)) return;

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }

        // ── Reflection helpers ─────────────────────────────────────────────────
        //
        // Item components serialize private fields, which is right for runtime code and simply
        // means an editor script goes in the way the Inspector does. PickupableItem is
        // additionally internal to Assembly-CSharp, so it cannot be named from here at all.

        private static void AddInternal(GameObject go, string typeName)
        {
            Type type = typeof(ItemGrip).Assembly.GetType(typeName);
            if (type == null) { Debug.LogError($"[GravelBlaster] No type {typeName}."); return; }
            go.AddComponent(type);
        }

        private static FieldInfo Field(object target, string name)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo info = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (info != null) return info;
            }

            Debug.LogError($"[GravelBlaster] No field '{name}' on {target.GetType().Name}.");
            return null;
        }

        private static void SetPrivate(object target, string name, object value) =>
            Field(target, name)?.SetValue(target, value);

        private static void SetPrivateLayerMask(object target, string name, int mask) =>
            Field(target, name)?.SetValue(target, (LayerMask)mask);

        private static void SetPrivateEnum(object target, string name, string enumValue)
        {
            FieldInfo field = Field(target, name);
            if (field == null) return;

            try { field.SetValue(target, Enum.Parse(field.FieldType, enumValue)); }
            catch (ArgumentException)
            {
                Debug.LogWarning($"[GravelBlaster] '{enumValue}' is not a {field.FieldType.Name}; left at default.");
            }
        }
    }
}
