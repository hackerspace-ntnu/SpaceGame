# Body Equipment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wearable gear: two gauntlet slots fired on Q/E, a back slot deployed on double-Space, a 3-slot hand hotbar, an F screen to arrange the six, Interact on I, dev browser on O, and six artifacts re-fitted as forearm gauntlets.

**Architecture:** A second server-authoritative slot list (`BodyEquipmentNetwork`) beside the hotbar's, worn instances derived from it on every machine (`BodyEquipmentController`), and the existing press/hold/release use pipeline extracted from `EquipmentController` into a reusable `UseChannel` so Q, E and the back item fire through the same networked path as the hand. Pure rule classes (`BodySlotRules`, `GearMoves`, `UseSlotCode`, `DoubleTap`, `GearSaveCodec`) carry every decision so EditMode tests cover them without a scene.

**Tech Stack:** Unity 6000.3, Netcode for GameObjects (NetworkList/NetworkVariable/[Rpc]), Newtonsoft JSON saves, code-built uGUI (UITheme/UIBuilder), Blender 5.1 via `blender-model` skill.

Spec: [2026-09-02-body-equipment-design.md](../specs/2026-09-02-body-equipment-design.md).

**Verification loop used by every task:** `python3 tools/typecheck.py` (Assembly-CSharp only) → editor compiles (poll `Library/ScriptAssemblies/Assembly-CSharp-Editor.dll` newer than the edited file) → `Tools ▸ Tests ▸ Run EditMode Tests (headless)` → `cat Temp/headless_tests.txt` shows `FAILED=0` and `DONE`. New tests go in `Assets/Game/Editor/Tests/` (namespace `SpaceGame.EditorTools`), because every new type is in Assembly-CSharp.

**Facts that shape the tasks (verified 2026-09-02):**
- `PlayerCharacterNetworked.prefab` is a **variant** of `PlayerCharacter.prefab`. Savers (`PlayerInventorySaveable`, GUID `d73b5354…`) and `EquipmentController` live on the base; `PlayerInventoryNetwork`, `BackpackNetwork` etc. are added on the variant. `PlayerInventoryComponent` (local) is referenced only by the base and no scene uses the base directly, so no local `BodyEquipmentComponent` is built (YAGNI); `BodyEquipmentController` tolerates a missing `IBodyEquipment`.
- Q/E are the `Turn` axis mounts read through `SteerModule`. A mounted rider is parented under the seat, so `GetComponentInParent<MountModule>()` on the player is the rider-side mount test (`MountModule.MountedPlayerTransform` confirms it).
- `InputControls.cs` is generated from `InputSystem_Actions.inputactions`; the editor regenerates it on reimport. F and O are unbound today. UI map has `DevInventory` (I), Player map has `Interact` (E), `Backpack` (B).
- The hotbar HUD is `Assets/Game/Prefabs/UI/HUD/InventoryUI.prefab` (nested in `PlayerHUD.prefab`), anchored bottom-centre, 1200×200, with `inventoryGrid` a horizontal layout of `InventorySlotUI`s built in code.
- Leash `.blend` already has `Coll_Leash_Gauntlet`; item scanner already appends `Mesh_ArmCuff_Grip`; grapple bracer is on `Mesh_ArmCuff_Webbing`. Ruin scanner has no model (built-in cube).

---

## Part 1 — Input

### Task 1: Rebind and add actions in the input asset

**Files:**
- Modify: `Assets/Game/Settings/Input/InputSystem_Actions.inputactions`
- Regenerated: `Assets/Game/Settings/Input/InputControls.cs` (by the editor on reimport)

- [ ] Player/Interact binding `<Keyboard>/e` → `<Keyboard>/i`.
- [ ] UI/DevInventory binding `<Keyboard>/i` → `<Keyboard>/o`.
- [ ] Add Player actions `GauntletLeft` (Button; `<Keyboard>/q`, `<Gamepad>/leftShoulder`) and `GauntletRight` (Button; `<Keyboard>/e`, `<Gamepad>/rightShoulder`). New GUIDs for action and binding ids (`uuid4`).
- [ ] Add UI action `BodyInventory` (Button; `<Keyboard>/f`, `<Gamepad>/buttonWest`).
- [ ] Reimport the asset (Unity MCP `Unity_ManageAsset Import`, or focus the editor). Verify: `grep -n 'GauntletLeft\|GauntletRight\|BodyInventory\|"<Keyboard>/i"' Assets/Game/Settings/Input/InputControls.cs` shows the actions and the Interact path.

### Task 2: `DoubleTap` + `PlayerInputManager` events

**Files:**
- Create: `Assets/Game/Scripts/Items/Inventory/Core/DoubleTap.cs` (+ .meta)
- Modify: `Assets/Game/Scripts/Core/Input/PlayerInputManager.cs`
- Test: `Assets/Game/Editor/Tests/DoubleTapTests.cs`

- [ ] Test first: `DoubleTap(window: 0.3f)`; `Press(t: 0)` false, `Press(0.2)` true, `Press(0.3)` false (a third press does not chain), `Press(1.0)` false, `Press(1.25)` true; `Press(0)` then `Press(0.31)` false.
- [ ] Implement: `public sealed class DoubleTap { public DoubleTap(float windowSeconds); public bool Press(float now); }` — returns true when `now - last <= window` and resets `last` to `-inf` on a hit, otherwise records `now`.
- [ ] `PlayerInputManager`: fields `[SerializeField, Min(0.05f)] float bodyActivateDoubleTap = 0.3f`; events `OnGauntletPressed`/`OnGauntletReleased` (`Action<ItemGrip.Hand>`), `OnBodyActivatePressed` (`Action`). Bind in `BindActions`: `inputs.Player.GauntletLeft.performed/canceled`, `GauntletRight` likewise, and on `Jump.performed` run `doubleTap.Press(Time.unscaledTime)` → raise. `PackStowKeys` becomes `public const int HotbarKeys = 3` … no: replace with a `hotbarKeys` count read from `PlayerInventoryNetwork.inventorySize`? Simplest and honest: keep the four key actions but the consumer (`PackFocusSession`) already ignores slots ≥ size; leave `PackStowKeys = 4`, add a comment. *(Decision: leave.)*
- [ ] `PlayerInventoryNetwork.SelectSlot` / `SelectSlotServerRpc`: refuse `slotIndex >= inventorySize` (Hotbar4 key on a 3-slot bar).
- [ ] Type-check, run `DoubleTapTests`.

### Task 3: Bindings page and prompts

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/UI/Pages/PauseMenuUI.cs:658-686`
- Modify: `Assets/Game/Scripts/Core/SceneManagement/Interiors/InteriorTestBootstrap.cs:8,65,87` (E → I in text)
- Modify: `Assets/Game/Scripts/Presentation/UI/Pages/DevInventoryUI.cs` doc comments ("I" → "O")

- [ ] Rows: Interact **I**; new heading "Worn gear": Left gauntlet **Q**, Right gauntlet **E**, Body gear **F**, Deploy back item **Space, twice**; Select slot **1 – 3**; Artifact browser **O** (both rows).

## Part 2 — Body equipment core

### Task 4: `EquipKind`, `BodySlot`, `GearRef`, `BodySlotRules`, `UseSlotCode`

**Files:**
- Create: `Assets/Game/Scripts/Items/Body/BodySlot.cs` — `enum BodySlot { Back = 0, LeftGauntlet = 1, RightGauntlet = 2 }`, `enum GearArea { Hotbar = 0, Body = 1 }`, `readonly struct GearRef(GearArea Area, int Index)` with `static Hotbar(int)`, `static Body(BodySlot)`, `Equals`.
- Create: `Assets/Game/Scripts/Items/Body/BodySlotRules.cs` — `static bool Accepts(BodySlot slot, EquipKind kind)`; `static bool Accepts(GearRef target, EquipKind kind)` (hotbar accepts everything); `static bool HandEquips(EquipKind kind) => kind == EquipKind.Hand`; `static BodySlot? FirstMatching(EquipKind)`.
- Create: `Assets/Game/Scripts/Items/Body/UseSlotCode.cs` — `static int Encode(GearRef r) => ((int)r.Area << 8) | r.Index`; `static GearRef Decode(int code)`; hotbar indices 0..255 round-trip unchanged; `Decode(-1)` → `GearRef.None`.
- Modify: `Assets/Game/Scripts/Items/Core/InventoryItem.cs` — `public enum EquipKind { Hand, Gauntlet, Back }` (own file `Assets/Game/Scripts/Items/Core/EquipKind.cs`), field `[Tooltip] public EquipKind equipKind = EquipKind.Hand;`.
- Test: `Assets/Game/Editor/Tests/BodySlotRulesTests.cs`, `Assets/Game/Editor/Tests/UseSlotCodeTests.cs`.

- [ ] Tests: every kind × slot truth table; hotbar accepts all three kinds; `HandEquips` true only for Hand; encode/decode round trip for hotbar 0..2 (codes equal index) and body 0..2 (codes 256..258); `Decode(-1).IsNone`.
- [ ] Set `equipKind: 1` on the six gauntlet `.asset`s and `equipKind: 2` on `WingPack.asset` (YAML edit: add the line after `iconPrefab`).

### Task 5: `GearMoves` (pure move resolver)

**Files:**
- Create: `Assets/Game/Scripts/Items/Body/GearMoves.cs`
- Test: `Assets/Game/Editor/Tests/GearMovesTests.cs`

- [ ] API: `struct GearCell { InventoryItem Item; }` is overkill — use kinds: `static MoveResult Resolve(GearRef from, EquipKind? fromKind, GearRef to, EquipKind? toKind, bool mounted)` returning `MoveResult { bool Allowed; bool IsSwap; string Reason; }`. Rules: same ref → refused; from empty → refused; mounted → refused; target must accept fromKind; if toKind present, source must accept toKind (swap).
- [ ] Tests: gauntlet into empty gauntlet slot ok; gauntlet into back refused; hand item into gauntlet refused; swap gauntlet(hotbar)↔gauntlet(body) ok; swap hand(hotbar)↔gauntlet(body) refused (hand cannot go to body); back into back ok; anything while mounted refused; hotbar→hotbar always ok when non-empty.

### Task 6: `IBodyEquipment` + `BodyEquipmentNetwork` + hotbar `TrySetSlot`

**Files:**
- Create: `Assets/Game/Scripts/Items/Body/IBodyEquipment.cs` — `int SlotCount (3)`, `InventorySlot GetSlot(BodySlot)`, `event Action<BodySlot, InventorySlot> OnBodySlotChanged`, `void RequestMove(GearRef from, GearRef to)`, `void RestoreSlots(IReadOnlyList<InventoryItem>)` (server), `bool IsMounted` (rider seam, for the F screen and the server guard).
- Create: `Assets/Game/Scripts/Items/Body/BodyEquipmentNetwork.cs` — NetworkBehaviour, `NetworkList<FixedString64Bytes>` of 3, local `Inventory(3)` mirror, `startingBody` list, `AdoptCurrentState`, `HandleListChanged`, `MoveServerRpc(int fromArea, int fromIndex, int toArea, int toIndex)` with `RpcInvokePermission.Owner`, applying `GearMoves.Resolve` and writing both lists. Mount test: `GetComponentInParent<MountModule>() != null`.
- Modify: `IPlayerInventory` + `PlayerInventoryNetwork` + `PlayerInventoryComponent`: add `bool TrySetSlot(int index, InventoryItem item)` (server-only on the network one; writes `networkItems[index]`, `default(FixedString64Bytes)` for null; refuses off the server with a warning; clears the selection if the selected slot's item changed to a non-Hand kind? — no: `EquipmentController.HandleEquip` handles kinds).
- [ ] Type-check. No new EditMode test (NetworkList needs a spawned object); the pure part is `GearMoves`.

### Task 7: `UseChannel` extraction

**Files:**
- Create: `Assets/Game/Scripts/Items/Inventory/Core/UseChannel.cs`
- Modify: `Assets/Game/Scripts/Items/Inventory/Components/EquipmentController.cs` (remove the hold fields and the four handlers' bodies; keep `OnUse` wall guard, `HeldItem`, equip/unequip)
- Test: `Assets/Game/Editor/Tests/HoldLatchTests.cs` must still pass (it exercises the hold flow via EquipmentController? check before editing; if it touches private state, adapt).

- [ ] `UseChannel` constructor: `(Component host, GearRef slot, Func<UsableItem> item, Func<bool> slotStillValid)`; `Press()`, `Release()`, `Tick(float now)`, `EndHold(bool send)`, `OnUseRequested(in NetArg, ulong)`, `OnUsedElsewhere(in NetArg, ulong)`, `OnHoldRequested(in NetArg, ulong)`, `OnHeldElsewhere(in NetArg, ulong)`, `bool Owns(int code)` (decoded ref equals slot; for the hotbar channel `slot` is "any hotbar index": the channel is `GearArea.Hotbar` with Index -1 meaning "the selected slot" — `Owns` compares area only and `slotStillValid` does the selection check; `Press` encodes the current selection).
- [ ] `EquipmentController`: one `UseChannel hand`; `OnEnable/OnDisable` register the four NetMsgs and forward to `hand` when `hand.Owns(arg.A)`; `Update` → `hand.Tick`; `Unequip` → `hand.EndHold(true)`; `OnDisable` → `hand.EndHold(false)`.
- [ ] Behaviour must be byte-for-byte the old one for the hotbar (`A` = selected slot). Type-check; run `HoldLatchTests`, `GrappleUseFlowTests`, `LaserStaffBeamTests`.

### Task 8: `UsableItem.Worn` + `BodyEquipmentController`

**Files:**
- Modify: `Assets/Game/Scripts/Items/Core/UsableItem.cs` — `public bool Worn { get; set; }`; `OnEquipped` adds the `HoldAnimator` only when `UsesHoldPose && !Worn`.
- Create: `Assets/Game/Scripts/Items/Equipped/WornFit.cs` — `[SerializeField] Vector3 localPosition, localEuler; [SerializeField, Min(0)] float size;` read by the back socket.
- Create: `Assets/Game/Scripts/Items/Body/BodyEquipmentController.cs` — resolves right/left hand bones (same hints as `EquipmentController`; expose a static `ResolveBone` helper by moving it to `Assets/Game/Scripts/Items/Equipped/BoneResolver.cs` and using it from both) and the spine (`HumanBodyBones.Spine`, hints Spine/Chest/Torso); one `EquipItemSocket` per slot; subscribes to `IBodyEquipment.OnBodySlotChanged`; `Wear(slot)`/`Strip(slot)` per the spec's order; three `UseChannel`s (`GearRef.Body(slot)`); input: `OnGauntletPressed/Released` → left/right `Press/Release`, `OnBodyActivatePressed` → back `Press(); Release();`, all dropped while mounted; `WriteBackWornState()`, `ReapplyWornState()`, `IEnumerable<UsableItem> Worn` for the saver.
- Modify: `EquipmentController.HandleEquip`: `if (!BodySlotRules.HandEquips(slot.Item.equipKind)) { Unequip(); return; }`.
- Test: `Assets/Game/Editor/Tests/WornPoseTests.cs` — a `UsableItem` subclass instance with `Worn = true` gets no `HoldAnimator` from `OnEquipped`; with `Worn = false` it does (mirror `HoldPoseTests`' setup).

### Task 9: Player prefab wiring

**Files:**
- Modify: `Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab` — add `BodyEquipmentController` and `BodyEquipmentSaveable` MonoBehaviour blocks on the root.
- Modify: `Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab` — add `BodyEquipmentNetwork` as an added component on the variant root (copy the shape of the `PlayerInventoryNetwork` addition); `inventorySize: 4 → 3`; `startingItems` trimmed to three Hand items; `startingBody` gets the fourth if it is a gauntlet.
- [ ] Do this over Unity MCP once the scripts compile: `PrefabUtility.LoadPrefabContents` → `AddComponent` → `SaveAsPrefabAsset` for both prefabs, then `git diff --stat` the two prefabs to confirm only additions.

### Task 10: Persistence

**Files:**
- Create: `Assets/Game/Scripts/Core/Persistence/Adapters/GearSaveCodec.cs` — extracted from `InventorySaveCodec`: `CaptureSlots(IEnumerable<InventorySlot>)` → `(List<string> ids, List<Dictionary<string,string>> states)`; `ReadIds(JArray)` → `List<InventoryItem>` with the registry warning; `ReadStates(JArray, count)`.
- Modify: `PlayerInventorySaveable.cs` — `InventorySaveCodec` uses the codec; overflow rule in `Restore`: entries beyond `GetInventorySize()` go to `IBodyEquipment` via a new server-side `TryPlaceInBody(InventoryItem)` (first slot accepted by `BodySlotRules` and empty), else warn.
- Create: `Assets/Game/Scripts/Core/Persistence/Adapters/BodyEquipmentSaveable.cs` — key `"body"`, `ISaveable` + `IDeferredSaveable`; capture writes back worn state first; restore → `RestoreSlots` → states → `ReapplyWornState`; `OnLoadComplete` asks each worn `IItemDeferredRestore`.
- Test: `Assets/Game/Editor/Tests/GearSaveCodecTests.cs` — round trip through JSON text into a fresh in-memory `Inventory`; a 4-entry hotbar payload against a 3-slot inventory yields one overflow item.

## Part 3 — Body screen and HUD

### Task 11: `GearTile`

**Files:**
- Create: `Assets/Game/Scripts/Presentation/UI/HUD/GearTile.cs` — the visual half of `InventorySlotUI` (Compose, Refresh(item, selected, hovered, dropTarget, reserved, worn), Shake, key label text) as a `MonoBehaviour` built by `GearTile.Build(RectTransform parent, string keyLabel)`.
- Modify: `InventorySlotUI.cs` — owns a `GearTile`, keeps the pointer handlers and the `InventoryUI` bridge; `Refresh` forwards. Adds the "worn" glyph (a small `UITheme.CircleSprite` badge, `HotbarStyle.Thread`) when the item's kind is not Hand.
- [ ] `HoldPoseTests`/hotbar-related tests unaffected; visual check in play.

### Task 12: `BodyInventoryUI`

**Files:**
- Create: `Assets/Game/Scripts/Presentation/UI/Pages/BodyInventoryUI.cs` — singleton overlay; own `InputControls` UI map, `BodyInventory.performed → Toggle`; `Open` refuses with no local player, mounted (`IBodyEquipment.IsMounted`), or typing; `GameplayMenuScope.Enter(this, freezeTime: false, hideHud: true)`; builds canvas (sortingOrder 2050), panel 760×640, six `GearTile`s positioned per the spec; click-to-carry state machine (`carried: GearRef?`), hover prediction via `BodySlotRules`/`GearMoves.Resolve(... mounted:false)`, `RequestMove` on a legal click, `Shake` on an illegal one; redraw on `OnSlotChanged`/`OnBodySlotChanged`; Esc via `inputs.UI.Cancel` if it exists, else F only.
- [ ] Manual verification in play: open, carry, place, swap, refuse, close.

### Task 13: `BodyGearHUD`

**Files:**
- Create: `Assets/Game/Scripts/Presentation/UI/HUD/BodyGearHUD.cs` — `static void Attach(InventoryUI bar, RectTransform barRect, PlayerController player)`: three `GearTile`s (Q left of the bar, E right, back above centre, 0.75 scale), bound to `IBodyEquipment.OnBodySlotChanged`; `InventoryUI.Start` calls it when `player.GetComponent<IBodyEquipment>() != null`.

## Part 4 — Gauntlet models (parallel; `blender-model` skill)

### Task 14: Leash gauntlet
- `components/props/leash_device.blend`: `Coll_Leash_Gauntlet` seated on `Mesh_ArmCuff_Webbing` (appended from `arm_cuff.blend`) → export `Assets/Game/Art/Models/Items/leash_gauntlet.fbx` with an export script beside the .py; regenerate LIBRARY.md/library_index.json per the skill.
- `Leash.prefab`: swap the model child for the new FBX (LoadPrefabContents), keep `muzzle`/`Grip`; `ItemGrip` re-fit; `holdSize` Fitted bracket; `packSize` honest.

### Task 15: Item scanner gauntlet
- `models/gear/item_scanner.blend`: case, screen, dial, antenna and bracket on the webbing cuff; drop `Mesh_ArmCuff_Grip`; screen up along the forearm → re-export `item_scanner.fbx`.
- `ItemScanner.prefab`: re-fit; `ItemScannerScreen` still points at the screen mesh.

### Task 16: Ruin scanner gauntlet
- New `models/gear/ruin_scanner.blend` (+ `.py`, `_BUILD.md`): webbing cuff + emitter housing + lens on the top of the forearm → `Assets/Game/Art/Models/Items/ruin_scanner.fbx`.
- `RuinScanner.prefab`: replace the cube child with the FBX; keep `Grip`, `RuinScannerArtifact`, the pulse reference.

### Task 17: Re-fit all six + ladder + icons
- `ItemScaleLadder`: grapple, leash, ruin scanner → `Fitted` (pinned) rows; `packSize` authored for each.
- `HeldItemPoseAudit`: render worn items on the arm (uses `BodyEquipmentController`'s sockets).
- `Tools/Generate All Item Icons`; `Tools/SpaceGame/Multiplayer/Sync Network Prefabs` (no new prefabs expected — confirm no diff).

## Part 5 — Docs and verification

### Task 18: Docs
- New `docs/AI/systems/BodyEquipment.md`; entry in `docs/Human/the-systems.md`; update Inventory.md, Artifacts.md, CoreServices.md, UI.md, Ornithopter.md; `python3 tools/docs_check.py --index`.

### Task 19: Verification
- `python3 tools/typecheck.py`; full EditMode suite `FAILED=0`; play-mode smoke over MCP (equip gauntlet via O browser → F screen → Q fires); note client and save/load verification as required follow-up on a real client.
