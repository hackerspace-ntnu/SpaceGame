# Look Lab — a real-time post-processing tuning instrument

**Status:** approved design, not yet implemented
**Date:** 2026-09-07

## The problem

Tuning the game's look currently means editing `PastelPalette.cs`, waiting for a domain
reload, entering play mode and flying to a location that shows the change. That loop is
minutes long, shows one location at a time, and can only show one candidate look at a time.
Every judgement about colour is therefore made from memory of what the last version looked
like.

`GDC-L1-PROTO-0006` (objective, confidence 5) names loop count as the master variable of
quality and loop *length* as the thing to attack; `GDC-L1-ARCH-0005` says the same
architecturally. A slider that repaints six real frames instantly, and drives the running
game while it does, replaces a minutes-long loop with a millisecond one.

Both principles carry a warning that applies directly here. PROTO-0006: *"iteration needs
direction — looping without a clear question produces churn."* ARCH-0005: *"the honest
caution is over-tooling."* This repository has already built and deleted a ten-style
`PastelStyleLibrary` with a 4-up compare, biome palettes, contours, split-tone, cel
banding, dither and grain. **The lab must therefore have a commit path** — a way for an
exploration to end with one look landing in source — or it recreates that failure with a
better UI.

## What ships versus what explores

| | Explores | Ships |
| --- | --- | --- |
| Where the knobs live | The lab, and the editor-only bridge | Nowhere — one committed look |
| How many looks | As many as you like, saved as presets | Exactly one |
| Storage | Files under `LookLab/` | C# source |

The shipping renderer keeps its single look and its single `blend` knob. The lab is where
the alternatives live and die.

## Scope

The lab covers **everything downstream of the framebuffer**: colour grading, palette
quantization, dither, outlines, pixelation, fog, grain, vignette, bloom.

It cannot touch anything *upstream* of the framebuffer. Moving the sun, retinting the
skybox, swapping a material or changing a Volume override that runs before the capture
point all require Unity and a fresh capture. This is the honest cost of a still-based lab
and the reason the live bridge exists: tune fast on stills, then push into the running
Editor and fly around.

Lighting, shadows, skybox and materials are **not simulated** by the lab — they are baked
into the capture. The lab does not approximate the game's lighting; it inherits it.

## Architecture

```
Unity Editor                    LookLab/                      Browser lab
-----------                     --------                      -----------
Capture Scene Pack  --------->  scenes/<name>/                 loads packs as textures
(menu item)                       color_ldr.png   ---------->  WebGL2 pass graph
                                  color_hdr.bin                6 panels, repaint on change
                                  depth.bin
                                  normal.png                   preset bar: named looks
                                  pack.json

LookLabLive         <---------  live/look.json    <----------  POST /look
(polls mtime,                   (atomic write)                 tools/looklab.py
 pushes in memory)

PastelPalette.cs    <---------  tools/looklab.py commit <preset>
(one look, committed)           + palette_preview.py --check
                                + tools/looklab_parity.py
```

### 1. Capture

An editor menu item renders the current Game camera into `LookLab/scenes/<name>/`:

| File | Contents | Why |
| --- | --- | --- |
| `color_ldr.png` | sRGB, captured after URP post-processing | The exact buffer the quantizer sees. The lab's default working surface, and the source of bit-exact parity. |
| `color_hdr.bin` | Raw half-float RGB, before post | Makes exposure, tonemap and bloom tunable. Not a PNG: PNG cannot hold HDR, and RGBM encoding would silently cost precision. |
| `depth.bin` | Linear eye depth, half-float | Depth fog, depth-edge outlines, DoF. |
| `normal.png` | Encoded world normals | Normal-edge outlines. |
| `pack.json` | Resolution, camera FOV/near/far, volume state at capture, git SHA | A pack that no longer matches the game becomes detectable rather than misleading. |

Sampling depth and normals from a pass here needs an explicit `AddRasterRenderPass` with
`UseAllGlobalTextures(true)` plus `ConfigureInput(Depth | Normal)`.

**Capture at native resolution.** Dither and pixelation stages must be judged at the
resolution they will ship at; a downsampled capture would make the lab lie about exactly
the stages most sensitive to resolution.

**Commit the LDR PNGs; gitignore the binaries.** The LDR frames are the shared reference
the team can all see. The binaries are large and regenerable from the camera position
recorded in `pack.json`.

### 2. The lab

A static page under `LookLab/app/`, served locally.

- **Scene view** — tab through one scene at a time, or show all at once in a grid.
- **Preset bar** — named looks; click to switch, fork, rename, delete.
- **Stage stack** — add, remove, reorder, toggle; each stage's params as sliders.
- **A/B flicker** — a held key flips between the current look and a pinned preset. The eye
  detects change far better than it detects difference, so flicker beats side-by-side for
  judging small changes.

**Panels render on change, not at 60fps.** These are stills, so an idle stack costs
nothing. The grid renders each panel at its *displayed* size, which is what keeps a
204-entry nearest-neighbour search affordable across six panels.

### 3. The bridge — proven, 2026-09-07

Nothing connects to Unity. The page POSTs to the server that served it (same origin); the
server writes `LookLab/live/look.json` atomically (tempfile plus `os.replace`, a true
rename, so Unity can never read a half-written file); `LookLabLive` polls that file's
timestamp in `EditorApplication.update` and mutates the live render feature.

Verified end to end before this spec was written: writing `look.json` from a terminal
changed the rendered image in the Editor, with no recompile, no play mode and no MCP
involvement in the bridge itself. The log showed the change applying to **2 features** —
the PC and Mobile renderers together.

Constraints that came out of building it:

- **`LookLab/` lives at the repo root, never under `Assets/`.** A write inside the project
  triggers an asset import, and potentially a domain reload, on every parameter change.
- **The live driver must never call `SetDirty` or `SaveAssets`.** The in-memory feature
  instance is the one URP renders from; persisting it is unnecessary for a preview and
  would rewrite the renderer assets on every slider drag. Live values are ephemeral by
  design — a domain reload restores the committed ones.
- **Polling, not `FileSystemWatcher`.** The watcher raises on a background thread and does
  not survive domain reloads cleanly. A timestamp read at 20 Hz is free.
- **Turning the bridge off restores what it found.** It captures the feature's active flag
  and blend on enable.

Not yet measured: end-to-end latency under a continuous slider drag. The ceiling is the
50 ms poll interval plus a repaint.

### 4. `PaletteShape` — the fix for a silent no-op

`settings.blend` is pushed to the material every frame, so scalar parameters flow over the
bridge for free. **The palette does not**: it is built once in the pass constructor, so
palette parameters arriving over the bridge currently do nothing, with no error.

`PastelPalette.Default()` becomes `PastelPalette.Build(PaletteShape)`. `PaletteShape` is a
small struct holding the lattice constants — hue count, lightness steps, chroma fractions,
chroma ceiling, neutral count and range. `PaletteShape.Default` holds **exactly today's
committed values**, so the game's look does not change by one pixel. The render feature
rebuilds the pass when the shape changes.

**This is not the deleted style system.** The shape is not serialized on the renderer
assets; it stays a code default with one committed value, so the PC and Mobile renderers
still cannot drift. Only the editor-only bridge (in memory, ephemeral) and a code edit can
change it.

## The stage model

A look is an **ordered list of stages**. Each stage is one file — a JSON header plus a
GLSL body:

```
LookLab/stages/grade.exposure.stage
  { "id": "grade.exposure", "label": "Exposure",
    "params": { "ev": { "min": -4, "max": 4, "default": 0 } },
    "requires": [] }
  vec3 apply(vec3 c, vec2 uv) { return c * exp2(P_ev); }
```

**The lab builds its UI from these headers.** Adding a stage file yields sliders with no UI
code. This is what stops a lab covering "everything visual" from becoming a sprawl of
hand-maintained bespoke panels.

### Compilation: a pass graph

A look compiles to a **list of passes**, not a single shader.

- Stages that are a pure function of `(uv, colour, depth, normal)` declare no passes and
  are **inlined by string concatenation into the main fragment pass**. This is the
  overwhelming majority: grade, palette snap, posterize, dither, grain, vignette, depth
  fog, luma and depth outlines, pixelation.
- A stage may declare a `passes` array — sub-passes with their own GLSL, an output scale
  and named outputs the main pass can sample. Implemented with ping-ponged framebuffers.

Changing the stack recompiles (milliseconds). Dragging a slider only sets a uniform.

Keeping the simple case simple is the point: multipass exists for the stages that need it
without every other stage paying for it.

### Bloom is a special case, and its parity target is different

Bloom is the reason the pass graph exists. It also cannot be held to the same parity
standard as the colour stages, and pretending otherwise would build a check that fails
forever.

**URP already has bloom, in the Volume stack, before the capture point.** Reimplementing it
in GLSL to a tight ΔOklab threshold would mean matching URP's specific mip chain, threshold
knee and scatter — achievable only approximately, and pointless, because the game would
still be rendering URP's version.

So: **the lab's bloom is a preview of URP's bloom, parameterised the same way (threshold,
intensity, scatter). Committing bloom writes to the Volume profile's Bloom override, not to
custom HLSL.** No second bloom implementation ships. Bloom is explicitly excluded from the
strict parity gate and is judged by eye through the live bridge instead.

Bloom's threshold operates on HDR values, so it depends on the HDR capture. Both land in
phase 3.

## Parity: proving the lab tells the truth

`tools/looklab_parity.py` takes one scene pack and one look:

1. **Unity path** — an editor menu item blits the still through the *real* material and
   writes a PNG.
2. **Lab path** — drives the actual lab page headlessly via the `chrome-devtools` MCP and
   reads the canvas back. Deliberately the same code the user tunes with; a separate
   "reference implementation" would only verify a third thing nobody uses.
3. **Diff** — mean and max ΔOklab plus a difference image. Over threshold fails the commit.

The two renderers are genuinely independent implementations (HLSL versus GLSL), and the
differ reads only the two PNGs — it shares no code with either. A check that shares its
input with the thing it checks proves nothing.

`tools/palette_preview.py --check` already fails if its Python port of the palette has
drifted from the C#; the commit path runs it too.

**A 3D LUT is not an option and should not be revisited.** A `.cube` LUT is sampled
trilinearly, and the palette snap is deliberately high-frequency in colour space —
interpolation would smear exactly the hard edges the snap exists to create. Grade-only
looks would survive a LUT; the snap would not.

## Phasing

**Phase 1 — close the loop.** LDR capture; lab shell (scene tabs, six-up grid, preset bar,
A/B flicker); the palette-snap stage; `PaletteShape`; the bridge carrying palette
parameters.
*Done when:* dragging a slider repaints six stills and the running game.

**Phase 2 — earn trust.** Parity harness; commit path; `palette_preview.py --check` folded
in.
*Done when:* a look reaches `PastelPalette.cs` with proof it matches.

**Phase 3 — reach.** HDR, depth and normal capture; the pass graph; the stage catalogue
(grade, dither, outline, pixelate, fog, grain); bloom last, committing to the Volume
override.

Phase 3 is the sprawl risk and is last deliberately: by then real use will have shown which
stages are actually reached for, so the unused ones never get built. That is ARCH-0005's
over-tooling caution applied rather than quoted.

**Each phase gets its own implementation plan.** This spec is too large for one, and the
phases are deliberately sequenced so that each is usable on its own: phase 1 is a working
instrument even if phase 2 never happens, and phase 2 makes it safe to act on even if phase
3 never happens. Plan phase 1 first, and let what it teaches reshape the phase 2 plan
rather than writing both now.

## Non-negotiables

**Multiplayer: not applicable, explicitly.** The lab, the capture tool and the bridge are
all editor-only. The shipped filter is a screen effect with no game state and nothing to
replicate. No runtime code and no network prefabs are involved.

**Persistence: no state worth saving, explicitly.** Presets are files under `LookLab/`. The
committed look is source code. Nothing enters a save file.

**Code smells.** Stage parameters are data in stage manifests, not magic numbers. The
palette lattice becomes `PaletteShape` rather than scattered constants. The bridge reuses
`VolumetricSetup.FindRenderers`; the capture tool reuses the existing render-feature
install helpers. Parse failures on `look.json` log loudly — a look that silently fails to
apply reads as a broken bridge.

## Documentation obligations

- Extend `docs/AI/systems/Environment.md` for `PaletteShape` and the bridge, deleting what
  they make untrue.
- A new `docs/AI/systems/LookLab.md` once the lab itself exists, plus a plain-language
  entry in `docs/Human/the-systems.md` — the validator fails without it.
- Add `symptoms:` entries for the two silent failures found while designing this: palette
  parameters over the bridge doing nothing, and a capture pack silently not matching the
  build it was taken from.
- `python3 tools/docs_check.py --index` to regenerate and validate.

## Open questions

- End-to-end latency under a continuous slider drag is unmeasured. If a 50 ms poll proves
  too slow to feel live, the interval can drop before anything more invasive is considered.
- The parity threshold for colour stages is unset. It should be chosen from a measured
  first run, not guessed.

## Notes for whoever implements this

`Assets/Game/Editor/Environment/LookLabLive.cs` and `LookLab/live/look.json` already exist
from the spike that proved the bridge. They are phase-1 code, not throwaway, but
`LookLabLive` currently carries only `enabled` and `blend`.

`tools/typecheck.py --editor` does **not** build `SpaceGame.Tests.EditMode` — it has its
own asmdef. A green typecheck does not mean Unity will reload the domain.
