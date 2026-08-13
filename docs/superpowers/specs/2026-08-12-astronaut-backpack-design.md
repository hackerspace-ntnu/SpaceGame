# Astronaut Field Backpack — Design

Date: 2026-08-12
Branch: `Feat/robotics-and-minigame`

A rustic, homemade-scifi backpack worn by the astronaut player. It carries gear on
exterior straps and in an interior compartment. Pressing a key unclips it: the pack
swings down to the player's feet, splits open like a clamshell, and the items inside
are shown as real 3D meshes. Aiming at an item and pressing Interact moves it into
the hotbar. The pack stays where it was left until the player walks back and
re-shoulders it.

---

## 1. Decisions (settled with the user)

| Question | Decision |
|---|---|
| Storage relationship | Backpack is a **separate, larger store**. The existing 4-slot `PlayerInventory` becomes the hotbar and is unchanged. |
| Movement while open | **Free to move.** The pack is a world object once it is off. |
| Exterior items | **Separate "strap" slots** — real items, visible while worn. |
| Picking items | **Crosshair + Interact key**, reusing `Interactor` / `IInteractable`. |
| Opening | **Clamshell split**, hinged along the bottom edge, falls open flat. |
| Take-off trigger | **Dedicated key toggle** (`B`). |
| Take-off motion | **Procedural swing-down arc.** No new rig animation. |
| Walking away | **The pack stays.** The player must return and re-shoulder it. |
| Model scope | **One backpack**, one collection. |
| Netcode | **Single-player now**, net-ready seams. |
| Grounded physics | **Kinematic**, placed by downward raycast and aligned to the ground normal. Not a settling rigidbody — on dune terrain it would roll away. |
| Capacity | **3 strap slots + 9 interior slots.** Hotbar stays at 4. |

### Architecture fork — resolved

The pack is a **world entity that owns its contents** (`BackpackObject` holds the
`BackpackContainer` and is the `IInteractable`). The player only holds a
`BackpackController` that knows which pack is theirs and where the back socket is.

Because the pack must survive independently of the player ("it stays where you left
it"), this makes that the normal case rather than a special one, and makes a pack
lying in a ruin or worn by an NPC the same object with no extra work.

---

## 2. Existing code this builds on

Read these before writing anything:

| Path | Why |
|---|---|
| `Assets/Game/Scripts/Items/Inventory/Core/Inventory.cs` | Slot array + `OnSlotChanged`. **Reused as-is, do not modify.** |
| `Assets/Game/Scripts/Items/Inventory/Core/InventorySlot.cs` | `Index`, `Item`, `IsEmpty`. |
| `Assets/Game/Scripts/Items/Core/InventoryItem.cs` | ScriptableObject: `itemName`, `itemPrefab`, `icon`, `ID`. |
| `Assets/Game/Scripts/Items/Inventory/Core/IPlayerInventory.cs` | The hotbar interface. |
| `Assets/Game/Scripts/Items/Inventory/Components/PlayerInventoryComponent.cs` | Hotbar impl, `inventorySize = 4`. |
| `Assets/Game/Scripts/Items/Inventory/Components/EquipmentController.cs` | **Pattern to copy** for resolving a bone socket from a humanoid `Animator`. |
| `Assets/Game/Scripts/Gameplay/Interaction/Core/Interactor.cs` | Raycast picker. `ResolveAlongRay` walks hits front-to-back. |
| `Assets/Game/Scripts/Gameplay/Interaction/Core/IInteractable.cs` | `CanInteract()`, `Interact(Interactor)`. |
| `Assets/Game/Scripts/Core/Input/PlayerInputManager.cs` | Single source of truth for input; all events live here. |
| `Assets/Game/Scripts/Items/Core/PickupableItem.cs` | Pickup path that needs the overflow change. |

### Constraints discovered

- The astronaut rig is **humanoid** (`animationType: 3` in `AstronautArmature.fbx.meta`),
  so `Animator.GetBoneTransform(HumanBodyBones.Spine)` resolves the back socket.
- The player is **human scale**: 2 m `CapsuleCollider`, root `m_LocalScale` 1,1,1.
  (The "3 m capsule" comment in `Interactor.cs` is stale.)
- The game is **first person** — `PlayerLook.Start` sets the head renderer to
  `ShadowsOnly`. Looking down at the pack at your feet works naturally.
- `Interactor.ResolveAlongRay` returns the **first** collider's `IInteractable`
  (`GetComponent` then `GetComponentInParent`). Item meshes sitting proud in their
  pockets therefore win the ray over the pack body behind them, with no extra work.
  **A solid collider with no `IInteractable` blocks the ray entirely** — so the pack's
  own collider must not cover the open mouth.
- Namespaces follow `SpaceGame.<Domain>`. All backpack scripts are `SpaceGame.Items`.
- `InputControls.cs` at `Assets/Game/Settings/Input/InputControls.cs` is **generated**
  from `InputSystem_Actions.inputactions`. Editing the action asset regenerates it.

---

## 3. The Blender model

**File:** `Assets/Game/Art/Models/_Source~/components/props/field_backpack.blend`
**Builder:** `field_backpack.py` alongside it, following the existing component convention.
**Collection:** `Coll_Backpack_Field` (one variation).

### Look

Rustic and homemade, but unmistakably built for somewhere with no atmosphere:

- A **bent steel-tube exoframe** — the load-bearing skeleton, visibly hand-bent, with
  welded cross-braces.
- A **canvas body** lashed to the frame with cord through punched eyelets.
- A **scavenged plate riveted over the back panel** — rust-patched, cut from something
  else, edges not square.
- **Tarnished brass buckles** and hand-stitched strap pads.
- A single small **amber lamp** on the lid — the one part that reads scifi rather than
  1970s expedition gear.

### Dimensions

Roughly **0.34 W × 0.20 D × 0.50 H m**, sized against the 2 m player capsule.

### Named objects (Unity depends on these names)

| Object | Role |
|---|---|
| `Mesh_Backpack_Frame` | Tube frame + shoulder straps. Never moves. |
| `Mesh_Backpack_ShellBase` | The deep half, worn against the back. Never moves. |
| `Mesh_Backpack_ShellLid` | The shallow half. Rotates open about `PIVOT_Clamshell`. |
| `Mesh_Backpack_Buckle_L` / `_R` | Brass latches. Child of the lid. |
| `PIVOT_Clamshell` | Empty at the bottom hinge line. Lid's parent. Local X is the hinge axis. |
| `SOCK_Strap_0` | Top of pack, horizontal — bedroll position. |
| `SOCK_Strap_1` / `_2` | Left / right side clip points. |
| `SOCK_Pocket_0..5` | Interior grid in the base half, 2 columns × 3 rows. |
| `SOCK_Pocket_6..8` | Shallow lid pockets, 3 across. Children of `PIVOT_Clamshell`. |

All `SOCK_*` empties point **+Y up out of the pocket mouth** and **+Z forward**, so an
item parented at identity sits upright and faces the player.

Pocket cells are ~0.13 m; strap cells ~0.22 m. These are the `fitSize` values Unity
uses to scale item meshes.

### Materials

From the existing shared palette only — no new materials:
`Mat_Fabric_Canvas_Faded` (body), `Mat_Metal_Steel_Worn` (frame),
`Mat_Metal_Rust_Heavy` (patched plate), `Mat_Metal_Brass_Tarnished` (buckles),
`Mat_Plastic_Rubber_Black` (strap pads), `Mat_Emissive_Amber` (lamp).

### Export

FBX to `Assets/Game/Art/Models/Props/field_backpack.fbx`, +Y up, +Z forward, metres,
scale 1.0. Empties export as transforms so Unity sees the sockets.

---

## 4. C# API contract

All in namespace `SpaceGame.Items`, under
`Assets/Game/Scripts/Items/Backpack/`.

This contract is **binding** — files are written in parallel against it.

### 4.1 `BackpackContainer.cs` — plain C#, no MonoBehaviour

```csharp
public enum BackpackCompartment { Strap, Main }

public class BackpackContainer
{
    public const int StrapSlots = 3;
    public const int MainSlots  = 9;

    public Inventory Straps { get; }        // new Inventory(StrapSlots)
    public Inventory Main   { get; }        // new Inventory(MainSlots)

    /// Raised for every content change, from either compartment.
    public event Action<BackpackCompartment, int, InventorySlot> OnSlotChanged;

    public BackpackContainer();

    public Inventory Get(BackpackCompartment compartment);
    public InventorySlot GetSlot(BackpackCompartment compartment, int index);
    public int SlotCount(BackpackCompartment compartment);

    /// Place into a specific compartment's first free slot.
    public bool TryAdd(BackpackCompartment compartment, InventoryItem item, out int index);

    /// Overflow target for world pickups: main compartment only. Straps are for
    /// deliberate placement, so a picked-up rock must never displace a bedroll.
    public bool TryAddToMain(InventoryItem item, out int index);

    /// Removes and returns the item, or null if the slot was empty.
    public InventoryItem TakeOut(BackpackCompartment compartment, int index);

    /// Every occupied slot, for rebuilding the visual display.
    public IEnumerable<(BackpackCompartment compartment, int index, InventoryItem item)> Contents();

    public bool IsFull(BackpackCompartment compartment);
}
```

**Rules**
- `TryAdd` / `TryAddToMain` return false and set `index = -1` when the item is null or
  the compartment is full. They never throw.
- `TakeOut` returns null for an out-of-range or empty index. Never throws.
- `OnSlotChanged` fires exactly once per slot that actually changed. It does not fire
  on a failed add or an empty take.
- `Contents()` yields in compartment order (Strap then Main), ascending index.

### 4.2 `BackpackDeployArc.cs` — plain C#, static

```csharp
public static class BackpackDeployArc
{
    /// Pose along the swing-down path. Uses UnityEngine.Pose (position + rotation).
    /// t is clamped to [0,1].
    ///   arcHeight — metres the pack rises above the straight line at midpoint,
    ///               so it swings over the shoulder rather than through the chest.
    ///   outward   — metres the pack bows away from the straight line horizontally,
    ///               along the horizontal component of (end - start) rotated 90 deg,
    ///               so it clears the player's own body.
    public static Pose Evaluate(Pose start, Pose end, float t, float arcHeight, float outward);
}
```

**Rules**
- `Evaluate(s, e, 0, ...)` returns exactly `s`; `Evaluate(s, e, 1, ...)` returns exactly `e`.
- Rotation is `Quaternion.Slerp(start.rotation, end.rotation, smoothstep(t))`.
- Position is a quadratic Bezier whose control point is the midpoint of start and end,
  displaced by `arcHeight` on world up and `outward` on the horizontal perpendicular.
- No frame of the path may sit below `min(start.y, end.y)`. This is what stops the
  pack from clipping through the ground on a downhill deploy, and is tested.
- Pure function. No `Time`, no state, no allocation.

### 4.3 `BackpackItemVisual.cs` — static helper, MonoBehaviour-free

```csharp
public static class BackpackItemVisual
{
    /// Build a display-only copy of an item prefab, parented to socket at identity,
    /// uniformly scaled so its largest bounds axis equals fitSize, and given exactly
    /// one BoxCollider for the interaction ray. Returns null if itemPrefab is null.
    public static GameObject Build(GameObject itemPrefab, Transform socket, float fitSize);
}
```

**Rules**
- Destroys every `MonoBehaviour`, `Rigidbody`, `Collider`, `Animator`, `AudioSource`
  and `ParticleSystem` on the copy before it can run — use `DestroyImmediate` on the
  instantiated copy's components in the same frame it is created, so no `Awake`
  side effects escape. Renderers and `MeshFilter`s survive.
- Computes combined `Renderer.bounds` in local space; if that is zero-sized, falls back
  to scale 1 rather than dividing by zero.
- Seats the copy so the **bottom** of its bounds sits at the socket origin, not its
  pivot — item prefabs have inconsistent pivots and would otherwise sink into the pack.
- Adds one `BoxCollider` sized to the scaled bounds, `isTrigger = false`, on the copy's
  root — so `Interactor` picks the item and `BackpackSlotView` on the same object answers.
- The copy's layer is set to match the socket's layer.

### 4.4 `BackpackObject.cs` — MonoBehaviour + IInteractable, on the pack prefab

```csharp
public class BackpackObject : MonoBehaviour, IInteractable
{
    public BackpackContainer Container { get; }
    public bool  IsOpen  { get; }
    public bool  IsWorn  { get; }

    public void Bind(BackpackController owner);       // null when dropped for good
    public void SetWorn(bool worn);                   // toggles strap-only vs full display
    public void SetOpen(bool open);                   // drives the clamshell + repopulates

    /// Move one item from the pack into the given hotbar. Returns false and changes
    /// nothing if the hotbar is full.
    public bool TryTakeToHotbar(BackpackCompartment compartment, int index, IPlayerInventory hotbar);

    // IInteractable — aiming at the pack body re-shoulders it (or opens it if it is
    // grounded and closed). Item picking is handled by BackpackSlotView, which wins
    // the ray because item colliders sit in front of the pack body.
    public bool CanInteract();
    public void Interact(Interactor interactor);
}
```

Serialized fields: `Transform clamshellPivot`, `Transform[] strapSockets`,
`Transform[] pocketSockets`, `float openAngle = 100f`, `float openSeconds = 0.35f`,
`float pocketFitSize = 0.13f`, `float strapFitSize = 0.22f`,
`List<InventoryItem> startingStrapItems`, `List<InventoryItem> startingMainItems`.

**Rules**
- Strap items are displayed whenever the pack exists, worn or not.
- Pocket items are displayed only while `IsOpen`. Closing destroys the pocket visuals.
- `SetOpen` rotates `clamshellPivot` from 0 to `openAngle` over `openSeconds`.
- Subscribes to `Container.OnSlotChanged` and refreshes only the affected socket.
- `CanInteract()` is false while the owning controller is mid-deploy or mid-stow.
- `Interact` on a grounded, closed pack opens it. On a grounded, open pack it asks the
  owner to re-shoulder. On a worn pack it does nothing (you cannot reach your own back).

### 4.5 `BackpackSlotView.cs` — MonoBehaviour + IInteractable, one per displayed item

```csharp
public class BackpackSlotView : MonoBehaviour, IInteractable
{
    public void Bind(BackpackObject pack, BackpackCompartment compartment, int index);

    public bool CanInteract();
    public void Interact(Interactor interactor);   // -> pack.TryTakeToHotbar(...)
}
```

**Rules**
- Resolves the hotbar via `interactor.GetComponent<IPlayerInventory>()`, matching how
  `PickupableItem.Pickup` does it.
- A full hotbar logs once and leaves the item in the pack. It must not silently do nothing.
- `CanInteract()` is false when the pack is not open.

### 4.6 `BackpackController.cs` — MonoBehaviour, on the Player

```csharp
public class BackpackController : MonoBehaviour
{
    public enum State { Shouldered, Deploying, Open, Stowing }

    public State CurrentState { get; }
    public BackpackObject Pack { get; }

    public void Toggle();
    public void Deploy();
    public void Reshoulder();
}
```

Serialized fields: `GameObject backpackPrefab`, `HumanBodyBones backBone = HumanBodyBones.Spine`,
`string[] backBoneNameHints = { "Spine", "Chest", "Torso" }`, `Vector3 wornLocalPosition`,
`Vector3 wornLocalEuler`, `float deploySeconds = 0.6f`, `float deployDistance = 0.9f`,
`float arcHeight = 0.45f`, `float arcOutward = 0.35f`, `LayerMask groundMask = ~0`.

**Rules**
- Resolves the back socket exactly like `EquipmentController.ResolveHandSocket`: prefer
  `Animator.GetBoneTransform` on a humanoid rig, fall back to a case-insensitive
  substring search over child transforms, then to a serialized override.
- **One persistent pack instance**, created on `Awake` and never destroyed. Deploying
  unparents it; re-shouldering re-parents it to the back socket. This keeps strap items
  visible continuously and avoids spawn/destroy churn.
- `Deploy` picks the ground point by raycasting down from
  `transform.position + forward * deployDistance + up * 1.0f` against `groundMask`,
  `QueryTriggerInteraction.Ignore`. **The raycast must ignore the player's own
  hierarchy** — see the same trap documented in `Interactor.DoInteractionTest`. If no
  ground is found, the deploy is refused and the pack stays shouldered.
- The grounded pose is aligned to the hit normal, upright side up, facing back toward
  the player.
- `Toggle` is a no-op during `Deploying` and `Stowing`.
- `Reshoulder` from a distance is allowed — the pack flies back along the same arc.
  This is the escape hatch that stops gear being permanently lost to a bad drop.
- Subscribes to `PlayerInputManager.OnBackpackPressed`; unsubscribes in `OnDisable`.

---

## 5. Input

Add a `Backpack` action to the **Player** map of
`Assets/Game/Settings/Input/InputSystem_Actions.inputactions`:

- Type `Button`, bound to `<Keyboard>/b` on the Keyboard&Mouse scheme and
  `<Gamepad>/buttonNorth` on Gamepad.
- Unity regenerates `InputControls.cs`. If the editor is not running, regenerate by
  hand and keep the generated file's existing style exactly.

Add to `PlayerInputManager`, alongside the existing events:

```csharp
public event Action OnBackpackPressed;
// in OnEnable:
inputs.Player.Backpack.performed += _ => OnBackpackPressed?.Invoke();
```

## 6. Pickup overflow

`PickupableItem.Pickup` currently fails when the 4-slot hotbar is full, so the pack
would start empty and never fill. Change it to: hotbar first, then the worn pack's
main compartment, then refuse.

```csharp
private void Pickup(Interactor interactor)
{
    IPlayerInventory inventory = interactor.GetComponent<IPlayerInventory>();
    if (inventory == null) return;

    bool added = inventory.TryAddItem(item);

    if (!added)
    {
        var backpack = interactor.GetComponent<BackpackController>();
        if (backpack != null && backpack.Pack != null)
            added = backpack.Pack.Container.TryAddToMain(item, out _);
    }

    if (added)
        GameServices.World.Despawn(gameObject);
}
```

Nothing else in the pickup path changes; the netcode RPC wrapper is untouched.

---

## 7. Unity assets

| Asset | Contents |
|---|---|
| `Assets/Game/Art/Models/Props/field_backpack.fbx` | Exported mesh + socket empties. |
| `Assets/Game/Prefabs/Items/Equipment/FieldBackpack.prefab` | FBX instance + `BackpackObject`, socket arrays wired, one `BoxCollider` on the frame that does **not** cover the open mouth. |
| `Assets/Game/Prefabs/Characters/Player/Player.prefab` | Gains `BackpackController` with `backpackPrefab` pointing at the above. |

The pack's frame collider must leave the mouth clear — a solid collider with no
`IInteractable` in front of the pockets would block every item pick
(`ResolveAlongRay` treats it as a wall).

---

## 8. Testing

EditMode tests in `Assets/Game/Tests/EditMode/`, matching the existing NUnit style:

**`BackpackContainerTests.cs`**
- Adding to a full compartment returns false, sets `index = -1`, raises no event.
- `TryAddToMain` never writes into a strap slot, even when straps are empty.
- `TakeOut` on an empty or out-of-range index returns null and raises no event.
- `TakeOut` then `TryAdd` reuses the freed index.
- `Contents()` yields strap slots before main slots, ascending, skipping empties.
- `OnSlotChanged` fires exactly once per real change.

**`BackpackDeployArcTests.cs`**
- `t = 0` and `t = 1` reproduce the endpoints exactly.
- `t` outside `[0,1]` is clamped, not extrapolated.
- No sampled point over `t ∈ [0,1]` dips below `min(start.y, end.y)`.
- With `arcHeight > 0`, the midpoint is strictly above the straight-line midpoint.
- A start and end at the same position returns that position for every `t`.

Play-mode verification over the unity-mcp bridge: compile clean, then screenshots of
the pack **worn**, **mid-swing**, and **open with items seated in pockets**.

---

## 8b. Corrections found during implementation

Six things in the sections above turned out to be wrong. They are left in place so the
reasoning is still readable, and corrected here.

1. **The player prefab is `PlayerCharacter.prefab`, not `Player.prefab`.** Section 7 named
   the wrong one. `Player.prefab` is a legacy stub with no Animator, no Rigidbody and no
   PlayerController; the live player is `PlayerCharacter.prefab` (Rigidbody + humanoid
   Animator on `AstronautArmatureAvatar` + PlayerController + PlayerInputManager).
   `BackpackController` is on that one. `PlayerCharacterNetworked.prefab` has NOT been
   given the component — single-player only, as scoped.

2. **The EditMode tests live in `Assets/Game/Tests/Editor/`, not `Tests/EditMode/`.**
   `SpaceGame.Tests.EditMode.asmdef` cannot reference `Assembly-CSharp`, where
   `SpaceGame.Items` lives — Unity forbids asmdefs referencing predefined assemblies, and
   this put 14 compile errors in the console. Moving `Items` into its own asmdef would
   cascade (`InventoryItem` implements `SpaceGame.Core.IRegistryEntry`, also in
   Assembly-CSharp). A folder named `Editor` outside any asmdef compiles into
   `Assembly-CSharp-Editor`, which already references `Assembly-CSharp`,
   `nunit.framework`, `UnityEngine.TestRunner` and `UnityEditor.TestRunner` — verified in
   its Bee `.rsp`. Zero production files moved.

3. **The FBX imports with `lossyScale = 100` on every transform.** Mesh data is 100x small
   under transforms 100x large (the FBX centimetre convention); it cancels for the pack but
   multiplies anything parented into a socket. `BackpackItemVisual.Build` therefore divides
   the fit by `socket.lossyScale`. Measured through the real code path: `fitSize 0.13` ->
   world size 0.130, `fitSize 0.22` -> 0.220. Without it they were 13 m and 22 m.
   Do NOT "fix" this by setting `useFileScale = false` on the importer — that was tried and
   inflates the pack to 34 x 53 x 23 m.

4. **`PIVOT_Clamshell` does not come in at identity.** Its authored rest local rotation is
   `(270.02, 0, 0)`. `SetOpen` must rotate RELATIVE to that rest pose, and `Awake` must not
   overwrite it. The first implementation did both wrong, which put the open pack 0.425 m
   underground. Measured after the fix: lid-open bounds `(0.345, 0.237, 0.938)` — the lid
   lying open flat and forward, as intended — against `(0.345, 0.532, 0.581)` before.

5. **`openAngle` is 170 degrees, not 100.** The lid pockets face straight down when shut, so
   the lid has to come most of the way over before their contents point at the sky. At 100
   they sit sideways and read as about to fall out.

6. **The grounded pose needs `groundLift`.** The pack's origin is at its mid-depth, not on
   its back face, so placing the origin at the raycast hit point rests it half-buried.
   `groundLift = 0.121` (measured origin-to-back-face on this mesh) lifts it along the
   surface normal. A different pack mesh needs a different value.

Also worth recording: `BackpackObject` disables its own collider while worn. Colliders
under one Rigidbody form a single compound collider, so a worn pack would otherwise bolt a
0.345 x 0.528 m box onto the player's capsule and wedge them in doorways they used to fit
through.

## 8c. Revision 2 — the stand-up cabinet pack (2026-08-13)

After seeing it in the editor the user asked for five changes. They supersede sections 3,
4.1, 4.4 and 8b.5 above.

1. **Much bigger.** 0.345 x 0.228 x 0.528 m becomes **0.728 x 0.509 x 1.119 m** measured
   (0.70 x 0.45 x 1.15 nominal; the exoframe stands proud of the canvas body). On a
   1.4 m astronaut the worn pack now tops out above the head — chosen deliberately over
   two smaller options.

2. **It stands up and opens like a cabinet.** The clamshell is gone. Two doors hinge on
   the REAR vertical edges of the frame — "the back supported part" — and swing outward
   135 degrees, so the gear on both inner faces turns to face the player at once. Deployed,
   the pack stands upright on its base rather than lying flat.
   Objects: `Mesh_Backpack_Frame`, `Mesh_Backpack_DoorL/R`, `Mesh_Backpack_Latch_L/R`,
   `PIVOT_Door_L`, `PIVOT_Door_R`.

3. **No interior grid.** The 9 discrete pockets become **one interior compartment** of 10
   anchors, 5 per door, deliberately staggered rather than gridded. Exterior points go
   from 3 to **6**, so a loaded pack visibly carries gear on the astronaut's back.
   `StrapSlots = 6`, `MainSlots = 10`. Sockets renamed `SOCK_Ext_0..5` / `SOCK_Int_0..9`.

4. **The deploy aims off the camera, not the body.** `BackpackController.AimForward()`
   uses the player camera's flattened forward. The reported "it landed behind me" could
   NOT be reproduced — root forward, camera forward and head forward all measured
   identical, `dot = 1.000` — so the most likely cause was the deploy being refused (no
   ground found) leaving the pack worn on the back. The ground probe now starts 2 m up and
   casts 6 m instead of 1 m and 4 m, which is the case that silently found nothing on a
   rise. Aiming off the camera is belt-and-braces.

5. **The arc reads better.** `deploySeconds` 0.6 -> 0.9, `arcHeight` 0.45 -> 0.6,
   `arcOutward` 0.35 -> 0.45, `deployDistance` 0.9 -> 1.15 (a 0.73 m wide pack needs the
   room). The doors opening remain a separate beat after the landing.

### Two axis facts this revision turns over

`groundLift` is now **0.01, not 0.121**: the pack's origin is at the bottom centre of its
footprint and it stands upright, so it only needs to clear z-fighting.

Blender `+Y` (frame, worn against the back) maps to Unity **`-Z`**, so the **doors are on
Unity `+Z`**. Two things follow, and both were wrong on the first attempt at this revision:
the deployed rotation is `LookRotation(toPlayer, normal)` — pointing local `+Z` AT the
player, or they get shown the back of a cabinet — and the body collider must sit on
`z -0.265..-0.075`, the frame side. A collider on the `+Z` side covers the open interior
and `ResolveAlongRay` then blocks every item pick.

### Verified by measurement

- Builder assertions: closed, every interior anchor points `+Y` into the shut cavity; open
  at 135 degrees, every one has swung to face the player. `dump_sockets()` fails the build
  otherwise.
- In Unity, deployed and open: `dot(anchor.up, toPlayer) = +0.71` on both doors; lowest
  geometry `y = 0.007` against a ground plane at 0; open footprint 1.805 m wide.
- Container and arc logic exercised in the live editor: **35 checks passed, 0 failed.**

Cost: the model is now **46,460 tris** (was ~17,000). That is hero-asset territory for a
prop; if it ever needs to be cheap, the exoframe alone is 24,212 of them and its bevel and
tube segment counts are where the fat is.

## 8d. Revision 3 — shelves, nets, and the floating-item bug (2026-08-13)

The user reported items "coming out of the backpack" in an unnatural placement, and asked
for a larger pack with a lot more gear carried outside. Both trace to one design error.

**The bug.** Interior anchors were on the DOOR INNER FACES, and `BackpackItemVisual` seats
an item by pushing it along the socket's normal. Closed, that pointed into the cavity and
looked fine. Open, the anchors rotated with the doors, so every item stuck 0.2 m straight
out of a swinging panel into mid-air. An anchor on a moving face can never look settled,
because nothing is under the item.

**The fix.** Interior stow moved INTO THE CARCASS, standing on a floor plus three shelves —
12 anchors, 3 across x 4 tiers, unparented from the doors. `dump_sockets()` now asserts
that every interior anchor's `+Y` is world up *and unchanged between closed and open*, which
fails the build if one ever gets parented to a door again.

**Exterior.** Three courses of cargo net per door, with 8 anchors under them plus 2 on the
outrigger loops — **10 exterior**, now the larger half of the pack. `BackpackItemVisual`
gained `BackpackSeat`: `StandOn` (shelf; item keeps its up and rests on its base) and
`LieFlat` (net; the item's THINNEST axis turns into the surface and its longest lies along
it, so it hugs the panel). Counts: `StrapSlots = 10`, `MainSlots = 12`.

**Size.** 0.728 x 0.509 x 1.119 -> **0.888 x 0.627 x 1.364 m**.

### A second, worse bug this exposed

Testing seating against REAL item prefabs rather than primitives showed `GrapplingHook` and
`Lasso` arriving **1 x 1 x 2 m and ignoring the socket entirely**. Both are rope items
carrying `LineRenderer`s, which usually run in **world space** — they ignore their own
transform, so the copy gets scaled and seated and the rope stays where the source prefab
drew it, dragging the measured bounds with it. `Strip` now removes `LineRenderer` and
`TrailRenderer`, and `LocalBounds` only measures `MeshRenderer`/`SkinnedMeshRenderer`.

This is why seating must be verified against real prefabs: a primitive cube passes every
version of this code, including the broken ones.

### Verified by measurement

- Builder assertions pass, including the new one that interior anchors do not move with the
  doors.
- All **6** real gadget prefabs seat correctly: flat items 0.03-0.08 m proud of the panel
  (was a 1 m artifact), shelf items land with `baseErr = 0.0000`.
- **14 checks passed, 0 failed** on counts, prefab wiring, and anchor parentage.

## 9. Deliberately out of scope

- Network replication of pack position and contents.
- Moving items from the hotbar back into the pack (one-way, pack → hotbar, for now).
- Dragging items between pack slots.
- Backpack variants / loot tiers — one pack, one collection.
- A "remove backpack" rig animation. The arc is procedural.
