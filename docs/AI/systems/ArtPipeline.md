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
  - "the hair shreds into floating shards once the animation plays, but is fine in the rest pose"
  - "one material kept its texture through export and the others silently lost theirs"
  - "the walk cycle plays but the legs never move, and the clip is full of keyframes"
  - "the animator controller came out with only the clips the model used to have"
  - "only the skinned parts of the model render inside-out; the rigid props are fine"
  - "the feet swing correctly but the legs above them stay rigid, so the creature skates"
  - "dark patches that look like holes punched in the model, symmetric on both sides"
  - "opening the creature's mouth drags its eyes and brow down with the jaw"
  - "the builder says the clip is missing from the FBX it just imported"
  - "Copied Avatar Rig Configuration mis-match: Transform not found in HumanDescription"
  - "the authored gesture plays but the character barely moves, or waves instead of reaching"
  - "a ring, band or collar on a model flickers where two parts meet and the generator's numbers look right"
  - "a small fitting swells and starts clashing with neighbours after the bevel width was raised"
  - "_zverify reports dozens of clashes in a component file that holds several variations"
  - "a mesh renders inside-out in Unity and looks correct in Blender"
reads_with: [Vehicles, PlayerShip, AgentSystem, Backpack]
updated: 2026-09-04
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
- **Prefab** is generated, not hand-wired, by a `*Builder.cs` under [`Assets/Game/Editor/`](Assets/Game/Editor) (39 of them; menu `Tools > …`). Re-running a builder after a re-export rebuilds import settings, clips, controllers and the prefab in place.
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
- **Anything worn is modelled at the wearer's true size**, measured off the skinned character rather than
  guessed — bake the mesh, keep the vertices bound to the bone in question, and bin them along and around
  the limb. The gauntlet base was cut that way; an earlier one built to a remembered forearm radius vanished
  inside the suit sleeve.
- **Walkable interiors** additionally get a baked convex decomposition from [`_collisionlib.py`](Assets/Game/Art/Models/_Source~/_collisionlib.py) — Unity refuses a concave MeshCollider on a Rigidbody.

## Layout

| Directory | Contains | Visible to Unity? |
|---|---|---|
| [`Assets/Game/Art/Models/_Source~/`](Assets/Game/Art/Models/_Source~) | `.blend` masters, generator/export `.py`, `PALETTE.md`, `LIBRARY.md`, `library_index.json`, `palette.blend` | **No** — trailing `~`; no `.meta` files, no Blender install needed to open the project |
| [`_Source~/components/{structural,props,mechanical,organic}/`](Assets/Game/Art/Models/_Source~/components) | 94 reusable component `.blend`s, variations as `Coll_*` collections | No |
| [`_Source~/models/{buildings,characters,creatures,gear,vehicles}/`](Assets/Game/Art/Models/_Source~/models) | 44 assembled model `.blend`s | No |
| [`Assets/Game/Art/Models/_backups~/`](Assets/Game/Art/Models/_backups~) | pre-surgery snapshots (`vrescal_before_legs.blend`, …) | No |
| [`Assets/Game/Art/Models/<Category>/`](Assets/Game/Art/Models) | 126 exported/imported `.fbx` + `.meta` | Yes |
| [`_Source~/components/{structural,props,mechanical,organic}/`](Assets/Game/Art/Models/_Source~/components) | 97 reusable component `.blend`s, variations as `Coll_*` collections | No |
| [`_Source~/models/{buildings,characters,creatures,gear,props,vehicles}/`](Assets/Game/Art/Models/_Source~/models) | 44 assembled model `.blend`s | No |
| [`Assets/Game/Art/Models/_backups~/`](Assets/Game/Art/Models/_backups~) | pre-surgery snapshots (`vrescal_before_legs.blend`, …) | No |
| [`Assets/Game/Art/Models/<Category>/`](Assets/Game/Art/Models) | 128 exported/imported `.fbx` + `.meta` | Yes |
| [`Assets/Game/Art/Materials/`](Assets/Game/Art/Materials) | 121 `.mat` in 13 domain folders | Yes |
| [`Assets/Game/Art/Animations/{Player,Creatures,UI}/`](Assets/Game/Art/Animations) | mocap FBX, `.anim`, `.controller`, `UpperBody.mask` | Yes |
| [`Assets/Game/Art/{Shaders,Textures,Sprites,VisualEffects,Brushes}/`](Assets/Game/Art) | shader graphs/HLSL, textures (own `Textures/Items/_Source~`), icons, VFX | Yes |
| [`Assets/ThirdParty/`](Assets/ThirdParty) | bought/free packs (Sci-Fi RTS, Cosmic Retro Blasters, Kevin Iglesias anims, TMP) — outside this pipeline | Yes |

## Model library

126 FBX under `Assets/Game/Art/Models/`. Generated assets are `snake_case`; older/imported ones are `camelCase` or `PascalCase`.
129 FBX under `Assets/Game/Art/Models/`. Generated assets are `snake_case`; older/imported ones are `camelCase` or `PascalCase`.

| Category | Example files | Count |
|---|---|---|
| [Environment](Assets/Game/Art/Models/Environment) | `Caves/Stalagmites/*`, `Structures/Outpost/*`, `Blockouts/tower.fbx`, `Ruins/SpaceRuin.fbx` | 35 |
| [Vehicles](Assets/Game/Art/Models/Vehicles) | `Crawler/desert_crawler.fbx`, `Ornithopter/dune_ornithopter.fbx`, `PlayerShip/*`, `RV/*`, `DuneFoil/*` | 34 |
| [Items](Assets/Game/Art/Models/Items) | `portal_gun.fbx`, `net_gun.fbx`, `item_scanner.fbx`, `jumping_rod.fbx`, `ShipParts/*` | 26 |
| [Creatures](Assets/Game/Art/Models/Creatures) | `Organic/DuneRat/dune_rat.fbx`, `Organic/Appa/appa.fbx`, `Constructs/Golem/golem.fbx`, `Organic/OrkhenRhot/*`, `Robotic/*` | 14 |
| [Weapons](Assets/Game/Art/Models/Weapons) | `GravelBlaster/gravel_blaster.fbx`, `LaserStaff/*`, `CixinGun/*` | 7 |
| [Props](Assets/Game/Art/Models/Props) | `expedition_rig.fbx`, `holo_base_puck.fbx`, `holo_base_table.fbx`, `repair_station.fbx` | 9 |
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
| Generic | 118 of 126 model FBX (`animationType: 2`) — creatures, vehicles, rigid-part rigs |
| Generic | 117 of the older 124 model FBX (new static exports default the same) (`animationType: 2`) — creatures, vehicles, rigid-part rigs |
| Avatar source | [`Characters/Astronaut/AstronautArmature.fbx`](Assets/Game/Art/Models/Characters/Astronaut/AstronautArmature.fbx) is `avatarSetup: 1` (Create From This Model); every clip in [`Animations/Player/`](Assets/Game/Art/Animations/Player) is `avatarSetup: 2` (Copy From Other Avatar) pointing at it |

- Player clips are retargeted mocap FBX driving [`AstronautArmature.controller`](Assets/Game/Art/Animations/Player); `UpperBody.mask` gates hold-pose layers.
- Creature clips are authored in Blender (`*_anim.py`) or generated by the builder, and land as `.anim` next to a `.controller` in [`Animations/Creatures/`](Assets/Game/Art/Animations/Creatures) (Vrescal, Golem, DuneRat, Appa).
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

- **Some library `.blend` files cannot be opened by Blender 4.2**, the only Blender installed —
  `palette.blend`, `components/props/supply_crate.blend`, `models/creatures/dune_rat.blend`, all
  *"not a blend file"*, written by a newer one. Assume more. Never "fix" one by re-running its
  generator; build a new file under a new name (`sandloper.py` reads the shipped FBX instead).
- **Never re-run a generator over an existing `.blend`.** The `.blend` is the source of truth and carries hand edits that exist nowhere else; `_buildlib.start()` hard-fails on this, but a script that bypasses it will destroy the file. Compare object *scales*, not names, to detect a hand-edited file. Notably hand-built: `models/vehicles/ship_lander_blockout.blend` (the user's interior), the `nomad.blend` family (which is why `nomad_before_*.blend` snapshots exist), `models/creatures/vrescal.blend`.
- **Blender FBX import at `lossyScale = 100`** on every transform (FBX centimetre convention): mesh data 100× small under transforms 100× large. It cancels for the model, but anything sized *against* a socket must divide by `socket.lossyScale` or it comes out 100× too big. `globalScale: 1` / `useFileScale: 1` on every model FBX meta; export uses `FBX_SCALE_NONE`, 1 Blender unit = 1 m.
- **Axis conversion is fixed at `-Z` forward / `Y` up.** Blender's −Y forward lands on Unity's +Z. Changing it silently rotates new assets relative to every existing one.
- **Stale renderer material overrides.** After an FBX reimport that changes submesh count/order, prefab renderers keep the old `sharedMaterials` array and render the wrong material with no error. Re-sync from the model (re-run the builder).
- **Materials freeze the shader defaults they were born with** — a `.mat` created against an older shader keeps those values when the shader gains new properties, so builders must rewrite their tunables on every run rather than assuming defaults.
- **`avatar.isHuman == false` fails silently.** A humanoid FBX re-exported with a changed hierarchy stops animating with a clean console. Check `isHuman` after any character re-export. Copy-From-Other-Avatar cannot be configured on an armature-only FBX — that one must be Create From This Model.
- **`_Source~` and `_backups~` are invisible to Unity.** No `.meta`, no GUIDs, nothing there can be referenced from a scene or prefab. Conversely, an export written to the pre-restructure `Assets/Models/` path is an orphan nothing imports — always go through `_exportlib.unity_path()`.
- **`_buildlib.Part._absorb` mis-assigns materials** across absorbed geometry (Blender reorders faces on `from_mesh`); symptom is a handful of faces in a neighbouring part's colour. New work uses [`_tracked.TrackedPart`](Assets/Game/Art/Models/_Source~/_tracked.py) and calls `restamp()` **before** bevelling. Bevel's own faces keep material index 0, so `MATS[0]` must be a safe default.
- **`optimizeGameObjects` must stay off for a rigid-part rig.** Meshes parented to bones hang off exactly the transforms that optimising strips, so turning it on deletes the model in pieces with no error. Appa is 21 bone-parented props against 6 skinned meshes; the Vrescal is the same shape. Only a fully skinned model (the Dune Rat) could safely enable it, and does not.
- **A packed image exports as no texture at all.** An image packed into the `.blend` has `filepath == ""`, and `embed_textures=False` writes a path -- so the FBX ships with no texture reference and every material arrives in Unity flat-coloured. Setting `filepath` is *not* enough: while `packed_file` is set the exporter still falls back to the embedded bytes and logs `Image "" not available. Keeping packed image`. It has to be genuinely unpacked (`img.unpack()`; `packed_file` is read-only and cannot be cleared). `appa_export.py` writes the images into `<fbx dir>/Textures/` and unpacks them, at export, so the author's `.blend` keeps its packed originals.
- **Inconsistent face winding is invisible in Blender and lit wrong in Unity.** Blender draws both sides of a face, so a mesh whose faces disagree about which way is out looks perfect there; Unity lights the side the normal points at, so those patches come out dark while the rest of the same mesh is fine. Appa's mane and shoulder fur each had ~5% of faces wound the wrong way and the ears exactly 50%, which read to the author as "the fur is messed up ... the UVs seem broken" — the UVs were clean. Recalculate normals outward at export (`appa_export.py::_make_normals_consistent`); reversing whole meshes does not fix it, because a blanket reverse preserves the disagreement. It must run **after** the transform bake below, so the counts it prints are much larger than the author's own inconsistency — the mane's 6240-of-6618 is the mirror being undone, not 6240 hand-modelling mistakes.
- **Single-axis bone probes do not survive extrapolation to a real pose.** The measured table for the astronaut's `RightArm` says -X is forward and -Z is up — true at the ±25° the probe used. Authoring a reach at x=-64, z=-46 off that table put the hand **1.09 m to the character's right and 0.17 m forward**: an arm held straight out sideways, which read as waving rather than petting. At 60° the rotations compose nowhere near the small-angle prediction, and the real forward swing turned out to be **+Y**. Probe the axes to learn which ones do anything, then find the pose by searching the space and measuring the **end effector** against a body landmark — `astronaut_pet.py` targets "hand 0.55 m forward, 0.18 m up, 0.22 m right of the chest" and the answer came out `RightArm y=+60 z=-30, RightForeArm x=-70`. Cheap: a few hundred depsgraph evaluations in a headless Blender run.
- **A failed avatar empties the take list, and the error you get blames the exporter.** Copying the astronaut's avatar onto the Blender-authored gesture fails with `Copied Avatar Rig Configuration mis-match. Transform 'Armature' not found in HumanDescription` — that avatar is rooted at a node called `AstronautArmature(Clone)` and Blender always names its armature object `Armature`. Unity then abandons the import, so `defaultClipAnimations` comes back **empty** and the next check reports "carries no animation take at all". This is the Copy-From-Other-Avatar gotcha above, arriving in disguise: an armature-only FBX must be **Create From This Model**. Retargeting is unaffected — both rigs are humanoid, and a humanoid clip plays through the humanoid abstraction rather than through bone names. When a take list comes back empty, check `Avatar.isValid` / `isHuman` **before** blaming the export.
- **Blender's FBX exporter bakes the SCENE range and names the take after the scene, unless you tell it otherwise.** `astronaut_pet.py` authored a 60-frame action and shipped a **251-frame take called `Scene`** — Blender's default 1..250 range, gesture buried in the first fifth of it. `bake_anim_use_all_actions=True` is what gives one take per action, named `<object>|<action>`; `scene.frame_start/frame_end` is what bounds it. `appa_export.py` never hit this because it sets both. Downstream, **never guess a take name** — read `ModelImporter.defaultClipAnimations` and use its `takeName` and its frame bounds, which is what `PlayerPetGestureBuilder.ImportClip` now does in a deliberate two-pass import: `defaultClipAnimations` is empty until `importAnimation` is on and the file has been reimported once. A `clipAnimations` entry naming a take that does not exist produces **no clip and no error**.
- **`SaveAndReimport()` does not guarantee the new clips are readable in the same run.** A builder that configures `ModelImporter.clipAnimations`, reimports, and then reads clips back with `AssetDatabase.LoadAllAssetsAtPath` can still get the **previous** clip set on the first build after new takes are added to the FBX. What that produced on Appa was an animator controller holding only Idle and Walk — no Roar, Ram, Hurt or Death, no error anywhere — and a creature that could be enraged and would charge in silence with a walk cycle playing. Check the clip set before building anything on it and abort with a message (`AppaBuilder.ClipsAreImported`); running the builder a second time then succeeds. Do not let it write a plausible controller over a good one.
- **Never key a raw euler component on a hand-made rig — measure the axis first.** A pose bone rotates about its *local* axes, and those are wherever the bone's roll left them. On Appa, +15° about local **Y** moves the tip of every single bone by **nothing**: Y runs along the bone, so keying it is a pure twist. `appa_anim.py` originally put the entire leg swing and the entire body bob on Y, and the result was a walk cycle in which no leg ever moved — the animal slid with its feet locked in the rest pose, the file was full of keyframes, and every count-based check called it healthy. Verify by posing each bone ±15° about each axis and printing where the tip actually goes; then author through a helper that converts a **world**-space pitch/yaw/roll into that bone's local euler (`appa_anim.py::pose`), so the clip reads as "nose up 20°" and cannot silently animate nothing. Guard it too: `appa_anim.py::verify` fails the build if any clip has no curve whose value actually changes, and if a gait clip moves fewer than 12 leg curves.
- **Never ship bone-heat weights unfiltered.** `ARMATURE_AUTO` binds by proximity, and proximity lies wherever geometry hangs near a bone it has nothing to do with. On Appa it bound **24.4% of the shoulder fur and 7.4% of the mane to `femur_*`** — the mane drapes around the front legs — so every stride dragged the hair apart into floating shards. It is invisible in Blender, because the rest pose never moves, and only appears in play mode. Restrict each skinned mesh to the bones that can legitimately drive it and renormalise (`appa_rig.py::WEIGHT_RESTRICT` / `restrict_weights`). Watch for vertices left with **zero** total weight after filtering: those collapse to the origin, so give them a fallback bone.
- **A dark "hole" with clean topology is a shading problem, and cavity/AO shading is what hides that from you.** Appa's front-leg armpits were reported as holes twice. The body mesh measures clean on every test that could make one: **0 boundary edges, 0 self-intersections, 0 winding-inconsistent edges, 0 Laplacian outlier vertices, no narrow slots, no dark texture faces, and 0 enclosed see-through pixels** when the silhouette is ray-cast from the reported camera angle. What is there is a deep hooked recess that takes no light. Diagnose with **flat** shading (`light='FLAT'`, `show_cavity=False`) and one colour per object — Workbench's cavity and URP's AO both darken every crease, so a fold and a puncture look identical and you go hunting topology that is fine. The fix was geometry in service of shading: `appa_export.py::_soften_leg_junction` runs a few distance-weighted Laplacian passes over the junction, moving 549 vertices a median of 7 mm, applied **at export** so the author's sculpt is untouched.
- **An unposed bone is not a neutral bone — it is whatever the sculptor left it as.** Appa's mouth rests **26 deg open**: a clip that simply does not touch `jaw` ships him gaping, and `Appa_Walk`, `Appa_Run` and both turns did exactly that because `_build_gait` only keyed legs, spine and tail. Worse, "closed" is not the same as "unrotated", so the idle's authored gape was measured from an already-open mouth. The fix is to never pose the bone directly: `appa_anim.py::set_jaw` takes how far open the mouth should be **measured from shut** and adds the `JAW_CLOSED` bias itself, so every clip holds it closed by default and the numbers survive someone re-sculpting the head. `verify()` now fails any clip that does not key the jaw at all. Measure the bias empirically rather than guessing — rotate the bone in a live Unity instance until the lip line meets, which is what gave 26 deg.
- **A mirrored *skinned* mesh arrives inside-out; a mirrored *rigid* one does not.** Duplicating a half by negating a scale axis leaves a negative scale determinant, which flips triangle winding. For a rigid prop that is harmless — it arrives as a MeshRenderer still carrying the negative scale, and Unity reverses the culling mode on a negative-determinant renderer, cancelling the flip exactly. **A SkinnedMeshRenderer does not deform through its own transform**, so there is no negative-determinant renderer transform for Unity to notice, nothing reverses the culling, and the mesh lights by a normal pointing into itself. Appa has 16 mirrored meshes and exactly 3 are also skinned — the legs, the mane and the shoulder fur — and those 3 were the ones that looked wrong. Blender never shows it, because there the negative scale is still a live object transform that the viewport flips for; **re-importing the FBX into Blender to check also looks perfectly correct**, which is what makes this so hard to see. Fix at the source: bake the transform into the vertices (`appa_export.py::_apply_object_transforms`, mirroring `dune_foil_rig.py`) so no mirror survives for anyone to compensate for, then recalculate normals — in that order, since baking a negative scale reverses winding. Do **not** hand-reverse the mirrored meshes instead: a pass that did so broke the 13 rigid ones that never needed help, and the mane rendered as a black dome lit from below.
- **Do not make a closed mesh double-sided; on hair it is actively destructive.** Reach for [`DoubleSidedMaterials.Apply`](Assets/Game/Editor/Support/DoubleSidedMaterials.cs) only for geometry modelled as open sheets — a ship hull, a sail — where culling really does discard the front half. Check first, with `boundary edges == 0` in Blender rather than by eye: Appa's mane, shoulder fur, brow tuft and ears all *look* like sheets and are all closed volumes, and making them double-sided turned URP loose on each lock's interior. URP's Lit, unlike Blender's viewport, does **not** flip a back face's shading normal, so those interiors light black and win the depth test wherever locks interpenetrate. It is a plausible-looking fix for an inside-out mesh and it only ever masks the real cause — on Appa, the mirrored-skin bug above.
- **The FBX exporter ignores which Material Output is active.** A material with two shader chains -- a flat one and a textured one -- renders from whichever output is marked active, but the exporter takes the *first* Principled BSDF it finds. Five of Appa's materials were built that way and exported untextured while a sixth, with a single chain, came through fine. Delete the dead chain before exporting.
- Auto-suffixed names (`Cube.003`, `Material.001`) are rejected at save time by `_buildlib.save()`; keep every object, mesh, collection and bone deliberately named. **Hand-authored files that never go through `_buildlib` are exempt in practice** — `models/creatures/appa.blend` is 27 `Cube.*` meshes and 16 `Material.*` slots, all the author's, and renaming them would be an edit to someone else's file. Its `appa_BUILD.md` maps the names to body parts instead.
- **A sub-part that STARTS or ENDS on the plane of the part it decorates z-fights.** This is by far the commonest way a correct-looking generator produces a flickering model: a sleeve whose bottom is the barrel's bottom, a lip whose base is the cap's base, a plinth whose top is the post's bottom, a corner post flush with the tower's own side. `oxygen_generator.blend` had 15 such pairs (0.353 m²) purely from blocks built flush to the machine's back plane. Sharing the plane is exactly what "sitting on it" reads like in source, so nothing looks wrong. **Every decorative sub-part must overshoot its parent's plane by ≥ 3 mm or be buried inside it** — never meet it. Verify with `_zverify.py`, which reports the separation in mm; anything at 0.000 mm will flicker.
- **A wide bevel swells a small fitting past its own bounds.** Raising `BEVEL_W` for a stylised chamfer (10 mm on 0.3 m blocks) applies it to whatever is in the `hard` list, and on a 14 mm slot or a 22 mm vent slat a 10 mm chamfer both eats the shape and pushes it into neighbours that were clear of it. Use two widths — coarse for the big blocks, ~3 mm for panel hardware — and bevel the **fine set first**, or the coarse pass re-walks edges the fine pass already rounded.
- **`_zverify.py` over-reports on a component file.** Variations are stacked at the origin by library convention, so it flags pairs between variations that never ship together. Only same-variation pairs are real; filter by the variation token in the object name before acting. On an assembled *model* file every pair is real.
- **`Matrix.Rotation(a, 4, 'Z')` carries local +X radially outward, not +Y.** Placing a rib, plate or boss around a barrel with the sizes in the wrong order gives a rib thin in the tangential direction instead of the radial one — which still looks like a rib, just wrong, and the origin and angle are both correct so nothing points at the bug. Assert the mapping (`rot @ (1,0,0) == out`) rather than reading it off the call.
- **A negative scale renders inside-out in Unity and looks right in Blender.** Dragging a scale gizmo past zero leaves the object on a negative-determinant transform. Blender's viewport respects the flip; the FBX carries the negative scale straight through and Unity shows the back faces — you see through the front of the mesh. Nothing errors. Detect it by the **determinant of `matrix_world.to_3x3()`**, confirm with the mesh's world-space signed volume going negative, and never by eye. [`_exportlib.export(fix_inverted=True)`](Assets/Game/Art/Models/_Source~/_exportlib.py) bakes the transform and recalculates normals **in memory at export time**, so a hand-edited `.blend` is never written to; it is opt-in so no model already shipping changes. Verify by re-importing the FBX and re-measuring, not by trusting the log line.
- **Scaling one part of an assembly moves its faces onto planes other parts were clear of.** Widening the oxygen generator's tower put the straps' side segments exactly on the tower's new side plane — four fresh 0.000 mm clashes from a change that touched neither part's geometry. Re-run `_zverify.py` after any transform edit, not only after a geometry edit.
- Auto-suffixed names (`Cube.003`, `Material.001`) are rejected at save time by `_buildlib.save()`; keep every object, mesh, collection and bone deliberately named. A **hand-added** object can still arrive named `Cylinder` — `save()` only guards the generator path — and it ships into the FBX under that name.

## Extending

1. Adding a variant of an existing thing → new collection in that component's existing `.blend` via MCP, not a new file.
2. Needing a colour → `palette.py check` first; only `add` when nothing serves, with a note saying why the near-miss does not.
3. New category → a folder under `components/` or `models/` only when nothing existing is a plausible home; the Unity-side sibling under `Assets/Game/Art/Models/` must match.
4. New builder → copy the shape of [`VrescalBuilder.cs`](Assets/Game/Editor/Creatures/VrescalBuilder.cs): idempotent, re-runnable, driven entirely from the FBX, reachable from a `Tools >` menu item, and documented in its own header.
5. Changing shared export behaviour → edit [`_exportlib.py`](Assets/Game/Art/Models/_Source~/_exportlib.py), never fork the flags into a per-model script (that divergence is exactly why the module exists).
