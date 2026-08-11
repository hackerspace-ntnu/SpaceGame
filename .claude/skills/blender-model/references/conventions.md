# Conventions

These exist so components built months apart still fit together. A component that disagrees with these is not reusable, which defeats the purpose of the library.

## Units and scale

- 1 Blender unit = 1 metre. Scene unit system: Metric, unit scale 1.0.
- Model at real-world size. A door is ~2.1 m tall, a crate ~0.6 m, a coffee mug ~0.1 m.
- Apply all transforms before saving (`Object > Apply > All Transforms`). Object scale should read 1.0, 1.0, 1.0.
- Non-uniform scale left unapplied breaks normals, bevels, and physics downstream.

## Grid and modularity

- Modular components snap to **0.25 m**.
- Large structural pieces (wall sections, floor plates, roof spans) snap to **1.0 m**.
- A component intended to tile must have its bounding footprint land exactly on the grid — 0.999 m is a visible seam.

## Orientation

- **+Z up**, **−Y forward**. A model faces −Y.
- Consistent forward matters for arraying, for mirroring, and for anything that will later be instanced along a path.

## Origins

Origin placement is the single most consequential choice for reusability.

- Put the origin at the **logical connection or pivot point**, not the geometric centre and not the world origin.
- A hinge: origin on the rotation axis.
- A wall panel: origin at the bottom-left corner where it meets its neighbour.
- A bolt: origin at the base of the shaft, where it enters the surface.
- A floor tile: origin at a corner, so tiling is integer translation.

If someone has to nudge your component into place by hand every time they use it, the origin is wrong.

## Naming

Everything named. `Cube.003` in a finished file is a defect.

Format: `Type_Descriptor_Variant`

| Prefix | Applies to |
|---|---|
| `Mesh_` | mesh objects |
| `Coll_` | collections |
| `Arm_` | armature objects |
| `Bone_` | bones |
| `Empty_` | empties, sockets, helpers |
| `Mat_` | materials (see `materials.md`) |

Examples: `Mesh_CrateBody_Small`, `Coll_Hinge_Large`, `Arm_DoorAssembly`, `Bone_LidPivot`, `Empty_Socket_HandleMount`.

Descriptors are PascalCase, meaningful, and describe the thing rather than its history. `Mesh_PanelFront` — not `Mesh_PanelFinal`, `Mesh_PanelNew`, or `Mesh_Panel2`.

## File organization

**Components** — `models/components/<category>/<component_name>.blend`

One component per file. Variations of that same component live in the same file, each in its own top-level collection:

```
crate_panel.blend
├── Coll_CratePanel_Plain
├── Coll_CratePanel_Slatted
└── Coll_CratePanel_Damaged
```

Variations are versions of one thing. A crate panel and a crate lid are two things — two files.

Build several variations per component as a matter of course, not only when asked — see the variation section in `SKILL.md`. They must differ in silhouette, structure, or condition; naming them `_A`, `_B`, `_C` is a sign they do not differ enough to deserve separate collections. Name them after what makes them different: `Coll_Tent_Ridge`, `Coll_Tent_LeanTo`, `Coll_Tent_Collapsed`.

**Models** — `models/models/<category>/<subcategory>/<model_name>.blend`

Nest categories as deeply as the content warrants. Prefer placing a model in an existing category over inventing a new one; create a category only when nothing existing is a reasonable home.

**Scripts** — saved beside their output, same basename: `crate_panel.blend` + `crate_panel.py`.

Remember these are historical record only. See `scripting.md`.

## Collections inside a model file

```
Coll_<ModelName>
├── Coll_Components      # linked/appended library components
├── Coll_Unique          # geometry specific to this model
└── Coll_Rig             # armature and helpers
```

This makes it obvious at a glance which parts came from the library and which are one-offs — and therefore which parts a later reader could pull out into components.

## Modifiers

- Leave non-destructive modifiers live (Bevel, Subdivision, Array, Mirror) unless the component needs baked geometry.
- Name modifiers meaningfully when a file has several.
- If a modifier is applied, note it in the generation script so a reader knows the mesh is baked.
