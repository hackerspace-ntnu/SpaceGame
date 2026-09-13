# Body Equipment — design

**Date:** 2026-09-02 · **Status:** draft for review · **Branch:** fix-ship-intro-scene

Gear you wear, not gear you hold. Two gauntlet slots fired on **Q** (left) and **E** (right), one
back slot deployed on a **double-tap of Space**, and the hand hotbar cut from four slots to three.
**F** opens a body screen where the six on-body slots are arranged. Interact moves to **I**, the dev
artifact browser to **O**. Six existing artifacts become forearm-worn gauntlets; three of them get
their model rebuilt on a shared gauntlet base. The backpack is untouched.

## 1. Why

Today every artifact is a hand item: select a hotbar slot, it appears in the palm, left-click fires
it. The grappling hook, the leash and the scanners are things a person would *wear*, and holding
them costs the hand that should be holding the bazooka. Wearing them on the forearm and firing them
on their own keys makes "what's on my arms" a real loadout decision — two of six devices at once
(GDC-L1-DESIGN-0002, a tradeoff with no dominant pick) — and frees the hand hotbar for things that
are actually held.

**Constitution notes.** Q/E for left/right is a natural mapping (GDC-L1-UX-0005: the most frequent
new verbs on the easiest keys next to WASD). The layout of the F screen borrows the Minecraft
silhouette so which slot is which needs no label (GDC-L1-UX-0004). The double-tap window is fixed
and short so it never reads as an assist (GDC-L1-FEEL-0003), and the first tap still jumps.
**Flagged:** moving Interact off E breaks a strong PC convention (GDC-L1-UX-0004 warns about
exactly this). It is the user's decision and this design follows it; the pause menu's bindings page
and every "press E" prompt are updated so the game never contradicts itself.

## 2. Decisions already made (from the brainstorm)

| Question | Answer |
| --- | --- |
| The three bottom slots in the F screen | **They are the hand hotbar, shrunk 4 → 3.** Keys 1-3, scroll, left-click as today. A gauntlet or the wing pack placed there is inert. |
| Repulsor Gauntlet | **Becomes a gauntlet too.** Six gauntlets: Grappling Hook, Sucker Puncher (the "brass knuckle"), Leash, Item Scanner, Ruin Scanner, Repulsor Gauntlet. |
| Gauntlet models | **All six rebuilt as one forearm family on a new `gauntlet_base` clamshell bracer** — see §7. |
| Back items | Wing Pack only, for now. |

## 3. Scope — four sub-projects, one spec

Each part is its own implementation plan. Build order 1 → 2 → 3; part 4 is independent and can run
in parallel with 2 and 3.

1. **Input** — rebinds and the new actions.
2. **Body equipment core** — slots, eligibility, wearing, per-channel use, netcode, save.
3. **Body screen + HUD** — the F overlay and the gauntlet/back tiles on the HUD.
4. **Gauntlet models** — one new base component, six new device models, six prefab re-fits.

"Done" for the feature is all four, verified on a real client and across a save/quit/load.

## 4. Part 1 — Input

### Bindings

Edit [InputSystem_Actions.inputactions](../../../Assets/Game/Settings/Input/InputSystem_Actions.inputactions)
and **reimport it so `InputControls.cs` regenerates** (CoreServices.md, "Add an input binding").
Grep the generated file for every new action name before believing the rebind.

| Map / action | Before | After |
| --- | --- | --- |
| Player / Interact | E | **I** |
| UI / DevInventory | I | **O** |
| Player / GauntletLeft *(new)* | — | **Q**, gamepad left shoulder |
| Player / GauntletRight *(new)* | — | **E**, gamepad right shoulder |
| UI / BodyInventory *(new)* | — | **F**, gamepad button west |
| Player / Turn (Q/E axis, mounts only) | Q/E | unchanged |
| Hotbar / Hotbar4 | 4 | still bound; selecting slot 3 on a 3-slot bar is refused |

Q and E stay on the `Turn` axis because every mount steers with it. The gauntlet triggers are
therefore **ignored while the player is mounted** (§5.5). Nothing else in the game reads Q/E on
foot. F and O are unbound today.

### `PlayerInputManager`

- New events, bound once in `BindActions`: `OnGauntletPressed(ItemGrip.Hand)`,
  `OnGauntletReleased(ItemGrip.Hand)`. Both press and release, because the grapple is a hold
  item and its winch latches on the button.
- New event `OnBodyActivatePressed`: two `Jump` performs within `bodyActivateDoubleTap` seconds
  (serialized, default 0.3). Detection lives in a pure `DoubleTap` class
  (`Items/Inventory/Core/DoubleTap.cs`, no UnityEngine) so it is unit-testable. The first press
  still reaches `Movement.OnJump` unchanged; the second is a jump too if the body is grounded,
  and nothing if it is not — which is precisely when the wing pack can deploy.
- `PackStowKeys` stops being the literal `4` and follows the hotbar size the pack reads.

### Pause menu bindings page

`PauseMenuUI.BuildControlsPage` rows: Interact **I**; Gauntlets **Q · E**; Body gear **F**;
Deploy back item **Space, twice**; Select slot **1 – 3**; Artifact browser **O**. Every
"press E" in a runtime string (`InteriorTestBootstrap` log, doc comments) says I instead.

## 5. Part 2 — Body equipment core

### 5.1 Model

- `BodySlot` enum: `Back = 0`, `LeftGauntlet = 1`, `RightGauntlet = 2`. Values are persisted and
  travel on the wire — append only.
- `EquipKind` enum on `InventoryItem` (serialized, default `Hand`): `Hand`, `Gauntlet`, `Back`.
  Set `Gauntlet` on the six gauntlet assets and `Back` on WingPack.
- `BodySlotRules` (pure static): `Accepts(BodySlot, EquipKind)` — Back takes `Back`, either
  gauntlet takes `Gauntlet`. A hotbar slot accepts any kind (storage), but the hand only
  *equips* `Hand`.
- Slot addressing across the two areas: `GearRef { GearArea area; int index; }` with
  `GearArea.Hotbar` / `GearArea.Body`. `UseSlotCode.Encode(GearRef)` packs it as
  `area << 8 | index` for `NetArg.A`, so every existing hotbar press (`A` = 0..2) is unchanged
  and the server's stale-slot guard still reads it.

### 5.2 Ownership and replication

`BodyEquipmentNetwork` — a `NetworkBehaviour` on `PlayerCharacterNetworked.prefab` beside
`PlayerInventoryNetwork`, same shape:

- `NetworkList<FixedString64Bytes>` of three item ids, server-authoritative; a local
  `Inventory(3)` mirror on every machine so body slots carry an `ItemState` bag exactly like
  hotbar slots; `AdoptCurrentState` for late joiners; events `OnBodySlotChanged(BodySlot,
  InventorySlot)`.
- `startingBody` list on the prefab, parallel to `startingItems`.
- One request, owner-permission: `MoveServerRpc(GearRef from, GearRef to)`. The server runs
  `GearMoves.Resolve` (pure): refuse if the target does not accept the source's kind; if the target
  is occupied, it is a swap and the source slot must accept the target's kind too; refuse anything
  while the player is mounted (a deployed wing pack must not be moved out from under its craft).
  Applied by writing both areas' NetworkLists; the hotbar side goes through a new server-only
  `IPlayerInventory.TrySetSlot(int, InventoryItem)`.
- **Moves reset per-instance state.** A NetworkList write arrives per index and assigning
  `InventorySlot.Item` clears its bag on every machine — the same rule the backpack already
  lives with. Documented in Gotchas, and the only state that matters (the wing pack's craft) is
  guarded by the mounted check above.
- Local-only `PlayerInventoryComponent` gets a matching `BodyEquipmentComponent` so the offline
  player prefab keeps working; both implement `IBodyEquipment` (the seam the UI, savers and
  controllers use).

### 5.3 Wearing

`BodyEquipmentController` (MonoBehaviour on the player, sibling of `EquipmentController`)
derives the worn instances from replicated state on **every** machine, never from a message:

- One `EquipItemSocket` per body slot: gauntlets on the right/left hand bones — **separate socket
  objects from the held-item sockets**, so a bracer on the forearm and a bazooka in the palm
  coexist — and the back on the spine bone `BackpackController` already resolves.
- Gauntlets seat through the existing hand grip frame and `ItemGrip` offsets, mirrored for the
  left hand by `HandGripFrame.Derive(isRightHand: false)`. If a bracer authored for the right
  forearm reads wrong on the left, the instance root gets `localScale.x = -1` behind a new
  `ItemGrip.mirrorInOffHand` flag — decided per prefab at fit time, not assumed.
- The back seats from a `WornFit` component on the item prefab (local position, euler and size
  in the bone's frame). Authored on `WingPack.prefab` through `PrefabUtility.LoadPrefabContents`,
  never a `WingPackBuilder` re-run (Ornithopter.md: the builder is lossy).
- Equip order per slot is the hand's: `Instantiate` → sanitize → seat → `usable.Worn = true` →
  `OnEquipped(holder)` → `RestoreItemState(slot.State)`. `UsableItem.OnEquipped` skips the
  `HoldAnimator` when `Worn`, so a worn gauntlet never poses the arm; every item's own
  `OnEquipped` setup (scanner blackout, puncher ram rest, grapple channel listen) still runs.
- Unequip mirrors the hand: write back state, end any hold stream, `OnUnequipped`, destroy.
- Hand hotbar: `EquipmentController.HandleEquip` equips only `EquipKind.Hand`. Selecting a slot
  holding a gauntlet or the wing pack leaves the hands empty, and the HUD tile says so (§6.3).

### 5.4 Using — one pipeline, four channels

`EquipmentController` already contains the whole request → present → authority → broadcast
pipeline and its hold stream, once, for the one held item. It is extracted into a reusable
`UseChannel` (plain class, `Items/Inventory/Core/UseChannel.cs`):

- Owns: the `GearRef` it fires for, a resolver for the current `UsableItem`, and the hold
  state (`useHeld`, `useButtonDown`, `nextHoldSend`, the 15 Hz interval).
- Methods: `Press()`, `Release()`, `Tick()`, `EndHold(send)`, and the four handlers
  `OnUseRequested`, `OnUsedElsewhere`, `OnHoldRequested`, `OnHeldElsewhere`.
- `EquipmentController` keeps one channel (hotbar) plus the `WallAimController` guard, which is
  a Use-button rule and does not apply to Q/E. `BodyEquipmentController` owns three.
- Both controllers subscribe to the same four `NetMsg`s on the player's relay and each dispatches
  only the messages whose decoded `A` names one of its channels. Server guards: hotbar → slot
  equals the selection (unchanged); body → the slot still holds an item.
- Q/E: press → left/right channel `Press`, release → `Release`. Continuous items (grapple)
  stream hold ticks exactly as they do from the hand. Double Space: back channel `Press` then
  `Release` on the same frame (the wing pack is not continuous).

No artifact changes its `Use`/`Present` split. Aim still comes from `ToolItem.aimProvider` on
the owner, which is a camera ray, not a hand position; the grapple's rope pays out of its own
`muzzle`, the puncher's ram animates on the instance, the scanner's screen is on the instance.

### 5.5 Gates

- `PlayerInputManager` disabled (death, menus, cutscenes) → no gauntlet or body events, as for
  every other action.
- **Mounted** → only the back press is dropped in `BodyEquipmentController` (a double Space is the
  ornithopter's flap, and a craft is already under the pilot). Gauntlets fire while riding and
  flying — decided 2026-09-02 after the first build; no shipped mount binds the Q/E Turn axis, so
  there is nothing to fight. The mount seam is whatever `MountModule` already exposes on the rider.
- A gauntlet slot with nothing in it → the press does nothing, silently, like an empty hotbar
  slot.

### 5.6 Persistence

- `BodyEquipmentSaveable`, key `"body"`, on the player next to `PlayerInventorySaveable`, using
  the same codec shape (`itemIds` positional by `BodySlot`, `itemStates` positional, omitted when
  empty) via a shared `GearSaveCodec` extracted from `InventorySaveCodec` so the two cannot drift.
- Capture writes back every worn instance's live state first (the wing pack's `craft`).
- Restore: server `RestoreSlots` → per-slot bags → reapply to worn instances → `OnLoadComplete`
  asks every worn `IItemDeferredRestore` (`TryCompleteRestore` is idempotent, as required).
- **Old saves with four hotbar entries.** `RestoreSlots` drops entries past the size silently
  today. The hotbar restore now routes an overflowing entry to the first free body slot that
  accepts its kind, and otherwise logs a warning naming the item. Written as a Gotcha; a dropped
  item is exactly the silent failure INVARIANTS.md forbids.
- Hotbar save format is unchanged (`inventory` key, three entries).

### 5.7 Multiplayer proof

Single-player is a host of one, so nothing here is verified until a real client has: worn a
gauntlet visible to the host, fired a grapple from Q and swung, seen the host's gauntlets, moved
items through the F screen, deployed wings on a double Space, and reloaded a save with all of it.

## 6. Part 3 — Body screen and HUD

### 6.1 `BodyInventoryUI`

A gameplay overlay in the `DevInventoryUI` mould (`Presentation/UI/Pages/BodyInventoryUI.cs`):
`[RuntimeInitializeOnLoadMethod]` singleton on a `DontDestroyOnLoad` object, its own
`InputControls` with only the UI map live, F toggles it, `GameplayMenuScope.Enter(this,
freezeTime: false, hideHud: true)`. Refuses to open with no local player, while mounted, or while
a text field is focused. Esc closes it as every overlay does.

### 6.2 Layout

One centred panel, six tiles laid out as a body seen from the front:

```
              [ Back  ·  Space ×2 ]
   [ Q · Left ]              [ Right · E ]

       [ 1 ]      [ 2 ]      [ 3 ]
```

Tiles are the hotbar's look — `InventorySlotUI` draws one today, welded to `InventoryUI`. The
tile drawing is extracted into `GearTile` (`Presentation/UI/HUD/GearTile.cs`) so the hotbar, the
HUD additions and this screen share one appearance; `InventorySlotUI` becomes a thin owner of a
`GearTile`. No PNGs, `UITheme` sprites throughout.

### 6.3 Interaction

Click-to-carry, as Minecraft does it and as the backpack's hand already does:

1. Click a filled tile → its icon follows the cursor; the origin tile draws empty-but-reserved
   (the hotbar's `heldOrigin` treatment).
2. Hovering a tile rings it green or red from `BodySlotRules` — the local prediction of the
   server's answer, never the decision.
3. Click a green tile → `MoveServerRpc`; the icon snaps back to its origin until the NetworkList
   change redraws both tiles. Click red → the tile shakes (the hotbar's refusal), the carry
   continues. Click the origin, Esc or F → put it back.
4. Nothing is dropped from this screen. Drop stays G on the selected hotbar slot. The backpack is
   not shown; it keeps B and its focus mode.

A hotbar tile holding a gauntlet or the wing pack shows a small "worn" glyph in the corner, on
the HUD bar as well, so "why can't I fire this" has a visible answer.

### 6.4 HUD

*Dropped after the first build (2026-09-02): a strip of Q/E/back tiles beside the hotbar read as loose
inventory tiles on the plain HUD. Worn gear is drawn on the F screen only; the hotbar tile keeps its
corner disc for a worn kind stored there.*

## 7. Part 4 — Gauntlet models

**Superseded the same day — what shipped is below.** The plan was to strap each device onto
`arm_cuff.blend`'s webbing cuff and change nothing else. Four of the six were built that way and
the user rejected the result: the cuff is authored for a slimmer arm than the astronaut's, so on
the rig it sank into the sleeve and the devices read as loose parts floating on a forearm. "I want
one gauntlet base that actually looks great on the astronaut. It should look like it fits on the
suit, it should be bulky. It shall look realistic in shape, but be quite simple in detail. Every
single gauntlet shall be built on this, so think about how extensions can be made."

So the family's mount is now **`components/props/gauntlet_base.blend`** — an armoured clamshell
bracer measured off the skinned suit and modelled at its TRUE size, in three variations (`Plain`,
`Mount`, `Rail`). Its dorsal hardpoint deck is the documented extension surface every device bolts
to; the contract is in the script's docstring and `gauntlet_base_BUILD.md`. Blender work follows
the `blender-model` skill and the parallel-session rules.

| Item | Geometry | Prefab |
| --- | --- | --- |
| Grappling Hook | **new** `gauntlet_grapple.blend`: launch tube, winch drum, gas bottle, seated harpoon | reseat + re-fit |
| Sucker Puncher | **new** `gauntlet_puncher.blend` on the `Rail` base: steam ram on the deck rails | rebuilt by its builder |
| Repulsor Gauntlet | **new** `gauntlet_repulsor.blend`: emitter coil, capacitor bank, glass ball | rebuilt by its builder (it was Unity primitives) |
| Leash | **new** `gauntlet_leash.blend`: hemp spool, brake lever, fairlead, snap hook | reseat + re-fit |
| Item Scanner | **new** `gauntlet_item_scanner.blend`: raked console, dial, whip antenna | reseat + re-fit |
| Ruin Scanner | **new** `gauntlet_ruin_scanner.blend`: horn emitter, housing, folding sight | reseat + re-fit |

The devices were then **doubled** on the user's second look — "I want the gauntlet items to be
quite visible; do not be afraid of size" — growing up and forward over the back of the hand, with
the elbow end held at y ≤ 0.36 so the arm can still fold. The base did not change.

Two things fell out of building it. `BodyEquipmentController.WearOnForearm` had its dorsal cross
product inverted on both arms, so every gauntlet's top faced the palm — invisible in a folded rest
pose, and shipped that way for a day. And the hand-socket fallback for a gauntlet without a
`GauntletFit` is gone: every gauntlet is on the base, so the component is required and its absence
is an error.

Prefab edits go through `PrefabUtility.LoadPrefabContents` so `NetworkObject`, `PickupableItem`,
`ItemGrip` and the artifact script are never stripped. Sizes: every gauntlet sits in the
`Fitted` bracket of `ItemScaleLadder` (sized to the forearm, pinned), and each gets an honest
`packSize` so the mat cost does not ride the hand bracket. `Tools/SpaceGame/Items/Audit Held Item
Poses` learns the worn sockets so it can render a gauntlet on the arm. Icons regenerated.

## 8. Testing

EditMode, `Assets/Game/Editor/Tests` or `Assets/Game/Tests/Editor` per the neighbours:

- `BodySlotRulesTests` — every kind × slot.
- `GearMovesTests` — move into empty, swap legal, swap refused, refused while mounted.
- `UseSlotCodeTests` — round trip; hotbar codes 0..2 unchanged.
- `DoubleTapTests` — inside window, outside window, three presses.
- `GearSaveCodecTests` — body round trip; a 4-entry hotbar save lands its fourth item in a
  matching body slot or warns.
- `WornPoseTests` — a worn equip adds no `HoldAnimator`; a hand equip still does.
- `NetworkPrefabRegistrationTests` stays green (no new network prefabs: worn instances are plain
  `Instantiate`s, exactly like held ones).

Then the headless Roslyn type-check, then the manual client and save/load checklist in §5.7.

## 9. Documentation

- New `docs/AI/systems/BodyEquipment.md` (Model → Key types → Flows → Multiplayer → Persistence
  → Gotchas → Extending) plus its plain-language entry in `docs/Human/the-systems.md`.
- Updated: Inventory.md (hotbar is 3, `EquipKind`, `TrySetSlot`, the overflow rule),
  Artifacts.md (channels, `Worn`, which artifacts are gauntlets), CoreServices.md (bindings),
  UI.md (`BodyInventoryUI`, `GearTile`, `BodyGearHUD`), Ornithopter.md (the pack is a back item
  fired by double Space; `WornFit`), ArtPipeline.md if the cuff family earns a line.
- `python3 tools/docs_check.py --index` regenerates INDEX and ROUTING.

## 10. Out of scope

- Gauntlet-specific animation clips. *(Added after the first build, 2026-09-02: the firing arm is raised
  forward through the aim rig's IK on every machine — `PlayerAimRig.RaiseArm`, `ArmRaiseLatch` — no clip.)*
- More back items, helmet or boot slots, or any slot the silhouette does not show.
- Dropping from the F screen; moving between the F screen and the backpack directly.
- Rebindable keys (the bindings page stays a reference).
- Carrying `ItemState` across a move.
