# Dune Ornithopter — build record

A homemade, desert-scavenged flapping-wing flyer built to a top-down reference
sketch: twin webbed wings on spoked shoulder gearboxes, a slim central fuselage
with a nose spike, a segmented tail boom ending in a second spoked hub and a
webbed tail fan.

## Brief as agreed

| | |
|---|---|
| Scale | Rideable mount — 6.0 m wingspan, 4.75 m nose to tail-fan tip |
| Rider | Prone, slung **under** the belly. Simple cradle: board, grip bar, stirrups |
| Articulation | Fan splay, shoulder flap, digit twist, tail fan splay + boom pitch |
| Budget | ≤30k tris, kept minimal. Priority on wing shape and structure; some visible gears |
| Look | Desert, homemade, beige wings |

### Revision — webbed wings

The first build gave each wing a fan of five separate paddle blades. That was
replaced on request: a wing is now **one continuous structure** — a thin spar
skeleton with cloth stretched between the digits, the way a bat's wing works —
and the whole machine was slimmed down.

The change is not cosmetic. Separate blades were rigid objects bolted to bones;
a continuous membrane has to *deform*, so the wings are skinned with vertex
weights and driven by an armature modifier while everything else stays
bone-parented. That split is the main thing to understand about this file.

Slimming pass, for the record: fuselage section down ~32%, boom down ~40%, the
shoulder bearing changed from a solid block to an open yoke, the pylon from a
boxed beam to a four-longeron truss you can see through, and the drive wheels
from heavy flywheels to narrow open rims.

## Reuse

**Nothing from `components/` was reused.** The library is otherwise rich enough
that this looks like an oversight, so: every existing mechanical component is
authored for the crawler/RV-ship family at 1–5k tris each (`road_wheel`
1768–4204, `hinge_heavy` 820–1630, `tail_segment` 2798–4668). Two `road_wheel`s
alone would have cost ~8k of the budget, and they are rubber-tyred ground
wheels rather than the open spoked drive wheels the sketch shows.

What *is* reused is the thing that actually carries visual coherence: the
shared material palette. Every surface links from `palette.blend`, so the flyer
sits in the same desert-bleached world as the crawler and the ship.

`components/mechanical/wing_blade.blend` — the six paddle blades from the first
build — is **kept but no longer used by this model**. It was not deleted: it is
a perfectly good component that another machine could want, and nothing asked
for it to go.

## Palette additions

Two materials; everything else came from the existing 24.

- `Mat_Fabric_Wing_Beige` `#CBB68E` — the beige the brief asked for. Nothing
  served: `Mat_Fabric_Canvas_Faded` `#6E6A5A` is dirty grey webbing and
  `Mat_Fabric_Flag_Bleached` `#D8D2C2` is near-white. The checker flagged
  `Mat_Plastic_Cream_Aged` `#B8AD94` at deltaE 9.8, but that is interior
  cabinet plastic at roughness 0.6 — wrong category, wrong finish, visibly
  cooler than the warm sailcloth wanted here.
- `Mat_Metal_Brass_Tarnished` `#9C7B3F` — gear teeth, bearing collars, wrist
  and knuckle pins. Checker confirmed nothing was close. Brass against bleached
  steel is what sells "machined from scrap".

## Decomposition

### `components/structural/wing_panel.blend` — the wing *(new)*

The load-bearing component. Four variations, two objects each:

| Object | Role |
|---|---|
| `Mesh_WingPanel_<Var>_Frame` | the skeleton — arm, wrist, five digits, knuckle pins, tension cable |
| `Mesh_WingPanel_<Var>_Web` | the cloth — sagging, hemmed, double-sided |

Splitting frame from web is the split the brief described ("a structure, and
then wings between them"), not a subdivision of the wing itself. Both objects
carry identical vertex groups, so both fold together off one armature.

Variations: `Main`, `Patched` (bleached repair squares sewn on), `Torn` (a
truncated outer bay and a plywood splint lashed over digit 2), `TailFan` (a
smaller five-digit fan with no arm, radiating straight off its hub).

Three things make the cloth read as cloth rather than as flat triangles:

- **Scalloped free edge.** Each bay is pulled back toward the wrist at
  mid-chord, so the trailing edge bows inward between digit tips.
- **Sag.** The membrane droops between the spars, most at mid-bay and more
  toward the tips where the bay is widest.
- **A hem.** The outermost band of every bay is `Mat_Fabric_Canvas_Faded`, so
  the free edge reads as sewn rather than cut.

The sheet is built double-sided with real thickness — a wing gets seen from
underneath, and a single-sided plane would vanish under backface culling.

### `components/mechanical/shoulder_gear.blend` — 4 variations

`Spoked` (narrow open drive wheel), `Toothed` (a real cog with drilled
lightening holes), `Bearing` (the open yoke the wing pivots in), `Crank` (arm
and connecting rod). Kept separate from the wing because a spoked wheel and a
crank are the two most reusable things on the machine.

### `components/structural/wing_frame.blend` — 3 variations

`Pylon` (four-longeron open truss carrying the shoulder off the fuselage),
`Strut` (turnbuckle tie-rod), `Hub` (the original fan pivot — retained for the
library, unused here now that the wing carries its own wrist).

### `components/structural/fuselage_pod.blend` — 4 variations

`Nose` (tapered cone plus the sketch's forward spike), `Core` (slim lofted body
with a spine ridge and a belly rail), `Boom` (thin tapered tail tube with
collar bands), `TailHub` (the small spoked wheel at the boom's end).

### `components/props/prone_cradle.blend` — 3 variations

`Pad` (plywood board, ochre padding, webbing straps), `GripBar` (control bar),
`Stirrup` (foot rest, placed once per foot). Deliberately simple, per the brief.

## Assembly — `models/vehicles/dune_ornithopter.blend`

Fuselage on the centreline, nose to −Y. Each shoulder carries a truss pylon, an
open bearing yoke, a spoked drive wheel and a crank. The wing panel bolts to
the shoulder pivot and is mirrored for the port side. The two wings use
*different variations* — starboard `Main`, port `Patched` — so they do not read
as mirror-perfect copies.

### Rig — `Arm_DuneOrnithopter`, 30 bones

```
Bone_Root
└─ Bone_Body
   ├─ Bone_Nose
   ├─ Bone_Cradle                     rider mount point
   ├─ Bone_Shoulder_L/R               FLAP
   │  ├─ Bone_Gear_L/R                gear spin
   │  │  └─ Bone_Crank_L/R            crank throw
   │  └─ Bone_Arm_L/R                 wing sweep; deforms out to the wrist
   │     └─ Bone_Digit_L/R_1..5       SPLAY + TWIST; each lies along its spar
   └─ Bone_Boom_1 → Bone_Boom_2       PITCH
      └─ Bone_TailHub
         └─ Bone_TailDigit_1..5       tail fan SPLAY
```

Rigid parts (fuselage, gears, cranks, pylons, cradle) are **bone-parented**.
The six webbed panels are **skinned** — parented to the armature object with an
Armature modifier and vertex weights.

### Axis and sign conventions

This is the genuinely confusing part of the rig, and it caused two real bugs
during the build, so it is written down rather than left to be rediscovered:

| Motion | Bone | Axis | Per-side sign? |
|---|---|---|---|
| Wing beat | `Bone_Shoulder_L/R` | local **X** | **No** |
| Wing sweep | `Bone_Arm_L/R` | local **Z** | **Yes** |
| Digit splay | `Bone_Digit_*` | local **Z** | **Yes** |
| Digit twist | `Bone_Digit_*` | local **Y** (roll) | **No** |
| Gear spin | `Bone_Gear_L/R` | local **Y** | either |
| Tail fan splay | `Bone_TailDigit_1..5` | local **Z** | n/a |
| Boom pitch | `Bone_Boom_1/2` | local **X** | n/a |

The rule behind the table: the wing bones point outboard in **opposite**
directions, so their local X and Y axes are already mirrored and the *same*
angle on both sides produces a symmetric result. Local Z points up on both
sides and is *not* mirrored, so anything rotating about Z needs an explicit
per-side sign or one wing opens while the other closes.

Digit twist is a pure roll because each digit bone lies exactly along its spar.
That axis choice is the whole reason the rig stays simple.

`dune_ornithopter_posetest.py` drives all of it and renders the result. It
opens the file read-only and never saves. Poses: `rest`, `glide`, `downstroke`,
`upstroke`, `folded`.

### Vertex groups — a trap worth knowing

In this Blender version vertex groups belong to the **mesh**, not the object.
An object created from an existing mesh already has them, so adding groups by
name *appends duplicates* and leaves every weight bound to the old names. The
assembly therefore renames groups in place, and copies the mesh per instance —
otherwise renaming the port wing's groups silently renames the starboard
wing's too. Both mistakes fail silently: the wings simply do not deform.

The component declares groups in the fixed order given by
`_ornithopter.SKIN_GROUPS`; weights are stored against group *indices*, so the
order is what actually has to hold, and the assembly asserts it on load. It
also asserts the rename map is one-to-one, because a collision would let
Blender suffix the duplicate into a dead group.

## The 10 m rescale — and why the generator was not re-run *(2026-08-11)*

**The machine now ships at a 10.0 m span and the prone rider fits.** The cradle board is 1.85 m
against a 1.80 m rider, measured in Unity. That closes the open problem recorded below, which is
kept as written because the reasoning still holds.

The fix this file prescribed — set `TARGET_SPAN = 10.0` and rerun the builds — was **only half
right**, and the other half would have destroyed work.

The five ornithopter-exclusive components (`wing_panel`, `wing_frame`, `fuselage_pod`,
`prone_cradle`, `wing_blade`) *are* pure generator output. Each was rebuilt into a scratch file and
compared against the shipped one before anything was overwritten: identical object names, triangle
counts, parenting, modifiers and vertex-group counts, with every dimension scaled by exactly
1.6667. Those were rebuilt in place.

**The assembly was not.** `dune_ornithopter.blend` carries hand edits that exist nowhere else:

| Object | Object scale in the shipped file |
|---|---|
| `Mesh_Cradle_Pad` | 1.0, 1.0, **1.3130** |
| `Mesh_Cradle_Stirrup_1` | 1.0, 1.0, **1.2571** |
| `Mesh_Cradle_Stirrup_2` | 1.0, 1.0, **1.2588** |
| `Mesh_Fuselage_Core` | **1.1471, 1.1471, 0.8495** |

Someone had already been stretching the cradle by hand to fit a rider. The two stirrups differ by
0.0017, which no script produces. `Mesh_Cradle_GripBar` was additionally left in **Edit Mode** when
the file was last saved.

So the assembly was **scaled in place** instead, by `dune_ornithopter_rescale.py`, which preserves
those object scales rather than regenerating over them. `_buildlib.start()` refuses to overwrite an
existing .blend, and that guard is the only reason the hand edits still exist — do not work around
it.

Three traps that script had to handle, all of which fail silently:

- **An object left in Edit Mode keeps a separate edit-mesh** that is flushed back over the mesh
  datablock on save. `Mesh.transform()` appears to work, reports correctly scaled vertices, and is
  then thrown away when the file is written. One mesh survived the first attempt at its original
  size because of this.
- **Shared mesh datablocks.** Six of the 25 meshes are placed twice off one datablock — bearings,
  drive wheels, cranks, one per side. Scaling per object rather than per datablock squares the
  factor on the second placement.
- **Connected bones share head and tail.** `Bone_Boom_2`'s head IS `Bone_Boom_1`'s tail, so scaling
  in place reads a value the parent already scaled. `Bone_Boom_1`'s tail came out at k². Snapshot
  every head/tail first, then assign.

### `shoulder_gear` is shared, and does NOT follow `TARGET_SPAN`

`components/mechanical/shoulder_gear.blend` is also appended by `models/creatures/horse_robot.py`
and `models/creatures/humanoid_robot.py`, which place its meshes at object scale 1.0 — so its size
is theirs too. Rebuilding it at 10 m would have left both robots correct *today*, because their
.blends are already built, and silently grown their shoulder gears by 1.67× the next time either
was regenerated.

It is therefore pinned: `shoulder_gear.py` imports `SHARED_COMPONENT_SCALE` rather than `SCALE`,
and the ornithopter's assembly scales what it appends from it by `SHARED_COMPONENT_FIXUP`. The
coupling is gone rather than merely unexercised.

## Unity

Exported by `dune_ornithopter_export.py` to
`Assets/Art/Models/Vehicles/Ornithopter/dune_ornithopter.fbx` — 25 meshes, 30 bones, no leaf bones, six
skinned panels each bound to all 30. The export asserts that skinning before it writes, because a
webbed panel arriving as a rigid mesh means the shoulders flap and the cloth hangs in space behind
them, and nothing downstream reports it.

The prefab is built from that FBX by `Assets/Editor/Vehicles/OrnithopterBuilder.cs`. See
`Assets/Scripts/Vehicles/Ornithopter/README.md` for the flight model and the articulation.

## Scale, and one open problem

The layout is authored in round numbers off the sketch, which put the span at
8.196 m. `models/_ornithopter.py` holds `TARGET_SPAN = 6.0` and derives `SCALE`
from it; every component scales its mesh on the way out via `_buildlib.SCALE`,
and the assembly scales only the positions it places parts at. Nothing is
scaled at object level, so every object reads scale 1.0 and the file needs no
apply step.

> **Resolved 2026-08-11** — see "The 10 m rescale" above. The machine ships at 10 m and the
> reasoning below is what drove that change. Kept because it explains *why* the span moved.

**The 6 m span and the prone rider still do not fit together.** At this scale
the cradle board is 1.11 m and the fuselage 1.59 m; a prone adult is about
1.8 m. The sketch's proportions cause it — roughly 1.25 times as wide as long —
so pinning the span at 6 m forces a short body.

The model ships at the requested 6.0 m. To make it genuinely rideable, change
one constant:

```python
# models/_ornithopter.py
TARGET_SPAN = 10.0        # cradle board becomes ~1.85 m
```

then rerun the six component builds and the assembly. The scale flows through
every part; nothing else needs touching.

## Triangle budget

**19,786 tris** against the 30k ceiling — down from 29,486 before the redesign,
because a webbed wing is far cheaper than a fan of solid blades. Roughly: wings
2.6k, tail fan 0.9k, fuselage 4.3k, the two shoulder assemblies 8.4k, rider
cradle 2.3k.

Two decisions are worth recording as quality changes rather than savings:

- **The sailcloth is not bevelled.** `Part.bevel()` takes an explicit face list
  on the old blades so only hardware gets rounded edges — a taut sail has a cut
  hem, not a rolled one.
- **The membrane's poly budget went into subdivision, not detail.** A bay is a
  7×6 grid so the sag curves smoothly; that reads far better at this size than
  the same triangles spent on fittings.

Headroom is deliberate: ~10k tris remain for hand-modelled additions.
