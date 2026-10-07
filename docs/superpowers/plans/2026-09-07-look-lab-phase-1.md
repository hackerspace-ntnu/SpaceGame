# Look Lab Phase 1 — Close the Loop: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the pastel palette tunable in milliseconds — a browser lab that repaints six captured stills on every slider drag, and drives the running Unity Editor at the same time.

**Architecture:** An editor menu item captures the post-processed Game frame to `LookLab/scenes/<name>/color_ldr.png`. A static page under `LookLab/app/` reimplements the palette snap in WebGL2 and repaints those stills on change. The same page POSTs the look to `tools/looklab.py`, which atomically writes `LookLab/live/look.json`; the already-proven `LookLabLive` bridge polls that file and mutates the live render feature. The palette becomes rebuildable at runtime via a new `PaletteShape` struct, which is what makes palette parameters actually flow over the bridge instead of silently doing nothing.

**Tech Stack:** Unity 6 URP render graph (C#, HLSL), Python 3 stdlib (`http.server`), plain ES modules + WebGL2 (no build step, no dependencies), Pillow for the existing contact sheet.

---

## Reading before you start

- `docs/superpowers/specs/2026-09-07-look-lab-design.md` — the design this implements. Read it in full; this plan implements only its **Phase 1**.
- `docs/AI/systems/Environment.md` — the governing doc. Its `Gotchas` section explains why the palette is built in code and not serialized.
- `CLAUDE.md` — documentation obligations are part of the change, not a follow-up.

## Scope boundary — what phase 1 is NOT

Do not build any of this; it belongs to later phases and building it now is the over-tooling
failure the spec explicitly warns about:

- HDR / depth / normal capture (`color_hdr.bin`, `depth.bin`, `normal.png`) — phase 3.
- The multi-pass pass graph, ping-ponged framebuffers, `passes` arrays in stage manifests — phase 3.
- Any stage other than the palette snap — grade, dither, outline, pixelate, fog, grain, bloom — phase 3.
- The parity harness (`tools/looklab_parity.py`) and the commit path — phase 2.

Phase 1 ships **one stage**. The stage-manifest format exists so that adding a stage file
later yields sliders with no UI code — but only one manifest is written now.

## File structure

| File | Responsibility |
| --- | --- |
| `Assets/Game/Scripts/World/Environment/ColorGrade/PaletteShape.cs` | **Create.** The lattice constants as data: hue count, lightness steps, chroma fractions and ceiling, neutral count and range. Validation and element-wise equality. |
| `Assets/Game/Scripts/World/Environment/ColorGrade/PastelPalette.cs` | **Modify.** `Default()` becomes `Build(in PaletteShape)`. The colour math is untouched. |
| `Assets/Game/Scripts/World/Environment/ColorGrade/PastelQuantizeRenderFeature.cs` | **Modify.** Holds a non-serialized `PaletteShape` and rebuilds its uploaded palette when the shape changes. This is the fix for the silent no-op. |
| `Assets/Game/Editor/Environment/LookLabLive.cs` | **Modify.** `look.json` grows a `palette` object; the bridge validates it and pushes it. |
| `Assets/Game/Editor/Environment/LookLabCapture.cs` | **Create.** Menu item that writes a scene pack: `color_ldr.png` + `pack.json`. |
| `tools/looklab.py` | **Create.** Serves `LookLab/` on localhost and accepts the look and preset writes, atomically. |
| `tools/looklab_palette_dump.mjs` | **Create.** Node entry point that dumps the JS palette as hex, so Python can compare the two ports. |
| `tools/palette_preview.py` | **Modify.** Reads the constants from `PaletteShape.cs`; gains `--dump`; `--check` also compares against the golden file and the JS port. |
| `tools/palette_golden.txt` | **Create.** The 204 committed colours, captured *before* the refactor. The "not one pixel changes" proof. |
| `LookLab/app/package.json` | **Create.** One line: `{"type":"module"}`, so Node can import the browser ES modules for the cross-check. |
| `LookLab/app/palette.js` | **Create.** The lattice port to JS: Oklch to sRGB with gamut fit, plus the palette texture payload. |
| `LookLab/app/gl.js` | **Create.** WebGL2: compile the stage into a fragment shader, upload textures, draw one viewport per panel. |
| `LookLab/app/lab.js` | **Create.** App shell: scene tabs and six-up grid, manifest-driven sliders, preset bar, A/B flicker, POST to the bridge. |
| `LookLab/app/index.html` | **Create.** Markup and styling. No framework. |
| `LookLab/stages/palette.snap.stage` | **Create.** JSON header plus GLSL body. The only stage in phase 1. |
| `docs/AI/systems/LookLab.md` | **Create.** The system doc. |
| `docs/AI/systems/Environment.md` | **Modify.** `PaletteShape` and the bridge; delete what they make untrue. |
| `docs/Human/the-systems.md` | **Modify.** Plain-language entry; `docs_check.py` fails without it. |
| `.gitignore` | **Modify.** Ignore `LookLab/live/`; keep the LDR PNGs committed. |

## Constraints you must not violate

1. **`LookLab/` lives at the repo root, never under `Assets/`.** A write inside the project
   triggers an asset import — and potentially a domain reload — on every parameter change.
2. **The live driver must never call `SetDirty` or `SaveAssets`.** Live values are ephemeral
   by design; a domain reload restores the committed ones.
3. **`PaletteShape` is never serialized on the renderer assets.** It stays a code default with
   exactly one committed value, so the PC and Mobile renderers cannot drift.
4. **The committed look must not change by one pixel.** `tools/palette_golden.txt`, captured in
   Task 1, is what proves it.
5. **The Unity Editor is the last mile.** Tasks 1–13 need no Editor. Task 14 is the one visit.

## Known limitation, stated rather than hidden

`PastelPalette` and `PastelQuantizeRenderFeature` live in **Assembly-CSharp** (there is no
`.asmdef` under `Assets/Game/Scripts/World/Environment/`). `SpaceGame.Tests.EditMode` is an
asmdef assembly, and **an asmdef cannot reference Assembly-CSharp** — so no NUnit test can
touch `PaletteShape` without first splitting a new assembly out.

Splitting one was considered and rejected for phase 1: `tools/typecheck.py` only compiles
Assembly-CSharp and Assembly-CSharp-Editor, so moving these files into a new assembly would
make the headless type-check **blind to exactly the code this phase adds** — a worse trade than
the missing unit tests.

The new logic is therefore covered by checks that do not need an assembly reference:

- `tools/palette_golden.txt` — the palette output, entry for entry, against the pre-refactor build.
- The JS-vs-Python palette comparison — two independent ports, compared numerically.
- The existing constant mirror in `palette_preview.py --check`.
- `tools/typecheck.py --editor` — it compiles.

If a later phase does split the assembly, `PaletteShape.Validate` and `PaletteShape.Equals`
are the first two things to write NUnit tests for.

---

### Task 1: Capture the golden palette before touching anything

The whole refactor rests on "the game's look does not change by one pixel". Capture the
current output first, or there is nothing to compare against later.

**Files:**
- Modify: `tools/palette_preview.py`
- Create: `tools/palette_golden.txt`

- [ ] **Step 1: Add a `--dump` flag to `palette_preview.py`**

In `tools/palette_preview.py`, find `def main():` and replace the argument-parser block:

```python
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true',
                        help='validate the palette without writing the image')
    parser.add_argument('--out', default=None, help='output path for the contact sheet')
    args = parser.parse_args()
```

with:

```python
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true',
                        help='validate the palette without writing the image')
    parser.add_argument('--dump', action='store_true',
                        help='print the palette as one #RRGGBB per line and exit')
    parser.add_argument('--out', default=None, help='output path for the contact sheet')
    args = parser.parse_args()
```

Then immediately after `colors = default_palette()` in `main()`, add:

```python
    if args.dump:
        for color in colors:
            print(hex_of(color))
        return 0
```

And add this helper next to `check()` (it is the one place a colour becomes text, so both
`--dump` and the duplicate check below can use it):

```python
def hex_of(color):
    """A palette entry as #RRGGBB. The single place a colour becomes text, so the
    golden file, the duplicate check and the JS cross-check can never disagree about
    rounding."""
    return '#%02X%02X%02X' % tuple(round(c * 255) for c in color)
```

Then in `check()`, replace the line

```python
        name = '#%02X%02X%02X' % tuple(round(c * 255) for c in color)
```

with

```python
        name = hex_of(color)
```

- [ ] **Step 2: Verify the palette is still healthy before capturing it**

Run: `python3 tools/palette_preview.py --check`
Expected: exits 0, prints that the palette is well formed (204 entries, no duplicates, mirrors the C#).

If this fails **stop** — the golden capture would bake in an already-broken palette.

- [ ] **Step 3: Capture the golden file**

Run:

```bash
python3 tools/palette_preview.py --dump > tools/palette_golden.txt
wc -l tools/palette_golden.txt
```

Expected: `204 tools/palette_golden.txt`

- [ ] **Step 4: Sanity-check the contents**

Run: `head -3 tools/palette_golden.txt && tail -3 tools/palette_golden.txt`
Expected: six lines of `#RRGGBB`. The last three are near-white greys (the neutral ramp
runs to L 0.97), so expect something in the `#F2F2F2`–`#F8F8F8` region on the final line.

- [ ] **Step 5: Commit**

```bash
git add tools/palette_preview.py tools/palette_golden.txt
git commit -m "test: capture the committed pastel palette as a golden file"
```

---

### Task 2: `PaletteShape` — the lattice as data

**Files:**
- Create: `Assets/Game/Scripts/World/Environment/ColorGrade/PaletteShape.cs`

- [ ] **Step 1: Write `PaletteShape.cs`**

```csharp
using System;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// The lattice the pastel palette is built from, as data rather than as constants
    /// scattered through <see cref="PastelPalette"/>.
    ///
    /// <para>
    /// Deliberately not serialized anywhere. It stays a code default with exactly one
    /// committed value, so the PC and Mobile renderers cannot drift into building
    /// different palettes. The only things that change it are a code edit and the
    /// editor-only Look Lab bridge, whose values are in memory and ephemeral.
    /// </para>
    /// </summary>
    [Serializable]
    public struct PaletteShape : IEquatable<PaletteShape>
    {
        public int hueCount;

        /// <summary>Lightness steps, palest first. Not a ramp: the committed values are
        /// hand-tuned and unevenly spaced.</summary>
        public float[] lightnesses;

        /// <summary>Fractions of the in-gamut chroma ceiling at each hue and lightness —
        /// a muted and a vivid variant. Fractions rather than absolute chroma because
        /// sRGB holds very different amounts of chroma per hue, so a fixed pair would be
        /// clipped back to the same colour at some hues and collapse into duplicates.</summary>
        public float[] chromaFractions;

        /// <summary>Ceiling on the vivid variant, before the per-hue gamut fit. Without
        /// it the top fraction sits on the sRGB boundary and the palette goes neon.</summary>
        public float chromaCeiling;

        public int neutralCount;
        public float neutralMinL;
        public float neutralMaxL;

        /// <summary>
        /// Exactly the values the game shipped with before <see cref="PaletteShape"/>
        /// existed. Changing any number here changes the committed look, and
        /// <c>tools/palette_golden.txt</c> will fail — which is the point.
        ///
        /// <para>A property, not a static readonly field: the arrays are mutable, and a
        /// shared instance would let one caller's edit reach every other caller.</para>
        /// </summary>
        public static PaletteShape Default => new PaletteShape
        {
            hueCount = 16,
            lightnesses = new[] { 0.92f, 0.82f, 0.72f, 0.61f, 0.49f, 0.36f },
            chromaFractions = new[] { 0.5f, 1f },
            chromaCeiling = 0.20f,
            neutralCount = 12,
            neutralMinL = 0.16f,
            neutralMaxL = 0.97f,
        };

        /// <summary>How many colours <see cref="PastelPalette.Build"/> will emit.</summary>
        public int EntryCount =>
            hueCount * (lightnesses?.Length ?? 0) * (chromaFractions?.Length ?? 0) + neutralCount;

        /// <summary>
        /// True when this came back from JsonUtility over JSON that carried no palette
        /// at all. JsonUtility cannot report a missing field, so the caller has to tell
        /// "absent" from "present but wrong" itself — and the two deserve different
        /// answers: absent means keep what is committed, wrong means say so loudly.
        /// </summary>
        public bool IsUnset =>
            hueCount == 0 && neutralCount == 0 && lightnesses == null && chromaFractions == null;

        /// <summary>
        /// Whether this shape can be built at all. Every rejection is a shape that would
        /// otherwise produce a silently wrong palette — an empty one, one that overruns
        /// the shader's array, or a neutral ramp that divides by zero.
        /// </summary>
        public bool Validate(int maxEntries, out string error)
        {
            if (hueCount < 1)
            {
                error = $"hueCount is {hueCount}; it must be at least 1.";
                return false;
            }

            if (lightnesses == null || lightnesses.Length == 0)
            {
                error = "lightnesses is empty; the lattice needs at least one lightness step.";
                return false;
            }

            if (chromaFractions == null || chromaFractions.Length == 0)
            {
                error = "chromaFractions is empty; the lattice needs at least one chroma variant.";
                return false;
            }

            // The neutral ramp interpolates over neutralCount - 1, so a single neutral
            // divides by zero and writes a NaN colour into the palette.
            if (neutralCount < 2)
            {
                error = $"neutralCount is {neutralCount}; the grey ramp needs at least 2 steps.";
                return false;
            }

            if (chromaCeiling <= 0f)
            {
                error = $"chromaCeiling is {chromaCeiling}; it must be above 0.";
                return false;
            }

            int count = EntryCount;
            if (count > maxEntries)
            {
                error = $"the lattice would build {count} colours but the shader holds {maxEntries}.";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Element-wise, because the array fields make the default struct equality a
        /// reference comparison — which would report "unchanged" for a shape whose
        /// lightness ramp was rewritten in place, and the palette would never rebuild.
        /// </summary>
        public bool Equals(PaletteShape other)
        {
            return hueCount == other.hueCount
                && neutralCount == other.neutralCount
                && neutralMinL.Equals(other.neutralMinL)
                && neutralMaxL.Equals(other.neutralMaxL)
                && chromaCeiling.Equals(other.chromaCeiling)
                && SameValues(lightnesses, other.lightnesses)
                && SameValues(chromaFractions, other.chromaFractions);
        }

        public override bool Equals(object obj) => obj is PaletteShape other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + hueCount;
                hash = hash * 31 + neutralCount;
                hash = hash * 31 + neutralMinL.GetHashCode();
                hash = hash * 31 + neutralMaxL.GetHashCode();
                hash = hash * 31 + chromaCeiling.GetHashCode();
                hash = hash * 31 + ValuesHash(lightnesses);
                hash = hash * 31 + ValuesHash(chromaFractions);
                return hash;
            }
        }

        private static bool SameValues(float[] a, float[] b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (!a[i].Equals(b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static int ValuesHash(float[] values)
        {
            if (values == null)
            {
                return 0;
            }

            unchecked
            {
                int hash = values.Length;
                foreach (float value in values)
                {
                    hash = hash * 31 + value.GetHashCode();
                }

                return hash;
            }
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `python3 tools/typecheck.py`
Expected: exits 0. (`PaletteShape` is unreferenced at this point — that is fine, the next task wires it up.)

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Scripts/World/Environment/ColorGrade/PaletteShape.cs
git commit -m "feat: add PaletteShape, the pastel lattice as data"
```

---

### Task 3: `PastelPalette.Build(shape)`

**Files:**
- Modify: `Assets/Game/Scripts/World/Environment/ColorGrade/PastelPalette.cs`

- [ ] **Step 1: Replace the constants and `Default()` with `Build`**

In `PastelPalette.cs`, delete everything from the `// 16 hues x 6 lightnesses...` comment
block down to the closing brace of `Default()` — that is, the seven constant/array
declarations for `HueCount`, `Lightnesses`, `ChromaFractions`, `ChromaCeiling`,
`NeutralCount`, `NeutralMinL`, `NeutralMaxL`, and the whole `Default()` method — and put
this in their place. **Keep `GamutFitIterations` and `GamutEpsilon`**: they are properties of
the gamut solver, not of the lattice, and nothing tunes them.

```csharp
        // Halving 16 times resolves chroma far finer than an 8-bit channel can show.
        private const int GamutFitIterations = 16;
        private const float GamutEpsilon = 1e-4f;

        /// <summary>
        /// A lattice over Oklch — hue x lightness x chroma — so fields of similar colour
        /// snap to visibly distinct flats, plus a grey ramp so shadows keep their edges
        /// instead of collapsing into mush.
        ///
        /// <para>
        /// The caller is expected to have run <see cref="PaletteShape.Validate"/> first;
        /// this does no checking, because the two callers that exist both have somewhere
        /// better to report the problem than a colour array.
        /// </para>
        /// </summary>
        public static Color[] Build(in PaletteShape shape)
        {
            var colors = new Color[shape.EntryCount];
            int index = 0;

            for (int h = 0; h < shape.hueCount; h++)
            {
                float hue = h * (2f * Mathf.PI / shape.hueCount);
                foreach (float lightness in shape.lightnesses)
                {
                    // FitChroma of the ceiling *is* the in-gamut maximum: it returns the
                    // ceiling when that fits, and the gamut edge when it does not.
                    float ceiling = FitChroma(lightness, shape.chromaCeiling, hue);
                    foreach (float fraction in shape.chromaFractions)
                    {
                        colors[index++] = OklchToSrgb(lightness, ceiling * fraction, hue);
                    }
                }
            }

            for (int n = 0; n < shape.neutralCount; n++)
            {
                float lightness = Mathf.Lerp(
                    shape.neutralMinL, shape.neutralMaxL, n / (shape.neutralCount - 1f));
                colors[index++] = OklchToSrgb(lightness, 0f, 0f);
            }

            return colors;
        }
```

- [ ] **Step 2: Update the class summary**

Replace the `<summary>` on `PastelPalette` with:

```csharp
    /// <summary>
    /// Builds the pastel screen palette from a <see cref="PaletteShape"/>, and holds the
    /// Oklab conversion the quantize filter matches in. Kept apart from the render
    /// feature so the colour math can change without touching the render graph plumbing,
    /// and apart from <see cref="PaletteShape"/> so the numbers can be retuned without
    /// touching the math.
    /// </summary>
```

- [ ] **Step 3: Verify the type-check now fails, for the one expected reason**

Run: `python3 tools/typecheck.py`
Expected: FAIL, with an error in `PastelQuantizeRenderFeature.cs` on the line
`Color[] palette = PastelPalette.Default();` — "does not contain a definition for 'Default'".

That single error is the proof that the render feature was the only caller. **If any other
file appears in the error list, stop and read it** — it means there is a second consumer of
the palette this plan did not account for.

- [ ] **Step 4: Do not commit yet**

The tree does not compile until Task 4. Task 4's commit covers both.

---

### Task 4: The render feature rebuilds when the shape changes

This is the fix for the silent no-op the spec names: today the palette is built once in the
pass constructor, so palette parameters arriving over the bridge do nothing at all, with no
error.

**Files:**
- Modify: `Assets/Game/Scripts/World/Environment/ColorGrade/PastelQuantizeRenderFeature.cs`

- [ ] **Step 1: Make `MaxPaletteSize` public**

Replace:

```csharp
        private const int MaxPaletteSize = 256;
```

with:

```csharp
        public const int MaxPaletteSize = 256;
```

and replace its doc comment with one that says why it is public:

```csharp
        /// <summary>
        /// Must equal MAX_PALETTE in PastelQuantize.shader. Public because a
        /// <see cref="PaletteShape"/> has to be validated against it before it is pushed
        /// — a shape that overruns this would otherwise build a palette whose tail is
        /// silently ignored. A material's vector-array size freezes the first time it is
        /// set, so the upload is always padded to the full length and <c>_PaletteCount</c>
        /// carries the real count; upload fewer and the size is locked short for the
        /// material's lifetime.
        /// </summary>
```

- [ ] **Step 2: Add the shape to `Settings`**

Inside `public class Settings`, after the `blend` field, add:

```csharp
            /// <summary>
            /// The lattice the palette is built from. <see cref="System.NonSerialized"/>
            /// on purpose: keeping it off the renderer assets is what stops the PC and
            /// Mobile renderers drifting into different palettes, and what keeps the
            /// Look Lab an exploration tool rather than a second place a look can ship
            /// from. Only a code edit and the editor-only live bridge write it, and a
            /// domain reload restores this default.
            /// </summary>
            [System.NonSerialized] public PaletteShape paletteShape = PaletteShape.Default;
```

- [ ] **Step 3: Ask the pass to refresh before it is enqueued**

In `AddRenderPasses`, replace:

```csharp
            renderer.EnqueuePass(pass);
```

with:

```csharp
            pass.EnsurePalette();
            renderer.EnqueuePass(pass);
```

- [ ] **Step 4: Move the palette build out of the constructor**

In `PastelQuantizePass`, replace the `paletteCount` field declaration and the whole
constructor:

```csharp
            private readonly int paletteCount;

            public PastelQuantizePass(Settings settings)
            {
                this.settings = settings;
                renderPassEvent = settings.renderPassEvent;

                Color[] palette = PastelPalette.Default();
                if (palette.Length > MaxPaletteSize)
                {
                    Debug.LogError($"[PastelQuantize] PastelPalette has {palette.Length} colours but the " +
                                   $"shader holds {MaxPaletteSize}; the rest are ignored. Raise MAX_PALETTE " +
                                   "in PastelQuantize.shader and MaxPaletteSize here together.");
                }

                paletteCount = Mathf.Min(palette.Length, MaxPaletteSize);
                for (int i = 0; i < paletteCount; i++)
                {
                    Color linear = palette[i].linear;
                    paletteLinear[i] = linear;
                    paletteOklab[i] = PastelPalette.LinearToOklab(linear);
                }
            }
```

with:

```csharp
            private int paletteCount;
            private PaletteShape builtShape;
            private bool built;

            public PastelQuantizePass(Settings settings)
            {
                this.settings = settings;
                renderPassEvent = settings.renderPassEvent;
            }

            /// <summary>
            /// Rebuilds the uploaded palette when the shape has changed since the last
            /// build, and does nothing at all when it has not.
            ///
            /// <para>
            /// The palette used to be built once in the constructor, which meant a
            /// palette parameter arriving over the Look Lab bridge changed a field that
            /// nothing ever read again — no error, no effect. Scalar parameters like
            /// <c>blend</c> hid the problem because they are pushed to the material every
            /// frame regardless.
            /// </para>
            /// </summary>
            public void EnsurePalette()
            {
                if (built && builtShape.Equals(settings.paletteShape))
                {
                    return;
                }

                PaletteShape shape = settings.paletteShape;
                if (!shape.Validate(MaxPaletteSize, out string error))
                {
                    Debug.LogError($"[PastelQuantize] Cannot build the palette: {error} " +
                                   "Falling back to the committed shape.");
                    shape = PaletteShape.Default;
                    settings.paletteShape = shape;
                }

                Color[] palette = PastelPalette.Build(shape);
                paletteCount = palette.Length;
                for (int i = 0; i < paletteCount; i++)
                {
                    Color linear = palette[i].linear;
                    paletteLinear[i] = linear;
                    paletteOklab[i] = PastelPalette.LinearToOklab(linear);
                }

                builtShape = shape;
                built = true;
            }
```

The old `palette.Length > MaxPaletteSize` guard is gone on purpose: `Validate` now refuses
that shape before a single colour is built, which is both earlier and louder.

- [ ] **Step 5: Verify the whole project type-checks**

Run: `python3 tools/typecheck.py --editor`
Expected: exits 0.

- [ ] **Step 6: Prove the palette output is unchanged**

The Python port still mirrors the old constants, so the golden file must still match it:

```bash
python3 tools/palette_preview.py --dump | diff - tools/palette_golden.txt && echo IDENTICAL
```

Expected: `IDENTICAL`. (The C# side is proven against these same constants by the mirror
check, which Task 5 repoints at `PaletteShape.cs`.)

- [ ] **Step 7: Commit**

```bash
git add Assets/Game/Scripts/World/Environment/ColorGrade/PastelPalette.cs Assets/Game/Scripts/World/Environment/ColorGrade/PastelQuantizeRenderFeature.cs
git commit -m "fix: rebuild the pastel palette when its shape changes"
```

---

### Task 5: Point the Python mirror check at `PaletteShape.cs`

Without this the check still reads `PastelPalette.cs`, finds none of the constants there any
more, and reports seven "no X found" problems.

**Files:**
- Modify: `tools/palette_preview.py`

- [ ] **Step 1: Run the check and see it fail**

Run: `python3 tools/palette_preview.py --check`
Expected: FAIL, listing `no HueCount found`, `no Lightnesses array found` and five more.

- [ ] **Step 2: Repoint the path and loosen the patterns**

Replace the `CSHARP` constant:

```python
CSHARP = os.path.join('Assets', 'Game', 'Scripts', 'World', 'Environment', 'ColorGrade',
                      'PastelPalette.cs')
```

with:

```python
# The lattice constants moved out of PastelPalette.cs into PaletteShape.Default when the
# palette became rebuildable at runtime. The colour *math* is still in PastelPalette.cs;
# only the numbers live here.
CSHARP = os.path.join('Assets', 'Game', 'Scripts', 'World', 'Environment', 'ColorGrade',
                      'PaletteShape.cs')
```

Then in `check_mirrors_csharp`, replace the two matcher helpers. They must now accept an
object-initialiser (`hueCount = 16,`) as well as a const declaration (`HueCount = 16;`), and
a `new[] { ... }` array as well as a bare `{ ... }`:

```python
    def scalar(name, expected):
        # Terminator is ',' inside PaletteShape.Default's object initialiser and ';' for
        # a plain const, so accept either rather than pinning the declaration style.
        match = re.search(r'\b%s\s*=\s*([0-9.]+)f?\s*[,;]' % name, source)
        if match is None:
            problems.append('%s: no %s found' % (CSHARP, name))
        elif abs(float(match.group(1)) - expected) > 1e-6:
            problems.append('%s: %s is %s in C#, %s here'
                            % (CSHARP, name, match.group(1), expected))

    def float_array(name, expected):
        match = re.search(r'\b%s\s*=\s*(?:new\[\]\s*)?\{([^}]*)\}' % name, source, re.S)
        if match is None:
            problems.append('%s: no %s array found' % (CSHARP, name))
            return
        found = [float(v) for v in re.findall(r'([0-9.]+)f', match.group(1))]
        if [round(v, 6) for v in found] != [round(v, 6) for v in expected]:
            problems.append('%s: %s differs from this port\n'
                            '    C#: %s\n    here: %s' % (CSHARP, name, found, expected))
```

Finally, replace the seven calls with their new camelCase field names:

```python
    float_array('lightnesses', LIGHTNESSES)
    float_array('chromaFractions', CHROMA_FRACTIONS)
    scalar('chromaCeiling', CHROMA_CEILING)

    scalar('hueCount', HUE_COUNT)
    scalar('neutralCount', NEUTRAL_COUNT)
    scalar('neutralMinL', NEUTRAL_MIN_L)
    scalar('neutralMaxL', NEUTRAL_MAX_L)
```

- [ ] **Step 3: Add the golden comparison to `--check`**

The mirror check catches a changed *constant*. It cannot catch a changed *formula*. Add a
check that compares the built colours themselves.

Next to `check_mirrors_csharp`, add:

```python
GOLDEN = os.path.join('tools', 'palette_golden.txt')


def check_matches_golden(repo_root, colors):
    """Compares the built palette against the committed look, entry for entry.

    The constant mirror above catches a retuned number; this catches a changed formula,
    which is the failure mode a refactor of the palette code actually has. Regenerate
    the golden file only when the committed look is *meant* to change.
    """
    path = os.path.join(repo_root, GOLDEN)
    try:
        with open(path) as handle:
            golden = [line.strip() for line in handle if line.strip()]
    except OSError as error:
        return ['cannot read %s: %s' % (GOLDEN, error)]

    built = [hex_of(color) for color in colors]
    if len(built) != len(golden):
        return ['%s holds %d colours, the palette builds %d'
                % (GOLDEN, len(golden), len(built))]

    problems = ['entry %d is %s, %s says %s' % (i, b, GOLDEN, g)
                for i, (b, g) in enumerate(zip(built, golden)) if b != g]
    if problems:
        problems.append('the committed look changed. If that was intended, regenerate '
                        'with: python3 tools/palette_preview.py --dump > ' + GOLDEN)
    return problems
```

Then in `main()`, replace:

```python
    problems = check_mirrors_csharp(repo_root) + check(colors)
```

with:

```python
    problems = (check_mirrors_csharp(repo_root)
                + check_matches_golden(repo_root, colors)
                + check(colors))
```

- [ ] **Step 4: Verify the check passes again**

Run: `python3 tools/palette_preview.py --check`
Expected: exits 0.

- [ ] **Step 5: Verify the check still bites**

Temporarily change `chromaCeiling = 0.20f` to `0.21f` in `PaletteShape.cs`, then run:

Run: `python3 tools/palette_preview.py --check`
Expected: FAIL, reporting `chromaCeiling is 0.21 in C#, 0.2 here`.

Revert the edit and re-run; expected: exits 0. **Do not commit with the edit in place.**

- [ ] **Step 6: Commit**

```bash
git add tools/palette_preview.py
git commit -m "test: check the palette against PaletteShape.cs and the golden look"
```

---

### Task 6: The bridge carries the palette

**Files:**
- Modify: `Assets/Game/Editor/Environment/LookLabLive.cs`

- [ ] **Step 1: Extend `LookState`**

Replace the `LookState` class at the bottom of the file:

```csharp
        [Serializable]
        private class LookState
        {
            public bool enabled;
            public float blend;
        }
```

with:

```csharp
        [Serializable]
        private class LookState
        {
            public bool enabled;
            public float blend;

            /// <summary>
            /// Omit it and the committed shape is kept. JsonUtility cannot report a
            /// missing field, so an absent object arrives here as a zeroed struct —
            /// <see cref="PaletteShape.IsUnset"/> is how that is told apart from a shape
            /// somebody actually got wrong.
            /// </summary>
            public PaletteShape palette;
        }
```

- [ ] **Step 2: Push the palette in `Apply`**

Replace the `ForEachFeature` block:

```csharp
            int touched = ForEachFeature(pastel =>
            {
                pastel.SetActive(state.enabled);
                pastel.settings.blend = state.blend;
            });
```

with:

```csharp
            // Resolved once rather than per feature, so a bad shape is reported once with
            // the file that carried it rather than once per installed renderer.
            bool hasPalette = !state.palette.IsUnset;
            if (hasPalette && !state.palette.Validate(
                    PastelQuantizeRenderFeature.MaxPaletteSize, out string paletteError))
            {
                Debug.LogError($"[LookLab] {path} carries an unbuildable palette: {paletteError} " +
                               "Nothing was applied.");
                return;
            }

            int touched = ForEachFeature(pastel =>
            {
                pastel.SetActive(state.enabled);
                pastel.settings.blend = state.blend;
                if (hasPalette)
                {
                    // Both features share the one shape instance, and its arrays are
                    // read-only downstream — the next read of look.json builds fresh
                    // ones rather than writing into these.
                    pastel.settings.paletteShape = state.palette;
                }
            });
```

and replace the final log line:

```csharp
            Debug.Log($"[LookLab] enabled={state.enabled} blend={state.blend:0.###} " +
                      $"on {touched} feature(s)");
```

with:

```csharp
            string palette = hasPalette
                ? $"palette={state.palette.EntryCount} colours"
                : "palette=committed";
            Debug.Log($"[LookLab] enabled={state.enabled} blend={state.blend:0.###} " +
                      $"{palette} on {touched} feature(s)");
```

- [ ] **Step 3: Restore the palette too when the bridge is switched off**

Replace the three restore fields:

```csharp
        private static bool restoreCaptured;
        private static bool restoreActive;
        private static float restoreBlend;
```

with:

```csharp
        private static bool restoreCaptured;
        private static bool restoreActive;
        private static float restoreBlend;
        private static PaletteShape restoreShape;
```

In `CaptureRestoreState`, replace:

```csharp
                restoreActive = pastel.isActive;
                restoreBlend = pastel.settings.blend;
                restoreCaptured = true;
```

with:

```csharp
                restoreActive = pastel.isActive;
                restoreBlend = pastel.settings.blend;
                restoreShape = pastel.settings.paletteShape;
                restoreCaptured = true;
```

In `RestoreState`, replace:

```csharp
                pastel.SetActive(restoreActive);
                pastel.settings.blend = restoreBlend;
```

with:

```csharp
                pastel.SetActive(restoreActive);
                pastel.settings.blend = restoreBlend;
                pastel.settings.paletteShape = restoreShape;
```

- [ ] **Step 4: Update the file header comment**

Replace the first paragraph of the file comment:

```csharp
// Drives the installed pastel quantize filter from a file on disk, so a look can be
// retuned without a recompile, an asset edit or a play-mode restart.
```

with:

```csharp
// Drives the installed pastel quantize filter from a file on disk, so a look can be
// retuned without a recompile, an asset edit or a play-mode restart. Carries the blend
// and the whole palette shape: the shape is what most tuning actually moves, and it only
// reaches the renderer because PastelQuantizePass.EnsurePalette rebuilds on change.
```

- [ ] **Step 5: Verify it compiles**

Run: `python3 tools/typecheck.py --editor`
Expected: exits 0.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Editor/Environment/LookLabLive.cs
git commit -m "feat: carry the palette shape over the Look Lab bridge"
```

---

### Task 7: Capture a scene pack

**Files:**
- Create: `Assets/Game/Editor/Environment/LookLabCapture.cs`

- [ ] **Step 1: Write `LookLabCapture.cs`**

```csharp
// Writes a Look Lab scene pack: the post-processed Game frame plus the metadata needed
// to tell later whether the pack still matches the build it came from.
//
// Phase 1 is LDR only. HDR, depth and normal capture land in phase 3, and need an
// explicit AddRasterRenderPass with ConfigureInput(Depth | Normal) rather than this
// Camera.Render blit.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SpaceGame.World.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools.Environment
{
    public static class LookLabCapture
    {
        private const string CaptureMenuPath = "SpaceGame/Look Lab/Capture Scene Pack";
        private const string RevealMenuPath = "SpaceGame/Look Lab/Show Scene Packs";
        private const string ScenesRelativePath = "LookLab/scenes";

        [MenuItem(CaptureMenuPath)]
        public static void Capture()
        {
            Camera camera = FindGameCamera();
            if (camera == null)
            {
                Debug.LogError("[LookLab] No enabled Game camera renders to the screen, so there " +
                               "is nothing to capture. Enter play mode, or enable a Game camera.");
                return;
            }

            Vector2 viewSize = Handles.GetMainGameViewSize();
            int width = Mathf.Max(1, Mathf.RoundToInt(viewSize.x));
            int height = Mathf.Max(1, Mathf.RoundToInt(viewSize.y));

            // The lab quantizes the still itself, so the capture has to be the frame the
            // quantizer *sees*. Capturing with the filter on would feed the lab an
            // already-posterised image and every judgement made from it would be wrong.
            List<PastelQuantizeRenderFeature> suppressed = SuppressQuantize();
            byte[] png;
            try
            {
                png = Render(camera, width, height);
            }
            finally
            {
                foreach (PastelQuantizeRenderFeature feature in suppressed)
                {
                    feature.SetActive(true);
                }
            }

            string directory = NextPackDirectory();
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "color_ldr.png"), png);
            File.WriteAllText(
                Path.Combine(directory, "pack.json"),
                JsonUtility.ToJson(DescribePack(camera, width, height), true));

            Debug.Log($"[LookLab] Captured {width}x{height} to {directory}. Rename the folder to " +
                      "name the panel; the lab picks it up on reload.");
        }

        [MenuItem(RevealMenuPath)]
        public static void Reveal()
        {
            string directory = ScenesDirectory;
            Directory.CreateDirectory(directory);
            EditorUtility.RevealInFinder(directory + Path.DirectorySeparatorChar);
        }

        private static string ScenesDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", ScenesRelativePath));

        /// <summary>
        /// The camera whose output the player actually sees: the highest-depth enabled
        /// Game camera that draws to the screen. Deliberately not <c>Camera.main</c> —
        /// in this project that is not the player's camera, and picking it would silently
        /// capture the wrong viewpoint.
        /// </summary>
        private static Camera FindGameCamera()
        {
            Camera best = null;

            foreach (Camera camera in Camera.allCameras)
            {
                if (camera.cameraType != CameraType.Game || camera.targetTexture != null)
                {
                    continue;
                }

                if (best == null || camera.depth > best.depth)
                {
                    best = camera;
                }
            }

            return best;
        }

        /// <summary>
        /// Switches off every active quantize feature and returns the ones it touched, so
        /// the caller can switch exactly those back on. Never marks anything dirty: this
        /// is a momentary state change, not an edit to the renderer assets.
        /// </summary>
        private static List<PastelQuantizeRenderFeature> SuppressQuantize()
        {
            var suppressed = new List<PastelQuantizeRenderFeature>();

            foreach (var renderer in VolumetricSetup.FindRenderers())
            {
                foreach (var feature in renderer.rendererFeatures)
                {
                    if (feature is PastelQuantizeRenderFeature pastel && pastel.isActive)
                    {
                        pastel.SetActive(false);
                        suppressed.Add(pastel);
                    }
                }
            }

            return suppressed;
        }

        /// <summary>
        /// Renders one frame into an sRGB target and reads it back as PNG bytes. The
        /// target is sRGB so the write encodes the linear frame exactly the way the
        /// swapchain would; the readback texture is *not* linear, so the bytes land in
        /// the PNG untouched and the browser decodes them back to the same linear values.
        /// </summary>
        private static byte[] Render(Camera camera, int width, int height)
        {
            RenderTexture target = RenderTexture.GetTemporary(
                width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var readback = new Texture2D(width, height, TextureFormat.RGBA32, false, false);

            try
            {
                camera.targetTexture = target;
                camera.Render();

                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                readback.Apply();

                return readback.EncodeToPNG();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        /// <summary>Scene name plus a two-digit counter, so repeated captures never
        /// overwrite each other and the folder stays free to rename.</summary>
        private static string NextPackDirectory()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(scene))
            {
                scene = "untitled";
            }

            for (int index = 1; index < 100; index++)
            {
                string candidate = Path.Combine(
                    ScenesDirectory, $"{scene}-{index.ToString("00", CultureInfo.InvariantCulture)}");
                if (!Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(
                $"[LookLab] 99 packs already exist for scene '{scene}'. Rename or delete some.");
        }

        private static PackInfo DescribePack(Camera camera, int width, int height)
        {
            return new PackInfo
            {
                capturedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                gitSha = ReadHeadSha(),
                unityVersion = Application.unityVersion,
                colorSpace = QualitySettings.activeColorSpace.ToString(),
                scene = SceneManager.GetActiveScene().name,
                width = width,
                height = height,
                cameraName = camera.name,
                position = camera.transform.position,
                eulerAngles = camera.transform.eulerAngles,
                fieldOfView = camera.fieldOfView,
                nearClipPlane = camera.nearClipPlane,
                farClipPlane = camera.farClipPlane,
                volumeProfiles = ActiveVolumeProfiles(),
            };
        }

        /// <summary>
        /// Which global volume profiles were live at capture. Everything upstream of the
        /// framebuffer — lighting, sky, materials, the Volume stack — is baked into the
        /// PNG and cannot be tuned in the lab, so recording it is the only way a pack
        /// that no longer matches the game becomes detectable instead of misleading.
        /// </summary>
        private static string[] ActiveVolumeProfiles()
        {
            var profiles = new List<string>();

            foreach (Volume volume in UnityEngine.Object.FindObjectsByType<Volume>(
                         FindObjectsSortMode.None))
            {
                if (!volume.isActiveAndEnabled || !volume.isGlobal || volume.sharedProfile == null)
                {
                    continue;
                }

                profiles.Add($"{AssetDatabase.GetAssetPath(volume.sharedProfile)}" +
                             $"@{volume.weight.ToString("0.###", CultureInfo.InvariantCulture)}");
            }

            profiles.Sort(StringComparer.Ordinal);
            return profiles.ToArray();
        }

        /// <summary>
        /// HEAD's commit, read from .git rather than shelled out to git — the Editor has
        /// no guarantee git is on PATH, and a capture is not worth failing over.
        /// </summary>
        private static string ReadHeadSha()
        {
            try
            {
                string gitDirectory = Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", ".git"));
                string head = File.ReadAllText(Path.Combine(gitDirectory, "HEAD")).Trim();

                if (!head.StartsWith("ref:", StringComparison.Ordinal))
                {
                    return head;
                }

                string reference = head.Substring(4).Trim();
                string looseRef = Path.Combine(
                    gitDirectory, reference.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(looseRef))
                {
                    return File.ReadAllText(looseRef).Trim();
                }

                // A ref that has been packed away has no loose file of its own.
                string packed = Path.Combine(gitDirectory, "packed-refs");
                if (File.Exists(packed))
                {
                    foreach (string line in File.ReadAllLines(packed))
                    {
                        if (line.EndsWith(" " + reference, StringComparison.Ordinal))
                        {
                            return line.Substring(0, line.IndexOf(' '));
                        }
                    }
                }

                return string.Empty;
            }
            catch (IOException exception)
            {
                Debug.LogWarning("[LookLab] Could not read the git SHA for pack.json: " +
                                 exception.Message);
                return string.Empty;
            }
        }

        [Serializable]
        private class PackInfo
        {
            public string capturedUtc;
            public string gitSha;
            public string unityVersion;
            public string colorSpace;
            public string scene;
            public int width;
            public int height;
            public string cameraName;
            public Vector3 position;
            public Vector3 eulerAngles;
            public float fieldOfView;
            public float nearClipPlane;
            public float farClipPlane;
            public string[] volumeProfiles;
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `python3 tools/typecheck.py --editor`
Expected: exits 0.

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Editor/Environment/LookLabCapture.cs
git commit -m "feat: capture Look Lab scene packs from the Game camera"
```

---

### Task 8: The server

**Files:**
- Create: `tools/looklab.py`
- Modify: `.gitignore`

- [ ] **Step 1: Write `tools/looklab.py`**

```python
#!/usr/bin/env python3
"""Serves the Look Lab and takes its writes.

The page and the Unity Editor never meet. The page POSTs to this server, which writes
LookLab/live/look.json atomically; LookLabLive.cs polls that file's timestamp and mutates
the live render feature. Two halves that never connect, so there is no socket in the
Editor to leak across a domain reload.

    python3 tools/looklab.py serve            # http://127.0.0.1:8777/app/
    python3 tools/looklab.py serve --port N

Bound to the loopback interface only: this writes files in the repository on an
unauthenticated request, which is fine for a local dev tool and not fine on a shared
network.
"""
import argparse
import json
import os
import pathlib
import re
import sys
import tempfile
from http.server import HTTPServer, SimpleHTTPRequestHandler
from urllib.parse import unquote

ROOT = pathlib.Path(__file__).resolve().parent.parent
LOOKLAB = ROOT / 'LookLab'
LIVE_LOOK = LOOKLAB / 'live' / 'look.json'
PRESETS = LOOKLAB / 'presets'
SCENES = LOOKLAB / 'scenes'
STAGES = LOOKLAB / 'stages'

# Preset names become filenames, so they are an allowlist rather than a sanitiser: a
# deny-list of traversal sequences is a fight you lose eventually.
NAME = re.compile(r'^[A-Za-z0-9][A-Za-z0-9 _-]{0,63}$')

MAX_BODY_BYTES = 256 * 1024

# Mirrors PaletteShape.Default in
# Assets/Game/Scripts/World/Environment/ColorGrade/PaletteShape.cs. Only used to write a
# valid look.json the first time; the lab reads its own defaults from the stage manifest.
DEFAULT_LOOK = {
    'enabled': True,
    'blend': 1.0,
    'palette': {
        'hueCount': 16,
        'lightnesses': [0.92, 0.82, 0.72, 0.61, 0.49, 0.36],
        'chromaFractions': [0.5, 1.0],
        'chromaCeiling': 0.20,
        'neutralCount': 12,
        'neutralMinL': 0.16,
        'neutralMaxL': 0.97,
    },
}


def write_atomically(path, text):
    """Writes via a tempfile in the same directory plus os.replace — a true rename, so a
    reader can never see a half-written file. LookLabLive polls at 20 Hz and would
    otherwise eventually catch one mid-write and log a parse error."""
    path.parent.mkdir(parents=True, exist_ok=True)
    handle = tempfile.NamedTemporaryFile(
        'w', dir=str(path.parent), prefix=path.name + '.', delete=False)
    try:
        with handle:
            handle.write(text)
        os.replace(handle.name, str(path))
    except BaseException:
        # Leaving a stray temp file beside look.json would confuse the next reader more
        # than the failure itself does.
        pathlib.Path(handle.name).unlink(missing_ok=True)
        raise


def list_scenes():
    """Every capture pack that actually has a frame in it."""
    scenes = []
    if not SCENES.is_dir():
        return scenes

    for directory in sorted(SCENES.iterdir()):
        if not (directory / 'color_ldr.png').is_file():
            continue
        pack = {}
        pack_path = directory / 'pack.json'
        if pack_path.is_file():
            try:
                pack = json.loads(pack_path.read_text())
            except json.JSONDecodeError as error:
                # Loud, not silent: a pack whose metadata is unreadable is exactly the
                # pack that will later be blamed for not matching the build.
                print('looklab: %s is not valid JSON (%s)' % (pack_path, error),
                      file=sys.stderr)
        scenes.append({'name': directory.name, 'pack': pack})

    return scenes


class Handler(SimpleHTTPRequestHandler):
    """Static files out of LookLab/, plus the handful of writes the lab needs."""

    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(LOOKLAB), **kwargs)

    def log_message(self, fmt, *args):
        # One line per slider drag would bury anything worth reading.
        if not self.path.startswith('/api/look'):
            super().log_message(fmt, *args)

    def do_GET(self):
        if self.path == '/':
            self.send_response(302)
            self.send_header('Location', '/app/')
            self.end_headers()
            return
        if self.path == '/api/scenes':
            self.send_json(list_scenes())
            return
        if self.path == '/api/stages':
            names = sorted(p.name for p in STAGES.glob('*.stage')) if STAGES.is_dir() else []
            self.send_json(names)
            return
        if self.path == '/api/presets':
            names = sorted(p.stem for p in PRESETS.glob('*.json')) if PRESETS.is_dir() else []
            self.send_json(names)
            return
        super().do_GET()

    def do_POST(self):
        if self.path != '/api/look':
            self.send_error(404)
            return
        body = self.read_json()
        if body is None:
            return
        write_atomically(LIVE_LOOK, json.dumps(body, indent=2))
        self.send_json({'written': str(LIVE_LOOK.relative_to(ROOT))})

    def do_PUT(self):
        name = self.preset_name()
        if name is None:
            return
        body = self.read_json()
        if body is None:
            return
        write_atomically(PRESETS / (name + '.json'), json.dumps(body, indent=2))
        self.send_json({'saved': name})

    def do_DELETE(self):
        name = self.preset_name()
        if name is None:
            return
        path = PRESETS / (name + '.json')
        if not path.is_file():
            self.send_error(404, 'no preset named ' + name)
            return
        path.unlink()
        self.send_json({'deleted': name})

    def preset_name(self):
        prefix = '/api/presets/'
        if not self.path.startswith(prefix):
            self.send_error(404)
            return None
        name = unquote(self.path[len(prefix):])
        if not NAME.match(name):
            self.send_error(400, 'preset names are letters, digits, space, - and _ '
                                 '(1-64 characters)')
            return None
        return name

    def read_json(self):
        try:
            length = int(self.headers.get('Content-Length', '0'))
        except ValueError:
            self.send_error(400, 'bad Content-Length')
            return None
        if length <= 0 or length > MAX_BODY_BYTES:
            self.send_error(400, 'body must be 1..%d bytes' % MAX_BODY_BYTES)
            return None
        try:
            return json.loads(self.rfile.read(length))
        except json.JSONDecodeError as error:
            self.send_error(400, 'not JSON: %s' % error)
            return None

    def send_json(self, payload):
        encoded = json.dumps(payload).encode()
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)


def serve(port):
    for directory in (LOOKLAB / 'live', PRESETS, SCENES, STAGES):
        directory.mkdir(parents=True, exist_ok=True)
    if not LIVE_LOOK.exists():
        write_atomically(LIVE_LOOK, json.dumps(DEFAULT_LOOK, indent=2))
        print('looklab: wrote a default %s' % LIVE_LOOK.relative_to(ROOT))

    if not list_scenes():
        print('looklab: no scene packs yet — capture some with '
              'SpaceGame > Look Lab > Capture Scene Pack')

    server = HTTPServer(('127.0.0.1', port), Handler)
    print('looklab: http://127.0.0.1:%d/app/  (ctrl-c to stop)' % port)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print()
    finally:
        server.server_close()
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='command')
    serve_parser = sub.add_parser('serve', help='serve the lab on localhost')
    serve_parser.add_argument('--port', type=int, default=8777)
    args = parser.parse_args()

    if args.command != 'serve':
        parser.print_help()
        return 2
    return serve(args.port)


if __name__ == '__main__':
    sys.exit(main())
```

- [ ] **Step 2: Verify it starts and answers**

In one terminal: `python3 tools/looklab.py serve`
Expected: prints `looklab: wrote a default LookLab/live/look.json`, the no-scene-packs
notice, and the URL.

In another terminal:

```bash
curl -s http://127.0.0.1:8777/api/scenes
curl -s http://127.0.0.1:8777/api/presets
```

Expected: `[]` from both.

- [ ] **Step 3: Verify the write path**

```bash
curl -s -X POST http://127.0.0.1:8777/api/look -H 'Content-Type: application/json' -d '{"enabled":true,"blend":0.5,"palette":{"hueCount":16,"lightnesses":[0.9],"chromaFractions":[1.0],"chromaCeiling":0.2,"neutralCount":8,"neutralMinL":0.16,"neutralMaxL":0.97}}'
cat LookLab/live/look.json
```

Expected: the POST returns `{"written": "LookLab/live/look.json"}` and the file holds that
JSON, pretty-printed.

- [ ] **Step 4: Verify the preset name guard**

```bash
curl -s -o /dev/null -w '%{http_code}\n' -X PUT 'http://127.0.0.1:8777/api/presets/..%2F..%2Fescape' -d '{}'
```

Expected: `400`.

Stop the server with ctrl-c.

- [ ] **Step 5: Ignore the ephemeral live file**

Append to `.gitignore`:

```gitignore
# Look Lab. The live look is ephemeral tuning state rewritten on every slider drag —
# tracking it would make every session a dirty working tree. tools/looklab.py writes a
# valid default on first run. The LDR scene packs ARE committed: they are the shared
# reference the whole team judges a look against.
LookLab/live/
```

- [ ] **Step 6: Untrack the spike's committed look file**

```bash
git rm --cached LookLab/live/look.json
git status --short LookLab
```

Expected: `LookLab/live/look.json` staged as deleted, and ignored afterwards.

- [ ] **Step 7: Commit**

```bash
git add tools/looklab.py .gitignore
git commit -m "feat: add the Look Lab server"
```

---

### Task 9: The JS palette port, checked against the Python one

**Files:**
- Create: `LookLab/app/package.json`
- Create: `LookLab/app/palette.js`
- Create: `tools/looklab_palette_dump.mjs`
- Modify: `tools/palette_preview.py`

- [ ] **Step 1: Declare the app directory as ES modules**

Create `LookLab/app/package.json`:

```json
{
  "type": "module",
  "private": true
}
```

This exists solely so Node resolves `palette.js` as an ES module when `--check` imports it.
There are no dependencies and nothing to install: browsers load these as modules regardless.

- [ ] **Step 2: Write `LookLab/app/palette.js`**

```js
// Port of PaletteShape.Default and PastelPalette.Build in
// Assets/Game/Scripts/World/Environment/ColorGrade/. Three ports of this lattice now
// exist — C# (ships), Python (checks), JS (this) — so `python3 tools/palette_preview.py
// --check` compares this one against the Python one entry for entry. Change one, the
// check fails until all three agree.

export const DEFAULT_SHAPE = Object.freeze({
  hueCount: 16,
  lightnesses: [0.92, 0.82, 0.72, 0.61, 0.49, 0.36],
  chromaFractions: [0.5, 1.0],
  chromaCeiling: 0.20,
  neutralCount: 12,
  neutralMinL: 0.16,
  neutralMaxL: 0.97,
});

const GAMUT_FIT_ITERATIONS = 16;
const GAMUT_EPSILON = 1e-4;

/** Oklch to linear RGB. Unclamped, so inGamut can see the overflow. */
function oklchToLinear(lightness, chroma, hueRadians) {
  const a = chroma * Math.cos(hueRadians);
  const b = chroma * Math.sin(hueRadians);
  const l = (lightness + 0.3963377774 * a + 0.2158037573 * b) ** 3;
  const m = (lightness - 0.1055613458 * a - 0.0638541728 * b) ** 3;
  const s = (lightness - 0.0894841775 * a - 1.2914855480 * b) ** 3;
  return [
    +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s,
  ];
}

function inGamut(lightness, chroma, hueRadians) {
  return oklchToLinear(lightness, chroma, hueRadians)
    .every((c) => c >= -GAMUT_EPSILON && c <= 1 + GAMUT_EPSILON);
}

/**
 * The largest chroma up to `chroma` that still lands inside sRGB. Clamping an
 * out-of-gamut colour instead shifts its hue and drops its lightness, so an authored
 * entry quietly comes out darker and dirtier than the ramp says.
 */
function fitChroma(lightness, chroma, hueRadians) {
  if (inGamut(lightness, chroma, hueRadians)) return chroma;
  let low = 0;
  let high = chroma;
  for (let i = 0; i < GAMUT_FIT_ITERATIONS; i++) {
    const mid = 0.5 * (low + high);
    if (inGamut(lightness, mid, hueRadians)) low = mid;
    else high = mid;
  }
  return low;
}

/**
 * Unity's Color.gamma (LinearToGammaSpace). Note the 0.41666 exponent — Unity's own
 * approximation, not 1/2.4. The palette entries in C# go through this, so this port has
 * to as well or the two disagree in the third decimal.
 */
export function linearToGamma(c) {
  if (c <= 0) return 0;
  if (c <= 0.0031308) return c * 12.92;
  if (c <= 1) return 1.055 * Math.pow(c, 0.41666) - 0.055;
  return Math.pow(c, 0.45454545);
}

/** Unity's Color.linear (GammaToLinearSpace). */
export function gammaToLinear(c) {
  if (c <= 0.04045) return c / 12.92;
  if (c < 1) return Math.pow((c + 0.055) / 1.055, 2.4);
  return Math.pow(c, 2.2);
}

/** Must stay in lockstep with PastelPalette.LinearToOklab and the HLSL shader. */
export function linearToOklab([r, g, b]) {
  let l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b;
  let m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b;
  let s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b;
  l = Math.cbrt(Math.max(l, 0));
  m = Math.cbrt(Math.max(m, 0));
  s = Math.cbrt(Math.max(s, 0));
  return [
    0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
    1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
    0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s,
  ];
}

function oklchToSrgb(lightness, chroma, hueRadians) {
  const fitted = fitChroma(lightness, chroma, hueRadians);
  return oklchToLinear(lightness, fitted, hueRadians)
    .map((c) => linearToGamma(Math.min(1, Math.max(0, c))));
}

/**
 * The palette in the order PastelPalette.Build emits it, as gamma-space sRGB in 0..1 —
 * the same values PastelPalette hands back as a Color[].
 */
export function buildPalette(shape) {
  const colors = [];

  for (let h = 0; h < shape.hueCount; h++) {
    const hue = (h * 2 * Math.PI) / shape.hueCount;
    for (const lightness of shape.lightnesses) {
      // fitChroma of the ceiling IS the in-gamut maximum.
      const ceiling = fitChroma(lightness, shape.chromaCeiling, hue);
      for (const fraction of shape.chromaFractions) {
        colors.push(oklchToSrgb(lightness, ceiling * fraction, hue));
      }
    }
  }

  for (let n = 0; n < shape.neutralCount; n++) {
    const t = n / (shape.neutralCount - 1);
    colors.push(
      oklchToSrgb(shape.neutralMinL + (shape.neutralMaxL - shape.neutralMinL) * t, 0, 0));
  }

  return colors;
}

export function hexOf([r, g, b]) {
  const byte = (c) => Math.round(c * 255).toString(16).toUpperCase().padStart(2, '0');
  return '#' + byte(r) + byte(g) + byte(b);
}

/**
 * The palette as a `width` x 2 RGBA32F payload: row 0 is the linear RGB written out,
 * row 1 the Oklab matched against — exactly the two arrays the HLSL shader uploads.
 *
 * The gamma round trip is deliberate and must not be "simplified" away. C# builds each
 * entry as a gamma-space Color and the render feature reads back `.linear`, so the value
 * the shader writes has been through Unity's two approximations. Skipping the trip here
 * would make the lab a fraction brighter than the game for no visible reason.
 */
export function paletteTexture(colors, width) {
  const data = new Float32Array(width * 2 * 4);
  colors.forEach((gamma, i) => {
    const linear = gamma.map(gammaToLinear);
    const oklab = linearToOklab(linear);
    data.set([linear[0], linear[1], linear[2], 1], i * 4);
    data.set([oklab[0], oklab[1], oklab[2], 1], (width + i) * 4);
  });
  return data;
}
```

- [ ] **Step 3: Write the Node dump entry point**

Create `tools/looklab_palette_dump.mjs`:

```js
#!/usr/bin/env node
// Prints the JS palette as one #RRGGBB per line, so tools/palette_preview.py --check can
// compare the browser port against the Python one. Lives in tools/ rather than in
// LookLab/app/ so the served app stays free of Node-only code.
import { DEFAULT_SHAPE, buildPalette, hexOf } from '../LookLab/app/palette.js';

for (const color of buildPalette(DEFAULT_SHAPE)) {
  console.log(hexOf(color));
}
```

- [ ] **Step 4: Verify the two ports agree, by hand first**

```bash
node tools/looklab_palette_dump.mjs | diff - tools/palette_golden.txt && echo IDENTICAL
```

Expected: `IDENTICAL`.

If entries differ by one in the last digit, the culprit is almost always `linearToGamma` —
check that the exponent is `0.41666` and not `1/2.4`.

- [ ] **Step 5: Fold the comparison into `--check`**

In `tools/palette_preview.py`, add next to `check_matches_golden`:

```python
JS_DUMP = os.path.join('tools', 'looklab_palette_dump.mjs')


def check_matches_js(repo_root, colors):
    """Compares the Look Lab's JS port against this one, entry for entry.

    Two ports of one lattice drift the moment either is touched, and the drift is
    invisible: the lab keeps rendering, just not the look the game ships. Skipped rather
    than failed when Node is absent, so the palette check stays runnable anywhere.
    """
    import subprocess

    dump = os.path.join(repo_root, JS_DUMP)
    if not os.path.exists(dump):
        return ['%s is missing; the Look Lab palette cannot be cross-checked' % JS_DUMP]

    try:
        result = subprocess.run(['node', dump], cwd=repo_root,
                                capture_output=True, text=True)
    except FileNotFoundError:
        print('note: node is not installed, skipping the Look Lab palette cross-check')
        return []

    if result.returncode != 0:
        return ['node %s failed:\n%s' % (JS_DUMP, result.stderr.strip())]

    theirs = [line.strip() for line in result.stdout.splitlines() if line.strip()]
    ours = [hex_of(color) for color in colors]
    if len(theirs) != len(ours):
        return ['the JS port builds %d colours, this one builds %d'
                % (len(theirs), len(ours))]

    return ['entry %d is %s here and %s in LookLab/app/palette.js' % (i, o, t)
            for i, (o, t) in enumerate(zip(ours, theirs)) if o != t]
```

Then extend the `problems` expression in `main()`:

```python
    problems = (check_mirrors_csharp(repo_root)
                + check_matches_golden(repo_root, colors)
                + check_matches_js(repo_root, colors)
                + check(colors))
```

- [ ] **Step 6: Verify**

Run: `python3 tools/palette_preview.py --check`
Expected: exits 0.

- [ ] **Step 7: Verify the cross-check bites**

Temporarily change `chromaCeiling: 0.20` to `0.21` in `LookLab/app/palette.js`, then run:

Run: `python3 tools/palette_preview.py --check`
Expected: FAIL with a list of `entry N is #... here and #... in LookLab/app/palette.js`.

Revert the edit and re-run; expected: exits 0.

- [ ] **Step 8: Commit**

```bash
git add LookLab/app/package.json LookLab/app/palette.js tools/looklab_palette_dump.mjs tools/palette_preview.py
git commit -m "feat: port the pastel lattice to JS, cross-checked against Python"
```

---

### Task 10: The stage manifest

**Files:**
- Create: `LookLab/stages/palette.snap.stage`

- [ ] **Step 1: Write the stage file**

The format is a JSON header, a line containing only `---`, then the GLSL body. Phase 1
ships exactly one of these; the format exists so that phase 3 can add stages without
writing UI code for each.

`LookLab/stages/palette.snap.stage`:

```
{
  "id": "palette.snap",
  "label": "Palette snap",
  "params": {
    "hueCount":        { "min": 4,    "max": 32,  "step": 1,     "default": 16, "integer": true, "rebuild": true },
    "lightnesses":     { "min": 0.02, "max": 1,   "step": 0.01,  "default": [0.92, 0.82, 0.72, 0.61, 0.49, 0.36], "list": true, "rebuild": true },
    "chromaFractions": { "min": 0,    "max": 1,   "step": 0.01,  "default": [0.5, 1], "list": true, "rebuild": true },
    "chromaCeiling":   { "min": 0,    "max": 0.4, "step": 0.005, "default": 0.2,  "rebuild": true },
    "neutralCount":    { "min": 2,    "max": 32,  "step": 1,     "default": 12, "integer": true, "rebuild": true },
    "neutralMinL":     { "min": 0,    "max": 1,   "step": 0.01,  "default": 0.16, "rebuild": true },
    "neutralMaxL":     { "min": 0,    "max": 1,   "step": 0.01,  "default": 0.97, "rebuild": true },
    "blend":           { "min": 0,    "max": 1,   "step": 0.01,  "default": 1 }
  },
  "requires": ["palette"]
}
---
// Nearest neighbour in Oklab, mirroring the frag body of
// Assets/Game/Art/Shaders/Environment/PastelQuantize.shader. `c` arrives linear.
//
// "rebuild" params above do not reach this body at all: they describe the lattice, which
// is built on the CPU and arrives as uPalette. Only `blend` is a uniform here.
vec3 apply(vec3 c, vec2 uv) {
  vec3 okl = linearToOklab(clamp(c, 0.0, 1.0));

  int best = 0;
  float bestDist = 1e10;
  for (int i = 0; i < uPaletteCount; i++) {
    vec3 d = okl - texelFetch(uPalette, ivec2(i, 1), 0).rgb;
    float dist = dot(d, d);
    if (dist < bestDist) {
      bestDist = dist;
      best = i;
    }
  }

  return mix(c, texelFetch(uPalette, ivec2(best, 0), 0).rgb, P_blend);
}
```

- [ ] **Step 2: Verify the header parses**

```bash
python3 -c "import json,pathlib; t=pathlib.Path('LookLab/stages/palette.snap.stage').read_text(); h,b=t.split(chr(10)+'---'+chr(10),1); d=json.loads(h); print(d['id'], sorted(d['params']), len(b), 'chars of GLSL')"
```

Expected: `palette.snap ['blend', 'chromaCeiling', 'chromaFractions', 'hueCount', 'lightnesses', 'neutralCount', 'neutralMaxL', 'neutralMinL'] NNN chars of GLSL`

- [ ] **Step 3: Commit**

```bash
git add LookLab/stages/palette.snap.stage
git commit -m "feat: add the palette snap stage manifest"
```

---

### Task 11: The WebGL2 renderer

**Files:**
- Create: `LookLab/app/gl.js`

- [ ] **Step 1: Write `LookLab/app/gl.js`**

```js
// WebGL2 for the Look Lab: one context, one program, one viewport draw per panel.
//
// One canvas rather than one per scene, because a browser caps live WebGL contexts and
// six panels plus a pinned A/B comparison would sit near that cap. Panels are drawn into
// their own viewport rects on the single canvas instead.
//
// Nothing here runs on a timer: these are stills, so the app calls draw() when something
// changed and an idle stack costs nothing.

const VERTEX_SOURCE = `#version 300 es
// A fullscreen triangle from gl_VertexID — no vertex buffer, no attribute state.
out vec2 vUv;
void main() {
  vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
  vUv = p;
  gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;

const FRAGMENT_PRELUDE = `#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

in vec2 vUv;
out vec4 fragColor;

uniform sampler2D uSource;
uniform sampler2D uPalette;   // column i: row 0 linear RGB, row 1 Oklab
uniform int uPaletteCount;

// Must stay in lockstep with PastelPalette.LinearToOklab and the HLSL shader.
vec3 linearToOklab(vec3 c) {
  vec3 lms = vec3(
    dot(c, vec3(0.4122214708, 0.5363325363, 0.0514459929)),
    dot(c, vec3(0.2119034982, 0.6806995451, 0.1073969566)),
    dot(c, vec3(0.0883024619, 0.2817188376, 0.6299787005)));
  lms = pow(max(lms, 0.0), vec3(1.0 / 3.0));
  return vec3(
    dot(lms, vec3(0.2104542553,  0.7936177850, -0.0040720468)),
    dot(lms, vec3(1.9779984951, -2.4285922050,  0.4505937099)),
    dot(lms, vec3(0.0259040371,  0.7827717662, -0.8086757660)));
}

// The canvas is untagged, so the page encodes on the way out the same way the swapchain
// does for the game. The standard exponent, not Unity's 0.41666 approximation: this is
// display encoding, not a palette value.
vec3 linearToSrgb(vec3 c) {
  c = clamp(c, 0.0, 1.0);
  return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c));
}
`;

const FRAGMENT_MAIN = `
void main() {
  // uSource is SRGB8_ALPHA8, so the sample is already linear — the exact inverse of the
  // sRGB encode Unity's capture render target applied.
  vec4 source = texture(uSource, vUv);
  fragColor = vec4(linearToSrgb(apply(source.rgb, vUv)), source.a);
}
`;

export class LabRenderer {
  constructor(canvas) {
    this.gl = canvas.getContext('webgl2', {
      alpha: false,
      antialias: false,
      preserveDrawingBuffer: true, // so the parity harness in phase 2 can read the canvas
    });
    if (!this.gl) throw new Error('This browser has no WebGL2. The lab needs it.');
    this.canvas = canvas;
    this.program = null;
    this.uniforms = new Map();
    this.paletteTexture = null;
    this.paletteCount = 0;
    this.sources = new Map();
    this.vao = this.gl.createVertexArray(); // WebGL2 refuses to draw with no VAO bound
  }

  /** Compiles the stage bodies into one fragment shader. Milliseconds; a slider drag
   *  does not come through here, it only sets a uniform. */
  compile(stageBodies, paramNames) {
    const gl = this.gl;
    const declarations = paramNames.map((n) => `uniform float P_${n};`).join('\n');
    const source = FRAGMENT_PRELUDE + declarations + '\n' + stageBodies.join('\n') + FRAGMENT_MAIN;

    const program = link(gl, VERTEX_SOURCE, source);
    if (this.program) gl.deleteProgram(this.program);
    this.program = program;

    this.uniforms.clear();
    const count = gl.getProgramParameter(program, gl.ACTIVE_UNIFORMS);
    for (let i = 0; i < count; i++) {
      const name = gl.getActiveUniform(program, i).name.replace(/\[0\]$/, '');
      this.uniforms.set(name, gl.getUniformLocation(program, name));
    }
  }

  /** Uploads one captured still. Kept by name so switching panels costs nothing. */
  setSource(name, image) {
    const gl = this.gl;
    let texture = this.sources.get(name);
    if (!texture) {
      texture = gl.createTexture();
      this.sources.set(name, texture);
    }
    gl.bindTexture(gl.TEXTURE_2D, texture);
    // SRGB8_ALPHA8 so the hardware decode is the exact inverse of the sRGB encode the
    // capture applied. Doing it in GLSL instead would be a second approximation.
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.SRGB8_ALPHA8, gl.RGBA, gl.UNSIGNED_BYTE, image);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  }

  /** `data` is the width x 2 RGBA32F payload from palette.js. */
  setPalette(data, width, count) {
    const gl = this.gl;
    if (!this.paletteTexture) this.paletteTexture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, this.paletteTexture);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA32F, width, 2, 0, gl.RGBA, gl.FLOAT, data);
    // NEAREST throughout: entries are looked up by index with texelFetch, and filtering
    // between two palette entries would smear exactly the hard edges the snap creates.
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    this.paletteCount = count;
  }

  /** `panels` is [{ scene, x, y, width, height }] in CSS pixels from the top left. */
  draw(panels, params) {
    const gl = this.gl;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const width = Math.round(this.canvas.clientWidth * dpr);
    const height = Math.round(this.canvas.clientHeight * dpr);
    if (this.canvas.width !== width || this.canvas.height !== height) {
      this.canvas.width = width;
      this.canvas.height = height;
    }

    gl.bindVertexArray(this.vao);
    gl.useProgram(this.program);
    gl.disable(gl.DEPTH_TEST);
    gl.disable(gl.BLEND);
    gl.viewport(0, 0, width, height);
    gl.clearColor(0, 0, 0, 1);
    gl.clear(gl.COLOR_BUFFER_BIT);

    gl.activeTexture(gl.TEXTURE1);
    gl.bindTexture(gl.TEXTURE_2D, this.paletteTexture);
    this.setUniform('uPalette', (l) => gl.uniform1i(l, 1));
    this.setUniform('uPaletteCount', (l) => gl.uniform1i(l, this.paletteCount));
    this.setUniform('uSource', (l) => gl.uniform1i(l, 0));

    for (const [name, value] of Object.entries(params)) {
      this.setUniform(`P_${name}`, (l) => gl.uniform1f(l, value));
    }

    for (const panel of panels) {
      const texture = this.sources.get(panel.scene);
      if (!texture) continue;
      gl.activeTexture(gl.TEXTURE0);
      gl.bindTexture(gl.TEXTURE_2D, texture);
      // GL's origin is bottom-left; the panel rects are top-left like the DOM.
      gl.viewport(
        Math.round(panel.x * dpr),
        height - Math.round((panel.y + panel.height) * dpr),
        Math.round(panel.width * dpr),
        Math.round(panel.height * dpr));
      gl.drawArrays(gl.TRIANGLES, 0, 3);
    }
  }

  setUniform(name, set) {
    const location = this.uniforms.get(name);
    // Absent is normal, not an error: GLSL drops any uniform the stage body never reads.
    if (location !== undefined && location !== null) set(location);
  }
}

function link(gl, vertexSource, fragmentSource) {
  const program = gl.createProgram();
  const vertex = compileShader(gl, gl.VERTEX_SHADER, vertexSource);
  const fragment = compileShader(gl, gl.FRAGMENT_SHADER, fragmentSource);
  gl.attachShader(program, vertex);
  gl.attachShader(program, fragment);
  gl.linkProgram(program);
  gl.deleteShader(vertex);
  gl.deleteShader(fragment);
  if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
    const log = gl.getProgramInfoLog(program);
    gl.deleteProgram(program);
    throw new Error('Link failed: ' + log);
  }
  return program;
}

function compileShader(gl, type, source) {
  const shader = gl.createShader(type);
  gl.shaderSource(shader, source);
  gl.compileShader(shader);
  if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
    // Numbered, because a stage body is concatenated into a prelude and the raw line
    // number in the driver's message means nothing on its own.
    const numbered = source.split('\n').map((l, i) => `${i + 1}: ${l}`).join('\n');
    const log = gl.getShaderInfoLog(shader);
    gl.deleteShader(shader);
    throw new Error(`Compile failed: ${log}\n${numbered}`);
  }
  return shader;
}
```

- [ ] **Step 2: Commit**

```bash
git add LookLab/app/gl.js
git commit -m "feat: add the Look Lab WebGL2 renderer"
```

---

### Task 12: The lab shell

**Files:**
- Create: `LookLab/app/index.html`
- Create: `LookLab/app/lab.js`

- [ ] **Step 1: Write `LookLab/app/index.html`**

```html
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Look Lab</title>
<style>
  :root {
    color-scheme: dark;
    --bg: #14161a; --panel: #1c1f25; --line: #2c313a;
    --ink: #e6e8ec; --dim: #9aa2b1; --accent: #7fb4ff;
  }
  * { box-sizing: border-box; }
  body { margin: 0; height: 100vh; display: grid; grid-template-columns: 1fr 320px;
         background: var(--bg); color: var(--ink);
         font: 13px/1.45 ui-sans-serif, system-ui, sans-serif; }
  main { display: flex; flex-direction: column; min-width: 0; }
  #view { flex: 1; min-height: 0; position: relative; }
  #canvas { width: 100%; height: 100%; display: block; }
  #labels { position: absolute; inset: 0; pointer-events: none; }
  #labels span { position: absolute; padding: 2px 6px; background: #000a; border-radius: 3px;
                 font-size: 11px; color: var(--dim); }
  #tabs { display: flex; gap: 4px; padding: 8px; border-bottom: 1px solid var(--line);
          flex-wrap: wrap; align-items: center; }
  aside { border-left: 1px solid var(--line); overflow-y: auto; padding: 12px; }
  h2 { font-size: 11px; text-transform: uppercase; letter-spacing: .08em;
       color: var(--dim); margin: 18px 0 8px; }
  h2:first-child { margin-top: 0; }
  button { background: var(--panel); color: var(--ink); border: 1px solid var(--line);
           border-radius: 4px; padding: 4px 9px; font: inherit; cursor: pointer; }
  button:hover { border-color: var(--accent); }
  button[aria-pressed="true"] { border-color: var(--accent); color: var(--accent); }
  .row { display: grid; grid-template-columns: 1fr 52px; gap: 8px; align-items: center;
         margin-bottom: 6px; }
  .row label { color: var(--dim); grid-column: 1 / -1; margin-bottom: -2px; }
  input[type=range] { width: 100%; accent-color: var(--accent); }
  output { text-align: right; font-variant-numeric: tabular-nums; color: var(--dim); }
  .list-controls { display: flex; gap: 4px; margin: 2px 0 10px; }
  #presets { display: flex; flex-wrap: wrap; gap: 4px; }
  #status { padding: 6px 8px; border-top: 1px solid var(--line); color: var(--dim);
            font-size: 11px; min-height: 26px; }
  #status.error { color: #ff9d9d; }
  kbd { background: var(--panel); border: 1px solid var(--line); border-radius: 3px;
        padding: 0 4px; }
</style>
</head>
<body>
  <main>
    <div id="tabs"></div>
    <div id="view"><canvas id="canvas"></canvas><div id="labels"></div></div>
    <div id="status">Hold <kbd>B</kbd> to flicker against the pinned preset.</div>
  </main>
  <aside>
    <h2>Presets</h2>
    <div id="presets"></div>
    <div class="list-controls">
      <button id="fork">Save as…</button>
      <button id="pin">Pin for A/B</button>
      <button id="delete">Delete</button>
    </div>
    <div id="stack"></div>
  </aside>
  <script type="module" src="./lab.js"></script>
</body>
</html>
```

- [ ] **Step 2: Write `LookLab/app/lab.js`**

```js
// The Look Lab shell: scene panels, manifest-driven sliders, presets, A/B flicker, and
// the POST that drives the running Editor.
//
// The sliders are built from the stage manifests' `params` blocks, not hand-written. That
// is what stops a lab that covers "everything downstream of the framebuffer" turning into
// a sprawl of bespoke panels — adding a stage file is enough to get its controls.

import { LabRenderer } from './gl.js';
import { buildPalette, paletteTexture } from './palette.js';

const PALETTE_WIDTH = 256; // must equal MaxPaletteSize in PastelQuantizeRenderFeature.cs
const GRID_COLUMNS = 3;
const MAX_PANELS = 6;      // six-up: as many as fit before a panel is too small to judge

const canvas = document.getElementById('canvas');
const labels = document.getElementById('labels');
const tabs = document.getElementById('tabs');
const stack = document.getElementById('stack');
const presetBar = document.getElementById('presets');
const status = document.getElementById('status');

const renderer = new LabRenderer(canvas);

const state = {
  stages: [],        // [{ id, label, params, body }]
  scenes: [],        // [{ name, pack }]
  values: {},        // param id -> number | number[]
  presets: [],       // names
  current: null,     // preset name, or null for an unsaved look
  pinned: null,      // { name, values } for A/B flicker
  flicker: false,
  gridMode: true,
  activeScene: null,
};

function say(message, isError = false) {
  status.textContent = message;
  status.classList.toggle('error', isError);
}

// --- loading -----------------------------------------------------------------

async function parseStage(name) {
  const text = await (await fetch(`../stages/${name}`)).text();
  const split = text.indexOf('\n---\n');
  if (split < 0) throw new Error(`${name}: no '---' line between the header and the GLSL`);
  const header = JSON.parse(text.slice(0, split));
  return { ...header, body: text.slice(split + 5) };
}

async function load() {
  const [stageNames, scenes, presets] = await Promise.all([
    fetch('/api/stages').then((r) => r.json()),
    fetch('/api/scenes').then((r) => r.json()),
    fetch('/api/presets').then((r) => r.json()),
  ]);

  state.stages = await Promise.all(stageNames.map(parseStage));
  state.scenes = scenes;
  state.presets = presets;

  for (const stage of state.stages) {
    for (const [id, spec] of Object.entries(stage.params)) {
      state.values[id] = Array.isArray(spec.default) ? [...spec.default] : spec.default;
    }
  }

  renderer.compile(state.stages.map((s) => s.body), scalarParamNames());

  await Promise.all(state.scenes.map(async (scene) => {
    const image = await loadImage(`../scenes/${encodeURIComponent(scene.name)}/color_ldr.png`);
    renderer.setSource(scene.name, image);
  }));

  state.activeScene = state.scenes[0]?.name ?? null;
  buildTabs();
  buildControls();
  buildPresetBar();
  pendingRebuild = true;
  requestDraw();

  if (!state.scenes.length) {
    say('No scene packs yet — capture some with SpaceGame > Look Lab > Capture Scene Pack.',
        true);
  } else {
    say(`${state.scenes.length} scene(s), ${state.stages.length} stage(s). ` +
        'Hold B to flicker against the pinned preset.');
  }
}

function loadImage(url) {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error('could not load ' + url));
    image.src = url;
  });
}

/** Only scalars become uniforms; list and `rebuild` params drive the CPU palette build. */
function scalarParamNames() {
  const names = [];
  for (const stage of state.stages) {
    for (const [id, spec] of Object.entries(stage.params)) {
      if (!spec.list && !spec.rebuild) names.push(id);
    }
  }
  return names;
}

// --- palette -----------------------------------------------------------------

function shapeFromValues(values) {
  return {
    hueCount: Math.round(values.hueCount),
    lightnesses: values.lightnesses,
    chromaFractions: values.chromaFractions,
    chromaCeiling: values.chromaCeiling,
    neutralCount: Math.round(values.neutralCount),
    neutralMinL: values.neutralMinL,
    neutralMaxL: values.neutralMaxL,
  };
}

function rebuildPalette(values) {
  const colors = buildPalette(shapeFromValues(values));
  if (colors.length > PALETTE_WIDTH) {
    say(`That lattice builds ${colors.length} colours; the shader holds ${PALETTE_WIDTH}.`,
        true);
    return false;
  }
  renderer.setPalette(paletteTexture(colors, PALETTE_WIDTH), PALETTE_WIDTH, colors.length);
  return true;
}

// --- UI ----------------------------------------------------------------------

function buildTabs() {
  tabs.replaceChildren();
  const grid = button(`Grid (${Math.min(state.scenes.length, MAX_PANELS)})`, () => {
    state.gridMode = true;
    buildTabs();
    requestDraw();
  });
  grid.setAttribute('aria-pressed', String(state.gridMode));
  tabs.append(grid);

  state.scenes.forEach((scene) => {
    const tab = button(scene.name, () => {
      state.gridMode = false;
      state.activeScene = scene.name;
      buildTabs();
      requestDraw();
    });
    tab.setAttribute('aria-pressed',
      String(!state.gridMode && state.activeScene === scene.name));
    tab.title = describePack(scene.pack);
    tabs.append(tab);
  });
}

function describePack(pack) {
  if (!pack || !pack.capturedUtc) return 'no pack.json — provenance unknown';
  return [`captured ${pack.capturedUtc}`, `${pack.width}x${pack.height}`,
          `scene ${pack.scene}`, `git ${(pack.gitSha || '?').slice(0, 8)}`,
          `volumes: ${(pack.volumeProfiles || []).join(', ') || 'none'}`].join('\n');
}

function buildControls() {
  stack.replaceChildren();
  for (const stage of state.stages) {
    const heading = document.createElement('h2');
    heading.textContent = stage.label;
    stack.append(heading);
    for (const [id, spec] of Object.entries(stage.params)) {
      stack.append(spec.list
        ? listControl(id, spec)
        : sliderControl(spec, () => state.values[id], (v) => { state.values[id] = v; }, id));
    }
  }
}

function sliderControl(spec, get, set, labelText) {
  const row = document.createElement('div');
  row.className = 'row';
  const label = document.createElement('label');
  label.textContent = labelText;
  const range = document.createElement('input');
  range.type = 'range';
  range.min = spec.min;
  range.max = spec.max;
  range.step = spec.step ?? 0.01;
  range.value = get();
  range.id = 'p-' + labelText.replace(/\W/g, '-');
  label.htmlFor = range.id;
  const readout = document.createElement('output');
  readout.textContent = format(get(), spec);
  range.addEventListener('input', () => {
    set(spec.integer ? Math.round(+range.value) : +range.value);
    readout.textContent = format(get(), spec);
    onValuesChanged(spec.rebuild || spec.list);
  });
  row.append(label, range, readout);
  return row;
}

function format(value, spec) {
  return spec.integer ? String(value) : (+value).toFixed(2);
}

/** A list param gets one slider per entry plus add/remove, so the number of lightness
 *  steps is itself tunable without a bespoke panel. */
function listControl(id, spec) {
  const wrapper = document.createElement('div');
  const redraw = () => {
    wrapper.replaceChildren();
    state.values[id].forEach((_, index) => {
      wrapper.append(sliderControl(spec,
        () => state.values[id][index],
        (v) => { state.values[id][index] = v; },
        `${id}[${index}]`));
    });
    const controls = document.createElement('div');
    controls.className = 'list-controls';
    controls.append(
      button('+', () => {
        const list = state.values[id];
        list.push(list.length ? list[list.length - 1] : spec.min);
        redraw();
        onValuesChanged(true);
      }),
      button('−', () => {
        if (state.values[id].length <= 1) return;
        state.values[id].pop();
        redraw();
        onValuesChanged(true);
      }));
    wrapper.append(controls);
  };
  redraw();
  return wrapper;
}

function button(text, onClick) {
  const element = document.createElement('button');
  element.textContent = text;
  element.addEventListener('click', onClick);
  return element;
}

function buildPresetBar() {
  presetBar.replaceChildren();
  for (const name of state.presets) {
    const element = button(state.pinned?.name === name ? '📌 ' + name : name,
                           () => selectPreset(name));
    element.setAttribute('aria-pressed', String(state.current === name));
    presetBar.append(element);
  }
  if (!state.presets.length) {
    const empty = document.createElement('span');
    empty.textContent = 'none yet';
    empty.style.color = 'var(--dim)';
    presetBar.append(empty);
  }
}

// --- presets -----------------------------------------------------------------

async function selectPreset(name) {
  state.values = await fetch(`../presets/${encodeURIComponent(name)}.json`)
    .then((r) => r.json());
  buildControls();
  onValuesChanged(true);
  state.current = name; // onValuesChanged clears it; a load is not an edit
  buildPresetBar();
}

document.getElementById('fork').addEventListener('click', async () => {
  const name = prompt('Preset name', state.current ?? 'untitled');
  if (!name) return;
  const response = await fetch(`/api/presets/${encodeURIComponent(name)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(state.values),
  });
  if (!response.ok) return say(await response.text(), true);
  if (!state.presets.includes(name)) state.presets.push(name);
  state.presets.sort();
  state.current = name;
  buildPresetBar();
  say(`Saved preset "${name}".`);
});

document.getElementById('pin').addEventListener('click', () => {
  state.pinned = {
    name: state.current ?? 'unsaved',
    values: structuredClone(state.values),
  };
  buildPresetBar();
  say(`Pinned "${state.pinned.name}". Hold B to flicker against it.`);
});

document.getElementById('delete').addEventListener('click', async () => {
  if (!state.current) return say('No preset selected.', true);
  const name = state.current;
  const response = await fetch(`/api/presets/${encodeURIComponent(name)}`,
                               { method: 'DELETE' });
  if (!response.ok) return say(await response.text(), true);
  state.presets = state.presets.filter((p) => p !== name);
  state.current = null;
  buildPresetBar();
  say(`Deleted preset "${name}".`);
});

// --- A/B flicker -------------------------------------------------------------
//
// A held key rather than side by side: the eye detects change far better than it detects
// difference, so flicker resolves a shift two panels apart would hide.

window.addEventListener('keydown', (event) => {
  if (event.key.toLowerCase() !== 'b' || event.repeat || !state.pinned) return;
  // instanceof first: a keydown's target is not always an Element (it is the document or
  // the window when nothing is focused, and for any programmatic dispatch), and calling
  // .matches on those throws inside the listener — which silently kills the flicker.
  if (event.target instanceof Element && event.target.matches('input, textarea')) return;
  state.flicker = true;
  pendingRebuild = true;
  requestDraw();
});

window.addEventListener('keyup', (event) => {
  if (event.key.toLowerCase() !== 'b' || !state.flicker) return;
  state.flicker = false;
  pendingRebuild = true;
  requestDraw();
});

// --- drawing and the bridge --------------------------------------------------

let queued = false;
let pendingRebuild = false;

function onValuesChanged(needsRebuild) {
  pendingRebuild = pendingRebuild || Boolean(needsRebuild);
  state.current = null; // an edited preset is an unsaved look until it is saved again
  requestDraw();
  pushLook();
}

/** Panels repaint on change, not at 60fps — these are stills, so an idle stack is free. */
function requestDraw() {
  if (queued) return;
  queued = true;
  requestAnimationFrame(() => {
    queued = false;
    const values = state.flicker && state.pinned ? state.pinned.values : state.values;
    if (pendingRebuild) {
      pendingRebuild = false;
      if (!rebuildPalette(values)) return;
    }
    renderer.draw(layout(), uniformsFrom(values));
    drawLabels();
  });
}

function uniformsFrom(values) {
  const uniforms = {};
  for (const name of scalarParamNames()) uniforms[name] = values[name];
  return uniforms;
}

/** Panel rects in CSS pixels. Each panel is drawn at its *displayed* size, which is what
 *  keeps a 204-entry nearest-neighbour search affordable across six of them. */
function layout() {
  const width = canvas.clientWidth;
  const height = canvas.clientHeight;
  if (!state.gridMode) {
    return state.activeScene
      ? [{ scene: state.activeScene, x: 0, y: 0, width, height }]
      : [];
  }
  const names = state.scenes.slice(0, MAX_PANELS).map((s) => s.name);
  const rows = Math.max(1, Math.ceil(names.length / GRID_COLUMNS));
  const cellWidth = width / GRID_COLUMNS;
  const cellHeight = height / rows;
  return names.map((scene, i) => ({
    scene,
    x: (i % GRID_COLUMNS) * cellWidth,
    y: Math.floor(i / GRID_COLUMNS) * cellHeight,
    width: cellWidth,
    height: cellHeight,
  }));
}

function drawLabels() {
  labels.replaceChildren();
  for (const panel of layout()) {
    const span = document.createElement('span');
    span.textContent = state.flicker ? `${panel.scene} — pinned` : panel.scene;
    span.style.left = `${panel.x + 6}px`;
    span.style.top = `${panel.y + 6}px`;
    labels.append(span);
  }
}

/** Drives the running Editor. Coalesced to one in-flight request: a slider drag fires
 *  faster than the round trip, and queueing them would make the Editor lag behind the
 *  page by an ever-growing backlog. */
let inFlight = false;
let sendAgain = false;

async function pushLook() {
  if (inFlight) { sendAgain = true; return; }
  inFlight = true;
  try {
    const response = await fetch('/api/look', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        enabled: true,
        blend: state.values.blend,
        palette: shapeFromValues(state.values),
      }),
    });
    if (!response.ok) say('Bridge write failed: ' + await response.text(), true);
  } catch (error) {
    say('Bridge unreachable: ' + error.message, true);
  } finally {
    inFlight = false;
    if (sendAgain) { sendAgain = false; pushLook(); }
  }
}

window.addEventListener('resize', requestDraw);

load().catch((error) => say(error.message, true));
```

- [ ] **Step 3: Verify the lab loads with no scene packs**

Start the server: `python3 tools/looklab.py serve`

Open `http://127.0.0.1:8777/app/`. Expected: the sidebar shows the **Palette snap**
heading with sliders for `hueCount`, six `lightnesses[N]` rows, two `chromaFractions[N]`
rows, `chromaCeiling`, `neutralCount`, `neutralMinL`, `neutralMaxL` and `blend`. The status
bar reads "No scene packs yet". The browser console is clean — **any error here is a shader
compile or a fetch path bug; fix it now, not after the packs exist.**

- [ ] **Step 4: Verify the bridge write fires**

Drag the `chromaCeiling` slider, then in a terminal:

```bash
cat LookLab/live/look.json
```

Expected: `chromaCeiling` in the file matches the slider.

- [ ] **Step 5: Commit**

```bash
git add LookLab/app/index.html LookLab/app/lab.js
git commit -m "feat: add the Look Lab shell"
```

---

### Task 13: Documentation

**Files:**
- Create: `docs/AI/systems/LookLab.md`
- Modify: `docs/AI/systems/Environment.md`
- Modify: `docs/Human/the-systems.md`

- [ ] **Step 1: Write `docs/AI/systems/LookLab.md`**

````markdown
---
system: LookLab
layer: tools
summary: Editor-only instrument for tuning the screen look — captured stills repainted in a browser, driving the running Editor over a file bridge
paths:
  - LookLab/
  - tools/looklab.py
  - tools/looklab_palette_dump.mjs
  - tools/palette_preview.py
  - Assets/Game/Editor/Environment/LookLabLive.cs
  - Assets/Game/Editor/Environment/LookLabCapture.cs
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
| Capture | LookLabCapture.cs | Renders the Game camera to `LookLab/scenes/<name>/color_ldr.png` plus a `pack.json` of provenance |
| The lab | `LookLab/app/` | Static ES modules; WebGL2 reimplementation of the stages; repaints on change |
| The bridge | [looklab.py](tools/looklab.py) writes, LookLabLive.cs polls | The page POSTs a look; the server writes `LookLab/live/look.json` atomically; the Editor polls its timestamp at 20 Hz |

Nothing connects to Unity. There is no socket in the Editor to leak across a domain reload.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `PaletteShape` | [PaletteShape.cs](Assets/Game/Scripts/World/Environment/ColorGrade/PaletteShape.cs) | The lattice as data — hue count, lightness steps, chroma fractions and ceiling, neutral count and range. `Default` holds the one committed look. Never serialized on the renderer assets |
| `PastelPalette.Build(in PaletteShape)` | [PastelPalette.cs](Assets/Game/Scripts/World/Environment/ColorGrade/PastelPalette.cs) | Builds the 204 colours from a shape. The colour math; the numbers live in `PaletteShape` |
| `PastelQuantizePass.EnsurePalette` | [PastelQuantizeRenderFeature.cs](Assets/Game/Scripts/World/Environment/ColorGrade/PastelQuantizeRenderFeature.cs) | Rebuilds and re-uploads the palette when the shape changed. Called from `AddRenderPasses` |
| `LookLabLive` | LookLabLive.cs | Polls `look.json`, validates the shape, pushes it to every installed feature. Toggled by `SpaceGame ▸ Look Lab ▸ Live Bridge` |
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
code. Phase 1 ships one stage, `palette.snap`.

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
- **Node is optional, but without it one check goes quiet.** `--check` skips the JS
  comparison when `node` is absent and says so; it does not fail.

## Extending

Adding a stage (phase 3): drop a `.stage` file in `LookLab/stages/`. Sliders appear with no
UI code. Pure functions of `(uv, colour, depth, normal)` are inlined into the main fragment
pass by concatenation; anything needing its own framebuffer waits for the pass graph.

Do not serialize `PaletteShape` on the renderer assets. One committed look, in C#, is the
constraint that stopped the deleted ten-style `PastelStyleLibrary` being rebuilt with a nicer
UI.
````

- [ ] **Step 2: Update `docs/AI/systems/Environment.md`**

Three edits.

**(a)** In the `symptoms:` list, add two entries after the existing quantizer ones:

```yaml
  - "a palette parameter changes in the lab and nothing on screen changes"
  - "the screen look reverts to the committed one after a script change"
```

**(b)** In the row for `PastelQuantizeRenderFeature`, replace the clause

> built in code by [PastelPalette.cs](Assets/Game/Scripts/World/Environment/ColorGrade/PastelPalette.cs) and **not serialized** on the renderer asset, so the PC and mobile renderers cannot drift apart.

with

> built by `PastelPalette.Build` from a [PaletteShape](Assets/Game/Scripts/World/Environment/ColorGrade/PaletteShape.cs) and **not serialized** on the renderer asset, so the PC and mobile renderers cannot drift apart. The shape is `[NonSerialized]`, defaults to `PaletteShape.Default` and is rebuilt by `EnsurePalette` whenever it changes — which is what lets the editor-only [Look Lab](docs/AI/systems/LookLab.md) bridge retune the palette live.

and replace the final clause

> `blend` on the renderer asset is the only knob, and 0 skips the pass entirely

with

> `blend` on the renderer asset is the only *serialized* knob, and 0 skips the pass entirely; the palette shape is tunable at runtime only through the Look Lab bridge, in memory, and a domain reload restores the committed one

**(c)** Add to the `## Gotchas` section:

```markdown
- **A palette parameter used to be able to change nothing, silently.** The palette was built
  once in `PastelQuantizePass`'s constructor, so a shape set after that — which is what the
  Look Lab bridge does — was never read again: no error, no effect. `EnsurePalette` rebuilds
  on change; `blend` hid the problem because scalars are pushed to the material every frame.
- **The committed look is guarded by `tools/palette_golden.txt`.**
  `python3 tools/palette_preview.py --check` compares the built palette against it entry for
  entry, and against the JS port in `LookLab/app/palette.js`. Retuning `PaletteShape.Default`
  means regenerating the golden file in the same commit — deliberately, so a look never
  changes by accident.
```

Finally bump `updated:` to `2026-09-07`.

- [ ] **Step 3: Add the plain-language entry**

In `docs/Human/the-systems.md`, immediately after the `### Weather, fog and sky
*(Environment)*` section, add:

```markdown
### Tuning how the game looks *(LookLab)*

The game's colours come from a fixed palette — every pixel on screen snaps to the nearest of
204 colours, which is what gives the flat, poster-like look. Deciding what those 204 colours
should be used to mean editing code and waiting minutes to see the result, one place in the
world at a time.

The Look Lab is a photographer's contact sheet for that decision. Screenshots taken around
the world sit side by side in a browser, and moving a slider repaints all of them at once —
and the running game with them. Hold a key and it flickers between what you have now and the
version you saved earlier, because the eye is far better at spotting a change than at
comparing two things side by side.

It is a workshop, not a wardrobe. You can try as many looks as you like in it, but the game
still ships with exactly one, written into the code — which is what keeps the palette from
quietly drifting into a dozen half-finished variants.
```

- [ ] **Step 4: Regenerate and validate the docs**

Run: `python3 tools/docs_check.py --index`
Expected: exits 0, and reports `INDEX.md` and `ROUTING.md` regenerated.

If it fails on a missing `docs/Human/the-systems.md` entry, the heading in Step 3 does not
match the `system:` name — it must contain `LookLab`.

- [ ] **Step 5: Commit**

```bash
git add docs/AI/systems/LookLab.md docs/AI/systems/Environment.md docs/AI/INDEX.md docs/AI/ROUTING.md docs/Human/the-systems.md
git commit -m "docs: document the Look Lab and PaletteShape"
```

---

### Task 14: The one Editor visit

Everything up to here was verified on disk. This is the only task that needs Unity, and it is
what "done" means for phase 1.

**Files:** none — verification only.

- [ ] **Step 1: Check it is safe to touch the Editor**

Run: `~/.unity/bin/unity --no-banner --json pipeline list`

If `isRunning` is true and the user may be in play mode, **ask before continuing.** Writing
under `Assets/` can stop play mode outright and throw them out of their session.

- [ ] **Step 2: Let the Editor compile, and read the console**

Focus the Unity Editor so it picks up the script changes. Expected: compiles clean, no errors
mentioning `PaletteShape`, `PastelPalette` or `LookLabLive`.

- [ ] **Step 3: Verify the committed look is untouched**

The quantize filter is installed inactive. Turn it on: `SpaceGame ▸ Environment ▸ Pastel
Quantize Filter`. Expected: the Game view posterises exactly as it did before this branch —
the palette is rebuilt from `PaletteShape.Default`, which holds the previous constants.

If it looks different, the refactor changed the look, and the golden check in Task 5 should
have caught it — start there.

- [ ] **Step 4: Capture six scene packs**

Enter play mode, fly to six places worth judging a look on — sky, sand, an interior, night, a
strongly lit exterior, a low-contrast one — and run `SpaceGame ▸ Look Lab ▸ Capture Scene
Pack` at each. Then:

```bash
ls LookLab/scenes/
python3 -c "import json,pathlib;
for p in sorted(pathlib.Path('LookLab/scenes').glob('*/pack.json')):
    d=json.loads(p.read_text()); print(p.parent.name, d['width'], d['height'], d['gitSha'][:8])"
```

Expected: six directories, each with a `color_ldr.png` and a `pack.json` carrying the Game
view's resolution and a git SHA.

Open one PNG. Expected: **not posterised** — the capture suppressed the filter. If it is
posterised, `SuppressQuantize` did not run.

- [ ] **Step 5: Rename the packs**

Rename each folder to something readable — `dunes`, `interior`, `night`, and so on. The lab
reads the folder name as the panel label.

- [ ] **Step 6: Verify the six-up grid**

Run `python3 tools/looklab.py serve`, open `http://127.0.0.1:8777/app/`, click **Grid**.
Expected: six panels, each posterised, each labelled with its folder name.

- [ ] **Step 7: Verify the lab matches the game by eye**

Set `blend` to 1 in the lab. Compare a panel against the same view in the Editor with the
filter on. Expected: the same look. Small sampling differences are expected; a *different*
look is not — the phase 2 parity harness exists to quantify this, but a by-eye mismatch here
means something is wrong now.

- [ ] **Step 8: Verify the live bridge, palette included**

Turn on `SpaceGame ▸ Look Lab ▸ Live Bridge`. Drag `chromaCeiling` in the lab.

Expected: **the six stills and the Game view change together.** The console logs
`[LookLab] enabled=True blend=1 palette=204 colours on 2 feature(s)` — two features, the PC
and Mobile renderers.

**This is the phase 1 acceptance criterion.** If the stills change and the Game view does
not, `EnsurePalette` is not being reached — check that `AddRenderPasses` calls it before
`EnqueuePass`.

- [ ] **Step 9: Verify a bad shape is loud**

```bash
curl -s -X POST http://127.0.0.1:8777/api/look -H 'Content-Type: application/json' -d '{"enabled":true,"blend":1,"palette":{"hueCount":64,"lightnesses":[0.9,0.5],"chromaFractions":[0.5,1],"chromaCeiling":0.2,"neutralCount":12,"neutralMinL":0.16,"neutralMaxL":0.97}}'
```

Expected: the Editor console logs `[LookLab] ... carries an unbuildable palette: the lattice
would build 268 colours but the shader holds 256. Nothing was applied.` — and the Game view
is unchanged.

- [ ] **Step 10: Verify the bridge restores what it found**

Turn the Live Bridge **off**. Expected: the Game view returns to the committed look and the
console logs `[LookLab] Live bridge off`.

- [ ] **Step 11: Commit the scene packs**

The LDR frames are the shared reference the team judges looks against, so they are committed;
the binaries phase 3 adds will not be.

```bash
git add LookLab/scenes
git status --short LookLab
git commit -m "feat: add the first six Look Lab scene packs"
```

- [ ] **Step 12: Final verification sweep**

```bash
python3 tools/typecheck.py --editor
python3 tools/palette_preview.py --check
python3 tools/docs_check.py --index
git status --short
```

Expected: all three exit 0, and `git status` is clean apart from anything under
`LookLab/live/` (which is ignored).

---

## Done when

Dragging a slider in the browser repaints six real frames **and** the running Editor, the
committed look is provably unchanged, and `PaletteShape` exists as the single place the
lattice is written down.

Phase 2 (parity harness and the commit path) gets its own plan, written after this one has
been used — the spec is explicit that what phase 1 teaches should reshape it.
