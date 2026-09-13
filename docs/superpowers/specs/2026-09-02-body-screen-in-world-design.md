# Body Screen in the World — design

**Date:** 2026-09-02 · **Status:** approved in brainstorm, spec for review · **Branch:** fix-ship-intro-scene
**Supersedes:** the F-screen half of [2026-09-02-body-equipment-design.md](2026-09-02-body-equipment-design.md) §7 (the six-tile panel).

The F screen stops being a flat panel of six abstract tiles and becomes **a camera step-out in the
live world**: your real character, seen from the front, thighs up, with three **sites** on the body
where worn gear lives — a ghost silhouette on each empty site saying what goes there — and the three
hand-hotbar tiles along the bottom. Click-to-carry is unchanged. Nothing pauses. The slots, the
rules, the wire and the save are untouched; this is presentation and interaction only.

## 1. Why

The six-tile panel told you *that* you have a back slot and two gauntlet slots; it could not show
you *where on your body* they are, what is on them, or how big it is. Showing the actual character
turns the body into its own diagram (GDC-L1-UX-0004: the body is the signifier — a translucent cuff
on an empty forearm says "a gauntlet goes here" with no label), and the ghost preview of the carried
item, seated exactly where it will be worn, answers "what will happen if I click?" before the click
(GDC-L1-UX-0003: what can I do, what will happen, at a glance). Placement gets layered feedback —
the ghost snaps solid, a sound, the arm flexes (GDC-L1-FEEL-0004).

**Constitution notes.** The world stays visible and running behind the body — the pack focus made
the same call and for the same reason: nothing pauses, so the player keeps their situational
awareness while their head is in their kit (GDC-L1-UX-0007, *incidental* friction cut; a blurred
world would have been friction with no payoff). Camera in and out are fast (0.4 s / 0.25 s) because
F is pressed hundreds of times a session — a moment, not a cutscene. All three principles are
`contextual`/`objective` guidance and were applied within their stated scope; no recorded
disagreement bears on this.

## 2. Decisions from the brainstorm

| Question | Answer |
| --- | --- |
| Where the body is shown | **A — in the live world.** A spawned focus camera in front of your real character. Not a dimmed world (B), not a render-texture paper doll (C). |
| The back slot from the front | **Over the shoulders.** Front view only; the back site is whatever shows past the shoulders. No body turn, no three-quarter angle. |
| What marks an empty slot | **Ghost silhouettes.** A faint translucent generic shape seated where the item would sit. No rings, no reticle brackets. |
| While carrying | The **carried item's own mesh**, translucent amber, seated at every legal site at once. Kept regardless of marker style. |
| Stance | **Idle.** The character stands as they are. An "inspect" stance (arms out) is a later polish pass, see §11. |
| Hand hotbar | **The three HUD-style tiles along the bottom**, as today, with the selected item visibly in the real hand. |
| Back placeholder | **A mount frame** — two uprights and a crossbar rising just above the shoulder line — *assumed* (the user approved §1–3 without choosing; the alternative was a faint wing-pack silhouette). Reason: every player already wears the expedition rig, so a "pack top" ghost reads as a second backpack. |
| Depth of field | Same treatment as the pack camera (aperture 2.2, 65 mm), focused on the chest. Mild; the world stays legible. A knob, not a rebuild. |
| Exit | Camera flies back to the eye over 0.25 s; controls return immediately on close. |
| Wear feedback | Newly worn gauntlets flex the arm (the existing arm-raise latch) and play `SfxId.WeaponEquip`, on every machine, derived from the replicated slot change. |

## 3. Scope — six parts, one spec

1. **Focus camera extraction** — pull the camera-handover mechanics out of `PackFocusCamera` into a shared base. Pure refactor; the pack must behave identically.
2. **Body focus** — `BodyFocusSession`, `BodyFocusCamera`, `BodySite`, the ghost/preview plumbing.
3. **F screen rework** — `BodyInventoryUI` over sites plus three tiles; chips and captions on `WorldOverlay`.
4. **Placeholders** — the existing `Coll_GauntletBase_Plain` shipped as `ghost_gauntlet`, plus a new `ghost_mount_frame` in the Blender library → two prefabs → wired onto the player prefab.
5. **Feedback** — commit/snap, refusal, wear-flex, sounds.
6. **Docs** — BodyEquipment.md, Backpack.md, Inventory.md rows; the Human systems entry.

"Done" is all six, verified on a real client, with the pack focus regression-checked and the ghosts
*seen rendering* in a screenshot.

## 4. The view

### Entering and leaving

- Trigger and preconditions are unchanged: **F** (`BodyInventoryUI.Toggle`), a local player, not
  mounted, not typing in a field, scope free. `GameplayMenuScope.Enter(this, freezeTime: false,
  hideHud: true)` — cursor free, input/look/movement off, HUD and crosshair hidden. The screen draws
  its own three hotbar tiles.
- On open, `BodyInventoryUI` finds the local player's `BodyFocusSession` and calls `Enter()`; on
  close, `Exit()`. The UI stays the conductor of the carry; the session owns everything in the world.
- **Every exit** — F, Esc, death, teardown, the session's component being disabled — goes through
  `Exit()`, which is idempotent. The carry is dropped (nothing was sent), hidden renderers are
  restored, ghosts destroyed, the camera dismissed.

### The camera

`BodyFocusCamera`, a spawned camera in the mould of `PackFocusCamera` (own GameObject, the player's
camera **and its AudioListener** switched off for the duration, put back on dismiss and on
`OnDestroy`). The pose is authored, not orbited:

| Tunable (serialized on `BodyFocusSession`) | Default | Meaning |
| --- | --- | --- |
| `lensDistance` | 2.3 m | Along the body's flattened forward, from the look target |
| `lensRise` | 0.10 m | Lens height above the look target |
| `pitchDown` | 4° | Slight downward pitch; level enough that the horizon stays honest |
| `fieldOfView` | 40 | The pack's narrow lens — flat perspective, honest sizes |
| `flyInSeconds` / `flyOutSeconds` | 0.40 / 0.25 | From the eye to the pose, and back |
| `minLensDistance` | 0.9 m | Floor when a wall pulls the lens in |
| `pullInRadius` | 0.25 m | Spherecast radius for the wall test |

- **Look target**: the chest bone (`BoneResolver`, `HumanBodyBones.Chest`, hints "Chest",
  "Spine"), sampled at spawn. That frames from the thighs up — the arms and chest are the targets and
  should each be a large fraction of the frame.
- **Direction**: the body's flattened forward (`transform.forward` with y = 0), as
  `BackpackController.DeployForward` does. The camera goes to where you face; the body does not turn.
- **Flight**: position + yaw + pitch lerped separately with `SmoothStep`, FOV lerped from the
  player's — the pack's roll-free flight, now shared. In: eye → pose. Out: pose → the eye's **live**
  pose (controls are already back, so a player who walks off as they close sees the camera catch up
  to them), then dismiss.
- **Walls**: a `SphereCast` from the look target toward the lens position, ignoring the player's own
  colliders and triggers; the lens stops at the hit minus the radius, never nearer than
  `minLensDistance`. At the floor the crop is tighter and that is accepted.
- **Parallax and depth of field**: the pack's cursor parallax (±6° yaw, ±4° pitch, 0.25 s damp) and
  DOF volume builder move into the shared base and are used unchanged, DOF focused on the chest.
- `WorldOverlay.EyeOverride` is set to the focus camera while the session is up and cleared on exit,
  so nameplates, damage numbers and this screen's own chips all project through the lens that is
  actually rendering.
- Head and helmet render without any special handling: `PlayerLook` hides them **per camera**, only
  for the player's own eye. Same for the worn pack.

### Extraction (part 1)

`FocusCamera` (abstract, `Assets/Game/Scripts/Presentation/Cameras/FocusCamera.cs`) takes from
`PackFocusCamera`: spawn/handover/dismiss, the roll-free flight (now with a fly-out), parallax, the
DOF volume, culling/clip copying. Subclasses provide `LensPosition()`, `LensYaw()`, `Pitch`, `Fov`,
`FocusDistance`. `PackFocusCamera : FocusCamera` keeps every number and comment it has and loses
only the mechanics. Verify the pack in play before building on it.

## 5. The sites

One `BodySite` per `BodySlot`. A site is anchored to **the same transform the worn item uses**, so a
ghost sits exactly where the real thing will:

- **Gauntlets**: the **LowerArm bone** the controller resolved, seated through
  `ForearmSeat.Apply` — the `GauntletFit` math extracted from
  `BodyEquipmentController.WearOnForearm` so the ghost and the real device use one copy. *(Revised
  2026-09-03: gauntlets were seated in the hand's grip frame when this spec was written; a
  concurrent session rebuilt all six on `components/props/gauntlet_base.blend` and moved the
  seating to the forearm.)*
- **Back**: the bone `BodyEquipmentController` resolved (exposed read-only as `BackBone`), seated
  through `BackSeat.Apply` — the `WornFit` seating math extracted from
  `BodyEquipmentController.WearOnBack` so both use one copy.
- Both anchors reach the body screen through read-only seams on the controller (`BackBone`,
  `ForearmBone(slot)`, `HandSocket(slot)`, `WornInstance(slot)`): the controller decides where worn
  gear sits, and nothing re-derives it.

### What a site shows

`BodySiteState` is a **pure resolver**: `(slot, slot contents kind, carried ref, carried kind,
hovered) → state`, with `GearMoves.Resolve` as its only source of legality, so a site can never
disagree with a tile about what the server will say.

| State | When | Visual |
| --- | --- | --- |
| `Empty` | Slot empty, nothing carried (or carried item cannot go here) | **Placeholder**: the generic ghost prefab, seated, body `UITheme.Accent` at 22% with a thin outline |
| `Worn` | Slot filled, nothing carried (or carried cannot go here) | The real worn instance. Hover: soft rim (`PackHandVisuals`' hover rim colour) + caption "Item Scanner · Q" |
| `Preview` | Carrying, legal, slot empty | The carried item's own mesh, seated as it would be worn, `HotbarStyle.Amber` at 55%; hovered 80% + caption |
| `SwapOutline` | Carrying, legal, slot filled | Amber outline shell on the worn instance. Hover caption "Swap · Item Scanner ↔ Repulsor Gauntlet" |
| `Refused` | Hovering a site the carried item cannot go to | `UITheme.Danger` tint at 45% on whatever is there (placeholder or worn) |
| `Reserved` | This site is the carry's origin | Worn instance's renderers disabled (recorded, restored on every exit path); placeholder shown grey at 30% |
| `Committing` | A legal click was sent, answer pending | Preview at 90%, rim white-amber, scale pop 1.06 → 1.0 over 0.15 s; times out after 1.0 s back to the resolved state with a shake |

**All legal sites light at once** while carrying — pick up the repulsor and both forearms show it,
one as a preview, one as a swap outline — so the player sees every option without hunting.

### Ghost copies

- `DisplayCopy.Make(prefab)` — extracted from `BackpackItemVisual.Build`: instantiate under a
  **deactivated stage** so no `Awake` runs, then `Strip` (every MonoBehaviour, NetworkBehaviour,
  particle, line/trail, rigidbody, collider, animator, audio). `BackpackItemVisual.Build` calls it.
- Seating a stripped copy: the fit component is read from the **prefab** (the copy has none —
  `Strip` takes every MonoBehaviour off) and handed to `ForearmSeat.Apply` or `BackSeat.Apply`,
  which are the same calls the controller makes when it wears the real thing.
- Tint materials come from `TintMaterials` (extracted from `PackHandVisuals`: builds
  `SpaceGame/PackDragTint` materials — rim only, or translucent body + rim, alpha-blended, `ZTest
  LEqual`). The outline shell builder moves to `OutlineShell.Build(visual, material, weight)`;
  `PackHandVisuals` uses both. Colours above; nothing new in shaders. The shader's two passes carry
  `LightMode` tags — the untagged-pass silent skip is the reason the pack's overlay never rendered
  for a day; do not add a pass.

### Hit testing — screen space, no physics

Each site's hit area is the **projected AABB of its current visual** (placeholder, preview, or worn
instance), padded 12 canvas px; the cursor is tested against those rects each frame, nearest centre
wins on overlap. Ignored while `EventSystem.current.IsPointerOverGameObject()` (a tile). Chosen over
the pack's collider ray on purpose: three sites do not justify colliders, and a trigger collider
anywhere near the player's hierarchy or on a gameplay layer is a thing the movement probes, the
scanner and other players' rays can hit — a class of bug this screen has no reason to invite.

### Placeholders (part 4)

Two models in the Blender library, one prefab each, referenced by serialized fields on
`BodyFocusSession` (never `Resources`-by-string):

- **`ghost_gauntlet`** → `Assets/Game/Prefabs/Items/Equipment/Ghosts/GhostGauntlet.prefab`. Not a
  new model: it is `Coll_GauntletBase_Plain` from `components/props/gauntlet_base.blend` — the
  bracer every real gauntlet is built on, with no device on top, authored at true suit scale — with
  a `GauntletFit` at the family's defaults, so it seats exactly as the six real gauntlets do.
- **`ghost_mount_frame`** → `GhostBack.prefab`. Two uprights and a crossbar, ~0.9 m wide, rising
  ~0.15 m above the shoulder line when seated. `WornFit` matching the wing pack's pose
  (`(0, 0.05, −0.22)`, size tuned so the crossbar clears the shoulders from the front). Built to be
  seen *past the shoulders*, which is the only part of the back the front view shows.

Both are single-material, low-poly, one variation. Built with the `blender-model` skill; verified in
the front view by screenshot before the prefab is wired.

## 6. Interaction and feedback

Click-to-carry, exactly the tiles' and the pack's gesture:

- **Click a filled site or tile** → carried. The icon follows the cursor (existing `carryRoot`).
  Origin tile hatched (existing `isReserved`); origin site `Reserved`.
- **Legal targets light**: tiles ring orange (existing), sites `Preview` / `SwapOutline`.
- **Click a legal target** → `IBodyEquipment.RequestMove(from, to)` (existing). Carry ends. Target
  site → `Committing`. The slot-change events redraw everything; on the site the real worn instance
  appears and the preview is destroyed.
- **Click an illegal site** → `Refused` flash 0.3 s, a few-millimetre positional shake of the
  ghost, `SfxId.UiError`. Tiles keep their shake.
- **Click empty world while carrying** → the carry returns to its origin. Nothing sent.
- **Esc / F** → close; a live carry is simply dropped (nothing was sent).
- **Any slot change the UI did not make** → drop the carry (existing rule).
- **Hover** anything filled → rim + caption; hover a site while carrying → the state's hover form.

### Labels

Key chips — `Q`, `E`, `SPACE ×2` — and hover captions are `WorldOverlay` labels
(`WorldOverlay.CreateLabel`, outlined text, canvas-scaled) with a small `UITheme.Panel` chip behind
them, anchored to the site's visual bounds: gauntlet chips offset outward (away from the body
centre), the back chip above the crest. Hidden when `Project` says the anchor is behind the lens.
Caption text: the item's `itemName`; for a swap, "Swap · A ↔ B".

### Screen chrome

Title `BODY GEAR` small, top-left; hint `click to pick up · click to place · F closes` top-right,
`UITheme.CaptionSize`, faint. No panel, no backdrop — the world is the backdrop. Three `GearTile`s
centred along the bottom with the caption `Hands · 1–3`; the selected slot lifts and rings amber as
on the HUD. The six-tile silhouette and its header rule go.

### Wear feedback (part 5)

In `BodyEquipmentController.OnSlotChanged`, **after the initial adopt in `Start`** (never on
load or late join): a newly worn gauntlet presses that arm's `ArmRaiseLatch` (the existing 0.6 s
linger — the same raise a Q press does) and a newly worn item plays `SfxId.WeaponEquip` at its bone.
Derived from the replicated slot change on every machine, so peers see the flex too, with no
message of its own — the arm-raise precedent.

## 7. Architecture

New and changed code. Everything under `Items/Body/Focus` mirrors `Items/Backpack/Focus`.

| File | Role |
| --- | --- |
| `Presentation/Cameras/FocusCamera.cs` *(new)* | Abstract spawned focus camera: handover, roll-free flight in/out, parallax, DOF, dismiss |
| `Items/Backpack/Focus/PackFocusCamera.cs` *(changed)* | `: FocusCamera`; keeps its pose numbers and comments, loses the mechanics |
| `Items/Body/Focus/BodyFocusSession.cs` *(new, on `PlayerCharacter.prefab`)* | Tunables + ghost prefab refs; owns the camera and three sites; `Enter/Exit`; per-frame hover; events `HoverChanged`, `SiteClicked`, `NothingClicked` |
| `Items/Body/Focus/BodyFocusCamera.cs` *(new)* | `: FocusCamera`; chest-front pose, wall pull-in via pure `LensStandoff.Resolve` |
| `Items/Body/Focus/BodySite.cs` *(new)* | One site: anchor, placeholder, preview, worn ref, visuals, screen rect, renderer hide/restore |
| `Items/Body/Focus/BodySiteState.cs` *(new)* | The pure state resolver and enum |
| `Items/Equipped/DisplayCopy.cs` *(new, extracted)* | Staged instantiate + `Strip`; `BackpackItemVisual.Build` calls it |
| `Items/Equipped/BackSeat.cs` *(new, extracted)* | `WornFit` seating; `BodyEquipmentController.WearOnBack` calls it |
| `Items/Equipped/ForearmSeat.cs` *(new, extracted)* | `GauntletFit` seating on the forearm bone; `BodyEquipmentController.WearOnForearm` calls it |
| `Items/Equipped/TintMaterials.cs`, `OutlineShell.cs` *(new, extracted)* | `PackDragTint` material builder and shell tracer; `PackHandVisuals` uses them |
| `Items/Body/BodyEquipmentController.cs` *(changed)* | `BackBone`, `WornInstance(slot)` read-only seams; wear-flex + equip sound after adopt |
| `Presentation/UI/Pages/BodyInventoryUI.cs` *(changed)* | Sites replace the three body tiles; chips/captions; carry over sites; new chrome |
| `Prefabs/Items/Equipment/Ghosts/GhostGauntlet.prefab`, `GhostBack.prefab` *(new)* | The placeholders |
| `Prefabs/Characters/Player/PlayerCharacter.prefab` *(changed, by YAML)* | `BodyFocusSession` with defaults and the two prefab GUIDs, on the **base** prefab like `BodyEquipmentController` |

Boundaries: the UI never touches a renderer or a transform in the world; the session never reads a
tile. `GearMoves` remains the only legality table. No new `NetMsg`, no new network prefab, no new
layer, no new shader.

## 8. Multiplayer

Nothing new on the wire. A move is the existing `RequestMove` → `MoveServerRpc` → `GearMoves` on
the server → slot events. Camera, ghosts, previews, chips and hit rects are all local. The only
peer-visible changes are derived from replicated state on every machine: the wear-flex and the equip
sound (§6). A peer sees you stand still facing forward while the screen is open, as today.

**Verify on a real client**, not the host: open F as a client; wear, swap and remove through the
sites; see the host's copy of the client's body update; see the client's copy of the host's flex.

## 9. Persistence

**This feature holds no state worth persisting** — the screen is a view over `BodyEquipmentNetwork`
and the hotbar, both already saved by `BodyEquipmentSaveable` / the inventory saver. Nothing to add;
verify a save → quit → load round trip still restores worn gear and that the sites show it on open.

## 10. Tests and verification

EditMode (pure, `Assets/Game/Editor/Tests`):

- `BodySiteStateTests` — every `(carried kind, slot kind, origin, hovered)` branch; the resolver
  agrees with `GearMoves` on legality by construction, and the table proves the mapping to states.
- `FocusFlightTests` — the shared level lerp: roll is zero at every `t`; `t = 0` is the from-pose,
  `t = 1` the target; angles wrap the short way (`LerpAngle`).
- `LensStandoffTests` — wall pull-in: no hit → full distance; hit → hit minus radius; never below
  the floor.
- `DisplayCopyTests` — a copy of a prefab with a `NetworkObject`, a `Collider` and a `MonoBehaviour`
  comes back with none of them and the same transform hierarchy (grip-point path survives).

In play, over MCP, **read the screenshots** (the pack's invisible overlay passed two static reviews):
the ghost cuffs and mount frame render in the front view; the preview seats where the real item then
appears; the pack focus is unchanged after the extraction; a client works; a save reloads.
`python3 tools/docs_check.py --index` passes.

## 11. Out of scope, deliberately

- **Inspect stance** (arms held out, forearms to the lens) — a later polish pass through the aim
  rig's IK; must coexist with a 1.35 m held staff.
- **Head look-at the lens** — eye contact would be charming; not needed for function.
- **Orbiting the camera**, **hotbar keys while the screen is open**, a dedicated equip `SfxId`
  (WeaponEquip is reused; the catalog's slot sharing is a known constraint), a placeholder per
  *kind* of gauntlet.

## 12. Documentation to update (part 6)

- **BodyEquipment.md**: Model (the F screen is a step-out, not a panel), Key types (new rows for
  the session, camera, site, state), Flows → Move (sites, commit, timeout), Gotchas (screen-space
  hit rects and why; renderer hide must be restored on every exit path; `Committing` timeout;
  wear-flex suppressed during the initial adopt; the placeholder must not look like a pack), and
  `symptoms:` for anything that costs time — e.g. "the ghost cuff never appears on the empty arm"
  (check the tint material's blend/ZTest and that the copy left the deactivated stage). Bump
  `updated:`.
- **Backpack.md**: `PackFocusCamera` row → derives from `FocusCamera`; `DisplayCopy`,
  `TintMaterials`, `OutlineShell` extraction rows. The doc owns `Presentation/Cameras/FocusCamera.cs`
  (it holds the numbers and the history); BodyEquipment.md cross-references it.
- **Inventory.md**: the `BackSeat` / `ForearmSeat` seams — where a worn item sits, now shared by the real thing and its ghost.
- **Human/the-systems.md**: the Body Equipment entry's sentence about the F screen.
- Regenerate: `python3 tools/docs_check.py --index`.
