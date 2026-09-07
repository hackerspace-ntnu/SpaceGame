---
system: LookLab
layer: pipeline
summary: Editor-only look-tuning instrument: captured stills repainted in a browser, driving the live Editor
paths:
  - LookLab/
  - tools/looklab.py
  - tools/looklab_palette_dump.mjs
  - tools/palette_preview.py
  - Assets/Game/Editor/Environment/LookLabLive.cs
  - Assets/Game/Scripts/World/Environment/ColorGrade/PaletteShape.cs
symptoms:
  - "a palette parameter changes in the lab and the Editor does not change at all"
  - "the lab's colours are close to the game's but not the same"
  - "a captured still already looks posterised before the lab touches it"
  - "the lab renders a look that no longer matches the world it was captured from"
  - "the screen look reverts to the committed one after a script change"
reads_with: [Environment, EditorTooling]
updated: 2026-09-07
---

# Look Lab

Tuning the screen look used to mean editing C#, waiting for a domain reload, entering play
mode and flying somewhere the change is visible. The Look Lab replaces that minutes-long
loop with a slider: six captured stills repaint instantly in a browser, and the same values
drive the running Editor.

**It is an exploration tool. Exactly one look ships**, in C#. See
`docs/superpowers/specs/2026-09-07-look-lab-design.md` for the phasing and the commit path.

## Model

Three parts that never talk to each other directly.

| Part | Where | What it does |
| --- | --- | --- |
| Capture | `LookLabCapture.cs` (not yet written — see the phase 1 plan, Task 7) | Renders the Game camera to `LookLab/scenes/<name>/color_ldr.png` plus a `pack.json` of provenance |
| The lab | `LookLab/app/` | Static ES modules; WebGL2 reimplementation of the stages; repaints on change |
| The bridge | [looklab.py](tools/looklab.py) writes, [LookLabLive.cs](Assets/Game/Editor/Environment/LookLabLive.cs) polls | The page POSTs a look; the server writes `LookLab/live/look.json` atomically; the Editor polls its timestamp at 20 Hz |

Nothing connects to Unity. There is no socket in the Editor to leak across a domain reload.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `PaletteShape` | [PaletteShape.cs](Assets/Game/Scripts/World/Environment/ColorGrade/PaletteShape.cs) | The lattice as data — hue count, lightness steps, chroma fractions and ceiling, neutral count and range. `Default` holds the one committed look. Never serialized on the renderer assets |
| `PastelPalette.Build(in PaletteShape)` | [PastelPalette.cs](Assets/Game/Scripts/World/Environment/ColorGrade/PastelPalette.cs) | Builds the 204 colours from a shape. The colour math; the numbers live in `PaletteShape` |
| `PastelQuantizePass.EnsurePalette` | [PastelQuantizeRenderFeature.cs](Assets/Game/Scripts/World/Environment/ColorGrade/PastelQuantizeRenderFeature.cs) | Rebuilds and re-uploads the palette when the shape changed. Called from `AddRenderPasses` |
| `LookLabLive` | [LookLabLive.cs](Assets/Game/Editor/Environment/LookLabLive.cs) | Polls `look.json`, validates the shape, pushes it to every installed feature. Toggled by `SpaceGame ▸ Look Lab ▸ Live Bridge` |
| `LabRenderer` | `LookLab/app/gl.js` | One WebGL2 context, one program, one viewport draw per panel |

## Flows

**Tune:** `python3 tools/looklab.py serve` → open `http://127.0.0.1:8777/app/` → in Unity turn
on `SpaceGame ▸ Look Lab ▸ Live Bridge` → drag a slider. The six stills repaint on the same
frame; the Editor follows within one 50 ms poll.

**Capture:** fly to somewhere worth judging → `SpaceGame ▸ Look Lab ▸ Capture Scene Pack` →
rename the folder under `LookLab/scenes/` to name the panel. The quantize filter is switched
off for the duration of the capture and switched back on afterwards.

**Commit a look:** phase 2. Today, read the numbers off the sliders, edit
`PaletteShape.Default` by hand, and bless the new look with
`python3 tools/palette_preview.py --dump > tools/palette_golden.txt`.

## Stages

A look is an ordered list of stages, each one file under `LookLab/stages/` — a JSON header,
a line containing only `---`, then a GLSL `vec3 apply(vec3 c, vec2 uv)` body. **The lab
builds its sliders from the headers**, so adding a stage file yields controls with no UI
code. One ships: `palette.snap` (order 20). A `detail.grain` stage existed briefly and was
deleted — screen-anchored noise swam over the ground, and its replacement needs depth.

Each stage takes the previous one's output, in `order` — **not** in filename order, because a
stage's position in the chain is part of what it means and a rename must not be able to
reverse it. `LabRenderer.compile` renames each body's `apply` to `apply_<index>` and calls
them in sequence; without that, **the second stage to declare `apply` would fail the link**,
which is why the chaining exists before a second stage does. Stages hand each other
**linear** colour, so a stage working in Oklab has to convert back before returning.

A param marked `"rebuild": true` describes the CPU-built lattice rather than a uniform, and
changing it rebuilds the palette texture. Everything else becomes a `P_<name>` float uniform.

## What the lab cannot do

It covers everything **downstream of the framebuffer**. Lighting, shadows, skybox, materials
and any Volume override that runs before the capture point are **baked into the PNG** — the
lab does not simulate the game's lighting, it inherits it. Moving the sun means a fresh
capture. That is the cost of a still-based lab, and the reason the live bridge exists: tune
fast on stills, then fly around in the Editor.

## Multiplayer

Not applicable, explicitly. The lab, the capture tool and the bridge are all editor-only, and
the shipped filter is a screen effect with no game state and nothing to replicate. No runtime
code and no network prefabs are involved.

## Persistence

No state worth saving, explicitly. Presets are files under `LookLab/presets/`, the committed
look is source code, and `LookLab/live/look.json` is gitignored ephemeral tuning state.
Nothing enters a save file.

## Gotchas

- **`LookLab/` must stay outside `Assets/`.** A write inside the project triggers an asset
  import, and potentially a domain reload, on every parameter change — exactly the loop the
  lab exists to remove.
- **A palette parameter that changes nothing used to be the normal case.** The palette was
  built once in the pass constructor, so anything arriving over the bridge set a field that
  was never read again: no error, no effect. `EnsurePalette` is the fix. Scalars like `blend`
  hid it, because they are pushed to the material every frame regardless.
- **The live bridge never calls `SetDirty` or `SaveAssets`.** The in-memory feature instance
  is what URP renders from; persisting it would rewrite the renderer assets on every slider
  drag. Live values are ephemeral, and a domain reload restores the committed ones — that is
  intended, not a bug.
- **Capture switches the quantize filter off.** The lab quantizes the still itself, so a
  capture taken with the filter on would be posterised twice and every judgement made from it
  would be wrong.
- **The palette goes through Unity's gamma round trip, and the JS port has to as well.** C#
  builds each entry as a gamma-space `Color` and the render feature reads back `.linear`, so
  the value the shader writes has been through `LinearToGammaSpace` (exponent **0.41666**,
  Unity's own approximation, not 1/2.4) and back. `LookLab/app/palette.js` replicates the
  round trip deliberately; "simplifying" it makes the lab a fraction brighter than the game.
- **Three ports of one lattice exist** — C# ships it, Python checks it, JS renders it.
  `python3 tools/palette_preview.py --check` compares all three and fails on drift. Change
  one, change all three.
- **`tools/palette_golden.txt` is the committed look, entry for entry.** If `--check` reports
  it changed and that was not intended, the palette code broke. If it *was* intended,
  regenerate it in the same commit.
- **A capture pack silently stops matching the build it came from.** `pack.json` records the
  git SHA, resolution, camera and active volume profiles for exactly this reason — hover a
  scene tab to see them. A pack whose provenance no longer matches is not something the lab
  can detect for you.
- **Part of the shipped look is not in the lab, by construction.** The ink lines need scene
  depth for their silhouettes, and a captured still carries none — see
  [Environment](Environment.md). `InkShape` is serialized on the render feature and dragged in
  the Inspector instead. The lab covers the palette; it is not the whole look any more.
- **Node is optional, but without it one check goes quiet.** `--check` skips the JS
  comparison when `node` is absent and says so; it does not fail.

## Extending

Adding a stage: drop a `.stage` file in `LookLab/stages/` with an `order`, and mirror its
body in the shipped HLSL. Sliders appear with no UI code. Pure functions of `(colour, uv)`
are inlined into the main fragment pass and chained; anything needing its own framebuffer, or
depth and normals, waits for the pass graph — a still carries neither.

A stage that changes what reaches the *next* stage changes what `blend` means. `_Blend` lerps
from the painted colour rather than the raw sample for that reason: in the shader the snap
receives whatever the ink and stipple left, and blending back to the untouched frame there
would make it disagree with a chain that had a stage in front of it.

Do not serialize `PaletteShape` on the renderer assets. One committed look, in C#, is the
constraint that stopped the deleted ten-style `PastelStyleLibrary` being rebuilt with a nicer
UI.
