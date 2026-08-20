# hulk_settlement — build record

A 60 m derelict bulk-handling machine with people living in it, built from a
concept reference: a tall block tower at the left end, a long horizontal hulk
stepping down from it, and a boom cantilevered out to the right well below the
tower top.

Brief as agreed: **~60 m mid landmark**, **the hero hulk only** (no satellite
outbuildings), **no ground or berm**, **no armature**.

- **Model:** `models/buildings/industrial/hulk_settlement.blend`
- **Extent:** 94.0 x 30.2 m footprint, z **-6.00 to exactly 60.00 m**
- **133 objects / 55 unique meshes / 355 652 triangles**
- **No material was added to the palette.** See *Materials* below — this is the
  first model in the library to need nothing new, and that is the palette
  working as intended rather than a coincidence.

## Relationship to `mining_rig_derelict`

`mining_rig_derelict.blend`, in this same folder, is a **different reading of
the same reference image** and the two are deliberately not merged. That one is
the *tower*: a 16 m face taken straight up, which is what the reference looks
like if you crop the boom out. This one is the *horizontal composition* — the
long mass and the cantilever that make the silhouette asymmetric, which is what
the reference looks like if you keep the whole frame.

They share `slab_block` and `exhaust_stack` and disagree about nothing. Placing
both in one scene is intended: they read as two machines of the same make.

## Height budget

| Range | What | Source |
|---|---|---|
| -6.0 – 2.0 | L0, four blocks, mostly buried | `slab_block` |
| 2.0 – 10.0 | L1, four blocks — the full 64 m length | `slab_block` |
| 10.0 – 18.0 | L2, three blocks, first setback | `slab_block` |
| 18.0 – 26.0 | L3, two blocks | `slab_block` |
| 26.0 – 34.0 | L4, one block — the stack is now a tower | `slab_block` |
| 34.0 – 42.0 | L5, one block, torn corner high up | `slab_block` |
| 42.0 – 43.2 | crown machine deck | unique geometry |
| 41.6 – 60.0 | `Derrick_Mast`, placed so the tip lands on 60.00 | `drill_derrick` |

The mass is 64 m long over a 42 m block stack; the boom carries the silhouette
out to 94 m overall. The tower is 60 m over a 16 m face — 3.75 : 1, which is
the proportion the reference has once the ground is taken out of it.

**The lowest course is buried on purpose.** It runs to z = -6 so terrain can
swallow whatever it wants without the model ever showing a floating footing.
This is how the brief's "no berm" was honoured while keeping the reference's
half-sunk read.

## Reused from the library, unchanged

| Component | Variations placed | Why it served |
|---|---|---|
| `structural/slab_block` | all 5 | The rusted-hulk storey. Every one of the 15 blocks is one of these; the whole mass is this component. |
| `mechanical/gantry_boom` | all 5 | New — see below. |
| `structural/shanty_addon` | all 5 | New — see below. |
| `mechanical/exhaust_stack` | Flue, Cluster, Scrubber, Cowl | Roofline punctuation. See the note on this file below. |
| `mechanical/drill_derrick` | Mast, PipeRack, Winch | The crown. `Mast` is what sets the final 60.00 m. |
| `structural/catwalk_span` | all 6 | Building-scale walkways wrapping four levels. |
| `structural/truss_frame` | Portal, Brace | Bracing from the mass into the boom saddle. |
| `structural/cabin_module` | Cargo, Workshop | Containers parked on the clear part of the L2 roof. |
| `structural/hab_capsule` | Pod | Two bolted-on pods — the round note against all the boxes. |
| `mechanical/conveyor_ramp` | Ramp, Trestle, Hopper | The strong diagonal, hugging the -Y flank. |
| `structural/support_leg` | Strut | Props against the lower flank. |
| `structural/handrail` | Ladder, Straight | Player-scale detail at ground level, where it is worth paying for. |
| `structural/bulkhead_frame` | Door | Three doors at the one height a player reaches. |
| `structural/deck_plate` | Worn, Grate | L1 roof plating, alternated so no tile repeats adjacently. |
| `structural/mast_rig` | Antenna | Two whips on the crown deck. |
| `mechanical/pipe_run` | Straight, Elbow, Junction | Service run across the L2 roof. |
| `mechanical/vent_grille` | Louvre, Fan | The Buttressed L1 flank. |
| `props/floodlight_bank` | Quad, Twin, Sweep | Site lighting on every large mass. |
| `props/light_fixture` | Clamp, Strip | Walkway lamps. |

Props authored at vehicle scale go through `scaled()`, which bakes a uniform
factor into one shared mesh copy per size. Every object in the file is at scale
1.0 and a size is paid for once however many times it is placed.

## New components

Two. Both are things the reference has that the library could not build, and
both are cut at the smallest unit that could plausibly recur.

| Component | Variations | Why it is separate |
|---|---|---|
| `mechanical/gantry_boom` | Span, Head, Heel, Stay, Counter | `truss_frame` is *static* — columns that stand and beams that span between two supports. A cantilever tapers, carries a stay mast and tie bars, and turns on a slew bearing. A truss beam has none of those and looks absurd with them bolted on. 16 940 tris across the five. |
| `structural/shanty_addon` | LeanTo, Box, Stack, Awning, Water | `cabin_module` and `hab_capsule` are both *manufactured*. These are made on site out of offcuts by people who did not build the hulk. That mismatch is the entire mechanism by which a derelict reads as occupied. 12 442 tris across the five. |

### One shared datum, five parts

Every `gantry_boom` variation uses **the pivot point as its origin** — `Heel`,
`Stay` and `Counter` sit on it, `Span` roots at it, `Head` lands at
`pivot + (26, 0, 0)`. Raking the whole boom nose-down 5 degrees is therefore
one rotation applied five times about one point, and nothing is measured twice.
That is worth more than it sounds: the first layout put the boom flat, and
changing it cost one constant.

### Mounting convention for `shanty_addon`

The mounting face is the plane **x = 0** and the shanty projects into **+X**.
Yaw -90 hangs one on a -Y flank, +90 on a +Y flank, 0 on a +X end face. Every
variation obeys it, so they are interchangeable at any mounting point without
re-measuring — which is what made three dense terraces cheap to lay out.

### Built ahead of the request

All ten new variations are placed in this model, so there is no unused
overshoot. The overshoot is instead in **breadth**: `Counter` and `Stay` exist
because a cantilever that does not explain how it holds itself up is a prop,
and `Water` and `Awning` exist because a settlement made only of rooms has
nowhere anybody would actually sit. None of the four was strictly needed to
match the reference silhouette. Any future industrial or inhabited-wreck asset
gets all ten for free.

## Unique to this model

Two pieces, both justified by being the specific junction between *these* parts
rather than anything reusable:

- `Mesh_Hulk_CrownDeck` — the machine deck at z 42.0–43.2. Its only job is to
  be the junction between this block stack and this derrick mast: a 16 x 14
  roof, a parapet, and a bolt circle sized to the mast base. A component would
  have to be parameterised on both, at which point it is this function with
  extra steps.
- `Mesh_Hulk_BoomSaddle` — the plinth under the boom heel on the L1 roof. A
  slew bearing has to land on something that spreads its load into the
  structure; without it an 8 m machine house sits on a roof looking dropped.

## Materials

**Nothing was added to the palette.** All 24 materials in the file already
existed. The rust family (`Mat_Metal_HullRust_Orange`, `Mat_Metal_Rust_Heavy`,
`Mat_Paint_Hull_Bleached`) is the reference's body colour more or less exactly,
and `Mat_Emissive_Cabin_Warm` — added for the RV's interior — turned out to be
the single most valuable material here, because one lit window on a 60 m rust
pile is the cheapest possible signal that somebody is home.

The two loud notes are deliberate and both are worth revisiting:

- `Mat_Paint_Safety_Orange` on the conveyor gallery is much more saturated than
  anything around it. It is the reference's strong diagonal, but the reference
  renders that diagonal dark.
- `Mat_Paint_White_Arctic` on the cabin modules and pods is arctic-cool against
  a warm desert palette. It matches the light patch the reference has on the
  middle of the mass, but it arrived by inheritance rather than by choice.

## A note on `mechanical/exhaust_stack`

This model reuses that component, and its contents changed underneath this
build. A concurrent session authored a version with variations `Broken`,
`Capped`, `Tall`, `Twin`; that script was then overwritten and the `.blend`
rebuilt with `Flue`, `Cluster`, `Scrubber`, `Cowl`. The file, its script and
both models that use it are now mutually consistent, but the earlier four
variations no longer exist anywhere. Recorded here because
`mining_rig_derelict_BUILD.md` still documents a height budget ending in
`ExhaustStack_Tall`, which is no longer what that model contains.

## Decisions worth revisiting

- **The conveyor's colour**, above. Swapping the gallery for a dark-painted
  variant would match the reference better; it would need a new
  `conveyor_ramp` variation rather than a change here.
- **No armature**, per the brief. If that changes, the candidates are already
  cut for it: the boom slews on `Heel`'s bearing, luffs at the same pivot, and
  `Head`'s drum and the `Conveyor_Hopper` gate are the two moving parts left.
  The one-shared-datum layout means rigging the boom is five parents to one
  bone.
- **355 k triangles** is a hero-landmark budget at a mid-landmark size. The
  cheapest reduction is the catwalk wraps and the twelve clamp lamps; the
  shanties are only 12 k of it and are what the model is *for*.
- **Fifteen blocks from one 5-variation component** is a lot of repeat. It
  survives because `slab_block` rotates — six of the fifteen are yawed 180 —
  and because the three envelope-breaking variations land where they buy
  silhouette. A sixth variation would still help the L0/L1 courses.
- **The settlement is clustered, not sprinkled.** Three dense terraces with
  empty flank between them, rather than fifteen evenly spaced dwellings. Evenly
  spaced reads as decoration on a machine; clustered reads as people choosing
  where to live.

## Verification

All three new/changed `.blend` files pass the production checklist: every
object at scale 1.0, no auto-suffixed object, mesh or material names, no empty
material slots, no loose vertices, no faceless objects, every material from the
palette. The assembly measures **exactly 60.00 m** — asserted in the build
script rather than hoped for, so a future edit that breaks it fails loudly.

| File | Objects | Tris | Result |
|---|---|---|---|
| `components/mechanical/gantry_boom.blend` | 5 | 16 940 | PASS |
| `components/structural/shanty_addon.blend` | 5 | 12 442 | PASS |
| `models/buildings/industrial/hulk_settlement.blend` | 133 | 355 652 | PASS |
