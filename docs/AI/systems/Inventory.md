---
system: Inventory
layer: items
summary: "Hotbar slots, InventoryItem assets, the hand socket that seats each equip, and the pickup/drop round trip"
paths:
  - Assets/Game/Scripts/Items/Inventory
  - Assets/Game/Scripts/Items/Core
  - Assets/Game/Scripts/Items/Equipped
  - Assets/Game/Resources/Items
  - Assets/Game/Prefabs/Items
symptoms:
  - "my new item never appears in the dev item browser (I key)"
  - "the item floats beside the hand instead of in it, or comes out comically large"
  - "a hotbar slot comes back empty after loading a save, but the others kept their positions"
  - "picking an item up works for the host and does nothing for a client"
  - "the item loses its charges or its state when I switch slots or re-equip it"
  - "an item I dropped is gone after save and reload, or shoves me around while held"
  - "an item is the right size in my hand and the wrong size lying on the backpack"
reads_with: [Artifacts, Backpack, Persistence, Combat]
updated: 2026-09-01
---

# Items & Inventory core

Hotbar slots holding `InventoryItem` assets, the hand socket that seats a fresh prefab instance per equip, and the world pickup/drop round trip.
**Scope:** [Items/Core](Assets/Game/Scripts/Items/Core), [Items/Inventory](Assets/Game/Scripts/Items/Inventory), [Items/Equipped](Assets/Game/Scripts/Items/Equipped), [Resources/Items](Assets/Game/Resources/Items), [Prefabs/Items](Assets/Game/Prefabs/Items).
**Related:** [Artifacts.md](Artifacts.md) (item *behaviour* subclasses), [Backpack.md](Backpack.md) (overflow storage), [Persistence.md](Persistence.md), [WeaponSystem.md](WeaponSystem.md), skill [.claude/skills/spacegame-artifact](.claude/skills/spacegame-artifact/SKILL.md).

## Model

- **Asset** — [`InventoryItem`](Assets/Game/Scripts/Items/Core/InventoryItem.cs) ScriptableObject: `ID` (the asset GUID, `[field: SerializeField]`, stamped in `OnValidate`), `itemName`, `itemPrefab`, `icon`, optional `iconPrefab`. Must live under [Assets/Game/Resources/Items](Assets/Game/Resources/Items) — [`RegistryLoader`](Assets/Game/Scripts/Core/Registry/RegistryLoader.cs) does `Resources.LoadAll<InventoryItem>("Items")`. `Assets/Game/ScriptableObjects/Items/` holds dead duplicates.
- **Prefab** — one prefab is *both* the held object and the thing lying in the sand: `NetworkObject` + [`PickupableItem`](Assets/Game/Scripts/Items/Core/PickupableItem.cs) + collider + Rigidbody + [`DropItemPhysics`](Assets/Game/Scripts/Items/Inventory/Components/DropItemPhysics.cs) + a [`UsableItem`](Assets/Game/Scripts/Items/Core/UsableItem.cs) subclass + optional [`ItemGrip`](Assets/Game/Scripts/Items/Equipped/ItemGrip.cs).
- **Runtime instance** — every equip is a fresh `Instantiate`, every unequip a `Destroy`. Nothing on the instance survives a slot switch; anything that must lives in the slot's [`ItemState`](Assets/Game/Scripts/Items/Inventory/Core/ItemState.cs) bag.
- **Who owns the inventory** — the player prefab carries an [`IPlayerInventory`](Assets/Game/Scripts/Items/Inventory/Core/IPlayerInventory.cs). Two implementations: [`PlayerInventoryNetwork`](Assets/Game/Scripts/Items/Inventory/Components/PlayerInventoryNetwork.cs) (server-authoritative, on `PlayerCharacterNetworked.prefab` — the real player) and [`PlayerInventoryComponent`](Assets/Game/Scripts/Items/Inventory/Components/PlayerInventoryComponent.cs) (local-only). Both wrap the same pure [`PlayerInventory`](Assets/Game/Scripts/Items/Inventory/Core/PlayerInventory.cs) / [`Inventory`](Assets/Game/Scripts/Items/Inventory/Core/Inventory.cs).
- Default hotbar size is **4** (`inventorySize`); overflow goes to the backpack. Selection is a single index, and re-selecting the selected slot **deselects** (empties the hands).
- Grip/pose data is authored per prefab; the *rig* half is derived per character, never serialized ([`HandGripFrame`](Assets/Game/Scripts/Items/Equipped/HandGripFrame.cs)).

## Key types

| Type | File | Role |
| --- | --- | --- |
| `InventoryItem` | [Core/InventoryItem.cs](Assets/Game/Scripts/Items/Core/InventoryItem.cs) | Item asset; `ID` = asset GUID, the save/registry key |
| `UsableItem` | [Core/UsableItem.cs](Assets/Game/Scripts/Items/Core/UsableItem.cs) | Base held behaviour; `Use()`/`Present()` split, `maxUses`, equip hooks, `IItemStateCarrier` |
| `ToolItem` / `EffectItem` | [Core/ToolItem.cs](Assets/Game/Scripts/Items/Core/ToolItem.cs), [Core/EffectItem.cs](Assets/Game/Scripts/Items/Core/EffectItem.cs) | Aimed/instant vs. timed effect on the holder's own body |
| `PickupableItem` | [Core/PickupableItem.cs](Assets/Game/Scripts/Items/Core/PickupableItem.cs) | `IInteractable` + `IScanTarget`; world → inventory |
| `Inventory` / `InventorySlot` | [Inventory/Core/](Assets/Game/Scripts/Items/Inventory/Core/Inventory.cs) | Slot array, add/move/swap/restore; slot holds `Item` + `State` |
| `IPlayerInventory` | [Inventory/Core/IPlayerInventory.cs](Assets/Game/Scripts/Items/Inventory/Core/IPlayerInventory.cs) | The seam every caller (pickup, UI, dev browser, savers) uses |
| `PlayerInventoryNetwork` | [Inventory/Components/](Assets/Game/Scripts/Items/Inventory/Components/PlayerInventoryNetwork.cs) | `NetworkList<FixedString64Bytes>` of item IDs + `NetworkVariable<int>` selection |
| `EquipmentController` | [Inventory/Components/](Assets/Game/Scripts/Items/Inventory/Components/EquipmentController.cs) | Resolves hand bones, equips/unequips, **and is the only place an item is triggered across the network** |
| `EquipItemSocket` | [Inventory/Core/EquipItemSocket.cs](Assets/Game/Scripts/Items/Inventory/Core/EquipItemSocket.cs) | Instantiate → sanitize physics → scale → seat grip point in the palm |
| `ItemGrip` | [Equipped/ItemGrip.cs](Assets/Game/Scripts/Items/Equipped/ItemGrip.cs) | Per-prefab hand, grip point, offsets, `holdSize`/`packSize`, `sizeReference`, `keepColliders`. Both sizes are **hand-frame metres**; the pack multiplies its own scale in. |
| `HandGripFrame` | [Equipped/HandGripFrame.cs](Assets/Game/Scripts/Items/Equipped/HandGripFrame.cs) | Anatomy-derived hand frame: +Y thumb side, +Z the way an item points, origin mid-fist |
| `HoldAnimator` | [Equipped/HoldAnimator.cs](Assets/Game/Scripts/Items/Equipped/HoldAnimator.cs) | Player → `PlayerAimRig.SetHeldStyle`; NPC/turret → a `Hold` bool |
| `ItemState` / `IItemStateCarrier` / `IItemDeferredRestore` | [Inventory/Core/ItemState.cs](Assets/Game/Scripts/Items/Inventory/Core/ItemState.cs) | String bag per slot; capture/restore; deferred pass for world references |
| `ItemBounds` | [Core/ItemBounds.cs](Assets/Game/Scripts/Items/Core/ItemBounds.cs) | Mesh-based (not `Renderer.bounds`) local extents, shared by hand + pack |
| `HotbarNavigation` | [Inventory/Core/HotbarNavigation.cs](Assets/Game/Scripts/Items/Inventory/Core/HotbarNavigation.cs) | Scroll target, clamped, `NoChange = -1` |
| `PlayerDropService` | [Core/GameServices/Implementations/](Assets/Game/Scripts/Core/GameServices/Implementations/PlayerDropService.cs) | `IItemDropService`: spawn + `SaveableEntity.EnsureRuntime(obj, item.ID)` + impulse |
| `DevInventoryUI` | [Presentation/UI/Pages/DevInventoryUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/DevInventoryUI.cs) | Dev item browser — **I** key, gated on `GameSettings.DevMode` |

## Flows

**Pick up** — 1. `Interactor` hits `PickupableItem.Interact`. 2. Pickup SFX plays *locally, immediately* (even if the add later fails). 3. `Network.Execute` → server path (`RequestPickupServerRpc` carries the interactor's `GetComponentInParent<NetworkObject>()`). 4. Server: `IsSpawned` guard (one item, one taker) → `IPlayerInventory.TryAddItem` → on refusal, `BackpackController.Pack.TryStow`. 5. On success `GameServices.World.Despawn`.

**Equip** — 1. `PlayerInventoryNetwork` selection changes → `OnSlotSelected` on *every* machine. 2. `EquipmentController.HandleEquip` → `Equip(item, slot)`; no extra network hop, the replicated selection is the decision. 3. `SocketFor(prefab)` picks main/off hand from `ItemGrip.HeldIn`. 4. `EquipItemSocket.Equip`: `Instantiate` under the bone → `Sanitize` (all colliders off, bodies kinematic, `detectCollisions=false`) → `ApplyScale` → rotate into the grip frame → translate grip point into the palm. 5. `usable.OnEquipped(player)` (adds a `HoldAnimator` if missing and `UsesHoldPose`). 6. **Then** `RestoreItemState(slot.State)` — after, because items reset themselves in `OnEquipped`.

**Use** — 1. Owner presses Use → `OnUse`: `arg.A = selected slot`, `OnRequestUse(ref arg)` (only honest place to read aim). 2. `PlayUse` locally at once. 3. `UseAuthority.Owner` → `TryUse` locally too. 4. `NetToServer(NetMsg.UseItem)`. 5. Server: slot-still-selected guard, `TryUse` if `Authority == Server`, then `NetToOthers(NetMsg.ItemUsed)` → peers `PlayUse` (cosmetics only). 6. Continuous items then stream hold ticks at **15 Hz** through the same three routes (`NetMsg.UseItemHold` / `ItemUseHeld`) until release, or until `WantsHold` goes false for self-timed items.

**Drop** — 1. Owner presses Drop → `DropItemServerRpc(slot)`. 2. Server clears the slot, sets selection `-1`, raises `OnItemDropped`. 3. `EquipmentController.OnItemDropped` → `GameServices.ItemDropService.DropItem(handSocket, item)` → `World.Spawn(item.itemPrefab)` + `SaveableEntity.EnsureRuntime(obj, item.ID)` + impulse. 4. `DropItemPhysics` re-freezes the body on first ground contact and snaps it to the hit point.

**Swap slot** — `Inventory.TryMoveItem`/`SwapItems` move `Item` **and** `State` together (both read out before assignment, because assigning `Item` clears `State`). Selecting the already-selected slot deselects.

## Multiplayer

- Hotbar contents and selection are **server state** (`NetworkList` of item IDs + `NetworkVariable<int>`); clients request via `[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]` — Owner, not Everyone, or anyone could edit anyone's hotbar.
- `TryAddItem`/`TryRemoveItem` return an **optimistic `true`** on a client. Only the server's answer is real; every caller that acts on it (pickup, backpack overflow) already runs server-side.
- Equipping is *derived* from replicated selection on each machine — never sent. `PlayerInventoryNetwork.AdoptCurrentState` covers late joiners (`OnListChanged` only fires on change); `EquipmentController.Start` pulls the current selection for the same reason.
- Authority split: `Use()` = server (or `UseAuthority.Owner` for effects on the holder's own body — the player transform is owner-authoritative, so server-applied forces are silently overwritten); `Present()` = every machine.
- **MUST be in the network prefab list** ([DefaultNetworkPrefabs.asset](Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset), referenced by [NetworkManager.prefab](Assets/Game/Prefabs/Systems/NetworkManager.prefab)): every `InventoryItem.itemPrefab` (it is dropped through `World.Spawn`) and every runtime-spawned deployable. Run `Tools/SpaceGame/Multiplayer/Sync Network Prefabs`.
- **MUST NOT be networked**: projectiles, beams, flying arcs and equipped visuals. A held item is a plain `Instantiate` onto a bone and is never spawned — its own `NetworkObject` is dormant, so `Network.Simulates(heldItem)` answers *true everywhere*. Use `UsableItem.OwnerIsLocal()` / `Network.Owns(owner.transform)` instead.
- Guarded by [NetworkPrefabRegistrationTests](Assets/Game/Editor/Tests/NetworkPrefabRegistrationTests.cs) (`EveryInventoryItemPrefab_IsANetworkedAndRegisteredPrefab`).

## Persistence

- [`PlayerInventorySaveable`](Assets/Game/Scripts/Core/Persistence/Adapters/PlayerInventorySaveable.cs), save key `"inventory"`. Format in `InventorySaveCodec`: `itemIds` (positional, `null` = empty slot), `selectedSlot`, `itemStates` (positional dicts, omitted entirely when nothing has state).
- Items are stored by **registry ID = asset GUID**, never by name or list index. An unknown ID logs a warning and leaves that slot empty; the positions of the others are kept.
- Capture order: `Equipment.WriteBackHeldItemState()` **first** (the held item has been diverging from its slot since equip), then codec capture. `UsableItem.CaptureItemState` writes the `"uses"` key; subclasses override and call `base`.
- Restore order: `RestoreSlots` (which equips the selected item as a side effect) → `RestoreSlotStates` → `Equipment.ReapplyHeldItemState()` (second pass; the held item is the one restored before its bag existed) → `OnLoadComplete` deferred pass for `IItemDeferredRestore` items whose state names something else in the world.
- Dropped items persist because `PlayerDropService` stamps `SaveableEntity.EnsureRuntime(obj, item.ID)` — the *item's* registry ID, so no prefab needs a hand-stamped `prefabId`.

## Gotchas

- **`InventoryItem.ID` needs `[field: SerializeField]`.** `OnValidate` is editor-only; without the attribute every built player ships a null ID and `Registry.Register` throws on the first item — editor-invisible, build-only, i.e. every real multiplayer session.
- **Item asset outside `Resources/Items`** = never registered, absent from the dev browser, and every save slot holding it comes back empty. No error.
- **`FixedString64Bytes` and null.** `cond ? item.ID : default` types the whole ternary as `string` and NREs on the empty arm — write `default(FixedString64Bytes)` (this took down the entire inventory restore once).
- **Assigning `InventorySlot.Item` clears `State`.** Anything moving an item must read the bag out first; anything restoring must write items before bags.
- **`Network.Simulates(heldItem)` is true on every machine** (unspawned NetworkObject). Same trap for `Network.Owns(item.transform)` — ask about the *owner*.
- **`ItemGrip.positionOffset` is metres in the hand's frame** (+Y thumb side, +Z the item's pointing direction, +X across the palm) and is **not** scaled by `holdSize`. `rotationOffset` is applied on top of the derived grip frame, and `gripPoint`'s *rotation is ignored*.
- **`holdSize` is a longest-axis size in metres, not a multiplier**; `0` means "keep the artist's scale" (the rig's own scale is still divided out). Sizes are brackets on a ladder anchored at the Dragon Bazooka's **1.25 m** — see [ItemScaleLadder](Assets/Game/Editor/Items/ItemScaleLadder.cs); four `Fitted` items (SuckerPuncher, RepulsorGauntlet, ItemScanner, WingPack) are **pinned** and must not be rescaled.
- **`packSize` is a separate number for the backpack mat** (`0` = follow `holdSize`), and it is **not** metres on the mat. Author it at the item's honest size next to the hand; [`ItemFootprint.Measure`](Assets/Game/Scripts/Items/Backpack/Placement/ItemFootprint.cs) multiplies [`PackScale.Factor`](Assets/Game/Scripts/Items/Backpack/Placement/PackScale.cs) (1.5 since 2026-09-01) in once, at the single choke point every pack consumer goes through. So an item on the mat measures `packSize * 1.5` and in the hand exactly `holdSize` — that asymmetry is deliberate and is [Backpack.md](Backpack.md)'s, not this doc's, to explain. A pre-multiplied `packSize` gives an item 2.25x on the mat and nothing says why.
- **`packSize: 0` is not "unset" — it is "follow `holdSize`", and that wires an item's share of the pack to the HAND's bracket ladder.** The two frames are not the same and are not close: `ItemScaleLadder` is deliberately not life size (the rig's hand is ~1.7x a human's), so it draws the Leash at 3.4x, the Grappling Hook at 2.6x and the Portal Gun at 2.8x their true modelled sizes. The mat is read from above at a fixed standoff and is **in true-world metres**, which is what every item ever re-sized for it says out loud — Leash 0.27 (true 0.160), Lasso 0.36 (true 0.267), GrapplingHook 0.54 (true 0.382), PortalGun 0.54 (true 0.4445): each its real size rounded up to the next 0.09 m webbing pitch, plus a cell of margin. An item whose hold size already sits near life size (DragonBazooka, 1.25 against a true 1.37) needs no second number. **The failure is a bracket edit**: nudge `holdSize` for feel, which is exactly what the ladder exists to do, and with `packSize` at 0 the item's cell cost moves with it — silently, with nothing in the diff naming the pack. The Portal Gun cost 36 of the rig's 255 cells that way and now costs 8. Author `packSize` for anything the ladder inflates; `ItemScaleLadder.Audit` prints the divergent rows.
- **`ItemGrip.PackSize` is never what `EquipItemSocket` reads.** The hand reads `HoldSize`. Changing the pack's scale therefore cannot move an item in the hand, and changing a `holdSize` bracket moves it on the mat only for the items that left `packSize` at `0`.
- **`sizeReference`** narrows the measurement — without it the Lasso's 4.4 m rope shrinks the handle to nothing. `gripPoint`/`sizeReference` pointing outside the prefab are cleared with a warning in `OnValidate`.
- **Prefabs owned by builder scripts** (LaserStaff, DragonBazooka, GravelBlaster, RepulsorGauntlet, SuckerPuncher, NetGun, JumpingRod, WingPack…) are replaced wholesale on the next run — tune in the builder, and keep its `holdSize` in sync with the ladder. This is also why a global pack scale lives in code and not in 29 prefabs: a builder re-run would put the old numbers back with no warning.
- **How an item lies down on the pack is authored data, not a seating decision.** `ItemFootprint.FootprintOf` is *defined* as `(size.x, size.z)` — the shadow the prefab casts with its own up still up — so a prefab modelled standing on end occupies a tall, thin footprint and reads as balanced on its tip. The fix is [`ItemPackOrientation`](Assets/Game/Editor/Backpack/ItemPackOrientation.cs) (`Tools/SpaceGame/Items/Fix Artifact Pack Orientation`), which turns the geometry and, in `Reframe` mode, compensates `rotationOffset` by the inverse so **the pose in the hand is preserved exactly**. For a builder-owned prefab, put the rotation in the builder instead.
- **No `HoldAnimator` needed** — `OnEquipped` adds one when `UsesHoldPose`; override `UsesHoldPose => false` for worn items. `HoldStyle.None` is not authorable (silently corrected to `OneHanded`).
- **A collider left live in the hand** shoves the holder around; `Sanitize` disables the whole hierarchy. `keepColliders` is an escape hatch, not a default. Nothing is restored on unequip — the instance is destroyed.
- **A refilling item must override `OnMaxUsesReached`** to stay silent, or `EquipmentController.ItemDepleted` removes it from the inventory.
- **Deleting a prefab** nulls `InventoryItem.itemPrefab` silently — restore by GUID.
- Audits: `Tools/SpaceGame/Items/Audit Held Item Poses`, `Tools/SpaceGame/Items/Audit Item Scale Ladder`, `Tools/SpaceGame/Items/Audit Pack Orientation (whole roster)`, `Tools/Generate All Item Icons`. Tests: [HoldPoseTests](Assets/Game/Editor/Tests/HoldPoseTests.cs), [GripFrameTests](Assets/Game/Editor/Tests/GripFrameTests.cs), [HoldLatchTests](Assets/Game/Editor/Tests/HoldLatchTests.cs).

## Extending — add a new item

1. Mesh → `Assets/Game/Art/Models/Items/<Name>.fbx` (`blender-model` skill), or reuse one.
2. Script → `Assets/Game/Scripts/Items/Artifacts/…/<Name>Artifact.cs`, namespace `SpaceGame.Items`, subclass `ToolItem` or `EffectItem`. No asmdef under `Scripts/Items`; editor tests go in `Assets/Game/Editor/`.
3. Prefab → `Assets/Game/Prefabs/Items/…/<Name>.prefab` with `NetworkObject`, `PickupableItem`, collider, Rigidbody, `DropItemPhysics`, the script, and an `ItemGrip`. Prefer an editor builder script when it nests an FBX.
4. Item asset → `Assets/Game/Resources/Items/<Category>/<Name>.asset` (`Create > Items > Item`); set `itemName` + `itemPrefab`.
5. Back-reference: set `PickupableItem.item` on the prefab to that asset.
6. Icon: `Tools/Generate All Item Icons`.
7. Grip: set `holdSize` from the bracket table in `ItemScaleLadder` (add the prefab to `Ladder`), then tune `rotationOffset`/`positionOffset` and re-run the pose audit.
8. Register the prefab: `Tools/SpaceGame/Multiplayer/Sync Network Prefabs`.
9. Route it in: `startingItems` on [PlayerCharacterNetworked.prefab](Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab), an `EntityLootTable`, a `TradeOffer`, or a prefab instance in a chunk scene. The dev browser (I) needs nothing.
10. If the item holds runtime state, override `CaptureItemState`/`RestoreItemState` (call `base`) and implement `IItemDeferredRestore` when the state names another world object.
11. Verify: EditMode tests, then **on an actual client** (equip, use, drop, re-pick-up) and **across a save/quit/load**.
