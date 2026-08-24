using System;
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
    /// Builds the Gravel Blaster artifact: its prefab, its <see cref="InventoryItem"/> asset, its
    /// gravel-debris material, and its entry in the network prefab list.
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
        private const string ModelPath  = "Assets/Game/Art/Models/Weapons/GravelBlaster/gravel_blaster.fbx";
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/GravelBlaster.prefab";
        private const string ItemPath   = "Assets/Game/Resources/Items/Artifacts/GravelBlaster.asset";
        private const string MaterialDir = "Assets/Game/Art/Materials/Weapons";
        private const string DebrisMatPath = MaterialDir + "/GravelDebris.mat";
        private const string SparkMatPath  = MaterialDir + "/LaserSpark.mat";
        private const string SmokeMatPath  = MaterialDir + "/LaserSmoke.mat";
        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        /// <summary>The ground layer DropItemPhysics settles against, shared by every artifact.</summary>
        private const int GroundLayerMask = 128;

        /// <summary>Desert gravel: dry brown-grey rock, and the dust it throws.</summary>
        private static readonly Color Gravel     = new Color(0.42f, 0.36f, 0.29f);
        private static readonly Color DustLight  = new Color(0.76f, 0.68f, 0.52f);
        private static readonly Color DustDark   = new Color(0.48f, 0.42f, 0.33f);
        private static readonly Color SparkHot   = new Color(1.00f, 0.83f, 0.55f);
        private static readonly Color SparkCool  = new Color(1.00f, 0.52f, 0.18f);
        private static readonly Color FlashWarm  = new Color(1.00f, 0.72f, 0.38f);

        [MenuItem("Tools/Build Gravel Blaster Artifact")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[GravelBlaster] No model at {ModelPath}. Run gravel_blaster_export.py first.");
                return;
            }

            Material debrisMat = EnsureDebrisMaterial();
            var sparkMat = AssetDatabase.LoadAssetAtPath<Material>(SparkMatPath);
            var smokeMat = AssetDatabase.LoadAssetAtPath<Material>(SmokeMatPath);
            if (debrisMat == null || sparkMat == null || smokeMat == null)
            {
                // The spark and smoke materials belong to the laser staff's build and are reused
                // rather than duplicated — one grit-and-sparks look across the artifacts.
                Debug.LogError("[GravelBlaster] Missing material. Run Tools/Build Laser Staff Artifact first " +
                               "if LaserSpark/LaserSmoke are absent.");
                return;
            }

            GameObject root = BuildHierarchy(model, debrisMat, sparkMat, smokeMat);

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

        private static GameObject BuildHierarchy(GameObject model, Material debrisMat,
                                                 Material sparkMat, Material smokeMat)
        {
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

            // ── Muzzle effects ──
            ParticleSystem gravelBurst = BuildGravel(muzzle, debrisMat, "GravelBurst",
                                                     count: 90, minSpeed: 16f, maxSpeed: 30f,
                                                     cone: 9f);
            ParticleSystem muzzleDust = BuildDust(muzzle, smokeMat, "MuzzleDust", count: 26,
                                                  cone: 15f);
            ParticleSystem muzzleSparks = BuildSparks(muzzle, sparkMat, "MuzzleSparks", count: 42,
                                                      cone: 12f);

            var flashObject = new GameObject("MuzzleFlash");
            flashObject.transform.SetParent(muzzle, false);
            Light flash = flashObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = FlashWarm;
            flash.range = 7f;
            flash.intensity = 8f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;

            // ── Backfire rig: one parent system, played with its children ──
            // Slower, wider and dirtier than the muzzle blast: this one goes off in the holder's
            // face, and it has to read as the gun failing rather than as a second shot.
            ParticleSystem backfire = BuildGravel(breech.transform, debrisMat, "BackfireBurst",
                                                  count: 40, minSpeed: 6f, maxSpeed: 14f,
                                                  cone: 40f);
            BuildSparks(backfire.transform, sparkMat, "BackfireSparks", count: 60, cone: 55f);
            BuildDust(backfire.transform, smokeMat, "BackfireSmoke", count: 30, cone: 45f,
                      dark: true);

            // ── Pickup / world presence ──
            // Mirrors LightningSpell.prefab component for component: the same prefab is both the
            // thing in your hand and the thing lying in the sand.
            var netObject = root.AddComponent<NetworkObject>();
            netObject.SynchronizeTransform = true;

            SphereCollider sphere = root.AddComponent<SphereCollider>();
            sphere.radius = 0.16f;

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
            // Zero offsets, like the portal gun: the same Blender front (-Y) and export flags
            // land the same orientation in the hand. holdSize per the longarm bracket (0.9–1.2).
            var itemGrip = root.AddComponent<ItemGrip>();
            SetPrivate(itemGrip, "gripPoint", grip);
            SetPrivate(itemGrip, "holdSize", 1.05f);
            SetPrivate(itemGrip, "sizeReference", modelInstance.transform);

            // ── The artifact ──
            var artifact = root.AddComponent<GravelBlasterArtifact>();
            SetPrivate(artifact, "muzzle", muzzle);
            SetPrivate(artifact, "gravelBurst", gravelBurst);
            SetPrivate(artifact, "muzzleDust", muzzleDust);
            SetPrivate(artifact, "muzzleSparks", muzzleSparks);
            SetPrivate(artifact, "backfireBurst", backfire);
            SetPrivate(artifact, "muzzleFlash", flash);
            SetPrivateEnum(artifact, "useSoundId", "WeaponGunFire");

            return root;
        }

        // ── The emitters ───────────────────────────────────────────────────────
        //
        // All bursts, not rates: a spring gun empties both pipes in one instant, so everything it
        // throws exists from frame one. All world-space, so gravel already in the air keeps its
        // arc when the gun is swung away.

        /// <summary>Tumbling rock chunks: opaque cube-mesh particles that bounce off the world.</summary>
        private static ParticleSystem BuildGravel(Transform parent, Material material, string name,
                                                  short count, float minSpeed, float maxSpeed,
                                                  float cone)
        {
            ParticleSystem ps = NewSystem(parent, name);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.038f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
            main.maxParticles = 200;
            main.startColor = new ParticleSystem.MinMaxGradient(Gravel, Gravel * 0.7f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            Burst(ps, count);
            Cone(ps, cone, 0.03f);

            // The bounce is the detail that sells gravel as rock rather than as glowing VFX — it
            // is the one part of the blast that acknowledges the world it lands in.
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.bounce = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            collision.dampen = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
            collision.lifetimeLoss = 0.2f;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            ConfigureRenderer(renderer, material, ParticleSystemRenderMode.Mesh);
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            return ps;
        }

        /// <summary>Hot steel-and-powder sparks, reusing the laser staff's spark material.</summary>
        private static ParticleSystem BuildSparks(Transform parent, Material material, string name,
                                                  short count, float cone)
        {
            ParticleSystem ps = NewSystem(parent, name);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 22f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.045f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.maxParticles = 120;
            main.startColor = new ParticleSystem.MinMaxGradient(SparkHot, SparkCool);

            Burst(ps, count);
            Cone(ps, cone, 0.02f);

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (SparkHot, 0f), (SparkCool, 0.5f), (SparkCool, 1f) },
                new[] { (1f, 0f), (1f, 0.55f), (0f, 1f) }));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            ConfigureRenderer(renderer, material, ParticleSystemRenderMode.Stretch);
            renderer.velocityScale = 0.015f;
            renderer.lengthScale = 1.8f;
            return ps;
        }

        /// <summary>The powder cloud, reusing the laser staff's smoke material.</summary>
        private static ParticleSystem BuildDust(Transform parent, Material material, string name,
                                                short count, float cone, bool dark = false)
        {
            ParticleSystem ps = NewSystem(parent, name);

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.32f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.06f, 0.02f);
            main.maxParticles = 80;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = dark
                ? new ParticleSystem.MinMaxGradient(DustDark * 0.5f, DustDark)
                : new ParticleSystem.MinMaxGradient(DustDark, DustLight);

            Burst(ps, count);
            Cone(ps, cone, 0.05f);

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(Ramp(
                new[] { (Color.white, 0f), (Color.white, 1f) },
                new[] { (0f, 0f), (0.65f, 0.15f), (0f, 1f) }));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f), new Keyframe(1f, 1f)));

            ConfigureRenderer(ps.GetComponent<ParticleSystemRenderer>(), material,
                              ParticleSystemRenderMode.Billboard);
            return ps;
        }

        private static ParticleSystem NewSystem(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            return ps;
        }

        private static void Burst(ParticleSystem ps, short count)
        {
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
        }

        private static void Cone(ParticleSystem ps, float angle, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
        }

        private static void ConfigureRenderer(ParticleSystemRenderer renderer, Material material,
                                              ParticleSystemRenderMode mode)
        {
            renderer.sharedMaterial = material;
            renderer.renderMode = mode;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.sortMode = ParticleSystemSortMode.None;
        }

        private static Gradient Ramp((Color colour, float time)[] colours,
                                     (float alpha, float time)[] alphas)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                colours.Select(c => new GradientColorKey(c.colour, c.time)).ToArray(),
                alphas.Select(a => new GradientAlphaKey(a.alpha, a.time)).ToArray());
            return gradient;
        }

        /// <summary>Opaque lit rock for the cube-mesh chunks — the one material this build owns.</summary>
        private static Material EnsureDebrisMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
            {
                Debug.LogError("[GravelBlaster] URP Simple Lit shader not found.");
                return null;
            }

            Directory.CreateDirectory(MaterialDir);

            var material = AssetDatabase.LoadAssetAtPath<Material>(DebrisMatPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, DebrisMatPath);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", Gravel);
            material.SetFloat("_Smoothness", 0.05f);
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
