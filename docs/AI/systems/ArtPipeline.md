---
system: ArtPipeline
layer: pipeline
summary: "How a .blend in the Unity-invisible source library becomes an FBX, material and rig in the game"
paths:
  - "Assets/Game/Art/Models/_Source~"
  - Assets/Game/Art/Models
  - Assets/Game/Art/Materials
  - Assets/Game/Art/Animations
  - Assets/Game/Editor/AssetPipeline
symptoms:
  - "a pouch, ring or band on a nomad renders inside out in Unity but looks fine in Blender"
  - "the imported mesh arrives untextured, or a handful of faces wear a neighbouring part's colour"
  - "the model comes out 100x too big when parented to a socket"
  - "a re-exported character stops animating and the console is clean"
  - "the new asset is rotated relative to every existing one"
  - "the prefab still renders the old materials after an FBX reimport"
  - "the exported FBX is nowhere in the project and nothing imports it"
  - "the model is correct in Blender and only some of its parts are in the wrong place in the FBX"
  - "an imported building is the right shape but its doors are half the height of the player"
  - "the hair shreds into floating shards once the animation plays, but is fine in the rest pose"
  - "a character stands correctly in the rest pose and its hands shred into spikes the moment it is posed"
  - "Rig Error: Required human bone 'LeftLowerLeg' not found, and the bone is plainly in the FBX"
  - "one material kept its texture through export and the others silently lost theirs"
  - "only the skinned parts of the model render inside-out; the rigid props are fine"
  - "dark patches that look like holes punched in the model, symmetric on both sides"
  - "Copied Avatar Rig Configuration mis-match: Transform not found in HumanDescription"
  - "a mesh renders inside-out in Unity and looks correct in Blender"
  - "a .blend in the library will not open: 'not a blend file'"
  - "an FBX take is named Scene and runs 250 frames"
  - "AnimationEvent 'X' has no receiver! Are you missing a component?, once per step"
  - "a PNG named after a Blender image appeared beside the .blend after an export"
  - "every bone of a re-exported character imports at scale 100"
  - "flapping an ear bends the back of the skull with it"
  - "the exported skin is a sibling's texture, not the one painted in Blender"
  - "every left-side vertex group of a copied character is empty"
reads_with: [Vehicles, PlayerShip, AgentSystem, Backpack]
updated: 2026-09-24
---

# Art Pipeline

How a 3D asset gets from a `.blend` in the Unity-invisible source library to an FBX, a material and a rig in the game.

**Scope:** [`Assets/Game/Art/`](Assets/Game/Art) — `Models/`, `Models/_Source~/`, `Materials/`, `Animations/`, `Textures/`, `Shaders/`; the import postprocessors in [`Assets/Game/Editor/AssetPipeline/`](Assets/Game/Editor/AssetPipeline).
**Related:** [Vehicles.md](Vehicles.md), [PlayerShip.md](PlayerShip.md), [AgentSystem.md](AgentSystem.md), [Backpack.md](Backpack.md).

> **The library is models, not machinery.** As of 2026-09-23 the generator scripts that once
> built these `.blend` files — `_buildlib`, `_tracked`, `_zverify`, `_preview`, every per-model
> `*.py`, `LIBRARY.md`, `PALETTE.md`, `library_index.json` — were deleted, along with the
> `*Builder.cs` editor passes that assembled prefabs from the FBX. **The `.blend` files and the
> exported FBX are the assets now.** A model is changed by opening its `.blend` in Blender and
> re-exporting; a prefab is changed by editing the prefab.

## Model

- **The `.blend` is the source of truth.** [`Assets/Game/Art/Models/_Source~/`](Assets/Game/Art/Models/_Source~) holds `components/<cat>/x.blend` (reusable parts, variations as `Coll_*` collections) and `models/<cat>/y.blend` (assembled deliverables). Many carry hand edits that exist nowhere else.
- **Materials** are *linked* from [`palette.blend`](Assets/Game/Art/Models/_Source~/palette.blend); models do not define local materials. See [Materials](#materials) for the one case where that silently stops being true.
- **Export** goes through [`_exportlib.py`](Assets/Game/Art/Models/_Source~/_exportlib.py) — the only build script kept. `export()` localises linked palette materials, optionally drops armatures, and writes to `unity_path(...)` = `Assets/Game/Art/Models/<Category>/name.fbx`. `export_collections()` writes one FBX per `Coll_*` for a contact-sheet `.blend` (the forty nomad buildings, the eighteen shade sails), each moved onto the world origin by the collection's `instance_offset`. Exports never write back to the `.blend`.
- **Walkable interiors** additionally get a baked convex decomposition from [`_collisionlib.py`](Assets/Game/Art/Models/_Source~/_collisionlib.py) — Unity refuses a concave MeshCollider on a Rigidbody. Kept for the same reason as `_exportlib`: it is part of getting a model *in*, not of building one.
- **Import** into Unity is automatic. [`MeshReadablePostprocessor`](Assets/Game/Editor/AssetPipeline/MeshReadablePostprocessor.cs) forces Read/Write on every mesh (runtime NavMesh baking); [`RootMotionCurveStripper`](Assets/Game/Editor/AssetPipeline/RootMotionCurveStripper.cs) deletes root-bound curves from imported clips.
- **Prefabs are authored assets.** They are edited in the Unity Inspector and committed. Nothing regenerates them.
- **Anything worn is modelled at the wearer's true size**, measured off the skinned character rather than guessed. An earlier gauntlet built to a remembered forearm radius vanished inside the suit sleeve.

## Layout

| Directory | Contains | Visible to Unity? |
|---|---|---|
| [`Assets/Game/Art/Models/_Source~/`](Assets/Game/Art/Models/_Source~) | `.blend` masters, `palette.blend`, `_exportlib.py`, `_collisionlib.py` | **No** — trailing `~`; no `.meta` files, no Blender install needed to open the project |
| [`_Source~/components/{structural,props,mechanical,organic,apparel,nomad_settlement}/`](Assets/Game/Art/Models/_Source~/components) | reusable component `.blend`s, variations as `Coll_*` collections | No |
| [`_Source~/models/{buildings,characters,creatures,gear,props,vehicles}/`](Assets/Game/Art/Models/_Source~/models) | assembled model `.blend`s | No |
| [`Assets/Game/Art/Models/_backups~/`](Assets/Game/Art/Models/_backups~) | pre-surgery snapshots (`vrescal_before_legs.blend`, …) | No |
| [`Assets/Game/Art/Models/<Category>/`](Assets/Game/Art/Models) | exported/imported `.fbx` + `.meta` | Yes |
| [`Assets/Game/Art/Materials/`](Assets/Game/Art/Materials) | `.mat` in domain folders | Yes |
| [`Assets/Game/Art/Animations/{Player,Creatures,UI}/`](Assets/Game/Art/Animations) | mocap FBX, `.anim`, `.controller`, `UpperBody.mask` | Yes |
| [`Assets/Game/Art/{Shaders,Textures,Sprites,VisualEffects,Brushes}/`](Assets/Game/Art) | shader graphs/HLSL, textures, icons, VFX | Yes |
| [`Assets/ThirdParty/`](Assets/ThirdParty) | bought/free packs and **borrowed art with a licence to keep** (`RedPlanetRampage/`: the Clanker body + clips, BSD-4-Clause, terms in `THIRD_PARTY_NOTICES.md` at the repo root) | Yes |

## Model library

FBX live under `Assets/Game/Art/Models/`, split by category: `Environment`, `Vehicles`, `Items`, `Creatures`, `Weapons`, `Props`, `Characters`. Older/imported assets are `camelCase` or `PascalCase`; assets exported from this library are `snake_case`.

## Materials

- **One shared palette** in `palette.blend`: materials named `Mat_<Category>_<Descriptor>` — `Mat_Metal_Steel_Worn`, `Mat_Emissive_Portal_Blue` — across Emissive, Fabric, Foliage, Glass, Hide, Metal, Neutral, Paint, Plastic and Wood.
- **How a mesh gets its material:** face material indices are stamped in Blender from linked palette slots → `_exportlib` calls `make_local()` (a *linked* material does not survive into the FBX; without this the meshes arrive untextured) → Unity imports them as **sub-assets of the FBX** (`materialLocation: 1`, `materialName: 0`, `materialSearch: 1` on every model FBX meta), regenerated on every reimport.
- Because they are sub-assets, per-material flags cannot be edited in place. [`DoubleSidedMaterials.Apply()`](Assets/Game/Editor/Support/DoubleSidedMaterials.cs) copies each to `Assets/Game/Art/Materials/Vehicles/<name> (DoubleSided).mat` and rewires the renderers. Vehicle hulls are modelled as surfaces, so back-face culling makes cabins see-through.
- Hand-authored `.mat` assets (terrain, portal, surfaces, VFX) live in the domain folders under [`Materials/`](Assets/Game/Art/Materials) and are unrelated to the palette.

## Rigs & animation

| | Setting |
|---|---|
| Humanoid | the astronauts, the nomads and the five drifters (`animationType: 3`). `sky_soldier.fbx` is NOT among them |
| Generic | almost every other model FBX (`animationType: 2`) — creatures, vehicles, rigid-part rigs |
| Avatar source | [`Characters/Astronaut/AstronautArmature.fbx`](Assets/Game/Art/Models/Characters/Astronaut/AstronautArmature.fbx) is `avatarSetup: 1` (Create From This Model); every clip in [`Animations/Player/`](Assets/Game/Art/Animations/Player) is `avatarSetup: 2` (Copy From Other Avatar) pointing at it |

- Player clips are retargeted mocap FBX driving [`AstronautArmature.controller`](Assets/Game/Art/Animations/Player); `UpperBody.mask` gates hold-pose layers.
- Creature clips are authored in Blender and land as `.anim` next to a `.controller` in [`Animations/Creatures/`](Assets/Game/Art/Animations/Creatures) (Vrescal, Golem, DuneRat, Appa).
- Export keeps the rig only when Unity drives bones: `export(..., keep_armature=True)`. `add_leaf_bones=False` is always set — Blender's `<bone>_end` tips otherwise appear as real transforms and break bone-walking code.
- Rigid-part rigs (meshes parented to bones, not skinned) are the ones the root-motion stripper exists for; skinned rigs never get a root curve.

## Flows

1. Open the model's `.blend` in Blender and edit it there. **It is the only copy of the geometry.**
2. Re-export with `_exportlib.export(SRC, unity_path("Category", "model.fbx"), keep_armature=…)`.
3. Let Unity import; check the material, rig and scale gotchas below.
4. Update the prefab by hand in the Inspector if the mesh, bone names or submesh order changed.

## Multiplayer

N/A — art assets carry no authority split. The one crossing point: a runtime-spawned prefab must be registered in the network prefab list (see [Multiplayer.md](Multiplayer.md)).

## Persistence

N/A for the art assets themselves. A spawnable prefab must carry a prefab id, or the entity vanishes on load — see [Persistence.md](Persistence.md).

## Gotchas

- **The `.blend` is the only copy.** There are no generators any more, so a `.blend` that is damaged or overwritten cannot be rebuilt from a script. Snapshot into [`_backups~/`](Assets/Game/Art/Models/_backups~) before surgery. Notably hand-built and irreplaceable: `models/vehicles/ship_lander_blockout.blend` (the user's interior), `models/vehicles/sky_city.blend`, the `nomad.blend` family, `models/creatures/vrescal.blend`, `models/creatures/appa.blend`.
- **The export convention, scale and axes, is fixed for the whole library.** Axis conversion is `-Z` forward / `Y` up: Blender's −Y forward lands on Unity's +Z, and changing it silently rotates new assets relative to every existing one. Scale is `FBX_SCALE_NONE`, 1 Blender unit = 1 m, with `globalScale: 1` / `useFileScale: 1` on every model FBX meta — which still means **Blender FBX import at `lossyScale = 100`** on every transform (FBX centimetre convention): mesh data 100× small under transforms 100× large. It cancels for the model, but anything sized *against* a socket must divide by `socket.lossyScale` or it comes out 100× too big. **The one deliberate exception is a skinned humanoid character**, exported with `_exportlib.export(keep_armature=True, scale_all=True)` (`FBX_SCALE_ALL`): with `FBX_SCALE_NONE` every BONE gets a scale of 100 in Unity, which anything parented to a hand then inherits. The drifters ship this way. Not every skinned rig does — Appa and the other creatures ship `FBX_SCALE_NONE` and size their bone-parented colliders against that 100 — so **read an existing FBX's header before re-exporting it**: `UnitScaleFactor` 100 means scale-all, 1 means none.
- **An `_exportlib` FBX is already axis-converted.** Anything that applies a further −90° X rotation on import doubles it.
- **`_exportlib.export(fix_inverted=True)` is not safe on a contact-sheet `.blend`.** `models/buildings/nomad_settlement.blend` holds forty finished buildings in one file and shares a single mesh datablock between as many as 75 objects. With `fix_inverted` on, three of the forty shipped their mirrored kit parts up to **137 m** from the model: the `.blend` measures correct vertex by vertex, and only the FBX is wrong, so nothing in Blender reports it. With it off, all forty match the source bounds to under 10 mm. Repair inverted winding on the Unity side instead, per renderer, by the sign of `Transform.localToWorldMatrix.determinant`.
- **A negative scale renders inside-out in Unity and looks right in Blender.** Dragging a scale gizmo past zero leaves the object on a negative-determinant transform. Blender's viewport respects the flip; the FBX carries the negative scale straight through and Unity shows the back faces. Nothing errors. Detect it by the **determinant of `matrix_world.to_3x3()`**, confirm with the mesh's world-space signed volume going negative, and never by eye.
- **A mirrored *skinned* mesh arrives inside-out; a mirrored *rigid* one does not.** For a rigid prop the negative scale survives onto the MeshRenderer and Unity reverses the culling mode, cancelling the flip exactly. **A SkinnedMeshRenderer does not deform through its own transform**, so nothing reverses the culling and the mesh lights by a normal pointing into itself. Appa has 16 mirrored meshes and exactly 3 are also skinned — the legs, the mane and the shoulder fur — and those 3 were the ones that looked wrong. **Re-importing the FBX into Blender to check also looks perfectly correct**, which is what makes this so hard to see. Fix at the source: bake the transform into the vertices, then recalculate normals — in that order, since baking a negative scale reverses winding.
- **Inconsistent face winding is invisible in Blender and lit wrong in Unity.** Blender draws both sides of a face; Unity lights the side the normal points at, so those patches come out dark while the rest of the same mesh is fine. Appa's mane and shoulder fur each had ~5% of faces wound the wrong way and the ears exactly 50%, which read to the author as "the UVs seem broken" — the UVs were clean. Recalculate normals outward; reversing whole meshes does not fix it, because a blanket reverse preserves the disagreement.
- **Do not make a closed mesh double-sided; on hair it is actively destructive.** Reach for [`DoubleSidedMaterials.Apply`](Assets/Game/Editor/Support/DoubleSidedMaterials.cs) only for geometry modelled as open sheets — a ship hull, a sail. Check with `boundary edges == 0` in Blender rather than by eye: Appa's mane, shoulder fur, brow tuft and ears all *look* like sheets and are all closed volumes. URP's Lit, unlike Blender's viewport, does **not** flip a back face's shading normal, so those interiors light black.
- **A dark "hole" with clean topology is a shading problem, and cavity/AO shading is what hides that from you.** Appa's front-leg armpits were reported as holes twice; the mesh measures clean on every test that could make one. What is there is a deep hooked recess that takes no light. Diagnose with **flat** shading (`light='FLAT'`, `show_cavity=False`) and one colour per object — Workbench's cavity and URP's AO both darken every crease, so a fold and a puncture look identical.
- **`avatar.isHuman == false` fails silently.** A humanoid FBX re-exported with a changed hierarchy stops animating with a clean console. Check `isHuman` after any character re-export. Copy-From-Other-Avatar cannot be configured on an armature-only FBX — that one must be Create From This Model. **An unchanged hierarchy is not enough: a human bone with NO WEIGHTED VERTICES is a bone Unity will not map**, reported as `Rig Error: Required human bone 'X' not found` while the transform is plainly there. The cause seen on the crumpy: **a Blender mirror/symmetrize copies vertex groups but only renames the side pairs it recognises, and it recognises the `.L`/`.R` SUFFIX, not the `Left…`/`Right…` PREFIX a Unity-Humanoid skeleton uses** — so the mirrored half arrived carrying the *right* side's groups and 21 left-side groups were empty. **The tell is an exact doubling**: `RightLowerLeg` 1700 → 3400, `RightHand` 768 → 1536, centre bones unchanged. Count weighted vertices per group (a group can exist and be empty, and every vertex still sums to 1.0, so neither the group count nor a loose-vertex check catches it).
- **A failed avatar empties the take list, and the error blames the exporter.** Copying the astronaut's avatar onto a Blender-authored gesture fails with `Copied Avatar Rig Configuration mis-match. Transform 'Armature' not found in HumanDescription` — that avatar is rooted at `AstronautArmature(Clone)` and Blender always names its armature object `Armature`. Unity then abandons the import, so `defaultClipAnimations` comes back **empty** and the next check reports "carries no animation take at all". When a take list comes back empty, check `Avatar.isValid` / `isHuman` **before** blaming the export.
- **Blender's FBX exporter bakes the SCENE range and names the take after the scene, unless told otherwise.** A 60-frame action shipped as a **251-frame take called `Scene`** — Blender's default 1..250 range. `bake_anim_use_all_actions=True` gives one take per action, named `<object>|<action>`; `scene.frame_start/frame_end` bounds it. Downstream, **never guess a take name** — read `ModelImporter.defaultClipAnimations` and use its `takeName` and frame bounds. A `clipAnimations` entry naming a take that does not exist produces **no clip and no error**.
- **`SaveAndReimport()` does not guarantee the new clips are readable in the same run.** Configuring `ModelImporter.clipAnimations`, reimporting, and reading clips back with `AssetDatabase.LoadAllAssetsAtPath` can still return the **previous** clip set on the first pass after new takes are added. On Appa that produced a controller holding only Idle and Walk — no Roar, Ram, Hurt or Death, no error anywhere. Check the clip set before building anything on it.
- **Never ship bone-heat weights unfiltered.** `ARMATURE_AUTO` binds by proximity, and proximity lies wherever geometry hangs near a bone it has nothing to do with. On Appa it bound **24.4% of the shoulder fur and 7.4% of the mane to `femur_*`**, so every stride dragged the hair into floating shards. Invisible in Blender, because the rest pose never moves. Watch for vertices left with **zero** total weight after filtering: those collapse to the origin. **Because proximity lies, a skin-stretch metric scores a BROKEN rig better than a fixed one — do not gate on it.** Measure **where the joints sit relative to the surface** instead.
- **Resculpting a rigged body does not move its skeleton, and nothing anywhere says so.** The three sculpt-base drifters are one mesh pushed into three shapes — same 24830 vertices in the same order — but only the human's 52 bones were built to fit. The crumpy wore the human's skeleton unchanged: **median joint depth −0.085 m, i.e. most of its joints were OUTSIDE its own body**. It still imported clean, still reported a valid Humanoid avatar, still stood correctly in the rest pose — the armature modifier is identity there — and only tore itself apart once a clip played. **The rest pose proves nothing about a rig; pose it before believing it.** Gary (`drifters/gary.blend`) is the fourth, and he is remeshed rather than pushed — 27218 vertices, every one re-weighted — while still wearing the human's skeleton unmoved: median joint depth +7.6 mm, the same as the human, but both `UpperArm` joints sit 5 cm outside his narrower shoulders. A hard test pose showed no tearing; refit the shoulders if they deform oddly in play. **`sculpt_base/gary.blend` is not Gary** — it is a byte-identical copy of the human base; the real one is `drifters/gary.blend`. **Raxy** (`drifters/raxy.blend`, resculpted from a copy of Gary: 27713 vertices, ears, three-toed feet, a thumb and three fingers) is the first to get its OWN skeleton, fitted to the mesh rather than inherited. Median joint depth is +11 mm and every joint is inside the body. Its 66 bones are the Humanoid set without the Little fingers, three bones per ear (`<Side>EarBase/Mid/Tip`, a flap hinge), one non-deforming bone per eye with the sphere bone-parented to it, and two per toe (`<Side>Toe{Inner,Middle,Outer}{1,2}`) hanging off the Humanoid `Toes` bone. The auto-mapper maps exactly Gary's 22 body bones plus 24 finger bones, and ignores the ears and toe chains. The copy arrived with the mirror trap below: 21 empty left-side groups.
- **Place joints from the mesh, not by eye: geodesic level sets give every branch's centreline.** Take the geodesic distance from the crown over the mesh edges. At each level, the edge crossings joined through shared faces are loops. A loop's centroid lies on the medial line of whatever it rings, and loops at consecutive levels link wherever the band between them connects. The result is the body's branching tree: ears split off at the head, arms at the shoulders, legs at the crotch, digits at the hand and foot. Each fork is a split point and each loop radius a thickness, which is how Raxy's knee (the knobbly bulge, radius 64 mm, at z 0.40) was told apart from the narrowest point above it. Bin whole VERTICES by distance instead and a long edge breaks every ring into fragments: 2147 pieces instead of 651 loops on Raxy, whose flat ears carry 13 cm edges.
- **Bone heat lets a flap's root bone reach round the skull.** Raxy's `EarBase` bones took 1108 vertices by `ARMATURE_AUTO`, some on the far side of the head, so flapping an ear would have dragged the skull with it. The flap was confined by geodesic distance from its outer tip: full ear weight within 0.34 m, fading to none by 0.40 m (where the coarse flap meets the dense skull), and everything beyond is `Head`. That left ~220/160/110 vertices on the three bones. Proximity lies here for the reason it lies on Appa's fur.
- **The `.blend` on disk can be behind the open Blender session.** Raxy's copy was repainted blue in the GUI while the saved file still packed Gary's texture byte for byte, so the first export shipped Gary's skin. Texture paint lives in the image buffer until the image itself is saved or re-packed, and no export can see it. Before exporting a character, compare the packed image's md5 against its siblings' `*_BaseColor.png`.
- **`optimizeGameObjects` must stay off for a rigid-part rig.** Meshes parented to bones hang off exactly the transforms that optimising strips, so turning it on deletes the model in pieces with no error. Appa is 21 bone-parented props against 6 skinned meshes; the Vrescal is the same shape.
- **A packed image exports as no texture at all.** An image packed into the `.blend` has `filepath == ""`, and `embed_textures=False` writes a path — so the FBX ships with no texture reference and every material arrives flat-coloured. Setting `filepath` is *not* enough: while `packed_file` is set the exporter still falls back to the embedded bytes and logs `Image "" not available. Keeping packed image`. It has to be genuinely unpacked — and not with `unpack(method='USE_ORIGINAL')`, which writes `<image name>.png` BESIDE THE .blend, inside `_Source~`, while the FBX goes on referencing a `<model>.fbm/` copy. Write `img.packed_file.data` to the destination yourself (it is the original PNG, byte for byte), then `img.unpack(method='REMOVE')` and point `filepath_raw` at the file.
- **The FBX exporter ignores which Material Output is active.** A material with two shader chains renders from whichever output is marked active, but the exporter takes the *first* Principled BSDF it finds. Five of Appa's materials exported untextured that way while a sixth, with a single chain, came through fine. Delete the dead chain before exporting.
- **A model that appends kit parts silently stops tracking the palette.** `bpy.data.materials` is keyed on name **and library**, so a local and a linked material can coexist under the same name with **no `.001` on either** — and `append` makes everything local. `sky_city` shipped a local `Mat_Metal_Steel_Worn` beside the linked one, so editing `palette.blend` no longer changed the model. Linking the palette *before* appending does not fix it; the names still collide. Check with `[m.name for m in bpy.data.materials if m.library is None]`, which should come back empty. Some components also **define their materials inline** rather than linking — `Mat_Glass_Amber_Warm` is in no palette entry, so any model using the camp lantern ships one local material it cannot avoid.
- **Stale renderer material overrides.** After an FBX reimport that changes submesh count/order, prefab renderers keep the old `sharedMaterials` array and render the wrong material with no error. Re-assign them on the prefab.
- **Materials freeze the shader defaults they were born with** — a `.mat` created against an older shader keeps those values when the shader gains new properties.
- **The nomad settlement kit is authored at about half human scale.** Its reference tower is 4.9 m on a 1.219 m storey and its doors measure 0.53–0.96 m — an astronaut is 2.00 m (the player capsule; the skinned bounds read 2.86 m because the edit-mode pose has the arms up, so never measure a character off that). Anything built from this kit has to be scaled in Unity against something a player reads scale from — roughly 2.3–4.3×.
- **`components/nomad_settlement/tents.blend` ships single-sided canopies.** Back-face culled, a sail is invisible from underneath, which is the side a player stands on. Fixed in Unity by rendering the cloth double-sided.
- **A borrowed rig's loose `.anim` clips bind by transform path, so the Animator must sit on the FBX instance root.** The Clanker's nine clips (`Assets/ThirdParty/RedPlanetRampage/Animation/rig.001_*.anim`) address `rig.001/root/DEF-…`; put the Animator on the prefab root above the model and every curve binds to nothing, silently. **A borrowed clip also carries the donor game's AnimationEvents** — six of those called `PlayWalkSound`, a method that exists nowhere here, so every Clanker logged `has no receiver!` on every stride. The events were stripped (`m_Events: []`). Grep a newly vendored clip for `functionName:` before wiring it into a controller.
- **The whole library is written by Blender 5** (`BLENDER17` file header) and none of it opens in Blender 4.2 — `palette.blend`, `components/props/supply_crate.blend` and `models/creatures/dune_rat.blend` all report *"not a blend file"* there. A portable 5.2.1 lives at `%LOCALAPPDATA%/Programs/Blender5/blender-5.2.1-windows-x64/blender.exe`.
- **The four sand nomads are authored OUTSIDE the repo**, in `~/Documents/Blender/sand_nogs.blend`, by the user's choice. **Never bake a pose into these meshes** — a retired step posed each body onto the reference skeleton and wrote the deformed vertices back, moving every carefully placed face on all four characters. Also: **a mirrored object (negative scale) renders correctly in Blender and inside out in Unity**; `Recalculate Outside` reports nothing, because in mesh space the normals are fine.
- **`_Source~` and `_backups~` are invisible to Unity.** No `.meta`, no GUIDs, nothing there can be referenced from a scene or prefab. Conversely, an export written to the pre-restructure `Assets/Models/` path is an orphan nothing imports — always go through `_exportlib.unity_path()`.
- **A model whose primary structure is MERGED meshes cannot get its collision from a per-renderer rule.** `sky_city` merges keel, cage, decks, prow, stern gear and gantry into one mesh each, spanning the whole 125 m ship, so a box over the decks mesh is a 22 × 112 m slab hanging in mid-air and a convex hull is worse. Its colliders come from convex islands authored in Blender (`COL_SkyCity_####`). **Convert the axes once and write both values down**: Blender `(bx, by, bz)` arrives at Unity `(-bx, bz, -by)`. Verify by raycast, not by eye.
- **Ship collision islands as separate objects, not one mesh to split in Unity.** The FBX import splits vertices along hard edges, so a mesh's islands have to be re-found by welding positions — and two boxes that merely share a corner weld into one island whose convex hull fills the space between them. **Route-check with the player's WORLD size**: the player is 3.0 m tall (2 m capsule, transform scaled 1.5 in Y).

## Extending

1. Adding a variant of an existing thing → a new `Coll_*` collection in that component's existing `.blend`, not a new file.
2. Needing a colour → add it to `palette.blend` and link it; do not define a material locally in a model.
3. New category → a folder under `components/` or `models/` only when nothing existing is a plausible home; the Unity-side sibling under `Assets/Game/Art/Models/` must match.
4. Changing shared export behaviour → edit [`_exportlib.py`](Assets/Game/Art/Models/_Source~/_exportlib.py), never fork the flags into a per-model script.
