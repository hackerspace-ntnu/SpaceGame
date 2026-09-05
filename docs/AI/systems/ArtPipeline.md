---
system: ArtPipeline
layer: pipeline
summary: "How a .blend in the Unity-invisible source library becomes an FBX, material, rig and generated prefab"
paths:
  - "Assets/Game/Art/Models/_Source~"
  - Assets/Game/Art/Models
  - Assets/Game/Art/Materials
  - Assets/Game/Art/Animations
  - Assets/Game/Editor
symptoms:
  - "the imported mesh arrives untextured, or a handful of faces wear a neighbouring part's colour"
  - "the model comes out 100x too big when parented to a socket"
  - "a re-exported character stops animating and the console is clean"
  - "the new asset is rotated relative to every existing one"
  - "re-running a generator script destroyed hand edits that existed only in the .blend"
  - "the prefab still renders the old materials after an FBX reimport"
  - "the exported FBX is nowhere in the project and nothing imports it"
  - "a ring, band or collar on a model flickers where two parts meet and the generator's numbers look right"
  - "a small fitting swells and starts clashing with neighbours after the bevel width was raised"
  - "_zverify reports dozens of clashes in a component file that holds several variations"
  - "a mesh renders inside-out in Unity and looks correct in Blender"
  - "_zverify reports a clash in the assembled model that the component file it came from never showed"
  - "a bevelled bezel has a chamfer groove running across its face at every corner"
reads_with: [Vehicles, PlayerShip, AgentSystem, Backpack]
updated: 2026-09-05
---

# Art Pipeline

How a 3D asset gets from a hand-authored/scripted `.blend` in the Unity-invisible source library to an FBX, a material, a rig and a prefab in the game.

**Scope:** [`Assets/Game/Art/`](Assets/Game/Art) — `Models/`, `Models/_Source~/`, `Materials/`, `Animations/`, `Textures/`, `Shaders/`; the model-facing editor tools in [`Assets/Game/Editor/`](Assets/Game/Editor).
**Related:** [blender-model skill](.claude/skills/blender-model/SKILL.md) (authoring rules, palette CLI, conventions — not restated here), [Vehicles.md](Vehicles.md), [PlayerShip.md](PlayerShip.md), [AgentSystem.md](AgentSystem.md), [Backpack.md](Backpack.md).

## Model

- **Author** in [`Assets/Game/Art/Models/_Source~/`](Assets/Game/Art/Models/_Source~): `components/<cat>/x.blend` for reusable parts, `models/<cat>/y.blend` for assembled deliverables. Each `.blend` sits next to its generator `.py` and usually a `<name>_BUILD.md` record.
- **Geometry helpers** come from [`_buildlib.py`](Assets/Game/Art/Models/_Source~/_buildlib.py) (bevelled boxes, tubes, lofts, rivets, palette linking, `append_objects` for pulling component parts into a model). `_buildlib.start()` **refuses to overwrite an existing `.blend`** — the file, not the script, is the source of truth.
- **Family kits sit beside their components.** Where several components share a look and a material table, the shared builders live in an underscore module next to them and are imported, not copied: [`panel_control.py`](Assets/Game/Art/Models/_Source~/components/mechanical/panel_control.py) (rockers, knobs, `tube_path`), [`_console_kit.py`](Assets/Game/Art/Models/_Source~/components/props/_console_kit.py) (the console family's sixteen-slot material table, rounded-rectangle slabs and rings, keycaps, slots, vents), [`models/gear/_gauntlet.py`](Assets/Game/Art/Models/_Source~/models/gear/_gauntlet.py) (the gauntlet seating). The console family — `crt_monitor`, `keyboard_deck`, `console_pedestal` and the assembled `models/props/standing_terminal` — is the worked example: a display plate is always its own object with a 0..1 UV rectangle (`handheld_terminal.screen_plate`), so a render texture or a procedural screen shader maps onto it 1:1.
- **Materials** are *linked* from [`palette.blend`](Assets/Game/Art/Models/_Source~/palette.blend); nothing defines a local material.
- **Export** via `<model>_export.py` → [`_exportlib.export()`](Assets/Game/Art/Models/_Source~/_exportlib.py), which localises linked palette materials, optionally drops armatures, and writes to `unity_path(...)` = `Assets/Game/Art/Models/<Category>/name.fbx`. Exports **are** meant to be re-run; they never write back to the `.blend`.
- **Import** into Unity is automatic; [`MeshReadablePostprocessor`](Assets/Game/Editor/AssetPipeline/MeshReadablePostprocessor.cs) forces Read/Write on every mesh (runtime NavMesh baking) and [`RootMotionCurveStripper`](Assets/Game/Editor/AssetPipeline/RootMotionCurveStripper.cs) deletes root-bound curves from imported clips.
- **Prefab** is generated, not hand-wired, by a `*Builder.cs` under [`Assets/Game/Editor/`](Assets/Game/Editor) (26 of them; menu `Tools > …`). Re-running a builder after a re-export rebuilds import settings, clips, controllers and the prefab in place.
- **A family shares a component, not a convention.** Where several models are the same thing with
  a different device on it, the shared part is one component `.blend` with the mounting surface
  documented in its docstring, and each model is authored against it. The gauntlets are the worked
  example: `components/props/gauntlet_base.blend` is an armoured bracer with a flat dorsal deck at
  a stated plane, and the seven `models/gear/gauntlet_*.blend` bolt their machinery onto that
  plane. `models/gear/_gauntlet.py` holds the frame and the deck's numbers so a device script can
  be read on its own. **Shared does not have to mean COPIED:** until 2026-09-04 every gauntlet
  appended the bracer and shipped its own copy inside its FBX, which meant seven copies of one
  armour and a bare sleeve whenever a gauntlet came off. The bracer is worn permanently now and
  the devices contain none of it — they still assume its deck, which is the part that has to be
  shared, and nothing else.
- **A hand-built variation can ship straight out of its component file.** The standing terminal
  is the user's reworked `Coll_CrtMonitor_Kiosk` inside `components/props/crt_monitor.blend`;
  `standing_terminal_export.py` ships that one collection with
  `_exportlib.export(..., keep_collection=...)`, so whatever the author adds to it ships and no
  object is named in the script. The generated pedestal-and-deck assembly it replaced
  (`models/props/standing_terminal.blend`) was deleted the same day; the FBX kept its name so
  the prefab's GUID survived. When a `.blend` has no markers, the Unity builder measures instead
  — `ScreenPlane` over the plate's triangles for a display, the vertices' lowest point for the
  floor line ([Terminal](Terminal.md)) — and it measures **vertices, never `Renderer.bounds`**:
  for a part that arrives rotated, Unity's world AABB is the box round the rotated LOCAL box, and
  on a cabinet leaning 24° it came out 0.9 m too deep and 0.2 m too tall. A submesh that left
  Blender with no material wears the pipeline's default (`Lit` under URP), which is a material
  not imported with the FBX — test that, not the name.
- **Anything worn is modelled at the wearer's true size**, measured off the skinned character
  rather than guessed — bake the mesh, keep the vertices bound to the bone in question, and bin
  them along and around the limb. The gauntlet base was cut that way; an earlier one built to a
  remembered forearm radius vanished inside the suit sleeve.
- **Walkable interiors** additionally get a baked convex decomposition from [`_collisionlib.py`](Assets/Game/Art/Models/_Source~/_collisionlib.py) — Unity refuses a concave MeshCollider on a Rigidbody.

## Layout

| Directory | Contains | Visible to Unity? |
|---|---|---|
| [`Assets/Game/Art/Models/_Source~/`](Assets/Game/Art/Models/_Source~) | `.blend` masters, generator/export `.py`, `PALETTE.md`, `LIBRARY.md`, `library_index.json`, `palette.blend` | **No** — trailing `~`; no `.meta` files, no Blender install needed to open the project |
| [`_Source~/components/{structural,props,mechanical,organic}/`](Assets/Game/Art/Models/_Source~/components) | 100 reusable component `.blend`s, variations as `Coll_*` collections | No |
| [`_Source~/models/{buildings,characters,creatures,gear,props,vehicles}/`](Assets/Game/Art/Models/_Source~/models) | 44 assembled model `.blend`s | No |
| [`Assets/Game/Art/Models/_backups~/`](Assets/Game/Art/Models/_backups~) | pre-surgery snapshots (`vrescal_before_legs.blend`, …) | No |
| [`Assets/Game/Art/Models/<Category>/`](Assets/Game/Art/Models) | 129 exported/imported `.fbx` + `.meta` | Yes |
| [`Assets/Game/Art/Materials/`](Assets/Game/Art/Materials) | 121 `.mat` in 13 domain folders | Yes |
| [`Assets/Game/Art/Animations/{Player,Creatures,UI}/`](Assets/Game/Art/Animations) | mocap FBX, `.anim`, `.controller`, `UpperBody.mask` | Yes |
| [`Assets/Game/Art/{Shaders,Textures,Sprites,VisualEffects,Brushes}/`](Assets/Game/Art) | shader graphs/HLSL, textures (own `Textures/Items/_Source~`), icons, VFX | Yes |
| [`Assets/ThirdParty/`](Assets/ThirdParty) | bought/free packs (Sci-Fi RTS, Cosmic Retro Blasters, Kevin Iglesias anims, TMP) — outside this pipeline | Yes |

## Model library

130 FBX under `Assets/Game/Art/Models/`. Generated assets are `snake_case`; older/imported ones are `camelCase` or `PascalCase`.

| Category | Example files | Count |
|---|---|---|
| [Environment](Assets/Game/Art/Models/Environment) | `Caves/Stalagmites/*`, `Structures/Outpost/*`, `Blockouts/tower.fbx`, `Ruins/SpaceRuin.fbx` | 35 |
| [Vehicles](Assets/Game/Art/Models/Vehicles) | `Crawler/desert_crawler.fbx`, `Ornithopter/dune_ornithopter.fbx`, `PlayerShip/*`, `RV/*`, `DuneFoil/*` | 34 |
| [Items](Assets/Game/Art/Models/Items) | `portal_gun.fbx`, `net_gun.fbx`, `item_scanner.fbx`, `jumping_rod.fbx`, `ShipParts/*` | 26 |
| [Creatures](Assets/Game/Art/Models/Creatures) | `Organic/DuneRat/dune_rat.fbx`, `Constructs/Golem/golem.fbx`, `Organic/OrkhenRhot/*`, `Robotic/*` | 13 |
| [Weapons](Assets/Game/Art/Models/Weapons) | `GravelBlaster/gravel_blaster.fbx`, `LaserStaff/*`, `CixinGun/*` | 7 |
| [Props](Assets/Game/Art/Models/Props) | `expedition_rig.fbx`, `holo_base_puck.fbx`, `holo_base_table.fbx`, `repair_station.fbx`, `standing_terminal.fbx` | 10 |
| [Characters](Assets/Game/Art/Models/Characters) | `Astronaut/astronaut.fbx`, `Astronaut/AstronautArmature.fbx`, `Nomad/nomad.fbx` | 4 |

## Materials

- **One shared palette**, documented in [`PALETTE.md`](Assets/Game/Art/Models/_Source~/PALETTE.md) and generated from `palette.blend`: **54 materials across 10 categories** (Emissive, Fabric, Foliage, Glass, Hide, Metal, Neutral, Paint, Plastic, Wood). Naming is `Mat_<Category>_<Descriptor>` — `Mat_Metal_Steel_Worn`, `Mat_Emissive_Portal_Blue`.
- The palette **starts empty and grows on demand**; the skill's `palette.py add` refuses a perceptually near-duplicate and names the existing material instead. Every entry's "Intended for" note records why it is not a duplicate — read it before adding a fifty-fifth grey.
- **How a mesh gets its material:** face material indices are stamped in Blender from linked palette slots → `_exportlib` calls `make_local()` (a *linked* material does not survive into the FBX; without this the meshes arrive untextured) → Unity imports them as **sub-assets of the FBX** (`materialLocation: 1`, `materialName: 0`, `materialSearch: 1` on every model FBX meta), regenerated on every reimport.
- Because they are sub-assets, per-material flags cannot be edited in place. [`DoubleSidedMaterials.Apply()`](Assets/Game/Editor/Support/DoubleSidedMaterials.cs) copies each to `Assets/Game/Art/Materials/Vehicles/<name> (DoubleSided).mat` and rewires the renderers — 74 of the 82 files there are these generated copies. Vehicle hulls are modelled as surfaces, so back-face culling makes cabins see-through.
- Hand-authored `.mat` assets (terrain, portal, surfaces, VFX) live in the domain folders under [`Materials/`](Assets/Game/Art/Materials) and are unrelated to the palette. `Materials/Untitled/New Material 1-3.mat` are junk.

## Rigs & animation

| | Setting |
|---|---|
| Humanoid | 4 model FBX + 24 clip FBX (`animationType: 3`) |
| Generic | 117 of the older 124 model FBX (new static exports default the same) (`animationType: 2`) — creatures, vehicles, rigid-part rigs |
| Avatar source | [`Characters/Astronaut/AstronautArmature.fbx`](Assets/Game/Art/Models/Characters/Astronaut/AstronautArmature.fbx) is `avatarSetup: 1` (Create From This Model); every clip in [`Animations/Player/`](Assets/Game/Art/Animations/Player) is `avatarSetup: 2` (Copy From Other Avatar) pointing at it |

- Player clips are retargeted mocap FBX driving [`AstronautArmature.controller`](Assets/Game/Art/Animations/Player); `UpperBody.mask` gates hold-pose layers.
- Creature clips are authored in Blender (`*_anim.py`) or generated by the builder, and land as `.anim` next to a `.controller` in [`Animations/Creatures/`](Assets/Game/Art/Animations/Creatures) (Vrescal, Golem, DuneRat).
- Export keeps the rig only when Unity drives bones: `export(..., keep_armature=True)`. `add_leaf_bones=False` is always set — Blender's `<bone>_end` tips otherwise appear as real transforms and break bone-walking code.
- Rigid-part rigs (meshes parented to bones, not skinned) are the ones the root-motion stripper exists for; skinned rigs never get a root curve.

## Flows

1. Read [`LIBRARY.md`](Assets/Game/Art/Models/_Source~/LIBRARY.md) and [`PALETTE.md`](Assets/Game/Art/Models/_Source~/PALETTE.md); reuse an existing component before building one.
2. Write `components/<cat>/thing.py` on top of `_buildlib` (or `_tracked.TrackedPart`), run `blender --background --python …`, save `thing.blend`. Verify with [`_preview.py`](Assets/Game/Art/Models/_Source~/_preview.py) and [`_zverify.py`](Assets/Game/Art/Models/_Source~/_zverify.py).
3. Assemble `models/<cat>/model.py` → `model.blend`; write `model_BUILD.md`.
4. Write `models/<cat>/model_export.py` calling `_exportlib.export(SRC, unity_path("Category", "model.fbx"), keep_armature=…)`; run it.
5. Let Unity import; add or extend a `*Builder.cs` in `Assets/Game/Editor/` that builds the prefab, clips and controller from the FBX. Never hand-wire the prefab.
6. Re-run the library index and palette doc (see the skill; both scripts default to the **old** `Assets/Models/_Source~` path and must be pointed at `Assets/Game/Art/Models/_Source~` explicitly).

## Multiplayer

N/A — art assets carry no authority split. The one crossing point: a runtime-spawned prefab a builder produces must be registered in the network prefab list (see [Multiplayer.md](Multiplayer.md)).

## Persistence

N/A for the art assets themselves. Builders that emit spawnable prefabs must stamp a prefab id, or the entity vanishes on load — see [Persistence.md](Persistence.md).

## Gotchas

- **Never re-run a generator over an existing `.blend`.** The `.blend` is the source of truth and carries hand edits that exist nowhere else; `_buildlib.start()` hard-fails on this, but a script that bypasses it will destroy the file. Compare object *scales*, not names, to detect a hand-edited file. Notably hand-built: `models/vehicles/ship_lander_blockout.blend` (the user's interior), the `nomad.blend` family (which is why `nomad_before_*.blend` snapshots exist), `models/creatures/vrescal.blend`.
- **Blender FBX import at `lossyScale = 100`** on every transform (FBX centimetre convention): mesh data 100× small under transforms 100× large. It cancels for the model, but anything sized *against* a socket must divide by `socket.lossyScale` or it comes out 100× too big. `globalScale: 1` / `useFileScale: 1` on every model FBX meta; export uses `FBX_SCALE_NONE`, 1 Blender unit = 1 m.
- **Axis conversion is fixed at `-Z` forward / `Y` up.** Blender's −Y forward lands on Unity's +Z. Changing it silently rotates new assets relative to every existing one.
- **Stale renderer material overrides.** After an FBX reimport that changes submesh count/order, prefab renderers keep the old `sharedMaterials` array and render the wrong material with no error. Re-sync from the model (re-run the builder).
- **Materials freeze the shader defaults they were born with** — a `.mat` created against an older shader keeps those values when the shader gains new properties, so builders must rewrite their tunables on every run rather than assuming defaults.
- **`avatar.isHuman == false` fails silently.** A humanoid FBX re-exported with a changed hierarchy stops animating with a clean console. Check `isHuman` after any character re-export. Copy-From-Other-Avatar cannot be configured on an armature-only FBX — that one must be Create From This Model.
- **`_Source~` and `_backups~` are invisible to Unity.** No `.meta`, no GUIDs, nothing there can be referenced from a scene or prefab. Conversely, an export written to the pre-restructure `Assets/Models/` path is an orphan nothing imports — always go through `_exportlib.unity_path()`.
- **`_buildlib.Part._absorb` mis-assigns materials** across absorbed geometry (Blender reorders faces on `from_mesh`); symptom is a handful of faces in a neighbouring part's colour. New work uses [`_tracked.TrackedPart`](Assets/Game/Art/Models/_Source~/_tracked.py) and calls `restamp()` **before** bevelling. Bevel's own faces keep material index 0, so `MATS[0]` must be a safe default.
- **A sub-part that STARTS or ENDS on the plane of the part it decorates z-fights.** This is by far the commonest way a correct-looking generator produces a flickering model: a sleeve whose bottom is the barrel's bottom, a lip whose base is the cap's base, a plinth whose top is the post's bottom, a corner post flush with the tower's own side. `oxygen_generator.blend` had 15 such pairs (0.353 m²) purely from blocks built flush to the machine's back plane. Sharing the plane is exactly what "sitting on it" reads like in source, so nothing looks wrong. **Every decorative sub-part must overshoot its parent's plane by ≥ 3 mm or be buried inside it** — never meet it. Verify with `_zverify.py`, which reports the separation in mm; anything at 0.000 mm will flicker.
- **A wide bevel swells a small fitting past its own bounds.** Raising `BEVEL_W` for a stylised chamfer (10 mm on 0.3 m blocks) applies it to whatever is in the `hard` list, and on a 14 mm slot or a 22 mm vent slat a 10 mm chamfer both eats the shape and pushes it into neighbours that were clear of it. Use two widths — coarse for the big blocks, ~3 mm for panel hardware — and bevel the **fine set first**, or the coarse pass re-walks edges the fine pass already rounded.
- **`_zverify.py` over-reports on a component file.** Variations are stacked at the origin by library convention, so it flags pairs between variations that never ship together. Only same-variation pairs are real; filter by the variation token in the object name before acting. On an assembled *model* file every pair is real.
- **`Matrix.Rotation(a, 4, 'Z')` carries local +X radially outward, not +Y.** Placing a rib, plate or boss around a barrel with the sizes in the wrong order gives a rib thin in the tangential direction instead of the radial one — which still looks like a rib, just wrong, and the origin and angle are both correct so nothing points at the bug. Assert the mapping (`rot @ (1,0,0) == out`) rather than reading it off the call.
- **A negative scale renders inside-out in Unity and looks right in Blender.** Dragging a scale gizmo past zero leaves the object on a negative-determinant transform. Blender's viewport respects the flip; the FBX carries the negative scale straight through and Unity shows the back faces — you see through the front of the mesh. Nothing errors. Detect it by the **determinant of `matrix_world.to_3x3()`**, confirm with the mesh's world-space signed volume going negative, and never by eye. [`_exportlib.export(fix_inverted=True)`](Assets/Game/Art/Models/_Source~/_exportlib.py) bakes the transform and recalculates normals **in memory at export time**, so a hand-edited `.blend` is never written to; it is opt-in so no model already shipping changes. Verify by re-importing the FBX and re-measuring, not by trusting the log line.
- **Scaling one part of an assembly moves its faces onto planes other parts were clear of.** Widening the oxygen generator's tower put the straps' side segments exactly on the tower's new side plane — four fresh 0.000 mm clashes from a change that touched neither part's geometry. Re-run `_zverify.py` after any transform edit, not only after a geometry edit.
- **A stand-off under 3 mm is a clash, and 2.000 mm is a coin toss.** `_zverify` treats parallel faces within `SEP` = 2 mm as coplanar, so the handheld family's 1.5 mm-proud slots and 2 mm-proud plates — which read as recesses on a 0.1 m instrument — are reported as clashes at any scale, and a part planted exactly 2.000 mm off a plane passes or fails on floating point: the keyboard deck's keys were clean in `keyboard_deck.blend` and flagged in `standing_terminal.blend`, where the same geometry sits under a 12° rotation and the world-space distance rounded the other way. Stand everything off by ≥ 3 mm (`_console_kit.EMBED`) and plant a fitting that passes through a proud plate twice that deep (`PLANT`), or its base ends on the plate's own back plane.
- **A rounded bezel built from four bevelled slabs has a chamfer groove across its face at every joint.** The bevel rounds each slab's own edges, joints included. Build a frame as one ring (`_console_kit.rounded_frame`) and a tray as one rounded prism (`rounded_slab`), and give extruded rounded outlines flat shading on their straight walls — smoothing them the way `_buildlib` smooths cylinder barrels pillows a 0.6 m wall from the arcs' tilted normals.
- Auto-suffixed names (`Cube.003`, `Material.001`) are rejected at save time by `_buildlib.save()`; keep every object, mesh, collection and bone deliberately named. A **hand-added** object can still arrive named `Cylinder` — `save()` only guards the generator path — and it ships into the FBX under that name.

## Extending

1. Adding a variant of an existing thing → new collection in that component's existing `.blend` via MCP, not a new file.
2. Needing a colour → `palette.py check` first; only `add` when nothing serves, with a note saying why the near-miss does not.
3. New category → a folder under `components/` or `models/` only when nothing existing is a plausible home; the Unity-side sibling under `Assets/Game/Art/Models/` must match.
4. New builder → copy the shape of [`VrescalBuilder.cs`](Assets/Game/Editor/Creatures/VrescalBuilder.cs): idempotent, re-runnable, driven entirely from the FBX, reachable from a `Tools >` menu item, and documented in its own header.
5. Changing shared export behaviour → edit [`_exportlib.py`](Assets/Game/Art/Models/_Source~/_exportlib.py), never fork the flags into a per-model script (that divergence is exactly why the module exists).
