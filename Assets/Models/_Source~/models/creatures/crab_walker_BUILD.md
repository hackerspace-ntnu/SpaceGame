# crab_walker — build record

Built 2026-08-11. Three variants from one script:

```
blender --background --python crab_walker.py -- --out crab_walker_6.blend --legs 6
blender --background --python crab_walker_export.py            # all three -> FBX
```

then **Tools ▸ Creatures ▸ Build Crab Walker Prefabs** in Unity
(`Assets/Editor/Creatures/CrabWalkerBuilder.cs`).

| | file |
| --- | --- |
| models | `Assets/Models/_Source~/models/creatures/crab_walker_{4,6,8}.blend` |
| generator | `Assets/Models/_Source~/models/creatures/crab_walker.py` |
| export | `Assets/Models/_Source~/models/creatures/crab_walker_export.py` |
| FBX | `Assets/Models/Creatures/Robotic/Crab/crab_walker_{4,6,8}.fbx` |
| prefabs | `Assets/Prefabs/agents/creatures/CrabWalker{4,6,8}.prefab` |
| runtime | `Assets/Scripts/Creatures/Crab/` + `Assets/Scripts/Creatures/CrabDriver.cs` |

## What it is

A wide, low, splayed walking machine that travels **across its own nose**. Four,
six or eight legs from one script, one rig convention and one Unity component;
two claw-arms carried forward.

## Reused from the library

| component | used for |
| --- | --- |
| `components/mechanical/walker_leg.blend` → `Coll_WalkerLeg_Heavy` | every leg, instanced at 0.42 |
| `components/mechanical/leg_shroud.blend` → Plate / Ribbed / Vented / Patched | leg armour, rotated down the rows so no two legs are dressed alike |
| `components/mechanical/claw_chela.blend` → `Coll_Chela_Heavy`, `Coll_Chela_Cutter` | the two claws — deliberately not a matched pair |
| `components/mechanical/vent_grille.blend` → Louvre, Scoop | shell fittings |

**Nothing new was added to the library and nothing was added to the palette.**
Every material is an existing palette entry; the claw and the leg were both
already there, which is most of why this model was a day's work rather than a
week's.

## New geometry, and why each piece is unique rather than a component

Four meshes, all specific to this hull's proportions. A component that only ever
fits one model is not a component:

- `Mesh_Crab_Carapace` — lofted across **X**, not along Y, so the widest section
  is amidships and the silhouette from the beam is the interesting one. Carries a
  coxa turret per leg, placed from the leg layout rather than authored.
- `Mesh_Crab_Underbelly` — ribs, sump and beam skirts. On a machine this low the
  underside is what a player standing beside it actually looks at.
- `Mesh_Crab_Prow` — eye stalks, sensor bar, bumper. The face.
- `Mesh_Crab_Stern` — vents, tanks, tow eye, so the back is not a blank wall.

Plus `Mesh_Claw_{P,N}_Limb`, the two arm segments, whose lengths are this
machine's and nobody else's.

## Why the legs point fore and aft

This is the one decision the whole model turns on.

Stride is `2 * RestFootRadius * sin(yawRange * 0.85)` — the chord of the arc the
foot sweeps when its coxa turns. So **the direction the machine covers ground
fastest in is the tangent of that arc**, perpendicular to the leg. The desert
crawler's legs stick out to port and starboard, its arcs sweep fore and aft, and
it walks along its nose. Turn that ninety degrees and you have a machine whose
best travel is sideways.

Everything else follows: the carapace is wider than it is deep because X is the
axis the feet are spread along; the gait's wave marches along X because that is
where the ground is going; and the claws go on the nose because it is the face
that is not doing the walking.

## Numbers the runtime reads back

| | value |
| --- | --- |
| leg component scale | 0.42 |
| hip plane | 1.75 m |
| foot reach from its own coxa axis | 2.05 m |
| rest extension | 73% of the linkage |
| stride at yawRange 28° | 1.65 m |
| foot span, travel axis | 8.40 m |
| shell | 6.80 × 3.90 m |
| sole-to-ground error | 0.0000 m |
| triangles | 130 k (4 legs) · 172 k (6) · 212 k (8) |

**Rest extension is the number that was got wrong first.** At leg scale 0.35 the
same splay put the foot at 83% of the linkage before it had taken a step, and the
worst reach came out at 1.14 travelling one way and 1.21 along the nose — a foot
visibly detached from its own leg. The fix was **longer legs, not a shorter
stride**: at 0.42 the rest pose is 73% and full stretch about 78%, which leaves
the solver a bend to work with on a slope. A crab has thick legs anyway.

## Rig

`CRAB_Rig`, one armature, exported with `add_leaf_bones=False`:

```
Root
├─ Coxa_<id> → Hip_<id> → Knee_<id> → Ankle_<id> → Foot_<id>     one per leg
└─ Arm_<P|N> → Shoulder_<id> → Elbow_<id> → Wrist_<id>           one per claw
```

Every joint carries a `*Pin*` mesh whose **longest axis is the hinge axis** —
that is how `WalkerRig.MeasureAxle` reads the axle and there is no fallback worth
relying on. The foot pin is lifted 0.16 m clear of the sole on purpose: only its
direction is ever read, but `LowestRendererPoint` takes the foot's length from the
lowest renderer under the ankle and skips nothing but `COL_`, so a pin bar centred
on the contact point hangs through the ground.

**The two naming schemes are not interchangeable.** `WalkerRig` assembles a LEG
by name across the whole armature, so a leg has to use the classic vocabulary. An
ARM is found by walking the hierarchy from its `Arm_` root, and its joints must
NOT be called `Arm_*` — `Arm_` is a root prefix, so every joint carrying it would
be claimed as an arm of its own and one claw would import as four. A shoulder is
not a coxa.

Collision is added on the Unity side, measured off the meshes, with each limb's
`COL_` box a **direct child of its joint** — nested under the mesh instead, the
recursive search reaches the knee's subtree before the thigh's own mesh and the
thigh measures the shin.

## Built ahead of the request

The request needed a crab. It got three, because the leg count was always going
to be a parameter and the marginal cost of the other two was a command line each.
`--legs 10` would work today; it needs a line in `CrabWalkerBuilder.Legs` and
nothing else.

## Judgement calls worth knowing about

- **Scale.** The machine is ~8.4 m across the feet and stands 1.75 m at the hip —
  bigger than a person, a quarter of the crawler. Nothing in the brief fixed a
  size; this one reads as a vehicle you walk around rather than one you walk
  under.
- **The claws are a mismatched pair** (Heavy one side, Cutter the other), which is
  deliberate — a salvager's machine, not a showroom one.
- **No mount station.** The crab is not rideable. Nothing in the brief asked for
  it and the shell has no deck; adding one is prefab wiring, not code.
