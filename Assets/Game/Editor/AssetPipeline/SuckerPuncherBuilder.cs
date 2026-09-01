using System;
using System.Collections.Generic;
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
    /// Builds the Sucker Puncher artifact: its prefab, its <see cref="InventoryItem"/> asset, its
    /// shockwave-ring material, and its entry in the network prefab list.
    ///
    /// A script rather than hand-authored YAML because the prefab nests an imported FBX, and the
    /// file ids Unity assigns inside a model are decided at import time — a hand-written prefab
    /// referencing guessed ids loads with a missing model and no error.
    ///
    /// Re-runnable, and re-running REPLACES the prefab wholesale. Tuning belongs in the numbers
    /// below, not in the Inspector.
    /// </summary>
    public static class SuckerPuncherBuilder
    {
        private const string ModelPath  = "Assets/Game/Art/Models/Items/sucker_puncher.fbx";
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/SuckerPuncher.prefab";
        private const string ItemPath   = "Assets/Game/Resources/Items/Artifacts/SuckerPuncher.asset";
        private const string RingMatPath = "Assets/Game/Art/Materials/Artifacts/SuckerPuncherShockRing.mat";
        private const string SmokeMatPath = "Assets/Game/Art/Materials/Weapons/LaserSmoke.mat";
        private const string ShakePath  = "Assets/Game/ScriptableObjects/Shake/RepulsorBlastShake.asset";
        private const string ShockShader = "SpaceGame/Artifacts/RepulsorShockwave";
        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        /// <summary>The ground layer DropItemPhysics settles against, shared by every artifact.</summary>
        private const int GroundLayerMask = 128;

        /// <summary>
        /// The four objects that ride the rails. They share one origin in the .blend
        /// (see sucker_puncher_BUILD.md), which is what lets the artifact slide all four by the
        /// same local offset instead of carrying a per-part rest pose.
        ///
        /// The piston rod is one of them. At the throw this item now uses, a rod welded to the
        /// cylinder shell visibly tears away from the carriage it is supposed to be driving;
        /// riding along with the ram, it slides out of the shell as a real one does.
        /// </summary>
        private static readonly string[] RamParts =
        {
            "Mesh_RamSlide_Carriage",
            "Mesh_SuckerPuncher_RamArm",
            "Mesh_KnuckleBlock_Segmented",
            "Mesh_RamSlide_Rod",
        };

        /// <summary>Hot white steam shading to soot, not the repulsor's cold blue.</summary>
        private static readonly Color SteamHot = new Color(1.00f, 0.96f, 0.88f, 0.85f);
        private static readonly Color SteamCool = new Color(0.62f, 0.60f, 0.56f, 0.0f);
        private static readonly Color RingWarm = new Color(1.00f, 0.86f, 0.62f, 1.0f);

        [MenuItem("Tools/Build Sucker Puncher Artifact")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[SuckerPuncher] No model at {ModelPath}. " +
                               "Run models/gear/sucker_puncher_export.py first.");
                return;
            }

            Material ringMat = EnsureRingMaterial();
            if (ringMat == null) return;

            GameObject root = BuildHierarchy(model, ringMat);

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? ".");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) { Debug.LogError("[SuckerPuncher] Prefab save failed."); return; }

            InventoryItem item = EnsureItem(prefab);
            WireItemIntoPickup(prefab, item);
            RegisterNetworkPrefab(prefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SuckerPuncher] Built {PrefabPath} and {ItemPath}. " +
                      "Run Tools/Generate All Item Icons to give it an inventory icon.");
        }

        // ── Hierarchy ──────────────────────────────────────────────────────────

        private static GameObject BuildHierarchy(GameObject model, Material ringMat)
        {
            var root = new GameObject("SuckerPuncher");

            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.name = "Model";
            modelInstance.transform.SetParent(root.transform, false);

            // The gauntlet was authored -Y forward / +Z up and exported with the same flags as the
            // portal gun and the gravel blaster, so it arrives pointing down the prefab's own +Z
            // with the hazard plate up. No correction rotation is applied here for the same reason
            // those two apply none.
            Transform grip = AdoptMarker(root.transform, modelInstance.transform,
                                         "Marker_Grip", "GripPoint");
            Transform vent = AdoptMarker(root.transform, modelInstance.transform,
                                         "Marker_Vent", "Vent");

            // Marker_Fist and Marker_Gauge are exported too and nothing consumes them yet. They
            // are 4 mm cubes, so leaving them visible would float two specks in the model.
            HideRemainingMarkers(modelInstance.transform);

            // Steam is vented backwards and up, away from the punch — a plume that followed the
            // fist would sit in front of the thing the player is trying to look at.
            vent.localRotation = Quaternion.LookRotation(new Vector3(0f, 0.55f, -1f).normalized);
            ParticleSystem steam = BuildSteam(vent);

            // ── Pickup / world presence ──
            // Mirrors LightningSpell.prefab component for component: the same prefab is both the
            // thing on your arm and the thing lying in the sand.
            var netObject = root.AddComponent<NetworkObject>();
            netObject.SynchronizeTransform = true;

            SphereCollider sphere = root.AddComponent<SphereCollider>();
            sphere.radius = 0.2f;

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = true;

            AddInternal(root, "SpaceGame.Items.PickupableItem");

            var drop = root.AddComponent<DropItemPhysics>();
            SetPrivate(drop, "rb", body);
            SetPrivateLayerMask(drop, "groundLayer", GroundLayerMask);

            root.AddComponent<SpaceGame.Core.NetRelay>();
            root.AddComponent<SpaceGame.Core.Persistence.SaveableEntity>();
            root.AddComponent<SpaceGame.Core.Persistence.TransformSaveable>();

            // ── Grip ──
            var itemGrip = root.AddComponent<ItemGrip>();
            SetPrivate(itemGrip, "gripPoint", grip);

            // The model's own longest axis, so world scale is pinned at 1.0 and the metres in the
            // .blend are metres in the game. That matters for a worn item more than for a held
            // one: the cavity is sized against measured hand dimensions, and any scaling at all
            // invalidates it.
            //
            // `holdSize = 0` would give the same 1.0 here, since ApplyScale divides out the rig's
            // scale before applying the authored one. Stating it explicitly is the point — it
            // stops a future rig or export change from silently resizing a part that has to fit a
            // hand. Re-measure if the model's extents change: it is the Y extent of the .blend's
            // bounding box (see sucker_puncher_BUILD.md).
            SetPrivate(itemGrip, "holdSize", 0.674f);

            // A quarter turn about the item's own forward, so the guard plate ends up on the BACK
            // OF THE HAND rather than out past the thumb.
            //
            // HandGripFrame's up is the thumb side, which is right for a gun (the sights sit
            // thumb-side in a pistol grip) and wrong for something worn over the hand. The frame's
            // remaining axis is the palm normal, and for a right hand the thumb sits on the index
            // side of index->pinky, which puts the item's +X out the back of the hand — hence -90
            // rather than +90. If it ever reads mirrored, this is the single number to flip.
            SetPrivate(itemGrip, "rotationOffset", new Vector3(0f, 0f, -90f));
            SetPrivate(itemGrip, "positionOffset", Vector3.zero);

            // ── The artifact ──
            var artifact = root.AddComponent<SuckerPuncherArtifact>();
            SetPrivate(artifact, "steamBurst", steam);
            SetPrivate(artifact, "ringMaterial", ringMat);
            SetPrivateEnum(artifact, "useSoundId", "WeaponMeleeImpact");

            var shake = AssetDatabase.LoadAssetAtPath<ShakeData>(ShakePath);
            if (shake == null)
                Debug.LogWarning($"[SuckerPuncher] No shake at {ShakePath}; punches will not kick the camera.");
            else
                SetPrivate(artifact, "punchShake", shake);

            WireRam(artifact, root.transform, modelInstance.transform);

            return root;
        }

        /// <summary>
        /// Point the artifact at the three sliding objects, and tell it which way "forward" is in
        /// their parent's space.
        ///
        /// The axis is derived rather than typed. The parts live under the imported model, whose
        /// orientation is decided by the FBX importer — exactly the kind of constant that is right
        /// until someone changes an export flag and then wrong with no error. Asking the transform
        /// which way the prefab's own forward points cannot go stale.
        /// </summary>
        private static void WireRam(SuckerPuncherArtifact artifact, Transform root, Transform model)
        {
            var parts = new List<Transform>();
            foreach (string name in RamParts)
            {
                Transform part = FindDeep(model, name);
                if (part == null)
                {
                    Debug.LogError($"[SuckerPuncher] No '{name}' in the FBX. The ram will not move.");
                    continue;
                }
                parts.Add(part);
            }

            SetPrivate(artifact, "ramParts", parts.ToArray());

            if (parts.Count == 0) return;

            Transform parent = parts[0].parent != null ? parts[0].parent : model;
            SetPrivate(artifact, "ramAxis", parent.InverseTransformDirection(root.forward));

            // The three share one origin in the .blend, and the artifact's single-offset slide
            // depends on it. Cheap to check here, and the alternative is a fist that comes apart
            // mid-punch with nothing in the console.
            for (int i = 1; i < parts.Count; i++)
            {
                if ((parts[i].localPosition - parts[0].localPosition).sqrMagnitude <= 1e-6f) continue;

                Debug.LogWarning($"[SuckerPuncher] '{parts[i].name}' does not share the ram pivot " +
                                 $"with '{parts[0].name}' ({parts[i].localPosition} vs " +
                                 $"{parts[0].localPosition}). The ram will still slide, but the " +
                                 "parts were meant to be co-located — check sucker_puncher.py.");
            }
        }

        /// <summary>
        /// The steam the cylinder dumps when the ram fires. A burst rather than a rate: the
        /// gauntlet empties its cylinder in one instant, so everything it vents exists from frame
        /// one. World-space, so a plume already in the air keeps its drift as the arm swings.
        /// </summary>
        private static ParticleSystem BuildSteam(Transform parent)
        {
            var go = new GameObject("SteamBurst");
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(SteamHot);
            main.gravityModifier = -0.06f;   // steam rises
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 34) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 26f;
            shape.radius = 0.02f;

            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.4f));

            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Gradient(SteamHot, SteamCool));

            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 1.4f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var smoke = AssetDatabase.LoadAssetAtPath<Material>(SmokeMatPath);
            if (smoke != null) renderer.sharedMaterial = smoke;
            else Debug.LogWarning($"[SuckerPuncher] No smoke material at {SmokeMatPath}; " +
                                  "steam will render with the default particle material.");

            return ps;
        }

        private static Gradient Gradient(Color from, Color to)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                new[] { new GradientAlphaKey(from.a, 0f), new GradientAlphaKey(0.55f, 0.25f),
                        new GradientAlphaKey(0f, 1f) });
            return gradient;
        }

        /// <summary>
        /// The ring the shockwave draws, on the repulsor's shader but in the punch's own colour.
        ///
        /// A separate material rather than reusing RepulsorBlastRing.mat: the two weapons are meant
        /// to be told apart mid-fight, the repulsor's ring is cold blue energy and this one is hot
        /// steam and dust, and the shader is the part worth sharing.
        /// </summary>
        private static Material EnsureRingMaterial()
        {
            Shader shader = Shader.Find(ShockShader);
            if (shader == null)
            {
                Debug.LogError($"[SuckerPuncher] Shader '{ShockShader}' not found.");
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(RingMatPath) ?? ".");

            var material = AssetDatabase.LoadAssetAtPath<Material>(RingMatPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, RingMatPath);
            }

            material.shader = shader;
            material.SetColor("_Color", RingWarm);
            material.SetFloat("_Intensity", 2.4f);
            material.SetFloat("_TrailStrength", 0.5f);
            EditorUtility.SetDirty(material);
            return material;
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
                Debug.LogWarning($"[SuckerPuncher] No {markerName} in the FBX; {wantedName} left at origin.");
                return adopted.transform;
            }

            adopted.transform.localPosition = root.InverseTransformPoint(marker.position);
            marker.gameObject.SetActive(false);
            return adopted.transform;
        }

        /// <summary>Hide every marker nothing adopted, so no stray cube ships in the model.</summary>
        private static void HideRemainingMarkers(Transform model)
        {
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
                if (child.name.StartsWith("Marker_", StringComparison.Ordinal))
                    child.gameObject.SetActive(false);
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

            item.itemName = "Sucker Puncher";
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
            if (pickup == null) { Debug.LogError("[SuckerPuncher] PickupableItem missing."); return; }

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
            if (list == null) { Debug.LogError($"[SuckerPuncher] No list at {NetworkPrefabsPath}."); return; }
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
            if (type == null) { Debug.LogError($"[SuckerPuncher] No type {typeName}."); return; }
            go.AddComponent(type);
        }

        private static FieldInfo Field(object target, string name)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo info = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (info != null) return info;
            }

            Debug.LogError($"[SuckerPuncher] No field '{name}' on {target.GetType().Name}.");
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
                Debug.LogWarning($"[SuckerPuncher] '{enumValue}' is not a {field.FieldType.Name}; left at default.");
            }
        }
    }
}
