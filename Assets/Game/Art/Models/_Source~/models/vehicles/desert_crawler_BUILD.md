# Desert Crawler — build record

A six-legged walking habitat, built 2026-08-10 from the reference image: two
converted container modules side by side with a lit vehicle bay in the slot
between them, a chassis slung under them on six mech legs, a mast gantry
bridging the module roofs, and flags above that.

This is a record of decisions, not a proposal.

---

## What it is

| | |
|---|---|
| envelope | 21.54 × 19.03 × 27.28 m |
| foot span | 17.20 m across, 14.56 m fore-aft |
| hip height at rest | 6.30 m |
| triangles | 289,370 across 101 renderers on **40 unique meshes** |
| materials | 20, all from the shared palette |
| rig | `CRAWLER_Rig`, 35 bones |

Authoring frame is the library convention — **−Y forward, +X starboard, +Z up**
— unlike `ship_rv`, which had to keep its legacy +X-forward frame. That matters
twice here: Blender's default FBX axis conversion puts −Y on Unity's +Z, and
`SpiderWalkerLocomotion` sorts legs into their gait order by `HomeLocal.x`
(sides) and `.z` (fore/aft) in Unity space. Authoring on the convention makes
both land correctly with **no yaw correction anywhere** — `DesertCrawlerBuilder`
has no equivalent of `ShipRVBuilder.ModelYaw`.

---

## The rest pose is load-bearing, not decoration

The single most important decision in this model, and the one most likely to be
undone by accident later.

`SpiderWalkerLocomotion` derives stride from how far each foot sits from **its
own coxa yaw axis**:

    strideLength = 2 * RestFootRadius * sin(yawRange * 0.85)

The reusable legs are modelled standing straight down, foot directly under hip —
`RestFootRadius` ≈ 0. Left like that the machine's stride is a few centimetres
however the gait is tuned, and no amount of `stepDuration` fixes it. So the legs
are **posed** in the assembly: each foot is planted 4.30 m outboard of its hip
and the linkage is bent by a two-link IK solve to reach it.

Measured on the built model, by `WalkerRig` at runtime:

| | |
|---|---|
| upper / lower / foot | 3.87 / 3.49 / 1.45 m |
| MaxReach | 8.55 m |
| RestFootRadius | 4.27 m (design target 4.30) |
| rest reach fraction | **0.89** — bent, with travel left in both directions |
| sole-to-ground error | **0.0000 m** on all six |
| stride / MaxSpeed | 4.81 m / 4.78 m/s |

`HIP_Z` and `FOOT_REACH` in `desert_crawler.py` trade against each other: raising
the hip or reaching further straightens the leg toward 1.0 and it stops being
able to step. 6.30 / 4.30 puts it at 0.89. The build script refuses outright if
the pair asks for more than the linkage has.

This is the same class of bug as the ostrich's foothold clamp — see
`[[project-ostrich-locomotion]]`.

---

## Decomposition

### Reused from the library, unchanged

`structural/deck_plate` (Grate, Worn, Hatch) · `mechanical/pipe_run` (Straight,
Elbow, CableBundle, Duct) · `mechanical/vent_grille` (Louvre, Scoop) ·
`props/light_fixture` (Strip, Clamp) · `props/wall_locker` (Dented, OpenShelf).

Everything ship_rv built for its own interior turned out to be exactly what the
crawler's service band and vehicle bay needed. That is the library working.

### The legs — promoted, not rebuilt

`components/mechanical/walker_leg.blend` is **new as a component but not as
art**. The four legs were hand-built in
`Assets/Game/Prefabs/agents/vehicle/walker_legs.blend`; that file is read and never
written. The component adds three things:

1. **One mesh per limb segment.** The source is 60–80 loose parts per leg hung
   off empties. Six legs would have been ~400 transforms per machine, paid every
   frame, for parts that never move relative to their own joint. Baked to
   Upper / Lower / Foot: 3 objects, ~13.3 k triangles per leg.
2. **Palette materials.** The source carries `LEG_Hull` / `LEG_DarkMetal` /
   `LEG_Piston` / `LEG_Accent`. Remapped on the way in — `LEG_Hull` is what
   `Mat_Paint_Hull_Bleached` was added to the palette for.
3. **Origins on the joints**, so a segment parents straight to a bone.

Variations `Heavy` / `Compact` / `Raised` / `Long` differ in real linkage
geometry (reach 8.81 / 8.22 / 10.69 / 8.51 m). The crawler uses **Heavy on all
six** — deliberately. Mixed leg lengths on one machine are a believable scavenger
look but they give the gait six different stride budgets, and the variation the
eye actually wants is in the armour, which is where it went instead.

### New components (5 files, 22 variations)

| Component | Variations | Why separate |
|---|---|---|
| `mechanical/leg_shroud` | Plate, Ribbed, Patched, Vented, Stub **(ahead)** | The slab armour on the outer thigh. Largest unbroken surface on the silhouette and where paint, stencils and damage live — a bare linkage reads as a linkage. |
| `mechanical/road_wheel` | Twin, Single, Hub **(ahead)**, Flat **(ahead)** | A legged hauler still carries wheels; they say *vehicle* in a way pipework cannot. |
| `structural/cabin_module` | Habitat, Cargo **(ahead)**, Workshop, Comms **(ahead)** | The domestic half. Each has its own roofline — at the distance a walking machine is seen, the roof is what distinguishes them. |
| `structural/mast_rig` | Flag, Pennant, Antenna, Windvane **(ahead)** | Breaks the boxy top. The flag is the only soft thing on the model and reads at a kilometre when nothing else does. |
| `structural/handrail` | Straight, Corner **(ahead)**, Gate, Ladder, Stair **(ahead)** | Sells scale. A box on legs could be four metres tall or forty; a 1.05 m rail on its roof fixes it against a human body. |
| `props/floodlight_bank` | Quad, Twin, Single, Sweep **(ahead)** | Distinct from `props/light_fixture`, which is interior fittings seen from two metres. These are weatherproof floods seen from fifty. |

### Unique to this model

Chassis (with the six coxa turrets), deck, vehicle bay, mast gantry, prow, and
the service band. All specific to this machine's proportions. The gantry in
particular is *not* a component: its span is set by how far apart these two
modules sit, and a component that only ever fits one model is not a component.

---

## Palette

Five materials added, all of which the duplicate guard cleared:

| Material | Hex | For |
|---|---|---|
| `Mat_Paint_Hull_Bleached` | `#AAA499` | The signature sun-bleached olive-white. Body panels, shrouds, modules, and the legs' own paint. |
| `Mat_Paint_Roof_Green` | `#6E7A5E` | Roof caps and banded accents — the older paint layer under the topcoat. |
| `Mat_Paint_Warn_Red` | `#8E2B22` | Matte hazard roundels. The non-glowing counterpart to `Mat_Emissive_Red_Warn`. |
| `Mat_Paint_Olive_Deep` | `#3F4A3A` | Shadow panels; keeps large bleached surfaces from flattening out. |
| `Mat_Fabric_Flag_Bleached` | `#D8D2C2` | Flags. Much lighter than `Canvas_Faded`, which is dirty webbing. |

`Roof_Green` tripped the near-duplicate warning against `Canvas_Faded` (ΔE 10.7)
and was kept: painted metal against fabric, and both appear on this model.

---

## Unity

    desert_crawler.py  →  desert_crawler.blend  →  desert_crawler_export.py
      →  Assets/Game/Art/Models/Vehicles/Crawler/desert_crawler.fbx
      →  Tools ▸ Vehicles ▸ Build Desert Crawler Prefab
      →  Assets/Game/Prefabs/agents/vehicle/DesertCrawler.prefab

**The export keeps the armature, which is the opposite of `ship_rv_export.py`.**
`ShipRVBuilder` finds parts by name and reparents them into hinges it makes
itself, so a rig in that FBX is dead weight. Here `WalkerRig.Build` walks the
live bone hierarchy at every `Initialise`, looking for `Coxa_/Hip_/Knee_/Ankle_/
Foot_<id>` chains and taking each joint's hinge axis from the `*Pin*` mesh
parented to it. Strip the armature and there is no walker.

Round-tripped through the FBX and re-inspected: 35 bones, six chains, exactly one
pin per joint, no leaf bones, all 20 materials, identical envelope and triangle
count. Zero problems.

### Three things that had to be got right, and are easy to undo

**Leaf bones off.** Blender otherwise appends a `<bone>_end` child to every chain
tip. `MeasureAxle` takes the *first* child whose name contains "Pin"; a leaf bone
arriving ahead of the real pin is a silent wrong axis, not an error.

**The foot pin is lifted 0.42 m off the sole.** Only its direction is ever read,
but `LowestRendererPoint` takes the foot's length from the lowest renderer under
the ankle and skips nothing except `COL_`. A pin bar centred on the contact point
hangs through the ground and the machine stands that much too high.

**`COL_` boxes hang directly off the joints, not off the meshes.**
`SpiderWalkerLocomotion.RadiusOf` takes each segment's capsule radius from the
first `COL_`-prefixed box under the joint and searches *recursively* — so a box
one level down under the mesh is still found, but the depth-first walk reaches
the knee's subtree before the thigh's own mesh, and the thigh ends up measuring
the shin. Fixing the nesting took worst-case reach from **5.88 → 1.38** and
unreachable frames from 572/600 → 15/600. `rig_walker` carries its
`COL_Hip_*` / `COL_Knee_*` / `COL_Ankle_*` as direct joint children; this matches.

### The prefab origin sits on the hip plane

`DropModelOntoHips` sinks the model inside the prefab so the root lands on the
hip plane rather than on the soles. `SpiderWalkerLocomotion` takes ride height as
`body.y - averageFootY`; with the root on the ground that difference is zero, the
hull is pinned at foot level, and all six legs are asked to reach the ground from
there — measured at 5.9× their own reach. Derived from the rig at build time, so
re-proportioning the model re-derives it.

---

## Known: it does not walk yet, and that is not this model

Verified working: the rig measures correctly (numbers above), the rest pose is
exact, and with the hull at its designed height the legs report
`worstReach 0.89` and **zero** unreachable legs.

Verified not working: no leg ever swings. At a perfect rest pose the foothold
search strands 3 of 6 legs on the first frame; `Gait.cs:71` sets
`IsBlocked = stranded > 0` and `Api.cs:66` then forces `commandedSpeed = 0`. That
is a latch with no way out — speed 0 means `gait.Advance` gets 0, the clock never
turns, no phase slice ever opens, and the stranded legs stay stranded.

**`rig_walker` fails identically** under the same test (blocked 300/300,
`maxStranded=1`, no swings), so this is not specific to the crawler. It sits in
`SpiderWalkerLocomotion`'s in-flight rewrite — the component grew `IsBlocked`,
`footholdSearchSpread`, `maxFootholdSlope`, `minFootholdFlatness`,
`clearanceInflation` and adaptive ride height, and split into six partial files,
during this build. The first suspect is one of the new foothold-validity checks
rejecting even a flat plane.

Nothing in the model or the prefab needs to change for this; when the gait steps
again, the crawler steps with it.

---

## Judgement calls worth knowing about

**289 k triangles**, against ship_rv's 102 k. The legs are 80 k of that and are
non-negotiable — they are the reused art. The rest: shrouds 31 k, chassis 16 k,
wheels 30 k, modules 22 k, masts 16 k. Already trimmed once (gantry decking 8→4,
pipe runs 7→4, vents 6→4, rails 8→5) for −29 k. The biggest remaining levers are
the four road wheels (30 k) and the chassis' six coxa turrets.

Mitigated by instancing: those 101 renderers share only **40 mesh datablocks**,
so six legs cost one leg's memory and Unity batches them.

**All six legs are the same variation** — see above. The variation is in the
dressing: four different shrouds, three wheel fits (Twin / Single / none), so no
two legs are identical while the gait sees one geometry.

**No `AgentController` or `WanderModule`**, which `rig_walker` carries. A habitat
that wanders off on its own AI seemed like the wrong default for a vehicle people
live in; both are a one-component addition if wanted.

**The bay is a garage, not a walkable interior.** It is lined, lit, floored and
fitted with lockers, and it has a collision floor and a `MountStation` at its
mouth — but there is no modelled room behind it. The reference image shows a
vehicle parked in that slot, not a corridor.

**The emissive materials do not emit.** All four `Mat_Emissive_*` in the palette
carry base colour only — `palette.py` supports `--emission` but they were added
without it, before this build. So the bay's light strips and the floodlight
lenses read as flat pale surfaces in a Blender render. Left alone deliberately:
fixing it edits shared palette entries that `ship_rv` also uses, and Unity
remaps materials on import anyway.
