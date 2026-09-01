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
reads_with: [Vehicles, PlayerShip, AgentSystem, Backpack]
updated: 2026-09-01
---

# Art Pipeline

How a 3D asset gets from a hand-authored/scripted `.blend` in the Unity-invisible source library to an FBX, a material, a rig and a prefab in the game.

**Scope:** [`Assets/Game/Art/`](Assets/Game/Art) — `Models/`, `Models/_Source~/`, `Materials/`, `Animations/`, `Textures/`, `Shaders/`; the model-facing editor tools in [`Assets/Game/Editor/`](Assets/Game/Editor).
**Related:** [blender-model skill](.claude/skills/blender-model/SKILL.md) (authoring rules, palette CLI, conventions — not restated here), [Vehicles.md](Vehicles.md), [PlayerShip.md](PlayerShip.md), [AgentSystem.md](AgentSystem.md), [Backpack.md](Backpack.md).

## Model

- **Author** in [`Assets/Game/Art/Models/_Source~/`](Assets/Game/Art/Models/_Source~): `components/<cat>/x.blend` for reusable parts, `models/<cat>/y.blend` for assembled deliverables. Each `.blend` sits next to its generator `.py` and usually a `<name>_BUILD.md` record.
- **Geometry helpers** come from [`_buildlib.py`](Assets/Game/Art/Models/_Source~/_buildlib.py) (bevelled boxes, tubes, lofts, rivets, palette linking). `_buildlib.start()` **refuses to overwrite an existing `.blend`** — the file, not the script, is the source of truth.
- **Materials** are *linked* from [`palette.blend`](Assets/Game/Art/Models/_Source~/palette.blend); nothing defines a local material.
- **Export** via `<model>_export.py` → [`_exportlib.export()`](Assets/Game/Art/Models/_Source~/_exportlib.py), which localises linked palette materials, optionally drops armatures, and writes to `unity_path(...)` = `Assets/Game/Art/Models/<Category>/name.fbx`. Exports **are** meant to be re-run; they never write back to the `.blend`.
- **Import** into Unity is automatic; [`MeshReadablePostprocessor`](Assets/Game/Editor/AssetPipeline/MeshReadablePostprocessor.cs) forces Read/Write on every mesh (runtime NavMesh baking) and [`RootMotionCurveStripper`](Assets/Game/Editor/AssetPipeline/RootMotionCurveStripper.cs) deletes root-bound curves from imported clips.
- **Prefab** is generated, not hand-wired, by a `*Builder.cs` under [`Assets/Game/Editor/`](Assets/Game/Editor) (26 of them; menu `Tools > …`). Re-running a builder after a re-export rebuilds import settings, clips, controllers and the prefab in place.
- **Walkable interiors** additionally get a baked convex decomposition from [`_collisionlib.py`](Assets/Game/Art/Models/_Source~/_collisionlib.py) — Unity refuses a concave MeshCollider on a Rigidbody.

## Layout

| Directory | Contains | Visible to Unity? |
|---|---|---|
| [`Assets/Game/Art/Models/_Source~/`](Assets/Game/Art/Models/_Source~) | `.blend` masters, generator/export `.py`, `PALETTE.md`, `LIBRARY.md`, `library_index.json`, `palette.blend` | **No** — trailing `~`; no `.meta` files, no Blender install needed to open the project |
| [`_Source~/components/{structural,props,mechanical,organic}/`](Assets/Game/Art/Models/_Source~/components) | 94 reusable component `.blend`s, variations as `Coll_*` collections | No |
| [`_Source~/models/{buildings,characters,creatures,gear,vehicles}/`](Assets/Game/Art/Models/_Source~/models) | 42 assembled model `.blend`s | No |
| [`Assets/Game/Art/Models/_backups~/`](Assets/Game/Art/Models/_backups~) | pre-surgery snapshots (`vrescal_before_legs.blend`, …) | No |
| [`Assets/Game/Art/Models/<Category>/`](Assets/Game/Art/Models) | 124 exported/imported `.fbx` + `.meta` | Yes |
| [`Assets/Game/Art/Materials/`](Assets/Game/Art/Materials) | 121 `.mat` in 13 domain folders | Yes |
| [`Assets/Game/Art/Animations/{Player,Creatures,UI}/`](Assets/Game/Art/Animations) | mocap FBX, `.anim`, `.controller`, `UpperBody.mask` | Yes |
| [`Assets/Game/Art/{Shaders,Textures,Sprites,VisualEffects,Brushes}/`](Assets/Game/Art) | shader graphs/HLSL, textures (own `Textures/Items/_Source~`), icons, VFX | Yes |
| [`Assets/ThirdParty/`](Assets/ThirdParty) | bought/free packs (Sci-Fi RTS, Cosmic Retro Blasters, Kevin Iglesias anims, TMP) — outside this pipeline | Yes |

## Model library

124 FBX under `Assets/Game/Art/Models/`. Generated assets are `snake_case`; older/imported ones are `camelCase` or `PascalCase`.

| Category | Example files | Count |
|---|---|---|
| [Environment](Assets/Game/Art/Models/Environment) | `Caves/Stalagmites/*`, `Structures/Outpost/*`, `Blockouts/tower.fbx`, `Ruins/SpaceRuin.fbx` | 35 |
| [Vehicles](Assets/Game/Art/Models/Vehicles) | `Crawler/desert_crawler.fbx`, `Ornithopter/dune_ornithopter.fbx`, `PlayerShip/*`, `RV/*`, `DuneFoil/*` | 34 |
| [Items](Assets/Game/Art/Models/Items) | `portal_gun.fbx`, `net_gun.fbx`, `item_scanner.fbx`, `jumping_rod.fbx`, `ShipParts/*` | 26 |
| [Creatures](Assets/Game/Art/Models/Creatures) | `Organic/DuneRat/dune_rat.fbx`, `Constructs/Golem/golem.fbx`, `Organic/OrkhenRhot/*`, `Robotic/*` | 13 |
| [Weapons](Assets/Game/Art/Models/Weapons) | `GravelBlaster/gravel_blaster.fbx`, `LaserStaff/*`, `CixinGun/*` | 7 |
| [Props](Assets/Game/Art/Models/Props) | `expedition_rig.fbx`, `field_backpack.fbx`, `pack_holders.fbx` | 4 |
| [Characters](Assets/Game/Art/Models/Characters) | `Astronaut/astronaut.fbx`, `Astronaut/AstronautArmature.fbx`, `Nomad/nomad.fbx` | 4 |

## Materials

- **One shared palette**, documented in [`PALETTE.md`](Assets/Game/Art/Models/_Source~/PALETTE.md) and generated from `palette.blend`: **54 materials across 10 categories** (Emissive, Fabric, Foliage, Glass, Hide, Metal, Neutral, Paint, Plastic, Wood). Naming is `Mat_<Category>_<Descriptor>` — `Mat_Metal_Steel_Worn`, `Mat_Emissive_Portal_Blue`.
- The palette **starts empty and grows on demand**; the skill's `palette.py add` refuses a perceptually near-duplicate and names the existing material instead. Every entry's "Intended for" note records why it is not a duplicate — read it before adding a fifty-fifth grey.
- **How a mesh gets its material:** face material indices are stamped in Blender from linked palette slots → `_exportlib` calls `make_local()` (a *linked* material does not survive into the FBX; without this the meshes arrive untextured) → Unity imports them as **sub-assets of the FBX** (`materialLocation: 1`, `materialName: 0`, `materialSearch: 1` on all 124 metas), regenerated on every reimport.
- Because they are sub-assets, per-material flags cannot be edited in place. [`DoubleSidedMaterials.Apply()`](Assets/Game/Editor/Support/DoubleSidedMaterials.cs) copies each to `Assets/Game/Art/Materials/Vehicles/<name> (DoubleSided).mat` and rewires the renderers — 74 of the 82 files there are these generated copies. Vehicle hulls are modelled as surfaces, so back-face culling makes cabins see-through.
- Hand-authored `.mat` assets (terrain, portal, surfaces, VFX) live in the domain folders under [`Materials/`](Assets/Game/Art/Materials) and are unrelated to the palette. `Materials/Untitled/New Material 1-3.mat` are junk.

## Rigs & animation

| | Setting |
|---|---|
| Humanoid | 4 model FBX + 24 clip FBX (`animationType: 3`) |
| Generic | 117 of 124 model FBX (`animationType: 2`) — creatures, vehicles, rigid-part rigs |
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
- **Blender FBX import at `lossyScale = 100`** on every transform (FBX centimetre convention): mesh data 100× small under transforms 100× large. It cancels for the model, but anything sized *against* a socket must divide by `socket.lossyScale` or it comes out 100× too big. `globalScale: 1` / `useFileScale: 1` on all 124 metas; export uses `FBX_SCALE_NONE`, 1 Blender unit = 1 m.
- **Axis conversion is fixed at `-Z` forward / `Y` up.** Blender's −Y forward lands on Unity's +Z. Changing it silently rotates new assets relative to every existing one.
- **Stale renderer material overrides.** After an FBX reimport that changes submesh count/order, prefab renderers keep the old `sharedMaterials` array and render the wrong material with no error. Re-sync from the model (re-run the builder).
- **Materials freeze the shader defaults they were born with** — a `.mat` created against an older shader keeps those values when the shader gains new properties, so builders must rewrite their tunables on every run rather than assuming defaults.
- **`avatar.isHuman == false` fails silently.** A humanoid FBX re-exported with a changed hierarchy stops animating with a clean console. Check `isHuman` after any character re-export. Copy-From-Other-Avatar cannot be configured on an armature-only FBX — that one must be Create From This Model.
- **`_Source~` and `_backups~` are invisible to Unity.** No `.meta`, no GUIDs, nothing there can be referenced from a scene or prefab. Conversely, an export written to the pre-restructure `Assets/Models/` path is an orphan nothing imports — always go through `_exportlib.unity_path()`.
- **`_buildlib.Part._absorb` mis-assigns materials** across absorbed geometry (Blender reorders faces on `from_mesh`); symptom is a handful of faces in a neighbouring part's colour. New work uses [`_tracked.TrackedPart`](Assets/Game/Art/Models/_Source~/_tracked.py) and calls `restamp()` **before** bevelling. Bevel's own faces keep material index 0, so `MATS[0]` must be a safe default.
- Auto-suffixed names (`Cube.003`, `Material.001`) are rejected at save time by `_buildlib.save()`; keep every object, mesh, collection and bone deliberately named.

## Extending

1. Adding a variant of an existing thing → new collection in that component's existing `.blend` via MCP, not a new file.
2. Needing a colour → `palette.py check` first; only `add` when nothing serves, with a note saying why the near-miss does not.
3. New category → a folder under `components/` or `models/` only when nothing existing is a plausible home; the Unity-side sibling under `Assets/Game/Art/Models/` must match.
4. New builder → copy the shape of [`VrescalBuilder.cs`](Assets/Game/Editor/Creatures/VrescalBuilder.cs): idempotent, re-runnable, driven entirely from the FBX, reachable from a `Tools >` menu item, and documented in its own header.
5. Changing shared export behaviour → edit [`_exportlib.py`](Assets/Game/Art/Models/_Source~/_exportlib.py), never fork the flags into a per-model script (that divergence is exactly why the module exists).
