# Wingsuit — build record

A membrane wing worn in the torso slot. Folded, it is a slim spar unit clipped to the expedition
rig's lash rail with two spar ends sticking out past the flanks; spread, two cambered cloth panels
run from the upper arms down to the hips.

Source: `wingsuit.py` → `wingsuit.blend` → `wingsuit_export.py` → `Assets/Game/Art/Models/Items/wingsuit.fbx`.
Consumed by `WingsuitBuilder` (Tools ▸ SpaceGame ▸ Items ▸ Build Wingsuit).

## Decomposition

Nine objects, nothing joined, no armature.

| Object | What it is | Material |
| --- | --- | --- |
| `Mesh_Wingsuit_Pack` | The spar case that sits on the rail, plus its canvas cover | `Mat_Metal_Steel_Dark`, `Mat_Fabric_Canvas_Sand` |
| `Mesh_Wingsuit_Clamp_L` / `_R` | The two jaws that grip the lash rail | `Mat_Metal_Steel_Worn`, `Mat_Fabric_Canvas_Faded` |
| `Mesh_Wingsuit_SparStub_L` / `_R` | Folded spar ends past the flanks — the whole of what a stowed suit looks like | `Mat_Metal_Steel_Worn`, `Mat_Metal_Steel_Dark` |
| `Mesh_Wingsuit_Membrane_L` / `_R` | The cloth panels. Reparented onto the wearer's upper-arm bones at runtime | `Mat_Fabric_Wing_Beige` |
| `Mesh_Wingsuit_Batten_L` / `_R` | Leading-edge spars the cloth is stretched over | `Mat_Metal_Steel_Worn` |

**Why the membranes are separate objects and not part of the pack.** They live on a different
transform at runtime: the pack stays on the spine while each membrane is parented to an arm bone, so
a raised arm carries its own wing. They are also the only parts that are hidden while the suit is
stowed, and the only ones that take the player's suit colour.

**Why two real mirrored meshes rather than one used twice.** A single mesh placed with a negative X
scale is what the gauntlet family already documented as a trap — it inverts winding, it fights the
humanoid rig's non-mirrored bone frames, and it makes every sign in the fit ambiguous. Two meshes
cost 176 triangles each. Each side therefore also gets its **own authored fit** on the prefab rather
than one fit mirrored in code.

**No armature.** A membrane deforms in `SpaceGame/ClothWind`, driven by airspeed rather than by
bones; the spars, clamps and case are rigid. Nothing here articulates.

## The membrane's own frame — load-bearing

Each membrane's **origin is the shoulder end of its leading edge**, because that is the point Unity
straps to the upper-arm bone. In its own object space:

| Axis | Runs | Extent |
| --- | --- | --- |
| X | outboard along the arm to the wrist | 0 → ±0.95 (sign is the side) |
| Y | aft to the free trailing edge | 0 (pinned) → −0.86 |
| Z | the panel's own camber and thickness | −0.006 → +0.089 |

**Y is the pin axis and that is what `ClothWind` needs.** The shader's `ClothFreedom` is a gradient
along one object-space axis from a pinned plane to a free one, so the leading edge (y = 0) is held
and the trailing edge (y = −0.86) is free to blow. `WingsuitBuilder` measures `_AnchorAxis`,
`_AnchorOrigin` and `_FreeLength` **off the mesh's own vertices on every run** rather than carrying
these numbers as constants — the nomad's cape shipped with measured-then-stale constants once, and a
re-export that changed its object space by 90x pinned every vertex at maximum displacement, which
read in game as the cloak wrapping round the front of the character.

Two things the builder measures because assuming them would be wrong, and both were:

- **The axis does not change.** `_exportlib.export` exports with `bake_space_transform=False`, so the
  frame change rides on the node transform and the mesh's own vertices stay in Blender's axes. The
  pin axis is **Y** in Unity's object space too, not the +Z the `(x, y, z) → (−x, z, −y)` conversion
  would suggest. A constant derived from the conversion would have pinned the wing across its span.
- **Object units are not metres.** A Blender FBX lands here at a lossy scale of **100**, so the chord
  measures 0.0086 in object space and 0.86 m in the world. `_AnchorOrigin` and `_FreeLength` want the
  former; `_MaxStretch` and `_WindStrength` are metres the shader converts itself. Mixing them up
  gave a 3 mm displacement ceiling — a wing pinned rigid, with a clean console.

## Shape

- **Chord falls off as a power curve** (`CHORD_FALLOFF` 1.35), not linearly: a straight taper reads
  as a triangle of cloth. A wingsuit is deep at the body, falls away fast, and runs out thin along
  the forearm.
- **Camber is zero at both ends** and deepest just inboard of half span — the shoulder end is pulled
  flat to the body and the wrist end is pinched onto the cuff.
- **The bow within a chord peaks about a third back**, which is where slack in a stretched panel
  actually collects, and the thickness pinches to a hem at the trailing edge rather than ending in a
  slab.
- Dimensions are a human's scaled by **1.7**, which is what this astronaut measures — the capsule
  stands 3 m and the hand was measured at 1.7x. They are not figures to recognise.

## Materials

**`Mat_Metal_Steel_Worn` is material index 0, and that is not alphabetical.** `Part.bevel` stamps
index 0 onto every face it creates, so whatever sits first in `MATS` is what all the softened edges
come out as. With the cloth first — the obvious order, since the wing is the point of the model —
the bevelled corners of both clamps and the case shipped as beige sailcloth, and the only way to
see it was to list the material slots on the imported prefab. Both bevelled parts are metal, so
steel is the correct default; the membranes carry one material and never bevel.


All five come from the palette; nothing was added. `Mat_Fabric_Wing_Beige` is the ornithopter's
sailcloth and describes this exactly ("sun-cured beige sailcloth stretched over wing blade frames"),
so a second near-identical cloth would have been the failure the palette guard exists to prevent.

In Unity the membrane's material is **replaced** by the builder with a `SpaceGame/ClothWind` material
named `WingsuitMembrane` — the palette material has no wind in it, and the name is what
`WingsuitRecolor` matches to paint the wing in the wearer's suit colour. The Blender material is
therefore the .blend's own look, not the shipped one.

## Verification

- `_zverify.py`: **0 clashing pairs**. The canvas cover sinks into the case lid rather than resting
  on it, the spar collar is sunk into its tube, and the batten stands proud of the leading edge —
  all three would otherwise be coincident surfaces.
- 1296 triangles total.
