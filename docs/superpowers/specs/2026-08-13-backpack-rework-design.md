# Backpack rework — deploy direction, hotbar swapping, expedition rig

Date: 2026-08-13
Branch: `Feat/robotics-and-minigame`
Supersedes parts of: `2026-08-12-astronaut-backpack-design.md` (read its section 8b first —
it lists six things that spec got wrong and implementation corrected)

Four changes, in dependency order: two code fixes that stand alone, then a model rebuild
that the code has to be generalised for, then two numbers.

---

## 1. The pack must deploy in FRONT of the player

> **Root cause, found during verification — read this first.** The drop direction was only half
> the story, and the smaller half. The pack was landing behind the player because
> `OnDisable` finished an interrupted deploy at the pack's **half-travelled pose** rather than at
> its destination. The arc starts on the player's back, so an interruption in its first frames
> leaves the pack a few centimetres behind them — unparented, `IsWorn` false, state `Open`.
> Every symptom reads as a completed deploy, and the pack is behind the player.
>
> It fires constantly rather than rarely because this is a streaming world: the player is
> disabled and re-enabled as scenes load, migrate and respawn around them.
>
> That is why fixing the drop direction never made the bug go away — and it is the reason it
> kept coming back. Both fixes are kept: the aim-source fix below is still correct and still
> needed, and the interrupted-arc fix is what actually closes this bug.
>
> Measured in play mode before the fix: aim source resolved correctly to `Main Camera`,
> `Dot(aim, body) = 1.00`, `inverted = false`, drop point computed 1.6 m in front and confirmed
> to hit ground — and the pack still ended up 0.49 m **behind** the player.

### What is wrong with the aim source

The player reports it lands behind them **every** deploy. Static reading of
`BackpackController.TryFindGroundPose` says it should not: it takes flattened
camera-forward times `deployDistance` from the player root, and the root is the transform
`PlayerLook` yaws (`playerRigidbody.MoveRotation`, PlayerLook.cs:85). The rotation is right
too — the pack's local +Z is the door side (verified against `SOCK_Int_*` sitting at Unity
z = -0.15 where Blender authored them at y = +0.150, so Blender +Y "back" maps to Unity -Z),
and the pose points +Z at the player.

So the failure is in the **aim source**, not the arithmetic. `aimTransform` is empty on
`PlayerCharacter.prefab`, so `Awake` falls back to:

```csharp
var cam = GetComponentInChildren<Camera>(true);
```

That takes the first camera in hierarchy order **including inactive ones**, with no check
that it is the camera the player is actually looking through. The project already has a
canonical aim source that this ignores: `PlayerController.PlayerCameraTransform` and
`AimProvider.GetAimRay()`.

### Fix — three layers

**Layer 1: resolve the aim from the project's own source.** In priority order:

1. serialized `aimTransform` (manual override, unchanged)
2. `PlayerController.PlayerCameraTransform`
3. `AimProvider`'s camera (reflection-free: read the component and its ray origin)
4. `transform` (the body) — always valid, never null

Never a bare `GetComponentInChildren<Camera>`.

**Layer 2: sign guard.** After flattening, compare against the body:

```csharp
if (Vector3.Dot(forward, transform.forward) < 0f) → use transform.forward, log an error
```

In a first-person rig the camera carries pitch only, so its flattened forward and the body's
forward can never legitimately disagree by more than rounding. A negative dot therefore means
the aim source is wrong, and the guard both corrects the frame and **names the offending
transform in the log** — which is what turns this from a fourth patch into a diagnosis.

The guard is deliberately `< 0` rather than a tolerance. A ±90° disagreement is still placed
in front; only an actual inversion is overridden. A tighter threshold would fight legitimate
third-person cameras if this rig ever gets one.

**Layer 3: prove it, do not assert it.**

- Every deploy logs `Dot(packPosition - playerPosition, playerForward)` at `Debug.Log` level
  behind a `verboseDeploy` toggle, defaulted off after verification.
- `OnDrawGizmosSelected` draws the computed drop point and the aim ray in the Scene view, so
  the direction is visible without pressing Play.
- An EditMode test drives the placement maths directly with a known body rotation and asserts
  the result is in front (see Testing below).
- A Play-mode run through the Unity MCP bridge, with a screenshot, before this is called done.

**Distance.** `deployDistance` 1.15 → **1.6 m**. The rebuilt pack is 0.54 m deep and its
origin is at the bottom centre of its footprint, so at 1.15 m its near face would sit ~0.88 m
from the player's chest — close enough to fill the view and read as "on top of me". At 1.6 m
the near face is ~1.33 m away and the whole 1.53 m pack is in frame.

**Layer 4: land at the destination, not at the interruption.** `Deploy` stores the grounded
pose it computed; `OnDisable` finishes an interrupted arc at that stored pose rather than at
`CurrentWorldPose(Pack)`. Cleared by both `FinishDeploy` and `SnapToWorn`, so a stale target
can never be applied to a later deploy.

### Non-goals

The drop pose is still captured once at press time and not re-followed. Walking forward
during the 0.9 s arc still leaves the pack beside you; that is correct — the pack goes where
you were standing when you set it down.

---

## 2. Swapping items when the hotbar is full

### What is wrong

`BackpackObject.TryTakeToHotbar` tests `hotbar.TryAddItem` and returns false when the 4-slot
hotbar is full, leaving the item in its pocket. There is no path to exchange.

### Rule

**The pack item goes into the selected hotbar slot; the item that was in that slot goes into
the pack slot that was just emptied.** One interaction, no new keys, no new UI.

With nothing selected (`SelectedSlotIndex == -1`), the swap targets **slot 0**. Refusing
would be a worse answer — the player is aiming at an item and pressing interact, and "nothing
happened" is the failure mode this change exists to remove.

### Mechanism

```
TryTakeToHotbar(compartment, index, hotbar):
    slot = Container.GetSlot(compartment, index)
    if slot empty                       → false
    if hotbar.TryAddItem(slot.Item)     → Container.TakeOut(...); true      // unchanged path
    else                                → SwapWithHotbar(compartment, index, hotbar)

SwapWithHotbar(compartment, index, hotbar):
    target = hotbar.SelectedSlotIndex >= 0 ? hotbar.SelectedSlotIndex : 0
    held   = hotbar.GetSlot(target)?.Item
    if held == null                     → false      // cannot happen on a full hotbar; guard anyway
    packItem = slot.Item

    hotbar.TryRemoveItem(target)        // clears the slot; does NOT drop into the world
    hotbar.TryAddItem(packItem)         // the only empty slot IS target, so it lands there
    Container.TakeOut(compartment, index)
    Container.PlaceAt(compartment, index, held)
    hotbar.SelectSlot(target)
    return true
```

Three things this design depends on, each verified in the source:

1. `Inventory.TryRemoveItem` clears the slot and raises `OnSlotChanged`. It does **not** spawn
   a world pickup — that is `PlayerInventory.DropItem`, a separate method. So a swap cannot
   leak an item onto the ground.
2. `Inventory.TryAddItem` fills `FindEmptySlot()`. On a hotbar that was full and has had
   exactly one slot cleared, that slot is the target. This is why no new `IPlayerInventory`
   member is needed — **the interface is untouched**, which matters because
   `PlayerInventoryNetwork`, `PickupableItem`, `ShipInteraction` and `RepairWorkstation` all
   implement or consume it.
3. `PlayerInventory.TryRemoveItem` sets `SelectedSlotIndex = -1` when it removes the selected
   slot (PlayerInventory.cs:76-77). Without the closing `SelectSlot(target)` the player ends
   the swap holding nothing, with the new item sitting unselected in their hand slot. This is
   the single trap in the whole feature and it gets its own test.

`SelectSlot` toggles to -1 when called with the already-selected index — harmless here,
because the selection is already -1 by the time it is called.

### New container method

`BackpackContainer.PlaceAt(compartment, index, item)` — put an item into one specific slot,
returning false if the slot is occupied or the item is null. Needed because `TryAdd` fills the
*first free* slot: without it the swapped-out item would jump to a different socket than the
one the player is looking at, which reads as the pack shuffling itself.

It writes through `Inventory.SetItem` and raises the change event, so the display rebuilds the
socket exactly as `TryAdd` does.

---

## 3. New rig — flap-top expedition pack

### Why the rig changes at all

The current pack is a two-door cabinet: a good container, a poor backpack. The brief is a big
camping pack carried across a foreign planet — oxygen, tubes, netting, cord, bulging pockets,
nothing square.

The hard problem: a top-loading pack hides its contents down a vertical tube, and this pack's
whole interaction is *seeing your gear as 3D meshes and aiming at them*. The cabinet existed
to solve exactly that.

### Answer: flap-top silhouette, drawbridge front panel

Real expedition packs have a full-length front zip. Ours folds down.

| Part | Hinge | Motion |
|---|---|---|
| `PIVOT_Lid` | back top edge, axis = local X | storm flap swings up and back, ~110° |
| `PIVOT_Panel` | **bottom** front edge, axis = local X | front panel folds down to a mat, ~100° |

Opening therefore *unpacks the pack onto the ground*: the cargo net and everything lashed
under it ride on the front panel, and as the panel falls to horizontal that gear ends up lying
face-up in front of the player. That is the "camp unloaded" read the brief asks for, and it
comes free from the geometry.

**Interior sockets stay on static shelves inside the carcass.** This is load-bearing and was
learned the expensive way on the last pack: an anchor parented to a moving panel seats its
item along the panel's normal, so the moment the panel swings, every item juts out into thin
air on the end of a board. Shelves are the only placement that stays natural through the whole
swing, because the item is resting on something the entire time.

The exterior net anchors DO ride the front panel, and that is fine — `BackpackSeat.LieFlat`
lays them against the surface, so they stay hugged to the panel through the fold and finish
lying flat on it.

### Code generalisation

`BackpackObject`'s two hardcoded `doorPivotLeft` / `doorPivotRight` fields, and the mirrored
`Quaternion.Euler(0,0,±angle)` swing, are replaced by:

```csharp
[Serializable] public struct BackpackHinge
{
    public Transform pivot;
    public Vector3 localAxis;     // normalised at use; (1,0,0) for both new hinges
    public float openAngle;       // degrees, signed
}
[SerializeField] private BackpackHinge[] hinges;
```

Each hinge's closed rest rotation is captured in `Awake` and the open pose applied
**relative** to it (`closed * AngleAxis(angle, axis)`), never as an absolute Euler — the FBX
hands empties back at their authored rest rotation, and treating that as identity buried the
previous doors 0.4 m underground.

This makes the component drive the old clamshell, the new flap-top, or a future design with
no code change. The old two-door prefab keeps working if the hinge array is filled with
`{L, (0,0,1), +135}` and `{R, (0,0,1), -135}`.

### Model content

Built in the library frame (+Z up, −Y forward), origin at bottom centre of the footprint,
matching the rest of `components/props/`.

- **Carcass** — soft-shouldered canvas body, tapered narrower at the base, top rolled into a
  collar. No box primitives anywhere in the silhouette.
- **Oxygen** — twin cylinders in a steel cradle on the frame beside the spine, domed caps,
  valve manifold, pressure gauge with an amber lamp.
- **Tubes** — braided lines from the manifold up through the shoulder harness, with clips.
  Reuses the existing `bent_tube()` helper.
- **Pockets** — lofted, bulging side pouches and lid pockets built from swept profiles, each
  with its own storm flap and buckle. Deliberately unsquare.
- **Cord lacing** — full-height zig-zag lacing down both sides through brass eyelets.
- **Cargo net** — knotted net over the front panel, and a second course over the lid.
- **Extras** — bedroll lashed under the base, antenna whip, exoframe skids.

Materials reuse the existing palette (`Mat_Fabric_Canvas_Faded`, `Mat_Metal_Steel_Worn`,
`Mat_Metal_Rust_Heavy`, `Mat_Metal_Brass_Tarnished`, `Mat_Plastic_Rubber_Black`,
`Mat_Emissive_Amber`) plus one addition for the oxygen bottles.

### Sockets — counts unchanged

**10 exterior, 12 interior**, the same `SOCK_Ext_0..9` / `SOCK_Int_0..11` names.
`BackpackContainer.StrapSlots` and `MainSlots` do not move, so no constant, test or slot-logic
churn.

Exterior placement follows one rule discovered while building it: **a socket has to present
its gear in BOTH states.** The panel is hinged on its bottom edge — the only hinge that lays
it out on the ground — and a board that falls forward about its own base necessarily arrives
outer-face DOWN, putting anything netted to it underneath. So the panel keeps its cargo net as
the visual it is meant to be and carries only the pair whose job is specifically to show gear
on a worn pack:

- `SOCK_Ext_0..1` — under the lid net, riding the lid back as it opens
- `SOCK_Ext_2..3` — side pouch outer flanks (static)
- `SOCK_Ext_4..5` — side pouch top nets (static)
- `SOCK_Ext_6..7` — outrigger loops on the frame (static)
- `SOCK_Ext_8..9` — front panel, under its net
- `SOCK_Int_0..8` — three across on each of three static shelves in the main bay
- `SOCK_Int_9..11` — three in the brain compartment the lid uncovers (also static)

Every socket obeys the existing rule, asserted by `dump_sockets()` closed **and** open: local
+Y points out of its own mouth.

### Build discipline

Written as a **new** `expedition_backpack.blend` with its own generator, not an overwrite of
`field_backpack.blend`. Three reasons: the shipped .blend is the source of truth and may carry
hand edits a generator would destroy; `field_backpack.py`'s own header forbids re-running it
over its output; and a new file makes the swap one prefab reference, so this is reversible
without touching git history.

`field_backpack.blend`/`.fbx` and `FieldBackpack.prefab` stay in the project untouched.

---

## 4. Sizes

As built (the shipped prefab values, not the code defaults, are what the player sees — the old
prefab carried 0.19 / 0.30 rather than the 0.13 / 0.22 in code):

| | Before | After |
|---|---|---|
| Pack bounds | 0.85 × 0.47 × 1.33 m | **1.17 × 0.75 × 1.59 m** |
| Body alone | 0.85 m wide | **0.90 m wide** |
| `pocketFitSize` | 0.19 m | **0.28 m** (+47%) |
| `strapFitSize` | 0.30 m | **0.42 m** (+40%) |
| `deployDistance` | 1.15 m | **1.6 m** |

Wider and deeper than the +15% the brief asked for, because the flap-top rig adds side pouches
and front pockets the cabinet did not have; the body itself is close to target. The fit sizes
are set by geometry rather than by the percentage: 0.28 m is the column pitch on the shelves,
and going past it makes neighbouring items overlap.

"Artifact items" in the brief are the game's items generally (`Resources/Items/Artifacts/*`),
not a distinct type — so the two fit sizes cover all of them.

Shelf spacing and net course spacing in the new model are set from the *new* fit sizes, not
the old: a 0.21 m item on a shelf pitched for 0.13 m ones would clip the shelf above.

---

## Testing

EditMode tests live in `Assets/Game/Tests/Editor/` — **not** `Tests/EditMode/`. That folder has
an asmdef, and a Unity asmdef cannot reference the predefined `Assembly-CSharp` where
`SpaceGame.Items` lives; tests there fail with CS0234. A folder named `Editor` outside any
asmdef lands in `Assembly-CSharp-Editor`, which already references what is needed.

New tests:

1. `PlaceAt` fills the named slot, refuses an occupied slot, refuses null, raises one change.
2. Swap on a full hotbar: pack item ends in the selected hotbar slot, held item ends in the
   pack slot that was aimed at, no item is lost, and **the selection is restored** (the
   `TryRemoveItem` trap).
3. Swap with `SelectedSlotIndex == -1` targets slot 0.
4. Non-full hotbar still takes the plain path and leaves the pack slot empty.
5. Deploy placement: with a body yawed to a known angle, the computed drop point satisfies
   `Dot(point - playerPos, playerForward) > 0`, including when the aim source is deliberately
   inverted (the sign guard).

Play-mode verification through the Unity MCP bridge, per
`project_headless_verification`: delete the results file and run the suite **twice**, or stale
results and old assemblies are read. Screenshot of the deployed pack from the player's view.

## Order of work

1. `BackpackContainer.PlaceAt` + tests
2. `BackpackObject` swap + tests
3. `BackpackController` aim resolution, sign guard, gizmo, distance + test
4. `BackpackHinge[]` generalisation, old prefab refilled so nothing breaks mid-way
5. New Blender model → FBX → prefab, sockets and hinges wired
6. Fit sizes, Play-mode verification, screenshots
