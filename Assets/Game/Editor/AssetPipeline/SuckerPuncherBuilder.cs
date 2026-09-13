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
        private const string LogTag     = "SuckerPuncher";

        /// <summary>
        /// How far the ram slides, in metres. This is not a feel number — it is the model's, and
        /// <c>gauntlet_puncher.py</c> derives and asserts it: the sled has to stay on the base's
        /// rails, which are 0.240 m long and belong to <c>gauntlet_base.blend</c> rather than to
        /// the device, and the rod's piston has to stay inside its gland at full extension. Set it
        /// longer than the .blend allows and the fist walks off the end of its own track.
        /// </summary>
        private const float RamStroke = 0.168f;
        private const string ModelPath  = "Assets/Game/Art/Models/Items/gauntlet_puncher.fbx";
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/SuckerPuncher.prefab";
        private const string ItemPath   = "Assets/Game/Resources/Items/Artifacts/SuckerPuncher.asset";
        private const string RingMatPath = "Assets/Game/Art/Materials/Artifacts/SuckerPuncherShockRing.mat";
        private const string SmokeMatPath = "Assets/Game/Art/Materials/Weapons/LaserSmoke.mat";
        private const string ShakePath  = "Assets/Game/ScriptableObjects/Shake/RepulsorBlastShake.asset";
        private const string ShockShader = "SpaceGame/Artifacts/RepulsorShockwave";
        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

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
                               "Run models/gear/gauntlet_puncher_export.py first.");
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

            // Built on the shared gauntlet base since 2026-09-02, in the family frame: origin at
            // the wrist joint, the arm down the model's -Z, the fist out past the hand on +Z. It
            // goes on at identity and the markers below carry every landmark.
            Transform grip = GauntletPrefab.AdoptMarker(root.transform, modelInstance.transform,
                                                        "Marker_Grip", "GripPoint", LogTag);
            Transform vent = GauntletPrefab.AdoptMarker(root.transform, modelInstance.transform,
                                                        "Marker_Vent", "Vent", LogTag);

            // Anything else the .blend ships as a marker is a build helper, not a socket, and
            // leaving it visible would float a speck in the model.
            GauntletPrefab.HideRemainingMarkers(modelInstance.transform);

            // Steam is vented backwards and up, away from the punch — a plume that followed the
            // fist would sit in front of the thing the player is trying to look at.
            vent.localRotation = Quaternion.LookRotation(new Vector3(0f, 0.55f, -1f).normalized);
            ParticleSystem steam = BuildSteam(vent);

            // ── Pickup / world presence ──
            // Mirrors LightningSpell.prefab component for component: the same prefab is both the
            // thing on your arm and the thing lying in the sand.
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
            // Worn on the forearm like every other gauntlet, and authored at the suit's true size,
            // so the whole family answers this the same way — see GauntletPrefab.
            //
            // It used to carry holdSize 0.674 and a -90 rotationOffset. Both belonged to the era
            // when the puncher was seated in the HAND frame: the size pinned a hand cavity
            // measured against the rig, and the quarter turn put the guard plate on the back of
            // the hand rather than out past the thumb. On the forearm the model's own axes ARE
            // the frame, so a rotation offset is a tilt nobody asked for, and the size is the
            // wearer's arm.
            GauntletPrefab.MakeWorn(root, grip, modelInstance.transform);

            // ── The artifact ──
            var artifact = root.AddComponent<SuckerPuncherArtifact>();
            SetPrivate(artifact, "steamBurst", steam);
            SetPrivate(artifact, "ringMaterial", ringMat);
            SetPrivate(artifact, "ramThrow", RamStroke);
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
                Transform part = GauntletPrefab.FindDeep(model, name);
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
