using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Builds the Wrist Blade artifact: its prefab, its <see cref="InventoryItem"/> asset, and its
    /// entry in the network prefab list.
    ///
    /// A script rather than hand-authored YAML because the prefab nests an imported FBX, and the
    /// file ids Unity assigns inside a model are decided at import time. Re-runnable, and
    /// re-running REPLACES the prefab wholesale: tuning belongs in the numbers below and in the
    /// artifact's serialized defaults, not in the Inspector.
    /// </summary>
    public static class WristBladeBuilder
    {
        private const string LogTag = "WristBlade";

        /// <summary>
        /// How far the blade slides, in metres. The model's number, not a feel number:
        /// <c>gauntlet_blade.py</c> sets it to the blade's own length and asserts that at that
        /// stroke the tip clears the sheath fully and stays inside the family's reach envelope.
        /// </summary>
        private const float BladeStroke = 0.550f;
        private const string BladeObject = "Mesh_RetractBlade_Straight";

        private const string ModelPath  = "Assets/Game/Art/Models/Items/gauntlet_blade.fbx";
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/WristBlade.prefab";
        private const string ItemPath   = "Assets/Game/Resources/Items/Artifacts/WristBlade.asset";
        private const string SparkMatPath = "Assets/Game/Art/Materials/Weapons/LaserSpark.mat";
        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        private static readonly Color SparkHot = new Color(1.00f, 0.92f, 0.70f, 1.0f);
        private static readonly Color SparkCool = new Color(1.00f, 0.45f, 0.10f, 0.0f);

        [MenuItem("Tools/Build Wrist Blade Artifact")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[{LogTag}] No model at {ModelPath}. Run models/gear/gauntlet_blade_export.py first.");
                return;
            }

            GameObject root = BuildHierarchy(model);

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? ".");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) { Debug.LogError($"[{LogTag}] Prefab save failed."); return; }

            InventoryItem item = EnsureItem(prefab);
            WireItemIntoPickup(prefab, item);
            RegisterNetworkPrefab(prefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{LogTag}] Built {PrefabPath} and {ItemPath}. Run Tools/Generate All Item Icons for its icon.");
        }

        // ── Hierarchy ───────────────────────────────────────────────────────────

        private static GameObject BuildHierarchy(GameObject model)
        {
            var root = new GameObject("WristBlade");

            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.name = "Model";
            modelInstance.transform.SetParent(root.transform, false);

            // Authored in the gauntlet family's frame on the bracer's deck: origin at the wrist
            // joint, the arm down the model's -Z, the blade out past the hand on +Z.
            Transform grip = GauntletPrefab.AdoptMarker(root.transform, modelInstance.transform,
                                                        "Marker_Grip", "GripPoint", LogTag);
            Transform mouth = GauntletPrefab.AdoptMarker(root.transform, modelInstance.transform,
                                                         "Marker_Mouth", "Mouth", LogTag);
            GauntletPrefab.HideRemainingMarkers(modelInstance.transform);

            // Sparks fly forward out of the mouth, the way the blade goes.
            mouth.localRotation = Quaternion.LookRotation(Vector3.forward);
            ParticleSystem sparks = BuildSparks(mouth);

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

            // ── The artifact ──
            var artifact = root.AddComponent<WristBladeArtifact>();
            SetPrivate(artifact, "mouthSparks", sparks);
            SetPrivate(artifact, "bladeThrow", BladeStroke);
            SetPrivateEnum(artifact, "useSoundId", "WeaponMeleeSwing");
            WireBlade(artifact, root.transform, modelInstance.transform);

            return root;
        }

        /// <summary>
        /// Point the artifact at the blade and tell it which way the prefab's forward is in the
        /// blade's parent space. Derived rather than typed, so an export-flag change cannot
        /// leave the blade sliding sideways with no error.
        /// </summary>
        private static void WireBlade(WristBladeArtifact artifact, Transform root, Transform model)
        {
            Transform blade = GauntletPrefab.FindDeep(model, BladeObject);
            if (blade == null)
            {
                Debug.LogError($"[{LogTag}] No '{BladeObject}' in the FBX. The blade will not move.");
                SetPrivate(artifact, "bladeParts", Array.Empty<Transform>());
                return;
            }

            SetPrivate(artifact, "bladeParts", new[] { blade });
            Transform parent = blade.parent != null ? blade.parent : model;
            SetPrivate(artifact, "bladeAxis", parent.InverseTransformDirection(root.forward));
        }

        /// <summary>
        /// Hot sparks off the mouth as the blade grinds out of it. Sized for a three-metre
        /// character seen from a third-person camera: a 1 cm spark at that distance is a dead
        /// pixel. Two bursts a tenth of a second apart cover the slide, so the sparks read as
        /// steel on steel for as long as the steel is moving, not as a puff on the press.
        /// </summary>
        private static ParticleSystem BuildSparks(Transform parent)
        {
            var go = new GameObject("MouthSparks");
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.duration = 0.3f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(SparkHot);
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 48;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14), new ParticleSystem.Burst(0.1f, 8) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 32f;
            shape.radius = 0.02f;

            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(SparkHot, 0f), new GradientColorKey(SparkCool, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 2.5f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var mat = AssetDatabase.LoadAssetAtPath<Material>(SparkMatPath);
            if (mat != null) renderer.sharedMaterial = mat;
            else Debug.LogWarning($"[{LogTag}] No material at {SparkMatPath}; sparks use the default particle material.");

            return ps;
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

            item.itemName = "Wrist Blade";
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

        // ── Reflection helpers ─────────────────────────────────────────────────

        private static void AddInternal(GameObject go, string typeName)
        {
            Type type = typeof(ItemGrip).Assembly.GetType(typeName);
            if (type == null) { Debug.LogError($"[{LogTag}] No type {typeName}."); return; }
            go.AddComponent(type);
        }

        private static FieldInfo Field(object target, string name)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo info = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (info != null) return info;
            }
            Debug.LogError($"[{LogTag}] No field '{name}' on {target.GetType().Name}.");
            return null;
        }

        private static void SetPrivate(object target, string name, object value) =>
            Field(target, name)?.SetValue(target, value);

        private static void SetPrivateEnum(object target, string name, string enumValue)
        {
            FieldInfo field = Field(target, name);
            if (field == null) return;
            try { field.SetValue(target, Enum.Parse(field.FieldType, enumValue)); }
            catch (ArgumentException)
            {
                Debug.LogWarning($"[{LogTag}] '{enumValue}' is not a {field.FieldType.Name}; left at default.");
            }
        }
    }
}
