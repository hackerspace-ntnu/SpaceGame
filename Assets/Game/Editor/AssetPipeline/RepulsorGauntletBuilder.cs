using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FirstGearGames.SmoothCameraShaker;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Builds the Repulsor Gauntlet artifact: its blast-ring material, its two shake assets, its
    /// prefab, its <see cref="InventoryItem"/> asset, its entry in the network prefab list, and
    /// the <see cref="FlungBody"/> landing on the player prefab.
    ///
    /// <para>
    /// The model is a GREYBOX built from primitives — the real mesh is a deferred follow-up, and
    /// authoring the prefab now means the whole use flow can be proven before any art exists.
    /// </para>
    /// <para>
    /// It is re-runnable, and re-running it REPLACES the prefab wholesale. Anything hand-added in
    /// the inspector afterwards is destroyed by the next run, so tuning belongs in the numbers
    /// below rather than in the scene.
    /// </para>
    /// </summary>
    public static class RepulsorGauntletBuilder
    {
        private const string PrefabPath  = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/RepulsorGauntlet.prefab";
        private const string ItemPath    = "Assets/Game/Resources/Items/Artifacts/RepulsorGauntlet.asset";
        private const string RingMatPath = "Assets/Game/Art/Materials/Artifacts/RepulsorBlastRing.mat";
        private const string GlowMatPath = "Assets/Game/Art/Materials/Artifacts/RepulsorChargeGlow.mat";
        private const string ShockwaveShaderPath = "Assets/Game/Art/Shaders/Artifacts/RepulsorShockwave.shader";

        /// <summary>Charge-glow tint. Alpha is the additive strength, not an opacity.</summary>
        private static readonly Color GlowColor = new Color(0.45f, 0.85f, 1f, 1f);

        /// <summary>Both shakes start as copies of the shipped damage shake and are tuned in the Inspector.</summary>
        private const string ShakeSourcePath = "Assets/Game/ScriptableObjects/Shake/DamageShake.asset";
        private const string BlastShakePath  = "Assets/Game/ScriptableObjects/Shake/RepulsorBlastShake.asset";
        private const string FlungShakePath  = "Assets/Game/ScriptableObjects/Shake/RepulsorFlungShake.asset";

        /// <summary>
        /// The prefab this builder OPENS to reach the player. Its root is a nested instance of
        /// PlayerCharacter.prefab, so the component added below is added to that instance and
        /// <c>SaveAsPrefabAsset</c> writes it through to the base prefab; what stays here is the
        /// property override. See <see cref="WireFlungBodyIntoPlayer"/>.
        /// </summary>
        private const string PlayerPrefabPath =
            "Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab";
        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        /// <summary>The ground layer DropItemPhysics settles against, shared by every artifact.</summary>
        private const int GroundLayerMask = 128;

        [MenuItem("Tools/SpaceGame/Items/Build Repulsor Gauntlet")]
        public static void Build()
        {
            Material ringMat = EnsureRingMaterial();
            Material glowMat = EnsureGlowMaterial();
            ShakeData blastShake = EnsureShake(BlastShakePath);
            if (blastShake == null) return;

            GameObject root = BuildHierarchy(ringMat, glowMat, blastShake);

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? ".");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) { Debug.LogError("[RepulsorGauntlet] Prefab save failed."); return; }

            InventoryItem item = EnsureItem(prefab);
            WireItemIntoPickup(prefab, item);
            RegisterNetworkPrefab(prefab);
            WireFlungBodyIntoPlayer();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[RepulsorGauntlet] Built {PrefabPath} and {ItemPath}. " +
                      "Run Tools/Generate All Item Icons for the inventory icon.");
        }

        // ── Hierarchy ──────────────────────────────────────────────────────────

        private static GameObject BuildHierarchy(Material ringMat, Material glowMat, ShakeData blastShake)
        {
            var root = new GameObject("RepulsorGauntlet");

            // ── Greybox model ──
            // Primitives, not an FBX: the real gauntlet mesh is a deferred follow-up, and the
            // builder simply re-runs over this slot when it lands. A cylinder cuff the forearm
            // slides through, and a flat plate over the back of the hand. The colliders the
            // primitives arrive with would fight the root's own pickup sphere.
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);

            GameObject cuff = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cuff.name = "Cuff";
            cuff.transform.SetParent(model.transform, false);
            cuff.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // Long axis along +Z.
            cuff.transform.localScale = new Vector3(0.09f, 0.11f, 0.09f);
            UnityEngine.Object.DestroyImmediate(cuff.GetComponent<Collider>());

            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Plate";
            plate.transform.SetParent(model.transform, false);
            plate.transform.localPosition = new Vector3(0f, 0.02f, 0.13f);
            plate.transform.localScale = new Vector3(0.1f, 0.04f, 0.08f);
            UnityEngine.Object.DestroyImmediate(plate.GetComponent<Collider>());

            // ── Grip ──
            var grip = new GameObject("GripPoint");
            grip.transform.SetParent(root.transform, false);
            grip.transform.localPosition = new Vector3(0f, 0f, -0.05f);

            // ── Charge glow ──
            // The sphere the artifact scales up between chargeGlowScale.x and .y while the Use
            // button is held. Off until a press; the artifact turns it on.
            //
            // Its own flat additive material, NOT the ring's. The shockwave shader reads uv.y as
            // "across the annulus width" and sweeps it with _Progress; on a sphere that coordinate
            // is latitude and nothing animates _Progress, so the ring material renders the glow
            // bright at one pole and invisible at the other.
            GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.name = "ChargeGlow";
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0f, 0.12f);
            glow.transform.localScale = Vector3.one * 0.03f;
            UnityEngine.Object.DestroyImmediate(glow.GetComponent<Collider>());
            glow.GetComponent<MeshRenderer>().sharedMaterial = glowMat;
            glow.SetActive(false);

            // ── Pickup / world presence ──
            // Mirrors LightningSpell.prefab component for component: the same prefab is both the
            // thing in your hand and the thing lying in the sand.
            var netObject = root.AddComponent<NetworkObject>();
            netObject.SynchronizeTransform = true;

            SphereCollider sphere = root.AddComponent<SphereCollider>();
            sphere.radius = 0.14f;

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

            var itemGrip = root.AddComponent<ItemGrip>();
            SetPrivate(itemGrip, "gripPoint", grip.transform);
            SetPrivate(itemGrip, "holdSize", 0.3f);
            SetPrivate(itemGrip, "sizeReference", model.transform);

            // ── The artifact ──
            var artifact = root.AddComponent<RepulsorGauntletArtifact>();
            SetPrivate(artifact, "chargeGlow", glow.transform);
            SetPrivate(artifact, "ringMaterial", ringMat);
            SetPrivate(artifact, "blastShake", blastShake);

            return root;
        }

        // ── Materials and shakes ───────────────────────────────────────────────

        private static Material EnsureRingMaterial()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShockwaveShaderPath);
            if (shader == null)
            {
                Debug.LogError($"[RepulsorGauntlet] No shader at {ShockwaveShaderPath}; " +
                               "falling back to URP Unlit.");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(RingMatPath) ?? ".");

            var material = AssetDatabase.LoadAssetAtPath<Material>(RingMatPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, RingMatPath);
            }

            // Re-pointed on every run, not only at creation: a first build that ran before the
            // shockwave shader had compiled left the material on the URP Unlit fallback, and
            // without this the re-run that would fix it silently returns the broken one.
            material.shader = shader;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// The charge glow's own material: URP Unlit, additively blended, so a sphere that grows
        /// out of the gauntlet reads as light rather than as a painted ball. Deliberately plain —
        /// the glow's whole animation is its SCALE, driven by the artifact, so a material with
        /// swept parameters of its own would only fight it.
        /// </summary>
        private static Material EnsureGlowMaterial()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GlowMatPath) ?? ".");

            var material = AssetDatabase.LoadAssetAtPath<Material>(GlowMatPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, GlowMatPath);
            }

            material.shader = Shader.Find("Universal Render Pipeline/Unlit");
            material.SetColor("_BaseColor", GlowColor);

            // URP Unlit's transparency is not a shader variant you can pick by name — it is these
            // properties plus the keyword plus the queue, exactly as the material inspector writes
            // them. Setting the blend factors alone leaves the material opaque.
            material.SetFloat("_Surface", 1f);  // 0 opaque, 1 transparent
            material.SetFloat("_Blend", 2f);    // 0 alpha, 1 premultiply, 2 additive, 3 multiply
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(material);
            return material;
        }

        private static ShakeData EnsureShake(string path)
        {
            var shake = AssetDatabase.LoadAssetAtPath<ShakeData>(path);
            if (shake != null) return shake;

            if (!AssetDatabase.CopyAsset(ShakeSourcePath, path))
            {
                Debug.LogError($"[RepulsorGauntlet] Could not copy {ShakeSourcePath} to {path}.");
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<ShakeData>(path);
        }

        // ── The player's landing ───────────────────────────────────────────────

        /// <summary>
        /// Put <see cref="FlungBody"/> on the player prefab and hand it its landing shake.
        ///
        /// <para>
        /// A separate menu entry as well as part of the build, because the component lives on the
        /// PLAYER prefab rather than the gauntlet's — being flung is something that happens to the
        /// victim, whoever's gauntlet did it. Idempotent: a re-run finds the existing component.
        /// </para>
        /// <para>
        /// WHERE it lands is not where this path points. PlayerCharacterNetworked's root is a
        /// nested PlayerCharacter.prefab instance, so <c>AddComponent</c> on the loaded contents
        /// adds to that instance and <c>SaveAsPrefabAsset</c> writes the component itself into the
        /// BASE prefab, <c>Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab</c>. The
        /// networked prefab inherits it and keeps only the <c>flungShake</c> override.
        /// </para>
        /// <para>
        /// That is the outcome we want and it is left alone: every player prefab built on the base
        /// can be flung, rather than only the networked one. It is written down because the code
        /// reads as if it edits the file named here, and a reader who checks that file finds
        /// nothing but an override and concludes the wiring failed.
        /// </para>
        /// </summary>
        [MenuItem("Tools/SpaceGame/Items/Wire FlungBody Into Player")]
        public static void WireFlungBodyIntoPlayer()
        {
            ShakeData flungShake = EnsureShake(FlungShakePath);
            if (flungShake == null) return;

            GameObject playerRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                FlungBody flung = playerRoot.GetComponent<FlungBody>();
                if (flung == null) flung = playerRoot.AddComponent<FlungBody>();

                var so = new SerializedObject(flung);
                so.FindProperty("flungShake").objectReferenceValue = flungShake;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(playerRoot, PlayerPrefabPath);
                Debug.Log("[RepulsorGauntlet] FlungBody wired into the base PlayerCharacter.prefab " +
                          $"via {PlayerPrefabPath}, which keeps the flungShake override.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(playerRoot);
            }
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

            item.itemName = "Repulsor Gauntlet";
            item.itemPrefab = prefab;
            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>
        /// Point the prefab's pickup at its own item asset. Done after the prefab is saved,
        /// because the item asset references the saved prefab and the prefab references the item —
        /// one of the two links can only be made once both files exist.
        /// </summary>
        private static void WireItemIntoPickup(GameObject prefab, InventoryItem item)
        {
            Component pickup = prefab.GetComponents<Component>()
                .FirstOrDefault(c => c != null && c.GetType().FullName == "SpaceGame.Items.PickupableItem");
            if (pickup == null)
            {
                Debug.LogError("[RepulsorGauntlet] PickupableItem missing from the built prefab.");
                return;
            }

            var so = new SerializedObject(pickup);
            so.FindProperty("item").objectReferenceValue = item;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SavePrefabAsset(prefab);
        }

        /// <summary>
        /// Add the gauntlet to the list NetworkManager actually reads. Not
        /// <c>Assets/DefaultNetworkPrefabs.asset</c>, which regenerates itself and is not the list
        /// in use. An unregistered item prefab fails on CLIENTS ONLY — dropping one routes through
        /// World.Spawn — so solo playtesting cannot find this mistake.
        /// </summary>
        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null)
            {
                Debug.LogError($"[RepulsorGauntlet] No network prefab list at {NetworkPrefabsPath}.");
                return;
            }

            if (list.Contains(prefab)) return;

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
            Debug.Log("[RepulsorGauntlet] Registered as a network prefab.");
        }

        // ── Reflection helpers ─────────────────────────────────────────────────
        //
        // The item components keep their fields private and serialize them, which is right for
        // runtime code and simply means an editor script has to go in the same way the inspector
        // does. PickupableItem is additionally internal to Assembly-CSharp, so it cannot even be
        // named from this assembly — hence the type lookup rather than a typeof.

        private static void AddInternal(GameObject go, string typeName)
        {
            Type type = typeof(ItemGrip).Assembly.GetType(typeName);
            if (type == null)
            {
                Debug.LogError($"[RepulsorGauntlet] Type '{typeName}' not found.");
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

        private static FieldInfo Field(Component target, string name)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo info = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (info != null) return info;
            }

            Debug.LogError($"[RepulsorGauntlet] No field '{name}' on {target.GetType().Name}.");
            return null;
        }
    }
}
