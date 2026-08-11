# Dune Ornithopter — build record

A homemade, desert-scavenged flapping-wing flyer built to a top-down reference
sketch: twin fan-bladed wings on spoked shoulder gearboxes, a slim central
fuselage with a nose spike, a segmented tail boom ending in a second spoked hub
and a splaying tail fan.

## Brief as agreed

| | |
|---|---|
| Scale | Rideable mount — 6.0 m wingspan, ~4.9 m nose to tail-fan tip |
| Rider | Prone, slung **under** the belly. Simple cradle: board, grip bar, stirrups |
| Articulation | Fan splay, shoulder flap, per-blade twist, tail fan splay + boom pitch |
| Budget | ≤30k tris, kept minimal. Priority on wing shape and structure; some visible gears |
| Look | Desert, homemade, beige wings |

## Reuse

**Nothing from `components/` was reused.** That is a deliberate call and worth
stating plainly, because the library is otherwise rich enough that it looks like
an oversight.

Every existing mechanical component is authored for the crawler/RV-ship family,
where parts are 1–5k tris each: `road_wheel` variations run 1768–4204 tris,
`hinge_heavy` 820–1630, `tail_segment` 2798–4668. The two shoulder wheels alone
would have cost ~8k tris of the budget as `road_wheel` — and they would have
looked wrong anyway, since those are rubber-tyred ground wheels rather than the
open spoked drive wheels the sketch shows. The same applies to `tail_segment`,
which is a 4 m armoured crawler tail, not a 1.9 m tube boom.

What **is** reused is the part that actually carries visual coherence: the
shared material palette. Every surface on this model links from `palette.blend`,
so the flyer sits in the same desert-bleached world as the crawler and the ship.

## Palette additions

Two materials added; everything else came from the existing 24.

- `Mat_Fabric_Wing_Beige` `#CBB68E` — the beige the brief asked for. Nothing in
  the palette served: `Mat_Fabric_Canvas_Faded` `#6E6A5A` is dirty grey webbing
  and `Mat_Fabric_Flag_Bleached` `#D8D2C2` is near-white. The palette checker
  flagged `Mat_Plastic_Cream_Aged` `#B8AD94` at deltaE 9.8, but that is interior
  cabinet plastic at roughness 0.6 — wrong category, wrong finish, and visibly
  cooler than the warm sailcloth wanted here.
- `Mat_Metal_Brass_Tarnished` `#9C7B3F` — gear teeth, bearing collars, crank
  pins. Checker confirmed nothing was close. Brass against bleached steel is
  what sells "machined from scrap" rather than "factory made".

## Decomposition

Five new component files. The cut follows what could plausibly reappear on other
desert machines, not what is convenient for this one model.

### `components/mechanical/wing_blade.blend` — 6 variations

The load-bearing component, and where most of the modelling attention went.
Each blade is beige sailcloth lofted over a tapering steel spine with plywood
battens and a brass root collar. Origin sits on the root pin; the blade runs
along local **+Y** so a bone laid along it gives **twist as bone roll** and
**splay as rotation about the hub Z**. That axis choice is the whole reason the
rig is simple.

| Collection | Length | Role |
|---|---|---|
| `Coll_WingBlade_Primary` | 2.30 m | Longest fan blade, outermost of the fan |
| `Coll_WingBlade_Secondary` | 1.92 m | Mid fan blade |
| `Coll_WingBlade_Covert` | 1.48 m | Short inner blade, closest to the hub |
| `Coll_WingBlade_Membrane` | 2.95 m | The broad swept leading panel — the wing's "arm" |
| `Coll_WingBlade_Tattered` | 1.92 m | Secondary with a torn trailing edge and a lashed patch |
| `Coll_WingBlade_TailFan` | 1.15 m | Short wide blade for the tail fan |

`Tattered` exists because a homemade machine with six identical undamaged blades
reads as manufactured. `TailFan` is separate rather than a scaled `Covert` —
uniform scale jitter is not variation, and the tail blade is genuinely a
different proportion (short and wide, not long and narrow).

### `components/mechanical/shoulder_gear.blend` — 4 variations

The visible mechanism. `Spoked` is the large open drive wheel straight from the
sketch; `Toothed` is a real toothed cog; `Bearing` is the pivot block the wing
root sits in; `Crank` is the arm and connecting rod that drives the beat.
Separated from the wing because a spoked wheel and a crank are the two most
reusable things on the entire machine.

### `components/structural/wing_frame.blend` — 3 variations

`Hub` is the fan pivot where the blade roots converge — a stacked disc with
knuckle lugs, one per blade. `Pylon` carries the shoulder off the fuselage.
`Strut` is the bracing tie-rod. Split from `shoulder_gear` because these are
structure, not mechanism, and a pylon or a tie-rod belongs on anything.

### `components/structural/fuselage_pod.blend` — 4 variations

`Nose` (tapered cone plus the sketch's forward spike), `Core` (lofted body with
a spine ridge and riveted flanks), `Boom` (tapered tail tube with collar bands),
`TailHub` (the small spoked wheel and the tail fan's knuckle plate).

Cut into four rather than modelled as one hull so the boom and nose can be
restated at other lengths without touching the body.

### `components/props/prone_cradle.blend` — 3 variations

`Pad` (padded board with ochre webbing straps), `GripBar` (the control bar the
rider holds), `Stirrup` (foot rest). Deliberately simple, per the brief.

## Assembly — `models/vehicles/dune_ornithopter.blend`

Fuselage on the centreline, nose to −Y. Each shoulder carries a pylon, a bearing
block, a spoked drive wheel, a crank, and a fan hub. Five blades per wing fan
aft-and-outward from the hub; the membrane leads. Blade variations are
distributed rather than repeated — the tattered blade sits in a different fan
position left and right so the two wings do not read as mirrored copies.

### Armature — `Arm_DuneOrnithopter`

Rigid objects are parented directly to bones, not weighted, per the mechanical
rule.

```
Bone_Root
└─ Bone_Body
   ├─ Bone_Nose
   ├─ Bone_Cradle                    rider mount point
   ├─ Bone_Shoulder_L/R              FLAP — whole wing beats
   │  ├─ Bone_Gear_L/R               gear spin
   │  ├─ Bone_Crank_L/R              crank throw
   │  └─ Bone_WingHub_L/R
   │     ├─ Bone_Membrane_L/R        SPLAY about hub Z, TWIST about own Y
   │     └─ Bone_Blade_L/R_1..5      SPLAY about hub Z, TWIST about own Y
   └─ Bone_Boom_1 → Bone_Boom_2      PITCH — boom flex
      └─ Bone_TailHub
         └─ Bone_TailBlade_1..5      tail fan SPLAY
```

Every blade bone lies along its blade, root to tip. That is what makes twist a
single-axis roll on the bone instead of a compound rotation, and it is the one
non-obvious rigging decision in the file.

## Triangle budget

See the final report; target was ~20k against the 30k ceiling, leaving headroom
for the user to add hand-modelled detail without blowing the limit.
