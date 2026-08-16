# mining_rig_derelict — build record

A 53.7 m abandoned desert mining structure on a 25.0 x 21.2 m footprint: five
stacked slab storeys, canted where the ground gave way under one side, rusted
through, still carrying its access catwalks and roof plant. Canted, its low
corner drops to z = -1.8, so it sits into sand without needing to be pushed
down by hand.

**Scope: the building only.** The reference image is a full crawler-excavator —
tracked undercarriage, boom conveyor, spoil heap, cliff. None of that is here.
The deliverable is the superstructure as a standing building, so it drops into a
scene at whatever depth and angle the terrain wants.

## Decomposition

### Reused from the library, unchanged

| Component | Variations used | Role |
|---|---|---|
| `structural/catwalk_span` | Wall, Corner, Balcony, Stair | level wraps, hung balconies, stair flights |
| `structural/handrail` | Straight, Corner, Gate, Ladder | crown railing, the continuous +X ladder climb |
| `structural/deck_plate` | Grate, Worn, Hatch | crown decking (alternated, never two alike adjacent) |
| `structural/hull_plate` | Patched, Riveted | weld-on repair plates over the worst corrosion |
| `structural/bulkhead_frame` | Door | the way in, at the base |
| `mechanical/vent_grille` | Louvre, Fan, Scoop | face ventilation, crown extract fans |
| `mechanical/pipe_run` | Straight, Elbow, CableBundle | the +Y riser, cables hanging off the overhang lip |
| `props/floodlight_bank` | Quad, Twin, Sweep | mast and parapet lighting |
| `props/light_fixture` | Clamp, Strip | walkway and crown-soffit lamps |

Nine existing components, twenty-six variations between them. Vents, pipes,
lamps and deck plates are authored at vehicle scale and are baked to 1.6–2.6x
via `scaled()`, which keeps every object's scale at 1.0.

### New components

**`structural/slab_block`** — 5 variations (Plain, Cantilever, Stepped,
Buttressed, Breached). The defining element, and the reason this is not just a
re-dress of `tower_bay`. A tower bay is *clad*: one smooth enamel surface with
seams scribed into it, the language of a machine somebody maintains. A slab
block is a **field of separate plates** standing 0.11 m proud of a dark backing,
with corner armour instead of pilasters. That difference runs all the way down,
so it is a second component rather than a flag on the first.
Base envelope 16 x 14 x 8 m, origin bottom centre. Cantilever, Stepped and
Breached deliberately break the envelope — an overhang, a setback and a torn
corner are the only silhouette events a stack of boxes gets.

**`mechanical/exhaust_stack`** — 4 variations (Flue, Cluster, Scrubber, Cowl).
Distinct from `drill_derrick`'s `Flare`: a flare is a bare burner pole meant to
be seen alight; these are ducts — lagged, banded, streaked below every joint,
and roof-height rather than rig-height.

**`structural/window_bank`** — 4 variations (Porthole, SlotRow, Shuttered,
Blown). `bulkhead_frame` covers openings people walk through; this covers the
ones they look through, and the two disagree on nearly every dimension.
Authored facing -Y with the origin at the opening centre. No booleans: the dark
reveal is a backing plate behind a raised frame, which reads the same and cannot
fail on a bevelled host.

**`props/hull_stencil`** — 5 variations (Chevron, DangerBand, Arrow, Roundel,
Placard). Painted markings as thin proud geometry, because nothing in this
library is UV-unwrapped and a stencil needing a texture atlas would be the only
asset here that could not just be dropped into a scene. Slanted stripes are
parallelogram prisms, not rotated boxes — a rotated box overshoots the band it
sits in and there are no booleans to trim it.

Built ahead of this request, not needed by it: `SlabBlock_Stepped` and
`SlabBlock_Buttressed` (only three levels were strictly required),
`ExhaustStack_Cowl`, `WindowBank_Shuttered`, and `HullStencil_Placard` — the
last is the only marking that reads at 3 m rather than 50 m.

## Assembly

```
 0.0 -  8.0   L0  SlabBlock_Buttressed   widest foot, raked ribs
 8.0 - 16.0   L1  SlabBlock_Plain        turned 90 deg -> a 1 m step
16.0 - 24.0   L2  SlabBlock_Cantilever   the overhang, toward +X
24.0 - 32.0   L3  SlabBlock_Breached     torn corner, high and lit
32.0 - 40.0   L4  SlabBlock_Stepped      setback + ledge, bleached paint
40.0 - 43.4   Crown machine house        unique geometry
43.7 - 53.7   Flue, Scrubber, Cluster, 4 Cowls on the roofs
```

53.7 m over a 16 m face — 3.4 : 1. Every level is turned or offset from the one
below; five identical boxes stacked square would read as a texture error, and
the turn at L1 is what gives the catwalks a ledge to stand on.

The **crown machine house** is the only unique geometry. It exists to be the
specific junction between L4's setback, the stack saddles and the deck the
catwalks arrive at. Low and wide on purpose — after five 8 m storeys the roof
plant needs to be what finishes the silhouette, and a sixth tall box would
fight it.

## Materials

No new palette entries. The build wanted a bright sun-baked ochre for the body,
and `palette.py check` put #B5813A within deltaE 11 of `Mat_Fabric_Wing_Ochre` —
but that is sailcloth at metallic 0.0, useless for plate. The right answer was
that `Mat_Metal_HullRust_Orange` already *is* the reference's body colour and
sunlight does the brightening; adding a fourth orange-brown metal would have
been exactly the "eleven slightly different greys" failure the palette guards
against. Everything draws from the existing 29.

## Rig

`Empty_MiningRig_Root` carries the cant — `(-3.2, 7.6, 0)` degrees — rather
than it being baked into geometry. **That is the one transform in this file
meant to be edited**: zero it and the building stands upright.

`Arm_MiningRig` has 11 bones, rigid bone-parented (not weighted — every mover is
a hinge, fan or hanging bundle, and vertex weights smear a rigid part at the
pivot): both crown extract fans, both sweep floodlights, the roof hatch, the
guyed flue, and six cable-bundle segments.

## Two near-duplicates that were checked and cleared

`LIBRARY.md` was stale when this build started — it listed 40 components while
54 existed on disk — so two candidates were only found afterwards and are worth
recording, because on dimensions alone both look like reuse that was missed.

- **`structural/outpost_block`** covers a similar envelope (`Station` is
  15.34 x 11.94 x 8.44 against `SlabBlock_Plain`'s 16.6 x 14.6 x 8.0) and even
  shares a `Breached` variation name. It is not the same thing: it is painted
  `Mat_Paint_Coral_Faded` over `Mat_Paint_Blue_Station` — a coral-and-blue
  outpost — and its other variations are Hab, Plant and Annex. It has no
  cantilever and no buttressed foot, which are the two silhouette events this
  building is built around. Different art direction, different silhouette set.
- **`structural/control_cab`** was the obvious candidate for the crown, and
  `Coll_ControlCab_Derelict` at 10.64 x 9.64 x 5.41 would have dropped onto
  L4's 11 x 9.5 setback almost exactly. Rejected on inspection: the cabs are
  arctic-white-and-salmon glazed observation boxes with outward-canted windows.
  On top of a rust hulk that reads as a different building. The crown stays
  unique geometry in the hulk's own material language.

If the rusted and the coral families should converge later, the merge to make is
`outpost_block` gaining Cantilever and Buttressed, not this file going away.

## Judgement calls worth knowing about

- **The stair flights stop in mid-air at their lower ends.** Deliberate: the
  flights that once carried on down are gone, same story as the breach. The
  continuous climb is the ladder run on the +X face. Say the word and they can
  be chained into a full switchback instead.
- **The cant is on an empty, not baked.** Reversible in one click.
- **No spoil, no terrain, no conveyor.** "Only the building." The library
  already has `mechanical/conveyor_ramp` if the boom is wanted later.
- **307 k triangles.** Landmark budget, comparable to `refinery_tower`. Almost
  all of it is instanced — 148 objects over far fewer meshes.
