// Builds the net gun from Assets/Game/Art/Models/Items/net_gun.fbx:
//
//   Assets/Game/Art/Materials/Items/Net_Cord.mat               the cord every flying net is drawn with
//   Assets/Game/Prefabs/Items/Artifacts/Gadgets/NetGun.prefab  the gun, in the hand and in the sand
//   Assets/Game/Resources/Items/Artifacts/NetGun.asset         the InventoryItem
//
// plus the prefab's entry in the network prefab list the NetworkManager actually reads.
//
// A script rather than hand-authored YAML because the prefab nests an imported FBX, and the file
// ids Unity assigns inside a model are decided at import time — a hand-written prefab referencing
// guessed ids loads with a missing model and no error.
//
// Re-runnable, and re-running REPLACES the prefab wholesale. Every tunable therefore belongs in the
// constants below rather than in the Inspector, or the next run quietly undoes it.
//
// The NETS are not built here and must never become a prefab. SnareCatch is presentation: every
// machine constructs its own from the shot's seed, so a NetworkObject on one would spawn a second
// net on the host and fail NetworkPrefabRegistrationTests.
//
// Re-run from: Tools ▸ Items ▸ Build Net Gun
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public static class NetGunBuilder
    {
        private const string ModelPath = "Assets/Game/Art/Models/Items/net_gun.fbx";
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/NetGun.prefab";
        private const string ItemAssetPath = "Assets/Game/Resources/Items/Artifacts/NetGun.asset";
        private const string CordMaterialPath = "Assets/Game/Art/Materials/Items/Net_Cord.mat";
        private const string AlbedoPath = "Assets/Game/Art/Textures/Items/rope_braid_albedo.png";
        private const string NormalPath = "Assets/Game/Art/Textures/Items/rope_braid_normal.png";

        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        /// <summary>The ground layer DropItemPhysics settles against, shared by every artifact.</summary>
        private const int GroundLayerMask = 128;

        /// <summary>
        /// Nets in the canister.
        ///
        /// <para>
        /// <b>This has to be authored.</b> <c>UsableItem.maxUses</c> defaults to -1, which means
        /// UNLIMITED, and an unlimited item reports <c>ChargesLeft == -1</c> — the first thing
        /// <c>NetGunArtifact.TickRecharge</c> returns on. So the default is not "three nets with no
        /// limit yet", it is infinite nets with a dead recharge clock, and nothing logs a word about
        /// it. <c>NetGunWiringTests</c> holds this at 3.
        /// </para>
        /// </summary>
        private const int MaxUses = 3;

        /// <summary>
        /// Metres along the gun's longest axis once it is in the hand — the <c>Gun</c> bracket of
        /// <see cref="ItemScaleLadder"/>, shared with Gun.prefab, the PortalGun and the GravelBlaster.
        ///
        /// <para>
        /// Not the size the model was built at. <c>EquipItemSocket.Seat</c> rescales a held item so
        /// its longest axis measures this, so the FBX's own 0.629 m is a modelling convention that
        /// never reaches the hand. <c>packSize</c> is deliberately left at 0: guns stay at the anchor
        /// on the pack mat too, because big gear goes on the rack with overhang.
        /// </para>
        /// </summary>
        private const float HoldSize = 1.25f;

        /// <summary>
        /// Layers <c>SnareReceiver</c>'s landing query looks in. Default only.
        ///
        /// <para>
        /// Every player, creature and vehicle collider in this project sits on Default; terrain is
        /// on Ground and stowed gear on PackItem. <c>~0</c> would still behave — <c>SnareCatch.Capture</c>
        /// refuses anything that is neither a player nor a creature however wide the mask is — but it
        /// hands the OverlapBox every terrain collider under a six-metre net for nothing.
        /// </para>
        /// </summary>
        private const int CatchableLayers = 1 << 0;

        /// <summary>Pickup volume, matching the other gun-sized artifacts.</summary>
        private const float PickupRadius = 0.16f;

        private const string CordShaderName = "Universal Render Pipeline/Lit";

        /// <summary>
        /// Braid repeats along one cord segment.
        ///
        /// <para>
        /// <c>SnareMesh</c> gives every strand segment the whole 0..1 UV with u running along the
        /// cord, so any repeat has to come from the material. A segment is one span of a 15-node
        /// lattice across a 6 m net — 0.43 m — against a 0.028 m cord and a 256x64 braid tile. Four
        /// repeats put the braid back at roughly the aspect it was drawn at instead of smearing one
        /// braid over half a metre of rope.
        /// </para>
        /// </summary>
        private static readonly Vector2 CordTiling = new Vector2(4f, 1f);

        /// <summary>Dry hemp cord. Matched to Rope_Leash, which is the same braid on the same texture.</summary>
        private const float CordSmoothness = 0.12f;

        [MenuItem("Tools/Items/Build Net Gun")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[NetGun] No model at {ModelPath}. Run " +
                               "_Source~/components/props/net_gun_export.py first.");
                return;
            }

            Material cord = EnsureCordMaterial();
            if (cord == null) return;

            GameObject root = BuildHierarchy(model, cord);
            if (root == null) return;

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? ".");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            if (prefab == null) { Debug.LogError("[NetGun] Prefab save failed."); return; }

            InventoryItem asset = EnsureItemAsset(prefab);
            WireItemIntoPickup(prefab, asset);
            RegisterNetworkPrefab(prefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // A NetworkObject added by script ships GlobalObjectIdHash 0, and NGO silently drops all
            // but one prefab when several share a hash. The hash is filled in by the component's own
            // OnValidate, which only resolves against the saved ASSET — so the prefab has to be
            // re-imported and then reserialized, or the corrected value never reaches the YAML.
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ForceReserializeAssets(new[] { PrefabPath });
            AssetDatabase.Refresh();

            if (!Verify()) return;

            Debug.Log($"[NetGun] Built {CordMaterialPath}, {PrefabPath} and {ItemAssetPath}. " +
                      "Run Tools/Generate All Item Icons for the inventory icon.");
        }

        // ─────────────────────────── The gun ───────────────────────────

        private static GameObject BuildHierarchy(GameObject model, Material cord)
        {
            var root = new GameObject("NetGun");

            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.name = "Model";
            modelInstance.transform.SetParent(root.transform, false);

            Dictionary<string, Transform> parts = PartsOf(root);
            if (!VerifyModel(parts))
            {
                Object.DestroyImmediate(root);
                return null;
            }

            Transform bundle = parts["Mesh_NetGun_Bundle"];

            // The markers carry a POSITION and nothing else: a Blender empty exports with whatever
            // rotation it had in the file, and the FBX axis conversion then turns the gun's own up
            // into the marker's forward. Both are adopted as plain children of the root so the
            // wiring points at transforms this prefab owns, and the originals are switched off —
            // ItemBounds skips a part that is disabled within the item, so a 4 mm marker cube can
            // never quietly decide how large the gun is held. They are kept rather than deleted so
            // a re-export from Blender still reaches this prefab.
            Transform muzzle = AdoptMarker(root.transform, parts, "Marker_Muzzle", "Muzzle");
            Transform grip = AdoptMarker(root.transform, parts, "Marker_Grip", "GripPoint");

            // NetGunArtifact fires along muzzle.forward, so the heading is measured rather than
            // typed: the bore runs from the middle of the gun out to the rim, and by the export's
            // own convention (front is Blender -Y) that lands on one of the prefab's own axes.
            Vector3 bore = BoreAxis(root, muzzle);
            if (bore == Vector3.zero) { Object.DestroyImmediate(root); return null; }
            muzzle.localRotation = Quaternion.LookRotation(bore);

            // ── Pickup / world presence ──
            // The same prefab is the thing in the hand and the thing lying in the sand, so it
            // carries both sets of components, component for component with the other artifacts.
            NetworkObject netObject = root.AddComponent<NetworkObject>();
            netObject.SynchronizeTransform = true;

            SphereCollider sphere = root.AddComponent<SphereCollider>();
            sphere.radius = PickupRadius;

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = true;

            AddByName(root, "SpaceGame.Items.PickupableItem");

            DropItemPhysics drop = root.AddComponent<DropItemPhysics>();
            var dropSo = new SerializedObject(drop);
            Field.Set(dropSo, "rb", body);
            Field.SetInt(dropSo, "groundLayer", GroundLayerMask);
            dropSo.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<NetRelay>();
            root.AddComponent<SaveableEntity>();
            root.AddComponent<TransformSaveable>();

            // ── Grip ──
            // Zero offsets, like the portal gun and the gravel blaster: the same Blender front (-Y)
            // and the same export flags land the same orientation in the hand.
            ItemGrip itemGrip = root.AddComponent<ItemGrip>();
            var gripSo = new SerializedObject(itemGrip);
            Field.Set(gripSo, "gripPoint", grip);
            Field.SetFloat(gripSo, "holdSize", HoldSize);
            Field.SetFloat(gripSo, "packSize", 0f);
            Field.Set(gripSo, "sizeReference", modelInstance.transform);
            gripSo.ApplyModifiedPropertiesWithoutUndo();

            // ── The artifact ──
            NetGunArtifact artifact = root.AddComponent<NetGunArtifact>();
            var artifactSo = new SerializedObject(artifact);
            Field.Set(artifactSo, "muzzle", muzzle);
            Field.Set(artifactSo, "netMaterial", cord);
            Field.Set(artifactSo, "loadedBundle", bundle.gameObject);
            Field.SetInt(artifactSo, "catchableLayers", CatchableLayers);
            Field.SetInt(artifactSo, "maxUses", MaxUses);
            artifactSo.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        /// <summary>
        /// Move a marker's position onto a plain child of the root and switch the marker off.
        /// </summary>
        private static Transform AdoptMarker(Transform root, Dictionary<string, Transform> parts,
                                             string markerName, string wantedName)
        {
            Transform marker = parts[markerName];

            var adopted = new GameObject(wantedName).transform;
            adopted.SetParent(root, false);
            adopted.localPosition = root.InverseTransformPoint(marker.position);

            marker.gameObject.SetActive(false);
            return adopted;
        }

        /// <summary>
        /// Which way the gun points, as one of the prefab's own axes, measured from the middle of
        /// the model out to the bore rim. Returns zero — having said why — when the model did not
        /// arrive along the axis the rest of this builder and the hold pose both assume.
        /// </summary>
        private static Vector3 BoreAxis(GameObject root, Transform muzzle)
        {
            Bounds bounds = ItemBounds.Measure(root, null);
            Vector3 outward = muzzle.localPosition - bounds.center;

            Vector3 axis = Mathf.Abs(outward.z) >= Mathf.Abs(outward.x)
                           && Mathf.Abs(outward.z) >= Mathf.Abs(outward.y)
                ? new Vector3(0f, 0f, Mathf.Sign(outward.z))
                : Vector3.zero;

            if (axis != Vector3.forward)
            {
                Debug.LogError($"[NetGun] The model did not arrive pointing along +Z — the bore " +
                               $"runs {outward.x:F3}, {outward.y:F3}, {outward.z:F3} from the " +
                               "middle of the gun. The FBX axis conversion has changed; fix " +
                               "net_gun_export.py rather than rotating it here.");
                return Vector3.zero;
            }

            return axis;
        }

        // ─────────────────────────── The cord ───────────────────────────

        /// <summary>
        /// The material every flying net is drawn with. Two-sided, because a draped net is seen from
        /// underneath every time it lands over something.
        ///
        /// <para>
        /// Every value the look depends on is written on every run. A <c>.mat</c> freezes the shader
        /// defaults it was created against, so a tunable left unset is whichever default URP happened
        /// to ship the day the file was born rather than the one being read here.
        /// </para>
        /// </summary>
        private static Material EnsureCordMaterial()
        {
            Shader shader = Shader.Find(CordShaderName);
            if (shader == null)
            {
                Debug.LogError($"[NetGun] Shader '{CordShaderName}' not found.");
                return null;
            }

            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
            if (albedo == null || normal == null)
            {
                Debug.LogError($"[NetGun] Missing {AlbedoPath} or {NormalPath}.");
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(CordMaterialPath) ?? ".");

            var material = AssetDatabase.LoadAssetAtPath<Material>(CordMaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, CordMaterialPath);
            }

            material.shader = shader;

            material.SetTexture("_BaseMap", albedo);
            material.SetTextureScale("_BaseMap", CordTiling);
            material.SetTextureOffset("_BaseMap", Vector2.zero);
            material.SetColor("_BaseColor", Color.white);

            material.SetTexture("_BumpMap", normal);
            material.SetTextureScale("_BumpMap", CordTiling);
            material.SetTextureOffset("_BumpMap", Vector2.zero);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");

            material.SetFloat("_WorkflowMode", 1f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", CordSmoothness);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_OcclusionStrength", 1f);
            material.SetFloat("_SpecularHighlights", 1f);
            material.SetFloat("_EnvironmentReflections", 1f);
            material.SetFloat("_ReceiveShadows", 1f);

            // Opaque, written out rather than inherited: the surface floats do not follow _Surface
            // on their own outside URP's own material inspector.
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_AlphaToMask", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            material.SetFloat("_ZWrite", 1f);
            material.renderQueue = -1;

            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.doubleSidedGI = true;

            EditorUtility.SetDirty(material);
            return material;
        }

        // ─────────────────────────── The item ───────────────────────────

        private static InventoryItem EnsureItemAsset(GameObject prefab)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ItemAssetPath) ?? ".");

            var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemAssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(asset, ItemAssetPath);
            }

            asset.itemName = "Net Gun";
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
                .FirstOrDefault(c => c != null && c.GetType().FullName == "SpaceGame.Items.PickupableItem");
            if (pickup == null) { Debug.LogError("[NetGun] PickupableItem missing."); return; }

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
            if (list == null) { Debug.LogError($"[NetGun] No list at {NetworkPrefabsPath}."); return; }
            if (list.Contains(prefab)) return;

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }

        // ─────────────────────────── Proof ───────────────────────────

        /// <summary>
        /// The objects this builder binds by name, and nothing else. A renamed mesh would otherwise
        /// produce a gun that inspects perfectly and fires out of its own foot.
        /// </summary>
        private static bool VerifyModel(Dictionary<string, Transform> parts)
        {
            string[] required = { "Mesh_NetGun_Body", "Mesh_NetGun_Bundle", "Marker_Muzzle", "Marker_Grip" };
            string[] missing = required.Where(n => !parts.ContainsKey(n)).ToArray();

            if (missing.Length == 0) return true;

            Debug.LogError($"[NetGun] The model is missing: {string.Join(", ", missing)}. " +
                           "Was it renamed in net_gun.blend, or exported from the wrong collection?");
            return false;
        }

        /// <summary>
        /// Re-read everything this run wrote, off disk, and assert it landed.
        ///
        /// <para>
        /// Unity's AssetDatabase goes read-only in some sessions and discards prefab and asset saves
        /// outright without raising anything, so a run that reports success having written nothing is
        /// a real outcome rather than a hypothetical one.
        /// </para>
        /// </summary>
        private static bool Verify()
        {
            var problems = new List<string>();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemAssetPath);
            var cord = AssetDatabase.LoadAssetAtPath<Material>(CordMaterialPath);

            if (prefab == null) problems.Add($"no prefab at {PrefabPath}");
            if (asset == null) problems.Add($"no item asset at {ItemAssetPath}");
            if (cord == null) problems.Add($"no material at {CordMaterialPath}");

            if (prefab != null)
            {
                var netObject = prefab.GetComponent<NetworkObject>();
                if (netObject == null) problems.Add("the prefab root has no NetworkObject");
                else if (netObject.PrefabIdHash == 0) problems.Add("GlobalObjectIdHash is 0");

                var grip = prefab.GetComponent<ItemGrip>();
                if (grip == null) problems.Add("no ItemGrip");
                else if (!Mathf.Approximately(grip.HoldSize, HoldSize))
                    problems.Add($"holdSize reads {grip.HoldSize:F3}, expected {HoldSize:F3}");

                var artifact = prefab.GetComponent<NetGunArtifact>();
                if (artifact == null) problems.Add("no NetGunArtifact");
                else problems.AddRange(ArtifactProblems(artifact, cord));

                var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
                if (list == null || !list.Contains(prefab))
                    problems.Add($"the prefab is not in {NetworkPrefabsPath}");
            }

            if (asset != null && asset.itemPrefab != prefab)
                problems.Add("the item asset does not point at the prefab");

            if (problems.Count == 0)
            {
                Debug.Log($"[NetGun] VERIFIED off disk: maxUses {MaxUses}, holdSize {HoldSize:F2}, " +
                          "muzzle, cord material and bundle all bound, registered for clients.");
                return true;
            }

            Debug.LogError("[NetGun] NOT VERIFIED:\n  " + string.Join("\n  ", problems));
            return false;
        }

        private static IEnumerable<string> ArtifactProblems(NetGunArtifact artifact, Material cord)
        {
            var so = new SerializedObject(artifact);

            int maxUses = so.FindProperty("maxUses").intValue;
            if (maxUses != MaxUses) yield return $"maxUses reads {maxUses}, expected {MaxUses}";

            if (so.FindProperty("muzzle").objectReferenceValue == null) yield return "muzzle is unset";
            if (so.FindProperty("loadedBundle").objectReferenceValue == null) yield return "loadedBundle is unset";
            if (so.FindProperty("netMaterial").objectReferenceValue != cord) yield return "netMaterial is not Net_Cord";

            int layers = so.FindProperty("catchableLayers").intValue;
            if (layers != CatchableLayers)
                yield return $"catchableLayers reads {layers}, expected {CatchableLayers}";
        }

        // ─────────────────────────── Shared ───────────────────────────

        private static Dictionary<string, Transform> PartsOf(GameObject root) =>
            root.GetComponentsInChildren<Transform>(true)
                .GroupBy(t => t.name)
                .ToDictionary(g => g.Key, g => g.First());

        /// <summary>
        /// PickupableItem is internal to Assembly-CSharp, so it cannot be named from an editor
        /// assembly at all.
        /// </summary>
        private static void AddByName(GameObject go, string fullName)
        {
            System.Type type = typeof(ItemGrip).Assembly.GetType(fullName);
            if (type == null) { Debug.LogError($"[NetGun] No such component: {fullName}."); return; }

            go.AddComponent(type);
        }

        /// <summary>
        /// Private [SerializeField] fields are not reachable from an editor script any other way, and
        /// widening the runtime API for a build-time convenience would be the wrong trade. A missing
        /// name warns loudly rather than silently doing nothing.
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

            private static SerializedProperty Find(SerializedObject so, string name)
            {
                SerializedProperty p = so.FindProperty(name);
                if (p == null)
                    Debug.LogWarning($"[NetGun] {so.targetObject.GetType().Name} has no serialized " +
                                     $"field '{name}' — it was renamed; this value is unset.");
                return p;
            }
        }
    }
}
