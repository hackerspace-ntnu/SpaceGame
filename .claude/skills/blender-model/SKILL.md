---
name: blender-model
description: Create production-ready Blender models as .blend files in a shared model library, decomposed into reusable components with multiple distinct variations and a shared material palette. Use this skill whenever the user asks to create, build, model, or add any 3D asset, prop, character, vehicle, building, or environment piece — phrases like "create a blender model for a cargo crate", "model a watchtower", "I need a rock formation for the level", or "add a variant of the existing door". Use it especially for requests implying several assets at once — "a camp with tents and crates", "props for a market scene", "a set of barrels" — since those need families of varied models rather than repeated copies. Also use it when the user asks to extend, vary, or reuse something already in the model library, or asks what models already exist. Trigger even when the user does not say the word "Blender" — if the deliverable is a 3D asset in this repository, this skill applies.
---

# Blender Model

> **Design check:** when an asset must read at a glance, sell an animation or fit a level's
> language, consult `CONTENT`, `LEVEL`, `ANIM` and `PERF` in
> `docs/game-development-constitution/INDEX.md` and cite the IDs.

Build 3D assets as `.blend` files inside a repository-wide model library. The point of the library is that a model is never a one-off: it is assembled from small, named, reusable components that share a single material palette, so the hundredth model costs less work than the tenth and the whole set stays visually coherent.

Two things distinguish this workflow from just opening Blender and extruding a cube:

- **Decomposition.** Nothing is built as a single monolithic mesh. Every model is broken into the smallest components that could plausibly be reused, and those components live independently.
- **The library is authoritative and it is shared.** Before building anything, you find out what already exists. Reuse always beats rebuilding — a second nearly-identical crate mesh is a defect, not a deliverable.

**Work autonomously.** There is exactly one place where you stop and wait: the single round of questions in Phase 2. Everything after that — decomposition, component builds, assembly, verification — runs to completion without check-ins, approval requests, or plans presented for review. Decisions get recorded in the build record and reported at the end, not proposed in advance.

## The `.blend` file is always the source of truth

This rule governs everything below, so internalize it first.

Generator scripts are saved next to the files they created, but they are **historical record, not source of truth**. Most files get hand-edited in Blender after generation, and those edits exist only in the `.blend`. A script that once produced a file will not reproduce its current state.

Therefore: **never re-run a generator script over an existing `.blend` file.** Doing so destroys the user's work irrecoverably. If a file exists, inspect it and modify it in place — never regenerate it.

## Protecting hand-made work

The user models by hand. Their geometry is not yours to rearrange.

**Default posture is additive.** You may add new objects, new collections, new variations, and new materials to any file. You may not move, reshape, rename, or delete geometry that was already there. If the user placed ten cubes in a file, those ten cubes are still there, unchanged, when you are done — even if they look wrong to you, even if they are misaligned, even if removing them would make your addition cleaner.

**The exception is an explicit request.** If the user says "make the roof taller" or "delete the second pillar," do it. Their instruction authorizes the change, and the authorization covers what they asked for, not what you think should follow from it.

**Conflicts resolve toward existing geometry, automatically.** When a new part collides with something already in the file, adapt the new part — do not move the old one. The existing geometry is the fixed constraint you build around. This is a rule precisely so it does not need to become a question: fitting the door to the wall is always the correct default, and it lets you keep working.

Ask only when the user's own instruction is ambiguous about what to destroy — "replace the pillar" when there are three of them. Ambiguity in an explicit destructive request is worth one message; everything else resolves by the rule above.

Before any destructive operation on a pre-existing file, state in your final summary what you changed and why the request authorized it.

## Scripts create, MCP edits

Two mechanisms, with different strengths:

**Scripting (`blender --background --python`)** — use for creating new geometry from nothing. Scripts are precise, repeatable within a single run, reviewable in a diff, and fast for parametric work like arrays, bevels, and procedurally placed detail. Every new component and new model starts as a script.

**Blender MCP** — use for editing existing files. Request the connection when you need it. MCP lets you inspect the live scene, verify what you have done, and make surgical adjustments to a file that already contains work you must not disturb. This is exactly the situation where blind scripting is dangerous.

The dividing line is whether the file already exists. New file → script. Existing file → MCP. If MCP is unavailable and the edit is unavoidable, say so explicitly and get the user's agreement before scripting against an existing file.

Save each generation script next to its output: `components/props/crate.blend` is accompanied by `components/props/crate.py`. Write scripts to be additive and defensive — create uniquely named objects, never call `read_factory_settings`, never delete by pattern.

See `references/scripting.md` for the bpy patterns this workflow depends on.

## Library layout

Find the source library at `Assets/Models/_Source~/`. If it does not exist, create it.

The trailing `~` is load-bearing: Unity's importer skips any folder whose name
ends in it, so the `.blend` masters and build scripts live inside `Assets/`
without being imported as game assets. The exported `.fbx` files that the game
actually references live in the sibling category folders under `Assets/Models/`.

```
Assets/Models/_Source~/
├── PALETTE.md            # documented material list — read before every build
├── palette.blend         # the actual material datablocks, linked from everywhere
├── LIBRARY.md            # generated index of everything below
├── components/           # reusable parts, one .blend per component
│   ├── structural/
│   ├── props/
│   ├── mechanical/
│   └── organic/
└── models/               # full assembled models
    ├── buildings/
    │   ├── industrial/
    │   └── residential/
    ├── vehicles/
    └── props/
```

**Components** are parts shared across models — a hinge, a window frame, a bolt pattern, a crate panel. One component gets one `.blend` file, but that file may hold **multiple variations** of the same component in separate collections (`Hinge_Small`, `Hinge_Large`, `Hinge_Broken`). Variations of one thing belong together; different things belong in different files.

**Models** are assembled deliverables. They are organized in categories and subcategories, nested as deeply as the content warrants. Place a new model in an existing category if one fits; only create a category when nothing existing is a reasonable home.

## Workflow

`$SKILL_DIR` below is this skill's own directory — wherever it is installed, e.g.
`.claude/skills/blender-model`. Set it once per session:

```bash
SKILL_DIR=.claude/skills/blender-model
```

### Phase 1 — Survey before you ask

Run the library index first, so your questions are informed:

```bash
python "$SKILL_DIR"/scripts/index_library.py --models-dir models
```

This opens every `.blend` headless and writes `library_index.json` plus a human-readable `LIBRARY.md` listing components, variations, dimensions, and materials in use. Read the index and `PALETTE.md` before speaking to the user.

If the palette does not exist yet, create it now — see the Materials section.

### Phase 2 — Interrogate, once

Ask focused questions before building anything — but ask them **all in one round**, then run to completion without further check-ins. Stopping repeatedly to confirm is worse than a wrong guess, because a wrong guess is visible in the file and can be fixed in one instruction.

Good questions do two jobs at once: they pin down what the user wants, and they surface reuse opportunities you found in the index.

Cover:

- **Purpose and context** — what is this for, what scene or project does it sit in, what reads it as believable?
- **Style and fidelity** — does it match an existing model's language? How detailed? Hero asset seen up close, or background filler?
- **Scale and footprint** — real-world dimensions. This determines whether existing components can be reused as-is.
- **Reuse, explicitly** — "The library already has `crate_panel` with three variations and a `latch_heavy` component. Should the new container use those, or does it need its own?" Always name specific candidates from the index rather than asking the generic "can we reuse anything?"
- **Articulation** — does anything move, open, rotate, or deform? This decides the armature.
- **Materials** — with an empty or small palette, this is where the project's look actually gets decided. Ask what it is made of and what condition it is in, then map that onto existing palette entries and name the gaps: "the palette has `Mat_Wood_Pine_Light`, but nothing for the metal banding — should that be clean steel or corroded?"
- **Multiplicity** — how many of this appear in a scene at once, and how close together? This decides how many variations to build, and it is more useful than asking "do you want variations?" — the answer to that is essentially always yes.

Cover everything in one message. Where a detail is low-stakes and reversible — bevel width, exact bolt count, which of two near-identical greys — decide it yourself rather than spending a question on it. Reserve questions for things that are expensive to change later: real-world scale, what the thing is made of, whether it articulates, and whether it should reuse a specific existing component.

Once the answers are in, build the whole thing without stopping again.

### Phase 3 — Decide the decomposition and record it

Work the decomposition out and commit to it. **Do not present it for approval** — decide, write it down, and start building in the same turn.

Record it in `<model_name>_BUILD.md` next to the model file, stating:

1. Which existing components are reused, by path.
2. Which new components are created, and why each is separate rather than part of another.
3. How the model assembles from them.
4. Which palette materials each part uses, and any material added to the palette for this build.

This is a build record, not a proposal. It exists so that a later reader — including you, months on — can see why the model is cut the way it is without reverse-engineering it from geometry. Write it as you go and finish it when the model is done.

**Decompose to the smallest sensible unit.** The test is reuse potential: if a piece could plausibly appear on a different model, it is its own component. A door is not one component — it is a panel, a frame, a hinge, and a handle. Err toward more components — parts are never merged anyway (see the geometry rules below), so a finer cut costs nothing later, while splitting a fused mesh is surgery.

## Variation

Anything that appears more than once must not look the same twice. A camp with four identical tents and six identical crates does not read as a camp — it reads as a copy-paste, and the eye catches it instantly even when every individual asset is good.

So when a request implies plurality — a camp, a village, a scrapyard, a shelf of supplies — **build a family, not an object**. Three to five distinct variations of each repeated element, minimum.

**Vary what the eye actually notices**, in roughly this order of impact:

1. **Silhouette and proportion** — a ridge tent, a lean-to, a dome, a collapsed one. This is what reads at distance and it is what most variation attempts skip.
2. **Structure** — different panel counts, different frame arrangements, an open lid versus a closed one.
3. **Condition** — patched, torn, sun-bleached, rusted, water-stained.
4. **Material and color** — the weakest axis on its own. Four tents that differ only in canvas color are still four identical tents.

Uniform scale jitter is not variation. A crate at 90% is the same crate, and a scene full of subtly mismatched sizes reads as sloppy rather than varied.

Variations of one thing live as separate collections in that thing's single `.blend` — `Coll_Tent_Ridge`, `Coll_Tent_LeanTo`, `Coll_Tent_Collapsed`. When assembling, never place the same variation adjacent to itself if another is available.

## Build more than the request needs

Overshoot deliberately. If the scene needs three crates, build five variations. If it needs one tent, build three. The marginal cost of another variation is small once the component's structure exists and its materials are chosen — and the next request that needs a crate then costs nothing at all.

This is what makes the library compound instead of accumulating. Extra variations are the highest-value work in this whole workflow, because they are the only work that pays off before it is asked for.

Two constraints on that, though, or overproduction turns into landfill:

- **Extras must be meaningfully different.** Same standard as above: distinct silhouette, structure, or condition. A fifth crate that differs from the fourth only in bevel width is not an asset, it is noise in the index — the same failure as a palette full of indistinguishable greys.
- **Extras go in the component file, not in new files.** Overproduce *variations within a component*, never near-duplicate component files. One `crate.blend` holding six variations stays discoverable; six crate files do not.

Note in the build record which variations the request actually needed and which were built ahead.

### Phase 4 — Build components first

Never build the model before its components exist. Each component:

- gets its own `.blend` in the right category folder,
- is modeled in focused detail — this is the level where quality is decided, so give each one real attention rather than roughing it in and hoping assembly hides the flaws,
- has every object explicitly named (see conventions),
- has its origin at its logical connection point, not at the world origin,
- uses only palette materials,
- has all transforms applied.

Build one component at a time and verify each before starting the next — a flawed component propagates into every model that uses it. Verify by inspecting the file (see `references/scripting.md`), not by asking. If a component comes out wrong, fix it and carry on; the user sees the result at the end, not each step.

### Phase 5 — Assemble the model

Link or append the components into the model file, position them, and add whatever geometry is genuinely unique to this model. Add the armature if anything articulates.

Where an element repeats, distribute the variations rather than reaching for the same one each time, and vary rotation and placement so repeated pieces do not sit in visible lockstep.

## Geometry rules

Three hard rules that apply at every phase — component builds, assembly, and any later edit.

### Never merge — every part stays its own object

Parts are **never** joined, merged, or boolean-unioned into a single mesh. Not during the build, not at assembly, not as a final "cleanup". Every logical part — a handle, a hinge leaf, a bolt, a strut, a panel — remains a separate, named object for its whole life, so anyone can later select, move, reshape, or delete it without performing surgery on a combined mesh.

- No `bpy.ops.object.join()`. No `bmesh` operation that folds two parts into one datablock. No applied boolean union between parts.
- Booleans are permitted only *within* one part's own construction (cutting a hole in a single panel), never to fuse two parts together.
- Rigidity comes from parenting — to the body object, an empty, or a bone — never from merging.
- "It's just two bolts" is not an exception. If it deserves a name, it stays an object.

Merging is destructive and one-way; the entire point of the library is that everything stays modifiable.

### No z-fighting

Separate objects mean surfaces sit against each other, and two coplanar overlapping faces flicker unpredictably in the viewport and in-game. Never let two faces from different objects occupy the same plane.

- Where parts touch, either **embed** one slightly into the other (overlap by a few millimetres so the hidden face is inside, not coincident) or **offset** the exposed face by at least 1–2 mm.
- Panels, plates, and decals sitting on a surface get a visible thickness or a deliberate stand-off — never a zero-distance face-on-face placement.
- Verify it: for every pair of touching parts, confirm from positions and dimensions that their face planes either differ by ≥ 0.001 m or genuinely interpenetrate. When in doubt, render or capture the viewport at a grazing angle and look.

### Rotations are verified, never trusted

A wrong rotation is the most common silent build error: an axis flipped, a sign inverted, degrees passed where radians were expected, a part facing +Y in this library's −Y-forward convention. **Always double-check every rotation** — assume yours is wrong until the data proves otherwise.

- `bpy` rotations are **radians**. Write `math.radians(45)`, never a bare `45`.
- After placing or rotating any part, verify from the file, not from intention: dump the object's world matrix or a known landmark vertex and confirm the axes point where intended (see `references/scripting.md` for the pattern). For anything visually asymmetric — a spout, a door, a seat, text — also capture a render or viewport image and look at it.
- Check the sign and the axis separately. A part rotated −90° about X looks superficially plausible next to one rotated +90°; only measurement tells them apart.
- Never stack a second rotation to "fix" one that looks wrong without first establishing what the current orientation actually is — compounding guesses is how parts end up mirrored.

### Phase 6 — Verify

Production-ready means someone else could open this file and use it immediately without cleanup. Check every item:

- [ ] Every object, mesh, collection, material, and bone has a deliberate name — no `Cube.003`, no `Material.001`
- [ ] All transforms applied; scale is uniform 1.0 unless deformation is intentional
- [ ] Normals consistent and outward-facing
- [ ] No loose geometry, no interior faces, no duplicate vertices
- [ ] Nothing merged: every logical part is still its own named object — no joins, no boolean unions between parts
- [ ] No z-fighting: no coplanar overlapping faces between objects — touching surfaces are offset or embedded
- [ ] Every rotation verified against the data (world matrix / landmark check) and, for asymmetric parts, a visual capture — not assumed correct
- [ ] Origins at logical connection points
- [ ] Every material comes from the palette
- [ ] Armature present wherever something articulates, with named bones and sensible rest pose
- [ ] Repeated elements have at least three genuinely distinct variations, differing in silhouette or structure rather than only color
- [ ] Component variations in clearly named separate collections
- [ ] Generation script saved alongside the `.blend`
- [ ] Nothing pre-existing was modified without authorization
- [ ] `LIBRARY.md` regenerated

Then re-run the index script so the library stays accurate.

### Phase 7 — Report

The user did not see a plan, so the summary at the end is the first full account they get of what you decided. Make it worth reading and keep it short:

- what was created, by path
- what was reused from the library
- which variations the request needed and which were built ahead for later use
- how the model was decomposed, and any non-obvious reason why
- any material added to the palette, and why nothing existing served
- anything pre-existing that was modified, and what authorized it
- anything you decided that they might want decided differently

That last item matters most. Autonomy means they find out afterwards, so name the judgement calls plainly rather than burying them — a decomposition choice or a scale assumption is cheap to reverse in the next instruction and expensive to discover in a month.

## Materials and the palette

The whole model set draws from one documented palette that **starts empty and grows only when a model genuinely needs a color that does not yet exist**. It is not seeded with a generic color set — that would impose an art direction the project never asked for.

Growing on demand has one failure mode, and it is the one to guard against: every model adds "just one" slightly-different grey until there are eleven of them and the palette constrains nothing. So reuse is not a nicety here. **A model that introduces its own material when the palette already has a serviceable one is wrong**, even if it looks fine in isolation.

`PALETTE.md` documents each material — name, hex, roughness, metallic, and what it is intended for — and is generated from `palette.blend`, so the two cannot drift apart. Models link from `palette.blend` rather than defining their own copies, so palette changes propagate.

Create the palette once, at library setup:

```bash
blender --background --python "$SKILL_DIR"/scripts/palette.py -- init
```

Before adding any material, check what already exists:

```bash
blender --background --python "$SKILL_DIR"/scripts/palette.py -- check --hex 7A7D80 --metallic 1.0
```

Then add only if nothing serves:

```bash
blender --background --python "$SKILL_DIR"/scripts/palette.py -- add \
    --category Metal --name Steel_Worn --hex 7A7D80 \
    --roughness 0.55 --metallic 1.0 \
    --note "Hull plating, structural beams, used equipment"
```

`add` refuses when a perceptually equivalent material already exists and names the one to use instead. When it refuses, use the existing material — that is the guard working, not an obstacle. Override with `--force` only when the difference is deliberate and visible at the distance the asset is viewed from.

Never define a material locally in a model or component file. See `references/materials.md` for the naming scheme and the duplicate thresholds.

## Conventions

Consistency is what makes components interchangeable. Two components that disagree on scale are not reusable together, so these are not stylistic preferences.

- **Units:** 1 Blender unit = 1 metre. Metric scene units.
- **Grid:** modular components snap to a 0.25 m grid; larger structural pieces to 1.0 m.
- **Orientation:** +Z up, −Y forward.
- **Origins:** at the logical connection or pivot point.
- **Transforms:** applied before saving.
- **Naming:** `Type_Descriptor_Variant` — `Mesh_CrateBody_Small`, `Arm_DoorHinge`, `Bone_LidPivot`.

Full detail in `references/conventions.md`.

## Armatures

Add an armature whenever any part of the model could move — hinges, wheels, lids, limbs, antennae, sliding panels, articulated arms. The bar is low on purpose: an armature costs little and its absence means the asset cannot be animated without being rebuilt.

Name every bone descriptively. Use a sensible rest pose. Parent with automatic weights only where deformation is genuinely needed; for rigid mechanical parts, parent objects directly to bones instead, which is cleaner and more predictable.

If nothing on the model can move — a solid rock, a fixed wall panel — skip the armature and say why in your summary.

## Reference files

- `references/conventions.md` — naming, units, grid, origins, file organization
- `references/scripting.md` — bpy patterns, headless invocation, additive/defensive scripting
- `references/materials.md` — palette structure, categories, naming, extension procedure

## Scripts

- `scripts/index_library.py` — walk the library, inspect every `.blend`, write `library_index.json` and `LIBRARY.md`
- `scripts/palette.py` — create, inspect, and add to the shared palette; regenerate `PALETTE.md`
