---
system: ShipSchematic
layer: items
summary: "The terminal's SHIP page: a live 3D lander whose missing modules glow and can be pointed at"
paths:
  - Assets/Game/Scripts/Gameplay/Terminal/ShipPartInfo.cs
  - Assets/Game/Scripts/Gameplay/Terminal/ShipSchematicOrbit.cs
  - Assets/Game/Scripts/Gameplay/Terminal/ShipSchematicPick.cs
  - Assets/Game/Scripts/Gameplay/Terminal/DragGesture.cs
  - Assets/Game/Scripts/Presentation/UI/World/Terminal/ShipSchematicStage.cs
  - Assets/Game/Scripts/Presentation/UI/World/Terminal/ShipSchematicView.cs
  - Assets/Game/Scripts/Presentation/UI/World/Terminal/ShipSchematicModel.cs
  - Assets/Game/Editor/Environment/ShipSchematicBuilder.cs
  - Assets/Game/Editor/Environment/FeatureEdges.cs
  - Assets/Game/Art/Shaders/UI/Terminal/SchematicHull.shader
  - Assets/Game/Art/Shaders/UI/Terminal/SchematicWire.shader
  - Assets/Game/Scripts/Vehicles/Parts/ShipPartNaming.cs
symptoms:
  - "a ghostly second lander floats inside the cockpit where the terminal stands"
  - "the terminal's SHIP page is a flat white rectangle"
  - "the schematic draws the cockpit around it instead of the little ship"
  - "the schematic turns but hovering a module never names it"
  - "the little ship swings round every time the lander changes heading"
  - "every module on the schematic reads MISSING on a ship that is clearly whole"
  - "the schematic keeps drawing the old hull after a ship re-export"
  - "Esc will not leave the terminal while a schematic module is selected"
  - "picking one module on the schematic makes the others impossible to click"
  - "the schematic is flat and unreadable with no near and no far"
  - "the schematic is a hairball of triangles with a ship somewhere inside it"
  - "the modules on the schematic are almost impossible to click"
  - "clicking a module on the schematic selects nothing and nudges the hull round instead"
  - "the schematic picks a small module when the cursor is just off a big one"
  - "the module the schematic had lit goes dark the moment the mouse button goes down"
  - "the wireframe draws every edge of every triangle"
reads_with: [Terminal, PlayerShip, Multiplayer]
updated: 2026-09-05
---

# ShipSchematic

The first page of the lander's cockpit terminal ([Terminal](Terminal.md)): the ship itself, drawn small
and green behind the glass as a hidden-line wireframe. The eleven salvage modules it can be missing
glow red and pulse; the crew turn the hull with a drag, zoom with the wheel, and point at a module to
read what it is and what the ship cannot do without it. Clicking one selects it, clicking it again
clears it, a different one moves the selection across.

**Scope:** the schematic only. The console, the zoom-in camera, the other pages and the telemetry
feed are [Terminal](Terminal.md); the modules, their sockets and the mask are [PlayerShip](PlayerShip.md).

## Model

- **The drawing is the real ship.** [`ShipSchematicBuilder`](Assets/Game/Editor/Environment/ShipSchematicBuilder.cs)
  cuts a miniature out of the SAME `player_ship.fbx` the hull is built from (renderers only, flat, no
  colliders, normalised to about a unit long), so the picture cannot describe a hull that no longer
  exists — which the side elevation of rectangles it replaced could, while also unable to say WHICH
  of two nuclear motors was gone.
- **Two halves and the arithmetic between them.** [`ShipSchematicStage`](Assets/Game/Scripts/Presentation/UI/World/Terminal/ShipSchematicStage.cs)
  is the 3D (miniature, lens, texture, painting, hit test);
  [`ShipSchematicView`](Assets/Game/Scripts/Presentation/UI/World/Terminal/ShipSchematicView.cs) is the
  UI (cursor, selection, words). Everything that can be wrong by a factor of two is pure and tested:
  `ShipSchematicOrbit` frames, `ShipSchematicPick` picks, `DragGesture` splits click from drag.
- **Orthographic on purpose.** A schematic is a drawing; perspective would make the near motor
  bigger than the far one and invite the reader to read something into it.
- **A wireframe of FEATURE edges over dark faces.** [`FeatureEdges`](Assets/Game/Editor/Environment/FeatureEdges.cs)
  keeps each shell's boundary and every fold sharper than `CreaseDegrees` (28°), discarding the
  triangulation — the lander is 46,000 triangles and inking all of them at this size is a hairball with
  a ship inside it. The faces are still drawn, nearly black, purely to fill depth: that is what makes
  it hidden-line rather than an x-ray where near and far motors look alike.
- **State is borrowed, never owned.** Which modules are fitted is `ShipPartRack`'s replicated mask,
  arriving in the terminal's `TelemetrySnapshot`; framing, hover and selection are local to the reader.
- **Pointing is forgiving and selecting never moves the lens.** The lens flew onto the picked module once,
  pushing the other ten out of frame; a pick is a plain toggle, a near miss resolves to the nearest module.
- **The words are pure.** [`ShipPartInfo`](Assets/Game/Scripts/Gameplay/Terminal/ShipPartInfo.cs) holds
  a name and a sentence per `ShipPartKind` plus the mask arithmetic, so a kind added with no words
  fails a test rather than drawing a blank panel.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `ShipSchematicStage` | [ShipSchematicStage.cs](Assets/Game/Scripts/Presentation/UI/World/Terminal/ShipSchematicStage.cs) | Builds the miniature, lens and render texture once; paints every renderer through a `MaterialPropertyBlock`; hides the miniature from every other camera; `Raycast(uv)` → socket index; renders only within `readableDistance` (6 m). |
| `ShipSchematicView` | [ShipSchematicView.cs](Assets/Game/Scripts/Presentation/UI/World/Terminal/ShipSchematicView.cs) | Raw cursor, hover, drag, wheel, click-to-select, `TryStepBack()` for Esc, and the panel's three labels. |
| `ShipSchematicOrbit` | [ShipSchematicOrbit.cs](Assets/Game/Scripts/Gameplay/Terminal/ShipSchematicOrbit.cs) | Pure. Yaw, pitch and orthographic size as target + current, `Step` easing between; the pivot is fixed on the hull's centre. `Adopt` / `Home` / `Drag` / `Zoom` / `Idle`. Tested. |
| `ShipSchematicPick` | [ShipSchematicPick.cs](Assets/Game/Scripts/Gameplay/Terminal/ShipSchematicPick.cs) | Pure. `At(orbit, uv, aspect, boxes, margin)` → which module a point on the glass meant: the nearest box the ray crosses, else the nearest **outline** within the margin. Tested. |
| `DragGesture` | [DragGesture.cs](Assets/Game/Scripts/Gameplay/Terminal/DragGesture.cs) | Pure. One press: a click until it leaves a dead zone measured **from the press point**, a turn after. Nothing turns inside the zone. Tested. |
| `ShipPartInfo` | [ShipPartInfo.cs](Assets/Game/Scripts/Gameplay/Terminal/ShipPartInfo.cs) | Pure. Name and function per kind, the detail block, the overview, `CountInstalled` / `FittedOfKind` / `MissingKinds`. Tested, including completeness over the enum. |
| `ShipSchematicModel` | [ShipSchematicModel.cs](Assets/Game/Scripts/Presentation/UI/World/Terminal/ShipSchematicModel.cs) | On the baked prefab: which renderers are modules (socket NAME + kind), which are hull, and their boxes in **model space**, measured from the meshes. |
| `ShipSchematicBuilder` | [ShipSchematicBuilder.cs](Assets/Game/Editor/Environment/ShipSchematicBuilder.cs) | **Tools ▸ SpaceGame ▸ Build Ship Schematic Prefab**. Fails loudly if any `ShipPartKind` has no mesh. Writes the line meshes to `Assets/Game/Art/Models/Generated/ShipSchematicWire.asset`. |
| `FeatureEdges` | [FeatureEdges.cs](Assets/Game/Editor/Environment/FeatureEdges.cs) | Welds by position, then keeps boundary and crease edges as a `MeshTopology.Lines` mesh. |
| `SchematicHull` / `SchematicWire` | [Shaders/UI/Terminal/](Assets/Game/Art/Shaders/UI/Terminal) | Faces: unlit, near-black, **depth-written**, scanline. Lines: unlit, flat, `ZWrite Off` + `Offset -1,-1`. Both take their colour per renderer. |
| `ShipPartNaming` | [ShipPartNaming.cs](Assets/Game/Scripts/Vehicles/Parts/ShipPartNaming.cs) | The `Part_<Kind>_<Side>` convention, shared with `PlayerShipBuilder`. |

## Flows

**Open.** The SHIP page being shown enables the view, which sizes the render texture to the viewport's
aspect and asks the stage to build: the miniature is instantiated at the stage's origin at unit scale,
faces and lines get their materials and are switched OFF, each module resolves to a bit of the ship's
mask **by mesh name**, and `Adopt` frames the whole hull three-quarters on.

**Point and press.** Each frame the view reads the mouse raw, converts it through the canvas's event
camera, and calls `Raycast(uv)` → `ShipSchematicPick.At`: a ray from the orbit against the modules' boxes
in model space, falling back to the module whose **outline** passes nearest, within `pickMargin`. A press
is a click until it leaves `dragDeadZone` (canvas units, from the press point): inside it nothing turns,
the module stays lit, and the pick is read from where the press went down, so highlight and click can
never disagree; leaving it turns the hull and spends the click. A click **selects** — a different module
replaces the current one, the same one again clears it, and so do empty glass and Esc — and the lens
moves for none of it. With no event camera (anybody who is not the operator) the hull just turns.

**Draw.** `LateUpdate` steps the orbit, writes the lens's pose and size, and repaints every renderer
— green fitted, red pulsing missing, brighter under the cursor, pale and brightest when selected; a
module's lines and faces always agree. Unselected modules stay at full strength (each is one the
reader may want next); only the hull steps back while one is selected.

**Build.** **Build Standing Terminal Prefab** rebuilds the miniature and wires both shaders, then **Build PlayerShip Prefab** re-nests it; **Build Ship Schematic Prefab** rebuilds the miniature alone.

## Multiplayer

| Path | Carrier | Authority |
| --- | --- | --- |
| Which modules are fitted | `ShipPartRack`'s `NetworkVariable<int>` mask, read through the snapshot | Server. This system adds nothing to the wire |
| Framing, hover, selected module | none | Local to each reader, by design |

A late joiner gets the fitted set with the ship; fitting one turns it green on the other machine by itself.

## Persistence

Nothing. Framing and selection are view state, cleared in `OnEnable` (with `Orbit.Home()`) so a terminal
left turned over comes back whole. What the page *shows* is `ShipPartsSaveable`'s; see [PlayerShip](PlayerShip.md).

## Gotchas

- **Hidden per CAMERA and isolated by LAYER — two mechanisms, two jobs.** Renderers rest `enabled = false`
  and switch on in `beginCameraRendering`, off in `endCameraRendering`, for this lens only (`PlayerLook`'s
  trick). A layer cannot do that half: the player camera's mask is authored `Everything`, so a `Schematic`
  layer visible to it puts **a ghostly second lander in the cockpit**. The layer (11 in
  [TagManager.asset](ProjectSettings/TagManager.asset)) does the other half, the lens's `cullingMask`.
- **Everything is measured in the miniature's OWN space.** `Renderer.bounds` is a world-axis AABB that
  reshapes as the ship it hangs under turns, so framing off it makes **the little ship swing round every
  time the lander changes heading**. `ShipSchematicModel` carries each mesh's own box into model space
  once, and the stage forces the miniature to local zero/identity/unit scale so model and stage space
  are one — which is what lets the orbit hand the lens a *local* pose.
- **Modules are tied to sockets by NAME, never by index.** This builder and `PlayerShipBuilder` walk the
  same model in separate passes; indices that line up today come apart on the first mesh reorder, and the
  symptom is **every module MISSING on a ship that is clearly whole**. The stage errors, naming each.
- **The terminal's builder rebuilds the miniature every time** (`Rebuild`, not "build it if missing"):
  reusing one wired the terminal to last week's prefab whenever what a miniature CONTAINS changed, and
  the only symptom was a runtime error about a prefab that looked fine.
- **Builders load shaders by PATH, never `Shader.Find`:** the name registry is empty for a shader Unity
  has not imported yet, so a build run seconds after a new .shader appears fails with "no shader named
  X" about a file plainly on disk — and leaves the old prefab in place.
- **`Hologram/Solid` is the wrong shader** and was tried: additive with `ZTest Always`, it collapses a
  hull into **one flat glowing blob with no near and no far**.
- **Pointing at eleven small things on a big glass, twice got wrong.** *Forgive to a module's OUTLINE,
  never its middle*: the boxes a ray can cross cover 5.9 % of the viewport, but modules differ threefold
  on the glass (a nuclear motor draws ~70 px across at 1080p, a belly turbine ~23), so a radius round the
  middle put the cursor just off the motor's flank outside the motor and inside the little turbine — it
  answered 13 % of the viewport, **9 % of those answers naming a module that was not the nearest thing on
  it**. The rectangle round the eight projected corners answers 25 %, takes back none of them, and an
  exact ray hit still wins. *And a click is how far the cursor got from the PRESS POINT, never how far it
  travelled*: a sum counts a resting hand's wander and never gives it back, four canvas units is **eight
  screen pixels** at 1080p, and the orbit turned from the frame the button went down — so most clicks
  became drags that nudged the hull, which reads as being ignored. **Measure before tuning `pickMargin`
  or `dragDeadZone`**: project the part boxes of `ship_lander_blockout.blend` through the orbit and count.
- **Feature edges need vertices WELDED BY POSITION first.** An FBX splits vertices at every normal
  and UV seam, so unwelded, every seam reads as a boundary and the "wireframe" is every triangle.
- **The line meshes are a generated ASSET**: a mesh built in memory and referenced by a saved prefab
  comes back null next launch. `ShipSchematicWire.asset` is remade every build, orphans and all.
- **A `RawImage` with no texture draws solid white**, a flashbulb on a phosphor tube — so the builder ships the viewport `enabled = false` and `ShowTexture` switches it on with the lens's texture.
- **The cursor here is read RAW**, like the terminal's exits, so the viewport is deliberately **not** a
  raycast target. With no focus session the canvas has no `worldCamera` and nothing converts.
- **The lens is an ordinary Game camera, so the world's fullscreen render features run on it too**: fog
  (nil at a metre), the pastel grade, sandstorm (a heavy storm hazes the schematic, which reads as
  interference and is left in). Shadows and post are off; more needs its own URP renderer.
- **Esc is layered:** `TerminalFocusSession` asks `TerminalScreen.TryStepBack()` first, so the first
  Esc on a selected module only clears the selection. Guarded on the SHIP page being shown.

## Extending

1. **A new module**: append it to [`ShipPartKind`](Assets/Game/Scripts/Vehicles/Parts/ShipPartKind.cs)
   (append — the values are bit positions in a saved, replicated mask) and to `PART_KINDS` in
   `ship_parts.py`, name it in `ShipPartInfo`, re-export, then run **Build Ship Schematic** and **Build
   PlayerShip Prefab**. The builder finds the mesh by prefix.
2. **A different readout beside the hull**: the panel is three labels wired by
   `StandingTerminalBuilder.BuildSchematic`; compose their words in `ShipPartInfo`.
3. **Verify on a client and after a reload**: fit a module on one machine, watch it turn green on the
   other, then reload and confirm it is still fitted.
