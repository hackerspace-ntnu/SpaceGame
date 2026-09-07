---
system: LookLab
layer: pipeline
summary: An Editor window that retunes the shipped palette live in the Game view, without a recompile
paths:
  - Assets/Game/Editor/Environment/LookLabWindow.cs
  - Assets/Game/Scripts/World/Environment/ColorGrade/PaletteShape.cs
  - tools/palette_preview.py
symptoms:
  - "a palette slider moves and the Game view does not change"
  - "the screen look reverts to the committed one after a script change"
  - "the Look Lab says the quantize filter is not installed"
  - "palette_preview.py --check fails after a look was retuned"
reads_with: [Environment, EditorTooling]
updated: 2026-09-07
---

# Look Lab

Tuning the screen look used to mean editing C#, waiting for a domain reload, entering play
mode and flying somewhere the change is visible. The Look Lab replaces that minutes-long
loop with a slider: `SpaceGame ▸ Look Lab`, drag, and the Game view repaints on the same
frame, in edit mode or in play mode.

**It is an exploration tool. Exactly one look ships.** Nothing here persists on its own —
see *Two saving contracts* below.

## Model

One `EditorWindow` writing to the in-memory render feature. Nothing else.

| Part | What it does |
| --- | --- |
| The window | Builds a [PaletteShape](Assets/Game/Scripts/World/Environment/ColorGrade/PaletteShape.cs), an [InkShape](Assets/Game/Scripts/World/Environment/ColorGrade/InkShape.cs) and a [NoiseShape](Assets/Game/Scripts/World/Environment/ColorGrade/NoiseShape.cs) from sliders, validates the palette, previews its colours as swatches |
| Live | Pushes all three plus `blend` onto every installed `PastelQuantizeRenderFeature`, and repaints |
| Global Volume | [SceneVolumeSection](Assets/Game/Editor/Environment/SceneVolumeSection.cs) draws the scene's global `Volume` grade — exposure, contrast, saturation — because that is what feeds the snap, and judging a palette while its input lives in another window is guesswork |
| Save ink + noise | Writes the *serialized* half to the renderer assets, on a button rather than on every frame of a drag |
| Copy as C# | Formats the palette shape as a `PaletteShape.Default` body on the clipboard |

The preview *is* the Game view — lit, moving, and already there. An earlier version of this
system was a browser app plus a Python server plus a polled JSON file, whose whole purpose
was repainting **captured stills** instantly; three moving parts and a second port of the
palette maths bought a worse preview than the one Unity already draws.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `LookLabWindow` | [LookLabWindow.cs](Assets/Game/Editor/Environment/LookLabWindow.cs) | The whole tool: sliders, swatches, live push, restore-on-close |
| `PaletteShape` | [PaletteShape.cs](Assets/Game/Scripts/World/Environment/ColorGrade/PaletteShape.cs) | The lattice as data — hue count, lightness steps, chroma fractions and ceiling, neutral count and range. `Default` holds the one committed look. Never serialized on the renderer assets |
| `PastelPalette.Build(in PaletteShape)` | [PastelPalette.cs](Assets/Game/Scripts/World/Environment/ColorGrade/PastelPalette.cs) | Builds the 204 colours from a shape. The colour math; the numbers live in `PaletteShape` |
| `PastelQuantizePass.EnsurePalette` | [PastelQuantizeRenderFeature.cs](Assets/Game/Scripts/World/Environment/ColorGrade/PastelQuantizeRenderFeature.cs) | Rebuilds and re-uploads the palette when the shape changed. Called from `AddRenderPasses`, and the reason a live edit reaches the screen at all |

## Flows

**Tune:** `SpaceGame ▸ Look Lab` → turn on **Live** → drag. Toggle **Show committed** to flick
back to the shipped look; the eye detects change far better than it detects difference, so
that resolves a shift two side-by-side views would hide.

**Commit a look:** `Copy as C#` → paste over `PaletteShape.Default` → bless it with
`python3 tools/palette_preview.py --dump > tools/palette_golden.txt`, in the same commit.

**Two saving contracts in one window**, because the look has two halves. The ink and the
noise are serialized on the renderer assets, so `Save ink + noise` persists them. The palette
shape is deliberately *not* serialized anywhere — that is what stops the PC and mobile
renderers drifting into different colours — so it leaves through `Copy as C#` and a paste over
`PaletteShape.Default`. The global Volume is a third case: it is an asset with no in-memory
copy to restore, so that section writes through `Undo` and `SetDirty` immediately, exactly as
its own Inspector would.

## Multiplayer

Not applicable, explicitly. The window is editor-only and the shipped filter is a screen
effect with no game state and nothing to replicate. No runtime code, no network prefabs.

## Persistence

No state worth saving, explicitly. Nothing here enters a save file. The palette shape is
source code and the window's copy of it lives only as long as the window; the ink and the
noise are serialized on the renderer assets but written only by `Save ink + noise`, never by
a drag.

## Gotchas

- **Nothing is written while a slider moves.** The in-memory feature instance is what URP
  renders from, so persisting on change would rewrite the renderer assets on every frame of a
  drag. Live values are therefore ephemeral, and **a domain reload restores the committed look
  mid-session** — that is intended, not a bug. Closing the window restores what it found, and
  anything worth keeping leaves through `Save ink + noise` or `Copy as C#`.
- **A Volume slider that does nothing is an override that was never ticked.** Each
  `VolumeParameter` is inert until its own `overrideState` is on, which is why the checkbox
  and the slider are drawn on one line here.
- **A palette parameter that changes nothing used to be the normal case.** The palette was
  built once in the pass constructor, so a shape set after that was never read again: no
  error, no effect. `EnsurePalette` is the fix. Scalars like `blend` hid it, because they are
  pushed to the material every frame regardless.
- **The palette goes through Unity's gamma round trip.** C# builds each entry as a
  gamma-space `Color` and the render feature reads back `.linear`, so the value the shader
  writes has been through `LinearToGammaSpace` (exponent **0.41666**, Unity's own
  approximation, not 1/2.4) and back. Anything that reimplements the palette has to
  replicate that or it lands a fraction brighter than the game.
- **Two ports of one lattice exist** — C# ships it, `tools/palette_preview.py` checks it.
  `--check` compares them constant by constant and fails on drift. A third port in JS existed
  for the browser lab and went with it; there is no reason to add another.
- **`tools/palette_golden.txt` is the committed look, entry for entry.** If `--check` reports
  it changed and that was not intended, the palette code broke. If it *was* intended,
  regenerate it in the same commit.

## Extending

Add a control by adding a field to `PaletteShape` and a slider that writes it — the window
reads and writes that struct directly, so there is no manifest, no schema and no second
place to register anything.

Do not serialize `PaletteShape` on the renderer assets. One committed look, in C#, is the
constraint that stopped the deleted ten-style `PastelStyleLibrary` being rebuilt with a nicer
UI, and it is the same constraint that keeps this window a workshop rather than a wardrobe.
