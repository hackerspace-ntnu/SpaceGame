using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Builds the Conjurer Staff artifact: the staff prefab lifted out of the creature's own model,
    /// its aim ring, a hand-sized copy of its charge effect, its <see cref="InventoryItem"/> asset,
    /// and its entry in the network prefab list.
    ///
    /// <para>
    /// Run this BEFORE <c>Tools/Creatures/Build Lightning Conjurer</c>. The creature's loot table
    /// points at the item asset this creates, and that builder can only warn about what is not
    /// there yet.
    /// </para>
    /// <para>
    /// It is re-runnable, and re-running REPLACES the prefabs wholesale — anything hand-added in the
    /// inspector afterwards is destroyed by the next run, so tuning belongs in the numbers below.
    /// </para>
    ///
    /// ---- the model is extracted, not authored -------------------------------------
    ///
    /// There is no standalone staff FBX and there should not be one. The staff already exists,
    /// drawn by <c>_Source~/staff.py</c> and baked into the creature's model as four meshes:
    /// <c>Staff_Shaft</c>, <c>Staff_Mount</c>, <c>Staff_Fan</c> and <c>Staff_Core</c>. Re-authoring
    /// them in Blender would give the player a staff that could drift away from the one the robot
    /// is holding, which is the whole promise of the drop.
    ///
    /// Lifting them out is possible because that FBX carries NO SKINNING at all — it is 52 rigid
    /// meshes bone-parented to the rig, so the staff parts are ordinary MeshFilter/MeshRenderer
    /// objects, not vertex weights inside somebody else's skinned mesh. They are copied by
    /// reference: the new prefab points at the same shared meshes and the same palette materials,
    /// so a re-export of the creature updates the loot for free.
    /// </summary>
    public static class ConjurerStaffBuilder
    {
        private const string ModelPath =
            "Assets/Game/Art/Models/Creatures/Robotic/LightningConjurer/LightningConjurer.fbx";

        private const string PrefabPath =
            "Assets/Game/Prefabs/Items/Artifacts/Gadgets/ConjurerStaff.prefab";

        /// <summary>Where the item asset lives. It MUST be under Resources/Items — RegistryLoader
        /// does Resources.LoadAll and nothing outside that folder is ever registered.</summary>
        public const string ItemPath = "Assets/Game/Resources/Items/Artifacts/ConjurerStaff.asset";

        private const string VfxDir = "Assets/Game/Prefabs/VisualEffects/Lightning";
        private const string RingPrefabPath = VfxDir + "/ConjurerStaffAimRing.prefab";
        private const string ChargeSourcePath = VfxDir + "/ConjurerStaffCharge.prefab";
        private const string ChargePrefabPath = VfxDir + "/ConjurerStaffChargeHandheld.prefab";
        private const string BoltPrefabPath = VfxDir + "/ConjurerLightningBolt.prefab";

        private const string MaterialDir = "Assets/Game/Art/Materials/Weapons";
        private const string RingMatPath = MaterialDir + "/ConjurerStaffAim.mat";

        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        /// <summary>The four meshes staff.py draws. Order is cosmetic; presence is not.</summary>
        public static readonly string[] StaffParts =
            { "Staff_Shaft", "Staff_Mount", "Staff_Fan", "Staff_Core" };

        /// <summary>
        /// How tall the staff stands once a person is holding it, in metres.
        ///
        /// The creature's is about nine, which is right for an eighteen-metre machine and absurd in
        /// a pair of hands. Everything else about the handheld version — the collider, the charge
        /// effect's turbine, the arc widths — is derived from the ratio between the two rather than
        /// typed, so this one number moves all of it together.
        /// </summary>
        private const float HandheldHeight = 1.9f;

        /// <summary>
        /// Where the fist closes on the shaft, as a fraction of the staff's height from the butt.
        ///
        /// staff.py MEASURES this off the carried arm rather than authoring it — the collar lands
        /// wherever the fist is, "a little over a third of the way up". This is that, and it is why
        /// the prefab's origin is the butt: a staff whose origin is on the floor lies down sensibly
        /// when it is dropped, and the grip is a child rather than the root.
        /// </summary>
        private const float GripFraction = 0.36f;

        /// <summary>The ground layer DropItemPhysics settles against, as every item prefab uses.</summary>
        private const int GroundLayerMask = 128;

        /// <summary>Cyan, matching Mat_Emissive_Portal_Blue on the staff's own emitter.</summary>
        private static readonly Color RingColour = new Color(0.24f, 0.55f, 1.00f);
        private static readonly Color RingCore = new Color(0.78f, 0.92f, 1.00f);
        private static readonly Color RingGlow = new Color(0.04f, 0.13f, 0.45f);

        [MenuItem("Tools/Build Conjurer Staff Artifact")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[ConjurerStaff] No creature model at {ModelPath}.");
                return;
            }

            GameObject ringPrefab = BuildAimRing();
            GameObject chargePrefab = BuildHandheldCharge();

            GameObject root = BuildHierarchy(model, ringPrefab, chargePrefab);
            if (root == null) return;

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? ".");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);

            if (prefab == null)
            {
                Debug.LogError("[ConjurerStaff] Prefab save failed.");
                return;
            }

            InventoryItem item = EnsureItem(prefab);
            WireItemIntoPickup(prefab, item);
            RegisterNetworkPrefab(prefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ConjurerStaff] Built {PrefabPath} and {ItemPath}. " +
                      "Run Tools/Generate All Item Icons for the inventory icon, then " +
                      "Tools/Creatures/Build Lightning Conjurer (prefab only) to make it drop.");
        }

        // ── The staff itself ───────────────────────────────────────────────────

        private static GameObject BuildHierarchy(GameObject model, GameObject ringPrefab,
                                                 GameObject chargePrefab)
        {
            var source = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (source == null)
            {
                Debug.LogError("[ConjurerStaff] Could not instantiate the creature model.");
                return null;
            }

            try
            {
                var parts = new List<Transform>();
                foreach (string partName in StaffParts)
                {
                    Transform found = FindDescendant(source.transform, partName);
                    if (found == null)
                    {
                        Debug.LogError($"[ConjurerStaff] '{partName}' is not in {ModelPath}. " +
                                       "Has the creature been re-exported without the staff?");
                        return null;
                    }

                    parts.Add(found);
                }

                // Which way is up, measured off the model rather than assumed.
                //
                // The creature stands in a rest pose with the staff carried vertical, so this comes
                // out as world +Y today — but a re-export that changed the rest pose or the axis
                // conversion would silently lay the staff on its side, and a staff held sideways is
                // the kind of wrong that looks like a rigging bug rather than a build one.
                Bounds staffBounds = WorldBounds(parts);
                Vector3 axis = LongestAxis(staffBounds);

                Transform core = parts[Array.IndexOf(StaffParts, "Staff_Core")];
                if (Vector3.Dot(core.position - staffBounds.center, axis) < 0f) axis = -axis;

                // Maps the staff's own long axis onto +Y. Identity when it already is, which is the
                // expected case — this is a guard, not a correction anyone is relying on.
                Quaternion straighten = Quaternion.FromToRotation(axis, Vector3.up);

                float height = Mathf.Max(0.01f, Vector3.Dot(staffBounds.size, Abs(axis)));
                float scale = HandheldHeight / height;

                // The butt of the staff, which becomes the prefab's origin.
                Vector3 butt = staffBounds.center - axis * (height * 0.5f);

                var root = new GameObject("ConjurerStaff");

                var modelRoot = new GameObject("Model");
                modelRoot.transform.SetParent(root.transform, false);
                modelRoot.transform.localScale = Vector3.one * scale;

                foreach (Transform part in parts) CopyPart(part, modelRoot.transform, butt, straighten);

                // ── Grip and tip ──
                // Both in the scaled staff's own frame, so they follow HandheldHeight without
                // anybody having to remember to move them.
                var grip = new GameObject("Grip");
                grip.transform.SetParent(root.transform, false);
                grip.transform.localPosition = Vector3.up * (HandheldHeight * GripFraction);

                var tip = new GameObject("Tip");
                tip.transform.SetParent(root.transform, false);
                tip.transform.localPosition =
                    straighten * (core.position - butt) * scale;

                // ── Pickup / world presence ──
                // Mirrors LightningSpell.prefab and LaserStaff.prefab component for component: the
                // same prefab is both the thing in your hand and the thing lying in the sand.
                var netObject = root.AddComponent<NetworkObject>();
                netObject.SynchronizeTransform = true;

                // A capsule rather than the sphere the smaller artifacts use. This one is nearly two
                // metres of stick: a sphere at its origin would make the whole staff un-clickable
                // except at the very bottom, and a dropped staff you cannot point at is a dropped
                // staff you cannot pick up.
                CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
                capsule.direction = 1;                       // Y
                capsule.height = HandheldHeight;
                capsule.radius = HandheldHeight * 0.09f;
                capsule.center = Vector3.up * (HandheldHeight * 0.5f);

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

                // ── Grip pose ──
                var itemGrip = root.AddComponent<ItemGrip>();
                SetPrivate(itemGrip, "gripPoint", grip.transform);
                SetPrivate(itemGrip, "sizeReference", modelRoot.transform);
                SetPrivate(itemGrip, "holdSize", HandheldHeight);

                // Zero rotation, and that is the answer rather than a placeholder: the staff's long
                // axis was just mapped onto +Y above, and ItemGrip's zero pose already means "+Y out
                // the thumb side, as a torch's flame would" — a staff standing out of the top of the
                // fist, which is how the creature carries it.
                SetPrivate(itemGrip, "rotationOffset", Vector3.zero);
                SetPrivate(itemGrip, "positionOffset", Vector3.zero);

                // ── The artifact ──
                var artifact = root.AddComponent<ConjurerStaffArtifact>();
                SetPrivate(artifact, "tip", tip.transform);
                if (ringPrefab != null)
                    SetPrivate(artifact, "aimRingPrefab", ringPrefab.GetComponent<GroundRing>());
                if (chargePrefab != null)
                    SetPrivate(artifact, "chargePrefab", chargePrefab);

                var bolt = AssetDatabase.LoadAssetAtPath<GameObject>(BoltPrefabPath);
                if (bolt == null)
                    Debug.LogWarning($"[ConjurerStaff] No bolt prefab at {BoltPrefabPath}; " +
                                     "the strike will damage without being drawn.");
                else
                    SetPrivate(artifact, "boltPrefab", bolt);

                // The wind-up's report. Not a loop: Sfx has no way to stop one, and the FMOD project
                // is lost so nothing new can be authored anyway.
                SetPrivateEnum(artifact, "useSoundId", "WeaponBallLightningChargeLoop");

                return root;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        /// Rebuild one staff mesh as a plain child of the new prefab.
        ///
        /// A fresh GameObject pointing at the SAME shared mesh and materials, rather than a copy of
        /// the imported object — copying would drag the bone-parent relationship and the rig's own
        /// transform chain along with it, and the staff would arrive wearing the creature's pose.
        private static void CopyPart(Transform part, Transform parent, Vector3 origin,
                                     Quaternion straighten)
        {
            var filter = part.GetComponent<MeshFilter>();
            var renderer = part.GetComponent<MeshRenderer>();
            if (filter == null || renderer == null) return;

            var go = new GameObject(part.name);
            go.transform.SetParent(parent, false);

            // The imported part's world pose, re-expressed against the staff's butt and straightened
            // onto +Y. Position is scaled by the parent's own scale rather than here, so the whole
            // model shrinks as one rigid object.
            go.transform.localPosition = straighten * (part.position - origin);
            go.transform.localRotation = straighten * part.rotation;
            go.transform.localScale = part.lossyScale;

            go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;

            var copy = go.AddComponent<MeshRenderer>();
            copy.sharedMaterials = renderer.sharedMaterials;
            copy.shadowCastingMode = renderer.shadowCastingMode;
            copy.receiveShadows = renderer.receiveShadows;
        }

        // ── The aim ring ───────────────────────────────────────────────────────

        /// <summary>
        /// The ring that marks where the bolt will land — a bare <see cref="GroundRing"/>, since
        /// that component builds and rewrites its own mesh every time the ground under it changes.
        /// </summary>
        private static GameObject BuildAimRing()
        {
            Material material = EnsureRingMaterial();
            if (material == null) return null;

            var root = new GameObject("ConjurerStaffAimRing");

            root.AddComponent<MeshFilter>();

            MeshRenderer renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(root.transform, false);
            Light glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = RingColour;
            glow.range = 12f;
            glow.intensity = 1.2f;
            glow.shadows = LightShadows.None;   // a couple of seconds at a time, on a moving marker

            var ring = root.AddComponent<GroundRing>();
            SetPrivate(ring, "glow", glow);
            SetPrivate(ring, "trackingColour", RingColour);

            Directory.CreateDirectory(VfxDir);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, RingPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);

            if (saved == null) Debug.LogError($"[ConjurerStaff] Could not save {RingPrefabPath}.");
            return saved;
        }

        /// <summary>
        /// The ring's material: SpaceGame/LightningBeam, the same shader the creature's own bolts
        /// and arcs wear, so the marker reads as belonging to the same weapon.
        ///
        /// <para>
        /// The two ends are switched OFF, which is the one thing that has to be different. That
        /// shader tapers to nothing at u = 0 and flares at u = 1 — a muzzle and an impact, right on
        /// a bolt that has two ends. A ring has neither, and left on they would burn a dark notch
        /// and a hot spot into the same arbitrary azimuth forever.
        /// </para>
        /// </summary>
        private static Material EnsureRingMaterial()
        {
            Shader shader = Shader.Find("SpaceGame/LightningBeam");
            if (shader == null)
            {
                Debug.LogError("[ConjurerStaff] Shader 'SpaceGame/LightningBeam' not found.");
                return null;
            }

            Directory.CreateDirectory(MaterialDir);

            var material = AssetDatabase.LoadAssetAtPath<Material>(RingMatPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, RingMatPath);
            }

            material.shader = shader;
            material.SetColor("_CoreColor", RingCore);
            material.SetColor("_BoltColor", RingColour);
            material.SetColor("_GlowColor", RingGlow);

            material.SetFloat("_MuzzleTaper", 0f);
            material.SetFloat("_TipFlare", 0f);

            // A fatter, softer filament than a bolt's. The band is only a few centimetres wide on
            // screen at any distance worth aiming from, and a needle-thin core inside it disappears.
            material.SetFloat("_CoreWidth", 0.30f);
            material.SetFloat("_CoreSharpness", 2.0f);
            material.SetFloat("_GlowFalloff", 0.80f);
            material.SetFloat("_Intensity", 3.2f);

            // Slower and shallower than a strike. This is a marker the player reads while aiming,
            // not a discharge — at the bolt's rates it strobes and becomes hard to look at.
            material.SetFloat("_CrackleScale", 1.4f);
            material.SetFloat("_CrackleSpeed", 6f);
            material.SetFloat("_CrackleDepth", 0.35f);
            material.SetFloat("_StrikeRate", 8f);
            material.SetFloat("_StrikeDepth", 0.15f);

            EditorUtility.SetDirty(material);
            return material;
        }

        // ── The charge, at hand size ───────────────────────────────────────────

        /// <summary>
        /// A copy of the creature's staff charge, sized for a staff a fifth as tall.
        ///
        /// <para>
        /// A copy rather than a rescaled instance, because <see cref="ConjurerStaffCharge"/> works
        /// in world METRES: its arcs are <see cref="LightningBoltEffect"/>s whose LineRenderers are
        /// <c>useWorldSpace</c>, so parenting the effect to a shrunken staff moves it without
        /// shrinking it, and the player gets a two-metre turbine of lightning around a wrist.
        /// </para>
        /// <para>
        /// Derived from the source prefab rather than rebuilt beside it, so a change to the
        /// creature's charge — another arc, a different colour — reaches this one on the next run
        /// instead of quietly leaving the two effects to diverge.
        /// </para>
        /// </summary>
        private static GameObject BuildHandheldCharge()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ChargeSourcePath);
            if (source == null)
            {
                Debug.LogWarning($"[ConjurerStaff] No charge effect at {ChargeSourcePath}; the " +
                                 "staff will wind up without one. Run Tools/Creatures/Build " +
                                 "Lightning Conjurer to generate it.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstanceCompletely(instance, InteractionMode.AutomatedAction);
            instance.name = "ConjurerStaffChargeHandheld";

            var charge = instance.GetComponent<ConjurerStaffCharge>();
            if (charge == null)
            {
                Debug.LogError($"[ConjurerStaff] {ChargeSourcePath} has no ConjurerStaffCharge.");
                UnityEngine.Object.DestroyImmediate(instance);
                return null;
            }

            // The creature's staff against this one. Read off the source effect's own turbine rather
            // than re-measuring the model: whatever the creature's charge thinks its fan is, this is
            // that at hand size.
            float sourceFan = (float)(Field(charge, "fanRadius")?.GetValue(charge) ?? 1.2f);
            float ratio = Mathf.Clamp(HandheldHeight / 9f, 0.05f, 1f);

            SetPrivate(charge, "fanRadius", sourceFan * ratio);
            SetPrivate(charge, "fanDrop",
                       (float)(Field(charge, "fanDrop")?.GetValue(charge) ?? 1.4f) * ratio);
            SetPrivate(charge, "skyReach",
                       (float)(Field(charge, "skyReach")?.GetValue(charge) ?? 14f) * ratio);

            // Must match the artifact's castSeconds, or the charge peaks before or after the bolt.
            SetPrivate(charge, "chargeSeconds", 1.2f);

            // The arcs themselves are ribbons with a width in metres for the same reason.
            foreach (LightningBoltEffect arc in instance.GetComponentsInChildren<LightningBoltEffect>(true))
            {
                ScalePrivateFloat(arc, "startWidth", ratio);
                ScalePrivateFloat(arc, "endWidth", ratio);
                ScalePrivateFloat(arc, "maxOffset", ratio);
            }

            foreach (Light light in instance.GetComponentsInChildren<Light>(true))
            {
                light.range *= ratio;
                light.shadows = LightShadows.None;
            }

            Directory.CreateDirectory(VfxDir);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, ChargePrefabPath);
            UnityEngine.Object.DestroyImmediate(instance);

            if (saved == null) Debug.LogError($"[ConjurerStaff] Could not save {ChargePrefabPath}.");
            return saved;
        }

        // ── Geometry helpers ───────────────────────────────────────────────────

        private static Bounds WorldBounds(IEnumerable<Transform> parts)
        {
            bool started = false;
            var bounds = new Bounds();

            foreach (Transform part in parts)
            {
                var renderer = part.GetComponent<Renderer>();
                if (renderer == null) continue;

                if (!started) { bounds = renderer.bounds; started = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            return started ? bounds : new Bounds(Vector3.zero, Vector3.one);
        }

        /// <summary>The world axis the bounds are longest along — the staff's own length.</summary>
        private static Vector3 LongestAxis(Bounds bounds)
        {
            Vector3 size = bounds.size;

            if (size.y >= size.x && size.y >= size.z) return Vector3.up;
            return size.x >= size.z ? Vector3.right : Vector3.forward;
        }

        private static Vector3 Abs(Vector3 v) =>
            new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private static Transform FindDescendant(Transform root, string childName)
        {
            if (root.name == childName) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDescendant(root.GetChild(i), childName);
                if (found != null) return found;
            }

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

            item.itemName = "Conjurer Staff";
            item.itemPrefab = prefab;
            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>
        /// Point the prefab's pickup at its own item asset. After the save, because the two files
        /// reference each other and neither link can be made until both exist.
        /// </summary>
        private static void WireItemIntoPickup(GameObject prefab, InventoryItem item)
        {
            Component pickup = prefab.GetComponents<Component>()
                .FirstOrDefault(c => c != null &&
                                     c.GetType().FullName == "SpaceGame.Items.PickupableItem");

            if (pickup == null)
            {
                Debug.LogError("[ConjurerStaff] PickupableItem missing from the built prefab.");
                return;
            }

            var so = new SerializedObject(pickup);
            so.FindProperty("item").objectReferenceValue = item;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SavePrefabAsset(prefab);
        }

        /// <summary>
        /// Add the staff to the list NetworkManager actually reads. Dropping one routes through
        /// World.Spawn, so an unregistered item prefab fails on CLIENTS ONLY — which is exactly the
        /// mistake solo playtesting cannot find.
        /// </summary>
        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null)
            {
                Debug.LogError($"[ConjurerStaff] No network prefab list at {NetworkPrefabsPath}.");
                return;
            }

            if (list.Contains(prefab))
            {
                Debug.Log("[ConjurerStaff] Already registered as a network prefab.");
                return;
            }

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
            Debug.Log("[ConjurerStaff] Registered as a network prefab.");
        }

        // ── Reflection helpers ─────────────────────────────────────────────────
        //
        // The item and effect components keep their fields private and serialize them, which is
        // right for runtime code and simply means an editor script goes in the way the inspector
        // does. PickupableItem is additionally internal to Assembly-CSharp, so it cannot be named
        // from this assembly at all — hence the type lookup rather than a typeof.

        private static void AddInternal(GameObject go, string typeName)
        {
            Type type = typeof(ItemGrip).Assembly.GetType(typeName);
            if (type == null)
            {
                Debug.LogError($"[ConjurerStaff] Type '{typeName}' not found.");
                return;
            }

            go.AddComponent(type);
        }

        private static void SetPrivate(Component target, string field, object value)
        {
            FieldInfo info = Field(target, field);
            info?.SetValue(target, value);
        }

        private static void SetPrivateLayerMask(Component target, string field, int mask)
        {
            FieldInfo info = Field(target, field);
            info?.SetValue(target, (LayerMask)mask);
        }

        private static void SetPrivateEnum(Component target, string field, string valueName)
        {
            FieldInfo info = Field(target, field);
            if (info == null) return;

            try { info.SetValue(target, Enum.Parse(info.FieldType, valueName)); }
            catch (ArgumentException)
            {
                Debug.LogWarning($"[ConjurerStaff] '{valueName}' is not a {info.FieldType.Name}; " +
                                 "left at its default.");
            }
        }

        private static void ScalePrivateFloat(Component target, string field, float factor)
        {
            FieldInfo info = Field(target, field);
            if (info == null || info.FieldType != typeof(float)) return;

            info.SetValue(target, (float)info.GetValue(target) * factor);
        }

        private static FieldInfo Field(Component target, string name)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo info = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (info != null) return info;
            }

            Debug.LogError($"[ConjurerStaff] No field '{name}' on {target.GetType().Name}.");
            return null;
        }
    }
}
