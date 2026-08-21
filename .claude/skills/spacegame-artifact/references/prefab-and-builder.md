# Artifact prefab layout and the editor-builder template

Reference for step 3 of the `spacegame-artifact` build order. Everything below is taken from the
shipped prefabs `LightningSpell.prefab`, `ItemScanner.prefab` and `LaserStaff.prefab`, and from
`Assets/Game/Editor/AssetPipeline/LaserStaffBuilder.cs`.

## Root component list

One prefab serves three lives — held in a hand, lying in the sand, and restored from a save — so
the root carries all three sets of components.

| Component | Namespace / path | Value that matters |
| --- | --- | --- |
| `Transform` | — | Local position/rotation zero. |
| `NetworkObject` | `Unity.Netcode` | `SynchronizeTransform: 1`. Must be on the **root**. |
| A `Collider` | — | `SphereCollider`, radius ≈ the item's half-size (0.13–0.16 m for a hand tool). Disabled automatically while held unless `ItemGrip.keepColliders`. |
| `Rigidbody` | — | `isKinematic: true`, `useGravity: true`. `DropItemPhysics` un-kinematics it on a throw and re-kinematics it on landing. |
| `PickupableItem` | `SpaceGame.Items` (`Scripts/Items/Core/PickupableItem.cs`) | `item` → the `InventoryItem` asset. `pickupId` → `SfxId.InteractPickup` (503) or `InteractPickupMetal` (504). Also registers the object with `ScannerRegistry`, which is how the Item Scanner finds loose salvage. |
| `DropItemPhysics` | `SpaceGame.Items` | `rb` → the Rigidbody above. `groundLayer` → mask `128`. |
| `NetRelay` | `SpaceGame.Core` | No fields. Required for `NetMessaging` on this object. |
| `SaveableEntity` | `SpaceGame.Core.Persistence` | Leave `prefabId`/`instanceId` empty — `OnValidate` stamps them. `scope: World`. |
| `TransformSaveable` | `SpaceGame.Core.Persistence` | Where it came to rest. |
| `RigidbodySaveable` | `SpaceGame.Core.Persistence` | Optional; `ItemScanner.prefab` has it, `LightningSpell.prefab` does not. |
| `ItemGrip` | `SpaceGame.Items` (`Scripts/Items/Equipped/ItemGrip.cs`) | See below. |
| The artifact script | `SpaceGame.Items` | Serialized references to the child transforms/renderers it drives. |
| `HoldAnimator` | `SpaceGame.Items` | **Optional.** `UsableItem.OnEquipped` adds one when absent. Add it by hand only to change `boolParameter` or the movement gate; an authored component is left untouched. |

### Children

- `Model` — a prefab instance of the FBX. Never a mesh copied into the prefab.
- `Grip` / `GripPoint` — an empty at the point the hand closes, referenced by `ItemGrip.gripPoint`.
- Any effect rig the script drives (`Muzzle`, `Beam`, `Impact`, screens, dials).

Layer stays `0` (Default) on every shipped artifact.

## Tuning `ItemGrip`

The offsets are expressed in the **hand's** frame, derived per rig by `HandGripFrame.Derive` from
the finger bones — not in bone-local axes. A grip tuned once therefore also holds on an NPC with a
different skeleton.

- Zero rotation means: the item's **+Z points where the back of the hand faces**, and its **+Y
  points out the thumb side**, as a torch's flame would. Start at zero and tune from there.
- `holdSize` is the longest-axis size in metres once held — hand tool 0.2–0.4, rifle 0.9–1.2, staff
  ≈1.35. `0` keeps the prefab's own scale.
- `sizeReference` restricts what `holdSize` is measured against. Needed when the prefab carries
  geometry that is not the item — the Lasso's 4.4 m rope shares a prefab with its handle, and
  sizing the pair to a hand shrinks the handle to nothing.
- `gripPoint` and `sizeReference` **must be inside this prefab**. `OnValidate` clears an outside
  reference with a warning, and the pose silently reverts to gripping the root.
- `hand = Left` routes the item to the off-hand socket when the rig has one.
- Blender FBXs sit under a −90° X rotation (`LightningSpell.prefab`'s Model instance overrides
  `m_LocalRotation` to `w 0.7071, x -0.7071`; see also the comment in `LaserStaffBuilder`), so a
  model's long axis is mesh-local **Z** but prefab-space **Y**. When a builder script derives a
  muzzle or tip from bounds, measure along the mesh's own axis — and beware that an origin at the
  grip makes the *butt* the far extremity, which is how a staff ends up firing out of its own foot.

## Authoring the prefab: script, not YAML

Write an editor builder under `Assets/Game/Editor/AssetPipeline/` whenever the prefab nests an FBX.
Unity decides the file ids inside an imported model at import time; hand-written YAML referencing a
guessed id does not fail loudly — it loads with a missing model and no error.

Builders are re-runnable and **replace the prefab wholesale**. Anything hand-added in the Inspector
afterwards is destroyed by the next run, so all tuning belongs in the script.

### Template

```csharp
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
    public static class SmokeBombBuilder
    {
        private const string ModelPath  = "Assets/Game/Art/Models/Items/smoke_bomb.fbx";
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/SmokeBomb.prefab";
        private const string ItemPath   = "Assets/Game/Resources/Items/Artifacts/SmokeBomb.asset";
        private const string NetworkPrefabsPath =
            "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        /// <summary>The ground layer DropItemPhysics settles against, shared by every artifact.</summary>
        private const int GroundLayerMask = 128;

        [MenuItem("Tools/Build Smoke Bomb Artifact")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) { Debug.LogError($"[SmokeBomb] No model at {ModelPath}."); return; }

            var root = new GameObject("SmokeBomb");

            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.name = "Model";
            modelInstance.transform.SetParent(root.transform, false);

            var grip = new GameObject("GripPoint");
            grip.transform.SetParent(root.transform, false);

            // ── Pickup / world presence ──
            var netObject = root.AddComponent<NetworkObject>();
            netObject.SynchronizeTransform = true;

            SphereCollider sphere = root.AddComponent<SphereCollider>();
            sphere.radius = 0.09f;

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
            SetPrivate(itemGrip, "gripPoint", grip.transform);
            SetPrivate(itemGrip, "holdSize", 0.18f);
            SetPrivate(itemGrip, "sizeReference", modelInstance.transform);

            // ── The artifact ──
            var artifact = root.AddComponent<SmokeBombArtifact>();
            SetPrivateEnum(artifact, "useSoundId", "WeaponProjectileWhoosh");

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? ".");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) { Debug.LogError("[SmokeBomb] Prefab save failed."); return; }

            InventoryItem item = EnsureItem(prefab);
            WireItemIntoPickup(prefab, item);
            RegisterNetworkPrefab(prefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SmokeBomb] Built {PrefabPath} and {ItemPath}. " +
                      "Run Tools/Generate All Item Icons for the inventory icon.");
        }

        private static InventoryItem EnsureItem(GameObject prefab)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ItemPath) ?? ".");

            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(item, ItemPath);
            }

            item.itemName = "Smoke Bomb";
            item.itemPrefab = prefab;
            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>
        /// The item asset references the saved prefab and the prefab references the item, so one of
        /// the two links can only be made once both files exist.
        /// </summary>
        private static void WireItemIntoPickup(GameObject prefab, InventoryItem item)
        {
            Component pickup = prefab.GetComponents<Component>()
                .FirstOrDefault(c => c != null && c.GetType().FullName == "SpaceGame.Items.PickupableItem");
            if (pickup == null) { Debug.LogError("[SmokeBomb] PickupableItem missing."); return; }

            var so = new SerializedObject(pickup);
            so.FindProperty("item").objectReferenceValue = item;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SavePrefabAsset(prefab);
        }

        /// <summary>
        /// The list NetworkManager actually reads. NOT Assets/DefaultNetworkPrefabs.asset, which
        /// Netcode regenerates and nothing consults.
        /// </summary>
        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null) { Debug.LogError($"[SmokeBomb] No list at {NetworkPrefabsPath}."); return; }
            if (list.Contains(prefab)) return;

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }

        // ── Reflection helpers ──
        //
        // Item components serialize private fields, which is right for runtime code and simply
        // means an editor script goes in the way the Inspector does. PickupableItem is additionally
        // internal to Assembly-CSharp, so it cannot be named from this assembly at all.

        private static void AddInternal(GameObject go, string typeName)
        {
            Type type = typeof(ItemGrip).Assembly.GetType(typeName);
            if (type == null) { Debug.LogError($"No type {typeName}."); return; }
            go.AddComponent(type);
        }

        private static FieldInfo Field(object target, string name) =>
            target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static void SetPrivate(object target, string name, object value) =>
            Field(target, name)?.SetValue(target, value);

        private static void SetPrivateLayerMask(object target, string name, int mask) =>
            Field(target, name)?.SetValue(target, (LayerMask)mask);

        private static void SetPrivateEnum(object target, string name, string enumValue)
        {
            FieldInfo field = Field(target, name);
            if (field != null) field.SetValue(target, Enum.Parse(field.FieldType, enumValue));
        }
    }
}
```

## After building

- Open the saved `.prefab` and confirm `GlobalObjectIdHash` is **non-zero**. A NetworkObject added
  by script into a prefab that is not saved through `PrefabUtility` ships hash `0` and can never be
  spawned on a client.
- `InventoryItem.ID` is the asset GUID, stamped by `InventoryItem.OnValidate`
  (`Scripts/Items/Core/InventoryItem.cs`). It is not written into the `.asset` YAML, so an asset
  produced outside the editor needs an import before it has an ID — and without an ID
  `PlayerInventoryNetwork` skips it in `startingItems` and the save system cannot key it.
- Verify writes landed by reading the file back off disk. The AssetDatabase goes read-only in some
  sessions (a Multiplayer Play Mode clone, for one) and discards prefab saves silently.

## Where items reach the player

| Route | Where to wire it |
| --- | --- |
| Spawn with it | `PlayerInventoryNetwork.startingItems` on `Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab` |
| Dev browser | Nothing to wire — press `I` with `GameSettings.DevMode`; it lists the whole registry |
| NPC death | `EntityLootTable.lootEntries` (`Scripts/agents/Entity/EntityLootTable.cs`) |
| Barter | `TradeOffer.wants` / `.gives` (`Scripts/Gameplay/Trading/TradeOffer.cs`) |
| Lying in the world | Place a prefab instance in a chunk scene; `PickupableItem` does the rest |
| An NPC using it | `EntityInventoryComponent` + `NpcItemUseModule` |

## Tests that guard this pipeline

`Assets/Game/Editor/Tests/`, run via `Tools/Tests/Run EditMode Tests (headless)`:

- `NetworkPrefabRegistrationTests` — every root `NetworkObject` prefab is in the list, no null
  entries, the player prefab is registered.
- `HoldPoseTests` — `UsableItem.OnEquipped` adds a `HoldAnimator` when one is absent, and skips it
  when `UsesHoldPose` is false.
- `GrappleUseFlowTests`, `LaserStaffBeamTests`, `GripFrameTests` — worked examples of testing an
  artifact's use flow, hold flow and grip pose.

These live in `Editor/` rather than beside the other EditMode tests because they touch
Assembly-CSharp types, and an asmdef cannot reference Assembly-CSharp.
