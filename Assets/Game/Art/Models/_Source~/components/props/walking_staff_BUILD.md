# walking_staff — build record

A family of hand-cut wooden staves. Built because the Nomad NPC carries one and
swings it when provoked, but filed as a component rather than as a Nomad
accessory: a walking stick is one of the most reusable props a desert settlement
can own — a hiker's third leg, a herder's goad, an awning pole, a lean-to ridge.

## Reused from the library

Nothing. No staff, cane, pole or shaft component existed — the closest hits in
`LIBRARY.md` were `Mesh_CutterRamRod` and `Mesh_Coll_LiftRodN`, which are 2.7 m
hydraulic rams on the Desert Crawler, not hand props.

Every **material** is reused; see below.

## Created

One component file, `components/props/walking_staff.blend`, holding four
variations. It is one file rather than four because these are variations of one
thing, and the library's rule is that variations of one thing live together.

| Collection | Length | What makes it different |
|---|---|---|
| `Coll_Staff_Nomad` | 1.62 m | Straight-grown stave, burl below the grip, cord-wrapped grip, iron ferrule, wrist thong. The hero — this is the one that ships. |
| `Coll_Staff_Gnarled` | 1.48 m | A raw branch: three hard kinks, a forked crown, stubs where side limbs were sawn off. No metal anywhere. |
| `Coll_Staff_Cane` | 1.02 m | Short cane with a T crossbar handle. Two thirds the height and a completely different top. |
| `Coll_Staff_Splinted` | 1.55 m | Snapped mid-shaft and field-repaired — two scrap plates over the break, whipped down with cord, and a deliberate kink where the halves did not realign. |

Only `Coll_Staff_Nomad` was needed by the request. The other three were built
ahead: the marginal cost of a variation is small once the shaft generator and
the material choices exist, and a settlement scene that needs sticks will need
more than one kind.

The variations differ in **silhouette**, not in colour. Four brown lines of
slightly different length would read as one stick and a scale bug, so each one
changes the outline: straight-with-ferrule, forked, T-topped-and-short,
splint-bulged.

## How it is built

Every shaft is a **single loft** along +Z whose stations wander laterally and
change radius. That matters for two reasons:

- A chain of cylinders would leave a visible crease at each joint. One loft is
  continuous, and smooth shading across it costs nothing.
- **A knot or burl is a local radius bulge on a station**, not a sphere stuck to
  the side. A sphere would leave interior faces inside the shaft; a bulge stays
  watertight.

Bevelling is applied to exactly one thing — the splint plates on
`Coll_Staff_Splinted`, which are the only box-like pieces in the file. A
whole-part bevel is deliberately avoided: `_buildlib`'s bevel welds thin swept
tubes into blobs, and every shaft here is a thin swept tube.

## Origin

**The centre of the grip, not the butt of the shaft.**

A staff spends its life either planted on the ground or held in a fist, and only
one of those can be the origin. The grip wins because holding is what this
family was built for — parenting to a hand bone becomes a zero-offset parent,
with no per-variation number to look up and get wrong.

The consequence, which matters for anyone placing one in a scene: the butt sits
at **negative Z**, so a staff stood upright on the ground must be raised by its
own grip height (1.32 m for the hero) rather than dropped at z = 0.

## Armature

None. Nothing on a stick moves, and the one break in the family is modelled as a
repair rather than as a joint. An armature would be cost with no capability.

## Materials — nothing added to the palette

| Slot | Material | Used for |
|---|---|---|
| 0 | `Mat_Wood_Ply_Worn` | every shaft, crossbar, fork and stub |
| 1 | `Mat_Fabric_Canvas_Faded` | cord grip wrap, splint whipping |
| 2 | `Mat_Metal_Steel_Worn` | ferrules and collars |
| 3 | `Mat_Metal_Rust_Heavy` | the splint plates |
| 4 | `Mat_Hide_Claw_Horn` | leather wrist thong |

The Wood category holds exactly one entry, so adding a sun-bleached desert wood
next to `Mat_Wood_Ply_Worn` was the obvious move and was **rejected by the
palette guard**: every candidate in that band came back within deltaE 3 of
`Mat_Plastic_Cream_Aged` (`#B8AD94`), and a wood that is perceptually identical
to the RV's cabinet plastic is exactly the "just one more slightly different
tone" that makes a palette stop constraining anything. The shafts therefore take
the plywood brown, and all the contrast on the model comes from the cord, the
iron and the leather instead.

One material choice was corrected after looking at the first render:
`Coll_Staff_Splinted`'s ferrule started as `Mat_Metal_Rust_Heavy` and read as an
orange moulded plastic tip rather than as corrosion. That material is a
saturated orange intended to be read as a streak across a large hull panel; at
20 mm it does not survive. It is now `Mat_Metal_Steel_Worn`, and the rust stays
on the splint plates, which are big enough to carry it.

## Export

`walking_staff_export.py` ships `Coll_Staff_Nomad` alone to
`Assets/Game/Art/Models/Weapons/WalkingStaff/walking_staff.fbx`.

`Weapons/` rather than `Props/` because the game reads it as the Nomad's melee
weapon and that is where someone will look for it. The library still files it
under `components/props`, where it belongs as a component.

The other three variations are deliberately **not** exported. An FBX nothing
references is not an asset, it is a file the next person has to work out whether
they may delete. Adding one is a single line in the export script's `TARGETS`.

## Triangle budget

568 tris for the hero, 1736 for all four. Ten-sided shafts: the prop is 35 mm
thick and is never seen further from the camera than a character's hand.
