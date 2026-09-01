// Builds the jumping rod from Assets/Game/Art/Models/Items/jumping_rod.fbx:
//
//   Assets/Game/Prefabs/Items/Artifacts/Gadgets/JumpingRodDeployed.prefab  the rod once planted
//   Assets/Game/Prefabs/Items/Artifacts/Gadgets/JumpingRod.prefab          the one carried
//   Assets/Game/Resources/Items/Artifacts/JumpingRod.asset                 the InventoryItem
//
// One FBX serves both prefabs, at two sizes: hand size in the hotbar (ItemGrip.holdSize) and
// player size once planted (JumpingRodItem.deployedSize).
//
// The planted rod is a PLAIN VISUAL — no NetworkObject, no collider, no Rigidbody. Every machine
// instantiates its own copy from JumpingRodItem.Present(), which is how an equipped visual works
// here; registering it as a network prefab would spawn a second one on the host and turn a
// cosmetic child into something the save system tries to rebuild.
//
// The FBX handling lives in JumpingRodBuilder.Model.cs.
//
// Re-run from: Tools ▸ Items ▸ Build Jumping Rod
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using SpaceGame.Gear.JumpingRod;

namespace SpaceGame.EditorTools
{
    public static partial class JumpingRodBuilder
    {
        private const string ModelPath = "Assets/Game/Art/Models/Items/jumping_rod.fbx";
        private const string DeployedPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/JumpingRodDeployed.prefab";
        private const string ItemPrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/JumpingRod.prefab";
        private const string ItemAssetPath = "Assets/Game/Resources/Items/Artifacts/JumpingRod.asset";
        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        /// <summary>The ground layer DropItemPhysics settles against, shared by every artifact.</summary>
        private const int GroundLayerMask = 128;

        /// <summary>
        /// Hand size for the carried rod, metres along its longest axis.
        ///
        /// On the item scale ladder's <c>Anchor</c> bracket with the LaserStaff (1.35) and the
        /// DragonBazooka (1.25) — it is a two-handed pole and reads wrong at hand-tool size.
        ///
        /// <para>
        /// What that costs on the pack changed with <see cref="LieDown"/> and is worth stating,
        /// because it is the price of the rod not standing on its tip: laid down its footprint is
        /// its LENGTH, so it reserves a long strip instead of a stub, and the only faces that take
        /// it are the ones built for long goods. That is what a real pole does on a real pack.
        /// <c>JumpingRodWiringTests.Item_FitsOnAPackSurface</c> holds it against the rig's real
        /// faces, including the rack's ski-fashion overhang. See ItemScaleLadder.cs, which owns
        /// the bracket table.
        /// </para>
        /// </summary>
        private const float HoldSize = 1.25f;

        /// <summary>Piston stroke, matching TRAVEL in _Source~/models/gear/jumping_rod.py.</summary>
        private const float Travel = 0.11f;

        /// <summary>
        /// The carried rod is turned onto its side, so it LIES DOWN like a pole put on a shelf.
        ///
        /// <para>
        /// The model arrives standing (<c>Verify</c> insists on it, because the DEPLOYED rod is a
        /// planted pogo stick and must). That is wrong for the carried one:
        /// <c>ItemFootprint.FootprintOf</c> is <em>defined</em> as <c>(size.x, size.z)</c> — the
        /// shadow an item casts with its own up still up — so a rod standing on its end reserved a
        /// tiny 3 x 1 cell rectangle on the pack and was drawn balanced on its tip in the middle of
        /// it. It read as a bug, and it was authored data.
        /// </para>
        /// <para>
        /// +90 about X takes the shaft from +Y to +Z, which is also the axis <see cref="ItemGrip"/>
        /// calls "the way the item points". <b>The pose in the hand does not move</b>: the grip's
        /// <c>rotationOffset</c> is set to the inverse, and
        /// <c>rotation = handRotation * Euler(offset)</c> multiplies the two back out. This is the
        /// same correction <c>ItemPackOrientation.Reframe</c> applies to hand-authored prefabs; it
        /// lives here instead because this prefab is rebuilt wholesale on the next run of this
        /// script and would swallow one made there without a word.
        /// </para>
        /// </summary>
        private static readonly Vector3 LieDown = new(90f, 0f, 0f);

        [MenuItem("Tools/Items/Build Jumping Rod")]
        public static void BuildAll()
        {
            GameObject deployed = BuildDeployed();
            if (deployed == null) return;

            BuildItem(deployed);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // ─────────────────────────── The planted rod ───────────────────────────

        private static GameObject BuildDeployed()
        {
            GameObject model = LoadModel();
            if (model == null) return null;

            var root = new GameObject("JumpingRodDeployed");
            NestModel(model, root.transform);

            Dictionary<string, Transform> parts = PartsOf(root);
            if (!Verify(parts, root)) { Object.DestroyImmediate(root); return null; }

            Transform piston = BindPistonAssembly(parts);

            JumpingRodSpring spring = root.AddComponent<JumpingRodSpring>();
            var so = new SerializedObject(spring);
            SerializedFields.Set(so, "rod", root.transform);
            SerializedFields.Set(so, "piston", piston);
            SerializedFields.Set(so, "coil", parts["Mesh_JumpingRod_Spring"]);
            SerializedFields.SetFloat(so, "travel", Travel);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Nothing else. No collider — the rod is drawn under a player who already has one, and
            // a second capsule down there would catch on every doorway they walked through.
            GameObject saved = SaveTo(root, DeployedPath);
            if (saved != null) Debug.Log($"[JumpingRod] Built {DeployedPath}.");
            return saved;
        }

        // ─────────────────────────── The carried item ───────────────────────────

        private static void BuildItem(GameObject deployed)
        {
            GameObject model = LoadModel();
            if (model == null) return;

            var root = new GameObject("JumpingRod");
            GameObject instance = NestModel(model, root.transform);

            // Before anything is measured off it — the collider, the grip point and the footprint
            // all read the bounds below, and they must all read the LAID-DOWN ones.
            instance.transform.localRotation = Quaternion.Euler(LieDown);

            Bounds whole = MeasuredBounds(root.transform, root);
            WirePickup(root, whole);
            WireGrip(root, instance, whole);

            JumpingRodItem item = root.AddComponent<JumpingRodItem>();
            var so = new SerializedObject(item);
            SerializedFields.Set(so, "deployedPrefab", deployed);
            SerializedFields.SetFloat(so, "deployedSize", 1.45f);

            // Stand-off only. The rod's HEIGHT is not authored: JumpingRodItem hangs its tip one
            // contact band below the holder's own soles, measured from their collider, because
            // this player's pivot is a metre above their feet and any number typed here would be
            // a number that is right for exactly one character.
            SerializedFields.SetVector3(so, "deployedOffset", new Vector3(0f, 0f, 0.22f));
            SerializedFields.SetInt(so, "groundMask", ~0);
            SerializedFields.SetFloat(so, "probeDistance", 2f);
            SerializedFields.SetFloat(so, "probeLift", 0.5f);
            // Equipment, not a consumable. -1 is UsableItem's unlimited sentinel.
            SerializedFields.SetInt(so, "maxUses", -1);
            SerializedFields.SetEnumByName(so, "useSoundId", "InteractLever");
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = SaveTo(root, ItemPrefabPath);
            if (saved == null) return;

            InventoryItem asset = EnsureItemAsset(saved);
            WireItemIntoPickup(saved, asset);
            RegisterNetworkPrefab(saved);

            Debug.Log($"[JumpingRod] Built {ItemPrefabPath} and {ItemAssetPath}. " +
                      "Run Tools/Generate All Item Icons for the inventory icon.");
        }

        /// <summary>
        /// What every artifact needs to be dropped, thrown, landed and picked back up. A kinematic
        /// body keeps it still on the ground until DropItemPhysics takes over.
        /// </summary>
        private static void WirePickup(GameObject root, Bounds whole)
        {
            var netObject = root.AddComponent<NetworkObject>();
            netObject.SynchronizeTransform = true;

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = true;

            CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
            capsule.direction = 2;                        // Z — the carried rod lies down
            capsule.radius = 0.08f;
            capsule.height = whole.size.z;
            capsule.center = new Vector3(0f, 0f, whole.center.z);

            AddByName(root, "SpaceGame.Items.PickupableItem");

            var drop = root.AddComponent<DropItemPhysics>();
            var so = new SerializedObject(drop);
            SerializedFields.Set(so, "rb", body);
            SerializedFields.SetInt(so, "groundLayer", GroundLayerMask);
            so.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<NetRelay>();
            root.AddComponent<SaveableEntity>();
            root.AddComponent<TransformSaveable>();
        }

        /// <summary>
        /// Held around the middle of the shaft, the way a pole is carried.
        ///
        /// <para>
        /// The grip point is the same physical spot on the rod it always was — the shaft's middle
        /// — expressed on the axis <see cref="LieDown"/> moved it to. Together with the inverse
        /// <c>rotationOffset</c> that means the hand sees no change at all: same point in the palm,
        /// same world rotation, and only the pack and the sand see a rod lying down.
        /// </para>
        /// </summary>
        private static void WireGrip(GameObject root, GameObject instance, Bounds whole)
        {
            Transform grip = MakeChild(root, "GripPoint", new Vector3(0f, 0f, whole.center.z));

            ItemGrip itemGrip = root.AddComponent<ItemGrip>();
            var so = new SerializedObject(itemGrip);
            SerializedFields.Set(so, "gripPoint", grip);
            SerializedFields.SetFloat(so, "holdSize", HoldSize);
            SerializedFields.SetVector3(so, "rotationOffset", -LieDown);
            SerializedFields.Set(so, "sizeReference", instance.transform);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ─────────────────────────── Assets ───────────────────────────

        private static GameObject SaveTo(GameObject root, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            if (saved == null) Debug.LogError($"[JumpingRod] Saving {path} failed.");
            return saved;
        }

        private static InventoryItem EnsureItemAsset(GameObject prefab)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ItemAssetPath) ?? ".");

            var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemAssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(asset, ItemAssetPath);
            }

            asset.itemName = "Jumping Rod";
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
            if (pickup == null) { Debug.LogError("[JumpingRod] PickupableItem missing."); return; }

            var so = new SerializedObject(pickup);
            SerializedFields.Set(so, "item", asset);
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SavePrefabAsset(prefab);
        }

        /// <summary>
        /// The list NetworkManager actually reads. NOT Assets/DefaultNetworkPrefabs.asset, which
        /// Netcode regenerates and nothing consults. Only the CARRIED rod goes in — dropping a
        /// hotbar slot routes through PlayerDropService to GameServices.World.Spawn. The planted rod
        /// must stay out of it: it is a cosmetic child every machine makes for itself.
        /// </summary>
        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null) { Debug.LogError($"[JumpingRod] No list at {NetworkPrefabsPath}."); return; }
            if (list.Contains(prefab)) return;

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }
    }
}
