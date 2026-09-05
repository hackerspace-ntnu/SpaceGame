# Oxygen Tank — build record

The pressure bottle the player carries and refills. Ø 0.22 × 0.48, pale enamel
barrel, orange cap / collar band / skirt, in the stylised sci-fi language of the
concept sheet.

> **`oxygen_tank.blend` is hand-edited and is the source of truth.**
> Never re-run `oxygen_tank.py` over it. The script is historical record.

## One model, not a family

It began as `supply_canister.blend`, a family of four bottles in different
proportions and accent colours — the library's usual "build a family, never an
object" rule. That was overridden on request: the brief is a single bottle, and
one bottle that is instantly recognisable in a dozen contexts is worth more here
than four that have to be told apart. The three built-ahead variations (squat,
slim, twin) were deleted rather than left orphaned in the library.

## Decomposition — five objects, never merged

| Object | Why separate |
|---|---|
| `Mesh_OxygenTank_Body` | barrel, grey sleeve, orange collar band, shoulder cone |
| `Mesh_OxygenTank_Cap` | the part that would open; origin on its own seat |
| `Mesh_OxygenTank_Skirt` | the base that plugs into a dock — a fitting, not decoration |
| `Mesh_OxygenTank_Ribs` | the two flank ribs and the latch clip |
| `Mesh_OxygenTank_Gauge` | the one emissive; a game may want to drive it |

Materials, all from the palette: `Mat_Paint_White_Arctic`,
`Mat_Neutral_Panel_Grey`, `Mat_Paint_Safety_Orange`, `Mat_Neutral_Black_Matte`,
`Mat_Neutral_Slate_Dark`, `Mat_Emissive_Green_CRT`, `Mat_Plastic_Rubber_Black`.
Nothing added for this model.

## The accent is structural, not decoration

Cap, collar band and skirt all wear the orange, and it is the only saturated
thing on the object. Those are also the parts with a distinct silhouette, so the
bottle survives being read in shadow or by a colour-blind player
(`GDC-L1-UX-0003` — never carry meaning in colour alone).

## Dimensions other files dock against

`OXY_R`, `OXY_CAP_R`, `OXY_SKIRT_R`, `OXY_LEN`, `OXY_PLUG` are published at the
top of `oxygen_tank.py` and **imported** by
`components/mechanical/dock_cradle.py` (collar bore, lug ring) and
`models/props/oxygen_generator.py` (dock pose, filler-arm reach). A second copy
of any of them is how a collar ends up 8 mm too tight with nothing in either
file looking wrong.

`OXY_SKIRT_R` is the number the collar bore is cut for — the skirt is the widest
thing that has to pass through, 8 mm fatter than the barrel.

## Two things removed on request

- **The wire bail handle.** A swept-tube loop arcing over the cap: thin, fiddly
  geometry that fought the flat-shaded look of everything else, and it occupied
  exactly the space the generator's filler head needs.
- **The three other canisters.** See above.

## Gotchas this build produced

- **Ribs on the back of a cylinder may as well not exist.** The first pass put
  the single rib at π from the front — invisible from every angle the bottle is
  seen from. They are now at ±0.44π, on the visible flanks.
- **`Matrix.Rotation(a, 4, 'Z')` carries local +X radially, not +Y.** A rib
  placed with the sizes in the wrong order is thin tangentially instead of
  radially: it still looks like a rib, and the origin and angle are both right,
  so nothing points at the bug. `_around()` exists to assert the mapping.
- **Every stacked coaxial part shared a cap plane with its parent** — sleeve
  bottom on barrel bottom, lip base on cap base, rib top on barrel top, foot ring
  on skirt base. Four flickering pairs from four "obviously correct" numbers.
  Each sub-part now overshoots or is buried.

`_zverify.py` on the generated version: **0 clashing pairs.** The final
hand-edited file predates the last two of those fixes and carries **3 pairs,
0.027 m²**:

| Pair | Sep | Cause |
|---|---|---|
| `Body` / `Skirt` | 1.0 mm | the grey sleeve's bottom annulus against the skirt's cap |
| `Body` / `Ribs` ×2 | 0.0 mm | each rib's top face on the barrel's own top plane |

Both are one-line fixes in the generator (`Z_BARREL − 0.006` for the rib top, a
deeper sleeve overlap) and would need re-applying to the .blend by hand or over
MCP — never by regenerating it.

## Shipping

`oxygen_tank_export.py` → `Assets/Game/Art/Models/Props/oxygen_tank.fbx`.
No Unity builder or gameplay code yet — there is no oxygen system to wire to.
